using JassSharp.Runtime;
using JassSharp.Syntax;

namespace JassSharp;

public sealed record Diagnostic(int Line, int Col, string Severity, string Message, string? File = null);

public static class Checker
{
    public static List<Diagnostic> Check(NsUnit unit, string? file = null)
    {
        var diags = new List<Diagnostic>();
        var classes = unit.Classes.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var fns = unit.Functions.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        var consts = unit.Consts.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var fields = unit.Fields.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var cls in unit.Classes)
        {
            if (cls.Ctor is not null) Walk(cls.Ctor.Body, cls.Ctor.Params, classes, fns, consts, fields, diags, file);
            foreach (var m in cls.Methods) Walk(m.Body, m.Params, classes, fns, consts, fields, diags, file);
        }
        foreach (var fn in unit.Functions) Walk(fn.Body, fn.Params, classes, fns, consts, fields, diags, file);
        return diags;
    }

    private static bool Known(string name, HashSet<string> fns, HashSet<string> consts, HashSet<string> classes, HashSet<string> fields) =>
        fns.Contains(name) || consts.Contains(name) || classes.Contains(name) || fields.Contains(name)
        || NativeCatalog.Natives.Contains(name) || NativeCatalog.Constants.Contains(name)
        || name.StartsWith("BJ", StringComparison.Ordinal);

    private static string? InferType(Syntax.Expr e, Dictionary<string, string> types)
    {
        return e switch
        {
            NameExpr n when types.TryGetValue(n.Name, out var t) => t,
            CallExpr c when c.Callee is NameExpr id && NativeSigs.Map.TryGetValue(id.Name, out var sig) => sig.ReturnType,
            NumExpr n => n.Text.Contains('.') || n.Text.Contains('e') || n.Text.Contains('E') ? "float" : "int",
            BoolExpr => "bool",
            StrExpr => "string",
            RawExpr => "int",
            _ => null
        };
    }

    private static void Walk(List<Stmt> body, List<ParamDecl> ps, HashSet<string> classes, HashSet<string> fns, HashSet<string> consts, HashSet<string> fields, List<Diagnostic> diags, string? file)
    {
        var locals = ps.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var types = ps.ToDictionary(p => p.Name, p => p.Type, StringComparer.Ordinal);

        void Stmt(Stmt s)
        {
            switch (s)
            {
                case VarStmt v:
                    locals.Add(v.Name);
                    if (v.Init is not null)
                    {
                        Expr(v.Init);
                        var t = InferType(v.Init, types);
                        if (t is not null) types[v.Name] = t;
                    }
                    break;
                case AssignStmt a:
                    Expr(a.Target); Expr(a.Value);
                    break;
                case ExprStmt e: Expr(e.Expr); break;
                case ReturnStmt r when r.Expr is not null: Expr(r.Expr); break;
                case IfStmt i:
                    Expr(i.Cond);
                    foreach (var t in i.Then) Stmt(t);
                    if (i.Else is not null) foreach (var t in i.Else) Stmt(t);
                    break;
                case WhileStmt w:
                    Expr(w.Cond);
                    foreach (var t in w.Body) Stmt(t);
                    break;
                case ForStmt f:
                    if (f.Init is not null) Stmt(f.Init);
                    if (f.Cond is not null) Expr(f.Cond);
                    if (f.Step is not null) Stmt(f.Step);
                    foreach (var t in f.Body) Stmt(t);
                    break;
            }
        }

        void Expr(Syntax.Expr e)
        {
            switch (e)
            {
                case CallExpr c when c.Callee is NameExpr id:
                    if (!Known(id.Name, fns, consts, classes, fields))
                        diags.Add(new Diagnostic(id.Line, id.Col, "warning", $"неизвестный вызов {id.Name}", file));
                    else if (NativeSigs.Map.TryGetValue(id.Name, out var sig))
                    {
                        if (c.Args.Count != sig.Params.Length)
                            diags.Add(new Diagnostic(id.Line, id.Col, "error", $"{id.Name}: ожидалось {sig.Params.Length} аргументов, получено {c.Args.Count}", file));
                        else
                        {
                            for (var i = 0; i < c.Args.Count; i++)
                            {
                                var arg = c.Args[i];
                                var expected = sig.Params[i];
                                var actual = InferType(arg, types);
                                if (actual is not null && actual != expected && !IsCompatible(actual, expected))
                                    diags.Add(new Diagnostic(arg.Line, arg.Col, "error", $"{id.Name}: аргумент {i + 1} ожидался {expected}, получен {actual}", file));
                            }
                        }
                    }
                    foreach (var a in c.Args) Expr(a);
                    break;
                case NameExpr n:
                    if (!locals.Contains(n.Name) && !Known(n.Name, fns, consts, classes, fields))
                        diags.Add(new Diagnostic(n.Line, n.Col, "warning", $"неизвестное имя {n.Name}", file));
                    break;
                case NewExpr n:
                    if (!classes.Contains(n.Type) && !NativeTable.Constructors.ContainsKey(n.Type) && !NativeCatalog.Types.Contains(n.Type))
                        diags.Add(new Diagnostic(n.Line, n.Col, "warning", $"неизвестный тип {n.Type}", file));
                    foreach (var a in n.Args) Expr(a);
                    break;
                case MemberExpr m: Expr(m.Object); break;
                case IndexExpr ix: Expr(ix.Object); Expr(ix.Index); break;
                case BinaryExpr b: Expr(b.Left); Expr(b.Right); break;
                case UnaryExpr u: Expr(u.Inner); break;
                case CallExpr c:
                    Expr(c.Callee);
                    foreach (var a in c.Args) Expr(a);
                    break;
            }
        }
        foreach (var s in body) Stmt(s);
    }

    private static bool IsCompatible(string actual, string expected)
    {
        if (actual == expected) return true;
        if (expected == "int" && actual == "float") return true;
        if (expected == "float" && actual == "int") return true;
        return false;
    }
}

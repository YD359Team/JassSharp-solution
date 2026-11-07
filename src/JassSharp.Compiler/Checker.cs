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
        foreach (var cls in unit.Classes)
        {
            if (cls.Ctor is not null) Walk(cls.Ctor.Body, cls.Ctor.Params, classes, fns, consts, diags, file);
            foreach (var m in cls.Methods) Walk(m.Body, m.Params, classes, fns, consts, diags, file);
        }
        foreach (var fn in unit.Functions) Walk(fn.Body, fn.Params, classes, fns, consts, diags, file);
        return diags;
    }

    private static bool Known(string name, HashSet<string> fns, HashSet<string> consts, HashSet<string> classes) =>
        fns.Contains(name) || consts.Contains(name) || classes.Contains(name)
        || NativeCatalog.Natives.Contains(name) || NativeCatalog.Constants.Contains(name)
        || name.StartsWith("BJ", StringComparison.Ordinal); // blizzard.j wrappers

    private static void Walk(List<Stmt> body, List<ParamDecl> ps, HashSet<string> classes, HashSet<string> fns, HashSet<string> consts, List<Diagnostic> diags, string? file)
    {
        var locals = ps.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        void Stmt(Stmt s)
        {
            switch (s)
            {
                case VarStmt v:
                    locals.Add(v.Name);
                    if (v.Init is not null) Expr(v.Init);
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
            }
        }
        void Expr(Syntax.Expr e)
        {
            switch (e)
            {
                case CallExpr c when c.Callee is NameExpr id:
                    if (!Known(id.Name, fns, consts, classes))
                        diags.Add(new Diagnostic(id.Line, id.Col, "warning", $"неизвестный вызов {id.Name}", file));
                    foreach (var a in c.Args) Expr(a);
                    break;
                case NameExpr n:
                    if (!locals.Contains(n.Name) && !Known(n.Name, fns, consts, classes))
                        diags.Add(new Diagnostic(n.Line, n.Col, "warning", $"неизвестное имя {n.Name}", file));
                    break;
                case NewExpr n:
                    if (!classes.Contains(n.Type) && !NativeTable.Constructors.ContainsKey(n.Type) && !NativeCatalog.Types.Contains(n.Type))
                        diags.Add(new Diagnostic(n.Line, n.Col, "warning", $"неизвестный тип {n.Type}", file));
                    foreach (var a in n.Args) Expr(a);
                    break;
                case MemberExpr m: Expr(m.Object); break;
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
}

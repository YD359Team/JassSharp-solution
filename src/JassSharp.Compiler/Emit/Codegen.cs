using JassSharp.Runtime;
using JassSharp.Syntax;

namespace JassSharp.Emit;

public sealed class Codegen
{
    private readonly bool _lua;
    private readonly HashSet<string> _classes;
    private readonly List<string> _lines = [];
    private int _indent;
    private Dictionary<string, string> _locals = new(StringComparer.Ordinal);
    private string? _className;

    public Codegen(bool lua, IEnumerable<string> classes)
    {
        _lua = lua;
        _classes = new HashSet<string>(classes, StringComparer.Ordinal);
    }

    public string Emit(NsUnit unit)
    {
        var hasInit = unit.Functions.Any(f => f.Name == "OnInit");
        if (_lua)
        {
            Line($"-- {unit.Name}  (JASS# → Lua)");
            if (unit.Usings.Count > 0)
                Line("-- usings: " + string.Join(", ", unit.Usings));
            Line("if not BJDebugMsg then function BJDebugMsg(s) print(s) end end");
            Line("");
            foreach (var f in unit.Fields)
                Line(f.IsArray ? $"{f.Name} = {{}}" : $"{f.Name} = nil");
            if (unit.Fields.Count > 0) Line("");
            foreach (var c in unit.Consts)
                Line($"local {c.Name} = {EmitExpr(c.Value)}");
            if (unit.Consts.Count > 0) Line("");
            foreach (var cls in unit.Classes) EmitClassLua(cls);
            foreach (var fn in unit.Functions) EmitFnLua(fn);
            EmitLuaEntry(hasInit);
        }
        else
        {
            Line($"library {unit.Name}{(hasInit ? " initializer OnInit" : "")}");
            if (unit.Consts.Count > 0 || unit.Fields.Count > 0)
            {
                Line("globals");
                _indent = 1;
                foreach (var c in unit.Consts)
                    Line($"constant {MapType(c.Type)} {c.Name} = {EmitExpr(c.Value)}");
                foreach (var f in unit.Fields)
                    Line($"{MapType(f.Type)} array {f.Name}");
                _indent = 0;
                Line("endglobals");
                Line("");
            }
            foreach (var cls in unit.Classes) EmitClassVj(cls);
            foreach (var fn in unit.Functions) EmitFnVj(fn, method: false);
            Line("endlibrary");
        }
        return string.Join("\n", _lines) + "\n";
    }

    private void EmitLuaEntry(bool hasInit)
    {
        Line("function config()");
        Line("end");
        Line("");
        Line("function main()");
        Line("    InitBlizzard()");
        if (hasInit)
        {
            Line("    local ok, err = pcall(OnInit)");
            Line("    if not ok then");
            Line("        print(\"JASS# OnInit error: \" .. tostring(err))");
            Line("        BJDebugMsg(\"JASS# OnInit error: \" .. tostring(err))");
            Line("    end");
        }
        Line("end");
    }

    private void EmitClassVj(ClassDecl cls)
    {
        _className = cls.Name;
        Line($"struct {cls.Name}");
        _indent++;
        foreach (var f in cls.Fields) Line($"{MapType(f.Type)} {f.Name}");
        if (cls.Ctor is not null)
        {
            _locals = cls.Ctor.Params.ToDictionary(p => p.Name, p => p.Type, StringComparer.Ordinal);
            Line($"static method create takes {Params(cls.Ctor)} returns {cls.Name}");
            _indent++;
            Line($"local {cls.Name} this = {cls.Name}.allocate()");
            EmitStmts(cls.Ctor.Body.Where(s => s is not ReturnStmt).ToList());
            Line("return this");
            _indent--;
            Line("endmethod");
        }
        foreach (var m in cls.Methods) EmitFnVj(m, method: true);
        _indent--;
        Line("endstruct");
        Line("");
        _className = null;
    }

    private void EmitClassLua(ClassDecl cls)
    {
        _className = cls.Name;
        Line($"local {cls.Name} = {{}}");
        Line($"{cls.Name}.__index = {cls.Name}");
        if (cls.Ctor is not null)
        {
            _locals = cls.Ctor.Params.ToDictionary(p => p.Name, p => p.Type, StringComparer.Ordinal);
            Line($"function {cls.Name}.new({string.Join(", ", cls.Ctor.Params.Select(p => p.Name))})");
            _indent++;
            Line($"local self = setmetatable({{}}, {cls.Name})");
            EmitStmts(cls.Ctor.Body.Where(s => s is not ReturnStmt).ToList());
            Line("return self");
            _indent--;
            Line("end");
        }
        foreach (var m in cls.Methods)
        {
            _locals = m.Params.ToDictionary(p => p.Name, p => p.Type, StringComparer.Ordinal);
            Line($"function {cls.Name}:{m.Name}({string.Join(", ", m.Params.Select(p => p.Name))})");
            _indent++;
            EmitStmts(m.Body);
            _indent--;
            Line("end");
        }
        Line("");
        _className = null;
    }

    private void EmitFnVj(FnDecl fn, bool method)
    {
        _locals = fn.Params.ToDictionary(p => p.Name, p => p.Type, StringComparer.Ordinal);
        var ps = Params(fn);
        if (method) Line($"method {fn.Name} takes {ps} returns {MapType(fn.ReturnType)}");
        else Line($"function {fn.Name} takes {ps} returns {MapType(fn.ReturnType)}");
        _indent++;
        EmitStmts(fn.Body);
        _indent--;
        Line(method ? "endmethod" : "endfunction");
    }

    private void EmitFnLua(FnDecl fn)
    {
        _locals = fn.Params.ToDictionary(p => p.Name, p => p.Type, StringComparer.Ordinal);
        Line($"function {fn.Name}({string.Join(", ", fn.Params.Select(p => p.Name))})");
        _indent++;
        EmitStmts(fn.Body);
        _indent--;
        Line("end");
        Line("");
    }

    private void EmitStmts(List<Stmt> ss)
    {
        foreach (var s in ss)
        {
            switch (s)
            {
                case VarStmt v:
                    _locals[v.Name] = v.Type;
                    var init = v.Init is null ? null : EmitExpr(v.Init);
                    if (_lua) Line(init is null ? $"local {v.Name}" : $"local {v.Name} = {init}");
                    else Line(init is null ? $"local {MapType(v.Type)} {v.Name}" : $"local {MapType(v.Type)} {v.Name} = {init}");
                    break;
                case AssignStmt a:
                    EmitAssign(a.Target, a.Value);
                    break;
                case ExprStmt e:
                    if (!EmitChain(e.Expr))
                    {
                        var ex = EmitExpr(e.Expr);
                        Line(_lua ? ex : $"call {ex}");
                    }
                    break;
                case ReturnStmt r:
                    Line(r.Expr is null ? "return" : $"return {EmitExpr(r.Expr)}");
                    break;
                case IfStmt i:
                    Line($"if {EmitExpr(i.Cond)} then");
                    _indent++;
                    EmitStmts(i.Then);
                    _indent--;
                    if (i.Else is not null)
                    {
                        Line("else");
                        _indent++;
                        EmitStmts(i.Else);
                        _indent--;
                    }
                    Line(_lua ? "end" : "endif");
                    break;
                case WhileStmt w:
                    if (_lua)
                    {
                        Line($"while {EmitExpr(w.Cond)} do");
                        _indent++;
                        EmitStmts(w.Body);
                        _indent--;
                        Line("end");
                    }
                    else
                    {
                        Line("loop");
                        _indent++;
                        Line($"exitwhen not ({EmitExpr(w.Cond)})");
                        EmitStmts(w.Body);
                        _indent--;
                        Line("endloop");
                    }
                    break;
                case ForStmt f:
                    if (_lua)
                    {
                        if (f.Init is not null) EmitStmts([f.Init]);
                        Line($"while {EmitExpr(f.Cond ?? new BoolExpr(true))} do");
                        _indent++;
                        EmitStmts(f.Body);
                        if (f.Step is not null) EmitStmts([f.Step]);
                        _indent--;
                        Line("end");
                    }
                    else
                    {
                        Line("loop");
                        _indent++;
                        if (f.Cond is not null) Line($"exitwhen not ({EmitExpr(f.Cond)})");
                        EmitStmts(f.Body);
                        if (f.Step is not null) EmitStmts([f.Step]);
                        _indent--;
                        Line("endloop");
                    }
                    break;
            }
        }
    }

    private void EmitAssign(Expr target, Expr value)
    {
        if (target is MemberExpr m)
        {
            var rt = RecvType(m.Object);
            var prop = rt is null ? null : NativeTable.Find(rt, m.Name);
            if (prop?.Kind == MemberKind.GetSet && prop.Write is not null)
            {
                var call = Apply(prop.Write, EmitExpr(m.Object), [EmitExpr(value)]);
                Line(_lua ? call : $"call {call}");
                return;
            }
        }
        var left = EmitExpr(target);
        var v = EmitExpr(value);
        Line(_lua ? $"{left} = {v}" : $"set {left} = {v}");
    }

    private bool EmitChain(Expr e)
    {
        var parts = new List<(Expr Recv, string Name, List<Expr> Args)>();
        var cur = e;
        while (cur is CallExpr { Callee: MemberExpr mem } call)
        {
            parts.Insert(0, (mem.Object, mem.Name, call.Args));
            cur = mem.Object;
        }
        if (parts.Count == 0) return false;
        var rt = RecvType(parts[0].Recv);
        if (rt is null || NativeTable.Find(rt, parts[0].Name) is null) return false;
        var recv = EmitExpr(parts[0].Recv);
        foreach (var p in parts)
        {
            var prop = NativeTable.Find(rt, p.Name);
            if (prop?.Kind != MemberKind.Method) return false;
            var args = p.Args.Select((a, i) => CodeArg(p.Name, i, a)).ToList();
            var call = Apply(prop.Read, recv, args);
            Line(_lua ? call : $"call {call}");
        }
        return true;
    }

    // If either side of + is a string literal, treat the whole expression as concatenation.
    // This covers "text" + this.IsAI, "text" + someBool, etc.
    private bool IsStringish(Expr e) =>
        e is StrExpr ||
        (e is BinaryExpr b && b.Op == "+" && (IsStringish(b.Left) || IsStringish(b.Right)));

    private string EmitExpr(Expr e) => e switch
    {
        NumExpr n => n.Text,
        StrExpr s => $"\"{Esc(s.Text)}\"",
        RawExpr r => _lua ? Rawcode.Pack(r.Id).ToString() : $"'{r.Id}'",
        BoolExpr b => b.Value ? "true" : "false",
        NullExpr => "null",
        ThisExpr => _lua ? "self" : "this",
        NameExpr n => n.Name,
        UnaryExpr u when u.Op == "!" && !_lua => $"not ({EmitExpr(u.Inner)})",
        UnaryExpr u => $"{(u.Op == "!" ? "not " : "-")}{EmitExpr(u.Inner)}",
        BinaryExpr b when b.Op == "+" && _lua && IsStringish(b) => $"({EmitExpr(b.Left)} .. {EmitExpr(b.Right)})",
        BinaryExpr b => $"({EmitExpr(b.Left)} {Op(b.Op)} {EmitExpr(b.Right)})",
        IndexExpr ix => $"{EmitExpr(ix.Object)}[{EmitExpr(ix.Index)}]",
        NewExpr n when NativeTable.Constructors.TryGetValue(n.Type, out var ctor) =>
            $"{ctor}({string.Join(", ", n.Args.Select(EmitExpr))})",
        NewExpr n => _lua
            ? $"{n.Type}.new({string.Join(", ", n.Args.Select(EmitExpr))})"
            : $"{n.Type}.create({string.Join(", ", n.Args.Select(EmitExpr))})",
        MemberExpr m => EmitMember(m),
        CallExpr c => EmitCall(c),
        _ => "null",
    };

    private string EmitMember(MemberExpr m)
    {
        var rt = RecvType(m.Object);
        var prop = rt is null ? null : NativeTable.Find(rt, m.Name);
        var recv = EmitExpr(m.Object);
        if (prop?.Kind is MemberKind.GetSet or MemberKind.GetOnly)
            return Apply(prop.Read, recv, []);
        return $"{recv}.{m.Name}";
    }

    private string EmitCall(CallExpr c)
    {
        if (c.Callee is MemberExpr mem)
        {
            var rt = RecvType(mem.Object);
            var prop = rt is null ? null : NativeTable.Find(rt, mem.Name);
            var recv = EmitExpr(mem.Object);
            var args = c.Args.Select((a, i) => CodeArg(mem.Name, i, a)).ToList();
            if (prop?.Kind == MemberKind.Method) return Apply(prop.Read, recv, args);
            if (_lua)
                return $"{recv}:{mem.Name}({string.Join(", ", args)})";
            return $"{recv}.{mem.Name}({string.Join(", ", args)})";
        }
        if (c.Callee is NameExpr id)
            return $"{id.Name}({string.Join(", ", c.Args.Select((a, i) => CodeArg(id.Name, i, a)))})";
        return $"{EmitExpr(c.Callee)}({string.Join(", ", c.Args.Select(EmitExpr))})";
    }

    private string? RecvType(Expr obj) => obj switch
    {
        ThisExpr => _className,
        NameExpr n when _locals.TryGetValue(n.Name, out var t) => t,
        NewExpr n => n.Type,
        IndexExpr ix when RecvType(ix.Object) is { } t => t,
        CallExpr { Callee: MemberExpr mem } call when RecvType(mem.Object) is { } rt &&
            NativeTable.Find(rt, mem.Name) is { Kind: MemberKind.Method } => rt,
        _ => null,
    };

    private string CodeArg(string method, int index, Expr arg)
    {
        var codeSlot = method is "AddAction" or "AddCondition" || (method == "Start" && index == 2);
        if (codeSlot && arg is NameExpr n && !_lua) return $"function {n.Name}";
        return EmitExpr(arg);
    }

    private static string Apply(string tpl, string recv, List<string> args)
    {
        if (tpl.Contains('$') || tpl.Contains('#'))
        {
            var i = 0;
            var s = tpl.Replace("$", recv);
            var b = new System.Text.StringBuilder();
            foreach (var ch in s)
            {
                if (ch == '#') b.Append(i < args.Count ? args[i++] : "null");
                else b.Append(ch);
            }
            return b.ToString();
        }
        return $"{tpl}({string.Join(", ", new[] { recv }.Concat(args))})";
    }

    private string Op(string op) => op switch
    {
        "&&" => "and",
        "||" => "or",
        _ => op,
    };

    private static string MapType(string t) => t switch
    {
        "int" => "integer",
        "float" => "real",
        "bool" => "boolean",
        "void" => "nothing",
        _ => t,
    };

    private static string Params(FnDecl fn) =>
        fn.Params.Count == 0 ? "nothing" : string.Join(", ", fn.Params.Select(p => $"{MapType(p.Type)} {p.Name}"));

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private void Line(string s) => _lines.Add(new string(' ', _indent * 4) + s);
}

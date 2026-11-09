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
    private readonly Dictionary<string, FnDecl> _inlineMethods = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FnDecl> _methods = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EnumDecl> _enums = new(StringComparer.Ordinal);

    public Codegen(bool lua, IEnumerable<string> classes)
    {
        _lua = lua;
        _classes = new HashSet<string>(classes, StringComparer.Ordinal);
    }

    public string Emit(NsUnit unit)
    {
        _namespaceName = unit.Name;

        foreach (var cls in unit.Classes)
            foreach (var m in cls.Methods)
                _methods[cls.Name + "." + m.Name] = m;
        foreach (var en in unit.Enums)
            _enums[en.Name] = en;
        foreach (var fn in unit.Functions)
            _methods[fn.Name] = fn;

        // collect inline
        foreach (var kv in _methods)
            if (kv.Value.Attributes?.Contains("Inline") == true)
                _inlineMethods[kv.Key] = kv.Value;

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
            foreach (var en in unit.Enums) EmitEnum(en);
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
                foreach (var en in unit.Enums) EmitEnum(en);
                foreach (var f in unit.Fields)
                    Line(f.IsArray ? $"{MapType(f.Type)} array {f.Name}" : $"{MapType(f.Type)} {f.Name}");
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
            string initializerName = $"{_namespaceName}_OnInit";

            Line($"    local ok, err = pcall({initializerName})");
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
        Line($"{cls.Name} = {{}}");
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

    private void EmitEnum(EnumDecl en)
    {
        int next = 0;
        foreach (var m in en.Members)
        {
            string value;
            if (m.Value is NumExpr n && int.TryParse(n.Text, out var v))
            {
                value = n.Text;
                next = v + 1;
            }
            else if (m.Value is not null)
            {
                value = EmitExpr(m.Value);
                next++;
            }
            else
            {
                value = next.ToString();
                next++;
            }

            if (_lua)
                Line($"{en.Name}_{m.Name} = {value}");
            else
                Line($"constant integer {en.Name}_{m.Name} = {value}");
        }

        // ToString helper
        if (_lua)
        {
            Line($"function {en.Name}_ToString(v)");
            _indent++;
            foreach (var m in en.Members)
                Line($"if v == {en.Name}_{m.Name} then return \"{m.Name}\" end");
            Line("return \"?\"");
            _indent--;
            Line("end");
        }
        else
        {
            Line($"function {en.Name}_ToString takes integer v returns string");
            _indent++;
            foreach (var m in en.Members)
                Line($"if v == {en.Name}_{m.Name} then return \"{m.Name}\" endif");
            Line("return \"?\"");
            _indent--;
            Line("endfunction");
        }
        Line("");
    }

    private void EmitFnVj(FnDecl fn, bool method)
    {
        _locals = fn.Params.ToDictionary(p => p.Name, p => p.Type, StringComparer.Ordinal);
        var ps = Params(fn);
        if (method) Line($"method {fn.Name} takes {ps} returns {MapType(fn.ReturnType)}");
        else Line($"function {fn.Name} takes {ps} returns {MapType(fn.ReturnType)}");
        _indent++;
        EmitStmts(fn.Body, fn.Attributes?.Contains("Trace") == true);
        _indent--;
        Line(method ? "endmethod" : "endfunction");
    }

    private string? _namespaceName;

    private void EmitFnLua(FnDecl fn)
    {
        _locals = fn.Params.ToDictionary(
            p => p.Name,
            p => p.Type,
            StringComparer.Ordinal);

        string functionName = fn.Name == "OnInit"
            ? $"{_namespaceName}_OnInit"
            : fn.Name;

        Line($"function {functionName}({string.Join(", ", fn.Params.Select(p => p.Name))})");

        _indent++;
        EmitStmts(fn.Body, fn.Attributes?.Contains("Trace") == true);
        _indent--;

        Line("end");
        Line("");
    }

    private void EmitStmts(List<Stmt> ss, bool trace = false)
    {
        foreach (var s in ss)
        {
            if (trace && s is not CondStmt)
            {
                var loc = s switch
                {
                    VarStmt v => $"{v.Line}:{v.Col}",
                    AssignStmt a => $"{a.Target.Line}:{a.Target.Col}",
                    ExprStmt e => $"{e.Expr.Line}:{e.Expr.Col}",
                    ReturnStmt r => r.Expr is null ? "return" : $"{r.Expr.Line}:{r.Expr.Col}",
                    IfStmt i => $"{i.Cond.Line}:{i.Cond.Col}",
                    WhileStmt w => $"{w.Cond.Line}:{w.Cond.Col}",
                    ForStmt f => f.Init is VarStmt vs ? $"{vs.Line}:{vs.Col}" : "for",
                    _ => "?"
                };
                var msg = $"[trace] {loc}";
                Line(_lua ? $"BJDebugMsg(\"{Esc(msg)}\")" : $"call BJDebugMsg(\"{Esc(msg)}\")");
            }
            switch (s)
            {
                case CondStmt c:
                    var match = (c.Target.Equals("LUA", StringComparison.OrdinalIgnoreCase) && _lua) ||
                                (c.Target.Equals("VJASS", StringComparison.OrdinalIgnoreCase) && !_lua);
                    if (match)
                        EmitStmts(c.Then, trace);
                    else if (c.Else is not null)
                        EmitStmts(c.Else, trace);
                    break;
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
                    if (TryEmitInline(e.Expr))
                        break;
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
                    EmitStmts(i.Then, trace);
                    _indent--;
                    if (i.Else is not null)
                    {
                        Line("else");
                        _indent++;
                        EmitStmts(i.Else, trace);
                        _indent--;
                    }
                    Line(_lua ? "end" : "endif");
                    break;
                case WhileStmt w:
                    if (_lua)
                    {
                        Line($"while {EmitExpr(w.Cond)} do");
                        _indent++;
                        EmitStmts(w.Body, trace);
                        _indent--;
                        Line("end");
                    }
                    else
                    {
                        Line("loop");
                        _indent++;
                        Line($"exitwhen not ({EmitExpr(w.Cond)})");
                        EmitStmts(w.Body, trace);
                        _indent--;
                        Line("endloop");
                    }
                    break;
                case ForStmt f:
                    if (_lua)
                    {
                        if (f.Init is not null) EmitStmts([f.Init], trace);
                        Line($"while {EmitExpr(f.Cond ?? new BoolExpr(true))} do");
                        _indent++;
                        EmitStmts(f.Body, trace);
                        if (f.Step is not null) EmitStmts([f.Step], trace);
                        _indent--;
                        Line("end");
                    }
                    else
                    {
                        Line("loop");
                        _indent++;
                        if (f.Cond is not null) Line($"exitwhen not ({EmitExpr(f.Cond)})");
                        EmitStmts(f.Body, trace);
                        if (f.Step is not null) EmitStmts([f.Step], trace);
                        _indent--;
                        Line("endloop");
                    }
                    break;
            }
        }
    }

    private bool TryEmitInline(Expr e)
    {
        if (e is not CallExpr c) return false;

        FnDecl? meth = null;
        string? recvName = null;
        List<Expr> args = c.Args;

        if (c.Callee is NameExpr id && _inlineMethods.TryGetValue(id.Name, out meth))
        {
            // free function
        }
        else if (c.Callee is MemberExpr mem)
        {
            var key = (RecvType(mem.Object) ?? "") + "." + mem.Name;
            if (!_inlineMethods.TryGetValue(key, out meth)) return false;
            recvName = EmitExpr(mem.Object);
        }
        else return false;

        if (meth is null) return false;

        // save locals
        var saved = new Dictionary<string, string>(_locals, StringComparer.Ordinal);

        // bind params
        for (int i = 0; i < meth.Params.Count && i < args.Count; i++)
        {
            var p = meth.Params[i];
            var argExpr = EmitExpr(args[i]);
            // simple: introduce temp or just substitute by emitting as local
            Line(_lua ? $"local {p.Name} = {argExpr}" : $"local {MapType(p.Type)} {p.Name} = {argExpr}");
            _locals[p.Name] = p.Type;
        }

        // if method, bind self/this
        if (recvName is not null && _className is not null)
        {
            Line(_lua ? $"local self = {recvName}" : $"local {_className} this = {recvName}");
        }

        EmitStmts(meth.Body);

        // restore locals
        _locals = saved;
        return true;
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

    private bool IsStringish(Expr e) =>
        e is StrExpr ||
        (e is BinaryExpr b && b.Op == "+" && (IsStringish(b.Left) || IsStringish(b.Right)));

    private string Concat(Expr left, Expr right)
    {
        var l = ToStringExpr(left);
        var r = ToStringExpr(right);
        return $"({l} .. {r})";
    }

    private string ToStringExpr(Expr e)
    {
        var t = InferType(e);
        var emitted = EmitExpr(e);
        return t switch
        {
            "int" => _lua ? $"tostring({emitted})" : $"I2S({emitted})",
            "float" => _lua ? $"tostring({emitted})" : $"R2S({emitted})",
            "bool" => _lua ? $"tostring({emitted})" : $"B2S({emitted})",
            "string" => emitted,
            "unit" => $"GetUnitName({emitted})",
            "player" => $"GetPlayerName({emitted})",
            "item" => $"GetItemName({emitted})",
            _ => _lua ? $"tostring({emitted})" : $"I2S(0)"
        };
    }

    private string? InferType(Expr e) => e switch
    {
        NameExpr n when _locals.TryGetValue(n.Name, out var t) => t,
        ThisExpr => _className,
        NewExpr n => n.Type,
        NumExpr => "int",
        StrExpr => "string",
        BoolExpr => "bool",
        _ => null
    };

    private string EmitExpr(Expr e) => e switch
    {
        NumExpr n => n.Text,
        StrExpr s => $"\"{Esc(s.Text)}\"",
        RawExpr r => _lua ? Rawcode.Pack(r.Id).ToString() : $"'{r.Id}'",
        BoolExpr b => b.Value ? "true" : "false",
        NullExpr => _lua ? "nil" : "null",
        ThisExpr => _lua ? "self" : "this",
        NameExpr n => n.Name,

        UnaryExpr u when u.Op == "!" && !_lua =>
            $"not ({EmitExpr(u.Inner)})",

        UnaryExpr u =>
            $"{(u.Op == "!" ? "not " : "-")}{EmitExpr(u.Inner)}",

        BinaryExpr b when b.Op == "+" && _lua && IsStringish(b) =>
            Concat(b.Left, b.Right),

        BinaryExpr b =>
            $"({EmitExpr(b.Left)} {Op(b.Op)} {EmitExpr(b.Right)})",

        IndexExpr ix =>
            $"{EmitExpr(ix.Object)}[{EmitExpr(ix.Index)}]",

        NewExpr n when NativeTable.Constructors.TryGetValue(n.Type, out var ctor) =>
            $"{ctor}({string.Join(", ", n.Args.Select(EmitExpr))})",

        NewExpr n when _lua =>
            $"{n.Type}.new({string.Join(", ", n.Args.Select(EmitExpr))})",

        NewExpr n =>
            $"{n.Type}.create({string.Join(", ", n.Args.Select(EmitExpr))})",

        MemberExpr m => EmitMember(m),
        CallExpr c => EmitCall(c),

        _ => throw new NotSupportedException(
            $"Unsupported expression node: {e.GetType().Name}")
    };

    private string EmitMember(MemberExpr m)
    {
        // Enum.Member → Enum_Member
        if (m.Object is NameExpr ne && _enums.TryGetValue(ne.Name, out var en))
        {
            if (en.Members.Any(mem => mem.Name == m.Name))
                return $"{en.Name}_{m.Name}";
        }

        var rt = RecvType(m.Object);
        var prop = rt is null ? null : NativeTable.Find(rt, m.Name);
        var recv = EmitExpr(m.Object);
        if (prop?.Kind is MemberKind.GetSet or MemberKind.GetOnly)
            return Apply(prop.Read, recv, []);
        return $"{recv}.{m.Name}";
    }

    private string EmitCall(CallExpr c)
    {
        if (c.Callee is MemberExpr mem && mem.Name == "ToString" && c.Args.Count == 0)
        {
            // прямое Colors.Red.ToString() → "Red"
            if (mem.Object is MemberExpr inner && inner.Object is NameExpr enName &&
                _enums.TryGetValue(enName.Name, out var en) &&
                en.Members.Any(m => m.Name == inner.Name))
            {
                return $"\"{inner.Name}\"";
            }

            // переменная enum-типа: c.ToString() → Colors_ToString(c)
            var t = InferType(mem.Object);
            if (t is not null && _enums.ContainsKey(t))
                return $"{t}_ToString({EmitExpr(mem.Object)})";
        }
        if (c.Callee is NameExpr id && id.Name == "Print")
        {
            var parts = c.Args.Select(ToStringExpr).ToList();
            var msg = parts.Count == 0 ? "\"\"" : string.Join(" .. ", parts);
            return $"BJDebugMsg({msg})";
        }
        return EmitNormalCall(c);
    }

    private string EmitNormalCall(CallExpr c)
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

    private string Apply(string tpl, string recv, List<string> args)
    {
        if (tpl.Contains('$') || tpl.Contains('#'))
        {
            int i = 0;
            string s = tpl.Replace("$", recv);
            var b = new System.Text.StringBuilder();

            foreach (char ch in s)
            {
                if (ch == '#')
                {
                    b.Append(
                        i < args.Count
                            ? args[i++]
                            : (_lua ? "nil" : "null"));
                }
                else
                {
                    b.Append(ch);
                }
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

    private static string Esc(string s) =>
        s.Replace("\\", "\\\\")
         .Replace("\"", "\\\"")
         .Replace("\n", "\\n")
         .Replace("\r", "\\r")
         .Replace("\t", "\\t");

    private void Line(string s) => _lines.Add(new string(' ', _indent * 4) + s);
}
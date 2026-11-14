using JassSharp.Runtime;
using JassSharp.Syntax;

namespace JassSharp.Emit;

public sealed class Codegen
{
    private readonly bool _lua;
    private readonly bool _headless;
    private readonly HashSet<string> _classes;
    private List<string> _lines = [];
    private int _indent;
    private Dictionary<string, string> _locals = new(StringComparer.Ordinal);
    private string? _className;
    private bool _currentMethodIsStatic;
    private readonly Dictionary<string, FnDecl> _inlineMethods = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FnDecl> _methods = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<FnDecl>> _operatorMethods = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EnumDecl> _enums = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> _classFields = new(StringComparer.Ordinal);

    public Codegen(bool lua, IEnumerable<string> classes, bool headless = false)
    {
        _lua = lua;
        _headless = headless;
        _classes = new HashSet<string>(classes, StringComparer.Ordinal);
    }

    public string Emit(NsUnit unit, IEnumerable<NsUnit>? imports = null)
    {
        _namespaceName = unit.Name;

        if (imports is not null)
        {
            foreach (var imported in imports)
            {
                foreach (var cls in imported.Classes)
                {
                    _classes.Add(cls.Name);
                    _classFields[cls.Name] = cls.Fields.ToDictionary(field => field.Name, field => field.Type, StringComparer.Ordinal);
                    foreach (var method in cls.Methods)
                    {
                        if (method.Operator is { } op)
                            RegisterOperator(cls.Name, op, method);
                        else
                            _methods[cls.Name + "." + method.Name] = method;
                    }
                }
                foreach (var en in imported.Enums)
                    _enums[en.Name] = en;
                foreach (var fn in imported.Functions)
                    _methods[fn.Name] = fn;
            }
        }

        foreach (var cls in unit.Classes)
        {
            _classFields[cls.Name] = cls.Fields.ToDictionary(field => field.Name, field => field.Type, StringComparer.Ordinal);
            foreach (var m in cls.Methods)
            {
                if (m.Operator is { } op)
                    RegisterOperator(cls.Name, op, m);
                else
                    _methods[cls.Name + "." + m.Name] = m;
            }
        }
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
            if (!_headless)
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
            if (!_headless)
                EmitLuaEntry(hasInit);

            Line(@"
function __list_remove(t, v)
    for i = 1, #t do
        if t[i] == v then
            table.remove(t, i)
            return true
        end
    end
    return false
end
function __list_indexof(t, v)
    for i = 1, #t do
        if t[i] == v then return i - 1 end
    end
    return -1
end
");
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
            _currentMethodIsStatic = false;
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
            _currentMethodIsStatic = false;
            _locals = cls.Ctor.Params.ToDictionary(p => p.Name, p => p.Type, StringComparer.Ordinal);
            Line($"function {cls.Name}.new({string.Join(", ", cls.Ctor.Params.Select(p => p.Name))})");
            _indent++;
            Line($"local self = setmetatable({{}}, {cls.Name})");
            foreach (var field in cls.Fields.Where(field => field.IsArray))
                Line($"self.{field.Name} = {{}}");
            EmitStmts(cls.Ctor.Body.Where(s => s is not ReturnStmt).ToList());
            Line("return self");
            _indent--;
            Line("end");
        }
        foreach (var m in cls.Methods)
        {
            _currentMethodIsStatic = m.IsStatic;
            _locals = m.Params.ToDictionary(p => p.Name, p => p.Type, StringComparer.Ordinal);
            if (m.Operator is { } op)
            {
                Line($"function {cls.Name}.{OperatorFunctionName(m)}({string.Join(", ", m.Params.Select(p => p.Name))})");
            }
            else if (m.IsStatic)
            {
                Line($"function {cls.Name}.{m.Name}({string.Join(", ", m.Params.Select(p => p.Name))})");
            }
            else
            {
                Line($"function {cls.Name}:{m.Name}({string.Join(", ", m.Params.Select(p => p.Name))})");
            }
            _indent++;
            EmitStmts(m.Body);
            _indent--;
            Line("end");
        }
        EmitOperatorMetamethods(cls);
        Line("");
        _currentMethodIsStatic = false;
        _className = null;
    }

    private void RegisterOperator(string owner, string op, FnDecl method)
    {
        string key = owner + "|" + op;
        if (!_operatorMethods.TryGetValue(key, out var overloads))
            _operatorMethods[key] = overloads = [];
        overloads.Add(method);
    }

    private static string OperatorFunctionName(FnDecl method)
    {
        string opName = method.Operator switch
        {
            "+" => "Add", "-" => "Subtract", "*" => "Multiply", "/" => "Divide", "%" => "Modulo",
            "==" => "Equal", "!=" => "NotEqual", "<" => "LessThan", ">" => "GreaterThan",
            "<=" => "LessThanOrEqual", ">=" => "GreaterThanOrEqual",
            _ => throw new InvalidOperationException($"unknown operator {method.Operator}")
        };
        string signature = string.Join("_", method.Params.Select(p =>
            new string(p.Type.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray())));
        return "__operator_" + opName + (signature.Length == 0 ? "" : "_" + signature);
    }

    private void EmitOperatorMetamethods(ClassDecl cls)
    {
        var groups = cls.Methods
            .Where(method => method.Operator is not null && LuaMetamethod(method.Operator, method.Params.Count) is not null)
            .GroupBy(method => (Name: LuaMetamethod(method.Operator!, method.Params.Count)!, Arity: method.Params.Count));

        foreach (var group in groups)
        {
            string[] args = Enumerable.Range(0, group.Key.Arity).Select(index => "arg" + index).ToArray();
            Line($"function {cls.Name}.{group.Key.Name}({string.Join(", ", args)})");
            _indent++;
            foreach (var method in group)
            {
                string condition = string.Join(" and ", method.Params.Select((parameter, index) =>
                    LuaTypeCheck(parameter.Type, args[index])));
                Line($"if {condition} then return {cls.Name}.{OperatorFunctionName(method)}({string.Join(", ", args)}) end");
            }
            Line("return nil");
            _indent--;
            Line("end");
        }
    }

    private static string LuaTypeCheck(string type, string expression) => type switch
    {
        "int" or "float" => $"type({expression}) == \"number\"",
        "bool" => $"type({expression}) == \"boolean\"",
        "string" => $"type({expression}) == \"string\"",
        _ => $"getmetatable({expression}) == {type}"
    };

    private static string? LuaMetamethod(string op, int arity) => (op, arity) switch
    {
        ("+", 2) => "__add",
        ("-", 2) => "__sub",
        ("-", 1) => "__unm",
        ("*", 2) => "__mul",
        ("/", 2) => "__div",
        ("%", 2) => "__mod",
        ("==", 2) => "__eq",
        ("<", 2) => "__lt",
        ("<=", 2) => "__le",
        _ => null
    };

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
                Line(_lua
                    ? (_headless ? $"print(\"{Esc(msg)}\")" : $"BJDebugMsg(\"{Esc(msg)}\")")
                    : $"call BJDebugMsg(\"{Esc(msg)}\")");
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
                    var inferredType = v.Type == "var" && v.Init is not null
                        ? InferType(v.Init) ?? throw new InvalidOperationException($"Не удалось вывести тип переменной {v.Name}")
                        : v.Type;
                    _locals[v.Name] = inferredType;
                    var init = v.Init is null ? null : EmitExpr(v.Init);
                    if (_lua)
                    {
                        if (init is not null)
                            Line($"local {v.Name} = {init}");
                        else if (inferredType.EndsWith("[]", StringComparison.Ordinal))
                            Line($"local {v.Name} = {{}}");
                        else
                            Line($"local {v.Name}");
                    }
                    else
                    {
                        Line(init is null
                            ? $"local {MapType(inferredType)} {v.Name}"
                            : $"local {MapType(inferredType)} {v.Name} = {init}");
                    }
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
        NameExpr n when CurrentFieldType(n.Name) is { } fieldType => fieldType,
        ThisExpr => _className,
        NewExpr n => n.Type,
        NumExpr n => IsFloatingLiteral(n.Text) ? "float" : "int",
        StrExpr => "string",
        BoolExpr => "bool",
        UnaryExpr u when u.Op == "!" => "bool",
        UnaryExpr u => InferType(u.Inner),
        BinaryExpr b => InferBinaryType(b),
        MemberExpr m => InferMemberType(m),
        CallExpr c => InferCallType(c),
        IndexExpr ix when InferType(ix.Object) == "string" => "string",
        IndexExpr ix when InferType(ix.Object) is { } arrayType &&
            arrayType.EndsWith("[]", StringComparison.Ordinal) => arrayType[..^2],
        _ => null
    };

    private string? CurrentFieldType(string name)
    {
        if (_currentMethodIsStatic || _className is null || _locals.ContainsKey(name))
            return null;

        return _classFields.TryGetValue(_className, out var fields) &&
               fields.TryGetValue(name, out var type)
            ? type
            : null;
    }

    private bool ShouldEmitCurrentField(string name) =>
        _lua && CurrentFieldType(name) is not null;

    private static bool IsFloatingLiteral(string text) =>
        text.Contains('.') || text.Contains('e') || text.Contains('E');

    private string? InferBinaryType(BinaryExpr expression)
    {
        var overload = FindOperator(InferType(expression.Left), InferType(expression.Right), expression.Op, [expression.Left, expression.Right]);
        if (overload is { } resolved)
            return resolved.Method.ReturnType;

        if (expression.Op is "==" or "!=" or "<" or ">" or "<=" or ">=" or "&&" or "||")
            return "bool";

        if (expression.Op == "+" &&
            (InferType(expression.Left) == "string" || InferType(expression.Right) == "string"))
            return "string";

        var leftType = InferType(expression.Left);
        var rightType = InferType(expression.Right);

        if (leftType == "float" || rightType == "float")
            return "float";

        if (leftType == "int" && rightType == "int")
            return "int";

        return null;
    }

    private string? InferMemberType(MemberExpr expression)
    {
        var receiverType = RecvType(expression.Object);
        if (receiverType is null)
            return null;

        if (_classFields.TryGetValue(receiverType, out var fields) &&
            fields.TryGetValue(expression.Name, out var fieldType))
            return fieldType;

        var member = NativeTable.Find(receiverType, expression.Name);
        return member is null ? null : GetNativeReturnType(member.Read);
    }

    private (string Owner, FnDecl Method)? FindOperator(string? leftType, string? rightType, string op, IReadOnlyList<Expr> arguments)
    {
        (string Owner, FnDecl Method)? best = null;
        int bestScore = int.MaxValue;

        foreach (string owner in new[] { leftType, rightType }.Where(t => !string.IsNullOrWhiteSpace(t)).Cast<string>().Distinct(StringComparer.Ordinal))
        {
            if (!_operatorMethods.TryGetValue(owner + "|" + op, out var overloads))
                continue;

            foreach (var method in overloads)
            {
                if (!method.IsStatic || method.Params.Count != arguments.Count)
                    continue;

                int score = 0;
                bool compatible = true;
                for (int index = 0; index < arguments.Count; index++)
                {
                    string? actual = InferType(arguments[index]);
                    string expected = method.Params[index].Type;
                    if (actual is null || actual == expected)
                        continue;
                    if (actual == "int" && expected == "float")
                    {
                        score++;
                        continue;
                    }

                    compatible = false;
                    break;
                }

                if (compatible && score < bestScore)
                {
                    best = (owner, method);
                    bestScore = score;
                }
            }
        }

        return best;
    }

    private string? InferCallType(CallExpr expression)
    {
        if (expression.Callee is NameExpr name)
        {
            if (_methods.TryGetValue(name.Name, out var function))
                return function.ReturnType;

            if (NativeSigs.Map.TryGetValue(name.Name, out var native))
                return native.ReturnType;

            return null;
        }

        if (expression.Callee is MemberExpr member)
        {
            if (member.Object is NameExpr className &&
                _methods.TryGetValue(className.Name + "." + member.Name, out var staticMethod) &&
                staticMethod.IsStatic)
                return staticMethod.ReturnType;

            var receiverType = RecvType(member.Object);
            if (receiverType is null)
                return null;

            if (_methods.TryGetValue(receiverType + "." + member.Name, out var method))
                return method.ReturnType;

            var nativeMember = NativeTable.Find(receiverType, member.Name);
            if (nativeMember?.Kind == MemberKind.Method)
                return GetNativeReturnType(nativeMember.Read);
        }

        return null;
    }

    private static string? GetNativeReturnType(string nativeExpression)
    {
        var openParen = nativeExpression.IndexOf('(');
        var nativeName = openParen >= 0
            ? nativeExpression[..openParen].Trim()
            : nativeExpression.Trim();

        return NativeSigs.Map.TryGetValue(nativeName, out var native)
            ? native.ReturnType
            : null;
    }

    private string EmitExpr(Expr e) => e switch
    {
        NumExpr n => n.Text,
        StrExpr s => $"\"{Esc(s.Text)}\"",
        RawExpr r => _lua ? Rawcode.Pack(r.Id).ToString() : $"'{r.Id}'",
        BoolExpr b => b.Value ? "true" : "false",
        NullExpr => _lua ? "nil" : "null",
        ThisExpr => _lua ? "self" : "this",
        NameExpr n when ShouldEmitCurrentField(n.Name) => $"self.{n.Name}",
        NameExpr n => n.Name,

        UnaryExpr u when FindOperator(InferType(u.Inner), null, u.Op, [u.Inner]) is { } overload =>
            $"{overload.Owner}.{OperatorFunctionName(overload.Method)}({EmitExpr(u.Inner)})",

        UnaryExpr u when u.Op == "!" && !_lua =>
            $"not ({EmitExpr(u.Inner)})",

        UnaryExpr u =>
            $"{(u.Op == "!" ? "not " : "-")}{EmitExpr(u.Inner)}",

        BinaryExpr b when FindOperator(InferType(b.Left), InferType(b.Right), b.Op, [b.Left, b.Right]) is { } overload =>
            $"{overload.Owner}.{OperatorFunctionName(overload.Method)}({EmitExpr(b.Left)}, {EmitExpr(b.Right)})",

        BinaryExpr b when b.Op == "+" && _lua && IsStringish(b) =>
            Concat(b.Left, b.Right),

        BinaryExpr b =>
            $"({EmitExpr(b.Left)} {Op(b.Op)} {EmitExpr(b.Right)})",

        IndexExpr ix => EmitIndex(ix),

        NewExpr n when n.Type.EndsWith("[]", StringComparison.Ordinal) && _lua => "{}",

        NewExpr n when n.Type == "List" && _lua => "{}",

        NewExpr n when NativeTable.Constructors.TryGetValue(n.Type, out var ctor) =>
            $"{ctor}({string.Join(", ", n.Args.Select(EmitExpr))})",

        NewExpr n when _lua =>
            $"{n.Type}.new({string.Join(", ", n.Args.Select(EmitExpr))})",

        NewExpr n =>
            $"{n.Type}.create({string.Join(", ", n.Args.Select(EmitExpr))})",

        MemberExpr m => EmitMember(m),
        CallExpr c => EmitCall(c),
        LambdaExpr l when _lua => EmitLambda(l),
        LambdaExpr l => throw new NotSupportedException($"лямбды поддерживаются только в Lua (строка {l.Line})"),

        _ => throw new NotSupportedException(
            $"Unsupported expression node: {e.GetType().Name}")
    };

    private string EmitLambda(LambdaExpr l)
    {
        var saved = _locals;
        _locals = new Dictionary<string, string>(saved, StringComparer.Ordinal);
        foreach (var p in l.Params)
            _locals[p.Name] = p.Type;

        var lines = new List<string>();
        var oldLines = _lines;
        _lines = lines;

        var ps = string.Join(", ", l.Params.Select(p => p.Name));
        Line($"function({ps})");
        _indent++;
        EmitStmts(l.Body);
        _indent--;
        Line("end");

        _lines = oldLines;
        _locals = saved;

        return string.Join("\n" + new string(' ', _indent * 4), lines);
    }

    private string EmitMember(MemberExpr m)
    {
        if (m.Name == "Length")
        {
            var expr = EmitExpr(m.Object);
            var t = RecvType(m.Object);
            if (t == "string")
                return $"string.len({expr})";
            // array / List
            return $"#{expr}";
        }

        // Built-in J.Units.XXX / J.Skills.XXX rawcode constants
        if (m.Object is MemberExpr mid && mid.Object is NameExpr j && j.Name == "J")
        {
            string? code = null;
            if (mid.Name == "Units" && StandardRawcodes.Units.TryGetValue(m.Name, out var ucode))
                code = ucode;
            else if (mid.Name == "Skills" && StandardRawcodes.Skills.TryGetValue(m.Name, out var acode))
                code = acode;
            if (code is not null)
                return _lua ? Rawcode.Pack(code).ToString() : $"'{code}'";
        }

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
        // List methods
        if (c.Callee is MemberExpr listMem && RecvType(listMem.Object) == "List")
        {
            var listRecv = EmitExpr(listMem.Object);
            var listArgs = c.Args.Select(EmitExpr).ToList();
            return listMem.Name switch
            {
                "Add" => $"table.insert({listRecv}, {listArgs[0]})",
                "Remove" => $"__list_remove({listRecv}, {listArgs[0]})",
                "IndexOf" => $"__list_indexof({listRecv}, {listArgs[0]})",
                "Clear" => $"for __i = #{listRecv}, 1, -1 do {listRecv}[__i] = nil end",
                _ => throw new NotSupportedException($"List.{listMem.Name}")
            };
        }

        // ToString
        if (c.Callee is MemberExpr toStrMem && toStrMem.Name == "ToString" && c.Args.Count == 0)
        {
            if (toStrMem.Object is MemberExpr inner && inner.Object is NameExpr enName &&
                _enums.TryGetValue(enName.Name, out var en) &&
                en.Members.Any(m => m.Name == inner.Name))
            {
                return $"\"{inner.Name}\"";
            }

            var t = InferType(toStrMem.Object);
            if (t is not null && _enums.ContainsKey(t))
                return $"{t}_ToString({EmitExpr(toStrMem.Object)})";
        }

        // Print
        if (c.Callee is NameExpr id && id.Name == "Print")
        {
            var parts = c.Args.Select(ToStringExpr).ToList();
            var msg = parts.Count == 0 ? "\"\"" : string.Join(" .. ", parts);
            return $"{(_headless ? "print" : "BJDebugMsg")}({msg})";
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

            bool isDeclaredStaticMethod =
                rt is not null &&
                _methods.TryGetValue(rt + "." + mem.Name, out var declaredMethod) &&
                declaredMethod.IsStatic;

            if (isDeclaredStaticMethod ||
                (mem.Object is NameExpr staticOwner &&
                 !_locals.ContainsKey(staticOwner.Name) &&
                 CurrentFieldType(staticOwner.Name) is null))
                return $"{recv}.{mem.Name}({string.Join(", ", args)})";

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
        NameExpr n when CurrentFieldType(n.Name) is { } fieldType => fieldType,
        NewExpr n => n.Type,
        BinaryExpr b => InferBinaryType(b),
        UnaryExpr u => FindOperator(InferType(u.Inner), null, u.Op, [u.Inner])?.Method.ReturnType ?? InferType(u.Inner),
        MemberExpr m => InferMemberType(m),
        CallExpr c => InferCallType(c),
        _ => null,
    };

    private string EmitIndex(IndexExpr ix)
    {
        var obj = EmitExpr(ix.Object);
        var t = RecvType(ix.Object);

        // string
        if (t == "string")
        {
            if (ix.Index is FromEndExpr feStr)
            {
                var kStr = EmitExpr(feStr.Inner);
                return $"string.sub({obj}, -{kStr}, -{kStr})";
            }
            var idxStr = EmitExpr(ix.Index);
            return $"string.sub({obj}, {idxStr} + 1, {idxStr} + 1)";
        }

        // array / List
        if (ix.Index is FromEndExpr fe)
        {
            var k = EmitExpr(fe.Inner);
            return $"{obj}[#{obj} - {k} + 1]";
        }
        var idx = EmitExpr(ix.Index);
        return $"{obj}[{idx} + 1]";
    }

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

    private static string MapType(string t)
    {
        while (t.EndsWith("[]", StringComparison.Ordinal))
            t = t[..^2];

        return t switch
        {
            "int" => "integer",
            "float" => "real",
            "bool" => "boolean",
            "void" => "nothing",
            _ => t,
        };
    }

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
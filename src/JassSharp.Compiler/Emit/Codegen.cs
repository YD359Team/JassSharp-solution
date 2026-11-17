using JassSharp.Runtime;
using JassSharp.Syntax;

namespace JassSharp.Emit;

public sealed class Codegen
{
    private readonly bool _headless;
    private List<string> _lines = [];
    private int _indent;
    private int _foreachCounter;
    private int _loopCounter;
    private readonly Stack<(string ContinueLabel, string? BreakFlag)> _loopTargets = new();
    private Dictionary<string, string> _locals = new(StringComparer.Ordinal);
    private string? _className;
    private bool _currentMethodIsStatic;
    private readonly Dictionary<string, FnDecl> _inlineMethods = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FnDecl> _methods = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _qualifiedFunctions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<FnDecl>> _operatorMethods = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EnumDecl> _enums = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> _classFields = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _classBaseCandidates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _classBases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, FnDecl>> _interfaceMethods = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _interfaceBases = new(StringComparer.Ordinal);

    public Codegen(bool headless = false)
    {
        _headless = headless;
    }

    public string Emit(NsUnit unit, IEnumerable<NsUnit>? imports = null, bool finalizeHooks = true)
    {
        _namespaceName = unit.Name;

        if (imports is not null)
        {
            foreach (var imported in imports)
            {
                foreach (var contract in imported.Interfaces ?? [])
                {
                    _interfaceMethods[contract.Name] = contract.Methods.ToDictionary(method => method.Name, StringComparer.Ordinal);
                    _interfaceBases[contract.Name] = contract.BaseInterfaces;
                }

                foreach (var cls in imported.Classes)
                {
                    _classFields[cls.Name] = cls.Fields.ToDictionary(field => field.Name, field => field.Type, StringComparer.Ordinal);
                    _classBaseCandidates[cls.Name] = cls.BaseTypes ?? [];
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
                foreach (var fn in imported.Functions.Where(function => !function.IsHook))
                {
                    _methods[fn.Name] = fn;
                    _qualifiedFunctions[imported.Name + "." + fn.Name] = fn.Name;
                }
            }
        }

        foreach (var contract in unit.Interfaces ?? [])
        {
            _interfaceMethods[contract.Name] = contract.Methods.ToDictionary(method => method.Name, StringComparer.Ordinal);
            _interfaceBases[contract.Name] = contract.BaseInterfaces;
        }

        foreach (var cls in unit.Classes)
        {
            _classFields[cls.Name] = cls.Fields.ToDictionary(field => field.Name, field => field.Type, StringComparer.Ordinal);
            _classBaseCandidates[cls.Name] = cls.BaseTypes ?? [];
            foreach (var m in cls.Methods)
            {
                if (m.Operator is { } op)
                    RegisterOperator(cls.Name, op, m);
                else
                    _methods[cls.Name + "." + m.Name] = m;
            }
        }
        foreach (var candidate in _classBaseCandidates)
        {
            string? baseClass = candidate.Value.FirstOrDefault(_classFields.ContainsKey);
            if (baseClass is not null)
                _classBases[candidate.Key] = baseClass;
        }

        foreach (var en in unit.Enums)
            _enums[en.Name] = en;
        foreach (var fn in unit.Functions.Where(function => !function.IsHook))
        {
            _methods[fn.Name] = fn;
            _qualifiedFunctions[unit.Name + "." + fn.Name] = fn.Name;
        }

        // collect inline
        foreach (var kv in _methods)
            if (kv.Value.Attributes?.Contains("Inline") == true)
                _inlineMethods[kv.Key] = kv.Value;

        var hasInit = unit.Functions.Any(f => f.Name == "OnInit");
        if (unit.Functions.Any(function => function.IsHook))
            EmitHookRuntime();
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
        foreach (var cls in GetClassEmissionOrder(unit.Classes)) EmitClassLua(cls);
        foreach (var fn in unit.Functions.Where(function => !function.IsHook)) EmitFnLua(fn);
        EmitHooksLua(unit.Functions.Where(function => function.IsHook));
        if (!_headless)
            EmitLuaEntry(hasInit);

        Line(@"
local __collection_null = {}
local function __collection_store(value)
    if value == nil then return __collection_null end
    return value
end
local function __collection_load(value)
    if value == __collection_null then return nil end
    return value
end

function __list_remove(t, v)
    v = __collection_store(v)
    for i = 1, #t do
        if t[i] == v then
            table.remove(t, i)
            return true
        end
    end
    return false
end
function __list_indexof(t, v)
    v = __collection_store(v)
    for i = 1, #t do
        if t[i] == v then return i - 1 end
    end
    return -1
end

function __queue_new()
    return { _items = {}, _first = 1, _last = 0 }
end
function __queue_count(q)
    return q._last - q._first + 1
end
function __queue_enqueue(q, value)
    q._last = q._last + 1
    q._items[q._last] = __collection_store(value)
end
function __queue_dequeue(q)
    if __queue_count(q) <= 0 then error(""Queue is empty"") end
    local value = __collection_load(q._items[q._first])
    q._items[q._first] = nil
    q._first = q._first + 1
    if q._first > q._last then q._first = 1; q._last = 0 end
    return value
end
function __queue_peek(q)
    if __queue_count(q) <= 0 then error(""Queue is empty"") end
    return __collection_load(q._items[q._first])
end

function __stack_new()
    return { _items = {} }
end
function __stack_push(s, value)
    table.insert(s._items, __collection_store(value))
end
function __stack_pop(s)
    if #s._items == 0 then error(""Stack is empty"") end
    return __collection_load(table.remove(s._items))
end
function __stack_peek(s)
    if #s._items == 0 then error(""Stack is empty"") end
    return __collection_load(s._items[#s._items])
end

function __dictionary_new()
    local items = {}
    setmetatable(items, {
        __index = function(_, key)
            error(""The given key was not present in the dictionary: "" .. tostring(key))
        end
    })
    return { _items = items, _count = 0 }
end
function __dictionary_add(d, key, value)
    if key == nil then error(""Dictionary key cannot be nil"") end
    if value == nil then error(""Dictionary value cannot be nil"") end
    if rawget(d._items, key) ~= nil then error(""An item with the same key already exists"") end
    d._items[key] = __collection_store(value)
    d._count = d._count + 1
end
function __dictionary_remove(d, key)
    if rawget(d._items, key) == nil then return false end
    d._items[key] = nil
    d._count = d._count - 1
    return true
end
function __dictionary_tryadd(d, key, value)
    if key == nil or rawget(d._items, key) ~= nil then return false end
    d._items[key] = __collection_store(value)
    d._count = d._count + 1
    return true
end
function __dictionary_tryremove(d, key)
    return __dictionary_remove(d, key)
end
function __dictionary_set(d, key, value)
    if key == nil then error(""Dictionary key cannot be nil"") end
    if rawget(d._items, key) == nil then d._count = d._count + 1 end
    d._items[key] = __collection_store(value)
end
function __dictionary_containskey(d, key)
    return rawget(d._items, key) ~= nil
end
");
        if (finalizeHooks)
            Line("if __jasssharp_apply_hooks then __jasssharp_apply_hooks() end");
        return string.Join("\n", _lines) + "\n";
    }

    private void EmitHookRuntime()
    {
        Line("if not __jasssharp_hook_registry then");
        _indent++;
        Line("__jasssharp_hook_registry = {}");
        Line("function __jasssharp_register_hook(name, factory)");
        _indent++;
        Line("if __jasssharp_hooks_applied then error(\"JASS# hook registered after finalization: \" .. name) end");
        Line("local chain = __jasssharp_hook_registry[name]");
        Line("if not chain then chain = {}; __jasssharp_hook_registry[name] = chain end");
        Line("chain[#chain + 1] = factory");
        _indent--;
        Line("end");
        Line("function __jasssharp_apply_hooks()");
        _indent++;
        Line("if __jasssharp_hooks_applied then return end");
        Line("local replacements = {}");
        Line("for name, chain in pairs(__jasssharp_hook_registry) do");
        _indent++;
        Line("local current = _G[name]");
        Line("if type(current) ~= \"function\" then error(\"JASS# hook target is not a function: \" .. name) end");
        Line("for i = #chain, 1, -1 do current = chain[i](current) end");
        Line("replacements[name] = current");
        _indent--;
        Line("end");
        Line("for name, replacement in pairs(replacements) do _G[name] = replacement end");
        Line("__jasssharp_hooks_applied = true");
        _indent--;
        Line("end");
        _indent--;
        Line("end");
        Line("");
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

    private IEnumerable<ClassDecl> GetClassEmissionOrder(IEnumerable<ClassDecl> classes)
    {
        var byName = classes.ToDictionary(cls => cls.Name, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<ClassDecl>();

        void Visit(ClassDecl cls)
        {
            if (!visited.Add(cls.Name))
                return;
            if (_classBases.TryGetValue(cls.Name, out var baseName) &&
                byName.TryGetValue(baseName, out var baseClass))
                Visit(baseClass);
            ordered.Add(cls);
        }

        foreach (var cls in classes)
            Visit(cls);
        return ordered;
    }

    private void EmitClassLua(ClassDecl cls)
    {
        _className = cls.Name;
        Line($"{cls.Name} = {{}}");
        Line($"{cls.Name}.__index = {cls.Name}");
        if (_classBases.TryGetValue(cls.Name, out var baseClassName))
            Line($"setmetatable({cls.Name}, {{ __index = {baseClassName} }})");

        var constructor = cls.Ctor;
        var constructorParameters = constructor?.Params ?? [];
        _currentMethodIsStatic = false;
        _locals = constructorParameters.ToDictionary(parameter => parameter.Name, parameter => parameter.Type, StringComparer.Ordinal);
        Line($"function {cls.Name}.__init(self{(constructorParameters.Count == 0 ? "" : ", " + string.Join(", ", constructorParameters.Select(parameter => parameter.Name)))})");
        _indent++;
        if (_classBases.TryGetValue(cls.Name, out baseClassName))
            Line($"{baseClassName}.__init(self)");
        foreach (var field in cls.Fields.Where(field => field.IsArray))
            Line($"self.{field.Name} = {{}}");
        if (constructor is not null)
            EmitStmts(constructor.Body.Where(statement => statement is not ReturnStmt).ToList());
        _indent--;
        Line("end");

        Line($"function {cls.Name}.new({string.Join(", ", constructorParameters.Select(parameter => parameter.Name))})");
        _indent++;
        Line($"local self = setmetatable({{}}, {cls.Name})");
        Line($"{cls.Name}.__init(self{(constructorParameters.Count == 0 ? "" : ", " + string.Join(", ", constructorParameters.Select(parameter => parameter.Name)))})");
        Line("return self");
        _indent--;
        Line("end");

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
            if (m.Value is NumExpr n && TryParseInt32Literal(n.Text, out var v))
            {
                value = EmitNumber(n.Text);
                next = unchecked(v + 1);
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

            Line($"{en.Name}_{m.Name} = {value}");
        }

        // ToString helper
        Line($"function {en.Name}_ToString(v)");
        _indent++;
        foreach (var m in en.Members)
            Line($"if v == {en.Name}_{m.Name} then return \"{m.Name}\" end");
        Line("return \"?\"");
        _indent--;
        Line("end");
        Line("");
    }

    private string? _namespaceName;

    private void EmitHooksLua(IEnumerable<FnDecl> hooks)
    {
        foreach (var hook in hooks)
        {
            string parameters = string.Join(", ", hook.Params.Select(parameter => parameter.Name));
            Line($"__jasssharp_register_hook(\"{hook.Name}\", function(__jasssharp_original)");
            _indent++;
            Line($"return function({parameters})");
            _indent++;
            Line("local original = __jasssharp_original");

            _className = null;
            _currentMethodIsStatic = false;
            _locals = hook.Params.ToDictionary(
                parameter => parameter.Name,
                parameter => parameter.Type,
                StringComparer.Ordinal);
            _locals["original"] = "function";

            EmitStmts(hook.Body, hook.Attributes?.Contains("Trace") == true);
            _indent--;
            Line("end");
            _indent--;
            Line("end)");
            Line("");
        }
    }

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
            if (trace)
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
                    ForeachStmt foreachLoop => $"{foreachLoop.Line}:{foreachLoop.Col}",
                    DoWhileStmt loop => $"{loop.Line}:{loop.Col}",
                    RawLuaStmt raw => $"{raw.Line}:{raw.Col}",
                    _ => "?"
                };
                var msg = $"[trace] {loc}";
                Line(_headless ? $"print(\"{Esc(msg)}\")" : $"BJDebugMsg(\"{Esc(msg)}\")");
            }
            switch (s)
            {
                case VarStmt v:
                    var inferredType = v.Type == "var" && v.Init is not null
                        ? InferType(v.Init) ?? throw new InvalidOperationException($"Не удалось вывести тип переменной {v.Name}")
                        : v.Type;
                    _locals[v.Name] = inferredType;
                    var init = v.Init is null ? null : EmitExpr(v.Init);
                    if (init is not null)
                        Line($"local {v.Name} = {init}");
                    else if (inferredType.EndsWith("[]", StringComparison.Ordinal))
                        Line($"local {v.Name} = {{}}");
                    else
                        Line($"local {v.Name}");
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
                        Line(ex);
                    }
                    break;
                case ReturnStmt r:
                    Line(r.Expr is null ? "return" : $"return {EmitExpr(r.Expr)}");
                    break;
                case BreakStmt:
                    if (_loopTargets.Count == 0)
                        throw new InvalidOperationException("break outside loop reached Lua code generation");
                    var breakTarget = _loopTargets.Peek();
                    if (breakTarget.BreakFlag is null)
                        Line("break");
                    else
                    {
                        Line($"{breakTarget.BreakFlag} = true");
                        Line($"goto {breakTarget.ContinueLabel}");
                    }
                    break;
                case ContinueStmt:
                    if (_loopTargets.Count == 0)
                        throw new InvalidOperationException("continue outside loop reached Lua code generation");
                    Line($"goto {_loopTargets.Peek().ContinueLabel}");
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
                    Line("end");
                    break;
                case WhileStmt w:
                {
                    string label = $"__continue{++_loopCounter}";
                    Line($"while {EmitExpr(w.Cond)} do");
                    _indent++;
                    Line("do");
                    _indent++;
                    _loopTargets.Push((label, null));
                    EmitStmts(w.Body, trace);
                    _loopTargets.Pop();
                    _indent--;
                    Line("end");
                    Line($"::{label}::");
                    _indent--;
                    Line("end");
                    break;
                }
                case DoWhileStmt loop:
                {
                    string label = $"__continue{++_loopCounter}";
                    Line("repeat");
                    _indent++;
                    Line("do");
                    _indent++;
                    _loopTargets.Push((label, null));
                    EmitStmts(loop.Body, trace);
                    _loopTargets.Pop();
                    _indent--;
                    Line("end");
                    Line($"::{label}::");
                    _indent--;
                    Line($"until not ({EmitExpr(loop.Cond)})");
                    break;
                }
                case RawLuaStmt raw:
                    foreach (var rawLine in raw.Code.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                        Line(rawLine);
                    break;
                case ForeachStmt foreachLoop:
                    EmitForeach(foreachLoop, trace);
                    break;
                case ForStmt f:
                {
                    if (f.Init is not null) EmitStmts([f.Init], trace);
                    string label = $"__continue{++_loopCounter}";
                    Line($"while {EmitExpr(f.Cond ?? new BoolExpr(true))} do");
                    _indent++;
                    Line("do");
                    _indent++;
                    _loopTargets.Push((label, null));
                    EmitStmts(f.Body, trace);
                    _loopTargets.Pop();
                    _indent--;
                    Line("end");
                    Line($"::{label}::");
                    if (f.Step is not null) EmitStmts([f.Step], trace);
                    _indent--;
                    Line("end");
                    break;
                }
            }
        }
    }

    private void EmitForeach(ForeachStmt loop, bool trace)
    {
        string collectionType = InferType(loop.Collection) ?? throw new InvalidOperationException(
            $"Не удалось определить тип коллекции foreach в строке {loop.Line}");
        string collection = EmitExpr(loop.Collection);
        var savedLocals = _locals;
        _locals = new Dictionary<string, string>(savedLocals, StringComparer.Ordinal);
        int id = ++_foreachCounter;
        string label = $"__continue_foreach{id}";

        if (collectionType == "group")
        {
            string breakFlag = $"__foreachBreak{id}";
            Line($"local {breakFlag} = false");
            Line($"ForGroup({collection}, function()");
            _indent++;
            Line($"if not {breakFlag} then");
            _indent++;
            Line($"local {loop.Name} = GetEnumUnit()");
            Line("do");
            _indent++;
            _locals[loop.Name] = "unit";
            _loopTargets.Push((label, breakFlag));
            EmitStmts(loop.Body, trace);
            _loopTargets.Pop();
            _indent--;
            Line("end");
            _indent--;
            Line("end");
            Line($"::{label}::");
            _indent--;
            Line("end)");
            _locals = savedLocals;
            return;
        }

        string collectionName = $"__foreachCollection{id}";
        string indexName = $"__foreachIndex{id}";
        string valueName = $"__foreachValue{id}";
        Line($"local {collectionName} = {collection}");

        if (collectionType.EndsWith("[]", StringComparison.Ordinal))
        {
            string elementType = collectionType[..^2];
            Line($"for {indexName} = 1, #{collectionName} do");
            _indent++;
            _locals[loop.Name] = elementType;
            Line($"local {loop.Name} = __collection_load({collectionName}[{indexName}])");
        }
        else if (TryGetGenericArguments(collectionType, out var genericName, out var genericArgs) &&
                 (genericName is "List" or "Queue" or "Stack"))
        {
            string elementType = genericArgs[0];
            if (genericName == "Queue")
                Line($"for {indexName} = {collectionName}._first, {collectionName}._last do");
            else if (genericName == "Stack")
                Line($"for {indexName} = #{collectionName}._items, 1, -1 do");
            else
                Line($"for {indexName} = 1, #{collectionName} do");
            _indent++;
            _locals[loop.Name] = elementType;
            Line(genericName == "Queue"
                ? $"local {loop.Name} = __collection_load({collectionName}._items[{indexName}])"
                : genericName == "Stack"
                    ? $"local {loop.Name} = __collection_load({collectionName}._items[{indexName}])"
                    : $"local {loop.Name} = __collection_load({collectionName}[{indexName}])");
        }
        else if (TryGetGenericArguments(collectionType, out genericName, out genericArgs) &&
                 genericName == "Dictionary")
        {
            string pairType = $"KeyValuePair<{genericArgs[0]},{genericArgs[1]}>";
            Line($"for {indexName}, {valueName} in pairs({collectionName}._items) do");
            _indent++;
            _locals[loop.Name] = pairType;
            Line($"local {loop.Name} = {{ Key = {indexName}, Value = __collection_load({valueName}) }}");
        }
        else
            throw new NotSupportedException(
                $"foreach does not support collection type '{collectionType}' (line {loop.Line})");

        Line("do");
        _indent++;
        _loopTargets.Push((label, null));
        EmitStmts(loop.Body, trace);
        _loopTargets.Pop();
        _indent--;
        Line("end");
        Line($"::{label}::");
        _indent--;
        Line("end");
        _locals = savedLocals;
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
            Line($"local {p.Name} = {argExpr}");
            _locals[p.Name] = p.Type;
        }

        // if method, bind self/this
        if (recvName is not null && _className is not null)
        {
            Line($"local self = {recvName}");
        }

        EmitStmts(meth.Body);

        // restore locals
        _locals = saved;
        return true;
    }

    private void EmitAssign(Expr target, Expr value)
    {
        if (target is IndexExpr dictionaryIndex &&
            RecvType(dictionaryIndex.Object) is { } dictionaryType &&
            IsGenericType(dictionaryType, "Dictionary"))
        {
            Line($"__dictionary_set({EmitExpr(dictionaryIndex.Object)}, {EmitExpr(dictionaryIndex.Index)}, {EmitExpr(value)})");
            return;
        }

        if (target is MemberExpr m)
        {
            var rt = RecvType(m.Object);
            var prop = rt is null ? null : NativeTable.Find(rt, m.Name);
            if (prop?.Kind == MemberKind.GetSet && prop.Write is not null)
            {
                var call = Apply(prop.Write, EmitExpr(m.Object), [EmitExpr(value)]);
                Line(call);
                return;
            }
        }
        var left = EmitExpr(target);
        var v = EmitExpr(value);
        Line($"{left} = {v}");
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
            var args = p.Args.Select(EmitExpr).ToList();
            var call = Apply(prop.Read, recv, args);
            Line(call);
        }
        return true;
    }

    private bool IsStringish(Expr e) =>
        InferType(e) == "string" ||
        (e is BinaryExpr b && b.Op == "+" && (IsStringish(b.Left) || IsStringish(b.Right)));

    private string Concat(Expr left, Expr right)
    {
        var l = ToStringExpr(left);
        var r = ToStringExpr(right);
        return $"({l} .. {r})";
    }

    private string EmitInterpolated(InterpolatedExpr expression)
    {
        var parts = new List<string>();
        foreach (var part in expression.Parts)
        {
            if (part.Text is not null)
                parts.Add($"\"{Esc(part.Text)}\"");
            else if (part.Expression is not null)
                parts.Add(ToStringExpr(part.Expression));
        }

        return parts.Count switch
        {
            0 => "\"\"",
            1 => parts[0],
            _ => $"({string.Join(" .. ", parts)})"
        };
    }

    private string ToStringExpr(Expr e)
    {
        if (e is NullExpr)
            return "\"\"";

        var t = InferType(e);
        var emitted = EmitExpr(e);
        return t switch
        {
            "int" => $"tostring({emitted})",
            "float" => $"tostring({emitted})",
            "bool" => $"tostring({emitted})",
            "string" => $"({emitted} or \"\")",
            "unit" => $"GetUnitName({emitted})",
            "player" => $"GetPlayerName({emitted})",
            "item" => $"GetItemName({emitted})",
            _ => $"tostring({emitted})"
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
        InterpolatedExpr => "string",
        BoolExpr => "bool",
        UnaryExpr u when u.Op == "!" => "bool",
        UnaryExpr u => InferType(u.Inner),
        BinaryExpr b => InferBinaryType(b),
        MemberExpr m => InferMemberType(m),
        CallExpr c => InferCallType(c),
        IndexExpr ix => InferIndexType(ix),
        _ => null
    };

    private string? CurrentFieldType(string name)
    {
        if (_currentMethodIsStatic || _className is null || _locals.ContainsKey(name))
            return null;

        return TryGetClassField(_className, name, out var type) ? type : null;
    }

    private FnDecl? FindInterfaceMethod(string interfaceName, string methodName)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(interfaceName);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            if (!visited.Add(current))
                continue;
            if (_interfaceMethods.TryGetValue(current, out var methods) &&
                methods.TryGetValue(methodName, out var method))
                return method;
            if (_interfaceBases.TryGetValue(current, out var parents))
                foreach (string parent in parents)
                    pending.Push(parent);
        }
        return null;
    }

    private FnDecl? FindClassMethod(string className, string methodName)
    {
        string? current = className;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (current is not null && visited.Add(current))
        {
            if (_methods.TryGetValue(current + "." + methodName, out var method))
                return method;
            current = _classBases.GetValueOrDefault(current);
        }
        return null;
    }

    private bool TryGetClassField(string className, string fieldName, out string type)
    {
        string? current = className;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (current is not null && visited.Add(current))
        {
            if (_classFields.TryGetValue(current, out var fields) &&
                fields.TryGetValue(fieldName, out type!))
                return true;
            current = _classBases.GetValueOrDefault(current);
        }
        type = null!;
        return false;
    }

    private bool ShouldEmitCurrentField(string name) =>
        CurrentFieldType(name) is not null;

    private static bool IsFloatingLiteral(string text) =>
        !text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
        (text.Contains('.') || text.Contains('e') || text.Contains('E'));

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

    private string? InferIndexType(IndexExpr expression)
    {
        string? indexedType = InferType(expression.Object);
        if (indexedType == "string")
            return "string";
        if (indexedType is not null && indexedType.EndsWith("[]", StringComparison.Ordinal))
            return indexedType[..^2];
        if (indexedType is not null && TryGetGenericArguments(indexedType, out var collectionName, out var arguments))
        {
            if (collectionName == "List") return arguments[0];
            if (collectionName == "Dictionary") return arguments[1];
        }
        return null;
    }

    private string? InferMemberType(MemberExpr expression)
    {
        var receiverType = RecvType(expression.Object);
        if (receiverType is null)
            return null;

        if (expression.Name == "HandleId" && Warcraft3TypeHierarchy.IsHandleType(receiverType))
            return "int";

        if (expression.Name == "Count" &&
            (IsGenericType(receiverType, "List") ||
             IsGenericType(receiverType, "Queue") ||
             IsGenericType(receiverType, "Stack") ||
             IsGenericType(receiverType, "Dictionary")))
            return "int";

        if (TryGetGenericArguments(receiverType, out var genericName, out var genericArgs) &&
            genericName == "KeyValuePair" && genericArgs.Count == 2)
            return expression.Name switch { "Key" => genericArgs[0], "Value" => genericArgs[1], _ => null };

        if (TryGetClassField(receiverType, expression.Name, out var fieldType))
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
                FindClassMethod(className.Name, member.Name) is { IsStatic: true } staticMethod)
                return staticMethod.ReturnType;

            var receiverType = RecvType(member.Object);
            if (receiverType is null)
                return null;

            if (TryGetGenericArguments(receiverType, out var collectionName, out var collectionArgs))
            {
                if (collectionName == "Queue" && member.Name == "Enqueue") return "void";
                if (collectionName == "Queue" && (member.Name is "Dequeue" or "Peek")) return collectionArgs[0];
                if (collectionName == "Stack" && member.Name == "Push") return "void";
                if (collectionName == "Stack" && (member.Name is "Pop" or "Peek")) return collectionArgs[0];
                if (collectionName == "List" && (member.Name is "Add" or "Clear")) return "void";
                if (collectionName == "List" && member.Name == "IndexOf") return "int";
                if (collectionName == "List" && member.Name == "Remove") return "bool";
                if (collectionName == "Dictionary" && member.Name == "Add") return "void";
                if (collectionName == "Dictionary" && (member.Name is "Remove" or "TryAdd" or "TryRemove" or "ContainsKey")) return "bool";
            }

            if (FindClassMethod(receiverType, member.Name) is { } method)
                return method.ReturnType;

            if (FindInterfaceMethod(receiverType, member.Name) is { } interfaceMethod)
                return interfaceMethod.ReturnType;

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
        NumExpr n => EmitNumber(n.Text),
        StrExpr s => $"\"{Esc(s.Text)}\"",
        InterpolatedExpr interpolated => EmitInterpolated(interpolated),
        RawExpr r => Rawcode.Pack(r.Id).ToString(),
        BoolExpr b => b.Value ? "true" : "false",
        NullExpr => "nil",
        ThisExpr => "self",
        NameExpr n when ShouldEmitCurrentField(n.Name) => $"self.{n.Name}",
        NameExpr n => n.Name,

        UnaryExpr u when FindOperator(InferType(u.Inner), null, u.Op, [u.Inner]) is { } overload =>
            $"{overload.Owner}.{OperatorFunctionName(overload.Method)}({EmitExpr(u.Inner)})",

        UnaryExpr u when u.Op == "-" && u.Inner is NumExpr { Text: "2147483648" } =>
            "(-2147483647 - 1)",

        UnaryExpr u =>
            $"{(u.Op == "!" ? "not " : "-")}{EmitExpr(u.Inner)}",

        BinaryExpr b when FindOperator(InferType(b.Left), InferType(b.Right), b.Op, [b.Left, b.Right]) is { } overload =>
            $"{overload.Owner}.{OperatorFunctionName(overload.Method)}({EmitExpr(b.Left)}, {EmitExpr(b.Right)})",

        BinaryExpr b when b.Op == "+" && IsStringish(b) =>
            Concat(b.Left, b.Right),

        BinaryExpr b =>
            $"({EmitExpr(b.Left)} {Op(b.Op)} {EmitExpr(b.Right)})",

        IndexExpr ix => EmitIndex(ix),

        NewExpr n when n.Type.EndsWith("[]", StringComparison.Ordinal) => "{}",

        NewExpr n when IsGenericType(n.Type, "List") => "{}",
        NewExpr n when IsGenericType(n.Type, "Queue") => "__queue_new()",
        NewExpr n when IsGenericType(n.Type, "Stack") => "__stack_new()",
        NewExpr n when IsGenericType(n.Type, "Dictionary") => "__dictionary_new()",

        NewExpr n when NativeTable.Constructors.TryGetValue(n.Type, out var ctor) =>
            $"{ctor}({string.Join(", ", n.Args.Select(EmitExpr))})",

        NewExpr n =>
            $"{n.Type}.new({string.Join(", ", n.Args.Select(EmitExpr))})",

        MemberExpr m => EmitMember(m),
        CallExpr c => EmitCall(c),
        LambdaExpr l => EmitLambda(l),

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
        var receiverType = RecvType(m.Object);
        var receiver = EmitExpr(m.Object);
        if (m.Name == "HandleId" && receiverType is not null &&
            Warcraft3TypeHierarchy.IsHandleType(receiverType))
            return $"GetHandleId({receiver})";
        if (m.Name == "Count" && receiverType is not null &&
            TryGetGenericArguments(receiverType, out var collectionName, out _))
        {
            return collectionName switch
            {
                "Queue" => $"__queue_count({receiver})",
                "Stack" => $"#{receiver}._items",
                "Dictionary" => $"{receiver}._count",
                "List" => $"#{receiver}",
                _ => $"{receiver}.Count"
            };
        }

        if (m.Name == "Length")
        {
            if (receiverType == "string")
                return $"utf8.len({receiver})";
            return $"#{receiver}";
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
                return Rawcode.Pack(code).ToString();
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
        // Built-in generic collection operations.
        if (c.Callee is MemberExpr collectionMember &&
            RecvType(collectionMember.Object) is { } collectionType &&
            TryGetGenericArguments(collectionType, out var collectionName, out _))
        {
            var collection = EmitExpr(collectionMember.Object);
            var args = c.Args.Select(EmitExpr).ToList();
            return collectionName switch
            {
                "List" => collectionMember.Name switch
                {
                    "Add" => $"table.insert({collection}, __collection_store({args[0]}))",
                    "Remove" => $"__list_remove({collection}, {args[0]})",
                    "IndexOf" => $"__list_indexof({collection}, {args[0]})",
                    "Clear" => $"for __i = #{collection}, 1, -1 do {collection}[__i] = nil end",
                    _ => throw new NotSupportedException($"List.{collectionMember.Name}")
                },
                "Queue" => collectionMember.Name switch
                {
                    "Enqueue" => $"__queue_enqueue({collection}, {args[0]})",
                    "Dequeue" => $"__queue_dequeue({collection})",
                    "Peek" => $"__queue_peek({collection})",
                    _ => throw new NotSupportedException($"Queue.{collectionMember.Name}")
                },
                "Stack" => collectionMember.Name switch
                {
                    "Push" => $"__stack_push({collection}, {args[0]})",
                    "Pop" => $"__stack_pop({collection})",
                    "Peek" => $"__stack_peek({collection})",
                    _ => throw new NotSupportedException($"Stack.{collectionMember.Name}")
                },
                "Dictionary" => collectionMember.Name switch
                {
                    "Add" => $"__dictionary_add({collection}, {args[0]}, {args[1]})",
                    "Remove" => $"__dictionary_remove({collection}, {args[0]})",
                    "TryAdd" => $"__dictionary_tryadd({collection}, {args[0]}, {args[1]})",
                    "TryRemove" => $"__dictionary_tryremove({collection}, {args[0]})",
                    "ContainsKey" => $"__dictionary_containskey({collection}, {args[0]})",
                    _ => throw new NotSupportedException($"Dictionary.{collectionMember.Name}")
                },
                _ => throw new NotSupportedException($"Collection {collectionName}")
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
            var parts = c.Args
                .Select(argument => argument is StrExpr ? EmitExpr(argument) : ToStringExpr(argument))
                .ToList();
            var msg = parts.Count == 0 ? "\"\"" : string.Join(" .. ", parts);
            return $"{(_headless ? "print" : "BJDebugMsg")}({msg})";
        }

        return EmitNormalCall(c);
    }

    private string EmitNormalCall(CallExpr c)
    {
        if (c.Callee is MemberExpr qualifiedMember &&
            TryGetQualifiedName(qualifiedMember, out var qualifiedName) &&
            _qualifiedFunctions.TryGetValue(qualifiedName, out var resolvedFunction))
        {
            return $"{resolvedFunction}({string.Join(", ", c.Args.Select(EmitExpr))})";
        }

        if (c.Callee is MemberExpr mem)
        {
            var rt = RecvType(mem.Object);
            var prop = rt is null ? null : NativeTable.Find(rt, mem.Name);
            var recv = EmitExpr(mem.Object);
            var args = c.Args.Select(EmitExpr).ToList();
            if (prop?.Kind == MemberKind.Method) return Apply(prop.Read, recv, args);

            bool isDeclaredStaticMethod =
                rt is not null &&
                FindClassMethod(rt, mem.Name) is { IsStatic: true };

            if (isDeclaredStaticMethod ||
                (mem.Object is NameExpr staticOwner &&
                 !_locals.ContainsKey(staticOwner.Name) &&
                 CurrentFieldType(staticOwner.Name) is null))
                return $"{recv}.{mem.Name}({string.Join(", ", args)})";

            return $"{recv}:{mem.Name}({string.Join(", ", args)})";
        }
        if (c.Callee is NameExpr id)
        {
            var arguments = string.Join(", ", c.Args.Select(EmitExpr));
            if (_className is not null &&
                FindClassMethod(_className, id.Name) is { } classMethod)
            {
                return classMethod.IsStatic
                    ? $"{_className}.{id.Name}({arguments})"
                    : $"self:{id.Name}({arguments})";
            }

            return $"{id.Name}({arguments})";
        }
        return $"{EmitExpr(c.Callee)}({string.Join(", ", c.Args.Select(EmitExpr))})";
    }

    private static bool TryGetQualifiedName(Expr expression, out string name)
    {
        switch (expression)
        {
            case NameExpr identifier:
                name = identifier.Name;
                return true;
            case MemberExpr member when TryGetQualifiedName(member.Object, out var prefix):
                name = prefix + "." + member.Name;
                return true;
            default:
                name = string.Empty;
                return false;
        }
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

        // Dictionary keys are not zero-based sequence indices.
        if (t is not null && IsGenericType(t, "Dictionary"))
        {
            var key = EmitExpr(ix.Index);
            return $"__collection_load({obj}._items[{key}])";
        }

        // List uses a sentinel so null elements remain representable in Lua tables.
        if (t is not null && IsGenericType(t, "List"))
        {
            if (ix.Index is FromEndExpr listFromEnd)
            {
                var k = EmitExpr(listFromEnd.Inner);
                return $"__collection_load({obj}[#{obj} - {k} + 1])";
            }
            var listIndex = EmitExpr(ix.Index);
            return $"__collection_load({obj}[{listIndex} + 1])";
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
                            : "nil");
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

    private static bool IsGenericType(string type, string name) =>
        TryGetGenericArguments(type, out var genericName, out _) && genericName == name;

    private static bool TryGetGenericArguments(string type, out string genericName, out List<string> arguments)
    {
        genericName = string.Empty;
        arguments = [];
        int open = type.IndexOf('<');
        if (open <= 0 || !type.EndsWith(">", StringComparison.Ordinal))
            return false;

        genericName = type[..open];
        string content = type[(open + 1)..^1];
        int depth = 0;
        int start = 0;
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == '<') depth++;
            else if (content[i] == '>') depth--;
            else if (content[i] == ',' && depth == 0)
            {
                arguments.Add(content[start..i]);
                start = i + 1;
            }
        }
        arguments.Add(content[start..]);
        return arguments.All(argument => argument.Length > 0);
    }

    private static bool TryParseInt32Literal(string text, out int value)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            uint.TryParse(text.AsSpan(2), System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture, out uint unsignedValue))
        {
            value = unchecked((int)unsignedValue);
            return true;
        }

        return int.TryParse(text, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static string EmitNumber(string text)
    {
        if (!text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
            !TryParseInt32Literal(text, out int value))
            return text;

        string emitted = value == int.MinValue
            ? "(-2147483647 - 1)"
            : "(" + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        return emitted;
    }

    private string Op(string op) => op switch
    {
        "&&" => "and",
        "||" => "or",
        "^" => "~",
        _ => op,
    };

    private static string Esc(string s) =>
        s.Replace("\\", "\\\\")
         .Replace("\"", "\\\"")
         .Replace("\n", "\\n")
         .Replace("\r", "\\r")
         .Replace("\t", "\\t");

    private void Line(string s) => _lines.Add(new string(' ', _indent * 4) + s);
}
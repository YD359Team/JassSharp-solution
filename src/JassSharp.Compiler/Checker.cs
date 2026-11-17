using JassSharp.Runtime;
using JassSharp.Syntax;

namespace JassSharp;

public sealed record Diagnostic(int Line, int Col, string Severity, string Message, string? File = null);

public static class Checker
{
    public static List<Diagnostic> Check(
        NsUnit unit,
        string? file = null,
        IEnumerable<NsUnit>? imports = null,
        bool implicitWarcraft3 = true,
        bool strictUnknownCalls = false)
    {
        var analyzer = new Analyzer(unit, file, imports, implicitWarcraft3, strictUnknownCalls);
        analyzer.Run();
        return analyzer.Diagnostics;
    }

    private sealed class Analyzer
    {
        private readonly NsUnit _unit;
        private readonly string? _file;
        private readonly bool _hasWarcraft3;
        private readonly bool _strictUnknownCalls;
        private readonly HashSet<string> _classes = new(StringComparer.Ordinal);
        private readonly HashSet<string> _interfaces = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _qualifiedClasses = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _qualifiedInterfaces = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _classNamespaces = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _interfaceNamespaces = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string[]> _namespaceUsings = new(StringComparer.Ordinal);
        private readonly Dictionary<FnDecl, string> _declarationNamespaces = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, ClassDecl> _classDeclarations = new(StringComparer.Ordinal);
        private readonly Dictionary<string, InterfaceDecl> _interfaceDeclarations = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string?> _classBaseTypes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _classInterfaces = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FnDecl> _functions = new(StringComparer.Ordinal);
        private readonly List<(string Namespace, FnDecl Function, bool IsCurrent)> _functionSymbols = [];
        private readonly List<(string Namespace, EnumDecl Enumeration, bool IsCurrent)> _enumSymbols = [];
        private readonly List<(string Namespace, string Name, string Type, bool IsCurrent)> _globalSymbols = [];
        private readonly HashSet<string> _inlineFunctions = new(StringComparer.Ordinal);
        private readonly List<FnDecl> _hooks = [];
        private NativeSig? _hookOriginalSignature;
        private readonly Dictionary<string, string> _globals = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, string>> _classFields = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, FnDecl>> _classMethods = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<FnDecl>> _operators = new(StringComparer.Ordinal);
        private readonly List<Dictionary<string, string>> _scopes = [];
        private string? _currentClass;
        private bool _currentMethodIsStatic;
        private string _expectedReturnType = "void";
        private int _loopDepth;

        public List<Diagnostic> Diagnostics { get; } = [];

        public Analyzer(
            NsUnit unit,
            string? file,
            IEnumerable<NsUnit>? imports,
            bool implicitWarcraft3,
            bool strictUnknownCalls)
        {
            _unit = unit;
            _file = file;
            _strictUnknownCalls = strictUnknownCalls;
            var importedUnits = imports?.ToList() ?? [];
            _hasWarcraft3 = implicitWarcraft3 ||
                unit.Usings.Contains("Warcraft3", StringComparer.Ordinal) ||
                importedUnits.Any(imported => imported.Name == "Warcraft3");

            // Match LuaEmitter: imported declarations are registered first, then the
            // current namespace. The current unit is emitted after its imports and
            // therefore owns name resolution when a symbol name is shared.
            foreach (var imported in importedUnits)
                AddUnit(imported);
            AddUnit(unit);
        }

        private void AddUnit(NsUnit unit)
        {
            _namespaceUsings[unit.Name] = unit.Usings.Select(name => name.Replace('.', '_')).ToArray();
            foreach (var contract in unit.Interfaces ?? [])
            {
                string identity = GetTypeIdentity(unit.Name, contract.Name);
                _interfaces.Add(identity); _qualifiedInterfaces[identity] = identity;
                _interfaceNamespaces[identity] = unit.Name; _interfaceDeclarations[identity] = contract;
                foreach (var method in contract.Methods) _declarationNamespaces[method] = unit.Name;
            }
            foreach (var cls in unit.Classes)
            {
                string identity = GetTypeIdentity(unit.Name, cls.Name);
                _classes.Add(identity); _qualifiedClasses[identity] = identity;
                _classNamespaces[identity] = unit.Name; _classDeclarations[identity] = cls;
                _classFields[identity] = new Dictionary<string, string>(StringComparer.Ordinal);
                _classMethods[identity] = new Dictionary<string, FnDecl>(StringComparer.Ordinal);
                if (cls.Ctor is not null) _declarationNamespaces[cls.Ctor] = unit.Name;
                foreach (var method in cls.Methods) _declarationNamespaces[method] = unit.Name;
                foreach (var field in cls.Fields) _classFields[identity][field.Name] = field.Type;
                foreach (var method in cls.Methods)
                {
                    if (method.Operator is { } op)
                    {
                        string key = identity + "|" + op;
                        if (!_operators.TryGetValue(key, out var overloads)) _operators[key] = overloads = [];
                        overloads.Add(method);
                    }
                    else _classMethods[identity][method.Name] = method;
                }
            }

            foreach (var enumeration in unit.Enums)
                _enumSymbols.Add((unit.Name, enumeration, ReferenceEquals(unit, _unit)));

            foreach (var fn in unit.Functions)
            {
                _declarationNamespaces[fn] = unit.Name;
                _functionSymbols.Add((unit.Name, fn, ReferenceEquals(unit, _unit)));
                if (fn.IsHook)
                {
                    if (ReferenceEquals(unit, _unit))
                        _hooks.Add(fn);
                }
                else
                {
                    _functions[fn.Name] = fn;
                    if (fn.Attributes?.Contains("Inline") == true)
                        _inlineFunctions.Add(fn.Name);
                }
            }

            foreach (var field in unit.Fields)
            {
                _globals[field.Name] = field.Type;
                _globalSymbols.Add((unit.Name, field.Name, field.Type, ReferenceEquals(unit, _unit)));
            }
            foreach (var constant in unit.Consts)
            {
                _globals[constant.Name] = constant.Type;
                _globalSymbols.Add((unit.Name, constant.Name, constant.Type, ReferenceEquals(unit, _unit)));
            }
            foreach (var enumeration in unit.Enums)
            {
                foreach (var member in enumeration.Members)
                    _globals[member.Name] = enumeration.Name;
            }
        }

        public void Run()
        {
            ValidateInheritance();

            foreach (var field in _unit.Fields)
                ValidateDeclaredType(field.Type, 1, 1, allowVoid: false);
            foreach (var constant in _unit.Consts)
            {
                ValidateDeclaredType(constant.Type, constant.Value.Line, constant.Value.Col, allowVoid: false);
                CheckExpression(constant.Value);
                var actual = InferType(constant.Value);
                if (actual is not null && !IsCompatible(actual, constant.Type))
                    Add(constant.Value.Line, constant.Value.Col, "error",
                        $"константа {constant.Name}: ожидался тип {constant.Type}, получен {actual}");
            }
            foreach (var enumeration in _unit.Enums)
            {
                foreach (var member in enumeration.Members)
                {
                    if (member.Value is null)
                        continue;

                    CheckExpression(member.Value);
                    var actual = InferType(member.Value);
                    if (actual is not null && actual != "int")
                        Add(member.Value.Line, member.Value.Col, "error",
                            $"значение элемента enum {enumeration.Name}.{member.Name} должно иметь тип int, получен {actual}");
                }
            }

            foreach (var contract in _unit.Interfaces ?? [])
            {
                foreach (var method in contract.Methods)
                {
                    ValidateDeclaredType(method.ReturnType, 1, 1, allowVoid: true);
                    foreach (var parameter in method.Params)
                        ValidateDeclaredType(parameter.Type, 1, 1, allowVoid: false);
                }
            }

            foreach (var cls in _unit.Classes)
            {
                ValidateOperators(cls);
                foreach (var field in cls.Fields)
                    ValidateDeclaredType(field.Type, 1, 1, allowVoid: false);
                _currentClass = GetTypeIdentity(_unit.Name, cls.Name);
                if (cls.Ctor is not null) CheckFunction(cls.Ctor, _currentClass);
                foreach (var method in cls.Methods) CheckFunction(method, _currentClass);
            }

            _currentClass = null;
            foreach (var function in _unit.Functions.Where(function => !function.IsHook))
                CheckFunction(function, null);

            CheckHooks();
        }

        private void ValidateOperators(ClassDecl cls)
        {
            var declared = cls.Methods.Where(method => method.Operator is not null).ToList();
            foreach (var method in declared)
            {
                string op = method.Operator!;
                int expectedArity = op == "-" && method.Params.Count == 1 ? 1 : 2;
                if (!method.IsStatic)
                    Add(1, 1, "error", $"перегруженный оператор {op} должен быть static");
                if (method.Params.Count != expectedArity)
                    Add(1, 1, "error", $"оператор {op} должен принимать {expectedArity} параметр(а)");
                if (!method.Params.Any(parameter => ResolveQualifiedUserType(parameter.Type, _unit.Name) == GetTypeIdentity(_unit.Name, cls.Name)))
                    Add(1, 1, "error", $"оператор {op} должен принимать параметр типа {cls.Name}");
                if ((op is "==" or "!=" or "<" or ">" or "<=" or ">=") && method.ReturnType != "bool")
                    Add(1, 1, "error", $"оператор сравнения {op} должен возвращать bool");
            }

            foreach (var duplicate in declared.GroupBy(method =>
                         method.Operator + "|" + string.Join(",", method.Params.Select(parameter => parameter.Type)))
                     .Where(group => group.Count() > 1))
                Add(1, 1, "error", $"повторное объявление перегрузки оператора {duplicate.First().Operator} с той же сигнатурой");

            var operators = declared.Select(method => method.Operator!).ToHashSet(StringComparer.Ordinal);
            if (operators.Contains("==") != operators.Contains("!="))
                Add(1, 1, "error", "операторы == и != должны объявляться парой");
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

        private bool TryResolveFunction(
            string name,
            int line,
            int col,
            out FnDecl? function,
            bool reportAmbiguity = true)
        {
            List<(string Namespace, FnDecl Function, bool IsCurrent)> matches;
            if (name.Contains('.'))
            {
                int separator = name.LastIndexOf('.');
                string namespaceName = name[..separator].Replace('.', '_');
                string functionName = name[(separator + 1)..];
                matches = _functionSymbols
                    .Where(symbol => symbol.Namespace == namespaceName &&
                                     symbol.Function.Name == functionName &&
                                     !symbol.Function.IsHook)
                    .ToList();
            }
            else
            {
                var current = _functionSymbols
                    .Where(symbol => symbol.IsCurrent &&
                                     symbol.Function.Name == name &&
                                     !symbol.Function.IsHook)
                    .ToList();
                if (current.Count > 0)
                {
                    matches = current;
                }
                else
                {
                    matches = _functionSymbols
                        .Where(symbol => !symbol.IsCurrent &&
                                         _unit.Usings.Contains(symbol.Namespace, StringComparer.Ordinal) &&
                                         symbol.Function.Name == name &&
                                         !symbol.Function.IsHook)
                        .ToList();
                }
            }

            if (matches.Count == 1)
            {
                function = matches[0].Function;
                return true;
            }

            function = null;
            if (matches.Count > 1 && reportAmbiguity)
            {
                string candidates = string.Join(", ", matches
                    .Select(match => match.Namespace + "." + match.Function.Name)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(candidate => candidate, StringComparer.Ordinal));
                Add(line, col, "error", $"неоднозначный вызов {name}: имя доступно из нескольких namespace ({candidates}); укажите полное имя");
            }

            return matches.Count > 0;
        }

        private bool TryResolveQualifiedGlobal(string name, out string type)
        {
            type = string.Empty;
            int separator = name.LastIndexOf('.');
            if (separator <= 0)
                return false;

            string namespaceName = name[..separator].Replace('.', '_');
            string symbolName = name[(separator + 1)..];
            var matches = _globalSymbols
                .Where(symbol => symbol.Namespace == namespaceName && symbol.Name == symbolName)
                .ToList();
            if (matches.Count != 1)
                return false;

            type = ResolveQualifiedUserType(matches[0].Type, matches[0].Namespace);
            return true;
        }

        private bool TryResolveQualifiedEnumMember(string name, out string enumName)
        {
            enumName = string.Empty;
            int memberSeparator = name.LastIndexOf('.');
            if (memberSeparator <= 0)
                return false;
            int enumSeparator = name.LastIndexOf('.', memberSeparator - 1);
            if (enumSeparator <= 0)
                return false;

            string namespaceName = name[..enumSeparator].Replace('.', '_');
            string declaredEnumName = name[(enumSeparator + 1)..memberSeparator];
            string memberName = name[(memberSeparator + 1)..];
            var matches = _enumSymbols
                .Where(symbol => symbol.Namespace == namespaceName &&
                                 symbol.Enumeration.Name == declaredEnumName &&
                                 symbol.Enumeration.Members.Any(member => member.Name == memberName))
                .ToList();
            if (matches.Count != 1)
                return false;
            enumName = matches[0].Enumeration.Name;
            return true;
        }

        private void CheckHooks()
        {
            foreach (var hook in _hooks)
            {
                if (hook.Name == "OnInit")
                {
                    Add(1, 1, "error", "нельзя перехватывать OnInit: точка входа компилируется под отдельным именем");
                    continue;
                }

                if (_hasWarcraft3 && hook.Name is "main" or "config")
                {
                    Add(1, 1, "error",
                        $"нельзя перехватывать {hook.Name}: точка входа карты управляется сборщиком JASS#");
                    continue;
                }

                NativeSig? targetSignature = null;
                if (_functions.TryGetValue(hook.Name, out var target))
                {
                    targetSignature = new NativeSig(target.Name, target.ReturnType,
                        target.Params.Select(parameter => parameter.Type).ToArray(), "");
                    if (_inlineFunctions.Contains(hook.Name))
                    {
                        Add(1, 1, "error",
                            $"нельзя перехватывать inline-функцию {hook.Name}: вызовы могут быть встроены в вызывающий код");
                    }
                }
                else if (_hasWarcraft3 && NativeSigs.Map.TryGetValue(hook.Name, out var native))
                {
                    targetSignature = native;
                }

                if (targetSignature is null)
                {
                    Add(1, 1, "error",
                        $"нельзя объявить hook для неизвестной функции {hook.Name}; для нативных функций добавьте using Warcraft3");
                }
                else if (hook.ReturnType != targetSignature.ReturnType ||
                         !hook.Params.Select(parameter => parameter.Type)
                             .SequenceEqual(targetSignature.Params, StringComparer.Ordinal))
                {
                    string expected = $"{targetSignature.ReturnType} {hook.Name}({string.Join(", ", targetSignature.Params)})";
                    string actual = $"{hook.ReturnType} {hook.Name}({string.Join(", ", hook.Params.Select(parameter => parameter.Type))})";
                    Add(1, 1, "error", $"сигнатура hook не совпадает с целевой функцией: ожидалась {expected}, получена {actual}");
                }

                _hookOriginalSignature = targetSignature ?? new NativeSig(
                    hook.Name, hook.ReturnType, hook.Params.Select(parameter => parameter.Type).ToArray(), "");
                CheckFunction(hook, null);
                _hookOriginalSignature = null;
            }
        }

        private void CheckFunction(FnDecl function, string? className)
        {
            ValidateDeclaredType(function.ReturnType, 1, 1, allowVoid: true);
            foreach (var parameter in function.Params)
                ValidateDeclaredType(parameter.Type, 1, 1, allowVoid: false);

            var previousClass = _currentClass;
            var previousIsStatic = _currentMethodIsStatic;
            var previousReturn = _expectedReturnType;
            _currentClass = className;
            _currentMethodIsStatic = className is not null && function.IsStatic;
            _expectedReturnType = function.ReturnType;

            PushScope();
            if (_hookOriginalSignature is not null)
                Declare("original", "function", 1, 1);
            foreach (var parameter in function.Params)
                Declare(parameter.Name, parameter.Type, 1, 1, reportDuplicate: _hookOriginalSignature is not null);

            CheckStatements(function.Body);
            PopScope();

            _currentClass = previousClass;
            _currentMethodIsStatic = previousIsStatic;
            _expectedReturnType = previousReturn;
        }

        private void CheckStatements(IEnumerable<Stmt> statements)
        {
            foreach (var statement in statements)
                CheckStatement(statement);
        }

        private void CheckStatement(Stmt statement)
        {
            switch (statement)
            {
                case BreakStmt jump:
                    if (_loopDepth == 0)
                        Add(jump.Line, jump.Col, "error", "break доступен только внутри цикла");
                    break;

                case ContinueStmt jump:
                    if (_loopDepth == 0)
                        Add(jump.Line, jump.Col, "error", "continue доступен только внутри цикла");
                    break;

                case VarStmt variable:
                {
                    string declaredType = variable.Type;
                    if (variable.Init is not null)
                    {
                        CheckExpression(variable.Init);
                        var actual = InferType(variable.Init);
                        if (declaredType == "var")
                        {
                            if (actual is null || actual == "null" || actual == "void")
                            {
                                Add(variable.Line, variable.Col, "error",
                                    $"невозможно вывести тип переменной {variable.Name} из выражения инициализации");
                                declaredType = "?";
                            }
                            else
                            {
                                declaredType = actual;
                            }
                        }
                        else if (actual is not null && !IsCompatible(actual, declaredType))
                        {
                            Add(variable.Line, variable.Col, "error",
                                $"переменная {variable.Name}: ожидался тип {declaredType}, получен {actual}");
                        }
                    }
                    else if (declaredType == "var")
                    {
                        Add(variable.Line, variable.Col, "error",
                            $"переменная var {variable.Name} должна иметь выражение инициализации");
                        declaredType = "?";
                    }

                    if (declaredType != "?" && !IsKnownType(declaredType, allowVoid: false))
                    {
                        string resolvedType = ResolveQualifiedUserType(declaredType);
                        if (resolvedType.StartsWith("?ambiguous:", StringComparison.Ordinal))
                            Add(variable.Line, variable.Col, "error",
                                $"неоднозначный тип {declaredType}: имя доступно из нескольких namespace; укажите полное имя");
                        else
                            Add(variable.Line, variable.Col, "error",
                                $"неизвестный тип {declaredType}; для типов Warcraft III добавьте using Warcraft3");
                    }

                    Declare(variable.Name, declaredType, variable.Line, variable.Col);
                    break;
                }

                case AssignStmt assignment:
                {
                    CheckExpression(assignment.Target);
                    CheckExpression(assignment.Value);

                    var targetType = InferType(assignment.Target);
                    var valueType = InferType(assignment.Value);
                    if (targetType is not null && valueType is not null &&
                        !IsCompatible(valueType, targetType))
                    {
                        Add(assignment.Value.Line, assignment.Value.Col, "error",
                            $"несовместимое присваивание: ожидался тип {targetType}, получен {valueType}");
                    }

                    if (assignment.Target is not (NameExpr or MemberExpr or IndexExpr))
                        Add(assignment.Target.Line, assignment.Target.Col, "error", "выражение не может быть целью присваивания");
                    break;
                }

                case ExprStmt expression:
                    CheckExpression(expression.Expr);
                    break;

                case ReturnStmt result:
                    if (result.Expr is null)
                    {
                        if (_expectedReturnType != "void" && _expectedReturnType != "?")
                            Add(1, 1, "error", $"ожидалось возвращаемое значение типа {_expectedReturnType}");
                    }
                    else
                    {
                        CheckExpression(result.Expr);
                        var actual = InferType(result.Expr);
                        if (_expectedReturnType == "void")
                            Add(result.Expr.Line, result.Expr.Col, "error", "функция с типом void не должна возвращать значение");
                        else if (_expectedReturnType != "?" && actual is not null && !IsCompatible(actual, _expectedReturnType))
                            Add(result.Expr.Line, result.Expr.Col, "error",
                                $"return: ожидался тип {_expectedReturnType}, получен {actual}");
                    }
                    break;

                case IfStmt conditional:
                    CheckCondition(conditional.Cond);
                    PushScope();
                    CheckStatements(conditional.Then);
                    PopScope();
                    if (conditional.Else is not null)
                    {
                        PushScope();
                        CheckStatements(conditional.Else);
                        PopScope();
                    }
                    break;

                case WhileStmt loop:
                    CheckCondition(loop.Cond);
                    PushScope();
                    _loopDepth++;
                    CheckStatements(loop.Body);
                    _loopDepth--;
                    PopScope();
                    break;

                case DoWhileStmt loop:
                    PushScope();
                    _loopDepth++;
                    CheckStatements(loop.Body);
                    _loopDepth--;
                    PopScope();
                    CheckCondition(loop.Cond);
                    break;

                case RawLuaStmt:
                    break;

                case ForeachStmt loop:
                {
                    CheckExpression(loop.Collection);
                    string? collectionType = InferType(loop.Collection);
                    string? elementType = GetForeachElementType(collectionType);
                    string itemType = loop.Type;
                    if (elementType is null)
                    {
                        if (itemType == "var")
                            Add(loop.Line, loop.Col, "error",
                                $"не удалось вывести тип элемента foreach для коллекции {collectionType ?? "?"}; поддерживаются массивы и group с using Warcraft3");
                        else
                            Add(loop.Line, loop.Col, "error",
                                $"foreach не поддерживает коллекцию типа {collectionType ?? "?"}; поддерживаются массивы и group с using Warcraft3");
                        itemType = itemType == "var" ? "?" : itemType;
                    }
                    else if (itemType == "var")
                    {
                        itemType = elementType;
                    }
                    else
                    {
                        if (!IsKnownType(itemType, allowVoid: false))
                            Add(loop.Line, loop.Col, "error", $"неизвестный тип элемента foreach: {itemType}");
                        if (!IsCompatible(elementType, itemType))
                            Add(loop.Line, loop.Col, "error",
                                $"foreach: ожидался элемент типа {itemType}, коллекция содержит {elementType}");
                    }

                    PushScope();
                    Declare(loop.Name, itemType, loop.Line, loop.Col);
                    _loopDepth++;
                    CheckStatements(loop.Body);
                    _loopDepth--;
                    PopScope();
                    break;
                }

                case ForStmt loop:
                    PushScope();
                    if (loop.Init is not null)
                        CheckStatement(loop.Init);
                    if (loop.Cond is not null)
                        CheckCondition(loop.Cond);
                    if (loop.Step is not null)
                        CheckStatement(loop.Step);
                    _loopDepth++;
                    CheckStatements(loop.Body);
                    _loopDepth--;
                    PopScope();
                    break;

            }
        }

        private string? GetForeachElementType(string? collectionType)
        {
            if (collectionType is null)
                return null;
            if (collectionType.EndsWith("[]", StringComparison.Ordinal))
                return collectionType[..^2];
            if (collectionType == "group")
                return _hasWarcraft3 ? "unit" : null;
            if (TryGetGenericArguments(collectionType, out var collectionName, out var arguments))
            {
                if (collectionName is "List" or "Queue" or "Stack")
                    return arguments[0];
                if (collectionName == "Dictionary")
                    return $"KeyValuePair<{arguments[0]},{arguments[1]}>";
            }
            return null;
        }

        private void CheckCondition(Expr condition)
        {
            CheckExpression(condition);
            var type = InferType(condition);
            if (type is not null && type != "bool")
                Add(condition.Line, condition.Col, "error", $"условие должно иметь тип bool, получен {type}");
        }

        private void CheckExpression(Expr expression)
        {
            switch (expression)
            {
                case CallExpr call:
                    CheckCall(call);
                    break;

                case LambdaExpr lambda:
                    PushScope();
                    foreach (var parameter in lambda.Params)
                        Declare(parameter.Name, parameter.Type, lambda.Line, lambda.Col, reportDuplicate: false);
                    var previousReturn = _expectedReturnType;
                    var previousLoopDepth = _loopDepth;
                    _expectedReturnType = "?";
                    _loopDepth = 0;
                    CheckStatements(lambda.Body);
                    _expectedReturnType = previousReturn;
                    _loopDepth = previousLoopDepth;
                    PopScope();
                    break;

                case NameExpr name:
                    if (!TryLookup(name.Name, out _) && TryGetCurrentField(name.Name, out _))
                    {
                        if (_currentMethodIsStatic)
                            Add(name.Line, name.Col, "error", $"поле экземпляра {name.Name} недоступно внутри static-метода");
                    }
                    else if (!TryLookup(name.Name, out _) && IsWarcraftSymbol(name.Name) && !_hasWarcraft3)
                    {
                        Add(name.Line, name.Col, "error", $"{name.Name} доступна только после using Warcraft3");
                    }
                    else if (!TryLookup(name.Name, out _) && !IsKnownSymbol(name.Name))
                    {
                        Add(name.Line, name.Col, "warning", $"неизвестное имя {name.Name}");
                    }
                    break;

                case NumExpr number:
                    if (number.Text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!uint.TryParse(number.Text.AsSpan(2), System.Globalization.NumberStyles.AllowHexSpecifier,
                                System.Globalization.CultureInfo.InvariantCulture, out _))
                            Add(number.Line, number.Col, "error", "hex-литерал выходит за пределы 32-битного значения");
                    }
                    else if (!number.Text.Contains('.') && !number.Text.Contains('e') && !number.Text.Contains('E') &&
                             (!long.TryParse(number.Text, System.Globalization.NumberStyles.None,
                                  System.Globalization.CultureInfo.InvariantCulture, out long integerValue) ||
                              integerValue > int.MaxValue))
                    {
                        Add(number.Line, number.Col, "error", "целочисленный литерал выходит за пределы int32");
                    }
                    else if ((number.Text.Contains('.') || number.Text.Contains('e') || number.Text.Contains('E')) &&
                             (!double.TryParse(number.Text, System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out double floatingValue) ||
                              !double.IsFinite(floatingValue) ||
                              floatingValue > float.MaxValue || floatingValue < -float.MaxValue ||
                              (floatingValue != 0.0 && (float)floatingValue == 0.0f)))
                    {
                        Add(number.Line, number.Col, "error", "числовой литерал выходит за пределы float32");
                    }
                    break;

                case InterpolatedExpr interpolated:
                    foreach (var part in interpolated.Parts)
                        if (part.Expression is not null)
                            CheckExpression(part.Expression);
                    break;

                case ThisExpr self:
                    if (_currentClass is null)
                        Add(self.Line, self.Col, "error", "this доступен только внутри метода или конструктора");
                    else if (_currentMethodIsStatic)
                        Add(self.Line, self.Col, "error", "this недоступен внутри static-метода");
                    break;

                case NewExpr creation:
                    string resolvedCreationType = ResolveQualifiedUserType(creation.Type);
                    if (_interfaces.Contains(resolvedCreationType))
                        Add(creation.Line, creation.Col, "error", $"interface {creation.Type} cannot be instantiated");
                    else if (TryGetGenericArguments(creation.Type, out var constructedCollection, out _) &&
                        constructedCollection == "KeyValuePair")
                        Add(creation.Line, creation.Col, "error", "KeyValuePair can only be obtained by iterating a Dictionary");
                    else if (TryGetGenericArguments(creation.Type, out constructedCollection, out _) &&
                        (constructedCollection is "List" or "Queue" or "Stack" or "Dictionary") &&
                        creation.Args.Count != 0)
                        Add(creation.Line, creation.Col, "error",
                            $"{constructedCollection} does not accept constructor arguments");

                    if (!IsKnownType(creation.Type, allowVoid: false))
                    {
                        string baseType = StripArrayRanks(creation.Type);
                        if (NativeCatalog.Types.Contains(baseType) || NativeTable.Constructors.ContainsKey(baseType))
                            Add(creation.Line, creation.Col, "error",
                                $"тип {baseType} доступен только после using Warcraft3");
                        else
                            Add(creation.Line, creation.Col, "warning", $"неизвестный тип {creation.Type}");
                    }

                    foreach (var argument in creation.Args)
                        CheckExpression(argument);
                    break;

                case MemberExpr member:
                    if (TryGetQualifiedName(member, out var qualifiedMember) &&
                        (TryResolveQualifiedEnumMember(qualifiedMember, out _) ||
                         TryResolveQualifiedGlobal(qualifiedMember, out _)))
                        break;
                    CheckExpression(member.Object);
                    if (InferType(member.Object) is { } receiverType &&
                        !HasMember(receiverType, member.Name))
                    {
                        string severity = member.Name == "HandleId" ? "error" : "warning";
                        Add(member.Line, member.Col, severity,
                            $"тип {receiverType} не содержит члена {member.Name}");
                    }
                    break;

                case IndexExpr index:
                    CheckExpression(index.Object);
                    CheckExpression(index.Index);
                    var indexType = InferType(index.Index);
                    var indexedType = InferType(index.Object);

                    bool invalidFromEndReceiver = false;
                    if (index.Index is FromEndExpr fromEndIndex)
                    {
                        var offsetType = InferType(fromEndIndex.Inner);
                        if (offsetType is not null && offsetType != "int")
                            Add(fromEndIndex.Inner.Line, fromEndIndex.Inner.Col, "error",
                                $"индекс с конца требует смещение типа int, получен {offsetType}");

                        bool supportsFromEnd = indexedType == "string" ||
                            indexedType is not null && indexedType.EndsWith("[]", StringComparison.Ordinal);
                        if (indexedType is not null &&
                            TryGetGenericArguments(indexedType, out var fromEndCollectionName, out _))
                            supportsFromEnd = fromEndCollectionName == "List";

                        if (indexedType is not null && !supportsFromEnd)
                        {
                            invalidFromEndReceiver = true;
                            Add(index.Line, index.Col, "error",
                                $"индексация с конца не поддерживается для типа {indexedType}");
                        }
                    }

                    bool dictionaryIndexer = indexedType is not null &&
                        TryGetGenericArguments(indexedType, out var dictionaryCollectionName, out _) &&
                        dictionaryCollectionName == "Dictionary";
                    if (!dictionaryIndexer && indexType is not null && indexType != "int")
                        Add(index.Index.Line, index.Index.Col, "error",
                            $"индекс должен иметь тип int, получен {indexType}");
                    bool supportsIndex = indexedType == "string" ||
                        indexedType is not null && indexedType.EndsWith("[]", StringComparison.Ordinal);
                    if (indexedType is not null && TryGetGenericArguments(indexedType, out var indexedName, out _))
                        supportsIndex = indexedName is "List" or "Dictionary";
                    if (indexedType is not null && !supportsIndex && !invalidFromEndReceiver)
                        Add(index.Line, index.Col, "error", $"тип {indexedType} не поддерживает индексатор");
                    if (indexedType is not null && TryGetGenericArguments(indexedType, out var dictionaryName, out var dictionaryArgs) &&
                        dictionaryName == "Dictionary")
                    {
                        var keyType = InferType(index.Index);
                        if (keyType == "null")
                            Add(index.Index.Line, index.Index.Col, "error", "ключ Dictionary не может быть null");
                        else if (keyType is not null && !IsCompatible(keyType, dictionaryArgs[0]))
                            Add(index.Index.Line, index.Index.Col, "error",
                                $"ключ Dictionary: ожидался тип {dictionaryArgs[0]}, получен {keyType}");
                    }
                    break;

                case FromEndExpr fromEnd:
                    CheckExpression(fromEnd.Inner);
                    break;

                case BinaryExpr binary:
                    CheckExpression(binary.Left);
                    CheckExpression(binary.Right);
                    var leftType = InferType(binary.Left);
                    var rightType = InferType(binary.Right);
                    bool isNullComparison = binary.Op is "==" or "!=" &&
                        (binary.Left is NullExpr || binary.Right is NullExpr);
                    var binaryOverload = isNullComparison
                        ? null
                        : FindOperator(leftType, rightType, binary.Op, [binary.Left, binary.Right]);
                    if (binaryOverload is not null)
                    {
                        CheckMethodArguments(binary, [binary.Left, binary.Right], binaryOverload);
                    }
                    else if (!isNullComparison && (IsUserType(leftType) || IsUserType(rightType)))
                    {
                        Add(binary.Line, binary.Col, "error", $"оператор {binary.Op} не определён для типов {leftType ?? "?"} и {rightType ?? "?"}");
                    }
                    else if (binary.Op is "+" or "-" or "*" or "/" or "%")
                    {
                        bool stringConcatenation = binary.Op == "+" && (leftType == "string" || rightType == "string");
                        bool numericOperands = leftType is "int" or "float" && rightType is "int" or "float";
                        if (!stringConcatenation && !numericOperands && leftType is not null && rightType is not null)
                            Add(binary.Line, binary.Col, "error",
                                $"оператор {binary.Op} требует числовые операнды, получены {leftType} и {rightType}");
                    }
                    else if (binary.Op is "<<" or ">>" or "|" or "&" or "^")
                    {
                        if (leftType is not null && rightType is not null &&
                            (leftType != "int" || rightType != "int"))
                            Add(binary.Line, binary.Col, "error",
                                $"оператор {binary.Op} требует два операнда типа int, получены {leftType} и {rightType}");
                    }
                    break;

                case UnaryExpr unary:
                {
                    bool isInt32Minimum = unary.Op == "-" &&
                        unary.Inner is NumExpr { Text: "2147483648" };
                    if (!isInt32Minimum)
                        CheckExpression(unary.Inner);
                    var unaryOverload = FindOperator(InferType(unary.Inner), null, unary.Op, [unary.Inner]);
                    if (unaryOverload is not null)
                        CheckMethodArguments(unary, [unary.Inner], unaryOverload);
                    else if (IsUserType(InferType(unary.Inner)))
                        Add(unary.Line, unary.Col, "error", $"унарный оператор {unary.Op} не определён для типа {InferType(unary.Inner)}");
                    break;
                }
            }
        }

        private void CheckCall(CallExpr call)
        {
            if (call.Callee is MemberExpr
                {
                    Object: NameExpr { Name: "Lua" },
                    Name: "Raw"
                } rawLua)
            {
                Add(rawLua.Line, rawLua.Col, "error",
                    "Lua.Raw можно использовать только как отдельную инструкцию с одним строковым литералом");
                foreach (var argument in call.Args)
                    CheckExpression(argument);
                return;
            }

            if (TryGetQualifiedName(call.Callee, out var qualifiedName) &&
                qualifiedName.Contains('.') &&
                TryResolveFunction(qualifiedName, call.Line, call.Col, out var qualifiedFunction))
            {
                if (qualifiedFunction is not null)
                    CheckMethodArguments(call.Callee, call.Args, qualifiedFunction);
                else
                    foreach (var argument in call.Args)
                        CheckExpression(argument);
                return;
            }

            if (call.Callee is NameExpr name)
            {
                if (name.Name == "original")
                {
                    if (_hookOriginalSignature is { } originalSignature)
                        CheckArguments(name, call.Args, originalSignature.Params);
                    else
                    {
                        Add(name.Line, name.Col, "error", "original доступен только внутри hook");
                        foreach (var argument in call.Args)
                            CheckExpression(argument);
                    }
                    return;
                }
                if (_currentClass is not null &&
                    TryGetClassMethod(_currentClass, name.Name, out var currentClassMethod))
                {
                    CheckMethodArguments(name, call.Args, currentClassMethod);
                    if (!currentClassMethod.IsStatic && _currentMethodIsStatic)
                    {
                        Add(name.Line, name.Col, "error",
                            $"нестатический метод {_currentClass}.{name.Name} нельзя вызывать из статического метода без экземпляра");
                    }
                }
                else if (TryResolveFunction(name.Name, name.Line, name.Col, out var function))
                {
                    if (function is not null)
                        CheckMethodArguments(name, call.Args, function);
                    else
                        foreach (var argument in call.Args)
                            CheckExpression(argument);
                }
                else if (_hasWarcraft3 && NativeSigs.Map.TryGetValue(name.Name, out var native))
                {
                    CheckArguments(name, call.Args, native.Params);
                }
                else
                {
                    if (IsWarcraftSymbol(name.Name) && !_hasWarcraft3)
                        Add(name.Line, name.Col, "error", $"{name.Name} доступна только после using Warcraft3");
                    else if (!IsKnownSymbol(name.Name))
                        Add(name.Line, name.Col, _strictUnknownCalls ? "error" : "warning",
                            $"неизвестный вызов {name.Name}");

                    foreach (var argument in call.Args)
                        CheckExpression(argument);
                }

                return;
            }

            if (call.Callee is not MemberExpr member)
            {
                CheckExpression(call.Callee);
                foreach (var argument in call.Args)
                    CheckExpression(argument);
                return;
            }

            if (TryGetQualifiedName(member, out var qualifiedTypeMethod) &&
                TryResolveQualifiedClassMethod(qualifiedTypeMethod, out var qualifiedClassName, out var qualifiedStaticMethod))
            {
                CheckMethodArguments(member, call.Args, qualifiedStaticMethod);
                if (!qualifiedStaticMethod.IsStatic)
                    Add(member.Line, member.Col, "error",
                        $"нестатический метод {qualifiedClassName}.{member.Name} нельзя вызывать через имя класса");
                return;
            }

            CheckExpression(member.Object);
            var receiverType = InferType(member.Object);

            if (receiverType is not null &&
                TryGetClassMethod(receiverType, member.Name, out var instanceMethod))
            {
                CheckMethodArguments(member, call.Args, instanceMethod);
                if (instanceMethod.IsStatic)
                    Add(member.Line, member.Col, "error",
                        $"статический метод {receiverType}.{member.Name} необходимо вызывать через имя класса");
                return;
            }

            if (receiverType is not null && _interfaces.Contains(receiverType) &&
                TryGetInterfaceMethod(receiverType, member.Name, out var interfaceMethod))
            {
                CheckMethodArguments(member, call.Args, interfaceMethod);
                return;
            }

            if (member.Object is NameExpr className &&
                !_scopes.Any(scope => scope.ContainsKey(className.Name)) &&
                TryGetClassMethod(ResolveQualifiedUserType(className.Name), member.Name, out var classMethod))
            {
                CheckMethodArguments(member, call.Args, classMethod);
                if (!classMethod.IsStatic)
                    Add(member.Line, member.Col, "error",
                        $"нестатический метод {className.Name}.{member.Name} нельзя вызывать через имя класса");
                return;
            }

            if (receiverType is not null && TryGetGenericArguments(receiverType, out var collectionName, out var collectionArgs) &&
                CheckCollectionCall(member, call.Args, collectionName, collectionArgs))
                return;

            if (receiverType is not null)
            {
                var nativeMember = _hasWarcraft3 ? NativeTable.Find(receiverType, member.Name) : null;
                if (nativeMember?.Kind == MemberKind.Method)
                {
                    var nativeName = GetNativeName(nativeMember.Read);
                    if (nativeName is not null && _hasWarcraft3 && NativeSigs.Map.TryGetValue(nativeName, out var nativeMethod))
                    {
                        CheckArguments(member, call.Args, nativeMethod.Params);
                        return;
                    }
                }
                else if (nativeMember is null)
                {
                    Add(member.Line, member.Col, _strictUnknownCalls ? "error" : "warning",
                        $"тип {receiverType} не содержит метода {member.Name}");
                }
            }

            foreach (var argument in call.Args)
                CheckExpression(argument);
        }

        private void CheckMethodArguments(Expr callee, IReadOnlyList<Expr> arguments, FnDecl method)
        {
            string ownerNamespace = _declarationNamespaces.GetValueOrDefault(method, _unit.Name);
            CheckArguments(callee, arguments, method.Params.Select(parameter => ResolveQualifiedUserType(parameter.Type, ownerNamespace)).ToArray());
        }

        private void CheckArguments(Expr callee, IReadOnlyList<Expr> arguments, IReadOnlyList<string> parameters)
        {
            if (arguments.Count != parameters.Count)
            {
                Add(callee.Line, callee.Col, "error",
                    $"ожидалось {parameters.Count} аргументов, получено {arguments.Count}");
            }

            var count = Math.Min(arguments.Count, parameters.Count);
            for (var i = 0; i < count; i++)
            {
                CheckExpression(arguments[i]);
                var actual = InferType(arguments[i]);
                var expected = parameters[i];
                if (actual is not null && !IsCompatible(actual, expected))
                    Add(arguments[i].Line, arguments[i].Col, "error",
                        $"аргумент {i + 1}: ожидался тип {expected}, получен {actual}");
            }

            for (var i = count; i < arguments.Count; i++)
                CheckExpression(arguments[i]);
        }

        private string? InferType(Expr expression)
        {
            switch (expression)
            {
                case NameExpr name:
                    if (TryLookup(name.Name, out var localType))
                        return localType;
                    if (TryGetCurrentField(name.Name, out var fieldType))
                        return fieldType;
                    return _globals.TryGetValue(name.Name, out var globalType)
                        ? ResolveQualifiedUserType(globalType)
                        : null;

                case ThisExpr:
                    return _currentClass;

                case NumExpr number:
                    return !number.Text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                           (number.Text.Contains('.') || number.Text.Contains('e') || number.Text.Contains('E'))
                        ? "float"
                        : "int";

                case BoolExpr:
                    return "bool";
                case StrExpr:
                case InterpolatedExpr:
                    return "string";
                case RawExpr:
                    return "int";
                case NullExpr:
                    return "null";
                case NewExpr creation:
                    return ResolveQualifiedUserType(creation.Type);

                case CallExpr { Callee: NameExpr { Name: "original" } } when _hookOriginalSignature is { } originalSignature:
                    return originalSignature.ReturnType;

                case CallExpr { Callee: NameExpr name }:
                    if (TryResolveFunction(name.Name, name.Line, name.Col, out var function, reportAmbiguity: false) &&
                        function is not null)
                        return ResolveQualifiedUserType(function.ReturnType, _declarationNamespaces.GetValueOrDefault(function, _unit.Name));
                    return _hasWarcraft3 && NativeSigs.Map.TryGetValue(name.Name, out var native) ? native.ReturnType : null;

                case CallExpr { Callee: MemberExpr member }:
                {
                    if (TryGetQualifiedName(member, out var qualifiedName) &&
                        qualifiedName.Contains('.') &&
                        TryResolveFunction(qualifiedName, member.Line, member.Col, out var qualifiedFunction, reportAmbiguity: false) &&
                        qualifiedFunction is not null)
                        return ResolveQualifiedUserType(qualifiedFunction.ReturnType, _declarationNamespaces.GetValueOrDefault(qualifiedFunction, _unit.Name));
                    if (TryGetQualifiedName(member, out var qualifiedTypeMethod) &&
                        TryResolveQualifiedClassMethod(qualifiedTypeMethod, out _, out var qualifiedStaticMethod) &&
                        qualifiedStaticMethod.IsStatic)
                        return ResolveQualifiedUserType(qualifiedStaticMethod.ReturnType, _declarationNamespaces.GetValueOrDefault(qualifiedStaticMethod, _unit.Name));
                    if (member.Object is NameExpr className &&
                        TryGetClassMethod(ResolveQualifiedUserType(className.Name), member.Name, out var staticMethod) &&
                        staticMethod.IsStatic)
                        return ResolveQualifiedUserType(staticMethod.ReturnType, _declarationNamespaces.GetValueOrDefault(staticMethod, _unit.Name));

                    var receiverType = InferType(member.Object);
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

                    if (TryGetClassMethod(receiverType, member.Name, out var method))
                        return ResolveQualifiedUserType(method.ReturnType, _declarationNamespaces.GetValueOrDefault(method, _unit.Name));

                    if (_interfaces.Contains(receiverType) &&
                        TryGetInterfaceMethod(receiverType, member.Name, out var interfaceMethod))
                        return ResolveQualifiedUserType(interfaceMethod.ReturnType, _declarationNamespaces.GetValueOrDefault(interfaceMethod, _unit.Name));

                    var nativeMember = _hasWarcraft3 ? NativeTable.Find(receiverType, member.Name) : null;
                    if (nativeMember?.Kind != MemberKind.Method)
                        return null;

                    var nativeName = GetNativeName(nativeMember.Read);
                    return nativeName is not null && _hasWarcraft3 && NativeSigs.Map.TryGetValue(nativeName, out var native2)
                        ? native2.ReturnType
                        : null;
                }

                case MemberExpr member:
                {
                    if (TryGetQualifiedName(member, out var qualifiedMember))
                    {
                        if (TryResolveQualifiedEnumMember(qualifiedMember, out var enumName))
                            return enumName;
                        if (TryResolveQualifiedGlobal(qualifiedMember, out var qualifiedGlobalType))
                            return qualifiedGlobalType;
                    }
                    var receiverType = InferType(member.Object);
                    if (receiverType is null)
                        return null;

                    if (member.Name == "HandleId" && Warcraft3TypeHierarchy.IsHandleType(receiverType))
                        return "int";

                    if (member.Name == "Length" &&
                        (receiverType == "string" || receiverType.EndsWith("[]", StringComparison.Ordinal)))
                        return "int";

                    if (member.Name == "Count" && IsCollectionType(receiverType))
                        return "int";

                    if (TryGetGenericArguments(receiverType, out var pairCollection, out var pairArgs) &&
                        pairCollection == "KeyValuePair" && pairArgs.Count == 2)
                        return member.Name switch { "Key" => pairArgs[0], "Value" => pairArgs[1], _ => null };

                    if (TryGetClassField(receiverType, member.Name, out var fieldType2))
                        return fieldType2;

                    var nativeMember = _hasWarcraft3 ? NativeTable.Find(receiverType, member.Name) : null;
                    if (nativeMember is null || nativeMember.Kind == MemberKind.Method)
                        return null;

                    var nativeName = GetNativeName(nativeMember.Read);
                    return nativeName is not null && _hasWarcraft3 && NativeSigs.Map.TryGetValue(nativeName, out var native2)
                        ? native2.ReturnType
                        : null;
                }

                case IndexExpr index:
                {
                    var indexedType = InferType(index.Object);
                    if (indexedType == "string") return "string";
                    if (indexedType is not null && indexedType.EndsWith("[]", StringComparison.Ordinal))
                        return indexedType[..^2];
                    if (indexedType is not null && TryGetGenericArguments(indexedType, out var collectionName, out var collectionArgs))
                    {
                        if (collectionName == "List") return collectionArgs[0];
                        if (collectionName == "Dictionary") return collectionArgs[1];
                    }
                    return null;
                }

                case UnaryExpr unary:
                {
                    var overload = FindOperator(InferType(unary.Inner), null, unary.Op, [unary.Inner]);
                    if (overload is not null)
                        return ResolveQualifiedUserType(overload.ReturnType, _declarationNamespaces.GetValueOrDefault(overload, _unit.Name));
                    return unary.Op switch
                    {
                        "!" or "not" => "bool",
                        _ => InferType(unary.Inner)
                    };
                }

                case BinaryExpr binary:
                {
                    bool isNullComparison = binary.Op is "==" or "!=" &&
                        (binary.Left is NullExpr || binary.Right is NullExpr);
                    var overload = isNullComparison
                        ? null
                        : FindOperator(InferType(binary.Left), InferType(binary.Right), binary.Op, [binary.Left, binary.Right]);
                    if (overload is not null)
                        return ResolveQualifiedUserType(overload.ReturnType, _declarationNamespaces.GetValueOrDefault(overload, _unit.Name));

                    var left = InferType(binary.Left);
                    var right = InferType(binary.Right);
                    if (binary.Op is "==" or "!=" or "<" or ">" or "<=" or ">=" or "and" or "or" or "&&" or "||")
                        return "bool";
                    if (binary.Op is "<<" or ">>" or "|" or "&" or "^")
                        return "int";
                    if (binary.Op == "+" && (left == "string" || right == "string"))
                        return "string";
                    if (left == "float" || right == "float")
                        return "float";
                    if (left == "int" && right == "int")
                        return "int";
                    return null;
                }

                default:
                    return null;
            }
        }

        private bool IsUserType(string? type) =>
            type is not null && (_classes.Contains(type) || _interfaces.Contains(type));

        private FnDecl? FindOperator(string? leftType, string? rightType, string op, IReadOnlyList<Expr> arguments)
        {
            FnDecl? best = null;
            int bestScore = int.MaxValue;

            IEnumerable<string> owners = new[] { leftType, rightType }
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal);

            foreach (string owner in owners)
            {
                if (!_operators.TryGetValue(owner + "|" + op, out var overloads))
                    continue;

                foreach (var overload in overloads)
                {
                    if (!overload.IsStatic || overload.Params.Count != arguments.Count)
                        continue;

                    int score = 0;
                    bool compatible = true;
                    int ownerSeparator = owner.LastIndexOf('.');
                    string ownerNamespace = ownerSeparator > 0 ? owner[..ownerSeparator] : _unit.Name;
                    for (int i = 0; i < arguments.Count; i++)
                    {
                        var actual = InferType(arguments[i]);
                        var expected = ResolveQualifiedUserType(overload.Params[i].Type, ownerNamespace);
                        if (actual is null || actual == "null" || actual == expected)
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
                        best = overload;
                        bestScore = score;
                    }
                }
            }

            return best;
        }

        private bool HasMember(string type, string memberName)
        {
            if (memberName == "HandleId" && Warcraft3TypeHierarchy.IsHandleType(type))
                return true;

            if (memberName == "Length" &&
                (type == "string" || type.EndsWith("[]", StringComparison.Ordinal)))
                return true;

            if (IsCollectionType(type))
                return memberName == "Count";
            if (TryGetGenericArguments(type, out var pairName, out _) && pairName == "KeyValuePair")
                return memberName is "Key" or "Value";
            return TryGetClassField(type, memberName, out _) ||
                   TryGetClassMethod(type, memberName, out _) ||
                   (_interfaces.Contains(type) && TryGetInterfaceMethod(type, memberName, out _)) ||
                   (_hasWarcraft3 && NativeTable.Find(type, memberName) is not null);
        }

        private static string? GetNativeName(string? template)
        {
            if (string.IsNullOrWhiteSpace(template))
                return null;

            var end = template.IndexOf('(');
            return (end >= 0 ? template[..end] : template).Trim();
        }

        private bool TryGetCurrentField(string name, out string type)
        {
            if (_currentClass is not null &&
                TryGetClassField(_currentClass, name, out type!))
                return true;

            type = null!;
            return false;
        }

        private bool IsCompatible(string actual, string expected)
        {
            actual = ResolveQualifiedUserType(actual);
            expected = ResolveQualifiedUserType(expected);
            if (actual == expected || actual == "null")
                return true;
            if (expected == "float" && actual == "int")
                return true;
            if (Warcraft3TypeHierarchy.IsAssignableFrom(actual, expected))
                return true;

            if (_classes.Contains(actual) && _classes.Contains(expected))
            {
                string? current = actual;
                var visited = new HashSet<string>(StringComparer.Ordinal);
                while (current is not null && visited.Add(current))
                {
                    if (current == expected)
                        return true;
                    current = _classBaseTypes.GetValueOrDefault(current);
                }
            }

            if (_classes.Contains(actual) && _interfaces.Contains(expected))
                return ClassImplementsInterface(actual, expected);

            if (_interfaces.Contains(actual) && _interfaces.Contains(expected))
                return InterfaceExtends(actual, expected);

            return false;
        }

        private void ValidateInheritance()
        {
            foreach (var (contractIdentity, contract) in _interfaceDeclarations)
            {
                string contractNamespace = _interfaceNamespaces[contractIdentity];
                foreach (string parent in contract.BaseInterfaces)
                    if (!_interfaces.Contains(ResolveQualifiedUserType(parent, contractNamespace)))
                        Add(1, 1, "error", $"interface {contract.Name} extends unknown interface {parent}");

                foreach (var duplicate in contract.Methods.GroupBy(method => method.Name, StringComparer.Ordinal)
                             .Where(group => group.Count() > 1))
                    Add(1, 1, "error", $"interface {contract.Name} declares duplicate method {duplicate.Key}");

                foreach (var inheritedGroup in GetInterfaceMethods(contractIdentity)
                             .GroupBy(method => method.Name, StringComparer.Ordinal))
                {
                    var signatures = inheritedGroup
                        .Select(GetMethodSignature)
                        .Distinct(StringComparer.Ordinal)
                        .ToList();
                    if (signatures.Count > 1)
                        Add(1, 1, "error",
                            $"interface {contract.Name} inherits conflicting signatures for method {inheritedGroup.Key}");
                }
            }

            foreach (var (classIdentity, cls) in _classDeclarations)
            {
                string classNamespace = _classNamespaces[classIdentity];
                var bases = cls.BaseTypes ?? [];
                var resolvedBases = bases.Select(type => ResolveQualifiedUserType(type, classNamespace)).ToList();
                var classBases = resolvedBases.Where(_classes.Contains).ToList();
                var interfaceBases = resolvedBases.Where(_interfaces.Contains).ToList();

                foreach (var (baseName, resolvedBaseName) in bases.Zip(resolvedBases))
                    if (!_classes.Contains(resolvedBaseName) && !_interfaces.Contains(resolvedBaseName))
                        Add(1, 1, "error", $"type {cls.Name} inherits unknown type {baseName}");

                if (classBases.Count > 1)
                    Add(1, 1, "error", $"class {cls.Name} can inherit only one class");
                if (classBases.Count == 1 && resolvedBases.Count > 0 && resolvedBases[0] != classBases[0])
                    Add(1, 1, "error", $"base class {classBases[0]} must be first in class {cls.Name}'s inheritance list");

                _classBaseTypes[classIdentity] = classBases.FirstOrDefault();
                _classInterfaces[classIdentity] = interfaceBases;

                if (_classBaseTypes[classIdentity] is { } baseClass &&
                    _classDeclarations.TryGetValue(baseClass, out var baseDeclaration) &&
                    baseDeclaration.Ctor is { Params.Count: > 0 })
                    Add(1, 1, "error",
                        $"base class {baseClass} must have a parameterless constructor; implicit base constructor calls do not accept arguments");
            }

            foreach (var (classIdentity, cls) in _classDeclarations)
            {
                foreach (var duplicate in cls.Fields.GroupBy(field => field.Name, StringComparer.Ordinal)
                             .Where(group => group.Count() > 1))
                    Add(1, 1, "error", $"class {cls.Name} declares duplicate field {duplicate.Key}");

                foreach (var duplicate in cls.Methods.Where(method => method.Operator is null)
                             .GroupBy(method => method.Name, StringComparer.Ordinal)
                             .Where(group => group.Count() > 1))
                    Add(1, 1, "error", $"class {cls.Name} declares duplicate method {duplicate.Key}");

                foreach (var method in cls.Methods.Where(method => method.Operator is null))
                {
                    if (TryGetBaseClassMethod(classIdentity, method.Name, out var inheritedMethod) &&
                        !string.Equals(GetMethodSignature(method), GetMethodSignature(inheritedMethod), StringComparison.Ordinal))
                        Add(1, 1, "error",
                            $"method {cls.Name}.{method.Name} hides an inherited method with an incompatible signature");
                }

                foreach (string interfaceName in GetImplementedInterfaces(classIdentity))
                {
                    foreach (var required in GetInterfaceMethods(interfaceName))
                    {
                        if (!TryGetClassMethod(classIdentity, required.Name, out var implementation))
                        {
                            Add(1, 1, "error",
                                $"class {cls.Name} does not implement {GetShortTypeName(interfaceName)}.{required.Name}()");
                            continue;
                        }

                        bool sameSignature = !implementation.IsStatic &&
                            ResolveQualifiedUserType(implementation.ReturnType, _declarationNamespaces.GetValueOrDefault(implementation, _unit.Name)) ==
                                ResolveQualifiedUserType(required.ReturnType, _declarationNamespaces.GetValueOrDefault(required, _unit.Name)) &&
                            implementation.Params.Count == required.Params.Count &&
                            implementation.Params.Select(parameter => ResolveQualifiedUserType(parameter.Type, _declarationNamespaces.GetValueOrDefault(implementation, _unit.Name)))
                                .SequenceEqual(required.Params.Select(parameter => ResolveQualifiedUserType(parameter.Type, _declarationNamespaces.GetValueOrDefault(required, _unit.Name))), StringComparer.Ordinal);
                        if (!sameSignature)
                            Add(1, 1, "error",
                                $"method {cls.Name}.{required.Name} does not match {interfaceName}.{required.Name}()");
                    }
                }
            }

            foreach (var classIdentity in _classDeclarations.Keys)
            {
                string? current = classIdentity;
                var visited = new HashSet<string>(StringComparer.Ordinal);
                while (current is not null && visited.Add(current))
                    current = _classBaseTypes.GetValueOrDefault(current);
                if (current is not null)
                    Add(1, 1, "error", $"class inheritance cycle detected at {current}");
            }

            foreach (var interfaceIdentity in _interfaceDeclarations.Keys)
                if (InterfaceHasCycle(interfaceIdentity))
                    Add(1, 1, "error", $"interface inheritance cycle detected at {interfaceIdentity}");
        }

        private bool InterfaceHasCycle(string start)
        {
            var active = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            return Visit(start);

            bool Visit(string current)
            {
                if (active.Contains(current))
                    return true;
                if (!visited.Add(current))
                    return false;
                active.Add(current);
                if (_interfaceDeclarations.TryGetValue(current, out var contract))
                    foreach (string parent in contract.BaseInterfaces)
                        if (_interfaces.Contains(ResolveQualifiedUserType(parent, _interfaceNamespaces[current])) && Visit(ResolveQualifiedUserType(parent, _interfaceNamespaces[current])))
                            return true;
                active.Remove(current);
                return false;
            }
        }

        private string GetMethodSignature(FnDecl method)
        {
            string ownerNamespace = _declarationNamespaces.GetValueOrDefault(method, _unit.Name);
            return ResolveQualifiedUserType(method.ReturnType, ownerNamespace) + "(" +
                   string.Join(",", method.Params.Select(parameter => ResolveQualifiedUserType(parameter.Type, ownerNamespace))) + ")" +
                   (method.IsStatic ? " static" : " instance");
        }

        private bool TryGetBaseClassMethod(string className, string methodName, out FnDecl method)
        {
            string? current = _classBaseTypes.GetValueOrDefault(className);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (current is not null && visited.Add(current))
            {
                if (_classMethods.TryGetValue(current, out var methods) &&
                    methods.TryGetValue(methodName, out method!))
                    return true;
                current = _classBaseTypes.GetValueOrDefault(current);
            }

            method = null!;
            return false;
        }

        private bool TryGetClassMethod(string className, string methodName, out FnDecl method)
        {
            string? current = className;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (current is not null && visited.Add(current))
            {
                if (_classMethods.TryGetValue(current, out var methods) &&
                    methods.TryGetValue(methodName, out method!))
                    return true;
                current = _classBaseTypes.GetValueOrDefault(current);
            }

            method = null!;
            return false;
        }

        private bool TryGetClassField(string className, string fieldName, out string type)
        {
            string? current = className;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (current is not null && visited.Add(current))
            {
                if (_classFields.TryGetValue(current, out var fields) &&
                    fields.TryGetValue(fieldName, out var fieldType))
                {
                    type = ResolveQualifiedUserType(fieldType, _classNamespaces.GetValueOrDefault(current, _unit.Name));
                    return true;
                }
                current = _classBaseTypes.GetValueOrDefault(current);
            }

            type = null!;
            return false;
        }

        private bool TryGetInterfaceMethod(string interfaceName, string methodName, out FnDecl method)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>();
            pending.Push(interfaceName);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                if (!visited.Add(current) || !_interfaceDeclarations.TryGetValue(current, out var contract))
                    continue;
                var found = contract.Methods.FirstOrDefault(candidate => candidate.Name == methodName);
                if (found is not null)
                {
                    method = found;
                    return true;
                }
                foreach (string parent in contract.BaseInterfaces)
                    pending.Push(ResolveQualifiedUserType(parent, _interfaceNamespaces[current]));
            }

            method = null!;
            return false;
        }

        private IEnumerable<FnDecl> GetInterfaceMethods(string interfaceName)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>();
            pending.Push(interfaceName);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                if (!visited.Add(current) || !_interfaceDeclarations.TryGetValue(current, out var contract))
                    continue;
                foreach (var method in contract.Methods)
                    yield return method;
                foreach (string parent in contract.BaseInterfaces)
                    pending.Push(ResolveQualifiedUserType(parent, _interfaceNamespaces[current]));
            }
        }

        private IEnumerable<string> GetImplementedInterfaces(string className)
        {
            var visitedClasses = new HashSet<string>(StringComparer.Ordinal);
            string? current = className;
            while (current is not null && visitedClasses.Add(current))
            {
                if (_classInterfaces.TryGetValue(current, out var interfaces))
                    foreach (string interfaceName in interfaces)
                        yield return interfaceName;
                current = _classBaseTypes.GetValueOrDefault(current);
            }
        }

        private bool ClassImplementsInterface(string className, string interfaceName) =>
            GetImplementedInterfaces(className).Any(candidate =>
                candidate == interfaceName || InterfaceExtends(candidate, interfaceName));

        private bool InterfaceExtends(string child, string parent)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>();
            pending.Push(child);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                if (!visited.Add(current))
                    continue;
                if (current == parent)
                    return true;
                if (_interfaceDeclarations.TryGetValue(current, out var contract))
                    foreach (string parentName in contract.BaseInterfaces)
                        pending.Push(ResolveQualifiedUserType(parentName, _interfaceNamespaces[current]));
            }
            return false;
        }

        private bool TryResolveQualifiedClassMethod(string qualifiedName, out string className, out FnDecl method)
        {
            int separator = qualifiedName.LastIndexOf('.');
            string qualifiedTypeName = separator > 0 ? NormalizeQualifiedTypeName(qualifiedName[..separator]) : string.Empty;
            if (separator > 0 &&
                _qualifiedClasses.TryGetValue(qualifiedTypeName, out className!) &&
                TryGetClassMethod(className, qualifiedName[(separator + 1)..], out method!))
                return true;

            className = null!;
            method = null!;
            return false;
        }

        private static string NormalizeQualifiedTypeName(string typeName)
        {
            int separator = typeName.LastIndexOf('.');
            return separator <= 0 ? typeName : typeName[..separator].Replace('.', '_') + typeName[separator..];
        }

        private static string GetTypeIdentity(string namespaceName, string typeName) => namespaceName + "." + typeName;

        private static string GetShortTypeName(string identity)
        {
            int separator = identity.LastIndexOf('.');
            return separator < 0 ? identity : identity[(separator + 1)..];
        }

        private string ResolveQualifiedUserType(string type, string? declaringNamespace = null)
        {
            type = NormalizeQualifiedTypeName(type);
            if (type.Contains('.', StringComparison.Ordinal))
            {
                if (_qualifiedClasses.TryGetValue(type, out var classIdentity)) return classIdentity;
                if (_qualifiedInterfaces.TryGetValue(type, out var interfaceIdentity)) return interfaceIdentity;
                return type;
            }
            string owner = (declaringNamespace ?? _unit.Name).Replace('.', '_');
            string local = GetTypeIdentity(owner, type);
            if (_classes.Contains(local) || _interfaces.Contains(local)) return local;
            var imported = (_namespaceUsings.GetValueOrDefault(owner) ?? [])
                .Select(ns => GetTypeIdentity(ns, type))
                .Where(identity => _classes.Contains(identity) || _interfaces.Contains(identity))
                .Distinct(StringComparer.Ordinal).ToList();
            if (imported.Count == 1) return imported[0];
            if (imported.Count > 1) return "?ambiguous:" + type;
            var matches = _classes.Concat(_interfaces).Where(identity => identity.EndsWith("." + type, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToList();
            return matches.Count == 1 ? matches[0] : type;
        }

        private bool IsKnownType(string type, bool allowVoid)
        {
            string baseType = StripArrayRanks(type);
            if (type != baseType && baseType.Length == 0)
                return false;
            if (TryGetGenericArguments(baseType, out var genericName, out var genericArguments))
            {
                int expectedArity = genericName switch
                {
                    "List" or "Queue" or "Stack" => 1,
                    "Dictionary" or "KeyValuePair" => 2,
                    _ => -1
                };
                return expectedArity == genericArguments.Count &&
                       genericArguments.All(argument => IsKnownType(argument, allowVoid: false));
            }
            if (baseType is "int" or "float" or "bool" or "string")
                return true;
            if (allowVoid && baseType == "void")
                return true;
            string resolvedUserType = ResolveQualifiedUserType(baseType);
            if (_classes.Contains(resolvedUserType) || _interfaces.Contains(resolvedUserType))
                return true;
            return _hasWarcraft3 &&
                (NativeCatalog.Types.Contains(baseType) || baseType is "integer" or "real" or "nothing" or "code");
        }

        private static string StripArrayRanks(string type)
        {
            while (type.EndsWith("[]", StringComparison.Ordinal))
                type = type[..^2];
            return type;
        }

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

        private static bool IsCollectionType(string type) =>
            TryGetGenericArguments(type, out var name, out _) &&
            (name is "List" or "Queue" or "Stack" or "Dictionary");

        private bool CheckCollectionCall(MemberExpr member, IReadOnlyList<Expr> args, string collectionName, IReadOnlyList<string> typeArgs)
        {
            string[] expected = collectionName switch
            {
                "List" => member.Name switch
                {
                    "Add" or "Remove" or "IndexOf" => [typeArgs[0]],
                    "Clear" => [],
                    _ => []
                },
                "Queue" => member.Name == "Enqueue" ? [typeArgs[0]] : [],
                "Stack" => member.Name == "Push" ? [typeArgs[0]] : [],
                "Dictionary" => member.Name switch
                {
                    "Add" or "TryAdd" => [typeArgs[0], typeArgs[1]],
                    "Remove" or "TryRemove" or "ContainsKey" => [typeArgs[0]],
                    _ => []
                },
                _ => []
            };

            bool known = collectionName switch
            {
                "List" => member.Name is "Add" or "Remove" or "IndexOf" or "Clear",
                "Queue" => member.Name is "Enqueue" or "Dequeue" or "Peek",
                "Stack" => member.Name is "Push" or "Pop" or "Peek",
                "Dictionary" => member.Name is "Add" or "Remove" or "TryAdd" or "TryRemove" or "ContainsKey",
                _ => false
            };
            if (!known)
                return false;

            if (args.Count != expected.Length)
                Add(member.Line, member.Col, "error",
                    $"{collectionName}.{member.Name}: ожидалось {expected.Length} аргументов, получено {args.Count}");

            for (int i = 0; i < Math.Min(args.Count, expected.Length); i++)
            {
                CheckExpression(args[i]);
                string? actual = InferType(args[i]);
                if (collectionName == "Dictionary" && i == 0 && actual == "null")
                    Add(args[i].Line, args[i].Col, "error", "ключ Dictionary не может быть null");
                else if (actual is not null && !IsCompatible(actual, expected[i]))
                    Add(args[i].Line, args[i].Col, "error",
                        $"{collectionName}.{member.Name}: аргумент {i + 1}: ожидался тип {expected[i]}, получен {actual}");
            }
            for (int i = expected.Length; i < args.Count; i++)
                CheckExpression(args[i]);
            return true;
        }

        private static bool IsWarcraftSymbol(string name) =>
            NativeCatalog.Natives.Contains(name) ||
            (NativeCatalog.Constants.Contains(name) || Warcraft3AdditionalConstants.Constants.Contains(name)) ||
            NativeSigs.Map.ContainsKey(name);

        private void ValidateDeclaredType(string type, int line, int col, bool allowVoid)
        {
            if (!IsKnownType(type, allowVoid))
            {
                string resolved = ResolveQualifiedUserType(type);
                if (resolved.StartsWith("?ambiguous:", StringComparison.Ordinal))
                    Add(line, col, "error", $"неоднозначный тип {type}: имя доступно из нескольких namespace; укажите полное имя");
                else
                    Add(line, col, "error",
                        $"неизвестный тип {type}; для типов Warcraft III добавьте using Warcraft3");
            }
        }

        private bool IsKnownSymbol(string name)
        {
            return _functions.ContainsKey(name) ||
                   _globals.ContainsKey(name) ||
                   _classes.Contains(ResolveQualifiedUserType(name)) ||
                   _interfaces.Contains(ResolveQualifiedUserType(name)) ||
                   (_hasWarcraft3 && NativeCatalog.Natives.Contains(name)) ||
                   (_hasWarcraft3 && (NativeCatalog.Constants.Contains(name) || Warcraft3AdditionalConstants.Constants.Contains(name))) ||
                   (_hasWarcraft3 && NativeSigs.Map.ContainsKey(name)) ||
                   name is "J" or "math" or "string" or "table" or "utf8" or "error" or "Print";
        }

        private void PushScope() => _scopes.Add(new Dictionary<string, string>(StringComparer.Ordinal));

        private void PopScope()
        {
            if (_scopes.Count > 0)
                _scopes.RemoveAt(_scopes.Count - 1);
        }

        private void Declare(string name, string type, int line, int col, bool reportDuplicate = true)
        {
            if (_scopes.Count == 0)
                PushScope();

            var scope = _scopes[^1];
            if (reportDuplicate && scope.ContainsKey(name))
                Add(line, col, "error", $"повторное объявление переменной {name}");

            scope[name] = ResolveQualifiedUserType(type);
        }

        private bool TryLookup(string name, out string type)
        {
            for (var i = _scopes.Count - 1; i >= 0; i--)
            {
                if (_scopes[i].TryGetValue(name, out type!))
                    return true;
            }

            type = null!;
            return false;
        }

        private void Add(int line, int col, string severity, string message) =>
            Diagnostics.Add(new Diagnostic(line, col, severity, message, _file));
    }

}

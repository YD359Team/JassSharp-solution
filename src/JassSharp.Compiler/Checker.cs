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
        bool implicitWarcraft3 = true)
    {
        var analyzer = new Analyzer(unit, file, imports, implicitWarcraft3);
        analyzer.Run();
        return analyzer.Diagnostics;
    }

    private sealed class Analyzer
    {
        private readonly NsUnit _unit;
        private readonly string? _file;
        private readonly bool _hasWarcraft3;
        private readonly HashSet<string> _classes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FnDecl> _functions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _globals = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, string>> _classFields = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, FnDecl>> _classMethods = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<FnDecl>> _operators = new(StringComparer.Ordinal);
        private readonly List<Dictionary<string, string>> _scopes = [];
        private string? _currentClass;
        private bool _currentMethodIsStatic;
        private string _expectedReturnType = "void";

        public List<Diagnostic> Diagnostics { get; } = [];

        public Analyzer(NsUnit unit, string? file, IEnumerable<NsUnit>? imports, bool implicitWarcraft3)
        {
            _unit = unit;
            _file = file;
            var importedUnits = imports?.ToList() ?? [];
            _hasWarcraft3 = implicitWarcraft3 ||
                unit.Usings.Contains("Warcraft3", StringComparer.Ordinal) ||
                importedUnits.Any(imported => imported.Name == "Warcraft3");

            AddUnit(unit);
            foreach (var imported in importedUnits)
                AddUnit(imported);
        }

        private void AddUnit(NsUnit unit)
        {
            foreach (var cls in unit.Classes)
            {
                _classes.Add(cls.Name);
                if (!_classFields.ContainsKey(cls.Name))
                    _classFields[cls.Name] = new Dictionary<string, string>(StringComparer.Ordinal);
                if (!_classMethods.ContainsKey(cls.Name))
                    _classMethods[cls.Name] = new Dictionary<string, FnDecl>(StringComparer.Ordinal);

                foreach (var field in cls.Fields)
                    _classFields[cls.Name][field.Name] = field.Type;
                foreach (var method in cls.Methods)
                {
                    if (method.Operator is { } op)
                    {
                        string key = cls.Name + "|" + op;
                        if (!_operators.TryGetValue(key, out var overloads))
                            _operators[key] = overloads = [];
                        overloads.Add(method);
                    }
                    else
                    {
                        _classMethods[cls.Name][method.Name] = method;
                    }
                }
            }

            foreach (var fn in unit.Functions)
                _functions[fn.Name] = fn;

            foreach (var field in unit.Fields)
                _globals[field.Name] = field.Type;
            foreach (var constant in unit.Consts)
                _globals[constant.Name] = constant.Type;
            foreach (var enumeration in unit.Enums)
            {
                foreach (var member in enumeration.Members)
                    _globals[member.Name] = enumeration.Name;
            }
        }

        public void Run()
        {
            foreach (var field in _unit.Fields)
                ValidateDeclaredType(field.Type, 1, 1, allowVoid: false);
            foreach (var constant in _unit.Consts)
                ValidateDeclaredType(constant.Type, constant.Value.Line, constant.Value.Col, allowVoid: false);

            foreach (var cls in _unit.Classes)
            {
                ValidateOperators(cls);
                foreach (var field in cls.Fields)
                    ValidateDeclaredType(field.Type, 1, 1, allowVoid: false);
                _currentClass = cls.Name;

                if (cls.Ctor is not null)
                    CheckFunction(cls.Ctor, cls.Name);
                foreach (var method in cls.Methods)
                    CheckFunction(method, cls.Name);
            }

            _currentClass = null;
            foreach (var function in _unit.Functions)
                CheckFunction(function, null);
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
                if (!method.Params.Any(parameter => parameter.Type == cls.Name))
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
            foreach (var parameter in function.Params)
                Declare(parameter.Name, parameter.Type, 1, 1, reportDuplicate: false);

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
                        Add(variable.Line, variable.Col, "error",
                            $"неизвестный тип {declaredType}; для типов Warcraft III добавьте using Warcraft3");

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
                    CheckStatements(loop.Body);
                    PopScope();
                    break;

                case ForeachStmt loop:
                {
                    CheckExpression(loop.Collection);
                    string? collectionType = InferType(loop.Collection);
                    string? elementType = GetForeachElementType(collectionType);
                    if (elementType is null && collectionType != "List")
                        Add(loop.Line, loop.Col, "error",
                            $"foreach не поддерживает коллекцию типа {collectionType ?? "?"}");

                    string itemType = loop.Type;
                    if (itemType == "var")
                    {
                        if (elementType is null)
                        {
                            Add(loop.Line, loop.Col, "error",
                                $"не удалось вывести тип элемента foreach для коллекции {InferType(loop.Collection) ?? "?"}");
                            itemType = "?";
                        }
                        else
                        {
                            itemType = elementType;
                        }
                    }
                    else
                    {
                        if (!IsKnownType(itemType, allowVoid: false))
                            Add(loop.Line, loop.Col, "error", $"неизвестный тип элемента foreach: {itemType}");
                        if (elementType is not null && !IsCompatible(elementType, itemType))
                            Add(loop.Line, loop.Col, "error",
                                $"foreach: ожидался элемент типа {itemType}, коллекция содержит {elementType}");
                    }

                    PushScope();
                    Declare(loop.Name, itemType, loop.Line, loop.Col);
                    CheckStatements(loop.Body);
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
                    CheckStatements(loop.Body);
                    PopScope();
                    break;

                case CondStmt conditional:
                    // Compile-time conditional branches have independent lexical scopes.
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
                    _expectedReturnType = "?";
                    CheckStatements(lambda.Body);
                    _expectedReturnType = previousReturn;
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

                case ThisExpr self:
                    if (_currentClass is null)
                        Add(self.Line, self.Col, "error", "this доступен только внутри метода или конструктора");
                    else if (_currentMethodIsStatic)
                        Add(self.Line, self.Col, "error", "this недоступен внутри static-метода");
                    break;

                case NewExpr creation:
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
                    CheckExpression(member.Object);
                    if (InferType(member.Object) is { } receiverType &&
                        !HasMember(receiverType, member.Name))
                    {
                        Add(member.Line, member.Col, "warning",
                            $"тип {receiverType} не содержит члена {member.Name}");
                    }
                    break;

                case IndexExpr index:
                    CheckExpression(index.Object);
                    CheckExpression(index.Index);
                    var indexType = InferType(index.Index);
                    if (indexType is not null && indexType != "int")
                        Add(index.Index.Line, index.Index.Col, "error",
                            $"индекс должен иметь тип int, получен {indexType}");
                    var indexedType = InferType(index.Object);
                    if (indexedType is not null && indexedType != "string" &&
                        indexedType != "List" && !indexedType.EndsWith("[]", StringComparison.Ordinal))
                        Add(index.Line, index.Col, "error",
                            $"тип {indexedType} не поддерживает индексатор");
                    break;

                case FromEndExpr fromEnd:
                    CheckExpression(fromEnd.Inner);
                    break;

                case BinaryExpr binary:
                    CheckExpression(binary.Left);
                    CheckExpression(binary.Right);
                    var leftType = InferType(binary.Left);
                    var rightType = InferType(binary.Right);
                    var binaryOverload = FindOperator(leftType, rightType, binary.Op, [binary.Left, binary.Right]);
                    if (binaryOverload is not null)
                    {
                        CheckArguments(binary, [binary.Left, binary.Right], binaryOverload.Params.Select(p => p.Type).ToArray());
                    }
                    else if (IsUserType(leftType) || IsUserType(rightType))
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
                    break;

                case UnaryExpr unary:
                    CheckExpression(unary.Inner);
                    var unaryOverload = FindOperator(InferType(unary.Inner), null, unary.Op, [unary.Inner]);
                    if (unaryOverload is not null)
                        CheckArguments(unary, [unary.Inner], unaryOverload.Params.Select(p => p.Type).ToArray());
                    else if (IsUserType(InferType(unary.Inner)))
                        Add(unary.Line, unary.Col, "error", $"унарный оператор {unary.Op} не определён для типа {InferType(unary.Inner)}");
                    break;
            }
        }

        private void CheckCall(CallExpr call)
        {
            if (call.Callee is NameExpr name)
            {
                if (_functions.TryGetValue(name.Name, out var function))
                {
                    CheckArguments(name, call.Args, function.Params.Select(p => p.Type).ToArray());
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
                        Add(name.Line, name.Col, "warning", $"неизвестный вызов {name.Name}");

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

            CheckExpression(member.Object);
            var receiverType = InferType(member.Object);

            if (receiverType is not null &&
                _classMethods.TryGetValue(receiverType, out var methods) &&
                methods.TryGetValue(member.Name, out var instanceMethod))
            {
                CheckArguments(member, call.Args, instanceMethod.Params.Select(p => p.Type).ToArray());
                if (instanceMethod.IsStatic)
                    Add(member.Line, member.Col, "error",
                        $"статический метод {receiverType}.{member.Name} необходимо вызывать через имя класса");
                return;
            }

            if (member.Object is NameExpr className &&
                !_scopes.Any(scope => scope.ContainsKey(className.Name)) &&
                _classMethods.TryGetValue(className.Name, out var staticMethods) &&
                staticMethods.TryGetValue(member.Name, out var classMethod))
            {
                CheckArguments(member, call.Args, classMethod.Params.Select(p => p.Type).ToArray());
                if (!classMethod.IsStatic)
                    Add(member.Line, member.Col, "error",
                        $"нестатический метод {className.Name}.{member.Name} нельзя вызывать через имя класса");
                return;
            }

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
                    Add(member.Line, member.Col, "warning",
                        $"тип {receiverType} не содержит метода {member.Name}");
                }
            }

            foreach (var argument in call.Args)
                CheckExpression(argument);
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
                    return _globals.TryGetValue(name.Name, out var globalType) ? globalType : null;

                case ThisExpr:
                    return _currentClass;

                case NumExpr number:
                    return number.Text.Contains('.') || number.Text.Contains('e') || number.Text.Contains('E')
                        ? "float"
                        : "int";

                case BoolExpr:
                    return "bool";
                case StrExpr:
                    return "string";
                case RawExpr:
                    return "int";
                case NullExpr:
                    return "null";
                case NewExpr creation:
                    return creation.Type;

                case CallExpr { Callee: NameExpr name }:
                    if (_functions.TryGetValue(name.Name, out var function))
                        return function.ReturnType;
                    return _hasWarcraft3 && NativeSigs.Map.TryGetValue(name.Name, out var native) ? native.ReturnType : null;

                case CallExpr { Callee: MemberExpr member }:
                {
                    if (member.Object is NameExpr className &&
                        _classMethods.TryGetValue(className.Name, out var staticMethods) &&
                        staticMethods.TryGetValue(member.Name, out var staticMethod) &&
                        staticMethod.IsStatic)
                        return staticMethod.ReturnType;

                    var receiverType = InferType(member.Object);
                    if (receiverType is null)
                        return null;

                    if (_classMethods.TryGetValue(receiverType, out var methods) &&
                        methods.TryGetValue(member.Name, out var method))
                        return method.ReturnType;

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
                    var receiverType = InferType(member.Object);
                    if (receiverType is null)
                        return null;

                    if (_classFields.TryGetValue(receiverType, out var fields) &&
                        fields.TryGetValue(member.Name, out var fieldType2))
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
                    return indexedType is not null && indexedType.EndsWith("[]", StringComparison.Ordinal)
                        ? indexedType[..^2]
                        : null;
                }

                case UnaryExpr unary:
                {
                    var overload = FindOperator(InferType(unary.Inner), null, unary.Op, [unary.Inner]);
                    if (overload is not null)
                        return overload.ReturnType;
                    return unary.Op switch
                    {
                        "!" or "not" => "bool",
                        _ => InferType(unary.Inner)
                    };
                }

                case BinaryExpr binary:
                {
                    var overload = FindOperator(InferType(binary.Left), InferType(binary.Right), binary.Op, [binary.Left, binary.Right]);
                    if (overload is not null)
                        return overload.ReturnType;

                    var left = InferType(binary.Left);
                    var right = InferType(binary.Right);
                    if (binary.Op is "==" or "!=" or "<" or ">" or "<=" or ">=" or "and" or "or" or "&&" or "||")
                        return "bool";
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
            type is not null && _classes.Contains(type);

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
                    for (int i = 0; i < arguments.Count; i++)
                    {
                        var actual = InferType(arguments[i]);
                        var expected = overload.Params[i].Type;
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
            return (_classFields.TryGetValue(type, out var fields) && fields.ContainsKey(memberName)) ||
                   (_classMethods.TryGetValue(type, out var methods) && methods.ContainsKey(memberName)) ||
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
                _classFields.TryGetValue(_currentClass, out var fields) &&
                fields.TryGetValue(name, out type!))
                return true;

            type = null!;
            return false;
        }

        private bool IsKnownType(string type, bool allowVoid)
        {
            string baseType = StripArrayRanks(type);
            if (type != baseType && baseType.Length == 0)
                return false;
            if (baseType is "int" or "float" or "bool" or "string" or "List")
                return true;
            if (allowVoid && baseType == "void")
                return true;
            if (_classes.Contains(baseType))
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

        private static bool IsWarcraftSymbol(string name) =>
            NativeCatalog.Natives.Contains(name) ||
            NativeCatalog.Constants.Contains(name) ||
            NativeSigs.Map.ContainsKey(name);

        private void ValidateDeclaredType(string type, int line, int col, bool allowVoid)
        {
            if (!IsKnownType(type, allowVoid))
                Add(line, col, "error",
                    $"неизвестный тип {type}; для типов Warcraft III добавьте using Warcraft3");
        }

        private bool IsKnownSymbol(string name)
        {
            return _functions.ContainsKey(name) ||
                   _globals.ContainsKey(name) ||
                   _classes.Contains(name) ||
                   (_hasWarcraft3 && NativeCatalog.Natives.Contains(name)) ||
                   (_hasWarcraft3 && NativeCatalog.Constants.Contains(name)) ||
                   (_hasWarcraft3 && NativeSigs.Map.ContainsKey(name)) ||
                   name is "J" or "math" or "string" or "table" or "Print";
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

            scope[name] = type;
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

    private static bool IsCompatible(string actual, string expected)
    {
        if (actual == expected || actual == "null")
            return true;
        if (expected == "float" && actual == "int")
            return true;
        return false;
    }
}

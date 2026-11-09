namespace JassSharp.Syntax;

public abstract record Node;
public sealed record NsUnit(
    string Name,
    List<string> Usings,
    List<ConstDecl> Consts,
    List<ClassDecl> Classes,
    List<FnDecl> Functions,
    List<FieldDecl> Fields,
    List<EnumDecl> Enums) : Node;
public sealed record ConstDecl(string Type, string Name, Expr Value);
public sealed record FieldDecl(string Type, string Name, bool IsArray = false);
public sealed record ParamDecl(string Type, string Name);
public sealed record ClassDecl(string Name, List<FieldDecl> Fields, FnDecl? Ctor, List<FnDecl> Methods);
public sealed record EnumMember(string Name, Expr? Value);
public sealed record EnumDecl(string Name, List<EnumMember> Members);
public sealed record FnDecl(string ReturnType, string Name, List<ParamDecl> Params, List<Stmt> Body, HashSet<string> Attributes = null!);
public sealed record LambdaExpr(List<ParamDecl> Params, List<Stmt> Body, int Line = 1, int Col = 1) : Expr(Line, Col);

public abstract record Stmt;
public sealed record VarStmt(string Type, string Name, Expr? Init, int Line = 1, int Col = 1) : Stmt;
public sealed record AssignStmt(Expr Target, Expr Value) : Stmt;
public sealed record ExprStmt(Expr Expr) : Stmt;
public sealed record ReturnStmt(Expr? Expr) : Stmt;
public sealed record IfStmt(Expr Cond, List<Stmt> Then, List<Stmt>? Else) : Stmt;
public sealed record WhileStmt(Expr Cond, List<Stmt> Body) : Stmt;
public sealed record ForStmt(Stmt? Init, Expr? Cond, Stmt? Step, List<Stmt> Body) : Stmt;
public sealed record CondStmt(string Target, List<Stmt> Then, List<Stmt>? Else) : Stmt; // #if LUA / VJASS

public abstract record Expr(int Line = 1, int Col = 1);
public sealed record NameExpr(string Name, int Line = 1, int Col = 1) : Expr(Line, Col);
public sealed record ThisExpr(int Line = 1, int Col = 1) : Expr(Line, Col);
public sealed record NumExpr(string Text, int Line = 1, int Col = 1) : Expr(Line, Col);
public sealed record StrExpr(string Text, int Line = 1, int Col = 1) : Expr(Line, Col);
public sealed record RawExpr(string Id, int Line = 1, int Col = 1) : Expr(Line, Col);
public sealed record BoolExpr(bool Value, int Line = 1, int Col = 1) : Expr(Line, Col);
public sealed record NullExpr(int Line = 1, int Col = 1) : Expr(Line, Col);
public sealed record UnaryExpr(string Op, Expr Inner, int Line = 1, int Col = 1) : Expr(Line, Col);
public sealed record MemberExpr(Expr Object, string Name, int Line = 1, int Col = 1) : Expr(Line, Col);
public sealed record IndexExpr(Expr Object, Expr Index, int Line = 1, int Col = 1) : Expr(Line, Col);
public sealed record FromEndExpr(Expr Inner, int Line = 1, int Col = 1) : Expr(Line, Col);
public sealed record CallExpr(Expr Callee, List<Expr> Args, int Line = 1, int Col = 1) : Expr(Line, Col);
public sealed record NewExpr(string Type, List<Expr> Args, int Line = 1, int Col = 1) : Expr(Line, Col);
public sealed record BinaryExpr(string Op, Expr Left, Expr Right, int Line = 1, int Col = 1) : Expr(Line, Col);

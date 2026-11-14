namespace JassSharp.Syntax;

public sealed class Parser(List<Token> tokens)
{
    private int _i;
    private Token Peek => tokens[_i];

    public NsUnit ParseNamespace()
    {
        EatId("namespace");
        var name = Eat(TokKind.Id).Text;
        while (Accept(".")) name += "_" + Eat(TokKind.Id).Text;
        Expect("{");
        var usings = new List<string>();
        var fields = new List<FieldDecl>();
        var ns = new NsUnit(name, usings, [], [], [], fields, []);
        while (!Accept("}"))
        {
            if (Peek.Kind == TokKind.Eof) throw new ParseException("незакрытый namespace", Peek.Line, Peek.Col);
            if (CheckId("using"))
            {
                Eat(TokKind.Id);
                var mod = Eat(TokKind.Id).Text;
                while (Accept(".")) mod += "_" + Eat(TokKind.Id).Text;
                Accept(";");
                usings.Add(mod);
                continue;
            }
            var attrs = ParseAttributes();
            SkipVis();
            if (CheckId("const"))
            {
                Eat(TokKind.Id);
                ns.Consts.Add(new ConstDecl(Eat(TokKind.Id).Text, Eat(TokKind.Id).Text, AfterEq()));
            }
            else if (CheckId("class"))
            {
                Eat(TokKind.Id);
                ns.Classes.Add(ParseClass(attrs));
            }
            else if (CheckId("enum"))
            {
                Eat(TokKind.Id);
                ns.Enums.Add(ParseEnum());
            }
            else
            {
                var typeTok = Peek;
                string typeName = ParseTypeName();
                var isArray = typeName.EndsWith("[]", StringComparison.Ordinal);
                var id = Eat(TokKind.Id).Text;
                if (Accept("("))
                {
                    var ps = ParseParams();
                    Expect(")");
                    Expect("{");
                    ns.Functions.Add(new FnDecl(typeName, id, ps, ParseBlock(), attrs));
                }
                else
                {
                    Accept(";");
                    fields.Add(new FieldDecl(typeName, id, isArray));
                }
            }
        }
        return ns;
    }

    private HashSet<string> ParseAttributes()
    {
        var attrs = new HashSet<string>(StringComparer.Ordinal);
        while (Accept("["))
        {
            var id = Eat(TokKind.Id).Text;
            attrs.Add(id);
            Expect("]");
        }
        return attrs;
    }

    private Expr AfterEq()
    {
        Expect("=");
        var e = ParseExpr();
        Accept(";");
        return e;
    }

    private ClassDecl ParseClass(HashSet<string> classAttrs)
    {
        var name = Eat(TokKind.Id).Text;
        Expect("{");
        var fields = new List<FieldDecl>();
        var methods = new List<FnDecl>();
        FnDecl? ctor = null;
        while (!Accept("}"))
        {
            if (Peek.Kind == TokKind.Eof) throw new ParseException($"незакрытый class {name}", Peek.Line, Peek.Col);
            var attrs = ParseAttributes();
            SkipVis();
            bool isStatic = false;
            if (CheckId("static"))
            {
                Eat(TokKind.Id);
                isStatic = true;
            }
            if (CheckId(name) && _i + 1 < tokens.Count && tokens[_i + 1].Text == "(")
            {
                Eat(TokKind.Id);
                Expect("(");
                var ps = ParseParams();
                Expect(")");
                Expect("{");
                ctor = new FnDecl(name, "create", ps, ParseBlock(), attrs);
                continue;
            }
            var type = ParseTypeName();
            var isArray = type.EndsWith("[]", StringComparison.Ordinal);
            if (CheckId("operator"))
            {
                Eat(TokKind.Id);
                var opToken = Eat(TokKind.Sym);
                if (opToken.Text is not ("+" or "-" or "*" or "/" or "%" or "==" or "!=" or "<" or ">" or "<=" or ">="))
                    throw new ParseException($"оператор {opToken.Text} нельзя перегрузить", opToken.Line, opToken.Col);
                Expect("(");
                var operatorParams = ParseParams();
                Expect(")");
                Expect("{");
                methods.Add(new FnDecl(type, "__operator_" + OperatorIdentifier(opToken.Text), operatorParams, ParseBlock(), attrs, isStatic, opToken.Text));
                continue;
            }
            var id = Eat(TokKind.Id).Text;
            if (Accept("("))
            {
                var ps = ParseParams();
                Expect(")");
                Expect("{");
                methods.Add(new FnDecl(type, id, ps, ParseBlock(), attrs, isStatic));
            }
            else
            {
                Accept(";");
                fields.Add(new FieldDecl(type, id, isArray));
            }
        }
        return new ClassDecl(name, fields, ctor, methods);
    }

    private static string OperatorIdentifier(string op) => op switch
    {
        "+" => "Add", "-" => "Subtract", "*" => "Multiply", "/" => "Divide", "%" => "Modulo",
        "==" => "Equal", "!=" => "NotEqual", "<" => "LessThan", ">" => "GreaterThan",
        "<=" => "LessThanOrEqual", ">=" => "GreaterThanOrEqual",
        _ => throw new ArgumentOutOfRangeException(nameof(op))
    };

    private EnumDecl ParseEnum()
    {
        var name = Eat(TokKind.Id).Text;
        Expect("{");
        var members = new List<EnumMember>();
        while (!Accept("}"))
        {
            if (Peek.Kind == TokKind.Eof) throw new ParseException("незакрытый enum", Peek.Line, Peek.Col);
            var id = Eat(TokKind.Id).Text;
            Expr? val = null;
            if (Accept("=")) val = ParseExpr();
            Accept(",");
            members.Add(new EnumMember(id, val));
        }
        return new EnumDecl(name, members);
    }

    private string ParseTypeName()
    {
        string typeName = Eat(TokKind.Id).Text;
        if (typeName == "List" && Accept("<"))
        {
            Eat(TokKind.Id);
            Expect(">");
        }

        while (Accept("["))
        {
            Expect("]");
            typeName += "[]";
        }

        return typeName;
    }

    private List<ParamDecl> ParseParams()
    {
        var list = new List<ParamDecl>();
        if (Check(")")) return list;
        do
        {
            string type = ParseTypeName();
            string name = Eat(TokKind.Id).Text;
            list.Add(new ParamDecl(type, name));
        }
        while (Accept(","));
        return list;
    }

    private List<Stmt> ParseBlock()
    {
        var list = new List<Stmt>();
        while (!Check("}"))
        {
            if (Peek.Kind == TokKind.Eof) throw new ParseException("незакрытый блок", Peek.Line, Peek.Col);
            list.Add(ParseStmt());
        }
        Expect("}");
        return list;
    }

    private static readonly HashSet<string> TypeNames = new(StringComparer.Ordinal)
    {
        // common types
        "int", "float", "bool", "void", "string", "var",
        // extended types
        "List",
        // warctaft 3 types
        "ability", "abilitybooleanfield", "abilitybooleanlevelarrayfield", "abilitybooleanlevelfield", "abilityintegerfield",
        "abilityintegerlevelarrayfield", "abilityintegerlevelfield", "abilityrealfield", "abilityreallevelarrayfield",
        "abilityreallevelfield", "abilitystringfield", "abilitystringlevelarrayfield", "abilitystringlevelfield", "agent",
        "aidifficulty", "alliancetype", "animtype", "armortype", "attacktype", "boolexpr", "buff", "button", "camerafield",
        "camerasetup", "code", "commandbuttoneffect", "conditionfunc", "damagetype", "defeatcondition", "defensetype",
        "destructable", "dialog", "dialogevent", "effect", "effecttype", "event", "eventid", "fgamestate", "filterfunc",
        "fogmodifier", "fogstate", "fogstyle", "force", "frameeventtype", "framehandle", "framepointtype", "gamecache",
        "gamedifficulty", "gameevent", "gamespeed", "gamestate", "gametype", "group", "handle", "hashtable", "heroattribute",
        "igamestate", "image", "integer", "item", "itembooleanfield", "itemintegerfield", "itempool", "itemrealfield",
        "itemstringfield", "itemTag", "itemtype", "leaderboard", "lightning", "limitop", "loadoutslot", "location",
        "mapcontrol", "mapdensity", "mapflag", "mapsetting", "mapvisibility", "metakeytype", "minimapicon", "mousebuttontype",
        "movetype", "multiboard", "multiboarditem", "nothing", "originframetype", "oskeytype", "pathingflag", "pathingtype",
        "placement", "player", "playercolor", "playergameresult", "playerevent", "playerscore", "playerslotstate", "playerstate",
        "playerunitevent", "quest", "questitem", "race", "racepreference", "raritycontrol", "real", "rect", "regentype", "region",
        "sound", "soundtype", "startlocprio", "string", "subanimtype", "targetflag", "terraindeformation", "texmapflags",
        "textaligntype", "texttag", "timerdialog", "timer", "trackable", "trigger", "triggeraction", "triggercondition",
        "ubersplat", "unit", "unitbooleanfield", "unitcategory", "unitevent", "unitintegerfield", "unitpool", "unitrealfield",
        "unitstate", "unitstringfield", "unittype", "unitweaponbooleanfield", "unitweaponintegerfield", "unitweaponrealfield",
        "unitweaponstringfield", "version", "volumegroup", "weapontype", "weathereffect", "widget", "widgetevent",
    };

    private bool LooksLikeDecl()
    {
        if (Peek.Kind != TokKind.Id || _i + 1 >= tokens.Count) return false;

        int nameIndex = _i + 1;
        while (nameIndex + 1 < tokens.Count &&
               tokens[nameIndex].Kind == TokKind.Sym && tokens[nameIndex].Text == "[" &&
               tokens[nameIndex + 1].Kind == TokKind.Sym && tokens[nameIndex + 1].Text == "]")
        {
            nameIndex += 2;
        }

        if (nameIndex >= tokens.Count || tokens[nameIndex].Kind != TokKind.Id)
            return false;

        if (nameIndex + 1 < tokens.Count &&
            tokens[nameIndex + 1].Kind == TokKind.Sym && tokens[nameIndex + 1].Text == "(")
            return false;

        return TypeNames.Contains(Peek.Text) || char.IsUpper(Peek.Text[0]);
    }

    private Stmt ParseStmt()
    {
        if (Check("#"))
        {
            Eat(TokKind.Sym);
            if (CheckId("if"))
            {
                Eat(TokKind.Id);
                var target = Eat(TokKind.Id).Text;
                Expect("{");
                var then = ParseBlock();
                List<Stmt>? els = null;
                if (CheckId("else"))
                {
                    Eat(TokKind.Id);
                    Expect("{");
                    els = ParseBlock();
                }
                return new CondStmt(target, then, els);
            }
            throw new ParseException("неизвестная директива", Peek.Line, Peek.Col);
        }
        if (CheckId("if"))
        {
            Eat(TokKind.Id);
            Expect("(");
            var c = ParseExpr();
            Expect(")");
            Expect("{");
            var then = ParseBlock();
            List<Stmt>? els = null;
            if (CheckId("else")) { Eat(TokKind.Id); Expect("{"); els = ParseBlock(); }
            return new IfStmt(c, then, els);
        }
        if (CheckId("while"))
        {
            Eat(TokKind.Id);
            Expect("(");
            var c = ParseExpr();
            Expect(")");
            Expect("{");
            return new WhileStmt(c, ParseBlock());
        }
        if (CheckId("for"))
        {
            Eat(TokKind.Id);
            Expect("(");
            Stmt? init = null;
            if (!Check(";")) init = ParseStmt();
            else Eat(TokKind.Sym);
            Expr? cond = null;
            if (!Check(";")) cond = ParseExpr();
            Expect(";");
            Stmt? step = null;
            if (!Check(")")) step = ParseStmt();
            Expect(")");
            Expect("{");
            return new ForStmt(init, cond, step, ParseBlock());
        }
        if (CheckId("return"))
        {
            Eat(TokKind.Id);
            if (Accept(";")) return new ReturnStmt(null);
            var e = ParseExpr();
            Accept(";");
            return new ReturnStmt(e);
        }
        if (LooksLikeDecl())
        {
            var typeTok = Peek;
            string typeName = ParseTypeName();
            var id = Eat(TokKind.Id).Text;
            Expr? init = null;
            if (Accept("=")) init = ParseExpr();
            Accept(";");
            return new VarStmt(typeName, id, init, typeTok.Line, typeTok.Col);
        }
        var expr = ParseExpr();
        if (Accept("="))
        {
            var v = ParseExpr();
            Accept(";");
            return new AssignStmt(expr, v);
        }
        if (Accept("+="))
        {
            var v = ParseExpr();
            Accept(";");
            return new AssignStmt(expr, new BinaryExpr("+", expr, v));
        }
        if (Accept("-="))
        {
            var v = ParseExpr();
            Accept(";");
            return new AssignStmt(expr, new BinaryExpr("-", expr, v));
        }
        if (Accept("*="))
        {
            var v = ParseExpr();
            Accept(";");
            return new AssignStmt(expr, new BinaryExpr("*", expr, v));
        }
        if (Accept("/="))
        {
            var v = ParseExpr();
            Accept(";");
            return new AssignStmt(expr, new BinaryExpr("/", expr, v));
        }
        if (Accept("%="))
        {
            var v = ParseExpr();
            Accept(";");
            return new AssignStmt(expr, new BinaryExpr("%", expr, v));
        }
        if (Accept("++"))
            return new AssignStmt(expr, new BinaryExpr("+", expr, new NumExpr("1")));
        if (Accept("--"))
            return new AssignStmt(expr, new BinaryExpr("-", expr, new NumExpr("1")));
        Accept(";");
        return new ExprStmt(expr);
    }

    // Full remaining methods (ParseExpr through Eat) are identical to the version in /tmp/Parser.cs
    private Expr ParseExpr() => ParseOr();
    private Expr ParseOr() { var l = ParseAnd(); while (Accept("||")) l = new BinaryExpr("||", l, ParseAnd()); return l; }
    private Expr ParseAnd() { var l = ParseEq(); while (Accept("&&")) l = new BinaryExpr("&&", l, ParseEq()); return l; }
    private Expr ParseEq() { var l = ParseRel(); while (true) { if (Accept("==")) l = new BinaryExpr("==", l, ParseRel()); else if (Accept("!=")) l = new BinaryExpr("!=", l, ParseRel()); else break; } return l; }
    private Expr ParseRel() { var l = ParseAdd(); while (Check("<") || Check(">") || Check("<=") || Check(">=")) { var op = Eat(TokKind.Sym).Text; l = new BinaryExpr(op, l, ParseAdd()); } return l; }
    private Expr ParseAdd() { var l = ParseMul(); while (true) { if (Accept("+")) l = new BinaryExpr("+", l, ParseMul()); else if (Accept("-")) l = new BinaryExpr("-", l, ParseMul()); else break; } return l; }
    private Expr ParseMul() { var l = ParseUnary(); while (true) { if (Accept("*")) l = new BinaryExpr("*", l, ParseUnary()); else if (Accept("/")) l = new BinaryExpr("/", l, ParseUnary()); else if (Accept("%")) l = new BinaryExpr("%", l, ParseUnary()); else break; } return l; }
    private Expr ParseUnary() { if (Accept("!")) return new UnaryExpr("!", ParseUnary()); if (Accept("-")) return new UnaryExpr("-", ParseUnary()); return ParsePost(); }
    private Expr ParsePost() 
    { 
        var e = ParsePrimary(); 
        while (true) 
        { 
            if (Accept(".")) 
            { 
                var id = Eat(TokKind.Id); 
                e = new MemberExpr(e, id.Text, id.Line, id.Col); 
            }
            else if (Accept("["))
            {
                Expr idx;
                if (Accept("^"))
                    idx = new FromEndExpr(ParseExpr(), Peek.Line, Peek.Col);
                else
                    idx = ParseExpr();
                Expect("]");
                e = new IndexExpr(e, idx, e.Line, e.Col);
            }
            else if (Accept("(")) 
            { 
                var line = e.Line; 
                var col = e.Col; 
                var args = new List<Expr>(); 
                if (!Check(")"))
                { 
                    do 
                        args.Add(ParseExpr()); 
                    while (Accept(",")); 
                } 
                Expect(")"); 
                e = new CallExpr(e, args, line, col); 
            } 
            else 
                break; 
        } 
        return e; 
    }
    private Expr? TryParseLambda()
    {
        if (!Check("(")) return null;
        int save = _i;
        try
        {
            Eat(TokKind.Sym); // (
            var ps = ParseParams();
            Expect(")");
            if (!Accept("=>")) { _i = save; return null; }
            Expect("{");
            var body = ParseBlock();
            var tok = tokens[save];
            return new LambdaExpr(ps, body, tok.Line, tok.Col);
        }
        catch
        {
            _i = save;
            return null;
        }
    }

    // в ParsePrimary:
    private Expr ParsePrimary()
    {
        var lambda = TryParseLambda();
        if (lambda is not null) return lambda;
        if (Accept("("))
        {
            var grouped = ParseExpr();
            Expect(")");
            return grouped;
        }
        if (CheckId("new")) 
        { 
            var newTok = Eat(TokKind.Id);
            string type = ParseTypeName();
            if (type.EndsWith("[]", StringComparison.Ordinal) && !Check("("))
                return new NewExpr(type, [], newTok.Line, newTok.Col);

            Expect("(");
            var args = new List<Expr>(); 
            if (!Check(")")) 
            { 
                do args.Add(ParseExpr()); 
                while (Accept(",")); 
            } 
            Expect(")"); 
            return new NewExpr(type, args, newTok.Line, newTok.Col); 
        }
        if (CheckId("this")) { var t = Eat(TokKind.Id); return new ThisExpr(t.Line, t.Col); }
        if (CheckId("true")) { var t = Eat(TokKind.Id); return new BoolExpr(true, t.Line, t.Col); }
        if (CheckId("false")) { var t = Eat(TokKind.Id); return new BoolExpr(false, t.Line, t.Col); }
        if (CheckId("null")) { var t = Eat(TokKind.Id); return new NullExpr(t.Line, t.Col); }
        var tok = tokens[_i++]; return tok.Kind switch { TokKind.Id => new NameExpr(tok.Text, tok.Line, tok.Col), TokKind.Num => new NumExpr(tok.Text, tok.Line, tok.Col), TokKind.Str => new StrExpr(tok.Text, tok.Line, tok.Col), TokKind.Raw => new RawExpr(tok.Text, tok.Line, tok.Col), _ => throw new ParseException($"неожиданный токен {tok.Text}", tok.Line, tok.Col), };
    }
    private void SkipVis() { while (CheckId("public") || CheckId("private")) Eat(TokKind.Id); }
    private bool CheckId(string id) => Peek.Kind == TokKind.Id && Peek.Text == id;
    private bool Check(string sym) => Peek.Kind == TokKind.Sym && Peek.Text == sym;
    private bool Accept(string sym) { if (!Check(sym)) return false; _i++; return true; }
    private void Expect(string sym) { if (!Accept(sym)) throw new ParseException($"ожидалось «{sym}», получено {Peek.Text}", Peek.Line, Peek.Col); }
    private void EatId(string id) { if (!CheckId(id)) throw new ParseException($"ожидалось {id}", Peek.Line, Peek.Col); _i++; }
    private Token Eat(TokKind k) { var t = tokens[_i++]; if (t.Kind != k) throw new ParseException($"ожидалось {k}, получено {t.Text}", t.Line, t.Col); return t; }
}
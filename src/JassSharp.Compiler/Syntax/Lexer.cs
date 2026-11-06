namespace JassSharp.Syntax;

public enum TokKind { Id, Num, Str, Raw, Sym, Eof }

public readonly record struct Token(TokKind Kind, string Text, int Line, int Col);

public sealed class Lexer(string source)
{
    private int _i;
    private int _line = 1;
    private int _col = 1;

    public List<Token> Lex()
    {
        var list = new List<Token>();
        while (_i < source.Length)
        {
            var c = source[_i];
            if (c == '\n') { _i++; _line++; _col = 1; continue; }
            if (char.IsWhiteSpace(c)) { Advance(); continue; }
            if (c == '/' && Peek(1) == '/')
            {
                while (_i < source.Length && source[_i] != '\n') Advance();
                continue;
            }
            if (c == '/' && Peek(1) == '*')
            {
                Advance(); Advance();
                while (_i < source.Length && !(source[_i] == '*' && Peek(1) == '/'))
                {
                    if (source[_i] == '\n') { _i++; _line++; _col = 1; }
                    else Advance();
                }
                if (_i < source.Length) { Advance(); Advance(); }
                continue;
            }
            var line = _line;
            var col = _col;
            if (c == '\'')
            {
                Advance();
                list.Add(new Token(TokKind.Raw, ReadUntil('\''), line, col));
                continue;
            }
            if (c == '"')
            {
                Advance();
                list.Add(new Token(TokKind.Str, ReadString(), line, col));
                continue;
            }
            if (char.IsDigit(c) || (c == '.' && char.IsDigit(Peek(1))))
            {
                var start = _i;
                while (_i < source.Length && (char.IsDigit(source[_i]) || source[_i] == '.')) Advance();
                list.Add(new Token(TokKind.Num, source[start.._i], line, col));
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                var start = _i;
                while (_i < source.Length && (char.IsLetterOrDigit(source[_i]) || source[_i] == '_')) Advance();
                list.Add(new Token(TokKind.Id, source[start.._i], line, col));
                continue;
            }
            var two = _i + 1 < source.Length ? source.Substring(_i, 2) : "";
            if (two is "==" or "!=" or "<=" or ">=" or "&&" or "||" or "+=" or "-=" or "*=" or "/=" or "++" or "--" or "=>")
            {
                list.Add(new Token(TokKind.Sym, two, line, col));
                Advance(); Advance();
                continue;
            }
            list.Add(new Token(TokKind.Sym, c.ToString(), line, col));
            Advance();
        }
        list.Add(new Token(TokKind.Eof, "", _line, _col));
        return list;
    }

    private void Advance() { _i++; _col++; }

    private char Peek(int d) => _i + d < source.Length ? source[_i + d] : '\0';

    private string ReadUntil(char end)
    {
        var s = "";
        while (_i < source.Length && source[_i] != end) { s += source[_i]; Advance(); }
        if (_i < source.Length) Advance();
        return s;
    }

    private string ReadString()
    {
        var s = "";
        while (_i < source.Length && source[_i] != '"')
        {
            if (source[_i] == '\\' && _i + 1 < source.Length)
            {
                Advance();
                var n = source[_i];
                s += n switch { 'n' => '\n', 't' => '\t', _ => n };
                Advance();
            }
            else { s += source[_i]; Advance(); }
        }
        if (_i < source.Length) Advance();
        return s;
    }
}

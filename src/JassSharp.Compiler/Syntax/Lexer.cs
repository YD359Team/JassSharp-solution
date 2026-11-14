using System.Text;

namespace JassSharp.Syntax;

public enum TokKind { Id, Num, Str, Interpolated, Raw, Sym, Eof }

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
                    Advance();
                if (_i < source.Length) { Advance(); Advance(); }
                continue;
            }

            var line = _line;
            var col = _col;

            if (c == '$' && Peek(1) == '"')
            {
                Advance(); // $
                bool raw = Peek(1) == '"' && Peek(2) == '"';
                if (raw)
                {
                    Advance(); Advance(); Advance();
                    list.Add(new Token(TokKind.Interpolated, ReadInterpolatedString("\"\"\"", true, line, col), line, col));
                }
                else
                {
                    Advance(); // opening quote
                    list.Add(new Token(TokKind.Interpolated, ReadInterpolatedString("\"", false, line, col), line, col));
                }
                continue;
            }

            if (c == '"' && Peek(1) == '"' && Peek(2) == '"')
            {
                Advance(); Advance(); Advance();
                list.Add(new Token(TokKind.Str, ReadRawString(line, col), line, col));
                continue;
            }

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
                if (c == '0' && (Peek(1) is 'x' or 'X'))
                {
                    Advance();
                    Advance();
                    int digitsStart = _i;
                    while (_i < source.Length && Uri.IsHexDigit(source[_i]))
                        Advance();

                    if (_i == digitsStart)
                        throw new ParseException("hex-литерал должен содержать хотя бы одну шестнадцатеричную цифру", line, col);

                    if (_i < source.Length && (char.IsLetterOrDigit(source[_i]) || source[_i] == '_'))
                        throw new ParseException("некорректный hex-литерал", line, col);
                }
                else
                {
                    if (c == '.')
                        Advance();

                    while (_i < source.Length && char.IsDigit(source[_i]))
                        Advance();

                    if (_i < source.Length && source[_i] == '.' && Peek(1) != '.')
                    {
                        Advance();
                        while (_i < source.Length && char.IsDigit(source[_i]))
                            Advance();
                    }

                    if (_i < source.Length && source[_i] is 'e' or 'E')
                    {
                        Advance();
                        if (_i < source.Length && source[_i] is '+' or '-')
                            Advance();

                        int exponentStart = _i;
                        while (_i < source.Length && char.IsDigit(source[_i]))
                            Advance();

                        if (_i == exponentStart)
                            throw new ParseException("экспонента числового литерала должна содержать цифры", line, col);
                    }
                }

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
            if (two is "==" or "!=" or "<=" or ">=" or "&&" or "||" or "+=" or "-=" or "*=" or "/=" or "%=" or "++" or "--" or "=>" or "<<" or ">>" or "&=" or "|=" or "^=")
            {
                if ((two is "<<" or ">>") && Peek(2) == '=')
                {
                    var three = source.Substring(_i, 3);
                    list.Add(new Token(TokKind.Sym, three, line, col));
                    Advance(); Advance(); Advance();
                }
                else
                {
                    list.Add(new Token(TokKind.Sym, two, line, col));
                    Advance(); Advance();
                }
                continue;
            }
            list.Add(new Token(TokKind.Sym, c.ToString(), line, col));
            Advance();
        }
        list.Add(new Token(TokKind.Eof, "", _line, _col));
        return list;
    }

    private void Advance()
    {
        if (source[_i] == '\n')
        {
            _i++;
            _line++;
            _col = 1;
        }
        else
        {
            _i++;
            _col++;
        }
    }

    private char Peek(int d) => _i + d < source.Length ? source[_i + d] : '\0';

    private string ReadUntil(char end)
    {
        var builder = new StringBuilder();
        while (_i < source.Length && source[_i] != end)
        {
            builder.Append(source[_i]);
            Advance();
        }
        if (_i >= source.Length)
            throw new ParseException("незакрытый rawcode-литерал", _line, _col);
        Advance();
        return builder.ToString();
    }

    private string ReadString()
    {
        var builder = new StringBuilder();
        while (_i < source.Length && source[_i] != '"')
        {
            if (source[_i] is '\n' or '\r')
                throw new ParseException("обычная строка не может содержать перевод строки; используйте raw-строку в тройных кавычках", _line, _col);
            if (source[_i] == '\\' && _i + 1 < source.Length)
            {
                Advance();
                char escaped = source[_i];
                builder.Append(escaped switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '0' => '\0',
                    '"' => '"',
                    '\\' => '\\',
                    _ => escaped
                });
                Advance();
            }
            else
            {
                builder.Append(source[_i]);
                Advance();
            }
        }
        if (_i >= source.Length)
            throw new ParseException("незакрытая строка", _line, _col);
        Advance();
        return builder.ToString();
    }

    private string ReadRawString(int startLine, int startCol)
    {
        var start = _i;
        while (_i < source.Length && !(source[_i] == '"' && Peek(1) == '"' && Peek(2) == '"'))
            Advance();
        if (_i >= source.Length)
            throw new ParseException("незакрытая raw-строка", _line, _col);
        var value = source[start.._i];
        Advance(); Advance(); Advance();
        return TrimRawIndentation(value, startLine, startCol);
    }

    private string ReadInterpolatedString(string terminator, bool raw, int startLine, int startCol)
    {
        var builder = new StringBuilder();
        int braceDepth = 0;
        char expressionQuote = '\0';
        bool escaped = false;

        while (_i < source.Length)
        {
            if (braceDepth == 0 && source.AsSpan(_i).StartsWith(terminator, StringComparison.Ordinal))
            {
                for (int i = 0; i < terminator.Length; i++) Advance();
                return raw ? TrimRawIndentation(builder.ToString(), startLine, startCol) : builder.ToString();
            }

            char c = source[_i];
            if (braceDepth == 0)
            {
                if (!raw && c is ('\n' or '\r'))
                    throw new ParseException("обычная интерполируемая строка не может содержать перевод строки; используйте многострочную raw-интерполяцию", _line, _col);

                if (!raw && c == '\\' && _i + 1 < source.Length)
                {
                    Advance();
                    char next = source[_i];
                    builder.Append(next switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        '0' => '\0',
                        '"' => '"',
                        '\\' => '\\',
                        _ => next
                    });
                    Advance();
                    continue;
                }

                if (c == '{')
                {
                    if (Peek(1) == '{')
                    {
                        builder.Append("{{");
                        Advance(); Advance();
                    }
                    else
                    {
                        braceDepth = 1;
                        builder.Append(c);
                        Advance();
                    }
                    continue;
                }

                if (c == '}' && Peek(1) == '}')
                {
                    builder.Append("}}");
                    Advance(); Advance();
                    continue;
                }

                builder.Append(c);
                Advance();
                continue;
            }

            if (expressionQuote != '\0')
            {
                builder.Append(c);
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == expressionQuote) expressionQuote = '\0';
                Advance();
                continue;
            }

            if (c is '"' or '\'')
            {
                expressionQuote = c;
                builder.Append(c);
                Advance();
                continue;
            }

            if (c == '{') braceDepth++;
            else if (c == '}') braceDepth--;
            builder.Append(c);
            Advance();
        }

        throw new ParseException("незакрытая строка или интерполяция", _line, _col);
    }

    private static string TrimRawIndentation(string value, int startLine, int startCol)
    {
        if (value.Length == 0 || (value[0] != '\n' && value[0] != '\r'))
            return value;

        int firstLineBreak = value[0] == '\r' && value.Length > 1 && value[1] == '\n' ? 2 : 1;
        int lastLineBreak = value.LastIndexOf('\n');
        if (lastLineBreak < firstLineBreak)
            return value[firstLineBreak..];

        string closingIndent = value[(lastLineBreak + 1)..];
        if (closingIndent.Any(c => c is not (' ' or '\t')))
            return value[firstLineBreak..];

        string body = value[firstLineBreak..lastLineBreak];
        string[] lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                lines[i] = "";
                continue;
            }

            if (!lines[i].StartsWith(closingIndent, StringComparison.Ordinal))
                throw new ParseException("отступ строки raw-литерала меньше отступа закрывающего разделителя", startLine, startCol);

            lines[i] = lines[i][closingIndent.Length..];
        }
        return string.Join("\n", lines);
    }
}

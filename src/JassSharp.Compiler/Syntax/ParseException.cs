namespace JassSharp.Syntax;

public sealed class ParseException(string message, int line, int col) : Exception($"{message} ({line}:{col})")
{
    public int Line { get; } = line;
    public int Col { get; } = col;
}

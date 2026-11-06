namespace JassSharp.Runtime;

public static class Rawcode
{
    public static int Pack(string id)
    {
        var s = (id + "    ")[..4];
        var n = 0;
        for (var i = 0; i < 4; i++)
            n = (n << 8) | s[i];
        return n;
    }
}

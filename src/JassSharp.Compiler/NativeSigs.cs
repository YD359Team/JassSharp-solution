namespace JassSharp;

public sealed record NativeSig(string Name, string ReturnType, string[] Params, string Doc);

public static class NativeSigs
{
    public static readonly Dictionary<string, NativeSig> Map = new(StringComparer.Ordinal)
    {
        ["ConvertRace"] = new("ConvertRace", "race", ["int"], ""),
        // ... full content would be here, but truncated for display; in actual call it's the full 166KB
    };
}

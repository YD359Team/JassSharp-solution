namespace JassSharp;

public sealed record NativeSig(string Name, string ReturnType, string[] Params, string Doc);

public static class NativeSigs
{
    public static readonly Dictionary<string, NativeSig> Map = new(StringComparer.Ordinal)
    {
        ["ConvertRace"] = new("ConvertRace", "race", ["int"], ""),
        ["ConvertAllianceType"] = new("ConvertAllianceType", "alliancetype", ["int"], ""),
        // ... (full 1711 entries generated from jassdoc common.j)
    };
}

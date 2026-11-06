namespace JassSharp.Runtime;

public enum MemberKind { GetSet, GetOnly, Method }

public sealed record NativeMember(string Type, string Name, MemberKind Kind, string Read, string? Write);

public static partial class NativeTable
{
    public static IReadOnlyList<NativeMember> Members => Generated;

    public static readonly Dictionary<string, string> Constructors = new()
    {
        ["unit"] = "CreateUnit",
        ["trigger"] = "CreateTrigger",
        ["timer"] = "CreateTimer",
        ["group"] = "CreateGroup",
        ["effect"] = "AddSpecialEffect",
        ["item"] = "CreateItem",
        ["location"] = "Location",
        ["rect"] = "Rect",
        ["force"] = "CreateForce",
        ["region"] = "CreateRegion",
        ["sound"] = "CreateSound",
        ["quest"] = "CreateQuest",
        ["leaderboard"] = "CreateLeaderboard",
        ["multiboard"] = "CreateMultiboard",
        ["dialog"] = "DialogCreate",
        ["texttag"] = "CreateTextTag",
        ["lightning"] = "AddLightning",
        ["image"] = "CreateImage",
        ["ubersplat"] = "CreateUbersplat",
        ["fogmodifier"] = "CreateFogModifierRect",
        ["hashtable"] = "InitHashtable",
    };

    public static NativeMember? Find(string type, string name) =>
        Members.FirstOrDefault(m => m.Type == type && m.Name == name);
}

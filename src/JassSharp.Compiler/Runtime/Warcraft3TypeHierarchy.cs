namespace JassSharp.Runtime;

/// <summary>
/// Warcraft III's native handle type hierarchy. Types not listed with a
/// specialized parent are direct handle descendants when present in the
/// native type catalog.
/// </summary>
public static class Warcraft3TypeHierarchy
{
    private static readonly IReadOnlyDictionary<string, string> Parents =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["agent"] = "handle",
            ["event"] = "agent",
            ["widget"] = "agent",
            ["unit"] = "widget",
            ["destructable"] = "widget",
            ["item"] = "widget",
            ["ability"] = "agent",
            ["buff"] = "ability",
            ["force"] = "agent",
            ["group"] = "agent",
            ["trigger"] = "agent",
            ["triggercondition"] = "agent",
            ["triggeraction"] = "handle",
            ["timer"] = "agent",
            ["location"] = "agent",
            ["region"] = "agent",
            ["rect"] = "agent",
            ["boolexpr"] = "agent",
            ["conditionfunc"] = "boolexpr",
            ["filterfunc"] = "boolexpr",
            ["sound"] = "agent",
            ["unitpool"] = "handle",
            ["itempool"] = "handle",
            ["effect"] = "agent",
            ["weathereffect"] = "handle",
            ["terraindeformation"] = "handle",
            ["fogmodifier"] = "agent",
            ["dialog"] = "agent",
            ["button"] = "agent",
            ["quest"] = "agent",
            ["questitem"] = "agent",
            ["defeatcondition"] = "agent",
            ["timerdialog"] = "agent",
            ["leaderboard"] = "agent",
            ["multiboard"] = "agent",
            ["multiboarditem"] = "agent",
            ["texttag"] = "handle",
            ["lightning"] = "handle",
            ["minimapicon"] = "handle",
            ["framehandle"] = "handle",
            ["commandbuttoneffect"] = "handle"
        };

    public static bool IsHandleType(string type)
    {
        if (type.EndsWith("[]", StringComparison.Ordinal))
            return false;

        return type == "handle" || NativeCatalog.Types.Contains(type);
    }

    public static bool IsAssignableFrom(string actualType, string expectedType)
    {
        if (actualType == expectedType)
            return true;

        if (!IsHandleType(actualType) || !IsHandleType(expectedType))
            return false;

        string current = actualType;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (visited.Add(current) && Parents.TryGetValue(current, out string? parent))
        {
            if (parent == expectedType)
                return true;
            current = parent;
        }

        // Most JASS enum/descriptor handles derive directly from handle.
        return expectedType == "handle";
    }
}

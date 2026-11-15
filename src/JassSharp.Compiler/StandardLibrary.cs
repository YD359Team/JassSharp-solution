using System.Reflection;
using JassSharp.Syntax;

namespace JassSharp;

internal static class StandardLibrary
{
    public static IReadOnlyList<(string FileName, string Source)> LoadSources()
    {
        Assembly assembly = typeof(StandardLibrary).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".w3js", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name =>
            {
                using Stream stream = assembly.GetManifestResourceStream(name)
                    ?? throw new InvalidOperationException($"Не удалось открыть ресурс стандартной библиотеки {name}");
                using StreamReader reader = new(stream);
                string fileName = string.Join(".", name.Split('.').TakeLast(2));
                return (fileName, reader.ReadToEnd());
            })
            .ToArray();
    }

    internal static NsUnit MergeUnits(IEnumerable<NsUnit> sourceUnits)
    {
        List<NsUnit> units = sourceUnits.ToList();
        if (units.Count == 0)
            throw new ArgumentException("at least one namespace unit is required", nameof(sourceUnits));

        string namespaceName = units[0].Name;
        if (units.Any(unit => !string.Equals(unit.Name, namespaceName, StringComparison.Ordinal)))
            throw new ArgumentException("all namespace units must have the same name", nameof(sourceUnits));

        if (units.Count == 1)
            return units[0];

        return new NsUnit(
            namespaceName,
            units.SelectMany(unit => unit.Usings).Distinct(StringComparer.Ordinal).ToList(),
            units.SelectMany(unit => unit.Consts).ToList(),
            units.SelectMany(unit => unit.Classes).ToList(),
            units.SelectMany(unit => unit.Functions).ToList(),
            units.SelectMany(unit => unit.Fields).ToList(),
            units.SelectMany(unit => unit.Enums).ToList());
    }

    public static IEnumerable<NsUnit> LoadUnits()
    {
        var parsedUnits = LoadSources()
            .Select(ParseUnit)
            .GroupBy(unit => unit.Name, StringComparer.Ordinal);

        foreach (var group in parsedUnits)
            yield return MergeUnits(group);
    }

    private static NsUnit ParseUnit((string FileName, string Source) source)
    {
        try
        {
            return new Parser(new Lexer(source.Source).Lex()).ParseNamespace();
        }
        catch (ParseException ex)
        {
            throw new InvalidDataException(
                $"standard-library source '{source.FileName}':{ex.Line}:{ex.Col}: {ex.Message}",
                ex);
        }
    }
}

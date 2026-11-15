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

    public static IEnumerable<NsUnit> LoadUnits()
    {
        var parsedUnits = LoadSources()
            .Select(ParseUnit)
            .GroupBy(unit => unit.Name, StringComparer.Ordinal);

        foreach (var group in parsedUnits)
        {
            var units = group.ToList();
            if (units.Count == 1)
            {
                yield return units[0];
                continue;
            }

            yield return new NsUnit(
                group.Key,
                units.SelectMany(unit => unit.Usings).Distinct(StringComparer.Ordinal).ToList(),
                units.SelectMany(unit => unit.Consts).ToList(),
                units.SelectMany(unit => unit.Classes).ToList(),
                units.SelectMany(unit => unit.Functions).ToList(),
                units.SelectMany(unit => unit.Fields).ToList(),
                units.SelectMany(unit => unit.Enums).ToList());
        }
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

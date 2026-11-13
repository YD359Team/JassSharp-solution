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
        foreach (var source in LoadSources())
            yield return new Parser(new Lexer(source.Source).Lex()).ParseNamespace();
    }
}

using JassSharp;
using System.Text;
using System.Text.Json;
using JassSharp.Emit;
using JassSharp.Syntax;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

if (args.Length == 0 ||
    args[0] is not ("build" or "check" or "map" or "symbols") ||
    (args[0] != "map" && args.Length < 2))
{
    Console.Error.WriteLine("usage:");
    Console.Error.WriteLine("  jasssharp build <file.w3js> [--target lua]");
    Console.Error.WriteLine("  jasssharp check <file.w3js>");
    Console.Error.WriteLine("  jasssharp map [--project dir] [--config file.json] [--run]");
    Console.Error.WriteLine("  jasssharp symbols <file.w3js> [--project dir]");
    return 1;
}

var cmd = args[0];

if (cmd == "check")
{
    var path = Path.GetFullPath(args[1]);
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"file not found: {path}");
        return 1;
    }

    var diagnostics = new List<JassSharp.Diagnostic>();

    try
    {
        var unit = new Parser(new Lexer(File.ReadAllText(path)).Lex()).ParseNamespace();
        var projectDirectory = FindProjectDirectory(path);
        var allUnits = MapBuild.LoadProject(projectDirectory);
        var imports = MapBuild.ResolveImports(unit, allUnits);

        diagnostics.AddRange(JassSharp.Checker.Check(unit, path, imports));
    }
    catch (ParseException ex)
    {
        diagnostics.Add(new JassSharp.Diagnostic(ex.Line, ex.Col, "error", ex.Message, path));
    }
    catch (InvalidDataException ex)
    {
        diagnostics.Add(new JassSharp.Diagnostic(1, 1, "error", ex.Message, path));
    }
    catch (Exception ex)
    {
        diagnostics.Add(new JassSharp.Diagnostic(1, 1, "error", ex.Message, path));
    }

    Console.WriteLine(JsonSerializer.Serialize(diagnostics));
    return diagnostics.Any(d => d.Severity == "error") ? 1 : 0;
}

if (cmd == "map")
{
    var project = Directory.GetCurrentDirectory();
    var run = false;
    string? configName = null;

    for (var i = 1; i < args.Length; i++)
    {
        if (args[i] == "--project" && i + 1 < args.Length)
            project = args[++i];
        else if (args[i] == "--config" && i + 1 < args.Length)
            configName = args[++i];
        else if (args[i] == "--run")
            run = true;
        else
        {
            Console.Error.WriteLine($"unknown or incomplete map option: {args[i]}");
            return 1;
        }
    }

    return MapBuild.Run(project, run, configName);
}

if (cmd == "symbols")
{
    var project = Directory.GetCurrentDirectory();
    for (var i = 2; i < args.Length; i++)
    {
        if (args[i] == "--project" && i + 1 < args.Length)
            project = args[++i];
        else
        {
            Console.Error.WriteLine($"unknown or incomplete symbols option: {args[i]}");
            return 1;
        }
    }

    try
    {
        var all = MapBuild.LoadProject(project);
        var currentFile = args[1];
        if (!File.Exists(currentFile))
        {
            Console.Error.WriteLine($"file not found: {currentFile}");
            return 1;
        }

        var current = new Parser(new Lexer(File.ReadAllText(currentFile)).Lex()).ParseNamespace();
        var imports = MapBuild.ResolveImports(current, all);

        var result = new
        {
            namespaces = all.ToDictionary(
                kv => kv.Key,
                kv => new
                {
                    classes = kv.Value.Classes.Select(c => c.Name),
                    functions = kv.Value.Functions.Select(f => f.Name),
                    consts = kv.Value.Consts.Select(c => c.Name),
                    fields = kv.Value.Fields.Select(f => f.Name)
                }),
            currentImports = imports.Select(u => u.Name).ToList()
        };

        Console.WriteLine(JsonSerializer.Serialize(result));
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"symbols: {ex.Message}");
        return 1;
    }
}

var file = Path.GetFullPath(args[1]);
if (!File.Exists(file))
{
    Console.Error.WriteLine($"file not found: {file}");
    return 1;
}

var target = "lua";
for (var i = 2; i < args.Length; i++)
{
    if (args[i] == "--target" && i + 1 < args.Length)
        target = args[++i].Trim().ToLowerInvariant();
    else
    {
        Console.Error.WriteLine($"unknown or incomplete build option: {args[i]}");
        return 1;
    }
}

if (target != "lua")
{
    Console.Error.WriteLine($"unsupported target '{target}'. JASS# supports only the Lua target.");
    return 1;
}

try
{
    var unit = new Parser(new Lexer(File.ReadAllText(file)).Lex()).ParseNamespace();
    var projectDirectory = FindProjectDirectory(file);
    var mode = ReadProjectMode(projectDirectory);
    if (mode is not ("wc3" or "headless"))
        throw new InvalidDataException($"unsupported project mode '{mode}'. Supported modes are 'wc3' and 'headless'.");
    var allUnits = MapBuild.LoadProject(projectDirectory);
    var imports = MapBuild.ResolveImports(unit, allUnits);
    foreach (var sourceUnit in imports.Append(unit))
    {
        var sourceImports = imports.Where(dependency => dependency.Name != sourceUnit.Name);
        var diagnostics = Checker.Check(
            sourceUnit,
            sourceUnit.Name == unit.Name ? file : null,
            sourceImports);
        foreach (var diagnostic in diagnostics)
            Console.Error.WriteLine($"{sourceUnit.Name}:{diagnostic.Line}:{diagnostic.Col}: {diagnostic.Severity}: {diagnostic.Message}");
        if (diagnostics.Any(diagnostic => diagnostic.Severity == "error"))
            return 1;
    }

    var sourceDirectory = Path.GetDirectoryName(file) ?? Directory.GetCurrentDirectory();
    var outputDirectory = Path.Combine(sourceDirectory, "bin");
    Directory.CreateDirectory(outputDirectory);

    var outputPath = Path.Combine(
        outputDirectory,
        Path.GetFileNameWithoutExtension(file) + ".lua");

    var generatedParts = new List<string>();
    foreach (var imported in imports)
        generatedParts.Add(new LuaEmitter().Emit(imported, imports.Where(dependency => dependency.Name != imported.Name)));
    generatedParts.Add(new LuaEmitter().Emit(unit, imports, headless: mode == "headless"));
    File.WriteAllText(outputPath, string.Join("\n", generatedParts), new UTF8Encoding(false));
    Console.WriteLine(outputPath);
}
catch (ParseException ex)
{
    Console.Error.WriteLine($"error at {ex.Line}:{ex.Col}: {ex.Message}");
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

return 0;


static string ReadProjectMode(string projectDirectory)
{
    string configPath = Path.Combine(projectDirectory, "jasssharp.json");
    if (!File.Exists(configPath))
        return "wc3";

    using var document = JsonDocument.Parse(File.ReadAllText(configPath));
    var root = document.RootElement;
    if (!root.TryGetProperty("mode", out var modeElement) || modeElement.ValueKind == JsonValueKind.Null)
        return "wc3";
    if (modeElement.ValueKind != JsonValueKind.String)
        throw new InvalidDataException("project configuration property 'mode' must be a string");

    return modeElement.GetString()?.Trim().ToLowerInvariant() ?? "wc3";
}

static string FindProjectDirectory(string sourceFile)
{
    var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(sourceFile))!);

    for (DirectoryInfo? current = directory; current is not null; current = current.Parent)
    {
        if (Directory.Exists(Path.Combine(current.FullName, "src")))
            return current.FullName;
    }

    return directory.FullName;
}

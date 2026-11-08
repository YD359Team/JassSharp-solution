using System.Text;
using System.Text.Json;
using JassSharp.Emit;
using JassSharp.Syntax;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

if (args.Length < 2 || args[0] is not ("build" or "check" or "map"))
{
    Console.Error.WriteLine("usage:");
    Console.Error.WriteLine("  jasssharp build <file.w3js> [--target vjass|lua|both]");
    Console.Error.WriteLine("  jasssharp check <file.w3js>");
    Console.Error.WriteLine("  jasssharp map [--project dir] [--config file.json] [--run]");
    return 1;
}

var cmd = args[0];

if (cmd == "check")
{
    var path = args[1];
    if (!File.Exists(path)) { Console.Error.WriteLine($"file not found: {path}"); return 1; }
    var diags = new List<JassSharp.Diagnostic>();
    try
    {
        var unit = new Parser(new Lexer(File.ReadAllText(path)).Lex()).ParseNamespace();
        diags.AddRange(JassSharp.Checker.Check(unit, path));
    }
    catch (ParseException ex)
    {
        diags.Add(new JassSharp.Diagnostic(ex.Line, ex.Col, "error", ex.Message, path));
    }
    catch (Exception ex)
    {
        diags.Add(new JassSharp.Diagnostic(1, 1, "error", ex.Message));
    }
    Console.WriteLine(JsonSerializer.Serialize(diags));
    return diags.Any(d => d.Severity == "error") ? 1 : 0;
}

if (cmd == "map")
{
    var project = Directory.GetCurrentDirectory();
    var run = false;
    string? configName = null;
    for (var i = 1; i < args.Length; i++)
    {
        if (args[i] == "--project" && i + 1 < args.Length) project = args[++i];
        else if (args[i] == "--config" && i + 1 < args.Length) configName = args[++i];
        else if (args[i] == "--run") run = true;
    }
    return MapBuild.Run(project, run, configName);
}

if (cmd == "symbols")
{
    var project = Directory.GetCurrentDirectory();
    for (var i = 1; i < args.Length; i++)
        if (args[i] == "--project" && i + 1 < args.Length) project = args[++i];

    var all = MapBuild.LoadProject(project);
    var currentFile = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : null;

    NsUnit? current = null;
    if (currentFile is not null && File.Exists(currentFile))
        current = new Parser(new Lexer(File.ReadAllText(currentFile)).Lex()).ParseNamespace();

    var imports = current is null ? new List<NsUnit>() : MapBuild.ResolveImports(current, all);

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

var file = args[1];
if (!File.Exists(file)) { Console.Error.WriteLine($"file not found: {file}"); return 1; }
var target = "both";
for (var i = 2; i < args.Length - 1; i++)
    if (args[i] == "--target") target = args[i + 1];

try
{
    var unit = new Parser(new Lexer(File.ReadAllText(file)).Lex()).ParseNamespace();
    var srcDir = Path.GetDirectoryName(Path.GetFullPath(file)) ?? ".";
    var bin = Path.Combine(srcDir, "bin");
    Directory.CreateDirectory(bin);
    var stem = Path.GetFileNameWithoutExtension(file);
    if (target is "vjass" or "both")
    {
        var outPath = Path.Combine(bin, stem + ".j");
        File.WriteAllText(outPath, new VjassEmitter().Emit(unit));
        Console.WriteLine(outPath);
    }
    if (target is "lua" or "both")
    {
        var outPath = Path.Combine(bin, stem + ".lua");
        File.WriteAllText(outPath, new LuaEmitter().Emit(unit));
        Console.WriteLine(outPath);
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}
return 0;
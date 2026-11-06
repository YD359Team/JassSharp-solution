using System.Text.Json;
using JassSharp.Emit;
using JassSharp.Syntax;

if (args.Length < 2 || args[0] is not ("build" or "check" or "map"))
{
    Console.Error.WriteLine("usage:");
    Console.Error.WriteLine("  jasssharp build <file.w3js> [--target vjass|lua|both]");
    Console.Error.WriteLine("  jasssharp check <file.w3js>");
    Console.Error.WriteLine("  jasssharp map [--project dir] [--run]");
    return 1;
}

var cmd = args[0];

if (cmd == "check")
{
    var path = args[1];
    if (!File.Exists(path)) { Console.Error.WriteLine($"файл не найден: {path}"); return 1; }
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
    for (var i = 1; i < args.Length; i++)
    {
        if (args[i] == "--project" && i + 1 < args.Length) project = args[++i];
        else if (args[i] == "--run") run = true;
    }
    return MapBuild.Run(project, run);
}

// build
var file = args[1];
if (!File.Exists(file)) { Console.Error.WriteLine($"файл не найден: {file}"); return 1; }
var target = "both";
for (var i = 2; i < args.Length - 1; i++)
    if (args[i] == "--target") target = args[i + 1];

try
{
    var unit = new Parser(new Lexer(File.ReadAllText(file)).Lex()).ParseNamespace();
    var dir = Path.GetDirectoryName(Path.GetFullPath(file)) ?? ".";
    var stem = Path.GetFileNameWithoutExtension(file);
    if (target is "vjass" or "both")
    {
        var outPath = Path.Combine(dir, stem + ".j");
        File.WriteAllText(outPath, new VjassEmitter().Emit(unit));
        Console.WriteLine(outPath);
    }
    if (target is "lua" or "both")
    {
        var outPath = Path.Combine(dir, stem + ".lua");
        File.WriteAllText(outPath, new LuaEmitter().Emit(unit));
        Console.WriteLine(outPath);
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ошибка: {ex.Message}");
    return 1;
}
return 0;

static class MapBuild
{
    public static int Run(string project, bool launch)
    {
        var cfgPath = Path.Combine(project, "jasssharp.json");
        if (!File.Exists(cfgPath))
        {
            Console.Error.WriteLine("нет jasssharp.json");
            return 1;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(cfgPath));
        var root = doc.RootElement;
        var map = root.GetProperty("map").GetString() ?? "map";
        var target = root.TryGetProperty("target", out var t) ? t.GetString() ?? "lua" : "lua";
        var entry = root.TryGetProperty("entry", out var e) ? e.GetString() : null;
        var game = root.TryGetProperty("game", out var g) ? g.GetString() : null;
        game = string.IsNullOrWhiteSpace(game) ? Environment.GetEnvironmentVariable("JASSSHARP_GAME") : game;
        map = Path.IsPathRooted(map) ? map : Path.Combine(project, map);

        var files = new List<string>();
        if (!string.IsNullOrEmpty(entry))
            files.Add(Path.IsPathRooted(entry) ? entry : Path.Combine(project, entry));
        else
        {
            var src = Path.Combine(project, "src");
            if (Directory.Exists(src))
                files.AddRange(Directory.GetFiles(src, "*.w3js", SearchOption.AllDirectories));
        }
        if (files.Count == 0) { Console.Error.WriteLine("нет .w3js"); return 1; }

        var chunks = new List<string>();
        foreach (var f in files)
        {
            try
            {
                var unit = new Parser(new Lexer(File.ReadAllText(f)).Lex()).ParseNamespace();
                chunks.Add(target == "vjass" ? new VjassEmitter().Emit(unit) : new LuaEmitter().Emit(unit));
            }
            catch (ParseException ex)
            {
                Console.Error.WriteLine($"{f}:{ex.Line}:{ex.Col}: {ex.Message}");
                return 1;
            }
        }
        var script = string.Join("\n", chunks);

        if (File.Exists(map) && (map.EndsWith(".w3x", StringComparison.OrdinalIgnoreCase) || map.EndsWith(".w3m", StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine("архив .w3x: укажите папку карты (распакованную) в jasssharp.json → map");
            return 1;
        }
        Directory.CreateDirectory(map);
        var name = target == "vjass" ? "war3map.j" : "war3map.lua";
        var dest = Path.Combine(map, name);
        File.WriteAllText(dest, script);
        Console.WriteLine(dest);

        if (launch)
        {
            if (string.IsNullOrWhiteSpace(game) || !File.Exists(game))
            {
                Console.Error.WriteLine("для запуска укажите game в jasssharp.json (путь к Warcraft III.exe)");
                return 1;
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = game,
                Arguments = $"-loadfile \"{map}\"",
                UseShellExecute = true,
            });
            Console.WriteLine("запуск: " + game);
        }
        return 0;
    }
}

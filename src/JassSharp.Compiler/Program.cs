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
    Console.Error.WriteLine("  jasssharp map [--project dir] [--run]");
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
    for (var i = 1; i < args.Length; i++)
    {
        if (args[i] == "--project" && i + 1 < args.Length) project = args[++i];
        else if (args[i] == "--run") run = true;
    }
    return MapBuild.Run(project, run);
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

static class MapBuild
{
    public static int Run(string project, bool launch)
    {
        var cfgPath = Path.Combine(project, "jasssharp.json");
        if (!File.Exists(cfgPath))
        {
            Console.Error.WriteLine("no jasssharp.json");
            return 1;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(cfgPath));
        var root = doc.RootElement;
        var map = root.GetProperty("map").GetString() ?? "map";
        var target = root.TryGetProperty("target", out var t) ? t.GetString() ?? "lua" : "lua";
        var entry = root.TryGetProperty("entry", out var e) ? e.GetString() : null;
        var game = root.TryGetProperty("game", out var g) ? g.GetString() : null;
        var extra = root.TryGetProperty("args", out var a) ? a.GetString() : null;
        game = string.IsNullOrWhiteSpace(game) ? Environment.GetEnvironmentVariable("JASSSHARP_GAME") : game;
        map = Path.IsPathRooted(map) ? map : Path.Combine(project, map);

        var src = Path.Combine(project, "src");
        var files = new List<string>();
        if (!string.IsNullOrEmpty(entry))
            files.Add(Path.IsPathRooted(entry) ? entry : Path.Combine(project, entry));
        else if (Directory.Exists(src))
            files.AddRange(Directory.GetFiles(src, "*.w3js", SearchOption.AllDirectories));
        if (files.Count == 0) { Console.Error.WriteLine("no .w3js"); return 1; }

        // резолвим using: ищем файлы модулей и ставим их перед entry
        var ordered = ResolveUsings(files, src, project);
        Console.WriteLine("files: " + string.Join(", ", ordered.Select(Path.GetFileName)));

        var chunks = new List<string>();
        foreach (var f in ordered)
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
            Console.Error.WriteLine("archive .w3x: set map to unpacked folder in jasssharp.json");
            return 1;
        }
        Directory.CreateDirectory(map);
        var name = target == "vjass" ? "war3map.j" : "war3map.lua";
        var dest = Path.Combine(map, name);
        File.WriteAllText(dest, script);
        var scriptInfo = new FileInfo(dest);
        Console.WriteLine($"script: {dest} ({scriptInfo.Length} bytes)");

        var w3i = Path.Combine(map, "war3map.w3i");
        if (!File.Exists(w3i))
            Console.WriteLine("warn: no war3map.w3i — карта не загрузится в World Editor");
        else
            Console.WriteLine($"w3i: {w3i} ({new FileInfo(w3i).Length} bytes)");

        var tool = Environment.GetEnvironmentVariable("JASSSHARP_MPQ")
                   ?? FindOnPath("mpqtool.exe")
                   ?? FindOnPath("mpqtool");
        string? packed = null;
        if (!string.IsNullOrWhiteSpace(tool) && File.Exists(tool))
        {
            var outDir = Path.Combine(project, "bin");
            Directory.CreateDirectory(outDir);
            packed = Path.Combine(outDir, "JassSharpMap.w3x");
            if (File.Exists(packed)) File.Delete(packed);
            Console.WriteLine("pack: " + tool + " new \"" + map + "\" \"" + packed + "\"");
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = tool,
                Arguments = $"new \"{map}\" \"{packed}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            });
            if (p != null)
            {
                var err = p.StandardError.ReadToEnd();
                var outp = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (!string.IsNullOrWhiteSpace(outp)) Console.WriteLine(outp.Trim());
                if (p.ExitCode != 0 || !File.Exists(packed))
                {
                    Console.Error.WriteLine("pack failed: " + err.Trim());
                    packed = null;
                }
                else
                {
                    var pi = new FileInfo(packed);
                    Console.WriteLine($"packed: {packed} ({pi.Length} bytes)");
                    if (pi.Length < 1024)
                        Console.WriteLine("warn: карта очень маленькая — возможно, в папке map нет war3map.w3e/w3i");
                }
            }
        }
        else
        {
            Console.WriteLine("warn: mpqtool not found. set JASSSHARP_MPQ or put mpqtool.exe on PATH");
        }

        if (launch)
        {
            if (string.IsNullOrWhiteSpace(game) || !File.Exists(game))
            {
                Console.Error.WriteLine("set game in jasssharp.json (path to Warcraft III.exe)");
                return 1;
            }
            if (packed == null)
            {
                Console.Error.WriteLine("cannot run: no .w3x. install mpqtool and set jasssharp.mpqToolPath");
                return 1;
            }

            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var testDir = Path.Combine(docs, "Warcraft III", "Maps", "Test");
            var downloadDir = Path.Combine(docs, "Warcraft III", "Maps", "Download");
            Directory.CreateDirectory(testDir);
            Directory.CreateDirectory(downloadDir);

            var testMap = Path.Combine(testDir, "JassSharpMap.w3x");
            var downloadMap = Path.Combine(downloadDir, "JassSharpMap.w3x");
            try { File.Copy(packed, testMap, true); Console.WriteLine("copy test: " + testMap); }
            catch (Exception ex) { Console.WriteLine("warn copy test: " + ex.Message); }
            try { File.Copy(packed, downloadMap, true); Console.WriteLine("copy download: " + downloadMap); }
            catch (Exception ex) { Console.WriteLine("warn copy download: " + ex.Message); }

            var launchFile = File.Exists(testMap) ? testMap : packed;

            var exeDir = Path.GetDirectoryName(game)!;
            var workDir = exeDir.EndsWith("x86_64", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(exeDir)!
                : exeDir;

            var launchArgs = $"-launch -editor -windowmode windowed -loadfile \"{launchFile}\"";
            if (!string.IsNullOrWhiteSpace(extra)) launchArgs += " " + extra;

            Console.WriteLine("LAUNCH: \"" + game + "\" " + launchArgs);
            Console.WriteLine("WORKDIR: " + workDir);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = game,
                Arguments = launchArgs,
                WorkingDirectory = workDir,
                UseShellExecute = true,
            });
            Console.WriteLine("started");
        }
        return 0;
    }

    // using Модуль; → ищем Модуль.w3js или папку/Модуль.w3js и ставим перед зависимым
    static List<string> ResolveUsings(List<string> files, string src, string project)
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var search = new[] { src, project }.Where(Directory.Exists);
        foreach (var dir in search)
            foreach (var f in Directory.GetFiles(dir, "*.w3js", SearchOption.AllDirectories))
                byName[Path.GetFileNameWithoutExtension(f)] = f;

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string path)
        {
            if (!seen.Add(path)) return;
            if (!File.Exists(path)) return;
            NsUnit? unit = null;
            try { unit = new Parser(new Lexer(File.ReadAllText(path)).Lex()).ParseNamespace(); }
            catch { }
            if (unit is not null)
            {
                foreach (var u in unit.Usings)
                {
                    if (byName.TryGetValue(u, out var dep)) Add(dep);
                    else Console.WriteLine("warn: using " + u + " — файл не найден");
                }
            }
            result.Add(path);
        }
        foreach (var f in files) Add(f);
        return result;
    }

    static string? FindOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            try
            {
                var full = Path.Combine(dir.Trim(), name);
                if (File.Exists(full)) return full;
            }
            catch { }
        }
        return null;
    }
}

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
        var baseMap = root.TryGetProperty("base", out var b) ? b.GetString() : null;
        game = string.IsNullOrWhiteSpace(game) ? Environment.GetEnvironmentVariable("JASSSHARP_GAME") : game;
        map = Path.IsPathRooted(map) ? map : Path.Combine(project, map);
        if (!string.IsNullOrWhiteSpace(baseMap) && !Path.IsPathRooted(baseMap))
            baseMap = Path.Combine(project, baseMap);

        var src = Path.Combine(project, "src");
        var files = new List<string>();
        if (!string.IsNullOrEmpty(entry))
            files.Add(Path.IsPathRooted(entry) ? entry : Path.Combine(project, entry));
        else if (Directory.Exists(src))
            files.AddRange(Directory.GetFiles(src, "*.w3js", SearchOption.AllDirectories));
        if (files.Count == 0) { Console.Error.WriteLine("no .w3js"); return 1; }

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
        var scriptName = target == "vjass" ? "war3map.j" : "war3map.lua";

        // пишем скрипт во временный файл
        var tmpScript = Path.Combine(Path.GetTempPath(), "JassSharp_" + scriptName);
        File.WriteAllText(tmpScript, script);
        Console.WriteLine($"script: {tmpScript} ({new FileInfo(tmpScript).Length} bytes)");

        // ищем базовую карту: base в конфиге, или .w3x в папке map, или сам map это .w3x
        string? sourceW3x = null;
        if (!string.IsNullOrWhiteSpace(baseMap) && File.Exists(baseMap))
            sourceW3x = baseMap;
        else if (File.Exists(map) && map.EndsWith(".w3x", StringComparison.OrdinalIgnoreCase))
            sourceW3x = map;
        else if (Directory.Exists(map))
        {
            var found = Directory.GetFiles(map, "*.w3x").FirstOrDefault();
            if (found is not null) sourceW3x = found;
        }

        var outDir = Path.Combine(project, "bin");
        Directory.CreateDirectory(outDir);
        var packed = Path.Combine(outDir, "JassSharpMap.w3x");

        var editor = Environment.GetEnvironmentVariable("JASSSHARP_MPQEDITOR")
                     ?? FindOnPath("MPQEditor.exe")
                     ?? FindOnPath("MPQEditor");
        var mpqtool = Environment.GetEnvironmentVariable("JASSSHARP_MPQ")
                      ?? FindOnPath("mpqtool.exe")
                      ?? FindOnPath("mpqtool");

        bool ok = false;
        if (sourceW3x is not null && editor is not null && File.Exists(editor))
        {
            // копия + инжект через MPQEditor (workflow из статьи)
            Console.WriteLine("base: " + sourceW3x);
            File.Copy(sourceW3x, packed, true);
            Console.WriteLine($"copy: {packed} ({new FileInfo(packed).Length} bytes)");
            var args = $"add \"{packed}\" \"{tmpScript}\" \"{scriptName}\"";
            Console.WriteLine("inject: " + editor + " " + args);
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = editor,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            });
            if (p is not null)
            {
                var err = p.StandardError.ReadToEnd();
                var outp = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (!string.IsNullOrWhiteSpace(outp)) Console.WriteLine(outp.Trim());
                if (p.ExitCode != 0)
                    Console.Error.WriteLine("inject failed: " + err.Trim());
                else
                {
                    ok = true;
                    Console.WriteLine($"injected: {packed} ({new FileInfo(packed).Length} bytes)");
                }
            }
        }
        else if (Directory.Exists(map) && mpqtool is not null && File.Exists(mpqtool))
        {
            // фолбэк: пакуем папку
            File.WriteAllText(Path.Combine(map, scriptName), script);
            if (File.Exists(packed)) File.Delete(packed);
            Console.WriteLine("pack: " + mpqtool + " new \"" + map + "\" \"" + packed + "\"");
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = mpqtool,
                Arguments = $"new \"{map}\" \"{packed}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            });
            if (p is not null)
            {
                var err = p.StandardError.ReadToEnd();
                var outp = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (!string.IsNullOrWhiteSpace(outp)) Console.WriteLine(outp.Trim());
                if (p.ExitCode == 0 && File.Exists(packed))
                {
                    ok = true;
                    Console.WriteLine($"packed: {packed} ({new FileInfo(packed).Length} bytes)");
                }
                else Console.Error.WriteLine("pack failed: " + err.Trim());
            }
        }
        else
        {
            Console.Error.WriteLine("no base .w3x and no MPQEditor/mpqtool. set base in jasssharp.json or install MPQEditor");
            return 1;
        }

        if (!ok) return 1;

        // проверяем Lua-флаг в w3i если есть в папке
        var w3iPath = Directory.Exists(map) ? Path.Combine(map, "war3map.w3i") : null;
        if (w3iPath is not null && File.Exists(w3iPath))
        {
            var scriptType = ReadScriptType(w3iPath);
            if (scriptType is null) Console.WriteLine("w3i: не удалось прочитать script type");
            else if (target == "lua" && scriptType != 1)
                Console.WriteLine("warn: war3map.w3i script type = " + scriptType + " (не Lua). в World Editor: Scenario → Map Options → Script language = Lua");
            else Console.WriteLine("w3i script type: " + (scriptType == 1 ? "Lua" : "JASS"));
        }

        if (launch)
        {
            if (string.IsNullOrWhiteSpace(game) || !File.Exists(game))
            {
                Console.Error.WriteLine("set game in jasssharp.json (path to Warcraft III.exe)");
                return 1;
            }

            // убиваем предыдущий процесс
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName("Warcraft III"))
            {
                try { proc.Kill(); Console.WriteLine("killed previous Warcraft III"); } catch { }
            }

            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var testDir = Path.Combine(docs, "Warcraft III", "Maps", "Test");
            Directory.CreateDirectory(testDir);
            var testMap = Path.Combine(testDir, "JassSharpMap.w3x");
            try { File.Copy(packed, testMap, true); Console.WriteLine("copy test: " + testMap); }
            catch (Exception ex) { Console.WriteLine("warn copy test: " + ex.Message); }

            var launchFile = File.Exists(testMap) ? testMap : packed;
            var exeDir = Path.GetDirectoryName(game)!;
            var workDir = exeDir.EndsWith("x86_64", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(exeDir)! : exeDir;

            // без -editor, как в статье
            var launchArgs = $"-loadfile \"{launchFile}\" -windowmode windowed";
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

    // читаем script type из war3map.w3i (0=JASS, 1=Lua), format >= 28
    static int? ReadScriptType(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            var format = br.ReadInt32();
            if (format < 28) return 0;
            br.ReadInt32(); // map version
            br.ReadInt32(); // editor version
            br.ReadInt32(); br.ReadInt32(); br.ReadInt32(); br.ReadInt32(); // game version
            // пропускаем строки до script type — это хрупко, поэтому просто ищем в конце
            // для надёжности логируем format и говорим проверить вручную если нужно
            return null;
        }
        catch { return null; }
    }

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

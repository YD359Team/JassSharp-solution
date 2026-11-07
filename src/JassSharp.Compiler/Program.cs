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
        var cfgEditor = root.TryGetProperty("mpqEditor", out var me) ? me.GetString() : null;
        var cfgMpq = root.TryGetProperty("mpqTool", out var mt) ? mt.GetString() : null;
        game = string.IsNullOrWhiteSpace(game) ? Environment.GetEnvironmentVariable("JASSSHARP_GAME") : game;
        map = Norm(Path.IsPathRooted(map) ? map : Path.Combine(project, map));
        if (!string.IsNullOrWhiteSpace(baseMap))
            baseMap = Norm(Path.IsPathRooted(baseMap) ? baseMap : Path.Combine(project, baseMap));

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

        var tmpScript = Path.Combine(Path.GetTempPath(), "JassSharp_" + scriptName);
        File.WriteAllText(tmpScript, script);
        Console.WriteLine($"script: {tmpScript} ({new FileInfo(tmpScript).Length} bytes)");

        string? sourceW3x = null;
        if (!string.IsNullOrWhiteSpace(baseMap) && File.Exists(baseMap))
            sourceW3x = baseMap;
        else if (File.Exists(map) && map.EndsWith(".w3x", StringComparison.OrdinalIgnoreCase))
            sourceW3x = map;
        else if (Directory.Exists(map))
        {
            var found = Directory.GetFiles(map, "*.w3x").FirstOrDefault();
            if (found is not null) sourceW3x = Norm(found);
        }

        var outDir = Path.Combine(project, "bin");
        Directory.CreateDirectory(outDir);
        var packed = Norm(Path.Combine(outDir, "JassSharpMap.w3x"));

        var envEditor = Environment.GetEnvironmentVariable("JASSSHARP_MPQEDITOR");
        var editor = !string.IsNullOrWhiteSpace(cfgEditor) && File.Exists(cfgEditor) ? cfgEditor
                     : !string.IsNullOrWhiteSpace(envEditor) && File.Exists(envEditor) ? envEditor
                     : FindOnPath("MPQEditor.exe") ?? FindOnPath("MPQEditor");
        var mpqtool = !string.IsNullOrWhiteSpace(cfgMpq) && File.Exists(cfgMpq) ? cfgMpq
                      : Environment.GetEnvironmentVariable("JASSSHARP_MPQ")
                      ?? FindOnPath("mpqtool.exe") ?? FindOnPath("mpqtool");

        Console.WriteLine("MPQEditor: " + (editor ?? "not found"));
        if (sourceW3x is not null) Console.WriteLine("base: " + sourceW3x + " (" + new FileInfo(sourceW3x).Length + " bytes)");

        bool ok = false;
        if (sourceW3x is not null && editor is not null && File.Exists(editor))
        {
            Console.WriteLine("mode: inject");
            File.Copy(sourceW3x, packed, true);
            var args = $"add \"{packed}\" \"{tmpScript}\" \"{scriptName}\"";
            Console.WriteLine("inject: " + Path.GetFileName(editor) + " add ... \"" + scriptName + "\"");
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
            if (sourceW3x is not null)
                Console.WriteLine("warn: base set but MPQEditor not found, packing folder");
            Console.WriteLine("mode: pack folder");
            File.WriteAllText(Path.Combine(map, scriptName), script);
            if (File.Exists(packed)) File.Delete(packed);
            Console.WriteLine("pack: " + Path.GetFileName(mpqtool) + " new ...");
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
            Console.Error.WriteLine("no base .w3x and no MPQEditor/mpqtool");
            return 1;
        }

        if (!ok) return 1;

        var w3iPath = Directory.Exists(map) ? Path.Combine(map, "war3map.w3i") : null;
        if (w3iPath is not null && File.Exists(w3iPath))
        {
            var st = ReadScriptType(w3iPath);
            if (st is null) Console.WriteLine("w3i: не удалось прочитать script type");
            else Console.WriteLine("w3i script type: " + (st == 1 ? "Lua" : "JASS"));
            if (target == "lua" && st != 1)
                Console.WriteLine("warn: карта не Lua. в World Editor: Scenario → Map Options → Script language = Lua");
        }

        if (launch)
        {
            if (string.IsNullOrWhiteSpace(game) || !File.Exists(game))
            {
                Console.Error.WriteLine("set game in jasssharp.json");
                return 1;
            }

            foreach (var proc in System.Diagnostics.Process.GetProcessesByName("Warcraft III"))
            {
                try { proc.Kill(); Console.WriteLine("killed previous"); } catch { }
            }

            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var testDir = Path.Combine(docs, "Warcraft III", "Maps", "Test");
            Directory.CreateDirectory(testDir);
            var testMap = Norm(Path.Combine(testDir, "JassSharpMap.w3x"));
            try { File.Copy(packed, testMap, true); Console.WriteLine("copy: " + testMap); }
            catch (Exception ex) { Console.WriteLine("warn copy: " + ex.Message); }

            var launchFile = File.Exists(testMap) ? testMap : packed;
            var exeDir = Path.GetDirectoryName(game)!;
            var workDir = exeDir.EndsWith("x86_64", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(exeDir)! : exeDir;

            var launchArgs = $"-launch -editor -windowmode windowed -loadfile \"{launchFile}\"";
            if (!string.IsNullOrWhiteSpace(extra)) launchArgs += " " + extra;

            Console.WriteLine("LAUNCH: \"" + Path.GetFileName(game) + "\" " + launchArgs);
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

    static string Norm(string p) => Path.GetFullPath(p.Replace('/', '\\'));

    // парсим war3map.w3i до script type (0=JASS, 1=Lua), format >= 28
    static int? ReadScriptType(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            var format = br.ReadInt32();
            if (format < 28) return 0;

            br.ReadInt32(); // saves
            br.ReadInt32(); // editor version
            br.ReadInt32(); br.ReadInt32(); br.ReadInt32(); br.ReadInt32(); // game version

            // name, author, description, recommended
            for (var i = 0; i < 4; i++) ReadString(br);

            // camera bounds float[8]
            for (var i = 0; i < 8; i++) br.ReadSingle();

            // margins int[4]
            for (var i = 0; i < 4; i++) br.ReadInt32();

            // playable width, height
            br.ReadInt32();
            br.ReadInt32();

            // flags
            br.ReadInt32();

            // tileset char
            br.ReadByte();

            // loading screen index
            br.ReadInt32();

            if (format >= 25)
            {
                ReadString(br); // loading screen path
                ReadString(br); // loading text
                ReadString(br); // loading title
                ReadString(br); // loading subtitle
                ReadString(br); // prologue path
                ReadString(br); // prologue text
                ReadString(br); // prologue title
                ReadString(br); // prologue subtitle

                br.ReadInt32(); // fog style
                br.ReadSingle(); br.ReadSingle(); br.ReadSingle(); // fog z start/end/density
                br.ReadByte(); br.ReadByte(); br.ReadByte(); br.ReadByte(); // fog color
                br.ReadInt32(); // weather
                ReadString(br); // sound environment
                br.ReadByte(); // light
                br.ReadByte(); br.ReadByte(); br.ReadByte(); br.ReadByte(); // water color
            }

            if (format >= 28)
                return br.ReadInt32(); // script type: 0=JASS, 1=Lua

            return 0;
        }
        catch { return null; }
    }

    static string ReadString(BinaryReader br)
    {
        var sb = new StringBuilder();
        byte b;
        while ((b = br.ReadByte()) != 0)
            sb.Append((char)b);
        return sb.ToString();
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

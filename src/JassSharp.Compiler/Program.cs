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
        var cfgMpqcli = root.TryGetProperty("mpqcli", out var mc) ? mc.GetString() : null;
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

        try { if (File.Exists(packed)) File.Delete(packed); } catch { }
        Console.WriteLine("cleaned old bin map");

        var envEditor = Environment.GetEnvironmentVariable("JASSSHARP_MPQEDITOR");
        var editor = !string.IsNullOrWhiteSpace(cfgEditor) && File.Exists(cfgEditor) ? cfgEditor
                     : !string.IsNullOrWhiteSpace(envEditor) && File.Exists(envEditor) ? envEditor
                     : FindOnPath("MPQEditor.exe") ?? FindOnPath("MPQEditor");
        var mpqtool = !string.IsNullOrWhiteSpace(cfgMpq) && File.Exists(cfgMpq) ? cfgMpq
                      : Environment.GetEnvironmentVariable("JASSSHARP_MPQ")
                      ?? FindOnPath("mpqtool.exe") ?? FindOnPath("mpqtool");
        var mpqcli = !string.IsNullOrWhiteSpace(cfgMpqcli) && File.Exists(cfgMpqcli) ? cfgMpqcli
                     : Environment.GetEnvironmentVariable("JASSSHARP_MPQCLI")
                     ?? FindOnPath("mpqcli.exe") ?? FindOnPath("mpqcli");

        Console.WriteLine("MPQEditor: " + (editor ?? "not found"));
        Console.WriteLine("mpqcli: " + (mpqcli ?? "not found"));
        if (sourceW3x is not null) Console.WriteLine("base: " + sourceW3x + " (" + new FileInfo(sourceW3x).Length + " bytes)");

        bool ok = false;
        if (sourceW3x is not null && (mpqcli is not null || editor is not null))
        {
            Console.WriteLine("mode: inject");

            // для mpqcli работаем в C:\Temp без кириллицы
            var workDir = Path.Combine(Path.GetTempPath(), "JassSharp_work");
            Directory.CreateDirectory(workDir);
            var workMap = Path.Combine(workDir, "map.w3x");
            File.Copy(sourceW3x, workMap, true);
            Console.WriteLine($"work map: {workMap}");

            var origDir = Path.Combine(Path.GetTempPath(), "JassSharp_orig");
            Directory.CreateDirectory(origDir);
            var origFile = Path.Combine(origDir, scriptName);
            try { if (File.Exists(origFile)) File.Delete(origFile); } catch { }

            string finalScript = script;
            bool gotOrig = false;
            if (mpqcli is not null)
                gotOrig = ExtractWithMpqcli(mpqcli, workMap, scriptName, origFile);
            else if (editor is not null)
                gotOrig = ExtractFile(editor, workMap, scriptName, origDir) && File.Exists(origFile);

            if (gotOrig && File.Exists(origFile))
            {
                var orig = File.ReadAllText(origFile);
                Console.WriteLine($"original from base: {orig.Length} bytes");
                var userOnly = System.Text.RegularExpressions.Regex.Replace(
                    script,
                    @"function\s+config\s*\(\s*\)\s*end\s*function\s+main\s*\(\)[\s\S]*?end\s*$",
                    "",
                    System.Text.RegularExpressions.RegexOptions.Multiline);
                finalScript = orig + "\n\n-- JASS#\n" + userOnly + @"
do
  local _origMain = main
  function main()
    _origMain()
    if OnInit then
      local ok, err = pcall(OnInit)
      if not ok then
        print('JASS# OnInit error: ' .. tostring(err))
        if BJDebugMsg then BJDebugMsg('JASS# OnInit error: ' .. tostring(err)) end
      end
    end
  end
end
";
                Console.WriteLine("hooking into original");
            }
            else Console.WriteLine("no original in base, using generated only");

            File.WriteAllText(tmpScript, finalScript);
            Console.WriteLine($"final script: {new FileInfo(tmpScript).Length} bytes");

            if (target == "lua")
            {
                var tmpDir = Path.Combine(Path.GetTempPath(), "JassSharp_w3i");
                Directory.CreateDirectory(tmpDir);
                var tmpW3i = Path.Combine(tmpDir, "war3map.w3i");
                try { if (File.Exists(tmpW3i)) File.Delete(tmpW3i); } catch { }

                bool gotW3i = false;
                if (mpqcli is not null)
                    gotW3i = ExtractWithMpqcli(mpqcli, workMap, "war3map.w3i", tmpW3i);
                else if (editor is not null)
                    gotW3i = ExtractFile(editor, workMap, "war3map.w3i", tmpDir) && File.Exists(tmpW3i);

                if (gotW3i && File.Exists(tmpW3i))
                {
                    var st = ReadScriptType(tmpW3i);
                    Console.WriteLine("w3i before: " + (st == 1 ? "Lua" : st == 0 ? "JASS" : "?"));
                    if (st != 1)
                    {
                        if (PatchScriptType(tmpW3i, 1))
                        {
                            Console.WriteLine("patched script type → 1");
                            if (mpqcli is not null)
                                InjectWithMpqcli(mpqcli, workMap, tmpW3i, "war3map.w3i");
                            else if (editor is not null)
                                InjectFile(editor, workMap, tmpW3i, "war3map.w3i");
                        }
                        else Console.WriteLine("warn: не удалось патчить w3i");
                    }
                }
                else Console.WriteLine("warn: war3map.w3i не извлечён");

                try { Directory.Delete(tmpDir, true); } catch { }
            }

            Console.WriteLine("inject script: " + scriptName);
            bool injected = false;
            if (mpqcli is not null)
                injected = InjectWithMpqcli(mpqcli, workMap, tmpScript, scriptName);
            else if (editor is not null)
                injected = InjectFile(editor, workMap, tmpScript, scriptName);

            if (injected)
            {
                ok = true;
                File.Copy(workMap, packed, true);
                Console.WriteLine($"injected: {packed} ({new FileInfo(packed).Length} bytes)");

                var verifyDir = Path.Combine(Path.GetTempPath(), "JassSharp_verify");
                Directory.CreateDirectory(verifyDir);
                var verifyFile = Path.Combine(verifyDir, scriptName);
                try { if (File.Exists(verifyFile)) File.Delete(verifyFile); } catch { }
                bool verified = false;
                if (mpqcli is not null)
                    verified = ExtractWithMpqcli(mpqcli, workMap, scriptName, verifyFile);
                else if (editor is not null)
                    verified = ExtractFile(editor, workMap, scriptName, verifyDir) && File.Exists(verifyFile);

                if (verified && File.Exists(verifyFile))
                {
                    var got = File.ReadAllText(verifyFile);
                    Console.WriteLine($"verify: extracted {got.Length} bytes");
                    var head = got.Length > 250 ? got[..250] : got;
                    Console.WriteLine("verify head: " + head.Replace("\n", " | "));
                }
                else Console.WriteLine("verify: не удалось извлечь обратно");
                try { Directory.Delete(verifyDir, true); } catch { }
            }
            else Console.Error.WriteLine("inject script failed");

            try { Directory.Delete(origDir, true); } catch { }
            try { Directory.Delete(workDir, true); } catch { }
        }
        else if (Directory.Exists(map) && mpqtool is not null && File.Exists(mpqtool))
        {
            if (sourceW3x is not null)
                Console.WriteLine("warn: base set but MPQEditor not found, packing folder");
            Console.WriteLine("mode: pack folder");
            File.WriteAllText(Path.Combine(map, scriptName), script);
            var folderW3i = Path.Combine(map, "war3map.w3i");
            if (target == "lua" && File.Exists(folderW3i))
            {
                if (PatchScriptType(folderW3i, 1))
                    Console.WriteLine("patched folder w3i → Lua");
            }
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
            Console.Error.WriteLine("no base .w3x and no MPQEditor/mpqcli/mpqtool");
            return 1;
        }

        if (!ok) return 1;

        Console.WriteLine("built: " + packed);
        Console.WriteLine("открывай этот файл в редакторе, не base");

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

    static bool InjectWithMpqcli(string mpqcli, string archive, string filePath, string nameInArchive)
    {
        var args = $"add \"{archive}\" \"{filePath}\" --path \"{nameInArchive}\" --overwrite";
        Console.WriteLine($"mpqcli cmd: {Path.GetFileName(mpqcli)} {args}");
        var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = mpqcli,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        });
        if (p is null) { Console.WriteLine("mpqcli: process null"); return false; }
        var err = p.StandardError.ReadToEnd();
        var outp = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Console.WriteLine($"mpqcli exit: {p.ExitCode}");
        if (!string.IsNullOrWhiteSpace(outp)) Console.WriteLine("mpqcli out: " + outp.Trim());
        if (!string.IsNullOrWhiteSpace(err)) Console.WriteLine("mpqcli err: " + err.Trim());
        return p.ExitCode == 0;
    }

    static bool ExtractWithMpqcli(string mpqcli, string archive, string nameInArchive, string targetFile)
    {
        var dir = Path.GetDirectoryName(targetFile)!;
        Directory.CreateDirectory(dir);
        var args = $"extract \"{archive}\" \"{nameInArchive}\" --output \"{dir}\"";
        Console.WriteLine($"mpqcli extract: {args}");
        var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = mpqcli,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        });
        if (p is null) return false;
        var err = p.StandardError.ReadToEnd();
        var outp = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (!string.IsNullOrWhiteSpace(outp)) Console.WriteLine("extract out: " + outp.Trim());
        if (p.ExitCode != 0 && !string.IsNullOrWhiteSpace(err)) Console.WriteLine("extract err: " + err.Trim());

        var found = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            .FirstOrDefault(f => Path.GetFileName(f).Equals(nameInArchive, StringComparison.OrdinalIgnoreCase));
        if (found is not null && found != targetFile)
        {
            Console.WriteLine($"extract found: {found}");
            File.Copy(found, targetFile, true);
        }
        return File.Exists(targetFile);
    }

    static bool ExtractFile(string editor, string archive, string nameInArchive, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = editor,
            Arguments = $"extract \"{archive}\" \"{nameInArchive}\" \"{targetDir}\"",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        });
        if (p is null) return false;
        var err = p.StandardError.ReadToEnd();
        var outp = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (!string.IsNullOrWhiteSpace(outp)) Console.WriteLine("extract: " + outp.Trim());
        if (p.ExitCode != 0 && !string.IsNullOrWhiteSpace(err)) Console.WriteLine("extract err: " + err.Trim());
        return p.ExitCode == 0;
    }

    static bool InjectFile(string editor, string archive, string filePath, string nameInArchive)
    {
        var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = editor,
            Arguments = $"add \"{archive}\" \"{filePath}\" \"{nameInArchive}\"",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        });
        if (p is null) return false;
        p.WaitForExit();
        return p.ExitCode == 0;
    }

    static bool PatchScriptType(string path, int value)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            using var ms = new MemoryStream(bytes);
            using var br = new BinaryReader(ms);
            var format = br.ReadInt32();
            if (format < 28) return false;

            br.ReadInt32();
            br.ReadInt32();
            br.ReadInt32(); br.ReadInt32(); br.ReadInt32(); br.ReadInt32();

            for (var i = 0; i < 4; i++) SkipString(br);

            br.ReadBytes(32);
            br.ReadBytes(16);
            br.ReadInt32(); br.ReadInt32();
            br.ReadInt32();
            br.ReadByte();
            br.ReadInt32();

            if (format >= 25)
            {
                for (var i = 0; i < 8; i++) SkipString(br);
                br.ReadInt32();
                br.ReadBytes(12);
                br.ReadBytes(4);
                br.ReadInt32();
                SkipString(br);
                br.ReadByte();
                br.ReadBytes(4);
            }

            var pos = (int)ms.Position;
            if (pos + 4 > bytes.Length) return false;
            bytes[pos] = (byte)value;
            bytes[pos + 1] = 0;
            bytes[pos + 2] = 0;
            bytes[pos + 3] = 0;
            File.WriteAllBytes(path, bytes);
            return true;
        }
        catch { return false; }
    }

    static void SkipString(BinaryReader br)
    {
        while (br.ReadByte() != 0) { }
    }

    static string Norm(string p) => Path.GetFullPath(p.Replace('/', '\\'));

    static int? ReadScriptType(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            var format = br.ReadInt32();
            if (format < 28) return 0;

            br.ReadInt32();
            br.ReadInt32();
            br.ReadInt32(); br.ReadInt32(); br.ReadInt32(); br.ReadInt32();

            for (var i = 0; i < 4; i++) SkipString(br);

            br.ReadBytes(32);
            br.ReadBytes(16);
            br.ReadInt32();
            br.ReadInt32();
            br.ReadInt32();
            br.ReadByte();
            br.ReadInt32();

            if (format >= 25)
            {
                for (var i = 0; i < 8; i++) SkipString(br);
                br.ReadInt32();
                br.ReadBytes(12);
                br.ReadBytes(4);
                br.ReadInt32();
                SkipString(br);
                br.ReadByte();
                br.ReadBytes(4);
            }

            if (format >= 28)
                return br.ReadInt32();

            return 0;
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

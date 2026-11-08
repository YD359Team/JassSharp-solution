using System.Text.Json;
using JassSharp.Emit;
using JassSharp.Syntax;

static class MapBuild
{
    public static int Run(string project, bool launch, string? configName = null)
    {
        var cfgName = configName ?? "jasssharp.json";
        var cfgPath = Path.Combine(project, cfgName);
        if (!File.Exists(cfgPath))
        {
            Console.Error.WriteLine($"no {cfgName}");
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
        var onInits = new List<string>();
        foreach (var f in ordered)
        {
            try
            {
                var unit = new Parser(new Lexer(File.ReadAllText(f)).Lex()).ParseNamespace();
                var emitted = target == "vjass" ? new VjassEmitter().Emit(unit) : new LuaEmitter().Emit(unit);
                if (target == "lua" && unit.Functions.Any(fn => fn.Name == "OnInit"))
                    onInits.Add(unit.Name + "_OnInit"); // unique per namespace
                chunks.Add(emitted);
            }
            catch (ParseException ex)
            {
                Console.Error.WriteLine($"{f}:{ex.Line}:{ex.Col}: {ex.Message}");
                return 1;
            }
        }

        var script = string.Join("\n", chunks);
        if (target == "lua" && onInits.Count > 0)
        {
            // Strip individual main() and emit one that calls all OnInit
            script = System.Text.RegularExpressions.Regex.Replace(script, @"function main\(\)[\s\S]*?end", "");
            script += "\nfunction main()\n    InitBlizzard()\n";
            foreach (var init in onInits)
                script += $"    if {init} then pcall({init}) end\n";
            script += "end\n";
        }

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

                var userOnly = script;
                var idx = script.LastIndexOf("function config()", StringComparison.Ordinal);
                if (idx >= 0)
                    userOnly = script[..idx].TrimEnd();
                userOnly = userOnly.TrimEnd();

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
            var workDirLaunch = exeDir.EndsWith("x86_64", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(exeDir)! : exeDir;

            var launchArgs = $"-launch -editor -windowmode windowed -loadfile \"{launchFile}\"";
            if (!string.IsNullOrWhiteSpace(extra)) launchArgs += " " + extra;

            Console.WriteLine("LAUNCH: \"" + Path.GetFileName(game) + "\" " + launchArgs);
            Console.WriteLine("WORKDIR: " + workDirLaunch);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = game,
                Arguments = launchArgs,
                WorkingDirectory = workDirLaunch,
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
        if (!string.IsNullOrWhiteSpace(outp)) Console.WriteLine("mpqcli out: " + outp.Trim());
        if (!string.IsNullOrWhiteSpace(err)) Console.WriteLine("mpqcli err: " + err.Trim());
        return p.ExitCode == 0 && File.Exists(targetFile);
    }

    static bool ExtractFile(string editor, string archive, string nameInArchive, string targetDir)
    {
        var args = $"extract \"{archive}\" \"{nameInArchive}\" \"{targetDir}\"";
        var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = editor,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        });
        if (p is null) return false;
        p.StandardError.ReadToEnd();
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode == 0;
    }

    static bool InjectFile(string editor, string archive, string filePath, string nameInArchive)
    {
        var args = $"add \"{archive}\" \"{filePath}\" \"{nameInArchive}\"";
        var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = editor,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        });
        if (p is null) return false;
        p.StandardError.ReadToEnd();
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode == 0;
    }

    static int ReadScriptType(string w3iPath)
    {
        try
        {
            var bytes = File.ReadAllBytes(w3iPath);
            if (bytes.Length < 8) return -1;
            // script type is typically at offset 0x14 or similar; simplified check
            for (int i = 0; i < Math.Min(bytes.Length - 4, 64); i++)
            {
                if (bytes[i] == 0 && bytes[i + 1] == 0 && bytes[i + 2] == 0 && (bytes[i + 3] == 0 || bytes[i + 3] == 1))
                    return bytes[i + 3];
            }
            return -1;
        }
        catch { return -1; }
    }

    static bool PatchScriptType(string w3iPath, int type)
    {
        try
        {
            var bytes = File.ReadAllBytes(w3iPath);
            if (bytes.Length < 8) return false;
            for (int i = 0; i < Math.Min(bytes.Length - 4, 64); i++)
            {
                if (bytes[i] == 0 && bytes[i + 1] == 0 && bytes[i + 2] == 0 && (bytes[i + 3] == 0 || bytes[i + 3] == 1))
                {
                    bytes[i + 3] = (byte)type;
                    File.WriteAllBytes(w3iPath, bytes);
                    return true;
                }
            }
            return false;
        }
        catch { return false; }
    }

    static string Norm(string p) => Path.GetFullPath(p);

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

    static List<string> ResolveUsings(List<string> files, string src, string project)
    {
        return files;
    }
}

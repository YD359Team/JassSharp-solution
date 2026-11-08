using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using JassSharp.Emit;
using JassSharp.Syntax;

static class MapBuild
{
    private const int ProcessTimeoutMs = 120_000;
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static int Run(string project, bool launch, string? configName = null)
    {
        string workRoot = Path.Combine(Path.GetTempPath(), "JassSharp_" + Guid.NewGuid().ToString("N"));

        try
        {
            project = Path.GetFullPath(project);
            if (!Directory.Exists(project))
                return Fail($"project directory does not exist: {project}");

            string cfgName = configName ?? "jasssharp.json";
            string cfgPath = Path.IsPathRooted(cfgName)
                ? cfgName
                : Path.Combine(project, cfgName);

            if (!File.Exists(cfgPath))
                return Fail($"no {cfgName}");

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(cfgPath));
            JsonElement root = doc.RootElement;

            string mapValue = root.GetProperty("map").GetString() ?? "map";
            string target = root.TryGetProperty("target", out JsonElement targetElement)
                ? targetElement.GetString() ?? "lua"
                : "lua";
            string? entry = GetString(root, "entry");
            string? game = GetString(root, "game");
            string? extra = GetString(root, "args");
            string? baseValue = GetString(root, "base");
            string? cfgEditor = GetString(root, "mpqEditor");
            string? cfgMpq = GetString(root, "mpqTool");
            string? cfgMpqCli = GetString(root, "mpqcli");

            target = target.Trim().ToLowerInvariant();
            if (target is not ("lua" or "vjass"))
                return Fail($"unsupported target '{target}'. Supported targets: lua, vjass.");

            game = string.IsNullOrWhiteSpace(game)
                ? Environment.GetEnvironmentVariable("JASSSHARP_GAME")
                : game;
            if (!string.IsNullOrWhiteSpace(game))
                game = ResolveConfiguredPath(project, game);

            string map = NormalizePath(project, mapValue);
            string? baseMap = string.IsNullOrWhiteSpace(baseValue)
                ? null
                : NormalizePath(project, baseValue);

            if (baseMap is not null && !File.Exists(baseMap))
                return Fail($"base map does not exist: {baseMap}");

            string src = Path.Combine(project, "src");
            List<string> files = new();

            if (!string.IsNullOrWhiteSpace(entry))
            {
                string entryPath = NormalizePath(project, entry);
                if (!File.Exists(entryPath))
                    return Fail($"entry file does not exist: {entryPath}");
                files.Add(entryPath);
            }
            else if (Directory.Exists(src))
            {
                files.AddRange(Directory.GetFiles(src, "*.w3js", SearchOption.AllDirectories));
            }

            if (files.Count == 0)
                return Fail("no .w3js source files found");

            List<string> ordered = ResolveUsings(files, src, project);
            Console.WriteLine("files: " + string.Join(", ", ordered.Select(Path.GetFileName)));

            List<string> chunks = new();
            List<string> onInits = new();
            HashSet<string> seenInitializers = new(StringComparer.Ordinal);

            foreach (string file in ordered)
            {
                try
                {
                    var unit = new Parser(new Lexer(File.ReadAllText(file)).Lex()).ParseNamespace();
                    string emitted = target == "vjass"
                        ? new VjassEmitter().Emit(unit)
                        : new LuaEmitter().Emit(unit);

                    if (target == "lua" && unit.Functions.Any(fn => fn.Name == "OnInit"))
                    {
                        string initializerName = unit.Name + "_OnInit";
                        if (seenInitializers.Add(initializerName))
                            onInits.Add(initializerName);
                    }

                    chunks.Add(emitted);
                }
                catch (ParseException ex)
                {
                    return Fail($"{file}:{ex.Line}:{ex.Col}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    return Fail($"failed to compile '{file}': {ex.Message}");
                }
            }

            string generatedScript = string.Join("\n", chunks);
            string scriptName = target == "vjass" ? "war3map.j" : "war3map.lua";

            string outDir = Path.Combine(project, "bin");
            Directory.CreateDirectory(outDir);

            string packed = Path.GetFullPath(
                Path.Combine(outDir, "JassSharpMap.w3x"));

            string objDir = Path.Combine(project, "obj");

            string? sourceW3x = FindSourceMap(map, baseMap);

            string? editor = FindTool(project, cfgEditor,
                Environment.GetEnvironmentVariable("JASSSHARP_MPQEDITOR"),
                "MPQEditor.exe", "MPQEditor");
            string? mpqCli = FindTool(project, cfgMpqCli,
                Environment.GetEnvironmentVariable("JASSSHARP_MPQCLI"),
                "mpqcli.exe", "mpqcli");
            string? mpqTool = FindTool(project, cfgMpq,
                Environment.GetEnvironmentVariable("JASSSHARP_MPQ"),
                "mpqtool.exe", "mpqtool");

            Console.WriteLine("MPQEditor: " + (editor ?? "not found"));
            Console.WriteLine("mpqcli: " + (mpqCli ?? "not found"));
            Console.WriteLine("mpqtool: " + (mpqTool ?? "not found"));
            if (sourceW3x is not null)
                Console.WriteLine($"base: {sourceW3x} ({new FileInfo(sourceW3x).Length} bytes)");

            if (target == "vjass")
            {
                Console.WriteLine("warning: vJASS is source code; Warcraft III needs it preprocessed/compiled by JassHelper before it can execute it.");
            }

            Directory.CreateDirectory(workRoot);

            if (sourceW3x is not null)
            {
                if (mpqCli is null && editor is null)
                    return Fail("a base .w3x was specified, but neither mpqcli nor MPQEditor is available; mpqtool cannot inject files into an existing archive");

                return BuildFromArchive(
                    sourceW3x,
                    packed,
                    objDir,
                    scriptName,
                    target,
                    generatedScript,
                    onInits,
                    mpqCli,
                    editor,
                    workRoot,
                    game,
                    extra,
                    launch);
            }

            if (Directory.Exists(map) && mpqTool is not null)
            {
                return BuildFromFolder(
                    map,
                    packed,
                    objDir,
                    scriptName,
                    target,
                    generatedScript,
                    onInits,
                    mpqTool,
                    game,
                    extra,
                    launch);
            }

            return Fail("no base .w3x and no usable project folder/mpqtool combination was found");
        }
        catch (JsonException ex)
        {
            return Fail("invalid jasssharp.json: " + ex.Message);
        }
        catch (Exception ex)
        {
            return Fail(ex.ToString());
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    private static int BuildFromArchive(
        string sourceW3x,
        string packed,
        string objDir,
        string scriptName,
        string target,
        string generatedScript,
        IReadOnlyList<string> onInits,
        string? mpqCli,
        string? editor,
        string workRoot,
        string? game,
        string? extra,
        bool launch)
    {
        string workMap = Path.Combine(workRoot, "map.w3x");

        File.Copy(sourceW3x, workMap, overwrite: true);

        Console.WriteLine("work map: " + workMap);

        string w3iDir = Path.Combine(workRoot, "w3i");
        string w3iPath = Path.Combine(w3iDir, "war3map.w3i");

        bool gotW3i = ExtractArchiveFile(
            mpqCli,
            editor,
            workMap,
            "war3map.w3i",
            w3iPath);

        if (!gotW3i)
        {
            return Fail(
                "could not extract war3map.w3i from the base map; " +
                "refusing to build an archive with an unknown map/script format");
        }

        int wantedScriptType = target == "lua" ? 1 : 0;

        // Сохраняем оригинал до изменения.
        byte[] originalW3i = File.ReadAllBytes(w3iPath);

        W3iInfo w3iInfo;

        try
        {
            w3iInfo = SetW3iScriptType(w3iPath, wantedScriptType);
        }
        catch (Exception ex)
        {
            return Fail("invalid or unsupported war3map.w3i: " + ex.Message);
        }

        Console.WriteLine(
            $"w3i format: {w3iInfo.FormatVersion}; " +
            $"script type: {FormatScriptType(w3iInfo.ScriptType)}");

        if (w3iInfo.ScriptType is null && target == "lua")
        {
            return Fail(
                "this map's W3I format predates Lua support " +
                "(format version < 28). Save/upgrade the base map " +
                "in a Warcraft III 1.31+ World Editor before building Lua into it");
        }

        byte[] patchedW3i = File.ReadAllBytes(w3iPath);

        bool w3iChanged =
            !originalW3i.AsSpan().SequenceEqual(patchedW3i);

        if (w3iChanged)
        {
            Console.WriteLine("W3I script type changed; injecting updated war3map.w3i");

            if (!InjectArchiveFile(
                    mpqCli,
                    editor,
                    workMap,
                    w3iPath,
                    "war3map.w3i"))
            {
                return Fail("failed to inject patched war3map.w3i");
            }
        }

        string baseScriptPath = Path.Combine(
            workRoot,
            "base-script",
            scriptName);

        bool gotOriginalScript = ExtractArchiveFile(
            mpqCli,
            editor,
            workMap,
            scriptName,
            baseScriptPath);

        string finalScript;

        if (target == "lua")
        {
            string userCode = gotOriginalScript
                ? RemoveLuaFunctions(generatedScript, "config", "main")
                : RemoveLuaFunctions(generatedScript, "main");

            if (gotOriginalScript)
            {
                string original = File.ReadAllText(baseScriptPath);

                Console.WriteLine(
                    $"original Lua script: {new FileInfo(baseScriptPath).Length} bytes");

                finalScript =
                    original +
                    "\n\n-- JASS# generated code\n" +
                    userCode +
                    CreateLuaMainWrapper(
                        onInits,
                        callInitBlizzardWhenMissingOriginalMain: true);
            }
            else
            {
                Console.WriteLine(
                    "warning: base map has no extractable war3map.lua; " +
                    "only generated JASS# code will be included");

                Console.WriteLine(
                    "warning: existing JASS triggers/scripts are not converted to Lua automatically");

                finalScript =
                    userCode +
                    CreateLuaMainWrapper(
                        onInits,
                        callInitBlizzardWhenMissingOriginalMain: true);
            }
        }
        else
        {
            string userCode = generatedScript;

            if (gotOriginalScript)
            {
                string original = File.ReadAllText(baseScriptPath);

                Console.WriteLine(
                    $"original JASS script: {new FileInfo(baseScriptPath).Length} bytes");

                finalScript =
                    original +
                    "\n\n// JASS# generated code\n" +
                    userCode;
            }
            else
            {
                finalScript = userCode;
            }
        }

        // Сохраняем именно итоговый скрипт, который попадёт в MPQ.
        string artifactPath = WriteBuildArtifact(
            objDir,
            scriptName,
            finalScript);

        string finalScriptPath = Path.Combine(workRoot, scriptName);

        File.Copy(
            artifactPath,
            finalScriptPath,
            overwrite: true);

        Console.WriteLine(
            $"final script: {new FileInfo(finalScriptPath).Length} bytes");

        Console.WriteLine("inject script: " + scriptName);

        if (!InjectArchiveFile(
                mpqCli,
                editor,
                workMap,
                finalScriptPath,
                scriptName))
        {
            return Fail("script injection failed");
        }

        string verifyDir = Path.Combine(workRoot, "verify");

        string verifyScript = Path.Combine(verifyDir, scriptName);
        string verifyW3i = Path.Combine(verifyDir, "war3map.w3i");

        if (!ExtractArchiveFile(
                mpqCli,
                editor,
                workMap,
                scriptName,
                verifyScript))
        {
            return Fail(
                "script was injected, but could not be extracted back for verification");
        }

        string extractedScript = File.ReadAllText(verifyScript);

        if (!string.Equals(
                extractedScript,
                finalScript,
                StringComparison.Ordinal))
        {
            return Fail(
                "MPQ verification failed: extracted script differs from the script that was injected");
        }

        if (!ExtractArchiveFile(
                mpqCli,
                editor,
                workMap,
                "war3map.w3i",
                verifyW3i))
        {
            return Fail(
                "script was injected, but war3map.w3i could not be extracted back for verification");
        }

        W3iInfo verifiedW3i;

        try
        {
            verifiedW3i = ReadW3i(File.ReadAllBytes(verifyW3i));
        }
        catch (Exception ex)
        {
            return Fail(
                "MPQ verification failed: extracted war3map.w3i is invalid: " +
                ex.Message);
        }

        bool scriptTypeMismatch = verifiedW3i.ScriptType.HasValue
            ? verifiedW3i.ScriptType.Value != (uint)wantedScriptType
            : wantedScriptType != 0;

        if (scriptTypeMismatch)
        {
            return Fail(
                $"MPQ verification failed: expected script type {wantedScriptType}, " +
                $"got {FormatScriptType(verifiedW3i.ScriptType)}");
        }

        Console.WriteLine(
            $"verified: {scriptName} ({extractedScript.Length} characters)");

        Console.WriteLine(
            $"verified W3I script type: {FormatScriptType(verifiedW3i.ScriptType)}");

        File.Copy(workMap, packed, overwrite: true);

        Console.WriteLine(
            $"built: {packed} ({new FileInfo(packed).Length} bytes)");

        return LaunchIfRequested(packed, game, extra, launch);
    }

    private static int BuildFromFolder(
        string mapFolder,
        string packed,
        string objDir,
        string scriptName,
        string target,
        string generatedScript,
        IReadOnlyList<string> onInits,
        string mpqTool,
        string? game,
        string? extra,
        bool launch)
    {
        string w3iPath = Path.Combine(mapFolder, "war3map.w3i");

        if (!File.Exists(w3iPath))
            return Fail($"map folder does not contain war3map.w3i: {mapFolder}");

        int wantedScriptType = target == "lua" ? 1 : 0;

        W3iInfo w3iInfo;

        try
        {
            w3iInfo = SetW3iScriptType(w3iPath, wantedScriptType);
        }
        catch (Exception ex)
        {
            return Fail("invalid or unsupported war3map.w3i: " + ex.Message);
        }

        Console.WriteLine(
            $"w3i format: {w3iInfo.FormatVersion}; " +
            $"script type: {FormatScriptType(w3iInfo.ScriptType)}");

        if (w3iInfo.ScriptType is null && target == "lua")
        {
            return Fail(
                "this map's W3I format predates Lua support " +
                "(format version < 28). Save/upgrade the base map " +
                "in a Warcraft III 1.31+ World Editor before building Lua into it");
        }

        string scriptPath = Path.Combine(mapFolder, scriptName);

        string finalScript;

        if (target == "lua")
        {
            bool hasOriginal = File.Exists(scriptPath);

            string userCode = hasOriginal
                ? RemoveLuaFunctions(generatedScript, "config", "main")
                : RemoveLuaFunctions(generatedScript, "main");

            if (hasOriginal)
            {
                string original = File.ReadAllText(scriptPath);

                finalScript =
                    original +
                    "\n\n-- JASS# generated code\n" +
                    userCode +
                    CreateLuaMainWrapper(
                        onInits,
                        callInitBlizzardWhenMissingOriginalMain: true);
            }
            else
            {
                finalScript =
                    userCode +
                    CreateLuaMainWrapper(
                        onInits,
                        callInitBlizzardWhenMissingOriginalMain: true);
            }
        }
        else
        {
            finalScript = File.Exists(scriptPath)
                ? File.ReadAllText(scriptPath) +
                  "\n\n// JASS# generated code\n" +
                  generatedScript
                : generatedScript;
        }

        // Артефакт для просмотра и отладки в VS Code.
        WriteBuildArtifact(
            objDir,
            scriptName,
            finalScript);

        // Рабочая папка карты получает тот же скрипт.
        File.WriteAllText(
            scriptPath,
            finalScript,
            Utf8NoBom);

        Console.WriteLine(
            $"wrote {scriptName}: {new FileInfo(scriptPath).Length} bytes");

        if (File.Exists(packed))
            File.Delete(packed);

        ProcessResult pack = RunTool(
            mpqTool,
            "new",
            mapFolder,
            packed);

        PrintProcessResult("mpqtool", pack);

        if (pack.ExitCode != 0 || !File.Exists(packed))
            return Fail("mpqtool failed to pack the map folder");

        Console.WriteLine(
            $"built: {packed} ({new FileInfo(packed).Length} bytes)");

        return LaunchIfRequested(packed, game, extra, launch);
    }

    private static int LaunchIfRequested(string packed, string? game, string? extra, bool launch)
    {
        if (!launch)
            return 0;

        if (string.IsNullOrWhiteSpace(game) || !File.Exists(game))
            return Fail("set a valid 'game' path in jasssharp.json or JASSSHARP_GAME");

        try
        {
            foreach (Process process in Process.GetProcessesByName("Warcraft III"))
            {
                try
                {
                    Console.WriteLine($"closing existing Warcraft III process {process.Id}");
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(10_000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"warning: could not close process {process.Id}: {ex.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }

            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string testDir = Path.Combine(documents, "Warcraft III", "Maps", "Test");
            Directory.CreateDirectory(testDir);

            string testMap = Path.GetFullPath(Path.Combine(testDir, Path.GetFileName(packed)));
            File.Copy(packed, testMap, overwrite: true);

            string exeDir = Path.GetDirectoryName(Path.GetFullPath(game))
                ?? throw new InvalidOperationException("could not determine Warcraft III executable directory");
            string workingDirectory = exeDir.EndsWith("x86_64", StringComparison.OrdinalIgnoreCase)
                ? Directory.GetParent(exeDir)?.FullName ?? exeDir
                : exeDir;

            string arguments = $"-launch -editor -windowmode windowed -loadfile \"{testMap}\"";
            if (!string.IsNullOrWhiteSpace(extra))
                arguments += " " + extra;

            Console.WriteLine("LAUNCH: \"" + Path.GetFileName(game) + "\" " + arguments);
            Console.WriteLine("WORKDIR: " + workingDirectory);

            Process? proc = Process.Start(new ProcessStartInfo
            {
                FileName = game,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = true
            });
            if (proc is null) throw new InvalidOperationException("Process.Start returned null");

            Console.WriteLine("started");
            return 0;
        }
        catch (Exception ex)
        {
            return Fail("could not launch Warcraft III: " + ex.Message);
        }
    }

    private static W3iInfo SetW3iScriptType(string path, int wantedType)
    {
        if (wantedType is not (0 or 1))
            throw new ArgumentOutOfRangeException(nameof(wantedType));

        byte[] bytes = File.ReadAllBytes(path);
        W3iInfo info = ReadW3i(bytes);

        if (info.ScriptTypeOffset is null)
        {
            if (wantedType == 1)
                throw new InvalidDataException($"W3I format {info.FormatVersion} does not contain the Lua script_type field; Lua requires format version 28 or newer");

            // Before W3I version 28, the map format only supports JASS.
            return info;
        }

        if (info.ScriptType is not (0 or 1))
            throw new InvalidDataException($"unknown script_type value {info.ScriptType}");

        if (info.ScriptType != wantedType)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(info.ScriptTypeOffset.Value, sizeof(uint)),
                checked((uint)wantedType));

            File.WriteAllBytes(path, bytes);
            info = ReadW3i(bytes);
        }

        return info;
    }

    private static W3iInfo ReadW3i(byte[] bytes)
    {
        using MemoryStream stream = new(bytes, writable: false);
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);

        uint formatVersion = reader.ReadUInt32();
        if (formatVersion == 0)
            throw new InvalidDataException("format_version is zero");

        _ = reader.ReadUInt32(); // map version
        _ = reader.ReadUInt32(); // editor version

        if (formatVersion >= 28)
        {
            _ = reader.ReadUInt32(); // game version major
            _ = reader.ReadUInt32(); // game version minor
            _ = reader.ReadUInt32(); // game version patch
            _ = reader.ReadUInt32(); // game version build
        }

        // Map name, author, description, suggested players.
        SkipCString(reader);
        SkipCString(reader);
        SkipCString(reader);
        SkipCString(reader);

        // Camera bounds (8 floats), camera complements (4 ints),
        // playable width/height, flags and tileset.
        SkipBytes(reader, 8 * sizeof(float));
        SkipBytes(reader, 7 * sizeof(uint));
        SkipBytes(reader, sizeof(byte));

        _ = reader.ReadUInt32(); // loading screen background number
        if (formatVersion >= 39)
            _ = reader.ReadUInt32(); // RaceHud (added in W3I v39)

        if (formatVersion >= 25)
            SkipCString(reader); // loading screen path

        SkipCString(reader); // loading screen text
        SkipCString(reader); // loading screen title
        SkipCString(reader); // loading screen subtitle

        if (formatVersion >= 25)
            _ = reader.ReadUInt32(); // game data set

        if (formatVersion == 18)
            _ = reader.ReadUInt32(); // RoC map loading screen number

        if (formatVersion >= 25)
            SkipCString(reader); // prologue screen model

        SkipCString(reader); // prologue text
        SkipCString(reader); // prologue title
        SkipCString(reader); // prologue subtitle

        if (formatVersion >= 25)
        {
            _ = reader.ReadUInt32(); // fog style
            SkipBytes(reader, 3 * sizeof(float)); // fog start, end, density
            SkipBytes(reader, 4); // fog BGRA

            if (formatVersion >= 39)
            {
                // Warcraft III 3.0 added six fields between FogColor and GlobalWeather.
                SkipBytes(reader, 5 * sizeof(float)); // fog height start/end, linear start/end, max opacity
                _ = reader.ReadUInt32(); // draw fog over sky
            }

            _ = reader.ReadUInt32(); // weather id
            SkipCString(reader); // custom sound environment
            SkipBytes(reader, 1); // custom light tileset
            SkipBytes(reader, 4); // water RGBA
        }

        if (formatVersion >= 28)
        {
            int offset = checked((int)stream.Position);
            uint scriptType = reader.ReadUInt32();
            return new W3iInfo(checked((int)formatVersion), scriptType, offset);
        }

        return new W3iInfo(checked((int)formatVersion), null, null);
    }

    private static void SkipCString(BinaryReader reader)
    {
        while (reader.ReadByte() != 0)
        {
            // Intentionally consume bytes only; the string's contents are not needed.
        }
    }

    private static void SkipBytes(BinaryReader reader, int count)
    {
        if (count < 0 || reader.BaseStream.Length - reader.BaseStream.Position < count)
            throw new EndOfStreamException("war3map.w3i ended unexpectedly while reading its fixed-size fields");

        reader.BaseStream.Position += count;
    }

    private static string CreateLuaMainWrapper(
        IReadOnlyList<string> onInits,
        bool callInitBlizzardWhenMissingOriginalMain)
    {
        StringBuilder sb = new();
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("-- JASS# entry point");
        sb.AppendLine("do");
        sb.AppendLine("    local __jasssharpOriginalMain = main");
        sb.AppendLine("    local __jasssharpInitializers = {");

        foreach (string initializer in onInits)
            sb.Append("        ").Append(LuaString(initializer)).AppendLine(",");

        sb.AppendLine("    }");
        sb.AppendLine("    function main()");
        sb.AppendLine("        if __jasssharpOriginalMain then");
        sb.AppendLine("            __jasssharpOriginalMain()");

        if (callInitBlizzardWhenMissingOriginalMain)
        {
            sb.AppendLine("        elseif InitBlizzard then");
            sb.AppendLine("            InitBlizzard()");
        }

        sb.AppendLine("        end");
        sb.AppendLine();
        sb.AppendLine("        for _, __name in ipairs(__jasssharpInitializers) do");
        sb.AppendLine("            local __initializer = _G[__name]");
        sb.AppendLine("            if type(__initializer) == 'function' then");
        sb.AppendLine("                local __ok, __err = pcall(__initializer)");
        sb.AppendLine("                if not __ok then");
        sb.AppendLine("                    local __message = 'JASS# OnInit error [' .. __name .. ']: ' .. tostring(__err)");
        sb.AppendLine("                    if print then print(__message) end");
        sb.AppendLine("                    if BJDebugMsg then BJDebugMsg(__message) end");
        sb.AppendLine("                end");
        sb.AppendLine("            end");
        sb.AppendLine("        end");
        sb.AppendLine("    end");
        sb.AppendLine("end");
        return sb.ToString();
    }

    private static string WriteBuildArtifact(
    string objDir,
    string scriptName,
    string content)
    {
        Directory.CreateDirectory(objDir);

        string path = Path.Combine(objDir, scriptName);

        File.WriteAllText(
            path,
            content,
            Utf8NoBom);

        Console.WriteLine(
            $"artifact: {path} ({new FileInfo(path).Length} bytes)");

        return path;
    }

    private static string LuaString(string value)
    {
        StringBuilder sb = new(value.Length + 2);
        sb.Append('\'');
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\'': sb.Append("\\'"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (char.IsControl(c))
                        sb.Append('\\').Append(((int)c).ToString("D3"));
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('\'');
        return sb.ToString();
    }

    private static string RemoveLuaFunctions(string source, params string[] functionNames)
    {
        if (string.IsNullOrEmpty(source) || functionNames.Length == 0)
            return source;

        HashSet<string> names = new(functionNames, StringComparer.Ordinal);
        List<LuaToken> tokens = TokenizeLua(source);
        List<(int Start, int End)> ranges = new();

        int blockDepth = 0;
        int pendingLoopDo = 0;

        for (int i = 0; i < tokens.Count; i++)
        {
            LuaToken token = tokens[i];

            if (token.Text == "function" && blockDepth == 0 &&
                i + 2 < tokens.Count &&
                names.Contains(tokens[i + 1].Text) &&
                tokens[i + 2].Text == "(")
            {
                int endToken = FindFunctionEnd(tokens, i);
                int start = token.Start;
                if (i > 0 && tokens[i - 1].Text == "local")
                    start = tokens[i - 1].Start;

                ranges.Add((start, tokens[endToken].End));
                i = endToken;
                continue;
            }

            UpdateLuaBlockDepth(token.Text, ref blockDepth, ref pendingLoopDo);
        }

        if (ranges.Count == 0)
            return source;

        StringBuilder result = new(source.Length);
        int position = 0;
        foreach ((int start, int end) in ranges)
        {
            if (start < position)
                continue;

            result.Append(source, position, start - position);
            result.AppendLine();
            position = end;
        }
        result.Append(source, position, source.Length - position);
        return result.ToString();
    }

    private static int FindFunctionEnd(IReadOnlyList<LuaToken> tokens, int functionTokenIndex)
    {
        int depth = 0;
        int pendingLoopDo = 0;

        for (int i = functionTokenIndex; i < tokens.Count; i++)
        {
            UpdateLuaBlockDepth(tokens[i].Text, ref depth, ref pendingLoopDo);
            if (depth == 0)
                return i;
        }

        throw new InvalidDataException("generated Lua contains a function without a matching 'end'");
    }

    private static void UpdateLuaBlockDepth(string token, ref int depth, ref int pendingLoopDo)
    {
        switch (token)
        {
            case "function":
            case "if":
            case "repeat":
                depth++;
                break;

            case "for":
            case "while":
                depth++;
                pendingLoopDo++;
                break;

            case "do":
                if (pendingLoopDo > 0)
                    pendingLoopDo--;
                else
                    depth++;
                break;

            case "end":
            case "until":
                depth--;
                if (depth < 0)
                    throw new InvalidDataException("generated Lua has an unmatched block terminator");
                break;
        }
    }

    private static List<LuaToken> TokenizeLua(string source)
    {
        List<LuaToken> tokens = new();
        int i = 0;

        while (i < source.Length)
        {
            char c = source[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '-' && i + 1 < source.Length && source[i + 1] == '-')
            {
                i += 2;
                if (TrySkipLongBracket(source, i, out int afterLongComment))
                {
                    i = afterLongComment;
                    continue;
                }

                while (i < source.Length && source[i] is not ('\r' or '\n'))
                    i++;
                continue;
            }

            if (c is '\'' or '"')
            {
                char quote = c;
                int start = i++;
                bool closed = false;
                while (i < source.Length)
                {
                    if (source[i] == '\\')
                    {
                        i = Math.Min(i + 2, source.Length);
                        continue;
                    }
                    if (source[i] == quote)
                    {
                        i++;
                        closed = true;
                        break;
                    }
                    i++;
                }

                if (!closed)
                    throw new InvalidDataException($"unterminated Lua string at character {start}");
                continue;
            }

            if (c == '[' && TrySkipLongBracket(source, i, out int afterLongString))
            {
                i = afterLongString;
                continue;
            }

            if (IsIdentifierStart(c))
            {
                int start = i++;
                while (i < source.Length && IsIdentifierPart(source[i]))
                    i++;
                tokens.Add(new LuaToken(source[start..i], start, i));
                continue;
            }

            if (c is '(' or ')' or '.' or ':' or ';')
                tokens.Add(new LuaToken(c.ToString(), i, i + 1));

            i++;
        }

        return tokens;
    }

    private static bool TrySkipLongBracket(string source, int start, out int end)
    {
        end = start;
        if (start >= source.Length || source[start] != '[')
            return false;

        int cursor = start + 1;
        while (cursor < source.Length && source[cursor] == '=')
            cursor++;

        if (cursor >= source.Length || source[cursor] != '[')
            return false;

        int equals = cursor - start - 1;
        cursor++;

        while (cursor < source.Length)
        {
            if (source[cursor] == ']')
            {
                int close = cursor + 1;
                int count = 0;
                while (close < source.Length && source[close] == '=' && count < equals)
                {
                    close++;
                    count++;
                }

                if (count == equals && close < source.Length && source[close] == ']')
                {
                    end = close + 1;
                    return true;
                }
            }
            cursor++;
        }

        throw new InvalidDataException($"unterminated Lua long string/comment at character {start}");
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';
    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool ExtractArchiveFile(
        string? mpqCli,
        string? editor,
        string archive,
        string archivePath,
        string destinationFile)
    {
        string? directory = Path.GetDirectoryName(destinationFile);
        if (directory is null)
            throw new InvalidOperationException("could not determine extraction directory");

        Directory.CreateDirectory(directory);
        TryDeleteFile(destinationFile);

        if (mpqCli is not null && ExtractWithMpqCli(mpqCli, archive, archivePath, destinationFile))
            return true;

        if (editor is not null && ExtractWithMpqEditor(editor, archive, archivePath, destinationFile))
            return true;

        return false;
    }

    private static bool InjectArchiveFile(
        string? mpqCli,
        string? editor,
        string archive,
        string sourceFile,
        string archivePath)
    {
        if (mpqCli is not null && InjectWithMpqCli(mpqCli, archive, sourceFile, archivePath))
            return true;

        if (editor is not null && InjectWithMpqEditor(editor, archive, sourceFile, archivePath))
            return true;

        return false;
    }

    private static bool ExtractWithMpqCli(string mpqCli, string archive, string archivePath, string destinationFile)
    {
        string directory = Path.GetDirectoryName(destinationFile)!;
        Directory.CreateDirectory(directory);
        TryDeleteFile(destinationFile);

        ProcessResult result = RunTool(mpqCli, "extract", archive, archivePath, "--output", directory);
        PrintProcessResult("mpqcli extract", result);
        return result.ExitCode == 0 && File.Exists(destinationFile);
    }

    private static bool InjectWithMpqCli(
        string mpqCli,
        string archive,
        string sourceFile,
        string archivePath)
    {
        ProcessResult result = RunTool(
            mpqCli,
            "add",
            archive,
            sourceFile,
            "--path", archivePath,
            "--overwrite",
            "--game", "warcraft3-map");

        PrintProcessResult("mpqcli add", result);
        return result.ExitCode == 0;
    }

    private static bool ExtractWithMpqEditor(string editor, string archive, string archivePath, string destinationFile)
    {
        string directory = Path.GetDirectoryName(destinationFile)!;
        Directory.CreateDirectory(directory);
        TryDeleteFile(destinationFile);

        ProcessResult result = RunTool(editor, "extract", archive, archivePath, directory);
        PrintProcessResult("MPQEditor extract", result);
        return result.ExitCode == 0 && File.Exists(destinationFile);
    }

    private static bool InjectWithMpqEditor(string editor, string archive, string sourceFile, string archivePath)
    {
        ProcessResult result = RunTool(editor, "add", archive, sourceFile, archivePath);
        PrintProcessResult("MPQEditor add", result);
        return result.ExitCode == 0;
    }

    private static ProcessResult RunTool(string executable, params string[] arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using Process process = new() { StartInfo = startInfo };
        StringBuilder stdout = new();
        StringBuilder stderr = new();
        object stdoutLock = new();
        object stderrLock = new();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (stdoutLock)
                    stdout.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (stderrLock)
                    stderr.AppendLine(e.Data);
            }
        };

        try
        {
            if (!process.Start())
                return new ProcessResult(-1, string.Empty, "Process.Start returned false");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(ProcessTimeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                process.WaitForExit();
                return new ProcessResult(-1, GetText(stdout, stdoutLock),
                    GetText(stderr, stderrLock) + Environment.NewLine + "process timed out");
            }

            // WaitForExit() without a timeout flushes asynchronous output events.
            process.WaitForExit();
            return new ProcessResult(process.ExitCode, GetText(stdout, stdoutLock), GetText(stderr, stderrLock));
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, GetText(stdout, stdoutLock), ex.Message + Environment.NewLine + GetText(stderr, stderrLock));
        }
    }

    private static string GetText(StringBuilder builder, object sync)
    {
        lock (sync)
            return builder.ToString();
    }

    private static void PrintProcessResult(string name, ProcessResult result)
    {
        Console.WriteLine($"{name} exit: {result.ExitCode}");
        if (!string.IsNullOrWhiteSpace(result.StdOut))
            Console.WriteLine(name + " out: " + result.StdOut.Trim());
        if (!string.IsNullOrWhiteSpace(result.StdErr))
            Console.WriteLine(name + " err: " + result.StdErr.Trim());
    }

    private static string? FindSourceMap(string map, string? baseMap)
    {
        if (!string.IsNullOrWhiteSpace(baseMap))
            return baseMap;

        if (File.Exists(map) && map.EndsWith(".w3x", StringComparison.OrdinalIgnoreCase))
            return map;

        if (Directory.Exists(map))
        {
            string? found = Directory.GetFiles(map, "*.w3x", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            return found is null ? null : Path.GetFullPath(found);
        }

        return null;
    }

    private static string? FindTool(string project, string? configured, string? environmentValue, params string[] pathNames)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            string candidate = ResolveConfiguredPath(project, configured);
            if (File.Exists(candidate))
                return candidate;
            Console.WriteLine($"warning: configured tool not found: {candidate}");
        }

        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            string? candidate = File.Exists(environmentValue)
                ? Path.GetFullPath(environmentValue)
                : FindOnPath(environmentValue);
            if (candidate is not null)
                return candidate;
            Console.WriteLine($"warning: tool from environment not found: {environmentValue}");
        }

        foreach (string name in pathNames)
        {
            string? found = FindOnPath(name);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static string? FindOnPath(string name)
    {
        string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(directory.Trim().Trim('"'), name);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            catch
            {
                // Ignore invalid PATH entries.
            }
        }

        return null;
    }

    private static string NormalizePath(string project, string path)
    {
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(project, path));
    }

    private static string ResolveConfiguredPath(string project, string path)
    {
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(project, path));
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement element) && element.ValueKind != JsonValueKind.Null
            ? element.GetString()
            : null;
    }

    private static string FormatScriptType(uint? scriptType)
    {
        return scriptType switch
        {
            0 => "JASS (0)",
            1 => "Lua (1)",
            null => "JASS (implicit; W3I < 28)",
            _ => $"unknown ({scriptType})"
        };
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // A later operation will report a useful error if the file cannot be replaced.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Temporary-file cleanup must not hide the build result.
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private static List<string> ResolveUsings(List<string> files, string src, string project)
    {
        // TODO: resolve JASS# using directives and sort dependencies.
        return files;
    }

    private readonly record struct W3iInfo(int FormatVersion, uint? ScriptType, int? ScriptTypeOffset);
    private readonly record struct ProcessResult(int ExitCode, string StdOut, string StdErr);
    private readonly record struct LuaToken(string Text, int Start, int End);
}

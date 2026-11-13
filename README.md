# JASS# Lua-only CLI and project-check update

This archive contains complete replacement files for the repository paths listed below.

## Files

- `src/JassSharp.Compiler/Program.cs`
- `src/JassSharp.Compiler/MapBuild.cs`
- `src/jasssharp-vscode/package.json`
- `src/jasssharp-vscode/src/extension.ts`

Copy the archive contents over the repository root, replacing the corresponding files.

## Changes

### Compiler CLI

- Lua is now the only accepted `build --target` value.
- The default build target is Lua; the CLI no longer emits `.j` vJASS files.
- `map` can be invoked without a dummy second argument.
- `check` resolves the current source file's project, loads imported namespaces, and passes them to `Checker.Check`.
- `symbols` reports loading/parsing failures instead of silently terminating with an unhandled exception.
- Unknown or incomplete CLI options are rejected with an error.

### Map build

- Map build configuration accepts only `target: "lua"`.
- The build always injects `war3map.lua` and uses `LuaEmitter`.
- The project source catalog used for checks no longer silently skips malformed `.w3js` files.
- Project loading reports duplicate namespace names.
- Import resolution reports missing namespaces and cyclic imports, and keeps dependency-first order.

### VS Code extension

- Removed the vJASS build command from the contributed command list and command registration.
- The Build command defaults to Lua regardless of stale user settings.
- The extension now forwards the configured `jasssharp.projectFile` path to `jasssharp map --config`, including custom config filenames.
- Compiler DLL invocation through `dotnet` and per-document diagnostics debounce are retained.

## Compatibility notes

Projects whose `jasssharp.json` still declares `"target": "vjass"` or `"target": "both"` now fail explicitly. Change the value to `"lua"`.

The former VS Code `jasssharp.buildVjass` command has been removed. The `jasssharp.buildLua` command remains as an explicit Lua build alias.

## Validation performed

- `package.json` parsed as valid JSON.
- Source-level checks confirmed the vJASS command is absent from extension registration and that custom project config is forwarded.
- TypeScript parsing was checked with the globally installed TypeScript compiler. The check could not resolve `vscode`, Node.js typings, or the sibling `languageFeatures` module because this isolated archive does not include `node_modules` or the rest of the extension source tree.
- A full .NET build was not run: the execution environment has no .NET SDK/compiler installed.

Run `dotnet build` and `npm.cmd run compile` in the full repository after replacing the files.

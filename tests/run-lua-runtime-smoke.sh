#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPILER="$ROOT/src/JassSharp.Compiler/JassSharp.Compiler.csproj"
LUA_BIN=""

if command -v lua5.3 >/dev/null 2>&1; then
    LUA_BIN="$(command -v lua5.3)"
elif command -v lua >/dev/null 2>&1; then
    LUA_BIN="$(command -v lua)"
else
    echo "Lua 5.3 runtime was not found." >&2
    exit 1
fi

dotnet build "$COMPILER" --configuration Release

dotnet run --configuration Release --no-build --project "$COMPILER" -- \
    build "$ROOT/tests/fixtures/FunctionHooksSmoke.w3js" --target lua
"$LUA_BIN" "$ROOT/tests/lua/function-hooks-single.lua" \
    "$ROOT/tests/fixtures/bin/FunctionHooksSmoke.lua"

dotnet run --configuration Release --no-build --project "$COMPILER" -- \
    map --project "$ROOT/tests/fixtures/FunctionHooksModules"
"$LUA_BIN" "$ROOT/tests/lua/function-hooks-modules.lua" \
    "$ROOT/tests/fixtures/FunctionHooksModules/bin/FunctionHooksModules.lua"

dotnet run --configuration Release --no-build --project "$COMPILER" -- \
    build "$ROOT/tests/fixtures/StringsApiSmoke.w3js" --target lua
"$LUA_BIN" "$ROOT/tests/lua/strings-runtime.lua" \
    "$ROOT/tests/fixtures/bin/StringsApiSmoke.lua"

echo "All JASS# Lua runtime integration checks passed."

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/JassSharp.Compiler/JassSharp.Compiler.csproj"
$fixture = Join-Path $PSScriptRoot "fixtures/MathOperatorsSmoke.w3js"
$output = Join-Path $PSScriptRoot "fixtures/bin/MathOperatorsSmoke.lua"

Write-Host "[1/4] Building compiler..."
dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Compiler build failed with exit code $LASTEXITCODE"
}

Write-Host "[2/4] Checking the operator/Math fixture..."
dotnet run --configuration Release --no-build --project $project -- check $fixture
if ($LASTEXITCODE -ne 0) {
    throw "Fixture check failed with exit code $LASTEXITCODE"
}

Write-Host "[3/4] Generating Lua..."
dotnet run --configuration Release --no-build --project $project -- build $fixture
if ($LASTEXITCODE -ne 0) {
    throw "Fixture build failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $output)) {
    throw "Expected generated Lua file was not created: $output"
}

Write-Host "[4/4] Verifying generated static calls and operator adapters..."
$lua = Get-Content -LiteralPath $output -Raw
$requiredFragments = @(
    "function Score.FromValue(value)",
    "Score.FromValue(10.0)",
    "function Vector2.__add(arg0, arg1)",
    "__operator_Add_Vector2_Vector2"
)

foreach ($fragment in $requiredFragments) {
    if (-not $lua.Contains($fragment)) {
        throw "Generated Lua is missing expected fragment: $fragment"
    }
}

Write-Host "All JASS# compiler smoke checks passed."

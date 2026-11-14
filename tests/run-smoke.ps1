$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/JassSharp.Compiler/JassSharp.Compiler.csproj"
$fixture = Join-Path $PSScriptRoot "fixtures/MathOperatorsSmoke.w3js"
$invalidFixture = Join-Path $PSScriptRoot "fixtures/StaticMethodMisuse.w3js"
$mathLibrary = Join-Path $repoRoot "standard-library/Math.w3js"
$output = Join-Path $PSScriptRoot "fixtures/bin/MathOperatorsSmoke.lua"

Write-Host "[1/6] Building compiler..."
dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Compiler build failed with exit code $LASTEXITCODE"
}

Write-Host "[2/6] Verifying invalid static method calls are rejected..."
$misuseDiagnostics = dotnet run --configuration Release --no-build --project $project -- check $invalidFixture 2>&1
$misuseExitCode = $LASTEXITCODE
$misuseText = $misuseDiagnostics -join "`n"
$hasStaticCallError = $misuseText.Contains("Factory.Create")
$hasInstanceCallError = $misuseText.Contains("Factory.Get")
$hasStaticFieldError = $misuseText.Contains("Value")
$hasErrorSeverity = $misuseText.Contains('"Severity":"error"')

if ($misuseExitCode -eq 0 -or -not $hasErrorSeverity -or -not $hasStaticCallError -or -not $hasInstanceCallError -or -not $hasStaticFieldError) {
    throw "Static/instance misuse was not rejected with all expected diagnostics. Exit code: $misuseExitCode. Output: $misuseText"
}

Write-Host "[3/6] Verifying build rejects semantic errors..."
$invalidBuildOutput = dotnet run --configuration Release --no-build --project $project -- build $invalidFixture 2>&1
$invalidBuildExitCode = $LASTEXITCODE
if ($invalidBuildExitCode -eq 0) {
    throw "Build accepted a fixture with invalid static/instance method calls. Output: $($invalidBuildOutput -join [Environment]::NewLine)"
}

Write-Host "[4/6] Checking the operator/Math fixture..."
dotnet run --configuration Release --no-build --project $project -- check $fixture
if ($LASTEXITCODE -ne 0) {
    throw "Fixture check failed with exit code $LASTEXITCODE"
}

dotnet run --configuration Release --no-build --project $project -- check $mathLibrary
if ($LASTEXITCODE -ne 0) {
    throw "Standard library check failed with exit code $LASTEXITCODE"
}

Write-Host "[5/6] Generating Lua..."
dotnet run --configuration Release --no-build --project $project -- build $fixture
if ($LASTEXITCODE -ne 0) {
    throw "Fixture build failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $output)) {
    throw "Expected generated Lua file was not created: $output"
}

Write-Host "[6/6] Verifying generated static calls and operator adapters..."
$lua = Get-Content -LiteralPath $output -Raw
$requiredFragments = @(
    "function Score.FromValue(value)",
    "Score.FromValue(10.0)",
    "function Vector2.__add(arg0, arg1)",
    "__operator_Add_Vector2_Vector2",
    "function Priority.__operator_LessThan_Priority_Priority(left, right)",
    "math.sqrt(self.X * self.X + self.Y * self.Y)",
    "self.Y * self.Z - self.Z * self.Y",
    "self.X * self.Y - self.Y * self.X"
)

foreach ($fragment in $requiredFragments) {
    if (-not $lua.Contains($fragment)) {
        throw "Generated Lua is missing expected fragment: $fragment"
    }
}

Write-Host "All JASS# compiler smoke checks passed."

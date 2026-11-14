$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/JassSharp.Compiler/JassSharp.Compiler.csproj"
$fixture = Join-Path $PSScriptRoot "fixtures/MathOperatorsSmoke.w3js"
$invalidFixture = Join-Path $PSScriptRoot "fixtures/StaticMethodMisuse.w3js"
$typeFixture = Join-Path $PSScriptRoot "fixtures/TypeSafetySmoke.w3js"
$mathLibrary = Join-Path $repoRoot "standard-library/Math.w3js"
$output = Join-Path $PSScriptRoot "fixtures/bin/MathOperatorsSmoke.lua"
$headlessProject = Join-Path $PSScriptRoot "headless-smoke"
$headlessOutput = Join-Path $headlessProject "bin/HeadlessSmoke.lua"

Write-Host "[1/7] Building compiler..."
dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Compiler build failed with exit code $LASTEXITCODE"
}

Write-Host "[2/7] Verifying invalid static method calls are rejected..."
$misuseDiagnostics = dotnet run --configuration Release --no-build --project $project -- check $invalidFixture 2>&1
$misuseExitCode = $LASTEXITCODE
$misuseText = $misuseDiagnostics -join "`n"
$hasStaticCallError = $misuseText.Contains("Factory.Create")
$hasInstanceCallError = $misuseText.Contains("Factory.Get")
$hasStaticFieldError = $misuseText.Contains("StoredValue")
$hasErrorSeverity = $misuseText.Contains('"Severity":"error"')

if ($misuseExitCode -eq 0 -or -not $hasErrorSeverity -or -not $hasStaticCallError -or -not $hasInstanceCallError -or -not $hasStaticFieldError) {
    throw "Static/instance misuse was not rejected with all expected diagnostics. Exit code: $misuseExitCode. Output: $misuseText"
}

Write-Host "[3/7] Verifying build rejects semantic errors..."
$previousErrorActionPreference = $ErrorActionPreference
try {
    # PowerShell can promote native stderr output to a terminating error when
    # ErrorActionPreference is Stop, even when a non-zero exit code is expected.
    $ErrorActionPreference = "Continue"
    $invalidBuildOutput = dotnet run --configuration Release --no-build --project $project -- build $invalidFixture 2>&1
    $invalidBuildExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}

if ($invalidBuildExitCode -eq 0) {
    throw "Build accepted a fixture with invalid static/instance method calls. Output: $($invalidBuildOutput -join [Environment]::NewLine)"
}

Write-Host "[4/7] Verifying strict assignment compatibility..."
$previousErrorActionPreference = $ErrorActionPreference
try {
    # Native stderr can become a terminating PowerShell error under Stop.
    $ErrorActionPreference = "Continue"
    $typeDiagnostics = dotnet run --configuration Release --no-build --project $project -- check $typeFixture 2>&1
    $typeExitCode = $LASTEXITCODE
    $typeText = $typeDiagnostics -join "`n"
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}

# The compiler emits JSON with escaped Unicode. Parse it so assertions operate
# on decoded diagnostic messages instead of their serialized representation.
try {
    $parsedTypeDiagnostics = ConvertFrom-Json -InputObject $typeText
}
catch {
    throw "Could not parse type-safety diagnostics as JSON. Exit code: $typeExitCode. Output: $typeText"
}

$typeErrors = @($parsedTypeDiagnostics | Where-Object { $_.Severity -eq "error" })
$errorLines = @($typeErrors | ForEach-Object { [int]$_.Line } | Sort-Object)
$actualErrorLines = $errorLines -join ","
$expectedErrorLines = "7,8,10,11"

# Keep this assertion ASCII-only: Windows PowerShell 5.1 may decode a UTF-8
# script without BOM using the active ANSI code page, corrupting Cyrillic text.
# These four source lines correspond to float->int, bool->int, var reassignment,
# and var without an initializer, respectively.
if ($typeExitCode -eq 0 -or $typeErrors.Count -ne 4 -or $actualErrorLines -ne $expectedErrorLines) {
    throw "Strict assignment compatibility was not enforced at all expected source locations. Exit code: $typeExitCode. Error lines: $actualErrorLines. Output: $typeText"
}

Write-Host "[5/7] Checking the operator/Math fixture..."
dotnet run --configuration Release --no-build --project $project -- check $fixture
if ($LASTEXITCODE -ne 0) {
    throw "Fixture check failed with exit code $LASTEXITCODE"
}

dotnet run --configuration Release --no-build --project $project -- check $mathLibrary
if ($LASTEXITCODE -ne 0) {
    throw "Standard library check failed with exit code $LASTEXITCODE"
}

Write-Host "[6/7] Generating Lua..."
dotnet run --configuration Release --no-build --project $project -- build $fixture
if ($LASTEXITCODE -ne 0) {
    throw "Fixture build failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $output)) {
    throw "Expected generated Lua file was not created: $output"
}

Write-Host "[7/8] Verifying generated static calls and operator adapters..."
$lua = Get-Content -LiteralPath $output -Raw
$requiredFragments = @(
    "function Score.FromValue(value)",
    "local inferredInt = 42",
    "local inferredFloat = 2.5",
    "local inferredScore = Score.FromValue(7.0)",
    "local remainder = (17 % 5)",
    "local fractionalRemainder = (7.5 % 2.0)",
    "remainder = (remainder % 3)",
    "function Score.__operator_Modulo_Score_Score(left, right)",
    "Score.FromValue(10.0)",
    "function Vector2.__add(arg0, arg1)",
    "__operator_Add_Vector2_Vector2",
    "function Priority.__operator_LessThan_Priority_Priority(left, right)",
    "self.Stored = Score.new(value)",
    "return self.Stored:GetValue()",
    "math.sqrt(((self.X * self.X) + (self.Y * self.Y)))",
    "((self.Y * other.Z) - (self.Z * other.Y))",
    "((self.X * other.Y) - (self.Y * other.X))"
)

foreach ($fragment in $requiredFragments) {
    if (-not $lua.Contains($fragment)) {
        throw "Generated Lua is missing expected fragment: $fragment"
    }
}

Write-Host "[8/8] Verifying headless mode emits standalone Lua without Warcraft entry points..."
$headlessBuildOutput = dotnet run --configuration Release --no-build --project $project -- map --project $headlessProject
if ($LASTEXITCODE -ne 0) {
    throw "Headless build failed: $($headlessBuildOutput -join [Environment]::NewLine)"
}
if (-not (Test-Path $headlessOutput)) {
    throw "Expected headless Lua output was not created: $headlessOutput"
}
$headlessLua = Get-Content -LiteralPath $headlessOutput -Raw
$forbiddenHeadlessFragments = @("function config()", "function main()", "InitBlizzard()", "if not BJDebugMsg")
foreach ($fragment in $forbiddenHeadlessFragments) {
    if ($headlessLua.Contains($fragment)) {
        throw "Headless Lua unexpectedly contains Warcraft runtime fragment: $fragment"
    }
}
if (-not $headlessLua.Contains('print("headless mode")')) {
    throw "Headless Print call was not translated to Lua print."
}

Write-Host "All JASS# compiler smoke checks passed."

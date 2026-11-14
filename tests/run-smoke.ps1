$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/JassSharp.Compiler/JassSharp.Compiler.csproj"
$fixture = Join-Path $PSScriptRoot "fixtures/MathOperatorsSmoke.w3js"
$invalidFixture = Join-Path $PSScriptRoot "fixtures/StaticMethodMisuse.w3js"
$typeFixture = Join-Path $PSScriptRoot "fixtures/TypeSafetySmoke.w3js"
$mathLibrary = Join-Path $repoRoot "standard-library/Math.w3js"
$vectorLibrary = Join-Path $repoRoot "standard-library/Vectors.w3js"
$geometryLibrary = Join-Path $repoRoot "standard-library/Geometry.w3js"
$output = Join-Path $PSScriptRoot "fixtures/bin/MathOperatorsSmoke.lua"
$headlessProject = Join-Path $PSScriptRoot "headless-smoke"
$headlessOutput = Join-Path $headlessProject "bin/HeadlessSmoke.lua"

Write-Host "[1/16] Building compiler..."
dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Compiler build failed with exit code $LASTEXITCODE"
}

Write-Host "[2/16] Verifying invalid static method calls are rejected..."
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

Write-Host "[3/16] Verifying build rejects semantic errors..."
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

Write-Host "[4/16] Verifying strict assignment compatibility..."
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

Write-Host "[5/16] Checking the operator/Math fixture and embedded standard-library modules..."
dotnet run --configuration Release --no-build --project $project -- check $fixture
if ($LASTEXITCODE -ne 0) {
    throw "Fixture check failed with exit code $LASTEXITCODE"
}

dotnet run --configuration Release --no-build --project $project -- check $mathLibrary
if ($LASTEXITCODE -ne 0) {
    throw "Math standard-library check failed with exit code $LASTEXITCODE"
}

dotnet run --configuration Release --no-build --project $project -- check $vectorLibrary
if ($LASTEXITCODE -ne 0) {
    throw "Vector standard-library check failed with exit code $LASTEXITCODE"
}

dotnet run --configuration Release --no-build --project $project -- check $geometryLibrary
if ($LASTEXITCODE -ne 0) {
    throw "Geometry standard-library check failed with exit code $LASTEXITCODE"
}

Write-Host "[6/16] Generating Lua and validating 2D geometry..."
$geometryFixture = Join-Path $PSScriptRoot "fixtures/GeometrySmoke.w3js"
$geometryBuildOutput = dotnet run --configuration Release --no-build --project $project -- build $geometryFixture --target lua
if ($LASTEXITCODE -ne 0) {
    throw "Geometry fixture failed to compile: $($geometryBuildOutput -join [Environment]::NewLine)"
}
$geometryLuaPath = Join-Path $PSScriptRoot "fixtures/bin/GeometrySmoke.lua"
if (-not (Test-Path $geometryLuaPath)) {
    throw "Expected generated geometry Lua file was not created: $geometryLuaPath"
}
$geometryLua = Get-Content -LiteralPath $geometryLuaPath -Raw
$geometryFragments = @(
    "function Circle:Contains(point)",
    "function Circle:ClosestPoint(point)",
    "function Circle:Intersects(other)",
    "function Rect2:Contains(point)",
    "function Rect2:IntersectsCircle(circle)",
    "function LineSegment2:ClosestPoint(point)",
    "function LineSegment2:DistanceToPoint(point)",
    "function LineSegment2:ContainsPoint(point)",
    "local reversedRectangle = Rect2.new(10.0, 5.0, -10.0, -5.0)"
)
foreach ($fragment in $geometryFragments) {
    if (-not $geometryLua.Contains($fragment)) {
        throw "Generated geometry Lua is missing expected fragment: $fragment"
    }
}

Write-Host "[7/16] Generating Math/vector Lua..."
dotnet run --configuration Release --no-build --project $project -- build $fixture
if ($LASTEXITCODE -ne 0) {
    throw "Fixture build failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $output)) {
    throw "Expected generated Lua file was not created: $output"
}

Write-Host "[7/16] Verifying generated static calls, Math helpers, and operator adapters..."
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
    "((self.X * other.Y) - (self.Y * other.X))",
    "function Math.Pi()",
    "function Math.InverseLerp(a, b, value)",
    "function Math.SmoothStep(a, b, t)",
    "function Math.MoveTowards(current, target, maxDelta)",
    "function Math.Repeat(value, length)",
    "function Math.LerpAngle(a, b, t)",
    "local radians = Math.DegToRad(180.0)",
    "local angle = Math.LerpAngle(350.0, 10.0, 0.5)"
)

foreach ($fragment in $requiredFragments) {
    if (-not $lua.Contains($fragment)) {
        throw "Generated Lua is missing expected fragment: $fragment"
    }
}

Write-Host "[8/16] Verifying headless mode emits standalone Lua without Warcraft entry points..."
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

$missingUsingFixture = Join-Path $headlessProject "src/HeadlessWarcraftMissingUsing.w3js"
$missingUsingOutput = dotnet run --configuration Release --no-build --project $project -- check $missingUsingFixture 2>&1
$missingUsingExitCode = $LASTEXITCODE
if ($missingUsingExitCode -eq 0) {
    throw "Headless mode accepted Warcraft III symbols without using Warcraft3."
}

Write-Host "[9/16] Verifying explicit using Warcraft3 enables the built-in module..."
$warcraftProject = Join-Path $PSScriptRoot "headless-warcraft-smoke"
$warcraftBuildOutput = dotnet run --configuration Release --no-build --project $project -- map --project $warcraftProject
if ($LASTEXITCODE -ne 0) {
    throw "Headless Warcraft3 module build failed: $($warcraftBuildOutput -join [Environment]::NewLine)"
}
$warcraftLua = Get-Content -LiteralPath (Join-Path $warcraftProject "bin/HeadlessWarcraftUsing.lua") -Raw
if (-not $warcraftLua.Contains("CreateGroup()") -or $warcraftLua.Contains("function main()")) {
    throw "Explicit Warcraft3 module did not compile to standalone Lua as expected."
}

Write-Host "[10/16] Verifying jagged array parsing, typing, and Lua indexing..."
$arrayFixture = Join-Path $PSScriptRoot "fixtures/ArrayTypesSmoke.w3js"
$arrayBuildOutput = dotnet run --configuration Release --no-build --project $project -- build $arrayFixture --target lua
if ($LASTEXITCODE -ne 0) {
    throw "Jagged array fixture failed to compile: $($arrayBuildOutput -join [Environment]::NewLine)"
}
$arrayLuaPath = Join-Path $PSScriptRoot "fixtures/bin/ArrayTypesSmoke.lua"
if (-not (Test-Path $arrayLuaPath)) {
    throw "Jagged array Lua output was not created: $arrayLuaPath"
}
$arrayLua = Get-Content -LiteralPath $arrayLuaPath -Raw
$arrayFragments = @(
    "GlobalMatrix = {}",
    "local matrix = {}",
    "matrix[0 + 1] = {}",
    "matrix[0 + 1][1 + 1] = 42",
    "local row = matrix[0 + 1]",
    "local inferredMatrix = {}",
    "local inferredRow = inferredMatrix[0 + 1]",
    "local weights = {}",
    "weights[0 + 1] = {}",
    "weights[0 + 1][0 + 1] = 2.5"
)
foreach ($fragment in $arrayFragments) {
    if (-not $arrayLua.Contains($fragment)) {
        throw "Jagged array Lua output is missing expected fragment: $fragment"
    }
}

Write-Host "[11/16] Verifying incompatible array element types are rejected..."
$arrayMismatchFixture = Join-Path $PSScriptRoot "fixtures/ArrayTypeMismatchSmoke.w3js"
$arrayMismatchOutput = dotnet run --configuration Release --no-build --project $project -- check $arrayMismatchFixture 2>&1
if ($LASTEXITCODE -eq 0) {
    throw "Compiler accepted assignment from float[] to int[]."
}

Write-Host "[12/16] Verifying foreach array loops and Warcraft group iteration..."
$foreachFixture = Join-Path $PSScriptRoot "fixtures/ForeachSmoke.w3js"
$foreachBuildOutput = dotnet run --configuration Release --no-build --project $project -- build $foreachFixture --target lua
if ($LASTEXITCODE -ne 0) {
    throw "Foreach fixture failed to compile: $($foreachBuildOutput -join [Environment]::NewLine)"
}
$foreachLuaPath = Join-Path $PSScriptRoot "fixtures/bin/ForeachSmoke.lua"
$foreachLua = Get-Content -LiteralPath $foreachLuaPath -Raw
$foreachFragments = @(
    "local __foreachCollection1 = matrix",
    "for __foreachIndex1 = 1, #__foreachCollection1 do",
    "local row = __foreachCollection1[__foreachIndex1]",
    "local __foreachCollection2 = row",
    "for __foreachIndex2 = 1, #__foreachCollection2 do",
    "local value = __foreachCollection2[__foreachIndex2]",
    "ForGroup(units, function()",
    "local unit = GetEnumUnit()"
)
foreach ($fragment in $foreachFragments) {
    if (-not $foreachLua.Contains($fragment)) {
        throw "Foreach Lua output is missing expected fragment: $fragment"
    }
}

Write-Host "[13/16] Verifying unsupported foreach collection types are rejected..."
$unsupportedForeachFixture = Join-Path $PSScriptRoot "fixtures/ForeachUnsupportedSmoke.w3js"
$unsupportedForeachOutput = dotnet run --configuration Release --no-build --project $project -- check $unsupportedForeachFixture 2>&1
if ($LASTEXITCODE -eq 0) {
    throw "Compiler accepted foreach over a scalar value."
}

Write-Host "[14/16] Verifying manually invoked class destructors..."
$destructorFixture = Join-Path $PSScriptRoot "fixtures/DestructorSmoke.w3js"
$destructorBuildOutput = dotnet run --configuration Release --no-build --project $project -- build $destructorFixture --target lua
if ($LASTEXITCODE -ne 0) {
    throw "Destructor fixture failed to compile: $($destructorBuildOutput -join [Environment]::NewLine)"
}
$destructorLuaPath = Join-Path $PSScriptRoot "fixtures/bin/DestructorSmoke.lua"
$destructorLua = Get-Content -LiteralPath $destructorLuaPath -Raw
if (-not $destructorLua.Contains("function TemporaryResource:Dispose()") -or
    -not $destructorLua.Contains("resource:Dispose()") -or
    -not $destructorLua.Contains('BJDebugMsg("resource released")')) {
    throw "Destructor was not emitted as a manually callable Dispose method."
}
if ($destructorLua.Contains("TemporaryResource.__gc") -or $destructorLua.Contains("newproxy")) {
    throw "Destructor must not be wired to automatic Lua garbage collection."
}

Write-Host "[15/16] Verifying invalid destructor declarations are rejected..."
$invalidDestructorFixtures = @(
    @{ Path = "DestructorWrongNameSmoke.w3js"; Description = "mismatched destructor name" },
    @{ Path = "DestructorParametersSmoke.w3js"; Description = "destructor parameters" },
    @{ Path = "DestructorStaticSmoke.w3js"; Description = "static destructor" },
    @{ Path = "DestructorVisibilitySmoke.w3js"; Description = "destructor visibility modifier" },
    @{ Path = "DestructorDuplicateSmoke.w3js"; Description = "duplicate destructor" },
    @{ Path = "DestructorDisposeConflictSmoke.w3js"; Description = "explicit Dispose conflict" }
)
$previousErrorActionPreference = $ErrorActionPreference
try {
    # Invalid compiler input is expected to produce a non-zero native exit code.
    $ErrorActionPreference = "Continue"
    foreach ($invalidCase in $invalidDestructorFixtures) {
        $invalidDestructorPath = Join-Path $PSScriptRoot ("fixtures/" + $invalidCase.Path)
        $invalidDestructorOutput = dotnet run --configuration Release --no-build --project $project -- check $invalidDestructorPath 2>&1
        $invalidDestructorExitCode = $LASTEXITCODE
        if ($invalidDestructorExitCode -eq 0) {
            throw "Compiler accepted invalid destructor declaration ($($invalidCase.Description)): $invalidDestructorPath"
        }
    }
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}

Write-Host "All JASS# compiler smoke checks passed."

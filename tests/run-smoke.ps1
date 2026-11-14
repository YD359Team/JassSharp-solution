$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/JassSharp.Compiler/JassSharp.Compiler.csproj"
$fixture = Join-Path $PSScriptRoot "fixtures/MathOperatorsSmoke.w3js"
$invalidFixture = Join-Path $PSScriptRoot "fixtures/StaticMethodMisuse.w3js"
$typeFixture = Join-Path $PSScriptRoot "fixtures/TypeSafetySmoke.w3js"
$mathLibrary = Join-Path $repoRoot "standard-library/Math.w3js"
$vectorLibrary = Join-Path $repoRoot "standard-library/Vectors.w3js"
$geometryLibrary = Join-Path $repoRoot "standard-library/Geometry.w3js"
$timerLibrary = Join-Path $repoRoot "standard-library/Timers.w3js"
$frameLibrary = Join-Path $repoRoot "standard-library/Frames.w3js"
$output = Join-Path $PSScriptRoot "fixtures/bin/MathOperatorsSmoke.lua"
$headlessProject = Join-Path $PSScriptRoot "headless-smoke"
$headlessOutput = Join-Path $headlessProject "bin/HeadlessSmoke.lua"

Write-Host "[1/20] Building compiler..."
dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Compiler build failed with exit code $LASTEXITCODE"
}

Write-Host "[1a] Verifying hexadecimal literals and bitwise operators..."
$bitwiseFixture = Join-Path $PSScriptRoot "fixtures/BitwiseSmoke.w3js"
$bitwiseBuildOutput = dotnet run --configuration Release --no-build --project $project -- build $bitwiseFixture --target lua
if ($LASTEXITCODE -ne 0) {
    throw "Bitwise fixture failed to compile: $($bitwiseBuildOutput -join [Environment]::NewLine)"
}
$bitwiseLuaPath = Join-Path $PSScriptRoot "fixtures/bin/BitwiseSmoke.lua"
if (-not (Test-Path $bitwiseLuaPath)) {
    throw "Expected generated bitwise Lua file was not created: $bitwiseLuaPath"
}
$bitwiseLua = Get-Content -LiteralPath $bitwiseLuaPath -Raw
foreach ($fragment in @(
    "local abc = (2748)",
    "local combined = (flags | mask)",
    "local intersection = (flags & mask)",
    "local toggled = (flags ~ mask)",
    "local shiftedLeft = (1 << 5)",
    "local shiftedRight = (32 >> 2)",
    "local allBits = (-1)",
    "local deadBeef = (-559038737)",
    "local signBit = (-2147483647 - 1)",
    "local minValue = (-2147483647 - 1)",
    "local precedence = (1 | (2 ~ (3 & (4 << 1))))",
    "flags = (flags | (16))",
    "flags = (flags & (255))",
    "flags = (flags ~ (1))",
    "flags = (flags << 1)",
    "flags = (flags >> 1)"
)) {
    if (-not $bitwiseLua.Contains($fragment)) {
        throw "Generated bitwise Lua is missing expected fragment: $fragment"
    }
}
if ($bitwiseLua.Contains("bit32.") -or $bitwiseLua.Contains("bit.")) {
    throw "Bitwise code generation must use native Lua 5.3 operators, not external bit libraries."
}

Write-Host "[1b] Verifying bitwise operand types and 32-bit literal overflow..."
$bitwiseInvalidFixture = Join-Path $PSScriptRoot "fixtures/BitwiseTypeMismatchSmoke.w3js"
$previousErrorActionPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = "Continue"
    $bitwiseDiagnostics = dotnet run --configuration Release --no-build --project $project -- check $bitwiseInvalidFixture 2>&1
    $bitwiseExitCode = $LASTEXITCODE
    $bitwiseText = $bitwiseDiagnostics -join "`n"
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}
try {
    $parsedBitwiseDiagnostics = ConvertFrom-Json -InputObject $bitwiseText
}
catch {
    throw "Could not parse bitwise diagnostics as JSON. Exit code: $bitwiseExitCode. Output: $bitwiseText"
}
$bitwiseErrors = @($parsedBitwiseDiagnostics | Where-Object { $_.Severity -eq "error" })
$bitwiseErrorLines = @($bitwiseErrors | ForEach-Object { [int]$_.Line } | Sort-Object) -join ","
if ($bitwiseExitCode -eq 0 -or $bitwiseErrors.Count -ne 5 -or $bitwiseErrorLines -ne "6,7,8,9,10") {
    throw "Bitwise type checking or 32-bit literal overflow validation failed. Exit code: $bitwiseExitCode. Error lines: $bitwiseErrorLines. Output: $bitwiseText"
}

Write-Host "[1c] Verifying overflow validation in constants and enum members..."
$bitwiseConstantsFixture = Join-Path $PSScriptRoot "fixtures/BitwiseConstantOverflowSmoke.w3js"
$previousErrorActionPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = "Continue"
    $bitwiseConstantsDiagnostics = dotnet run --configuration Release --no-build --project $project -- check $bitwiseConstantsFixture 2>&1
    $bitwiseConstantsExitCode = $LASTEXITCODE
    $bitwiseConstantsText = $bitwiseConstantsDiagnostics -join [Environment]::NewLine
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}
try {
    $parsedBitwiseConstantsDiagnostics = ConvertFrom-Json -InputObject $bitwiseConstantsText
}
catch {
    throw "Could not parse constant/enum overflow diagnostics as JSON. Exit code: $bitwiseConstantsExitCode. Output: $bitwiseConstantsText"
}
$bitwiseConstantsErrors = @($parsedBitwiseConstantsDiagnostics | Where-Object { $_.Severity -eq "error" })
$bitwiseConstantsErrorLines = @($bitwiseConstantsErrors | ForEach-Object { [int]$_.Line } | Sort-Object) -join ","
if ($bitwiseConstantsExitCode -eq 0 -or $bitwiseConstantsErrors.Count -ne 2 -or $bitwiseConstantsErrorLines -ne "3,6") {
    throw "Overflow validation did not cover const and enum expressions. Exit code: $bitwiseConstantsExitCode. Error lines: $bitwiseConstantsErrorLines. Output: $bitwiseConstantsText"
}

Write-Host "[2/20] Verifying invalid static method calls are rejected..."
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

Write-Host "[3/20] Verifying build rejects semantic errors..."
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

Write-Host "[4/20] Verifying strict assignment compatibility..."
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

Write-Host "[5/20] Checking the operator/Math fixture and embedded standard-library modules..."
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

dotnet run --configuration Release --no-build --project $project -- check $timerLibrary
if ($LASTEXITCODE -ne 0) {
    throw "Timer standard-library check failed with exit code $LASTEXITCODE"
}

dotnet run --configuration Release --no-build --project $project -- check $frameLibrary
if ($LASTEXITCODE -ne 0) {
    throw "Frame standard-library check failed with exit code $LASTEXITCODE"
}

Write-Host "[5a] Building typed frame controls fixture..."
$frameControlsFixture = Join-Path $PSScriptRoot "fixtures/FrameControlsSmoke.w3js"
$frameControlsBuildOutput = dotnet run --configuration Release --no-build --project $project -- build $frameControlsFixture --target lua
if ($LASTEXITCODE -ne 0) {
    throw "Frame controls fixture failed to compile: $($frameControlsBuildOutput -join [Environment]::NewLine)"
}
$frameControlsLuaPath = Join-Path $PSScriptRoot "fixtures/bin/FrameControlsSmoke.lua"
if (-not (Test-Path $frameControlsLuaPath)) {
    throw "Expected generated frame controls Lua file was not created: $frameControlsLuaPath"
}
$frameControlsLua = Get-Content -LiteralPath $frameControlsLuaPath -Raw
foreach ($fragment in @(
    '"CHECKBOX"',
    '"POPUPMENU"',
    '"MENU"',
    'FRAMEEVENT_CHECKBOX_CHECKED',
    'FRAMEEVENT_CHECKBOX_UNCHECKED',
    'FRAMEEVENT_POPUPMENU_ITEM_CHANGED',
    'function TextFrame:Subscribe'
)) {
    if (-not $frameControlsLua.Contains($fragment)) {
        throw "Generated frame controls Lua is missing expected fragment: $fragment"
    }
}

Write-Host "[5b] Building generic collection fixture..."
$collectionsFixture = Join-Path $PSScriptRoot "fixtures/CollectionsSmoke.w3js"
$collectionsBuildOutput = dotnet run --configuration Release --no-build --project $project -- build $collectionsFixture --target lua
if ($LASTEXITCODE -ne 0) {
    throw "Generic collections fixture failed to compile: $($collectionsBuildOutput -join [Environment]::NewLine)"
}
$collectionsLuaPath = Join-Path $PSScriptRoot "fixtures/bin/CollectionsSmoke.lua"
if (-not (Test-Path $collectionsLuaPath)) {
    throw "Expected generated collections Lua file was not created: $collectionsLuaPath"
}
$collectionsLua = Get-Content -LiteralPath $collectionsLuaPath -Raw
foreach ($fragment in @(
    '__queue_new()',
    '__queue_enqueue(queue, 10)',
    '__queue_dequeue(queue)',
    '__stack_new()',
    '__stack_push(stack, "first")',
    '__dictionary_new()',
    '__dictionary_add(scores, "Player1", 100)',
    '__dictionary_tryadd(scores, "Player2", 200)',
    '__dictionary_tryremove(scores, "Player2")',
    '__dictionary_set(scores, "Player1", 150)',
    '__collection_store(nil)',
    '__collection_load(',
    'pairs(__foreachCollection',
    'Key = __foreachIndex',
    'Value = __collection_load(__foreachValue'
)) {
    if (-not $collectionsLua.Contains($fragment)) {
        throw "Generated collections Lua is missing expected fragment: $fragment"
    }
}

Write-Host "[5c] Verifying generic collection type safety..."
$collectionsInvalidFixture = Join-Path $PSScriptRoot "fixtures/CollectionsTypeMismatchSmoke.w3js"
$previousErrorActionPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = "Continue"
    $collectionsDiagnostics = dotnet run --configuration Release --no-build --project $project -- check $collectionsInvalidFixture 2>&1
    $collectionsExitCode = $LASTEXITCODE
    $collectionsText = $collectionsDiagnostics -join "`n"
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}
try {
    $parsedCollectionsDiagnostics = ConvertFrom-Json -InputObject $collectionsText
}
catch {
    throw "Could not parse generic collection diagnostics as JSON. Exit code: $collectionsExitCode. Output: $collectionsText"
}
$collectionErrors = @($parsedCollectionsDiagnostics | Where-Object { $_.Severity -eq "error" })
if ($collectionsExitCode -eq 0 -or $collectionErrors.Count -ne 4) {
    throw "Generic collection type safety did not reject all four invalid type mismatches. Exit code: $collectionsExitCode. Errors: $($collectionErrors.Count). Output: $collectionsText"
}

Write-Host "[5d] Building interpolation and raw string fixture..."
$stringsFixture = Join-Path $PSScriptRoot "fixtures/StringsSmoke.w3js"
$stringsBuildOutput = dotnet run --configuration Release --no-build --project $project -- build $stringsFixture --target lua
if ($LASTEXITCODE -ne 0) {
    throw "String fixture failed to compile: $($stringsBuildOutput -join [Environment]::NewLine)"
}
$stringsLuaPath = Join-Path $PSScriptRoot "fixtures/bin/StringsSmoke.lua"
if (-not (Test-Path $stringsLuaPath)) {
    throw "Expected generated strings Lua file was not created: $stringsLuaPath"
}
$stringsLua = Get-Content -LiteralPath $stringsLuaPath -Raw
foreach ($fragment in @(
    'Product: ',
    'tostring(((x * y) * z))',
    'Result=',
    'enabled=',
    '{literal} and {',
    'First line\nSecond line\nThird line',
    'Product: ',
    'Enabled: ',
    'Braces: {literal}',
    'Line one\nLine two\tindented',
    'C:\\temp\\file'
)) {
    if (-not $stringsLua.Contains($fragment)) {
        throw "Generated strings Lua is missing expected fragment: $fragment"
    }
}

Write-Host "[5e] Verifying interpolation type safety..."
$stringsInvalidFixture = Join-Path $PSScriptRoot "fixtures/StringsTypeMismatchSmoke.w3js"
$previousErrorActionPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = "Continue"
    $stringsDiagnostics = dotnet run --configuration Release --no-build --project $project -- check $stringsInvalidFixture 2>&1
    $stringsExitCode = $LASTEXITCODE
    $stringsText = $stringsDiagnostics -join [Environment]::NewLine
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}
try {
    $parsedStringsDiagnostics = ConvertFrom-Json -InputObject $stringsText
}
catch {
    throw "Could not parse interpolation diagnostics as JSON. Exit code: $stringsExitCode. Output: $stringsText"
}
$stringsErrors = @($parsedStringsDiagnostics | Where-Object { $_.Severity -eq "error" })
if ($stringsExitCode -eq 0 -or $stringsErrors.Count -ne 1) {
    throw "Interpolation type safety did not reject assigning a string to int. Exit code: $stringsExitCode. Errors: $($stringsErrors.Count). Output: $stringsText"
}

Write-Host "[5f] Building the Strings standard-library API..."
$stringsApiFixture = Join-Path $PSScriptRoot "fixtures/StringsApiSmoke.w3js"
$stringsApiBuildOutput = dotnet run --configuration Release --no-build --project $project -- build $stringsApiFixture --target lua
if ($LASTEXITCODE -ne 0) {
    throw "Strings API fixture failed to compile: $($stringsApiBuildOutput -join [Environment]::NewLine)"
}
$stringsApiLuaPath = Join-Path $PSScriptRoot "fixtures/bin/StringsApiSmoke.lua"
if (-not (Test-Path $stringsApiLuaPath)) {
    throw "Expected generated Strings API Lua file was not created: $stringsApiLuaPath"
}
$stringsApiLua = Get-Content -LiteralPath $stringsApiLuaPath -Raw
foreach ($fragment in @(
    "function Strings.Substring(value, startIndex, length)",
    "function Strings.Replace(value, oldValue, newValue)",
    "function Strings.IndexOf(value, searchValue)",
    "function Strings.LastIndexOf(value, searchValue)",
    "function Strings.Contains(value, searchValue)",
    "function Strings.Trim(value)",
    "function Strings.TrimStart(value)",
    "function Strings.TrimEnd(value)",
    "function Strings.ToLower(value)",
    "function Strings.ToUpper(value)",
    "function Strings.Split(value, separator)",
    "function Strings.Join(separator, values)",
    "utf8.offset(value, (startIndex + 1))",
    "utf8.len(source)"
)) {
    if (-not $stringsApiLua.Contains($fragment)) {
        throw "Generated Strings API Lua is missing expected fragment: $fragment"
    }
}

Write-Host "[5g] Verifying do-while and Lua.Raw..."
$doWhileFixture = Join-Path $PSScriptRoot "fixtures/DoWhileSmoke.w3js"
$doWhileOutput = dotnet run --configuration Release --no-build --project $project -- build $doWhileFixture --target lua
if ($LASTEXITCODE -ne 0) {
    throw "Do-while fixture failed to compile: $($doWhileOutput -join [Environment]::NewLine)"
}
$doWhileLuaPath = Join-Path $PSScriptRoot "fixtures/bin/DoWhileSmoke.lua"
if (-not (Test-Path $doWhileLuaPath)) {
    throw "Expected generated do-while Lua file was not created: $doWhileLuaPath"
}
$doWhileLua = Get-Content -LiteralPath $doWhileLuaPath -Raw
if (-not $doWhileLua.Contains("repeat") -or -not $doWhileLua.Contains("until not ((value < 3))")) {
    throw "Do-while did not lower to Lua repeat/until. Output: $doWhileLua"
}

$luaRawFixture = Join-Path $PSScriptRoot "fixtures/LuaRawSmoke.w3js"
$luaRawOutput = dotnet run --configuration Release --no-build --project $project -- build $luaRawFixture --target lua
if ($LASTEXITCODE -ne 0) {
    throw "Lua.Raw fixture failed to compile: $($luaRawOutput -join [Environment]::NewLine)"
}
$luaRawPath = Join-Path $PSScriptRoot "fixtures/bin/LuaRawSmoke.lua"
if (-not (Test-Path $luaRawPath)) {
    throw "Expected generated Lua.Raw file was not created: $luaRawPath"
}
$luaRaw = Get-Content -LiteralPath $luaRawPath -Raw
if (-not $luaRaw.Contains("local rawValue = 42") -or -not $luaRaw.Contains("assert(rawValue == 42)")) {
    throw "Lua.Raw block was not emitted verbatim. Output: $luaRaw"
}

Write-Host "[5h] Verifying Lua.Raw restrictions and the removal of vJASS..."
$rawExpressionFixture = Join-Path $PSScriptRoot "fixtures/LuaRawExpressionRejectedSmoke.w3js"
$previousErrorActionPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = "Continue"
    $rawExpressionDiagnostics = dotnet run --configuration Release --no-build --project $project -- check $rawExpressionFixture 2>&1
    $rawExpressionExitCode = $LASTEXITCODE
    $rawExpressionText = $rawExpressionDiagnostics -join "`n"
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}
if ($rawExpressionExitCode -eq 0 -or -not $rawExpressionText.Contains("Lua.Raw")) {
    throw "Lua.Raw was accepted as an expression. Exit code: $rawExpressionExitCode. Output: $rawExpressionText"
}

$vjassDirectiveFixture = Join-Path $PSScriptRoot "fixtures/VjassDirectiveRejectedSmoke.w3js"
$previousErrorActionPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = "Continue"
    $vjassDirectiveDiagnostics = dotnet run --configuration Release --no-build --project $project -- check $vjassDirectiveFixture 2>&1
    $vjassDirectiveExitCode = $LASTEXITCODE
    $vjassDirectiveText = $vjassDirectiveDiagnostics -join "`n"
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}
if ($vjassDirectiveExitCode -eq 0) {
    throw "The removed target-conditional directive was accepted. Output: $vjassDirectiveText"
}

$previousErrorActionPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = "Continue"
    $vjassTargetOutput = dotnet run --configuration Release --no-build --project $project -- build $doWhileFixture --target vjass 2>&1
    $vjassTargetExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}
if ($vjassTargetExitCode -eq 0) {
    throw "The removed vJASS target was accepted. Output: $($vjassTargetOutput -join [Environment]::NewLine)"
}

Write-Host "[5i] Checking Color, floating text, lightning and Warcraft III 3.0 RPG modules..."
$visualLibraries = @(
    "Colors.w3js",
    "FloatingText.w3js",
    "Lightning.w3js",
    "Inventory.w3js",
    "RpgStats.w3js",
    "Effects.w3js",
    "Camera.w3js",
    "Audio.w3js"
)
foreach ($libraryName in $visualLibraries) {
    $libraryPath = Join-Path $repoRoot ("standard-library/" + $libraryName)
    $libraryOutput = dotnet run --configuration Release --no-build --project $project -- check $libraryPath
    if ($LASTEXITCODE -ne 0) {
        throw "$libraryName standard-library check failed: $($libraryOutput -join [Environment]::NewLine)"
    }
}
$visualFixture = Join-Path $PSScriptRoot "fixtures/VisualRpgSmoke.w3js"
$visualBuildOutput = dotnet run --configuration Release --no-build --project $project -- build $visualFixture --target lua
if ($LASTEXITCODE -ne 0) {
    throw "Visual/RPG fixture failed to compile: $($visualBuildOutput -join [Environment]::NewLine)"
}
$visualLuaPath = Join-Path $PSScriptRoot "fixtures/bin/VisualRpgSmoke.lua"
if (-not (Test-Path $visualLuaPath)) {
    throw "Expected visual/RPG Lua file was not created: $visualLuaPath"
}
$visualLua = Get-Content -LiteralPath $visualLuaPath -Raw
foreach ($fragment in @(
    "function Color.FromBytes(red, green, blue, alpha)",
    "function Color:ToArgb()",
    "BlzFrameSetTextColor(target, self:ToArgb())",
    "SetUnitVertexColor(target, self:RedByte(), self:GreenByte(), self:BlueByte(), self:AlphaByte())",
    "SetDestructableColor(target, color)",
    "SetLightningColor(target, self.R, self.G, self.B, self.A)",
    "function FloatingTextTag.new(text, x, y, z, size, lifespan, fadepoint)",
    "SetTextTagPosUnit(self._handle, target, zOffset)",
    "DestroyTextTag(self._handle)",
    "function LightningEffect.new(codeName, checkVisibility, start, end)",
    "MoveLightningEx(self._handle, checkVisibility",
    "DestroyLightning(self._handle)",
    "BlzUnitExtendedInventorySize(self._owner)",
    "BlzUnitItemInBagSlot(self._owner, slot)",
    "BlzUnitHasAnyItemEquipped(self._owner)",
    "ChooseRandomItemExWithFilterAndIncludes(type, level, equipment, tag, includeInvalidMorphs, includeNonPickRandom)",
    "BlzUnitEquipItem(self._owner, value)",
    "BlzUnitUnequipItemFromSlot(self._owner, slot)",
    "BlzResetUnitTalents(target)",
    "UnitAddAbility(target, abilityRawcode)",
    "SetUnitAbilityLevel(target, abilityRawcode, rank)",
    "UnitMakeAbilityPermanent(target, permanent, abilityRawcode)",
    "BlzUnitHeal(target, source, amount, isItem, applyStatBonuses)",
    "GetHeroStr(_owner, includeBonuses)",
    "GetHeroAgi(_owner, includeBonuses)",
    "GetHeroInt(_owner, includeBonuses)",
    "BlzGetUnitArmor(_owner)",
    "BlzSetUnitArmor(_owner, value)",
    "BlzGetUnitBaseDamage(_owner, weaponIndex)",
    "BlzSetUnitBaseDamage(_owner, value, weaponIndex)",
    "BlzGetUnitDiceNumber(_owner, weaponIndex)",
    "BlzSetUnitDiceNumber(_owner, value, weaponIndex)",
    "BlzGetUnitDiceSides(_owner, weaponIndex)",
    "BlzSetUnitDiceSides(_owner, value, weaponIndex)",
    "BlzGetUnitAttackCooldown(_owner, weaponIndex)",
    "BlzSetUnitAttackCooldown(_owner, seconds, weaponIndex)",
    "BlzGetUnitMaxHP(_owner)",
    "BlzSetUnitMaxHP(_owner, value)",
    "BlzGetUnitMaxMana(_owner)",
    "BlzSetUnitMaxMana(_owner, value)",
    "BlzSetUnitAbilityCooldownPercent(_owner, abilityRawcode, percent)",
    "BlzAdjustUnitAbilityCooldownPercent(_owner, abilityRawcode, deltaPercent)",
    "BlzRemoveEffect(value)",
    "BlzSetCameraAllowsHotkeyTargetLock(allowed)",
    "BlzGetCameraAllowsHotkeyTargetLock()",
    "SetThematicMusicAbsoluteVolume(volume)"
)) {
    if (-not $visualLua.Contains($fragment)) {
        throw "Generated visual/RPG Lua is missing expected fragment: $fragment"
    }
}
if ($visualLua.Contains("function UnitInventory.__gc") -or
    $visualLua.Contains("function FloatingTextTag.__gc") -or
    $visualLua.Contains("function LightningEffect.__gc")) {
    throw "Game handle wrappers must use explicit disposal, not Lua GC finalizers."
}

Write-Host "[6/20] Generating Lua and validating 2D geometry..."
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

Write-Host "[7/20] Building timer fixture and checking native timer integration..."
$timerFixture = Join-Path $PSScriptRoot "fixtures/TimerSmoke.w3js"
$timerBuildOutput = dotnet run --configuration Release --no-build --project $project -- build $timerFixture --target lua
if ($LASTEXITCODE -ne 0) {
    throw "Timer fixture failed to compile: $($timerBuildOutput -join [Environment]::NewLine)"
}
$timerLuaPath = Join-Path $PSScriptRoot "fixtures/bin/TimerSmoke.lua"
if (-not (Test-Path $timerLuaPath)) {
    throw "Expected generated timer Lua file was not created: $timerLuaPath"
}
$timerLua = Get-Content -LiteralPath $timerLuaPath -Raw
$timerFragments = @(
    "CreateTimer()",
    "TimerStart(self._handle, Math.Max(timeout, 0.0), false, callback)",
    "TimerStart(self._handle, Math.Max(timeout, 0.0), true, callback)",
    "PauseTimer(self._handle)",
    "ResumeTimer(self._handle)",
    "TimerGetElapsed(self._handle)",
    "TimerGetRemaining(self._handle)",
    "TimerGetTimeout(self._handle)",
    "function GameTimer:Dispose()",
    "DestroyTimer(self._handle)",
    "function()"
)
foreach ($fragment in $timerFragments) {
    if (-not $timerLua.Contains($fragment)) {
        throw "Generated timer Lua is missing expected fragment: $fragment"
    }
}
if ($timerLua.Contains("function GameTimer.__gc") -or $timerLua.Contains("newproxy")) {
    throw "Timer disposal must remain explicit and must not use Lua garbage collection."
}

Write-Host "[8/20] Generating Math/vector Lua..."
dotnet run --configuration Release --no-build --project $project -- build $fixture
if ($LASTEXITCODE -ne 0) {
    throw "Fixture build failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $output)) {
    throw "Expected generated Lua file was not created: $output"
}

Write-Host "[9/20] Verifying generated static calls, Math helpers, and operator adapters..."
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

Write-Host "[10/20] Verifying headless mode emits standalone Lua without Warcraft entry points..."
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

Write-Host "[11/20] Verifying explicit using Warcraft3 enables the built-in module..."
$warcraftProject = Join-Path $PSScriptRoot "headless-warcraft-smoke"
$warcraftBuildOutput = dotnet run --configuration Release --no-build --project $project -- map --project $warcraftProject
if ($LASTEXITCODE -ne 0) {
    throw "Headless Warcraft3 module build failed: $($warcraftBuildOutput -join [Environment]::NewLine)"
}
$warcraftLua = Get-Content -LiteralPath (Join-Path $warcraftProject "bin/HeadlessWarcraftUsing.lua") -Raw
if (-not $warcraftLua.Contains("CreateGroup()") -or $warcraftLua.Contains("function main()")) {
    throw "Explicit Warcraft3 module did not compile to standalone Lua as expected."
}

Write-Host "[12/20] Verifying jagged array parsing, typing, and Lua indexing..."
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

Write-Host "[13/20] Verifying incompatible array element types are rejected..."
$arrayMismatchFixture = Join-Path $PSScriptRoot "fixtures/ArrayTypeMismatchSmoke.w3js"
$arrayMismatchOutput = dotnet run --configuration Release --no-build --project $project -- check $arrayMismatchFixture 2>&1
if ($LASTEXITCODE -eq 0) {
    throw "Compiler accepted assignment from float[] to int[]."
}

Write-Host "[14/20] Verifying foreach array loops and Warcraft group iteration..."
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

Write-Host "[15/20] Verifying unsupported foreach collection types are rejected..."
$unsupportedForeachFixture = Join-Path $PSScriptRoot "fixtures/ForeachUnsupportedSmoke.w3js"
$unsupportedForeachOutput = dotnet run --configuration Release --no-build --project $project -- check $unsupportedForeachFixture 2>&1
if ($LASTEXITCODE -eq 0) {
    throw "Compiler accepted foreach over a scalar value."
}

Write-Host "[16/20] Verifying manually invoked class destructors..."
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

Write-Host "[17/20] Verifying invalid destructor declarations are rejected..."
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


Write-Host "[18/20] Verifying native UnitGroup ownership, operations, callbacks, and disposal..."
$groupLibrary = Join-Path $repoRoot "standard-library/Groups.w3js"
$groupFixture = Join-Path $PSScriptRoot "fixtures/GroupSmoke.w3js"

dotnet run --configuration Release --no-build --project $project -- check $groupLibrary
if ($LASTEXITCODE -ne 0) {
    throw "Groups standard-library check failed with exit code $LASTEXITCODE"
}

$groupBuildOutput = dotnet run --configuration Release --no-build --project $project -- build $groupFixture --target lua
if ($LASTEXITCODE -ne 0) {
    throw "Group fixture failed to compile: $($groupBuildOutput -join [Environment]::NewLine)"
}
$groupLuaPath = Join-Path $PSScriptRoot "fixtures/bin/GroupSmoke.lua"
if (-not (Test-Path $groupLuaPath)) {
    throw "Expected generated group Lua file was not created: $groupLuaPath"
}
$groupLua = Get-Content -LiteralPath $groupLuaPath -Raw
$groupFragments = @(
    "CreateGroup()",
    "GroupAddUnit(self._handle, value)",
    "GroupRemoveUnit(self._handle, value)",
    "GroupClear(self._handle)",
    "BlzGroupGetSize(self._handle)",
    "FirstOfGroup(self._handle)",
    "ForGroup(self._handle, callback)",
    "function UnitGroup:Dispose()",
    "DestroyGroup(self._handle)",
    "function()",
    "GetEnumUnit()"
)
foreach ($fragment in $groupFragments) {
    if (-not $groupLua.Contains($fragment)) {
        throw "Generated group Lua is missing expected fragment: $fragment"
    }
}
if ($groupLua.Contains("function UnitGroup.__gc") -or $groupLua.Contains("newproxy")) {
    throw "Group disposal must remain explicit and must not use Lua garbage collection."
}


Write-Host "[19/20] Verifying unit ability wrapper, cooldowns, and typed ability fields..."
$abilityLibrary = Join-Path $repoRoot "standard-library/Abilities.w3js"
$abilityFixture = Join-Path $PSScriptRoot "fixtures/AbilitySmoke.w3js"

dotnet run --configuration Release --no-build --project $project -- check $abilityLibrary
if ($LASTEXITCODE -ne 0) {
    throw "Abilities standard-library check failed with exit code $LASTEXITCODE"
}

$abilityBuildOutput = dotnet run --configuration Release --no-build --project $project -- build $abilityFixture --target lua
if ($LASTEXITCODE -ne 0) {
    throw "Ability fixture failed to compile: $($abilityBuildOutput -join [Environment]::NewLine)"
}
$abilityLuaPath = Join-Path $PSScriptRoot "fixtures/bin/AbilitySmoke.lua"
if (-not (Test-Path $abilityLuaPath)) {
    throw "Expected generated ability Lua file was not created: $abilityLuaPath"
}
$abilityLua = Get-Content -LiteralPath $abilityLuaPath -Raw
$abilityFragments = @(
    "AbilityId(",
    "GetUnitAbilityLevel(self._owner, self._rawcode)",
    "UnitAddAbility(self._owner, self._rawcode)",
    "UnitRemoveAbility(self._owner, self._rawcode)",
    "SetUnitAbilityLevel(self._owner, self._rawcode, level)",
    "IncUnitAbilityLevel(self._owner, self._rawcode)",
    "DecUnitAbilityLevel(self._owner, self._rawcode)",
    "UnitMakeAbilityPermanent(self._owner, permanent, self._rawcode)",
    "BlzUnitDisableAbility(self._owner, self._rawcode",
    "BlzGetUnitAbilityCooldown(self._owner, self._rawcode, level)",
    "level < 0",
    "BlzGetUnitAbilityCooldownRemaining(self._owner, self._rawcode)",
    "BlzStartUnitAbilityCooldown(self._owner, self._rawcode",
    "BlzEndUnitAbilityCooldown(self._owner, self._rawcode)",
    "BlzGetUnitAbilityManaCost(self._owner, self._rawcode, level)",
    "BlzGetUnitAbility(self._owner, self._rawcode)",
    "BlzGetAbilityBooleanField(self:Handle(), field)",
    "BlzSetAbilityBooleanField(self:Handle(), field, value)",
    "BlzGetAbilityIntegerField(self:Handle(), field)",
    "BlzSetAbilityIntegerField(self:Handle(), field, value)",
    "BlzGetAbilityRealField(self:Handle(), field)",
    "BlzSetAbilityRealField(self:Handle(), field, value)",
    "BlzGetAbilityStringField(self:Handle(), field)",
    "BlzSetAbilityStringField(self:Handle(), field, value)",
    "BlzGetAbilityBooleanLevelField(self:Handle(), field, level)",
    "BlzSetAbilityBooleanLevelField(self:Handle(), field, level, value)",
    "BlzGetAbilityBooleanLevelArrayField(self:Handle(), field, level, index)",
    "BlzSetAbilityBooleanLevelArrayField(self:Handle(), field, level, index, value)",
    "BlzAddAbilityBooleanLevelArrayField(self:Handle(), field, level, value)",
    "BlzRemoveAbilityBooleanLevelArrayField(self:Handle(), field, level, value)",
    "BlzGetAbilityIntegerLevelField(self:Handle(), field, level)",
    "BlzSetAbilityIntegerLevelField(self:Handle(), field, level, value)",
    "BlzGetAbilityIntegerLevelArrayField(self:Handle(), field, level, index)",
    "BlzSetAbilityIntegerLevelArrayField(self:Handle(), field, level, index, value)",
    "BlzAddAbilityIntegerLevelArrayField(self:Handle(), field, level, value)",
    "BlzRemoveAbilityIntegerLevelArrayField(self:Handle(), field, level, value)",
    "BlzGetAbilityRealLevelField(self:Handle(), field, level)",
    "BlzSetAbilityRealLevelField(self:Handle(), field, level, value)",
    "BlzGetAbilityRealLevelArrayField(self:Handle(), field, level, index)",
    "BlzSetAbilityRealLevelArrayField(self:Handle(), field, level, index, value)",
    "BlzAddAbilityRealLevelArrayField(self:Handle(), field, level, value)",
    "BlzRemoveAbilityRealLevelArrayField(self:Handle(), field, level, value)",
    "BlzGetAbilityStringLevelField(self:Handle(), field, level)",
    "BlzSetAbilityStringLevelField(self:Handle(), field, level, value)",
    "BlzGetAbilityStringLevelArrayField(self:Handle(), field, level, index)",
    "BlzSetAbilityStringLevelArrayField(self:Handle(), field, level, index, value)",
    "BlzAddAbilityStringLevelArrayField(self:Handle(), field, level, value)",
    "BlzRemoveAbilityStringLevelArrayField(self:Handle(), field, level, value)"
)
foreach ($fragment in $abilityFragments) {
    if (-not $abilityLua.Contains($fragment)) {
        throw "Generated ability Lua is missing expected fragment: $fragment"
    }
}
if ($abilityLua.Contains("function UnitAbility:Dispose()") -or $abilityLua.Contains("UnitAbility.__gc")) {
    throw "UnitAbility is a non-owning wrapper and must not generate disposal code."
}

Write-Host "All JASS# compiler smoke checks passed."

Write-Host "[20/20] Verifying Warcraft III Frame API wrappers, ownership, events, and coordinates..."
$frameFixture = Join-Path $PSScriptRoot "fixtures/FrameSmoke.w3js"
$frameBuildOutput = dotnet run --configuration Release --no-build --project $project -- build $frameFixture --target lua
if ($LASTEXITCODE -ne 0) {
    throw "Frame fixture failed to compile: $($frameBuildOutput -join [Environment]::NewLine)"
}
$frameLuaPath = Join-Path $PSScriptRoot "fixtures/bin/FrameSmoke.lua"
if (-not (Test-Path $frameLuaPath)) {
    throw "Expected generated frame Lua file was not created: $frameLuaPath"
}
$frameLua = Get-Content -LiteralPath $frameLuaPath -Raw
$frameFragments = @(
    "BlzLoadTOCFile(",
    "BlzHideOriginFrames(",
    "BlzGetHUDScale(",
    "BlzGetLocalClientWidth(",
    "BlzGetLocalClientHeight(",
    "BlzCreateFrame(",
    "BlzCreateSimpleFrame(",
    "BlzCreateFrameByType(",
    "BlzGetOriginFrame(",
    "BlzGetFrameByName(",
    "BlzFrameSetPoint(",
    "BlzFrameSetAbsPoint(",
    "BlzFrameSetAllPoints(",
    "BlzFrameSetText(",
    "BlzFrameSetTexture(",
    "BlzFrameSetSize(",
    "BlzFrameSetAlpha(",
    "BlzFrameSetEnable(",
    "BlzFrameSetValue(",
    "BlzFrameGetChild(",
    "BlzTriggerRegisterFrameEvent(",
    "BlzGetTriggerFrame(",
    "BlzGetTriggerFrameEvent(",
    "BlzGetTriggerFrameValue(",
    "BlzGetTriggerFrameText(",
    "BlzPixelToFrameX(",
    "BlzPixelToFrameY(",
    "BlzFrameToPixelX(",
    "BlzFrameToPixelY(",
    "function Frame:Dispose()",
    "BlzFrameClick(_handle)",
        "BlzFrameSetText(_handle, value)",
        "BlzFrameSetValue(_handle, value)",
        "BlzFrameSetMinMaxValue(_handle, minimum, maximum)",
        "BlzFrameSetFocus(_handle, value)",
        "BlzTriggerRegisterFrameEvent(_trigger, frame, eventType)",
        "TriggerAddAction(_trigger, callback)",
        "TriggerRemoveAction(_trigger, _action)",
        "DestroyTrigger(_trigger)",
        "function FrameEventSubscription:Dispose()",
        "function ButtonFrame:Dispose()",
        "function TextFrame:Dispose()",
        "function SliderFrame:Dispose()",
        "function EditBoxFrame:Dispose()",
    "BlzDestroyFrame(self._handle)"
)
foreach ($fragment in $frameFragments) {
    if (-not $frameLua.Contains($fragment)) {
        throw "Generated frame Lua is missing expected fragment: $fragment"
    }
}
foreach ($className in @("ButtonFrame", "TextFrame", "SliderFrame", "EditBoxFrame", "FrameEventSubscription")) {
    if ($frameLua.Contains("function " + $className + ".__gc")) {
        throw "$className must use explicit disposal and must not generate Lua garbage-collection finalizers."
    }
}
if ($frameLua.Contains("function Frame.__gc") -or $frameLua.Contains("newproxy")) {
    throw "Frame disposal must remain explicit and must not use Lua garbage collection."
}

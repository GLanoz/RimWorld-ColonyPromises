param(
    [string]$RimWorldDir = 'C:\Games\Steam\steamapps\common\RimWorld',
    [string]$RimWorldVersion = '1.6',
    [string]$GameManagedDir,
    [string]$CecilPath = 'C:\Users\vladl\.nuget\packages\mono.cecil\0.11.4\lib\net40\Mono.Cecil.dll'
)
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'GamePaths.ps1')
$paths = Get-PromiseGamePaths $RimWorldVersion $RimWorldDir $GameManagedDir
Add-Type -Path $CecilPath
$gameAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($paths.Game)
$modAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($paths.Mod)
$patches = 0
foreach ($type in $modAssembly.MainModule.Types) {
    foreach ($attribute in $type.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch' -and $_.ConstructorArguments.Count -eq 2 }) {
        $typeName = $attribute.ConstructorArguments[0].Value.FullName
        $methodName = $attribute.ConstructorArguments[1].Value
        $gameType = $gameAssembly.MainModule.Types | Where-Object FullName -eq $typeName
        $methods = @($gameType.Methods | Where-Object Name -eq $methodName)
        if ($methods.Count -ne 1) { throw "Patch target must be unambiguous: $typeName.$methodName" }
        foreach ($patchMethod in $type.Methods | Where-Object { $_.Name -in @('Prefix','Postfix') }) {
            foreach ($argument in $patchMethod.Parameters) {
                if ($argument.Name -eq '__state' -or $argument.Name -eq '__originalMethod') { continue }
                $expectedType = $null
                if ($argument.Name -eq '__instance') { $expectedType = $gameType }
                elseif ($argument.Name -eq '__result') { $expectedType = $methods[0].ReturnType }
                elseif ($argument.Name -match '^___(.+)$') {
                    $field = $gameType.Fields | Where-Object Name -eq $Matches[1]
                    if (!$field) { throw "Missing Harmony-injected field: $($type.Name).$($argument.Name)" }
                    $expectedType = $field.FieldType
                }
                elseif ($argument.Name -match '^__(\d+)$') {
                    $index = [int]$Matches[1]
                    if ($index -ge $methods[0].Parameters.Count) { throw "Missing indexed Harmony argument: $($type.Name).$($argument.Name)" }
                    $expectedType = $methods[0].Parameters[$index].ParameterType
                }
                if ($null -ne $expectedType) {
                    if ($expectedType.FullName -ne $argument.ParameterType.FullName) { throw "Incompatible injected Harmony argument: $($type.Name).$($argument.Name)" }
                    continue
                }
                $originalArgument = $methods[0].Parameters | Where-Object Name -eq $argument.Name
                if (!$originalArgument -or $originalArgument.ParameterType.FullName -ne $argument.ParameterType.FullName) {
                    throw "Invalid Harmony argument: $($type.Name).$($argument.Name)"
                }
            }
        }
        $patches++
    }
}
$roomType = $gameAssembly.MainModule.Types | Where-Object FullName -eq 'Verse.Room'
foreach ($methodName in @('Notify_ContainedThingSpawnedOrDespawned','Notify_TerrainChanged','Notify_BedTypeChanged','Notify_RoofChanged','Notify_RoomShapeChanged')) {
    if (@($roomType.Methods | Where-Object Name -eq $methodName).Count -ne 1) { throw "Room patch target unavailable: $methodName" }
    $patches++
}
$xmlCount = 0
foreach ($file in Get-ChildItem -LiteralPath $modRoot -Recurse -Filter '*.xml' | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notmatch '\\tools\\output\\' }) {
    [xml]$xml = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    $xmlCount++
}
[xml]$english = Get-Content -LiteralPath (Join-Path $modRoot 'Languages\English\Keyed\Promises.xml') -Raw -Encoding UTF8
[xml]$ukrainian = Get-Content -LiteralPath (Join-Path $modRoot 'Languages\Ukrainian\Keyed\Promises.xml') -Raw -Encoding UTF8
$englishKeys = @($english.LanguageData.ChildNodes | Where-Object NodeType -eq 'Element' | ForEach-Object Name)
$ukrainianKeys = @($ukrainian.LanguageData.ChildNodes | Where-Object NodeType -eq 'Element' | ForEach-Object Name)
if ($englishKeys.Count -ne @($englishKeys | Sort-Object -Unique).Count) { throw 'Duplicate English key' }
if ($ukrainianKeys.Count -ne @($ukrainianKeys | Sort-Object -Unique).Count) { throw 'Duplicate Ukrainian key' }
if (Compare-Object $englishKeys $ukrainianKeys) { throw 'Localization key sets differ' }
foreach ($key in $englishKeys) {
    $englishValue = $english.LanguageData.SelectSingleNode($key).InnerText
    $ukrainianValue = $ukrainian.LanguageData.SelectSingleNode($key).InnerText
    if (($englishValue -match '\{\d+:' -or $ukrainianValue -match '\{\d+:')) {
        throw "UI must use plain RimWorld placeholders; format numbers before translation: $key"
    }
    $enSlots = @([regex]::Matches($englishValue, '\{(\d+)(?:[^}]*)\}') | ForEach-Object { $_.Groups[1].Value } | Sort-Object)
    $ukSlots = @([regex]::Matches($ukrainianValue, '\{(\d+)(?:[^}]*)\}') | ForEach-Object { $_.Groups[1].Value } | Sort-Object)
    if (Compare-Object $enSlots $ukSlots) { throw "Placeholder mismatch in $key" }
}
foreach ($source in Get-ChildItem -LiteralPath (Join-Path $modRoot 'Source') -Filter '*.cs' -Recurse) {
    $code = Get-Content -LiteralPath $source.FullName -Raw
    foreach ($match in [regex]::Matches($code, '"(CP_[A-Za-z0-9]+)"\.Translate')) {
        if ($match.Groups[1].Value -notin $englishKeys) { throw "Missing localization: $($match.Groups[1].Value)" }
    }
}
# These common game APIs carry optional expansion rules; verify the relied-on call paths,
# not just the presence of a method with the right name. This is not a gameplay test.
$callPaths = @(
    @('RimWorld.RestUtility', 'CanUseBedEver', 'CanDeathrest'),
    @('RimWorld.RestUtility', 'CanUseBedNow', 'get_HarmedByVacuum'),
    @('RimWorld.EquipmentUtility', 'CanEquip', 'RolePreventsFromUsing'),
    @('RimWorld.FoodUtility', 'DrugIsSuitable', 'CanUseDrug'),
    @('RimWorld.PawnUtility', 'CanTakeDrug', 'CanTakeDrugForDependency'),
    @('RimWorld.BedUtility', 'WillingToShareBed', 'DoerWillingToDo'),
    @('RimWorld.Pawn_NeedsTracker', 'ShouldHaveNeed', 'DisablesNeed'),
    @('Verse.Map', 'get_IsPlayerHome', 'PlayerHasGravEngine')
)
foreach ($path in $callPaths) {
    $type = $gameAssembly.MainModule.Types | Where-Object FullName -eq $path[0]
    $calls = @($type.Methods | Where-Object Name -eq $path[1] | ForEach-Object {
        $_.Body.Instructions | Where-Object { $_.OpCode.Name -in @('call','callvirt') } | ForEach-Object { $_.Operand.Name }
    })
    if ($path[2] -notin $calls) { throw "Optional DLC API call path changed: $($path -join ' / ')" }
}
$swimming = $gameAssembly.MainModule.Types | Where-Object FullName -eq 'RimWorld.JobDriver_GoSwimming'
$swimmingTypes = @($swimming) + @($swimming.NestedTypes)
$joyCalls = @($swimmingTypes | ForEach-Object { $_.Methods } | ForEach-Object {
    if ($_.HasBody) { $_.Body.Instructions | Where-Object { $_.OpCode.Name -in @('call','callvirt') } | ForEach-Object { $_.Operand.Name } }
})
if ('JoyTickCheckEnd' -notin $joyCalls) { throw 'Swimming no longer exposes the supported recreation tick callback.' }
# Reflection-only fields must remain valid even when public call sites still compile.
foreach ($entry in @(@('RimWorld.Pawn_DrugPolicyTracker','curPolicy'),
    @('RimWorld.IdeoStyleTracker','hairFrequencies'), @('RimWorld.IdeoStyleTracker','beardFrequencies'),
    @('RimWorld.IdeoStyleTracker','tattooFrequencies'))) {
    $type = $gameAssembly.MainModule.Types | Where-Object FullName -eq $entry[0]
    if (!($type.Fields | Where-Object Name -eq $entry[1])) { throw "Private game field changed: $($entry -join '.')" }
}
$ingestion = ($gameAssembly.MainModule.Types | Where-Object FullName -eq 'Verse.Thing').Methods | Where-Object Name -eq 'Ingested'
if (!($ingestion.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'callvirt' -and $_.Operand.Name -eq 'DoIngestionOutcome' })) { throw 'Ingestion outcome callback changed.' }
$joy = ($gameAssembly.MainModule.Types | Where-Object FullName -eq 'RimWorld.JoyUtility').Methods | Where-Object Name -eq 'JoyTickCheckEnd'
if (!($joy.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'callvirt' -and $_.Operand.Name -eq 'EndJobWith' })) { throw 'Recheck recreation Prefix timing.' }
$loader = ($gameAssembly.MainModule.Types | Where-Object FullName -eq 'Verse.ModAssemblyHandler').Methods | Where-Object Name -eq 'ReloadAll'
if (!($loader.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldstr' -and $_.Operand -eq 'Assemblies/' })) { throw 'Assembly load-directory contract changed.' }
[xml]$folders = Get-Content -LiteralPath (Join-Path $modRoot 'LoadFolders.xml') -Raw
[xml]$about = Get-Content -LiteralPath (Join-Path $modRoot 'About/About.xml') -Raw
foreach ($version in @('1.6','1.7')) {
    $selected = @($folders.loadFolders.SelectSingleNode("v$version").li)
    if (($selected -join ',') -ne "/,$version" -or $version -notin $about.ModMetaData.supportedVersions.li) { throw "Invalid load folders or supportedVersions for $version" }
    $dlls = @($selected | ForEach-Object {
        $directory = if ($_ -eq '/') { Join-Path $modRoot 'Assemblies' } else { Join-Path $modRoot "$_/Assemblies" }
        if (Test-Path -LiteralPath $directory) { Get-ChildItem -LiteralPath $directory -Recurse -Filter '*.dll' }
    })
    if ($dlls.Count -ne 1 -or $dlls[0].FullName -ne (Join-Path $modRoot "$version\Assemblies\ColonyPromises.dll")) { throw "Incorrect or duplicate assemblies for $version" }
    $built = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($dlls[0].FullName)
    try {
        $reference = $built.MainModule.AssemblyReferences | Where-Object Name -eq 'Assembly-CSharp'
        $harmony = $built.MainModule.AssemblyReferences | Where-Object Name -eq '0Harmony'
        $target = ($built.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'System.Runtime.Versioning.TargetFrameworkAttribute' }).ConstructorArguments[0].Value
        $expectedTarget = if ($version -eq '1.6') { '.NETFramework,Version=v4.7.2' } else { '.NETCoreApp,Version=v10.0' }
        $expectedHarmony = if ($version -eq '1.6') { 2 } else { 3 }
        if ("$($reference.Version.Major).$($reference.Version.Minor)" -ne $version -or $target -ne $expectedTarget -or $harmony.Version.Major -ne $expectedHarmony) { throw "Wrong game/runtime/Harmony reference in $version output." }
    } finally { $built.Dispose() }
}
$gameAssembly.Dispose()
$modAssembly.Dispose()
Write-Output "Verified $patches Harmony targets, $xmlCount XML files, $($englishKeys.Count) keyed translations and placeholder parity."

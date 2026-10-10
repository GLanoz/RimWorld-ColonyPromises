param(
    [string]$RimWorldDir = 'C:\Games\Steam\steamapps\common\RimWorld',
    [string]$CecilPath = 'C:\Users\vladl\.nuget\packages\mono.cecil\0.11.4\lib\net40\Mono.Cecil.dll'
)
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path $CecilPath
$gameAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $RimWorldDir 'RimWorldWin64_Data\Managed\Assembly-CSharp.dll'))
$modAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $modRoot '1.6\Assemblies\ColonyPromises.dll'))
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
                if ($argument.Name -match '^__') { continue }
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
foreach ($file in Get-ChildItem -LiteralPath $modRoot -Recurse -Filter '*.xml' | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }) {
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
$gameAssembly.Dispose()
$modAssembly.Dispose()
Write-Output "Verified $patches Harmony targets, $xmlCount XML files, $($englishKeys.Count) keyed translations and placeholder parity."

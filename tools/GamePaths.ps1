$script:PromiseToolRoot = $PSScriptRoot
function Get-PromiseGamePaths {
    param([string]$RimWorldVersion = '1.6', [string]$RimWorldDir, [string]$GameManagedDir)
    if ($RimWorldVersion -notin @('1.6','1.7')) { throw 'RimWorldVersion must be 1.6 or 1.7.' }
    if (!$GameManagedDir) {
        $GameManagedDir = if ($RimWorldVersion -eq '1.7') { $RimWorldDir } else { Join-Path $RimWorldDir 'RimWorldWin64_Data/Managed' }
    }
    $assemblyPath = Join-Path $GameManagedDir 'Assembly-CSharp.dll'
    if (!(Test-Path -LiteralPath $assemblyPath)) { throw "Game assembly missing: $assemblyPath. Set RimWorldDir or GameManagedDir." }
    $version = [Reflection.AssemblyName]::GetAssemblyName($assemblyPath).Version
    if ("$($version.Major).$($version.Minor)" -ne $RimWorldVersion) { throw "Expected RimWorld $RimWorldVersion; found $version." }
    $modRoot = Split-Path -Parent $script:PromiseToolRoot
    return @{ Managed = $GameManagedDir; Game = $assemblyPath;
        Mod = Join-Path $modRoot "$RimWorldVersion/Assemblies/ColonyPromises.dll";
        TestDir = Join-Path $modRoot "Tests/GameChecks/bin/$RimWorldVersion/Release/net10.0" }
}

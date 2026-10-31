param([string]$RimWorldDir = 'C:\Games\Steam\steamapps\common\RimWorld', [string]$RimWorldVersion = '1.6', [string]$GameManagedDir)
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $modRoot 'tools/GamePaths.ps1')
$paths = Get-PromiseGamePaths $RimWorldVersion $RimWorldDir $GameManagedDir
$GameManagedDir = $paths.Managed
# The game's grammar resolver uses .NET Standard 2.1 APIs missing from Windows PowerShell's CLR.
# Run the actual game formatter in the .NET 10 test process, without starting Unity.
dotnet run --project (Join-Path $PSScriptRoot 'GameChecks/ColonyPromises.GameChecks.csproj') -c Release "-p:RimWorldDir=$RimWorldDir" "-p:RimWorldVersion=$RimWorldVersion" "-p:GameManagedDir=$GameManagedDir" -- $modRoot
if ($LASTEXITCODE -ne 0) { throw 'History checks failed.' }

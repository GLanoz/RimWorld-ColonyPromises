param([string]$RimWorldDir = 'C:\Games\Steam\steamapps\common\RimWorld', [string]$RimWorldVersion = '1.6', [string]$GameManagedDir)
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $modRoot 'tools/GamePaths.ps1')
$paths = Get-PromiseGamePaths $RimWorldVersion $RimWorldDir $GameManagedDir
# Use the same compiled fixtures for both game runtimes; never load .NET 10 into Windows PowerShell.
dotnet run --project (Join-Path $PSScriptRoot 'GameChecks/ColonyPromises.GameChecks.csproj') -c Release "-p:RimWorldDir=$RimWorldDir" "-p:RimWorldVersion=$RimWorldVersion" "-p:GameManagedDir=$($paths.Managed)" -- --requests-only $modRoot
if ($LASTEXITCODE -ne 0) { throw 'Compiled fixture checks failed.' }

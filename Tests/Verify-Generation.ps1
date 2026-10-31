param([string]$RimWorldDir = 'C:\Games\Steam\steamapps\common\RimWorld', [string]$RimWorldVersion = '1.6', [string]$GameManagedDir)
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $modRoot 'tools/GamePaths.ps1')
$paths = Get-PromiseGamePaths $RimWorldVersion $RimWorldDir $GameManagedDir
$GameManagedDir = $paths.Managed
dotnet run --project (Join-Path $PSScriptRoot 'GameChecks\ColonyPromises.GameChecks.csproj') -c Release "-p:RimWorldDir=$RimWorldDir" "-p:RimWorldVersion=$RimWorldVersion" "-p:GameManagedDir=$GameManagedDir" -- --generation-only $modRoot
if ($LASTEXITCODE -ne 0) { throw "Generation checks failed with exit code $LASTEXITCODE" }

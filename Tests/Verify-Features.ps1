param([string]$RimWorldDir = 'C:\Games\Steam\steamapps\common\RimWorld')
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
dotnet run --project (Join-Path $PSScriptRoot 'GameChecks\ColonyPromises.GameChecks.csproj') -c Release "-p:RimWorldDir=$RimWorldDir" -- --features-only $modRoot
if ($LASTEXITCODE -ne 0) { throw "Feature checks failed with exit code $LASTEXITCODE" }

param([string]$RimWorldDir = 'C:\Games\Steam\steamapps\common\RimWorld')
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
dotnet run --project (Join-Path $PSScriptRoot 'HistoryChecks\ColonyPromises.HistoryChecks.csproj') -c Release "-p:RimWorldDir=$RimWorldDir" -- --generation-only $modRoot
if ($LASTEXITCODE -ne 0) { throw "Generation checks failed with exit code $LASTEXITCODE" }

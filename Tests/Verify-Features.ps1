param([string]$RimWorldDir = 'C:\Games\Steam\steamapps\common\RimWorld', [string]$CecilPath)
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
dotnet build (Join-Path $PSScriptRoot 'GameChecks\ColonyPromises.GameChecks.csproj') -c Release "-p:RimWorldDir=$RimWorldDir"
if ($LASTEXITCODE -ne 0) { throw 'Feature fixture build failed.' }
if (!$CecilPath) {
    $CecilPath = Get-ChildItem -LiteralPath (Join-Path $env:USERPROFILE '.nuget\packages\mono.cecil') -Recurse -Filter Mono.Cecil.dll |
        Where-Object { $_.Directory.Name -eq 'net40' } | Select-Object -First 1 -ExpandProperty FullName
}
Add-Type -Path $CecilPath
$testAssembly = Join-Path $PSScriptRoot 'GameChecks\bin\Release\net10.0\Assembly-CSharp.dll'
$gameAssembly = Join-Path $RimWorldDir 'RimWorldWin64_Data\Managed\Assembly-CSharp.dll'
# Only the disposable test copy is changed. Do not initialize or write the player's ModsConfig.
$originalBytes = [IO.File]::ReadAllBytes($gameAssembly)
try {
    $resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
    $resolver.AddSearchDirectory((Split-Path -Parent $gameAssembly))
    $parameters = [Mono.Cecil.ReaderParameters]::new()
    $parameters.AssemblyResolver = $resolver
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($gameAssembly, $parameters)
    $type = $assembly.MainModule.Types | Where-Object FullName -eq 'Verse.ModsConfig'
    $initializer = $type.Methods | Where-Object Name -eq '.cctor'
    if (!$initializer -or @($type.Fields | Where-Object Name -in @('royaltyActive','ideologyActive','biotechActive','anomalyActive','odysseyActive')).Count -ne 5) {
        throw 'Unexpected ModsConfig fixture API.'
    }
    $initializer.Body.Instructions.Clear()
    $initializer.Body.ExceptionHandlers.Clear()
    $initializer.Body.Variables.Clear()
    $initializer.Body.GetILProcessor().Append([Mono.Cecil.Cil.Instruction]::Create([Mono.Cecil.Cil.OpCodes]::Ret))
    # Keep royal tracker collections, replacing only the Unity command-icon load with null.
    $royalType = $assembly.MainModule.Types | Where-Object FullName -eq 'RimWorld.Pawn_RoyaltyTracker'
    $royalInitializer = $royalType.Methods | Where-Object Name -eq '.cctor'
    $iconCall = $royalInitializer.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'call' -and $_.Operand.FullName -like '*ContentFinder*::Get*' }
    if (@($iconCall).Count -ne 1 -or $iconCall.Previous.Previous.OpCode.Name -ne 'ldstr' -or $iconCall.Previous.OpCode.Name -ne 'ldc.i4.1') {
        throw 'Unexpected royal command-icon initializer.'
    }
    $iconPath = $iconCall.Previous.Previous
    $iconFlag = $iconCall.Previous
    $processor = $royalInitializer.Body.GetILProcessor()
    $processor.Remove($iconPath)
    $processor.Remove($iconFlag)
    $processor.Replace($iconCall, [Mono.Cecil.Cil.Instruction]::Create([Mono.Cecil.Cil.OpCodes]::Ldnull))
    $assembly.Write($testAssembly)
    $assembly.Dispose()
    $previousGameData = $env:COLONY_PROMISES_GAME_DATA
    $previousGameRoot = $env:COLONY_PROMISES_GAME_ROOT
    $env:COLONY_PROMISES_GAME_DATA = Join-Path $RimWorldDir 'Data\Core\Defs\Drugs'
    $env:COLONY_PROMISES_GAME_ROOT = $RimWorldDir
    try {
        dotnet (Join-Path $PSScriptRoot 'GameChecks\bin\Release\net10.0\ColonyPromises.GameChecks.dll') --features-only $modRoot
    } finally {
        $env:COLONY_PROMISES_GAME_DATA = $previousGameData
        $env:COLONY_PROMISES_GAME_ROOT = $previousGameRoot
    }
    if ($LASTEXITCODE -ne 0) { throw "Feature checks failed with exit code $LASTEXITCODE" }
} finally {
    [IO.File]::WriteAllBytes($testAssembly, $originalBytes)
}

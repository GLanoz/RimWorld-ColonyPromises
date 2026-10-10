param(
    [Parameter(Mandatory = $true)]
    [string]$RimWorldDir
)

# Run in a fresh Windows PowerShell process after building the mod.
# These fixtures execute the compiled handler and Prefix without starting RimWorld.
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
$managedPath = Join-Path $RimWorldDir 'RimWorldWin64_Data\Managed'
[System.Reflection.Assembly]::LoadFrom((Join-Path $managedPath 'UnityEngine.CoreModule.dll')) | Out-Null
[System.Reflection.Assembly]::LoadFrom((Join-Path $managedPath 'Assembly-CSharp.dll')) | Out-Null
$modAssembly = [System.Reflection.Assembly]::LoadFrom((Join-Path $modRoot '1.6\Assemblies\ColonyPromises.dll'))
$privateInstance = [System.Reflection.BindingFlags]'NonPublic,Instance'
$privateStatic = [System.Reflection.BindingFlags]'NonPublic,Static'
$script:assertions = 0
$script:nextPawnId = 1

function New-Uninitialized([Type]$type) {
    return [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($type)
}

function Assert-Equal($expected, $actual, [string]$scenario) {
    $script:assertions++
    if ($expected -ne $actual) { throw "${scenario}: expected '$expected', got '$actual'" }
}

function Assert-Same($expected, $actual, [string]$scenario) {
    $script:assertions++
    if (![object]::ReferenceEquals($expected, $actual)) { throw "${scenario}: reference changed" }
}

$previousGame = [Verse.Current]::Game
$previousMode = [Verse.Scribe]::mode
try {
    $game = New-Uninitialized ([Verse.Game])
    $game.components = New-Object 'System.Collections.Generic.List[Verse.GameComponent]'
    $component = New-Object ColonyPromises.GameComponent_Promises -ArgumentList (, $game)
    $game.components.Add($component)
    [Verse.Current]::Game = $game
    [Verse.Scribe]::mode = [Verse.LoadSaveMode]::Inactive
    $active = $component.GetType().GetField('active', $privateInstance).GetValue($component)

    function Add-Active([ColonyPromises.PromiseKind]$kind, $thing, [int]$progress) {
        $pawn = New-Uninitialized ([Verse.Pawn])
        [Verse.Thing].GetField('thingIDNumber').SetValue($pawn, $script:nextPawnId++)
        $state = $component.StateFor($pawn)
        $record = New-Object ColonyPromises.PromiseRecord
        $record.Kind = $kind
        $record.StableThing = $thing
        $record.ProgressTicks = $progress
        $record.PreviouslySatisfied = $true
        $record.LastLeisureCreditTick = 5000
        $state.Active = $record
        $active.Add($pawn, $state)
        return $state
    }

    $bed = New-Uninitialized ([RimWorld.Building_Bed])
    $weapon = New-Uninitialized ([Verse.ThingWithComps])
    $apparel = New-Uninitialized ([RimWorld.Apparel])
    $otherBed = New-Uninitialized ([RimWorld.Building_Bed])
    $room = New-Uninitialized ([Verse.Room])
    $leisure = Add-Active ([ColonyPromises.PromiseKind]::Leisure) $null 2500
    $bedroom = Add-Active ([ColonyPromises.PromiseKind]::Bedroom) $bed 12000
    $bedroom.Active.ObservedRoom = $room
    $equipment = Add-Active ([ColonyPromises.PromiseKind]::Equipment) $weapon 15000
    $clothing = Add-Active ([ColonyPromises.PromiseKind]::Equipment) $apparel 18000
    $otherBedroom = Add-Active ([ColonyPromises.PromiseKind]::Bedroom) $otherBed 21000

    # Regression: a different pawn has no bed. Execute the actual Prefix followed
    # by the game's no-op UnclaimBed; no Harmony installation or map is required.
    $bedlessPawn = New-Uninitialized ([Verse.Pawn])
    $ownership = New-Object RimWorld.Pawn_Ownership -ArgumentList $bedlessPawn
    [Verse.Pawn].GetField('ownership').SetValue($bedlessPawn, $ownership)
    Assert-Same $null $ownership.OwnedBed 'Regression fixture has no assigned bed'
    $prefix = $modAssembly.GetType('ColonyPromises.BedUnclaimedPatch').GetMethod('Prefix', $privateStatic)
    $prefix.Invoke($null, @($ownership.PSObject.BaseObject)) | Out-Null
    Assert-Equal $false $ownership.UnclaimBed() 'Bedless UnclaimBed remains a no-op'
    Assert-Equal 2500 $leisure.Active.ProgressTicks 'Another colonist keeps accumulated recreation'
    Assert-Equal 5000 $leisure.Active.LastLeisureCreditTick 'Recreation credit timestamp is preserved'
    Assert-Equal 12000 $bedroom.Active.ProgressTicks 'Null invalidation leaves bedroom hold unchanged'
    Assert-Equal 15000 $equipment.Active.ProgressTicks 'Null invalidation leaves weapon hold unchanged'
    Assert-Equal 18000 $clothing.Active.ProgressTicks 'Null invalidation leaves apparel hold unchanged'
    Assert-Equal 21000 $otherBedroom.Active.ProgressTicks 'Null invalidation leaves another bed unchanged'

    # A real tracked bed must still reset only its own colonist's hold.
    $component.InvalidateThing($bed)
    Assert-Equal 0 $bedroom.Active.ProgressTicks 'Tracked bed resets bedroom hold'
    Assert-Equal $false $bedroom.Active.PreviouslySatisfied 'Tracked bed clears continuity flag'
    Assert-Same $null $bedroom.Active.StableThing 'Tracked bed clears item reference'
    Assert-Same $null $bedroom.Active.ObservedRoom 'Tracked bed clears room cache'
    Assert-Equal 2500 $leisure.Active.ProgressTicks 'Real bed invalidation preserves recreation'
    Assert-Equal 15000 $equipment.Active.ProgressTicks 'Real bed invalidation preserves weapon hold'
    Assert-Equal 21000 $otherBedroom.Active.ProgressTicks 'Real bed invalidation preserves another bedroom'

    $component.InvalidateThing($weapon)
    Assert-Equal 0 $equipment.Active.ProgressTicks 'Tracked weapon resets equipment hold'
    Assert-Same $null $equipment.Active.StableThing 'Tracked weapon clears item reference'
    Assert-Equal 18000 $clothing.Active.ProgressTicks 'Weapon invalidation preserves apparel hold'
    Assert-Equal 21000 $otherBedroom.Active.ProgressTicks 'Weapon invalidation preserves another bedroom'
    Assert-Equal 2500 $leisure.Active.ProgressTicks 'Weapon invalidation preserves recreation'

    $component.InvalidateThing($apparel)
    Assert-Equal 0 $clothing.Active.ProgressTicks 'Tracked apparel resets equipment hold'
    Assert-Equal 2500 $leisure.Active.ProgressTicks 'Apparel invalidation preserves recreation'
    Assert-Equal 21000 $otherBedroom.Active.ProgressTicks 'Apparel invalidation preserves another bedroom'

    $component.InvalidateThing((New-Uninitialized ([Verse.ThingWithComps])))
    Assert-Equal 21000 $otherBedroom.Active.ProgressTicks 'Unrelated item leaves another hold unchanged'
    Assert-Same $otherBed $otherBedroom.Active.StableThing 'Unrelated item preserves tracked reference'

    # Cumulative promises are excluded even if an unexpected item reference exists.
    $leisure.Active.StableThing = $otherBed
    $component.InvalidateThing($otherBed)
    Assert-Equal 2500 $leisure.Active.ProgressTicks 'Non-continuous promise ignores matching item events'
    Assert-Equal 0 $otherBedroom.Active.ProgressTicks 'Same event still resets the matching continuous hold'

    $otherBedroom.Active.ProgressTicks = 21000
    $otherBedroom.Active.StableThing = $otherBed
    [Verse.Scribe]::mode = [Verse.LoadSaveMode]::LoadingVars
    $component.InvalidateThing($otherBed)
    Assert-Equal 21000 $otherBedroom.Active.ProgressTicks 'Scribe loading guard remains effective'
}
finally {
    [Verse.Current]::Game = $previousGame
    [Verse.Scribe]::mode = $previousMode
}

Write-Output "Passed $script:assertions invalidation checks against the compiled mod and game assemblies. No in-game test was run."

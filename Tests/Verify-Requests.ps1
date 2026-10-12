param([string]$RimWorldDir = 'C:\Games\Steam\steamapps\common\RimWorld')
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
$managed = Join-Path $RimWorldDir 'RimWorldWin64_Data\Managed'
$gameDll = Join-Path $managed 'Assembly-CSharp.dll'
$unityDll = Join-Path $managed 'UnityEngine.CoreModule.dll'
$modDll = Join-Path $modRoot '1.6\Assemblies\ColonyPromises.dll'
[Reflection.Assembly]::LoadFrom($unityDll) | Out-Null
[Reflection.Assembly]::LoadFrom($gameDll) | Out-Null
[Reflection.Assembly]::LoadFrom($modDll) | Out-Null
$fixture = @'
using System;
using System.IO;
using ColonyPromises;
using Verse;

public static class RequestChecks
{
    sealed class ManagedLog : UnityEngine.ILogHandler
    {
        public void LogFormat(UnityEngine.LogType type, UnityEngine.Object context, string format, params object[] args)
        { throw new Exception(string.Format(format, args)); }
        public void LogException(Exception exception, UnityEngine.Object context) { throw exception; }
    }
    static int checks;
    static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }
    static ColonistRequest Request(PromiseKind kind, int magnitude)
    {
        return RequestGenerator.Offer(new RequestCandidate {
            Terms = new PromiseRecord { Kind = kind, RoomStage = 4, RequiredTicks = kind == PromiseKind.Leisure ? 10000 : 60000 },
            Magnitude = magnitude, ReasonKey = "CP_ReasonLeisure"
        }, 100);
    }
    public static int Run(string temporaryFile)
    {
        UnityEngine.Debug.unityLogger.logHandler = new ManagedLog();
        var state = new ColonistPromises();
        state.RecentSuccesses.Add(new SuccessStamp { Kind = PromiseKind.Leisure, Tick = 0 });
        state.Deficits.Add(new DeficitObservation { Kind = PromiseKind.Leisure, Ticks = 120000 });
        state.Request = Request(PromiseKind.Leisure, 0);
        var closed = RequestLifecycle.Close(state, PromiseOutcome.Declined, 500);
        Check(state.Trust == 49 && closed.AppliedTrust == -1 && closed.AppliedMood == -1, "Declining records its small personal penalty");
        Check(state.Active == null && state.Request == null, "Declining does not create a promise");
        Check(state.CooldownUntilTick == 180500, "Declining starts individual cooldown");
        Check(state.Deficits.Count == 0, "Declining discards old deficit");
        Check(state.RecentSuccesses.Count == 1, "Declining preserves repeat ledger");
        Check(RequestLifecycle.Close(state, PromiseOutcome.Declined, 600) == null, "Closure cannot apply twice");
        state.Request = Request(PromiseKind.Leisure, 0);
        closed = RequestLifecycle.Close(state, PromiseOutcome.Expired, 700);
        Check(state.Trust == 49 && closed.AppliedMood == 0, "Expiry applies no additional penalty");
        state.Request = Request(PromiseKind.Leisure, 0);
        closed = RequestLifecycle.Close(state, PromiseOutcome.SatisfiedUnaccepted, 800);
        Check(closed.AppliedTrust == 0 && state.RecentSuccesses.Count == 1, "Unaccepted fulfillment earns nothing");

        var leisure = Request(PromiseKind.Leisure, 0);
        state.Request = leisure;
        RequestLifecycle.Accept(state, leisure, 1000);
        var active = state.Active;
        Check(active.FrozenConsequences && active.SuccessMood == 1.5f, "Repeat reduction is frozen on acceptance");
        Check(active.RequiredTicks == 10000 && active.DeadlineTick == 181000, "Acceptance preserves target and sets relative deadline");
        Check(state.Request == null && state.Deficits.Count == 0, "Acceptance clears request and old observations");
        float savedMood = active.SuccessMood;
        int savedPenalty = active.BrokenMood;
        state.Trust = 100;
        state.RecentSuccesses.Clear();
        var room = Request(PromiseKind.Bedroom, 1);
        PromiseConsequences.Snapshot(state, room, 2000);
        Check(room.Terms.SuccessMood == 7 && room.Terms.SuccessMood != savedMood, "Room and recreation have different rewards");
        Check(active.SuccessMood == savedMood && active.BrokenMood == savedPenalty && active.DeadlineTick == 181000, "Later context does not rewrite accepted obligations");
        room.Terms = active;
        PromiseConsequences.Snapshot(state, room, 3000);
        Check(active.SuccessMood == savedMood && active.BrokenMood == savedPenalty, "Accepted consequence snapshot cannot be recalculated");

        // Deserialize an old record through the real Scribe implementation.
        File.WriteAllText(temporaryFile, "<save><record><kind>Leisure</kind><createdTick>100</createdTick><deadlineTick>900000</deadlineTick><requiredTicks>2500</requiredTicks><progressTicks>1234</progressTicks><lastCheckTick>500</lastCheckTick><lastLeisureCreditTick>490</lastLeisureCreditTick></record></save>");
        PromiseRecord legacy = null;
        try {
            Scribe.loader.InitLoading(temporaryFile);
            Scribe.EnterNode("record"); legacy = new PromiseRecord(); Scribe.loader.curParent = legacy; legacy.ExposeData(); Scribe.ExitNode();
            // Engine finalization is deliberately excluded from this field-loading fixture.
        } finally { Scribe.loader.ForceStop(); }
        Check(legacy != null && legacy.Kind == PromiseKind.Leisure, "Old kind loaded");
        Check(legacy.ProgressTicks == 1234 && legacy.RequiredTicks == 2500, "Old progress and one-hour target retained");
        Check(legacy.CreatedTick == 100 && legacy.DeadlineTick == 900000 && legacy.LastLeisureCreditTick == 490, "Old deadline and clocks retained");
        Check(!legacy.FrozenConsequences && legacy.MinimumHitPointsFraction == 0, "Missing new fields select legacy behavior");

        // Round-trip record values with real Scribe, excluding engine/reference finalization.
        var original = active;
        try {
            Scribe.saver.InitSaving(temporaryFile, "save");
            active.ExposeData();
            Scribe.saver.FinalizeSaving();
            active = new PromiseRecord();
            Scribe.loader.InitLoading(temporaryFile);
            Scribe.loader.curParent = active;
            active.ExposeData();
        } finally { Scribe.saver.ForceStop(); Scribe.loader.ForceStop(); }
        Check(active.FrozenConsequences && active.SuccessMood == savedMood && active.BrokenMood == savedPenalty, "Frozen consequences round-trip");
        Check(active.RequiredTicks == original.RequiredTicks && active.DeadlineTick == original.DeadlineTick, "Accepted target and deadline round-trip");
        Check(active.SuccessDays == 5 && active.CooldownDays == 5, "Consequence durations round-trip");
        var oldState = new ColonistPromises();
        Check(oldState.Request == null && oldState.Deficits.Count == 0 && oldState.Trust == 50, "New state fields have safe defaults");
        File.WriteAllText(temporaryFile, "<save><trust>67</trust><cooldownUntilTick>123000</cooldownUntilTick></save>");
        try {
            Scribe.loader.InitLoading(temporaryFile);
            Scribe.loader.curParent = oldState;
            oldState.ExposeData();
            Scribe.mode = LoadSaveMode.PostLoadInit;
            oldState.ExposeData();
        } finally { Scribe.loader.ForceStop(); }
        Check(oldState.Trust == 67 && oldState.CooldownUntilTick == 123000, "Old state trust and cooldown retained through field loading");
        Check(oldState.Request == null && oldState.Deficits.Count == 0 && oldState.History.Count == 0 && oldState.RecentSuccesses.Count == 0, "Missing collections normalize safely");

        // Apply actual compiled consequences without a mood tracker, not a simulated formula.
        var pawn = (Pawn)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Pawn));
        var fresh = new ColonistPromises { Pawn = pawn };
        PromiseConsequences.Apply(fresh, new PromiseRecord(), PromiseOutcome.Declined, 5000);
        PromiseConsequences.Apply(fresh, new PromiseRecord(), PromiseOutcome.Expired, 5000);
        Check(fresh.Trust == 50 && fresh.CooldownUntilTick == 0 && fresh.RecentSuccesses.Count == 0, "Consequence applicator also ignores unaccepted outcomes");
        var request = Request(PromiseKind.Bedroom, 1);
        fresh.Request = request;
        RequestLifecycle.Accept(fresh, request, 1000);
        PromiseConsequences.Apply(fresh, fresh.Active, PromiseOutcome.Fulfilled, 5000);
        Check(fresh.Trust == 56 && fresh.Active.AppliedMood == 7 && fresh.Active.AppliedTrust == 6, "Actual new success uses the saved reward");
        Check(fresh.CooldownUntilTick == 305000 && fresh.RecentSuccesses.Count == 1, "New success starts cooldown and records repetition");
        fresh = new ColonistPromises { Pawn = pawn };
        request = Request(PromiseKind.Leisure, 0);
        fresh.Request = request;
        RequestLifecycle.Accept(fresh, request, 1000);
        PromiseConsequences.Apply(fresh, fresh.Active, PromiseOutcome.Broken, 5000);
        Check(fresh.Active.AppliedMood == -7 && fresh.Trust == 42, "Actual failure uses acceptance-time trust and scale");
        fresh = new ColonistPromises { Pawn = pawn };
        request = Request(PromiseKind.Leisure, 0);
        fresh.Request = request;
        RequestLifecycle.Accept(fresh, request, 1000);
        PromiseConsequences.Apply(fresh, fresh.Active, PromiseOutcome.Withdrawn, 5000);
        Check(fresh.Active.AppliedMood == -2 && fresh.Trust == 47, "Actual withdrawal is milder");
        fresh = new ColonistPromises { Pawn = pawn };
        PromiseConsequences.Apply(fresh, legacy, PromiseOutcome.Fulfilled, 5000);
        Check(legacy.AppliedMood == 4 && fresh.Trust == 55 && fresh.CooldownUntilTick == 185000, "Migrated old promise retains previous reward and cooldown");
        Check(new LeisureCondition().ValidParameters(legacy), "Old one-hour promise still validates");
        Check((int)PromiseOutcome.Fulfilled == 0 && (int)PromiseOutcome.Broken == 1 && (int)PromiseOutcome.Withdrawn == 2 && (int)PromiseOutcome.Closed == 3, "Old outcome enum values remain stable");
        var observation = new DeficitObservation { Kind = PromiseKind.Leisure, Signature = "joy", Ticks = 50000, SevereTicks = 25000, LastTick = 80000 };
        try {
            Scribe.saver.InitSaving(temporaryFile, "save");
            observation.ExposeData();
            Scribe.saver.FinalizeSaving();
            observation = new DeficitObservation();
            Scribe.loader.InitLoading(temporaryFile);
            Scribe.loader.curParent = observation;
            observation.ExposeData();
        } finally { Scribe.saver.ForceStop(); Scribe.loader.ForceStop(); }
        Check(observation.Ticks == 50000 && observation.SevereTicks == 25000 && observation.LastTick == 80000 && observation.Signature == "joy", "Deficit clocks and signature round-trip");
        Check(RequestRules.ObserveTicks(observation.Ticks, observation.LastTick, 80000, true) == 50000, "Reloaded deficit gains no same-tick progress");
        request = Request(PromiseKind.Bedroom, 1);
        request.Terms = null; // Nested terms were covered separately; this checks request metadata.
        try {
            Scribe.saver.InitSaving(temporaryFile, "save");
            request.ExposeData();
            Scribe.saver.FinalizeSaving();
            request = new ColonistRequest();
            Scribe.loader.InitLoading(temporaryFile);
            Scribe.loader.curParent = request;
            request.ExposeData();
        } finally { Scribe.saver.ForceStop(); Scribe.loader.ForceStop(); }
        Check(request.OfferedTick == 100 && request.ExpiryTick == 120100 && request.DeadlineDays == 7 && request.Magnitude == 1, "Request timing and significance round-trip");
        Check(request.ReasonKey == "CP_ReasonLeisure", "Request reason remains a localization key");
        return checks;
    }
}
'@
Add-Type -TypeDefinition $fixture -ReferencedAssemblies @($gameDll, $unityDll, $modDll, (Join-Path $managed 'netstandard.dll'))
$temporaryFile = Join-Path $PSScriptRoot 'request-check.tmp.xml'
try {
    $count = [RequestChecks]::Run($temporaryFile)
    Write-Output "Passed $count compiled request and isolated Scribe field checks. Live game behavior remains unverified."
} catch {
    Write-Output $_.Exception.ToString()
    throw
} finally {
    if (Test-Path -LiteralPath $temporaryFile) { Remove-Item -LiteralPath $temporaryFile }
}

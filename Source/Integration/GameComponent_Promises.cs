using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ColonyPromises
{
    public sealed partial class GameComponent_Promises : GameComponent
    {
        private List<ColonistPromises> colonists = new List<ColonistPromises>();
        private readonly Dictionary<Pawn, ColonistPromises> byPawn = new Dictionary<Pawn, ColonistPromises>();
        private readonly Dictionary<Pawn, ColonistPromises> active = new Dictionary<Pawn, ColonistPromises>();
        private readonly Dictionary<Pawn, ColonistPromises> open = new Dictionary<Pawn, ColonistPromises>();
        public List<ColonistPromises> OpenRequests(int now) => open.Values.Where(s => RequestMaintenance.Visible(s, now))
            .OrderBy(s => s.Request.OfferedTick).ThenBy(s => s.Pawn.thingIDNumber).ToList();
        private int nextCheck;
        private int nextObservation;
        private int nextOffer;
        private int lastObservation = -1;
        public int LastObservation => lastObservation;
        public int NextObservation => nextObservation;
        public int NextOffer => nextOffer;
        public ColonistPromises PeekState(Pawn pawn) => byPawn.TryGetValue(pawn, out var state)
            ? state : new ColonistPromises { Pawn = pawn };
        public static GameComponent_Promises Current => Verse.Current.Game?.GetComponent<GameComponent_Promises>();
        public GameComponent_Promises(Game game) { }

        public ColonistPromises StateFor(Pawn pawn)
        {
            if (byPawn.TryGetValue(pawn, out var state)) return state;
            state = new ColonistPromises { Pawn = pawn };
            colonists.Add(state);
            byPawn.Add(pawn, state);
            return state;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref colonists, "colonyPromises", LookMode.Deep);
            Scribe_Values.Look(ref nextObservation, "nextRequestObservation");
            Scribe_Values.Look(ref nextOffer, "nextRequestOffer");
            Scribe_Values.Look(ref lastObservation, "lastRequestObservation", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) colonists = colonists ?? new List<ColonistPromises>();
        }

        public override void FinalizeInit()
        {
            byPawn.Clear();
            active.Clear();
            open.Clear();
            foreach (var state in colonists.Where(s => s?.Pawn != null))
            {
                byPawn[state.Pawn] = state;
                if (state.Active == null)
                {
                    if (state.Request?.Terms != null)
                    {
                        open[state.Pawn] = state;
                        RestoreHold(state.Pawn, state.Request.Terms);
                    }
                    continue;
                }
                state.Request = null;
                active[state.Pawn] = state;
                RestoreHold(state.Pawn, state.Active);
            }
            foreach (var state in colonists.Where(s => s != null && s.Pawn == null && s.Active != null).ToList())
                Resolve(state, PromiseOutcome.Closed, Find.TickManager.TicksGame);
            nextCheck = Find.TickManager.TicksGame;
            if (nextOffer == 0) nextOffer = nextCheck + RequestRules.OfferSpacing;
        }

        private static void RestoreHold(Pawn pawn, PromiseRecord record)
        {
            var condition = PromiseConditions.Get(record.Kind);
            if (condition?.UsesContinuousHold != true) return;
            var observation = condition.Observe(pawn, record);
            if (!observation.Satisfied || observation.Thing != record.StableThing) record.ResetHold();
            else record.ObservedRoom = observation.Room;
        }

        public string AcceptanceError(Pawn pawn, ColonistRequest request)
        {
            if (!PromiseConditions.Eligible(pawn)) return "CP_Ineligible".Translate();
            var state = StateFor(pawn);
            if (state.Active != null) return "CP_AlreadyActive".Translate();
            int now = Find.TickManager.TicksGame;
            if (request == null || state.Request != request || now >= request.ExpiryTick) return "CP_RequestUnavailable".Translate();
            if (now < state.CooldownUntilTick) return "CP_Cooldown".Translate(PromiseText.Number((state.CooldownUntilTick - now) / (float)PromiseBalance.TicksPerDay));
            if (!RequestGenerator.Relevant(pawn, request)) return "CP_RequestOutdated".Translate();
            return null;
        }

        public bool TryAccept(Pawn pawn, ColonistRequest request)
        {
            string error = AcceptanceError(pawn, request);
            if (error != null)
            {
                Messages.Message(error, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            int now = Find.TickManager.TicksGame;
            var state = StateFor(pawn);
            RequestLifecycle.Accept(state, request, now);
            open.Remove(pawn);
            active[pawn] = state;
            nextCheck = Math.Min(nextCheck, now);
            return true;
        }

        public void Decline(Pawn pawn, ColonistRequest request)
        {
            var state = StateFor(pawn);
            if (state.Request != request || request == null) return;
            CloseRequest(state, Find.TickManager.TicksGame >= request.ExpiryTick ? PromiseOutcome.Expired : PromiseOutcome.Declined,
                Find.TickManager.TicksGame);
        }

        public void CloseRequest(ColonistPromises state, PromiseOutcome outcome, int now, bool recoverTrust = false)
        {
            var record = RequestLifecycle.Close(state, outcome, now, recoverTrust);
            if (record == null) return;
            if (state.Pawn != null) open.Remove(state.Pawn);
            AddHistory(state, record);
            NotifyResult(state, record);
        }

        public void Withdraw(Pawn pawn, PromiseRecord expected)
        {
            if (!active.TryGetValue(pawn, out var state) || state.Active != expected) return;
            int now = Find.TickManager.TicksGame;
            Check(state, now);
            if (state.Active == expected && now < expected.DeadlineTick) Resolve(state, PromiseOutcome.Withdrawn, now);
        }

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now >= nextObservation)
            {
                ObserveRequests(now);
                nextObservation = now + RequestRules.ObservationInterval;
            }
            if (now < nextCheck) return;
            foreach (var state in active.Values.ToList()) Check(state, now);
            foreach (var state in open.Values.ToList()) CheckOpenRequest(state, now);
            nextCheck = now + PromiseBalance.CheckInterval;
            foreach (var state in active.Values) nextCheck = Math.Min(nextCheck, state.Active.DeadlineTick);
            foreach (var state in open.Values) nextCheck = Math.Min(nextCheck, state.Request.ExpiryTick);
        }

        private void Check(ColonistPromises state, int now)
        {
            var r = state.Active;
            if (r == null) return;
            var pawn = state.Pawn;
            if (pawn == null || pawn.Dead || pawn.Destroyed || pawn.Faction != Faction.OfPlayer)
            {
                Resolve(state, PromiseOutcome.Closed, now);
                return;
            }
            var condition = PromiseConditions.Get(r.Kind);
            PromiseProgress.Observe(r, condition, pawn, now);
            // Fulfillment always wins over expiry when the verified progress meets the target.
            if (r.ProgressTicks >= r.RequiredTicks && r.RequiredTicks > 0) Resolve(state, PromiseOutcome.Fulfilled, now);
            else if (now >= r.DeadlineTick) Resolve(state, PromiseOutcome.Broken, now);
        }

        public void CreditLeisure(Pawn pawn, int interval)
        {
            int tick = Find.TickManager.TicksGame;
            PromiseRecord r;
            if (active.TryGetValue(pawn, out var state)) r = state.Active;
            else if (open.TryGetValue(pawn, out state) && TrustRecovery.Available(state, state.Request, tick)
                && tick <= state.Request.ExpiryTick) r = state.Request.Terms;
            else return;
            if (r.Kind != PromiseKind.Leisure || !LeisureCondition.CanCredit(pawn)) return;
            int now = Find.TickManager.TicksGame;
            if (now > r.DeadlineTick) return;
            r.ProgressTicks = Math.Min(r.RequiredTicks, r.ProgressTicks + ProgressMath.LeisureCredit(now, interval, r.CreatedTick, r.DeadlineTick, r.LastLeisureCreditTick));
            r.LastLeisureCreditTick = Math.Max(r.LastLeisureCreditTick, now);
        }

        public void InvalidateThing(Thing thing)
        {
            if (thing == null || Scribe.mode != LoadSaveMode.Inactive) return;
            foreach (var state in active.Values.Concat(open.Values))
            {
                var record = state.Active ?? state.Request?.Terms;
                if (record?.StableThing == thing && PromiseConditions.Get(record.Kind)?.UsesContinuousHold == true)
                    record.ResetHold();
            }
        }

        public void InvalidatePawn(Pawn pawn)
        {
            if (Scribe.mode != LoadSaveMode.Inactive) return;
            if (!active.TryGetValue(pawn, out var state)) open.TryGetValue(pawn, out state);
            var record = state?.Active ?? state?.Request?.Terms;
            if (record != null && PromiseConditions.Get(record.Kind)?.UsesContinuousHold == true) record.ResetHold();
        }

        public void CheckLifecycle(Pawn pawn)
        {
            if (Scribe.mode == LoadSaveMode.Inactive && byPawn.TryGetValue(pawn, out var saved)
                && saved.Request != null && (pawn.Dead || pawn.Destroyed || pawn.Faction != Faction.OfPlayer))
                CloseRequest(saved, PromiseOutcome.Closed, Find.TickManager.TicksGame);
            if (Scribe.mode == LoadSaveMode.Inactive && active.TryGetValue(pawn, out var state)
                && (pawn.Dead || pawn.Destroyed || pawn.Faction != Faction.OfPlayer))
                Resolve(state, PromiseOutcome.Closed, Find.TickManager.TicksGame);
        }

        public void RoomChanged(Room room, bool shapeChanged)
        {
            if (Scribe.mode != LoadSaveMode.Inactive) return;
            foreach (var state in active.Values.Concat(open.Values))
            {
                var r = state.Active ?? state.Request?.Terms;
                if (r == null || r.Kind != PromiseKind.Bedroom || r.ObservedRoom != room) continue;
                if (shapeChanged || !PromiseConditions.Get(r.Kind).Observe(state.Pawn, r).Satisfied) r.ResetHold();
            }
        }

        private void Resolve(ColonistPromises state, PromiseOutcome outcome, int now)
        {
            var r = state.Active;
            if (r == null) return;
            // Commit the terminal state before applying external effects or notifications.
            state.Active = null;
            if (state.Pawn != null) active.Remove(state.Pawn);
            r.Outcome = outcome;
            r.RecordResolution(now);
            r.StableThing = null;
            r.ObservedRoom = null;
            state.RecentSuccesses.RemoveAll(s => now - s.Tick > PromiseBalance.RepeatWindowDays * PromiseBalance.TicksPerDay);
            PromiseConsequences.Apply(state, r, outcome, now);
            RequestGenerator.ResetObservations(state);
            AddHistory(state, r);
            NotifyResult(state, r);
        }

        private static void AddHistory(ColonistPromises state, PromiseRecord r)
        {
            state.History.Insert(0, r);
            if (state.History.Count > PromiseBalance.HistoryLimit) state.History.RemoveRange(PromiseBalance.HistoryLimit, state.History.Count - PromiseBalance.HistoryLimit);
        }

        private static void NotifyResult(ColonistPromises state, PromiseRecord r)
        {
            Messages.Message("CP_Result".Translate(state.Pawn?.LabelShort ?? "CP_MissingColonist".Translate().ToString(),
                ("CP_Type_" + r.Kind).Translate(), ("CP_Outcome_" + r.Outcome).Translate()),
                r.Outcome == PromiseOutcome.Fulfilled ? MessageTypeDefOf.PositiveEvent
                    : r.Outcome == PromiseOutcome.Broken || r.Outcome == PromiseOutcome.Withdrawn ? MessageTypeDefOf.NegativeEvent : MessageTypeDefOf.NeutralEvent);
        }
    }
}

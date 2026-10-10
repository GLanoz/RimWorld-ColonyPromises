using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ColonyPromises
{
    public sealed class GameComponent_Promises : GameComponent
    {
        private List<ColonistPromises> colonists = new List<ColonistPromises>();
        private readonly Dictionary<Pawn, ColonistPromises> byPawn = new Dictionary<Pawn, ColonistPromises>();
        private readonly Dictionary<Pawn, ColonistPromises> active = new Dictionary<Pawn, ColonistPromises>();
        private int nextCheck;
        private int nextObservation;
        private int nextOffer;
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
            if (Scribe.mode == LoadSaveMode.PostLoadInit) colonists = colonists ?? new List<ColonistPromises>();
        }

        public override void FinalizeInit()
        {
            byPawn.Clear();
            active.Clear();
            foreach (var state in colonists.Where(s => s?.Pawn != null))
            {
                byPawn[state.Pawn] = state;
                if (state.Active == null) continue;
                state.Request = null;
                active[state.Pawn] = state;
                var record = state.Active;
                var condition = PromiseConditions.Get(record.Kind);
                if (condition?.UsesContinuousHold == true)
                {
                    var observation = condition.Observe(state.Pawn, record);
                    if (!observation.Satisfied || observation.Thing != record.StableThing) record.ResetHold();
                    else record.ObservedRoom = observation.Room;
                }
            }
            foreach (var state in colonists.Where(s => s != null && s.Pawn == null && s.Active != null).ToList())
                Resolve(state, PromiseOutcome.Closed, Find.TickManager.TicksGame);
            nextCheck = Find.TickManager.TicksGame;
            if (nextOffer == 0) nextOffer = nextCheck + RequestRules.OfferSpacing;
        }

        public string AcceptanceError(Pawn pawn, ColonistRequest request)
        {
            if (!PromiseConditions.Eligible(pawn)) return "CP_Ineligible".Translate();
            var state = StateFor(pawn);
            if (state.Active != null) return "CP_AlreadyActive".Translate();
            int now = Find.TickManager.TicksGame;
            if (request == null || state.Request != request || now >= request.ExpiryTick) return "CP_RequestUnavailable".Translate();
            if (now < state.CooldownUntilTick) return "CP_Cooldown".Translate((state.CooldownUntilTick - now) / (float)PromiseBalance.TicksPerDay);
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

        public void CloseRequest(ColonistPromises state, PromiseOutcome outcome, int now)
        {
            var record = RequestLifecycle.Close(state, outcome, now);
            if (record == null) return;
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
            nextCheck = now + PromiseBalance.CheckInterval;
            foreach (var state in active.Values) nextCheck = Math.Min(nextCheck, state.Active.DeadlineTick);
        }

        private void ObserveRequests(int now)
        {
            foreach (var pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists.Where(PromiseConditions.Eligible))
                StateFor(pawn);
            var ready = new List<KeyValuePair<ColonistPromises, RequestCandidate>>();
            foreach (var state in colonists)
            {
                if (state.Pawn == null || state.Pawn.Dead || state.Pawn.Destroyed || state.Pawn.Faction != Faction.OfPlayer)
                {
                    if (state.Request != null) CloseRequest(state, PromiseOutcome.Closed, now);
                    RequestGenerator.ResetObservations(state);
                    continue;
                }
                if (state.Request != null)
                {
                    var r = state.Request;
                    bool observable = PromiseConditions.Observable(state.Pawn) && !state.Pawn.Downed && !state.Pawn.Drafted && !state.Pawn.InMentalState;
                    // Fulfillment of an unanswered request grants nothing, including at its expiry.
                    if (observable && !RequestGenerator.Relevant(state.Pawn, r))
                    {
                        bool satisfied = r.Terms.Kind == PromiseKind.Leisure
                            ? state.Pawn.needs?.joy != null && state.Pawn.needs.joy.CurLevel >= RequestRules.LowJoy
                            : PromiseConditions.Get(r.Terms.Kind)?.Observe(state.Pawn, r.Terms).Satisfied == true;
                        CloseRequest(state, satisfied ? PromiseOutcome.SatisfiedUnaccepted : PromiseOutcome.Obsolete, now);
                    }
                    else if (now >= r.ExpiryTick) CloseRequest(state, PromiseOutcome.Expired, now);
                    continue;
                }
                if (state.Active != null || now < state.CooldownUntilTick || !PromiseConditions.Eligible(state.Pawn))
                {
                    RequestGenerator.ResetObservations(state);
                    continue;
                }
                foreach (PromiseKind kind in Enum.GetValues(typeof(PromiseKind)))
                {
                    var candidate = RequestGenerator.Observe(state, kind, now);
                    if (candidate != null) ready.Add(new KeyValuePair<ColonistPromises, RequestCandidate>(state, candidate));
                }
            }
            if (now < nextOffer || ready.Count == 0) return;
            // Oldest eligible observation first; pawn identity and kind break ties deterministically.
            var chosen = ready.OrderByDescending(x => x.Key.Deficits.First(d => d.Kind == x.Value.Terms.Kind).Ticks)
                .ThenBy(x => x.Key.Pawn.thingIDNumber).ThenBy(x => x.Value.Terms.Kind).First();
            chosen.Key.Request = RequestGenerator.Offer(chosen.Value, now);
            RequestGenerator.ResetObservations(chosen.Key);
            nextOffer = now + RequestRules.OfferSpacing;
            Messages.Message("CP_NewRequest".Translate(chosen.Key.Pawn.LabelShort, ("CP_Type_" + chosen.Value.Terms.Kind).Translate()),
                MessageTypeDefOf.NeutralEvent);
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
            if (condition?.UsesContinuousHold == true)
            {
                var observation = condition.Observe(pawn, r);
                if (!observation.Satisfied) r.ResetHold();
                else
                {
                    bool same = r.PreviouslySatisfied && observation.Thing == r.StableThing
                        && (r.Kind != PromiseKind.Bedroom || observation.Room == r.ObservedRoom);
                    if (!same) r.ProgressTicks = 0;
                    else r.ProgressTicks += ProgressMath.VerifiedDelta(r.LastCheckTick, now, r.DeadlineTick);
                    r.StableThing = observation.Thing;
                    r.ObservedRoom = observation.Room;
                    r.PreviouslySatisfied = true;
                }
            }
            r.LastCheckTick = now;
            // Fulfillment always wins over expiry when the verified progress meets the target.
            if (r.ProgressTicks >= r.RequiredTicks && r.RequiredTicks > 0) Resolve(state, PromiseOutcome.Fulfilled, now);
            else if (now >= r.DeadlineTick) Resolve(state, PromiseOutcome.Broken, now);
        }

        public void CreditLeisure(Pawn pawn, int interval)
        {
            if (!active.TryGetValue(pawn, out var state) || state.Active.Kind != PromiseKind.Leisure || !LeisureCondition.CanCredit(pawn)) return;
            var r = state.Active;
            int now = Find.TickManager.TicksGame;
            if (now > r.DeadlineTick) return;
            r.ProgressTicks = Math.Min(r.RequiredTicks, r.ProgressTicks + ProgressMath.LeisureCredit(now, interval, r.CreatedTick, r.DeadlineTick, r.LastLeisureCreditTick));
            r.LastLeisureCreditTick = Math.Max(r.LastLeisureCreditTick, now);
        }

        public void InvalidateThing(Thing thing)
        {
            if (thing == null || Scribe.mode != LoadSaveMode.Inactive) return;
            foreach (var state in active.Values)
                if (state.Active.StableThing == thing && PromiseConditions.Get(state.Active.Kind)?.UsesContinuousHold == true)
                    state.Active.ResetHold();
        }

        public void InvalidatePawn(Pawn pawn)
        {
            if (Scribe.mode == LoadSaveMode.Inactive && active.TryGetValue(pawn, out var state)
                && PromiseConditions.Get(state.Active.Kind)?.UsesContinuousHold == true) state.Active.ResetHold();
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
            foreach (var state in active.Values)
            {
                var r = state.Active;
                if (r.Kind != PromiseKind.Bedroom || r.ObservedRoom != room) continue;
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
            r.ResolvedTick = now;
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

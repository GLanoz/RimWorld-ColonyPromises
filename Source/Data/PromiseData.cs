using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ColonyPromises
{
    public enum PromiseKind { Bedroom, Leisure, Equipment }
    public enum PromiseOutcome { Fulfilled, Broken, Withdrawn, Closed, Declined, Expired, SatisfiedUnaccepted, Obsolete }

    public sealed class PromiseRecord : IExposable
    {
        public string SharedId;
        public Pawn ParticipantA;
        public Pawn ParticipantB;
        public string HistoryPartnerLabel;
        public bool RequiresBarracks;
        public bool HousingBased;
        public bool RequiresConstructedBed;
        public bool IsShared => ParticipantA != null || ParticipantB != null || SharedId != null;
        public Pawn PartnerOf(Pawn pawn) => pawn == ParticipantA ? ParticipantB : ParticipantA;
        public PromiseKind Kind;
        public int CreatedTick;
        public int DeadlineTick;
        public int RequiredTicks;
        public int ProgressTicks;
        public int RoomStage;
        public ThingDef EquipmentDef;
        public QualityCategory MinimumQuality;
        public bool QualityOptional;
        public bool NeedBasedEquipment;
        public string EquipmentCause;
        public float CauseMood;
        public Thing ProblemThing;
        public bool RequireCoverage;
        public bool RequireMelee;
        public List<BedroomRequirement> BedroomRequirements;
        public bool AvoidHumanLeather;
        public int LastCheckTick = -1;
        public int LastLeisureCreditTick;
        public bool PreviouslySatisfied;
        public Thing StableThing;
        public Room ObservedRoom; // Runtime cache; room identity is rebuilt after loading.
        public PromiseOutcome Outcome;
        public int ResolvedTick;
        public float AppliedMood;
        public int AppliedTrust;
        public bool HasHistoryMetadata;
        public bool HasResolvedTick;
        public bool HasAppliedMood;
        public bool HasAppliedTrust;
        public float MinimumHitPointsFraction;
        public bool FrozenConsequences;
        public float SuccessMood;
        public int SuccessTrust;
        public int BrokenMood;
        public int BrokenTrust;
        public int WithdrawnMood;
        public int WithdrawnTrust;
        public int SuccessDays;
        public int BrokenDays;
        public int WithdrawnDays;
        public int CooldownDays;

        public PromiseRecord CopyForPreview() => (PromiseRecord)MemberwiseClone();

        public void RecordResolution(int tick)
        {
            ResolvedTick = tick;
            HasResolvedTick = true;
            HasHistoryMetadata = true;
        }

        public void RecordConsequences(float mood, int trust)
        {
            AppliedMood = mood;
            AppliedTrust = trust;
            HasAppliedMood = HasAppliedTrust = true;
            HasHistoryMetadata = true;
        }

        public void ResetHold()
        {
            ProgressTicks = 0;
            PreviouslySatisfied = false;
            StableThing = null;
            ObservedRoom = null;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref SharedId, "sharedId");
            Scribe_References.Look(ref ParticipantA, "participantA");
            Scribe_References.Look(ref ParticipantB, "participantB");
            Scribe_Values.Look(ref HistoryPartnerLabel, "historyPartnerLabel");
            Scribe_Values.Look(ref RequiresBarracks, "requiresBarracks");
            Scribe_Values.Look(ref HousingBased, "housingBased");
            Scribe_Values.Look(ref RequiresConstructedBed, "requiresConstructedBed");
            Scribe_Values.Look(ref Kind, "kind");
            Scribe_Values.Look(ref CreatedTick, "createdTick");
            Scribe_Values.Look(ref DeadlineTick, "deadlineTick");
            Scribe_Values.Look(ref RequiredTicks, "requiredTicks");
            Scribe_Values.Look(ref ProgressTicks, "progressTicks");
            Scribe_Values.Look(ref RoomStage, "roomStage");
            Scribe_Defs.Look(ref EquipmentDef, "equipmentDef");
            Scribe_Values.Look(ref MinimumQuality, "minimumQuality");
            Scribe_Values.Look(ref QualityOptional, "qualityOptional");
            Scribe_Values.Look(ref NeedBasedEquipment, "needBasedEquipment");
            Scribe_Values.Look(ref EquipmentCause, "equipmentCause");
            Scribe_Values.Look(ref CauseMood, "causeMood");
            Scribe_References.Look(ref ProblemThing, "problemThing");
            Scribe_Values.Look(ref RequireCoverage, "requireCoverage");
            Scribe_Values.Look(ref RequireMelee, "requireMelee");
            Scribe_Collections.Look(ref BedroomRequirements, "bedroomRequirements", LookMode.Deep);
            Scribe_Values.Look(ref AvoidHumanLeather, "avoidHumanLeather");
            Scribe_Values.Look(ref LastCheckTick, "lastCheckTick", -1);
            Scribe_Values.Look(ref LastLeisureCreditTick, "lastLeisureCreditTick");
            Scribe_Values.Look(ref PreviouslySatisfied, "previouslySatisfied");
            Scribe_References.Look(ref StableThing, "stableThing");
            Scribe_Values.Look(ref Outcome, "outcome");
            Scribe_Values.Look(ref ResolvedTick, "resolvedTick");
            Scribe_Values.Look(ref AppliedMood, "appliedMood");
            Scribe_Values.Look(ref AppliedTrust, "appliedTrust");
            Scribe_Values.Look(ref HasHistoryMetadata, "hasHistoryMetadata");
            Scribe_Values.Look(ref HasResolvedTick, "hasResolvedTick");
            Scribe_Values.Look(ref HasAppliedMood, "hasAppliedMood");
            Scribe_Values.Look(ref HasAppliedTrust, "hasAppliedTrust");
            if (Scribe.mode == LoadSaveMode.LoadingVars && !HasHistoryMetadata)
            {
                // Older saves omit default zero values. Infer only explicitly stored data.
                HasResolvedTick = Scribe.loader.curXmlParent["resolvedTick"] != null;
                HasAppliedMood = Scribe.loader.curXmlParent["appliedMood"] != null;
                HasAppliedTrust = Scribe.loader.curXmlParent["appliedTrust"] != null;
                HasHistoryMetadata = true;
            }
            Scribe_Values.Look(ref MinimumHitPointsFraction, "minimumHitPointsFraction");
            Scribe_Values.Look(ref FrozenConsequences, "frozenConsequences");
            Scribe_Values.Look(ref SuccessMood, "successMood");
            Scribe_Values.Look(ref SuccessTrust, "successTrust");
            Scribe_Values.Look(ref BrokenMood, "brokenMood");
            Scribe_Values.Look(ref BrokenTrust, "brokenTrust");
            Scribe_Values.Look(ref WithdrawnMood, "withdrawnMood");
            Scribe_Values.Look(ref WithdrawnTrust, "withdrawnTrust");
            Scribe_Values.Look(ref SuccessDays, "successDays");
            Scribe_Values.Look(ref BrokenDays, "brokenDays");
            Scribe_Values.Look(ref WithdrawnDays, "withdrawnDays");
            Scribe_Values.Look(ref CooldownDays, "cooldownDays");
        }
    }

    public sealed class ColonistPromises : IExposable
    {
        public Pawn Pawn;
        public string SharedId;
        public SharedBedroomCommitment Shared; // Runtime alias to the component's authoritative record.
        public int Trust = PromiseBalance.InitialTrust;
        public int CooldownUntilTick;
        public int LastGenerationCheck = -1;
        public int NextUnpromisedRecoveryTick;
        public List<SuccessStamp> RecentRecoveries = new List<SuccessStamp>();
        public PromiseRecord Active;
        public ColonistRequest Request;
        public List<DeficitObservation> Deficits = new List<DeficitObservation>();
        public List<HousingWitness> HousingWitnesses = new List<HousingWitness>();
        public List<PromiseRecord> History = new List<PromiseRecord>();
        // Separate reward ledger: trimming visible history must not restore full rewards.
        public List<SuccessStamp> RecentSuccesses = new List<SuccessStamp>();

        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_Values.Look(ref SharedId, "sharedId");
            Scribe_Values.Look(ref Trust, "trust", PromiseBalance.InitialTrust);
            Scribe_Values.Look(ref CooldownUntilTick, "cooldownUntilTick");
            Scribe_Values.Look(ref LastGenerationCheck, "lastGenerationCheck", -1);
            Scribe_Values.Look(ref NextUnpromisedRecoveryTick, "nextUnpromisedRecoveryTick");
            Scribe_Collections.Look(ref RecentRecoveries, "recentRecoveries", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.Saving && SharedId != null)
            {
                // Shared terms are serialized once by the component, not once per participant.
                PromiseRecord emptyActive = null;
                ColonistRequest emptyRequest = null;
                Scribe_Deep.Look(ref emptyActive, "active");
                Scribe_Deep.Look(ref emptyRequest, "request");
            }
            else
            {
                Scribe_Deep.Look(ref Active, "active");
                Scribe_Deep.Look(ref Request, "request");
            }
            Scribe_Collections.Look(ref Deficits, "deficits", LookMode.Deep);
            Scribe_Collections.Look(ref HousingWitnesses, "housingWitnesses", LookMode.Deep);
            Scribe_Collections.Look(ref History, "history", LookMode.Deep);
            Scribe_Collections.Look(ref RecentSuccesses, "recentSuccesses", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Trust = PromiseBalance.ClampTrust(Trust);
                History = History ?? new List<PromiseRecord>();
                RecentSuccesses = RecentSuccesses ?? new List<SuccessStamp>();
                Deficits = Deficits ?? new List<DeficitObservation>();
                HousingWitnesses = HousingWitnesses ?? new List<HousingWitness>();
                RecentRecoveries = RecentRecoveries ?? new List<SuccessStamp>();
            }
        }
    }

    public sealed class ColonistRequest : IExposable
    {
        public PromiseRecord Terms;
        public string ReasonKey;
        public List<HousingWitness> HousingReasons;
        public ExpectationDef Expectation;
        public bool Announced = true; // Missing old fields must not resend an announcement.
        public bool ReminderSent;
        public bool RecoveryEnabled; // Only newly generated requests opt in.
        public int OfferedTick;
        public int ExpiryTick;
        public int DeadlineDays;
        public int Magnitude;
        public void ExposeData()
        {
            Scribe_Deep.Look(ref Terms, "terms");
            Scribe_Values.Look(ref Announced, "announced", true);
            Scribe_Values.Look(ref ReminderSent, "reminderSent");
            Scribe_Values.Look(ref RecoveryEnabled, "recoveryEnabled");
            Scribe_Values.Look(ref ReasonKey, "reasonKey");
            Scribe_Collections.Look(ref HousingReasons, "housingReasons", LookMode.Deep);
            Scribe_Defs.Look(ref Expectation, "expectation");
            Scribe_Values.Look(ref OfferedTick, "offeredTick");
            Scribe_Values.Look(ref ExpiryTick, "expiryTick");
            Scribe_Values.Look(ref DeadlineDays, "deadlineDays");
            Scribe_Values.Look(ref Magnitude, "magnitude");
        }
    }

    public sealed class DeficitObservation : IExposable
    {
        public PromiseKind Kind;
        public string Signature;
        public int Ticks;
        public int SevereTicks;
        public bool ObservationPaused;
        public int LastTick = -1;
        public int StartTick = -1;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Kind, "kind");
            Scribe_Values.Look(ref Signature, "signature");
            Scribe_Values.Look(ref Ticks, "ticks");
            Scribe_Values.Look(ref SevereTicks, "severeTicks");
            Scribe_Values.Look(ref ObservationPaused, "observationPaused");
            Scribe_Values.Look(ref LastTick, "lastTick", -1);
            Scribe_Values.Look(ref StartTick, "startTick", -1);
        }
    }

    public sealed class SuccessStamp : IExposable
    {
        public PromiseKind Kind;
        public int Tick;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Kind, "kind");
            Scribe_Values.Look(ref Tick, "tick");
        }
    }
}

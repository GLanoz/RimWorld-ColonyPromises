using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using ColonyPromises;
using RimWorld;
using Verse;

public static class InvalidationChecks
{
    static int checks;
    static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    public static int Run()
    {
        var previousGame = Current.Game;
        var previousMode = Scribe.mode;
        try
        {
            var game = Empty<Game>();
            game.components = new List<GameComponent>();
            var component = new GameComponent_Promises(game);
            game.components.Add(component); Current.Game = game; Scribe.mode = LoadSaveMode.Inactive;
            var active = (Dictionary<Pawn, ColonistPromises>)typeof(GameComponent_Promises)
                .GetField("active", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(component);
            int nextPawn = 1;
            ColonistPromises Add(PromiseKind kind, Thing thing, int progress)
            {
                var pawn = Empty<Pawn>(); pawn.thingIDNumber = nextPawn++;
                var state = component.StateFor(pawn);
                state.Active = new PromiseRecord { Kind = kind, StableThing = thing, ProgressTicks = progress,
                    PreviouslySatisfied = true, LastLeisureCreditTick = 5000 };
                active.Add(pawn, state); return state;
            }
            var bed = Empty<Building_Bed>(); var weapon = Empty<ThingWithComps>();
            var apparel = Empty<Apparel>(); var otherBed = Empty<Building_Bed>();
            var leisure = Add(PromiseKind.Leisure, null, 2500);
            var bedroom = Add(PromiseKind.Bedroom, bed, 12000); bedroom.Active.ObservedRoom = Empty<Room>();
            var equipment = Add(PromiseKind.Equipment, weapon, 15000);
            var clothing = Add(PromiseKind.Equipment, apparel, 18000);
            var other = Add(PromiseKind.Bedroom, otherBed, 21000);
            var bedless = Empty<Pawn>(); bedless.ownership = new Pawn_Ownership(bedless);
            Check(bedless.ownership.OwnedBed == null, "Regression pawn has no bed");
            typeof(PromiseRecord).Assembly.GetType("ColonyPromises.BedUnclaimedPatch")
                .GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { bedless.ownership });
            Check(!bedless.ownership.UnclaimBed(), "Bedless UnclaimBed remains a no-op");
            Check(leisure.Active.ProgressTicks == 2500, "Another pawn retains accumulated recreation");
            Check(leisure.Active.LastLeisureCreditTick == 5000, "Recreation credit time is preserved");
            Check(bedroom.Active.ProgressTicks == 12000, "Null event preserves bedroom");
            Check(equipment.Active.ProgressTicks == 15000, "Null event preserves weapon");
            Check(clothing.Active.ProgressTicks == 18000, "Null event preserves apparel");
            Check(other.Active.ProgressTicks == 21000, "Null event preserves other bed");
            component.InvalidateThing(bed);
            Check(bedroom.Active.ProgressTicks == 0, "Tracked bed resets its hold");
            Check(!bedroom.Active.PreviouslySatisfied, "Tracked bed resets continuity");
            Check(bedroom.Active.StableThing == null, "Tracked bed clears reference");
            Check(bedroom.Active.ObservedRoom == null, "Tracked bed clears room cache");
            Check(leisure.Active.ProgressTicks == 2500, "Bed event preserves recreation");
            Check(equipment.Active.ProgressTicks == 15000, "Bed event preserves weapon");
            Check(other.Active.ProgressTicks == 21000, "Bed event preserves other bed");
            component.InvalidateThing(weapon);
            Check(equipment.Active.ProgressTicks == 0, "Weapon resets its hold");
            Check(equipment.Active.StableThing == null, "Weapon clears reference");
            Check(clothing.Active.ProgressTicks == 18000, "Weapon event preserves apparel");
            Check(other.Active.ProgressTicks == 21000, "Weapon event preserves other bed");
            Check(leisure.Active.ProgressTicks == 2500, "Weapon event preserves recreation");
            component.InvalidateThing(apparel);
            Check(clothing.Active.ProgressTicks == 0, "Apparel resets its hold");
            Check(leisure.Active.ProgressTicks == 2500, "Apparel event preserves recreation");
            Check(other.Active.ProgressTicks == 21000, "Apparel event preserves other bed");
            component.InvalidateThing(Empty<ThingWithComps>());
            Check(other.Active.ProgressTicks == 21000, "Unrelated item preserves hold");
            Check(ReferenceEquals(other.Active.StableThing, otherBed), "Unrelated item preserves reference");
            leisure.Active.StableThing = otherBed;
            component.InvalidateThing(otherBed);
            Check(leisure.Active.ProgressTicks == 2500, "Cumulative progress ignores matching item events");
            Check(other.Active.ProgressTicks == 0, "Matching continuous hold still resets");
            other.Active.ProgressTicks = 21000; other.Active.StableThing = otherBed;
            Scribe.mode = LoadSaveMode.LoadingVars; component.InvalidateThing(otherBed);
            Check(other.Active.ProgressTicks == 21000, "Loading guard preserves hold");
        }
        finally { Current.Game = previousGame; Scribe.mode = previousMode; }
        return checks;
    }
}

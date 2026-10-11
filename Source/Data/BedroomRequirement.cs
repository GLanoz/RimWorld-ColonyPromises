using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ColonyPromises
{
    // Value snapshot: no reference to a mutable title or optional DLC Def is serialized.
    public sealed class BedroomRequirement : IExposable
    {
        public string Kind;
        public int Value;
        public string LabelKey;
        public List<string> Names = new List<string>();

        public static BedroomRequirement Capture(RoomRequirement source)
        {
            var result = new BedroomRequirement { LabelKey = source.labelKey };
            if (source.GetType() == typeof(RoomRequirement_Area)) { result.Kind = "Area"; result.Value = ((RoomRequirement_Area)source).area; }
            else if (source.GetType() == typeof(RoomRequirement_Impressiveness)) { result.Kind = "Impressiveness"; result.Value = ((RoomRequirement_Impressiveness)source).impressiveness; }
            else if (source.GetType() == typeof(RoomRequirement_TerrainWithTags)) { result.Kind = "Floor"; result.Names.AddRange(((RoomRequirement_TerrainWithTags)source).tags); }
            else if (source.GetType() == typeof(RoomRequirement_Thing)) { result.Kind = "Thing"; result.Names.Add(((RoomRequirement_Thing)source).thingDef.defName); }
            else if (source.GetType() == typeof(RoomRequirement_ThingCount)) { result.Kind = "ThingCount"; result.Value = ((RoomRequirement_ThingCount)source).count; result.Names.Add(((RoomRequirement_ThingCount)source).thingDef.defName); }
            else if (source.GetType() == typeof(RoomRequirement_ThingAnyOf)) { result.Kind = "AnyThing"; result.Names.AddRange(((RoomRequirement_ThingAnyOf)source).things.Select(d => d.defName)); }
            else if (source.GetType() == typeof(RoomRequirement_ForbiddenBuildings)) { result.Kind = "Forbidden"; result.Names.AddRange(((RoomRequirement_ForbiddenBuildings)source).buildingTags); }
            else return null;
            return result;
        }

        public RoomRequirement Rebuild()
        {
            RoomRequirement result;
            switch (Kind)
            {
                case "Area": result = new RoomRequirement_Area { area = Value }; break;
                case "Impressiveness": result = new RoomRequirement_Impressiveness { impressiveness = Value }; break;
                case "Floor": result = new RoomRequirement_TerrainWithTags { tags = new List<string>(Names) }; break;
                case "Forbidden": result = new RoomRequirement_ForbiddenBuildings { buildingTags = new List<string>(Names) }; break;
                case "Thing":
                case "ThingCount":
                    var def = Names.Count == 1 ? DefDatabase<ThingDef>.GetNamedSilentFail(Names[0]) : null;
                    if (def == null) return null;
                    result = Kind == "ThingCount" ? new RoomRequirement_ThingCount { thingDef = def, count = Value }
                        : new RoomRequirement_Thing { thingDef = def }; break;
                case "AnyThing":
                    var defs = Names.Select(DefDatabase<ThingDef>.GetNamedSilentFail).Where(d => d != null).ToList();
                    if (defs.Count == 0) return null;
                    result = new RoomRequirement_ThingAnyOf { things = defs }; break;
                default: return null;
            }
            result.labelKey = LabelKey;
            return result;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Kind, "kind");
            Scribe_Values.Look(ref Value, "value");
            Scribe_Values.Look(ref LabelKey, "labelKey");
            Scribe_Collections.Look(ref Names, "names", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && Names == null) Names = new List<string>();
        }
    }
}

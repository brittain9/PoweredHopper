using HarmonyLib;
using Verse;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace PoweredHopper
{
    [StaticConstructorOnStartup]
    public class HarmonyPatches
    {
        static HarmonyPatches()
        {
            new Harmony("Alex.PoweredHopper").PatchAll(Assembly.GetExecutingAssembly());
        }

        public static Dictionary<ThingWithComps, CompPowerTrader> hopperCache = new Dictionary<ThingWithComps, CompPowerTrader>();
        public static Dictionary<Map, bool[]> hopperGrid = new Dictionary<Map, bool[]>();
    }

    //Change the perceived temperature
    [HarmonyPatch(typeof(GenTemperature), nameof(GenTemperature.GetTemperatureForCell))]
    public class Patch_GetTemperatureForCell
    {
        static public bool Prefix(Map map, IntVec3 c, ref float __result)
        {
            if (map?.info != null && HarmonyPatches.hopperGrid.TryGetValue(map, out bool[] grid))
            {
                int index = c.z * map.info.Size.x + c.x;
                if (index > -1 && index < grid.Length && grid[index])
                {
                    __result = -10f;
                    return false;
                }
            }

            return true;
        }
    }

    //This handles cache registration
    [HarmonyPatch(typeof(CompPowerTrader), nameof(CompPowerTrader.PostSpawnSetup))]
    public class Patch_PostSpawnSetup
    {
        static public void Postfix(CompPowerTrader __instance)
        {
            if (__instance.parent.def.HasModExtension<PoweredHopper>())
            {
                HarmonyPatches.hopperCache.Add(__instance.parent, __instance);
                HopperUtility.UpdateHopperGrid(__instance);
            }
        }
    }

    //This handles cache deregistration
    [HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.DeSpawn))]
    public class Patch_PostDeSpawn
    {
        static public void Prefix(ThingWithComps __instance)
        {
            CompPowerTrader comp;
            if (HarmonyPatches.hopperCache.TryGetValue(__instance, out comp))
            {
                comp.PowerOn = false;
                HopperUtility.UpdateHopperGrid(comp);
                HarmonyPatches.hopperCache.Remove(__instance);
            }
        }
    }

    //This handles fridge grid cache construction
    [HarmonyPatch(typeof(Map), nameof(Map.ConstructComponents))]
    public class Patch_ConstructComponents
    {
        static public void Prefix(Map __instance)
        {
            if (!HarmonyPatches.hopperGrid.ContainsKey(__instance)) HarmonyPatches.hopperGrid.Add(__instance, new bool[__instance.info.NumCells]);
        }
    }


    //Every 600 ticks, check fridge room temperature and adjust its power curve
    [HarmonyPatch(typeof(GameComponentUtility), nameof(GameComponentUtility.GameComponentTick))]
    public class Patch_GameComponentTick
    {
        static int tick;
        //If temperature is freezing or lower, 10% power. If 15C (60F) or higher, 100%
        static SimpleCurve powerCurve = new SimpleCurve
        {
            { new CurvePoint(0, -0.1f), true },
            { new CurvePoint(15, -1f), true }
        };

        static void Postfix()
        {
            if (++tick == 600) //about 10 seconds
            {
                tick = 0;
                foreach (var fridge in HarmonyPatches.hopperCache)
                {
                    //Validate that this fridge is still legit
                    if (fridge.Key.Map == null)
                    {
                        HarmonyPatches.hopperCache.Remove(fridge.Key);
                        break;
                    }

                    //Update power consumption
                    fridge.Value.powerOutputInt = fridge.Value.Props.PowerConsumption * powerCurve.Evaluate(fridge.Key.GetRoom().Temperature);

                    //While we're at it, update the grid the current power status
                    HopperUtility.UpdateHopperGrid(fridge.Value);
                }
            }
        }
    }

    //Flush the cache on reload
    [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
    public class Patch_LoadGame
    {
        static void Prefix()
        {
            HarmonyPatches.hopperCache.Clear();
            HarmonyPatches.hopperGrid.Clear();
        }
    }

    //Flush the cache on new games
    [HarmonyPatch(typeof(Game), nameof(Game.InitNewGame))]
    public class Patch_InitNewGame
    {
        static void Prefix()
        {
            HarmonyPatches.hopperCache.Clear();
            HarmonyPatches.hopperGrid.Clear();
        }
    }

    [HarmonyPatch(typeof(Alert_PasteDispenserNeedsHopper), "BadDispensers", MethodType.Getter)]
    public static class Patch_Alert_PasteDispenserNeedsHopper_BadDispensers_Getter
    {
        // Token: 0x06000017 RID: 23 RVA: 0x0000257C File Offset: 0x0000077C
        private static bool Prefix(Alert_PasteDispenserNeedsHopper __instance, ref List<Thing> __result)
        {
            __result = (List<Thing>)__instance.GetType().GetField("badDispensersResult", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(__instance);
            __result.Clear();
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                foreach (Thing thing in maps[i].listerThings.ThingsInGroup(ThingRequestGroup.FoodDispenser))
                {
                    bool flag = false;
                    ThingDef hopper = ThingDefOf.Hopper;
                    foreach (IntVec3 c in ((Building_NutrientPasteDispenser)thing).AdjCellsCardinalInBounds)
                    {
                        Thing edifice = c.GetEdifice(thing.Map);
                        if (edifice != null && (edifice.def == hopper || edifice.def.defName == "Alex_PoweredHopper"))
                        {
                            flag = true;
                            break;
                        }
                    }
                    if (!flag)
                    {
                        __result.Add(thing);
                    }
                }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Building_NutrientPasteDispenser), "FindFeedInAnyHopper")]
    public static class Patch_Building_NutrientPasteDispenser_FindFeedInAnyHopper
    {
        // Token: 0x06000015 RID: 21 RVA: 0x000023B4 File Offset: 0x000005B4
        private static bool Prefix(Building_NutrientPasteDispenser __instance, ref Thing __result)
        {
            foreach (IntVec3 c in __instance.AdjCellsCardinalInBounds)
            {
                Thing thing = null;
                Thing thing2 = null;
                foreach (Thing thing3 in c.GetThingList(__instance.Map))
                {
                    if (Building_NutrientPasteDispenser.IsAcceptableFeedstock(thing3.def))
                    {
                        thing = thing3;
                    }
                    if (thing3.def == ThingDefOf.Hopper || thing3.def.defName == "Alex_PoweredHopper")
                    {
                        thing2 = thing3;
                    }
                }
                if (thing != null && thing2 != null)
                {
                    __result = thing;
                    return false;
                }
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(Building_NutrientPasteDispenser), "HasEnoughFeedstockInHoppers")]
    public static class Patch_Building_NutrientPasteDispenser_HasEnoughFeedstockInHoppers
    {
        // Token: 0x06000016 RID: 22 RVA: 0x00002498 File Offset: 0x00000698
        private static bool Prefix(Building_NutrientPasteDispenser __instance, ref bool __result)
        {
            float num = 0f;
            for (int i = 0; i < __instance.AdjCellsCardinalInBounds.Count; i++)
            {
                IntVec3 c = __instance.AdjCellsCardinalInBounds[i];
                Thing thing = null;
                Thing thing2 = null;
                List<Thing> thingList = c.GetThingList(__instance.Map);
                for (int j = 0; j < thingList.Count; j++)
                {
                    Thing thing3 = thingList[j];
                    if (Building_NutrientPasteDispenser.IsAcceptableFeedstock(thing3.def))
                    {
                        thing = thing3;
                    }
                    if (thing3.def == ThingDefOf.Hopper || thing3.def.defName == "Alex_PoweredHopper")
                    {
                        thing2 = thing3;
                    }
                }
                if (thing != null && thing2 != null)
                {
                    num += (float)thing.stackCount * thing.GetStatValue(StatDefOf.Nutrition, true, -1);
                }
                if (num >= __instance.def.building.nutritionCostPerDispense)
                {
                    __result = true;
                    return false;
                }
            }
            __result = false;
            return false;
        }
    }
}

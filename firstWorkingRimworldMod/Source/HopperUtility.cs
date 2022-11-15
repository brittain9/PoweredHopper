using PoweredHopper;
using RimWorld;
using Verse;
using HarmonyLib;

namespace PoweredHopper
{
    public static class HopperUtility
    {
        public static void UpdateHopperGrid(CompPowerTrader thing)
        {
            Map map = thing?.parent.Map;
            if (map?.info != null)
            {
                CellRect cells = GenAdj.OccupiedRect(thing.parent.PositionHeld, thing.parent.Rotation,
                    thing.parent.def.size);
                foreach (var cell in cells)
                {
                    HarmonyPatches.hopperGrid[map][cell.z * map.info.Size.x + cell.x] = thing.PowerOn;
                }
            }
        }
    }
}

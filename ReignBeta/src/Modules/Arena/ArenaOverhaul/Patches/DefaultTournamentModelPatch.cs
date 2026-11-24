using ArenaOverhaul.Helpers;
using ArenaOverhaul.ModSettings;

using HarmonyLib;

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

using TaleWorlds.CampaignSystem.GameComponents;

namespace ArenaOverhaul.Patches
{
    [HarmonyPatch(typeof(DefaultTournamentModel))]
    public static class DefaultTournamentModelPatch
    {
        private static readonly MethodInfo miGetRenownReward = AccessTools.Method(typeof(DefaultTournamentModelPatch), "GetRenownReward");

        [HarmonyTranspiler]
        [HarmonyPatch("GetRenownReward")]
        public static IEnumerable<CodeInstruction> GetRenownRewardTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            List<CodeInstruction> codes = new(instructions);
            for (int i = 0; i + 1 < codes.Count; ++i)
            {
                if (miGetRenownReward != null && codes[i].LoadsConstant(3f) && codes[i + 1].opcode == OpCodes.Stloc_0)
                {
                    codes[i].opcode = OpCodes.Call;
                    codes[i].operand = miGetRenownReward;
                    return codes;
                }
            }
            throw LoggingHelper.MissingRequiredHook(__originalMethod);
        }

        /* service methods */
        internal static float GetRenownReward()
        {
            return Settings.Instance!.TournamentBaseRenownReward;
        }
    }
}
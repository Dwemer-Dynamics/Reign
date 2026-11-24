using HarmonyLib;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ArenaOverhaul.Helpers
{
    internal static class HarmonyHelper
    {
        public static bool PatchAll(ref Harmony? harmonyInstance, string sectionName, string logMessage, string chatMessage = "")
        {
            try
            {
                if (harmonyInstance is null)
                    harmonyInstance = new Harmony("Bannerlord.ArenaOverhaul");
                harmonyInstance.PatchAll(typeof(HarmonyHelper).Assembly);
                return true;
            }
            catch (Exception ex)
            {
                RollBack(harmonyInstance);
                DebugHelper.HandleException(ex, sectionName, logMessage, chatMessage);
                return false;
            }
        }

        internal static void RollBack(Harmony? harmony)
        {
            if (harmony is null)
                return;

            var failures = new List<Exception>();
            foreach (MethodBase target in Harmony.GetAllPatchedMethods().ToArray())
            {
                if (Harmony.GetPatchInfo(target)?.Owners.Contains(harmony.Id) != true)
                    continue;

                try
                {
                    harmony.Unpatch(target, HarmonyPatchType.All, harmony.Id);
                }
                catch (Exception ex)
                {
                    failures.Add(new InvalidOperationException($"Could not remove Arena hooks from {target}.", ex));
                }
            }

            if (failures.Count > 0)
                DebugHelper.HandleException(new AggregateException(failures), "Arena hook rollback", "{0}", "");
        }
    }
}
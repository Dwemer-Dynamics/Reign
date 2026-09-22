using System;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.SaveSystem;

namespace ReignBeta.Integration
{
    internal static class ReignSaveSyncDeletionPatch
    {
        private const string HarmonyId = "com.bannerlordreign.reignbeta.save_sync_deletion";
        private static Harmony _harmony;

        internal static void Apply()
        {
            try
            {
                _harmony = new Harmony(HarmonyId);
                _harmony.Patch(
                    AccessTools.Method(typeof(MBSaveLoad), nameof(MBSaveLoad.DeleteSaveGame)),
                    prefix: new HarmonyMethod(typeof(ReignSaveSyncDeletionPatch), nameof(BeforeDelete)),
                    postfix: new HarmonyMethod(typeof(ReignSaveSyncDeletionPatch), nameof(AfterDelete)));
                ReignLog.Info("Save Sync native-save deletion patch loaded.");
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Save Sync native-save deletion patch failed: " + ex);
            }
        }

        internal static void Unapply()
        {
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
        }

        private static void BeforeDelete(string saveName, out DeletedSaveIdentity __state)
        {
            __state = new DeletedSaveIdentity { NativeSaveName = saveName ?? string.Empty };
            try
            {
                SaveGameFileInfo info = MBSaveLoad.GetSaveFileWithName(saveName);
                MetaData metadata = info?.MetaData;
                if (metadata == null) return;
                __state.CampaignId = metadata["ReignCampaignId"] ?? string.Empty;
                __state.SavePointId = metadata["ReignSavePointId"] ?? string.Empty;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Save Sync could not read metadata before native save deletion: " + ex.Message);
            }
        }

        private static void AfterDelete(bool __result, DeletedSaveIdentity __state)
        {
            if (!__result || __state == null) return;
            ReignSaveSyncCoordinator.DeleteNativeSaveSnapshot(
                __state.CampaignId,
                __state.SavePointId,
                __state.NativeSaveName);
        }

        private sealed class DeletedSaveIdentity
        {
            internal string CampaignId = string.Empty;
            internal string SavePointId = string.Empty;
            internal string NativeSaveName = string.Empty;
        }
    }
}

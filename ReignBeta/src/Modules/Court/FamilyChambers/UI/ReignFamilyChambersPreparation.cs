using System;
using System.Threading.Tasks;
using ReignBeta.Court;
using ReignBeta.Integration;
using ReignBeta.UI.EventArt;

namespace ReignBeta.UI
{
    internal static class ReignFamilyChambersPreparation
    {
        internal static async void Begin(FamilyChambersSessionRecord session)
        {
            if (session?.ChatRecord == null) return;
            try
            {
                await ReignScenePreparationCoordinator.RunAsync(session.ChatRecord,
                    () => { ReignPartyChatScreenManager.OpenCastle(session.ChatRecord); return Task.CompletedTask; },
                    () => ReignCastleSceneClient.PrepareFamilyAsync(session)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                session.ChatRecord.ImageStatus = "failed";
                ReignLog.Warn("Family Chambers scene preparation failed; continuing without generated art: " + ex.Message);
            }
        }
    }
}

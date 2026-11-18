using System;
using System.Threading.Tasks;
using ReignBeta.Integration;
using ReignBeta.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Campaign
{
    public sealed partial class ReignCharacterEditorCampaignBehavior
    {
        private Settlement _profileTown;
        private bool _townProfilesSent, _townProfilesSending;
        private int _townProfileVisit;
        private DateTime _townProfilesRetryUtc;

        private void OnProfileTownEntered(MobileParty party, Settlement town, Hero hero)
        {
            if (party != MobileParty.MainParty || town?.IsTown != true) return;
            if (_profileTown == town) return;
            _profileTown = town; _townProfilesSent = false; _townProfileVisit++;
            _townProfilesRetryUtc = DateTime.MinValue;
            TryPrewarmTownProfiles();
        }

        private void OnProfileTownLeft(MobileParty party, Settlement town)
        {
            if (party != MobileParty.MainParty) return;
            _profileTown = null; _townProfilesSent = false; _townProfileVisit++;
        }

        private void TryPrewarmTownProfiles()
        {
            Settlement town = Hero.MainHero?.CurrentSettlement;
            if (_profileTown != town)
            {
                _profileTown = town; _townProfilesSent = false; _townProfileVisit++;
                _townProfilesRetryUtc = DateTime.MinValue;
            }
            if (!IsCurrentCampaign || town?.IsTown != true || _townProfilesSent || _townProfilesSending
                || DateTime.UtcNow < _townProfilesRetryUtc || ReignCampaignInitializationGate.IsPending
                || ReignSaveSyncCoordinator.IsAlignmentPending || ReignBetaSettings.Instance?.UseLocalServer == false) return;
            _townProfilesSending = true;
            // Snapshot construction runs now on the application thread. The async
            // transport never dereferences Hero/Settlement after leaving it.
            Task<Newtonsoft.Json.Linq.JObject> request;
            try
            {
                request = ReignTownNotableProfileClient.PrewarmTownNotableProfilesAsync(town,
                    () => ReignMainThread.InvokeAsync(() => IsCurrentCampaign));
            }
            catch (Exception ex)
            {
                _townProfilesSending = false; _townProfilesRetryUtc = DateTime.UtcNow.AddSeconds(30);
                ReignLog.Warn("Town notable profile capture failed: " + ex.Message); return;
            }
            _ = FinishTownProfileRequest(request, _townProfileVisit);
        }

        private async Task FinishTownProfileRequest(Task<Newtonsoft.Json.Linq.JObject> request, int visit)
        {
            bool ok = false;
            try
            {
                var response = await request.ConfigureAwait(false);
                ok = response?.Value<bool?>("ok") == true;
                ReignLog.Info("Town notable profile prewarm admitted=" + ok + " result=" + response?.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ReignLog.Warn("Town notable profile prewarm unavailable; first contact remains available: " + ex.Message); }
            finally
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!IsCurrentCampaign) return;
                    _townProfilesSending = false;
                    if (_townProfileVisit != visit) return;
                    _townProfilesSent = ok;
                    _townProfilesRetryUtc = DateTime.UtcNow.AddSeconds(30);
                }).ConfigureAwait(false);
            }
        }
    }
}

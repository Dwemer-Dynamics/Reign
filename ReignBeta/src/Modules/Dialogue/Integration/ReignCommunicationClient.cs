using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        private static int _communicationInFlight;
        private static DateTime _communicationNextPoll;
        private static string _communicationScope = "", _communicationGeneration = "";
        private static string _communicationCampaign = "", _communicationTimeline = "";
        private static readonly ReignCommunicationPollHold CommunicationPollHold = new ReignCommunicationPollHold();
        private static int _communicationPulseRequested;
        private static string CommunicationPollScope() => GetCampaignId() + "|"
            + (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main") + "|"
            + (ReignCampaignInitializationGate.Snapshot().Value<string>("generationId") ?? "");

        internal static JObject CheckpointCommunicationPollHold(string token, int seconds, bool pulse)
        {
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            if (seconds != 0 && (campaign == null || campaign.TimeControlMode != CampaignTimeControlMode.Stop))
                throw new InvalidOperationException("Periodic poll holds require a paused campaign.");
            string scope = campaign == null ? "" : CommunicationPollScope();
            bool released = false;
            if (seconds == 0) released = CommunicationPollHold.Release(campaign, scope, token, DateTime.UtcNow);
            else CommunicationPollHold.Acquire(campaign, scope, token, seconds, DateTime.UtcNow);
            if (campaign != null && (pulse || released)) RequestCommunicationPulse();
            return new JObject { ["version"] = 1, ["held"] = CommunicationPollHold.Active(campaign, scope, DateTime.UtcNow),
                ["untilUtc"] = CommunicationPollHold.UntilUtc, ["released"] = released,
                ["inFlightClearedByHold"] = false, ["queuesCleared"] = false };
        }

        public static void RequestCommunicationPulse()
        {
            Interlocked.Exchange(ref _communicationPulseRequested, 1);
            _communicationNextPoll = DateTime.MinValue;
            PulseCommunications();
        }
        private static void ApplyCommunicationCloseWork(JObject response)
        {
            var lives = ReignNpcLivesBehavior.Instance;
            if (response.Value<string>("communicationCampaignId") == GetCampaignId()
                && response.Value<string>("communicationTimelineId") == (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main")
                && lives != null && lives.Generation.Length > 0 && response.Value<string>("communicationGeneration") == lives.Generation
                && response["communicationHostReservations"] is JArray hosts)
                lives.ReconcileCommunicationHosts(hosts);
            RequestCommunicationPulse();
        }

        // The game thread freezes presence; encoding and HTTP run on a worker.
        public static void PulseCommunications()
        {
            bool requested = Volatile.Read(ref _communicationPulseRequested) != 0;
            if (CommunicationPollHold.HasLease && CommunicationPollHold.Suppresses(TaleWorlds.CampaignSystem.Campaign.Current,
                CommunicationPollScope(), DateTime.UtcNow, requested,
                ReignNpcLivesBehavior.Instance?.HasPendingCommunicationReceipts == true)) return;
            if ((!requested && DateTime.UtcNow < _communicationNextPoll) || ReignSaveSyncCoordinator.IsAlignmentPending
                || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress || ReignCampaignInitializationGate.IsPending
                || TaleWorlds.CampaignSystem.Campaign.Current == null || Hero.MainHero == null || Interlocked.CompareExchange(ref _communicationInFlight, 1, 0) != 0) return;
            Interlocked.Exchange(ref _communicationPulseRequested, 0);
            PersonalGossipPresenceSnapshot snapshot = null;
            try
            {
                snapshot = CapturePersonalGossipPresence();
                string scope = snapshot.CampaignId + "|" + snapshot.Timeline + "|" + snapshot.Generation;
                if (_communicationScope != scope) { _communicationScope = scope; _communicationGeneration = ""; }
                _communicationCampaign = snapshot.CampaignId; _communicationTimeline = snapshot.Timeline;
                _communicationNextPoll = DateTime.UtcNow.AddSeconds(10);
                var context = CaptureNpcLivesContext(_communicationGeneration);
                _ = PumpCommunicationAsync(snapshot, context, scope);
            }
            catch
            {
                snapshot?.DisposeCapture();
                Interlocked.Exchange(ref _communicationInFlight, 0);
                _communicationNextPoll = DateTime.UtcNow.AddSeconds(10);
            }
        }

        private static async Task PumpCommunicationAsync(PersonalGossipPresenceSnapshot snapshot, JObject payload, string scope)
        {
            try
            {
                if (string.IsNullOrEmpty(payload.Value<string>("generation")))
                {
                    var boot = await PostJsonAsync("/communications/scope", payload,
                        () => ReignMainThread.InvokeAsync(() => PersonalGossipSnapshotCurrent(snapshot))).ConfigureAwait(false);
                    if (boot.Value<bool?>("ok") != true) return;
                    payload["generation"] = boot.Value<string>("generation") ?? "";
                }
                payload["gossipPresence"] = await EncodePersonalGossipPresenceAsync(snapshot).ConfigureAwait(false);
                var response = await PostJsonAsync("/communications/tick", payload,
                    () => ReignMainThread.InvokeAsync(() => PersonalGossipSnapshotCurrent(snapshot))).ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (_communicationScope != scope || !PersonalGossipSnapshotCurrent(snapshot)) return;
                    if (response.Value<string>("generation") is string generation) _communicationGeneration = generation;
                    if (response.Value<bool?>("ok") != true) return;
                    var lives = ReignNpcLivesBehavior.Instance;
                    if (lives == null || lives.Generation != _communicationGeneration) return;
                    if (response["hostReservations"] is JArray hosts) lives.ReconcileCommunicationHosts(hosts);
                    foreach (var work in (response["summons"] as JArray ?? new JArray()).Children<JObject>())
                        lives.StartCommunicationSummons(work);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The durable server outbox and native receipt queue own retries.
                System.Diagnostics.Debug.WriteLine("Reign communication: " + ex.Message);
            }
            finally { snapshot.DisposeCapture(); Interlocked.Exchange(ref _communicationInFlight, 0); }
        }
    }
}

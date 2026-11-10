using System;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json.Linq;
using ReignBeta.Actions;
using ReignBeta.Integration;
using ReignBeta.Save;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.MountAndBlade;
using NativeCampaign = TaleWorlds.CampaignSystem.Campaign;

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        internal static bool NpcLivesFixtureBridgeArmed => _armed;
    }

    public sealed partial class ReignNpcLivesBehavior
    {
        private sealed class NativeAnnouncementDeliveryHold
        {
            internal NativeCampaign Owner;
            internal int Epoch;
            internal string Campaign, Timeline, Generation, SaveName, Operation;
            internal Stopwatch Clock;
            internal JObject Receipt;
        }
        private NativeAnnouncementDeliveryHold _nativeAnnouncementDeliveryHold;
        private const double NativeAnnouncementHoldMaximumSeconds = 180d;

        // Executed against the actual compiled client by the provider-free licensed probe.
        // The clock is process-local and monotonic; neither a save nor an HTTP request owns it.
        internal static bool AnnouncementTransportScopeAllows(bool sameCampaign, bool sameEpoch, bool sameTimeline,
            bool sameGeneration, bool sameSave, bool armed, bool paused, bool notSaving, double elapsedSeconds)
            => sameCampaign && sameEpoch && sameTimeline && sameGeneration && sameSave && armed && paused && notSaving
                && !double.IsNaN(elapsedSeconds) && !double.IsInfinity(elapsedSeconds)
                && elapsedSeconds >= 0 && elapsedSeconds < NativeAnnouncementHoldMaximumSeconds;

        private bool AnnouncementHoldOwnsCurrent(NativeAnnouncementDeliveryHold hold) => hold != null
            && Current(hold.Owner, hold.Epoch) && hold.Campaign == _campaign && hold.Timeline == _timeline
            && hold.Generation == Generation && hold.SaveName == ReignServerClient.ActiveNativeSaveName();

        private bool AnnouncementHoldActive(NativeAnnouncementDeliveryHold hold) => hold != null
            && AnnouncementTransportScopeAllows(ReferenceEquals(hold.Owner, NativeCampaign.Current), hold.Epoch == _epoch,
                hold.Campaign == _campaign && hold.Timeline == _timeline, hold.Generation == Generation,
                hold.SaveName == ReignServerClient.ActiveNativeSaveName(), ReignLiveInteractionTestHost.NpcLivesFixtureBridgeArmed,
                NativeCampaign.Current?.TimeControlMode == CampaignTimeControlMode.Stop,
                !ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress, hold.Clock.Elapsed.TotalSeconds);

        private void RetireAnnouncementHoldIfInvalid()
        {
            var hold = _nativeAnnouncementDeliveryHold;
            if (hold == null || AnnouncementHoldActive(hold)) return;
            // A foreign/restored campaign must not receive a stale fixture journal write.
            if (AnnouncementHoldOwnsCurrent(hold))
            {
                if (hold.Receipt.Value<string>("status") != "uncertain") hold.Receipt["status"] = "expired";
                hold.Receipt["expired"] = true;
                hold.Receipt["retiredUtc"] = DateTime.UtcNow.ToString("O");
                ReignAgreementBehavior.Instance?.SaveNativeOperation(hold.Operation, hold.Receipt);
            }
            _nativeAnnouncementDeliveryHold = null;
        }

        private static JArray AnnouncementMemberIds(JObject payload) => new JArray((payload["members"] as JArray ?? new JArray())
            .OfType<JObject>().Where(m => m.Value<bool?>("alive") == true && m.Value<bool?>("free") == true
                && m.Value<bool?>("adult") == true && m.Value<bool?>("inMainParty") == true
                && m.Value<string>("partyId") == payload.Value<string>("partyId"))
            .Select(m => m.Value<string>("heroId")).Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal));

        private JArray CurrentAnnouncementMemberIds() => AnnouncementMemberIds(ReignServerClient.CaptureNpcLivesContext(Generation, true));

        private bool CaptureHeldAnnouncement(NativeAnnouncementDeliveryHold hold, JObject row)
        {
            if (row == null) return true;
            var payload = row["payload"] as JObject;
            string operation = row.Value<string>("operationId") ?? "";
            string digest = PartyComposerDigest(payload);
            var captured = hold.Receipt["queuedAnnouncement"] as JObject;
            if (payload == null || operation.Length == 0 || captured != null
                && (captured.Value<string>("operationId") != operation || captured.Value<string>("payloadDigest") != digest))
            {
                hold.Receipt["status"] = "uncertain";
                hold.Receipt["error"] = "The original queued announcement identity or bytes changed; inspect or restore the enrolled Current.";
                ReignAgreementBehavior.Instance.SaveNativeOperation(hold.Operation, hold.Receipt);
                return false;
            }
            if (captured == null)
            {
                hold.Receipt["queuedAnnouncement"] = new JObject {
                    ["operationId"] = operation, ["payloadDigest"] = digest,
                    ["textLength"] = payload.Value<string>("text")?.Length ?? 0,
                    ["sourceMemberIds"] = AnnouncementMemberIds(payload), ["worldDay"] = payload["worldDay"]?.DeepClone(),
                    ["capturedUtc"] = DateTime.UtcNow.ToString("O") };
                ReignAgreementBehavior.Instance.SaveNativeOperation(hold.Operation, hold.Receipt);
            }
            return true;
        }

        // Called only at the ordinary outbox dispatch boundary. It never edits, removes,
        // acknowledges, retargets or sends a payload. Expiry resumes ordinary transport.
        private bool HoldAnnouncementDeliveryForNativeTest(JObject row)
        {
            if (row?.Value<string>("route") != "announcement") return false;
            RetireAnnouncementHoldIfInvalid();
            var hold = _nativeAnnouncementDeliveryHold;
            if (hold == null) return false;
            CaptureHeldAnnouncement(hold, row);
            return true;
        }

        private JObject AnnouncementControlObservation(JObject retained, bool replayed)
        {
            var result = (JObject)retained.DeepClone();
            var hold = _nativeAnnouncementDeliveryHold;
            result["replayed"] = replayed;
            result["active"] = hold != null && hold.Operation == retained.Value<string>("operationId") && AnnouncementHoldActive(hold);
            result["currentMemberIds"] = CurrentAnnouncementMemberIds();
            result["outboxCount"] = _outbox.Count;
            result["worldDay"] = CampaignTime.Now.ToDays;
            return result;
        }

        internal JObject ControlNativeAnnouncementDelivery(JObject test, JObject command)
        {
            string[] fields = { "caseId", "phase", "heroId", "otherHeroId", "operationId", "control" };
            if (test.Count != fields.Length || test.Properties().Any(p => !fields.Contains(p.Name)
                || p.Value.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)p.Value)
                || ((string)p.Value).Length > 128 || ((string)p.Value).Any(ch => !char.IsLetterOrDigit(ch) && ch != '_' && ch != '-' && ch != ':'))
                || test.Value<string>("phase") != "announcement_transport" || test.Value<string>("heroId") == test.Value<string>("otherHeroId")
                || !new[] { "arm", "observe", "release" }.Contains(test.Value<string>("control")))
                throw new InvalidOperationException("Supply only the exact bounded announcement control, case, operation and two distinct NPC identities.");
            var campaign = NativeCampaign.Current;
            if (!ReignLiveInteractionTestHost.OwnsNpcLivesSession || !_initialized || Generation.Length == 0 || !Current(campaign, _epoch)
                || campaign.TimeControlMode != CampaignTimeControlMode.Stop || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress)
                throw new InvalidOperationException("The announcement control requires its exact enrolled paused NPC Lives session.");
            string operation = "npc_announcement_hold:" + test.Value<string>("operationId");
            string save = command["enrollment"]?.Value<string>("disposableSaveName") ?? "";
            string hash = PartyComposerDigest(new { campaign = _campaign, timeline = _timeline, generation = Generation, save,
                gameInstance = command.Value<string>("gameInstanceId"), caseId = test.Value<string>("caseId"),
                heroId = test.Value<string>("heroId"), otherHeroId = test.Value<string>("otherHeroId"), operationId = test.Value<string>("operationId") });
            var journal = ReignAgreementBehavior.Instance ?? throw new InvalidOperationException("Native fixture journal is unavailable.");
            RetireAnnouncementHoldIfInvalid();
            var retained = journal.NativeOperation(operation);
            string control = test.Value<string>("control");
            if (retained != null)
            {
                if (retained.Value<string>("requestHash") != hash)
                    throw new InvalidOperationException("This operation identity already belongs to another exact announcement control.");
                var hold = _nativeAnnouncementDeliveryHold;
                if (control == "arm" || hold == null || hold.Operation != operation)
                    return AnnouncementControlObservation(retained, true); // Never rearm an old/expired/uncertain identity.
                if (!CaptureHeldAnnouncement(hold, _outbox.FirstOrDefault(r => r.Value<string>("route") == "announcement")))
                    throw new InvalidOperationException(hold.Receipt.Value<string>("error"));
                if (control == "release")
                {
                    hold.Receipt["status"] = "released"; hold.Receipt["releaseCount"] = 1;
                    hold.Receipt["releasedUtc"] = DateTime.UtcNow.ToString("O");
                    hold.Receipt["releaseMemberIds"] = CurrentAnnouncementMemberIds();
                    journal.SaveNativeOperation(operation, hold.Receipt);
                    _nativeAnnouncementDeliveryHold = null; _nextPoll = 0;
                }
                return AnnouncementControlObservation(hold.Receipt, false);
            }
            if (control != "arm") throw new InvalidOperationException("Arm this exact announcement control before observing or releasing it.");
            var party = MobileParty.MainParty;
            if (_nativeAnnouncementDeliveryHold != null || _busy || _outbox.Count != 0 || _failedOperations.Count != 0
                || party == null || !party.IsActive || party.IsDisbanding || party.LeaderHero != Hero.MainHero
                || Hero.MainHero == null || Hero.MainHero.IsDead || Hero.MainHero.IsChild || Hero.MainHero.IsPrisoner
                || party.Army != null || party.MapEvent != null || party.SiegeEvent != null || party.IsCurrentlyAtSea
                || party.IsTransitionInProgress || Mission.Current != null || !QuietMap())
                throw new InvalidOperationException("Arm only an idle drained exact Current with a free independent land player and no conversation, inquiry or encounter.");
            foreach (string id in new[] { test.Value<string>("heroId"), test.Value<string>("otherHeroId") })
            {
                var hero = ReignActionService.HeroById(id);
                if (hero == null || hero == Hero.MainHero || !hero.IsAlive || !hero.IsActive || hero.IsChild || hero.IsPrisoner
                    || hero.PartyBelongedTo != party || party.MemberRoster.GetTroopCount(hero.CharacterObject) < 1)
                    throw new InvalidOperationException("Both announcement control identities must be actual living free adult main-party NPCs.");
            }
            var members = CurrentAnnouncementMemberIds();
            if (members.Count > 64) throw new InvalidOperationException("This bounded announcement control observes at most 64 actual party NPCs.");
            var receipt = new JObject { ["schema"] = "reign-npc-announcement-transport-control-v1", ["origin"] = "operator_queued_delivery_control",
                ["caseId"] = test["caseId"], ["operationId"] = operation, ["requestHash"] = hash,
                ["status"] = "armed", ["armCount"] = 1, ["releaseCount"] = 0, ["maximumSeconds"] = NativeAnnouncementHoldMaximumSeconds,
                ["armedUtc"] = DateTime.UtcNow.ToString("O"), ["armMemberIds"] = members,
                ["before"] = ObservePartyComposerConservation(test.Value<string>("heroId"), test.Value<string>("otherHeroId")),
                ["consentGranted"] = false, ["providerCalls"] = 0,
                ["cleanup"] = "Release or allow the 180-second monotonic expiry; prove the original outbox drained before time advance or saving. Restore only the enrolled Current for roster-fixture cleanup." };
            journal.SaveNativeOperation(operation, receipt);
            _nativeAnnouncementDeliveryHold = new NativeAnnouncementDeliveryHold { Owner = campaign, Epoch = _epoch,
                Campaign = _campaign, Timeline = _timeline, Generation = Generation, SaveName = save, Operation = operation,
                Clock = Stopwatch.StartNew(), Receipt = receipt };
            return AnnouncementControlObservation(receipt, false);
        }
    }
}

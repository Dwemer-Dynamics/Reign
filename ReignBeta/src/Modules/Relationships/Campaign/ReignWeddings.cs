using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using Reign.Relationships;
using ReignBeta.Actions;
using ReignBeta.Events;
using ReignBeta.Family;
using ReignBeta.Integration;
using ReignBeta.Save;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.Campaign
{
    internal enum WeddingState
    {
        Choice, Scheduled, Ceremony, Ready, Applying, Completed, Cancelled, Recovery
    }

    internal sealed class ReignWedding
    {
        public string Id = "", CampaignId = "", TimelineId = "", FirstId = "", SecondId = "",
            FirstClanId = "", SecondClanId = "", ReceivingClanId = "", LeaderId = "", VenueId = "",
            TermsHash = "", Reason = "";
        public string LinkedTerms = "", LinkedActor = "", LinkedTarget = "";
        public WeddingState State;
        public ActionAgreement Consideration;
        public double Starts, Ends;
        public int Reschedules, Phase, LeaderGain;
        public bool PlayerChoice, PlayerSpouse, Attended, ChoiceShown, Announced, DirectorAction, DirectorReported, TrustedConsideration;
        public List<string> Guests = new List<string>();
        public Dictionary<string, string> Unavailable = new Dictionary<string, string>();
        public Dictionary<string, string> Appeals = new Dictionary<string, string>();
        public List<string> Settled = new List<string>();
        public Dictionary<string, JObject> Custody = new Dictionary<string, JObject>();
        public Dictionary<string, JObject> SettlementEvidence = new Dictionary<string, JObject>();
        public string PendingAppeal = "";
        public DateTime AppealUntilUtc;
        public bool Terminal => State == WeddingState.Completed || State == WeddingState.Cancelled || State == WeddingState.Recovery;
    }

    public sealed partial class ReignRelationshipCampaignBehavior
    {
        private Dictionary<string, ReignWedding> _weddings = new Dictionary<string, ReignWedding>(StringComparer.Ordinal);
        private List<string> _weddingChunks = new List<string>();
        private float _weddingTickSeconds;
        private bool _restoreWeddings;
        private string _completingWedding = "";
        private string _openWeddingChoice = "";
        private bool _weddingReportInFlight;
        private DateTime _weddingReportRetryUtc;

        internal static bool WeddingFeatureReady => ReignMarriagePatches.WeddingsReady
            && ReignBeta.Settings.ReignBetaSettings.Instance?.Enabled != false && Instance != null;

        private void SyncWeddings(IDataStore store)
        {
            if (store.IsSaving)
                _weddingChunks = ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(_weddings));
            store.SyncData("_reignWeddingsV1", ref _weddingChunks);
            if (store.IsLoading)
            {
                _weddings = _weddingChunks?.Count > 0
                    ? JsonConvert.DeserializeObject<Dictionary<string, ReignWedding>>(ReignSavePayloadCodec.Decode(_weddingChunks))
                        ?? new Dictionary<string, ReignWedding>(StringComparer.Ordinal)
                    : new Dictionary<string, ReignWedding>(StringComparer.Ordinal);
                _restoreWeddings = true;
            }
        }

        private static string WeddingTimeline => ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
        private static double WeddingNow => CampaignTime.Now.ToDays;
        internal ReignWedding Wedding(string id) => !string.IsNullOrEmpty(id) && _weddings.TryGetValue(id, out var match) ? match : null;

        internal bool ReservedForWedding(Hero hero)
        {
            if (hero == null) return false;
            return _weddings.Values.Any(w => (!w.Terminal || w.State == WeddingState.Recovery)
                && w.Id != _completingWedding && (w.FirstId == hero.StringId || w.SecondId == hero.StringId));
        }

        internal bool WeddingAuthorization(Hero first, Hero second)
        {
            var wedding = Wedding(_completingWedding);
            return wedding != null && wedding.FirstId == first?.StringId && wedding.SecondId == second?.StringId;
        }

        private bool SuitableWeddingPair(Hero first, Hero second, string owner = "")
        {
            if (first == null || second == null || !first.IsAlive || !second.IsAlive || first.IsPrisoner || second.IsPrisoner
                || first.Spouse != null || second.Spouse != null || first.Clan == null || second.Clan == null
                || first.Clan.IsEliminated || second.Clan.IsEliminated
                || FactionManager.IsAtWarAgainstFaction(first.MapFaction, second.MapFaction)) return false;
            string previous = _completingWedding;
            try
            {
                _completingWedding = owner;
                return TaleWorlds.CampaignSystem.Campaign.Current.Models.MarriageModel.IsCoupleSuitableForMarriage(first, second);
            }
            finally { _completingWedding = previous; }
        }

        internal bool RequestWedding(string id, Hero first, Hero second, ActionAgreement terms, string leaderId, int leaderGain, out ReignWedding match, bool trustedConsideration = false, bool legacyPreparation = false)
        {
            match = Wedding(id);
            if (match != null)
                return match.FirstId == first?.StringId && match.SecondId == second?.StringId
                    && match.TermsHash == (terms?.TermsHash() ?? "");
            if (!WeddingFeatureReady || string.IsNullOrWhiteSpace(id)) return false;
            if (legacyPreparation)
            {
                if (first?.Clan == null || second?.Clan == null || first.Clan != Clan.PlayerClan
                    || !first.IsAlive || !second.IsAlive || first.Spouse != null || second.Spouse != null
                    || ReservedForWedding(first) || ReservedForWedding(second)) return false;
            }
            else if (!SuitableWeddingPair(first, second)) return false;
            var receivingClan = TaleWorlds.CampaignSystem.Campaign.Current.Models.MarriageModel.GetClanAfterMarriage(first, second);
            if (receivingClan == null) return false;
            try { ReignWeddingBargain.Check(terms, first, second, trustedConsideration); }
            catch (InvalidOperationException ex)
            {
                ReignLog.Warn("Wedding bargain preflight rejected " + id + ": " + ex.Message);
                return false;
            }
            match = new ReignWedding
            {
                Id = id, CampaignId = ReignCampaignIdentity.CurrentCampaignId(), TimelineId = WeddingTimeline,
                FirstId = first.StringId, SecondId = second.StringId, FirstClanId = first.Clan.StringId,
                SecondClanId = second.Clan.StringId, ReceivingClanId = receivingClan.StringId, LeaderId = leaderId ?? "",
                Consideration = terms == null ? null : ReignActionService.Copy(terms), TermsHash = terms?.TermsHash() ?? "",
                PlayerChoice = first.Clan == Clan.PlayerClan || second.Clan == Clan.PlayerClan,
                PlayerSpouse = first == Hero.MainHero || second == Hero.MainHero, LeaderGain = leaderGain,
                State = WeddingState.Choice, TrustedConsideration = trustedConsideration
            };
            _weddings.Add(id, match);
            if (!match.PlayerChoice)
            {
                var venue = WeddingTowns(match).FirstOrDefault();
                if (venue == null) CancelWedding(match, "There is no town both clans can safely enter.");
                else BookWedding(match, venue);
            }
            return true;
        }

        internal static bool SafeWeddingTown(Settlement town, Clan first, Clan second)
        {
            return town?.IsTown == true && !town.IsUnderSiege && town.MapFaction != null && first != null && second != null
                && !FactionManager.IsAtWarAgainstFaction(town.MapFaction, first.MapFaction)
                && !FactionManager.IsAtWarAgainstFaction(town.MapFaction, second.MapFaction);
        }

        private IEnumerable<Settlement> WeddingTowns(ReignWedding match)
        {
            Hero first = FindHero(match.FirstId), second = FindHero(match.SecondId);
            if (first == null || second == null) return Enumerable.Empty<Settlement>();
            var center = first.CurrentSettlement ?? first.HomeSettlement ?? first.Clan?.FactionMidSettlement;
            return Settlement.All.Where(s => SafeWeddingTown(s, first.Clan, second.Clan))
                .OrderBy(s => s.OwnerClan?.StringId == match.ReceivingClanId ? 0 : s.OwnerClan == first.Clan || s.OwnerClan == second.Clan ? 1 : 2)
                .ThenBy(s => center == null ? 0f : center.GetPosition2D.DistanceSquared(s.GetPosition2D))
                .ThenBy(s => s.StringId, StringComparer.Ordinal);
        }

        private void BookWedding(ReignWedding match, Settlement venue)
        {
            if (!WeddingTowns(match).Contains(venue)) return;
            double date = WeddingNow + ArrangedMarriagePolicy.WeddingNoticeDays;
            foreach (var other in _weddings.Values.Where(w => w != match && (w.State == WeddingState.Scheduled || w.State == WeddingState.Ceremony))
                .OrderBy(w => w.Starts))
            {
                if (date >= other.Ends || date + ArrangedMarriagePolicy.WeddingWindowDays <= other.Starts) continue;
                if (other.VenueId == venue.StringId || other.FirstClanId == match.FirstClanId || other.FirstClanId == match.SecondClanId
                    || other.SecondClanId == match.FirstClanId || other.SecondClanId == match.SecondClanId)
                    date = other.Ends;
            }
            match.VenueId = venue.StringId;
            match.Starts = date;
            match.Ends = date + ArrangedMarriagePolicy.WeddingWindowDays;
            match.State = WeddingState.Scheduled;
            match.Phase = 0;
            match.Guests.Clear();
            match.Unavailable.Clear();
            match.Appeals.Clear();
            match.PendingAppeal = "";
            match.Announced = false;
        }

        private static string WeddingNames(ReignWedding match)
            => (FindHero(match.FirstId)?.Name?.ToString() ?? match.FirstId) + " and "
                + (FindHero(match.SecondId)?.Name?.ToString() ?? match.SecondId);

        internal bool HasWeddingChoice => _weddings.Values.Any(w => w.State == WeddingState.Choice && w.PlayerChoice);

        internal void ShowWeddingChoice()
        {
            var match = _weddings.Values.FirstOrDefault(w => w.State == WeddingState.Choice && w.PlayerChoice);
            if (match == null || _openWeddingChoice.Length != 0) return;
            match.ChoiceShown = true;
            _openWeddingChoice = match.Id;
            var options = new List<InquiryElement>
            {
                new InquiryElement("ceremony", "Hold a ceremony", null),
                new InquiryElement("immediate", "Marry now", null)
            };
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData("Wedding plans", WeddingNames(match),
                options, true, 1, 1, "Continue", "Decide later", selected =>
                {
                    _openWeddingChoice = "";
                    if (!WeddingChoiceCurrent(match)) return;
                    if ((string)selected[0].Identifier == "immediate") match.State = WeddingState.Ready;
                    else ShowWeddingTowns(match);
                }, _ => _openWeddingChoice = ""));
        }

        private bool WeddingChoiceCurrent(ReignWedding match)
            => ReferenceEquals(Instance, this) && Wedding(match.Id) == match
                && match.State == WeddingState.Choice && match.TimelineId == WeddingTimeline;

        private void ShowWeddingTowns(ReignWedding match)
        {
            var towns = WeddingTowns(match).ToList();
            if (towns.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage("There is no town both clans can safely enter."));
                return;
            }
            _openWeddingChoice = match.Id;
            if (!ReignBeta.UI.ReignActionBuilder.OpenWeddingTownPicker(WeddingNames(match), towns, town =>
                {
                    if (WeddingChoiceCurrent(match)) BookWedding(match, town);
                }, () => _openWeddingChoice = ""))
                _openWeddingChoice = "";
        }

        private void TickWeddings(float deltaTime)
        {
            _weddingTickSeconds += deltaTime;
            if (_weddingTickSeconds < .25f) return;
            _weddingTickSeconds = 0;
            if (!WeddingFeatureReady || Hero.MainHero?.Clan == null || ReignSaveSyncCoordinator.IsAlignmentPending
                || ReignCampaignInitializationGate.IsPending) return;
            if (_restoreWeddings)
            {
                _restoreWeddings = false;
                string campaign = ReignCampaignIdentity.CurrentCampaignId();
                foreach (var match in _weddings.Values.Where(w => w.CampaignId == campaign))
                {
                    // A restored native save can fork from a director action's
                    // old server timeline. Its local consent survives the fork.
                    if (match.TimelineId != WeddingTimeline) match.DirectorAction = false;
                    match.TimelineId = WeddingTimeline;
                    match.PendingAppeal = "";
                    if (match.State == WeddingState.Applying)
                    {
                        match.State = WeddingState.Recovery;
                        match.Reason = "Native settlement was interrupted and needs reconciliation.";
                    }
                }
                RestoreMarriageOffers();
            }
            TickMarriageOffers();
            PollWeddingReceipts();
            if (_weddings.Count == 0) return;
            var campaignNow = TaleWorlds.CampaignSystem.Campaign.Current;
            if (Mission.Current != null || campaignNow.SaveHandler?.IsSaving == true
                || campaignNow.ConversationManager?.IsConversationInProgress == true
                || MobileParty.MainParty?.MapEvent != null) return;
            foreach (var match in _weddings.Values.ToList())
            {
                if (match.Terminal || match.TimelineId != WeddingTimeline) continue;
                Hero first = FindHero(match.FirstId), second = FindHero(match.SecondId);
                if (first == null || second == null || !first.IsAlive || !second.IsAlive || first.Spouse != null || second.Spouse != null
                    || first.Clan?.StringId != match.FirstClanId || second.Clan?.StringId != match.SecondClanId
                    || FactionManager.IsAtWarAgainstFaction(first.MapFaction, second.MapFaction))
                {
                    CancelWedding(match, "The agreed match is no longer available.");
                    continue;
                }
                if (match.State == WeddingState.Scheduled)
                {
                    var venue = Settlement.Find(match.VenueId);
                    if (!match.Announced)
                    {
                        match.Announced = true;
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"{WeddingNames(match)} will marry at {venue?.Name} in {Math.Ceiling(Math.Max(0, match.Starts - WeddingNow))} days (day {match.Starts:0.00})."));
                    }
                    if (WeddingNow < match.Starts) continue;
                    if (!SafeWeddingTown(venue, first.Clan, second.Clan) || WeddingNow >= match.Ends
                        || !OpenWeddingDay(match, first, second, venue))
                    {
                        RescheduleWedding(match);
                        continue;
                    }
                }
                if (match.State == WeddingState.Ceremony)
                {
                    if (!SafeWeddingTown(Settlement.Find(match.VenueId), first.Clan, second.Clan)
                        || first.IsPrisoner || second.IsPrisoner)
                    {
                        RescheduleWedding(match);
                        continue;
                    }
                    if (match.PendingAppeal.Length != 0 && DateTime.UtcNow >= match.AppealUntilUtc)
                        match.PendingAppeal = "";
                    if (WeddingNow < match.Ends || match.PendingAppeal.Length != 0) continue;
                    if (match.PlayerSpouse)
                    {
                        RescheduleWedding(match);
                        continue;
                    }
                    if (ArrangedMarriagePolicy.CanConcludeUnattended(WeddingNow, match.Ends, match.PlayerSpouse, match.PendingAppeal.Length != 0))
                        match.State = WeddingState.Ready;
                }
                if (match.State == WeddingState.Ready && !TemporarilyUnavailable(first) && !TemporarilyUnavailable(second))
                    SettleWedding(match, first, second);
            }
            if (_openWeddingChoice.Length == 0 && _weddings.Values.Any(w => w.State == WeddingState.Choice && w.PlayerChoice && !w.ChoiceShown)
                && !InformationManager.IsAnyInquiryActive())
                ShowWeddingChoice();
        }

        private static bool TemporarilyUnavailable(Hero hero)
            => hero.IsPrisoner || hero.PartyBelongedTo?.MapEvent != null || hero.PartyBelongedTo?.Army != null;

        private bool OpenWeddingDay(ReignWedding match, Hero first, Hero second, Settlement venue)
        {
            var travel = ReignNpcLivesBehavior.Instance;
            if (travel == null || !SuitableWeddingPair(first, second, match.Id)) return false;
            foreach (var spouse in new[] { first, second }.Where(h => h != Hero.MainHero))
                if (travel.WeddingAttendanceProblem(spouse, venue).Length != 0) return false;
            match.State = WeddingState.Ceremony;
            var guests = new[] { first, second }.Concat(first.Clan.Heroes).Concat(second.Clan.Heroes)
                .Where(h => h != null && h.IsAlive && h != Hero.MainHero).Distinct();
            foreach (var guest in guests)
            {
                string reason = travel.BeginWeddingAttendance(match.Id, guest, venue);
                if (reason.Length == 0) match.Guests.Add(guest.StringId);
                else match.Unavailable[guest.StringId] = reason;
                if (reason.Length != 0 && (guest == first || guest == second)) return false;
            }
            return true;
        }

        private void RescheduleWedding(ReignWedding match)
        {
            ReignNpcLivesBehavior.Instance?.EndWeddingAttendance(match.Id);
            if (match.Reschedules++ != 0) { CancelWedding(match, "The ceremony could not take place on its second date."); return; }
            var original = Settlement.Find(match.VenueId);
            if (WeddingTowns(match).Contains(original)) BookWedding(match, original);
            else if (match.PlayerChoice)
            {
                match.State = WeddingState.Choice;
                match.ChoiceShown = false;
            }
            else
            {
                var next = WeddingTowns(match).FirstOrDefault();
                if (next != null) BookWedding(match, next);
                else CancelWedding(match, "There is no safe replacement town.");
            }
        }

        internal ReignWedding AvailableWedding(Settlement town)
            => _weddings.Values.Where(w => w.State == WeddingState.Ceremony && w.VenueId == town?.StringId
                && w.TimelineId == WeddingTimeline && WeddingNow < w.Ends).OrderBy(w => w.Starts).FirstOrDefault();

        internal bool ConcludeWedding(string id, int phase)
        {
            var match = Wedding(id);
            var town = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
            if (match == null || match != AvailableWedding(town) || phase < 2 || match.PendingAppeal.Length != 0
                || match.Phase != phase) return false;
            match.State = WeddingState.Ready;
            return true;
        }

        internal bool WithdrawPlayerWedding(string id)
        {
            var match = Wedding(id);
            if (match?.PlayerSpouse != true || match != AvailableWedding(Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement)) return false;
            CancelWedding(match, "The player withdrew from the wedding.");
            return true;
        }

        private static JObject WeddingDirectorReceipt(ReignWedding wedding)
        {
            string status = wedding.State == WeddingState.Completed ? "completed"
                : wedding.State == WeddingState.Cancelled ? "obsolete"
                : wedding.State == WeddingState.Recovery ? "recovery" : "wedding_pending";
            return new JObject
            {
                ["directorActionId"] = wedding.Id, ["worldDay"] = WeddingNow, ["timelineId"] = WeddingTimeline,
                ["status"] = status, ["error"] = wedding.Reason, ["marriageVerified"] = status == "completed",
                ["paymentVerified"] = status == "completed",
                ["fee"] = wedding.Consideration?.Clauses.Where(c => c.ActionId == "give.gold")
                    .Sum(c => int.Parse(ReignActionService.Arg(c, "quantity"))) ?? 0
            };
        }

        private void PollWeddingReceipts()
        {
            if (_weddingReportInFlight || DateTime.UtcNow < _weddingReportRetryUtc) return;
            var pending = _weddings.Values.Where(w => w.DirectorAction && w.Terminal && !w.DirectorReported
                && w.TimelineId == WeddingTimeline).Take(16).ToList();
            var offers = _marriageOffers.Values.Where(o => o.DirectorOffer && !o.DirectorReported && o.TimelineId == WeddingTimeline
                && (o.State == "refused" || o.State == "expired" || o.State == "unavailable")).Take(16).ToList();
            if (pending.Count + offers.Count != 0) _ = ReportWeddingReceipts(pending, offers);
        }

        private async System.Threading.Tasks.Task ReportWeddingReceipts(List<ReignWedding> pending, List<ReignPlayerMarriageOffer> offers)
        {
            _weddingReportInFlight = true;
            string timeline = WeddingTimeline;
            int generation = ReignSaveSyncCoordinator.CampaignGeneration;
            var receipts = new JArray(pending.Select(WeddingDirectorReceipt));
            foreach (var offer in offers)
                receipts.Add(new JObject { ["directorActionId"] = offer.Id, ["status"] = offer.State == "refused" ? "player_refused" : "obsolete",
                    ["worldDay"] = WeddingNow, ["timelineId"] = timeline });
            var result = await ReignServerClient.ReportRelationshipDirectorActionsAsync(receipts).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() =>
            {
                _weddingReportInFlight = false;
                if (!ReferenceEquals(Instance, this) || timeline != WeddingTimeline
                    || generation != ReignSaveSyncCoordinator.CampaignGeneration) return;
                if (result)
                {
                    foreach (var wedding in pending) wedding.DirectorReported = true;
                    foreach (var offer in offers) offer.DirectorReported = true;
                }
                else _weddingReportRetryUtc = DateTime.UtcNow.AddSeconds(10);
            }).ConfigureAwait(false);
        }

        private void CancelWedding(ReignWedding match, string reason)
        {
            if (match.Terminal) return;
            match.State = WeddingState.Cancelled;
            match.Reason = reason;
            match.PendingAppeal = "";
            ReignNpcLivesBehavior.Instance?.EndWeddingAttendance(match.Id);
            RecordWeddingOutcome(match, "called_off");
            InformationManager.DisplayMessage(new InformationMessage($"The wedding of {WeddingNames(match)} was called off."));
            if (match.PlayerChoice) InformationManager.DisplayMessage(new InformationMessage(reason));
        }

        private void SettleWedding(ReignWedding match, Hero first, Hero second)
        {
            ReignNpcLivesBehavior.Instance?.EndWeddingAttendance(match.Id);
            if (!SuitableWeddingPair(first, second, match.Id)
                || TaleWorlds.CampaignSystem.Campaign.Current.Models.MarriageModel.GetClanAfterMarriage(first, second)?.StringId != match.ReceivingClanId)
            {
                CancelWedding(match, "The native marriage rules no longer permit the agreed union.");
                return;
            }
            try
            {
                _completingWedding = match.Id;
                ReignWeddingBargain.Settle(match, first, second);
                if (match.LeaderGain != 0)
                {
                    var leader = FindHero(match.LeaderId);
                    if (leader != null)
                    {
                        int before = leader.GetRelation(Hero.MainHero);
                        ApplyPlayerMarriageRelation(match.LeaderId, match.LeaderGain);
                        match.SettlementEvidence["leader_relation"] = new JObject
                        {
                            ["leaderId"] = leader.StringId, ["before"] = before,
                            ["after"] = leader.GetRelation(Hero.MainHero), ["requestedDelta"] = match.LeaderGain
                        };
                    }
                }
                match.State = WeddingState.Completed;
                ReignBeta.Court.ReignCourtCampaignBehavior.Instance?.PumpCourtWeddingReports();
                RecordWeddingOutcome(match, "married");
                InformationManager.DisplayMessage(new InformationMessage(WeddingNames(match) + " are married."));
            }
            catch (Exception ex)
            {
                if (match.State == WeddingState.Applying)
                {
                    match.State = WeddingState.Recovery;
                    match.Reason = ex.Message;
                    ReignLog.Warn("Wedding settlement needs recovery " + match.Id + ": " + ex.Message);
                    InformationManager.DisplayMessage(new InformationMessage("The wedding settlement needs recovery. " + ex.Message));
                }
                else CancelWedding(match, ex.Message);
            }
            finally { _completingWedding = ""; }
        }

        private static void RecordWeddingOutcome(ReignWedding match, string outcome)
        {
            ReignWorldHistoryCampaignBehavior.Instance?.RecordReignSystemEvent("wedding", outcome, "relationships", match.Id,
                WeddingNames(match) + ". " + (match.Reason.Length != 0 ? match.Reason + " " : "") + DescribeMarriageTerms(match.Consideration),
                "participants", match.FirstId, match.SecondId, new JObject { ["weddingId"] = match.Id },
                new JArray(match.FirstId, match.SecondId));
        }

        private static void ApplyPlayerMarriageRelation(string leaderId, int delta)
        {
            var leader = FindHero(leaderId);
            if (leader != null && Hero.MainHero != null && leader != Hero.MainHero)
                SetRelationWithoutGameplayRewards(leader, Hero.MainHero, leader.GetRelation(Hero.MainHero) + delta);
        }
    }
}

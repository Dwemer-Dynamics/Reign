using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using Reign.Relationships;
using ReignBeta.Actions;
using ReignBeta.Integration;
using ReignBeta.Save;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace ReignBeta.Campaign
{
    internal sealed class ReignPlayerMarriageOffer
    {
        public string Id = "", LeaderId = "", PlayerMatchId = "", ProspectId = "", CampaignId = "", TimelineId = "",
            State = "courier", Response = "", MeetingTown = "";
        public ActionAgreement Terms;
        public double OfferedDay, DeliveredDay, ExpiresDay, ResponseDay, MeetingDispatch, ArrivalDay;
        public bool LetterSent, ResponseSent, MeetingDispatched, MeetingSent, MeetingArrived, MeetingFinished, DirectorReported;
        [JsonIgnore] public bool DirectorOffer => Id.StartsWith("director_action_", StringComparison.Ordinal);
        [JsonIgnore] public bool Open => State == "open" && ArrangedMarriagePolicy.PlayerReplyWindow(DeliveredDay, CampaignTime.Now.ToDays);
        [JsonIgnore] public bool Outstanding => State == "courier" || Open || State == "reply_courier";
    }

    public sealed partial class ReignRelationshipCampaignBehavior
    {
        private Dictionary<string, ReignPlayerMarriageOffer> _marriageOffers = new Dictionary<string, ReignPlayerMarriageOffer>();
        private List<string> _marriageOfferChunks = new List<string>();
        private List<string> _marriageHistoryChunks = new List<string>();
        private Dictionary<string, JObject> _marriageHistory = new Dictionary<string, JObject>(StringComparer.Ordinal);
        private readonly HashSet<string> _marriageMailInFlight = new HashSet<string>();
        private DateTime _marriageRetryUtc;

        private void SyncMarriageOffers(IDataStore store)
        {
            if (store.IsSaving) _marriageOfferChunks = ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(_marriageOffers));
            store.SyncData("_reignPlayerMarriageOffersV1", ref _marriageOfferChunks);
            if (store.IsSaving) _marriageHistoryChunks = ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(_marriageHistory));
            store.SyncData("_reignPlayerMarriageHistoryV1", ref _marriageHistoryChunks);
            if (store.IsLoading && _marriageHistoryChunks?.Count > 0)
                _marriageHistory = JsonConvert.DeserializeObject<Dictionary<string, JObject>>(ReignSavePayloadCodec.Decode(_marriageHistoryChunks))
                    ?? new Dictionary<string, JObject>(StringComparer.Ordinal);
            if (store.IsLoading)
                _marriageOffers = _marriageOfferChunks?.Count > 0
                    ? JsonConvert.DeserializeObject<Dictionary<string, ReignPlayerMarriageOffer>>(ReignSavePayloadCodec.Decode(_marriageOfferChunks))
                        ?? new Dictionary<string, ReignPlayerMarriageOffer>()
                    : new Dictionary<string, ReignPlayerMarriageOffer>();
        }

        private void RestoreMarriageOffers()
        {
            string campaign = ReignCampaignIdentity.CurrentCampaignId();
            foreach (var offer in _marriageOffers.Values.Where(o => o.CampaignId == campaign && o.TimelineId != WeddingTimeline))
            {
                offer.TimelineId = WeddingTimeline;
                offer.DirectorReported = true;
                if (offer.State == "courier") offer.LetterSent = false;
                if (offer.State == "reply_courier") offer.ResponseSent = false;
                if (!offer.MeetingSent) offer.MeetingDispatched = false;
            }
            _marriageMailInFlight.Clear();
        }

        internal IEnumerable<ReignPlayerMarriageOffer> OpenMarriageOffers(IEnumerable<Hero> contacts)
        {
            var ids = new HashSet<string>(contacts.Where(h => h != null).Select(h => h.StringId));
            return _marriageOffers.Values.Where(o => o.Open && o.TimelineId == WeddingTimeline && ids.Contains(o.LeaderId)).ToList();
        }

        internal bool OwnsWeddingAttendance(string id)
            => Wedding(id)?.State == WeddingState.Ceremony
                || _marriageOffers.TryGetValue(id, out var offer) && offer.Open && offer.MeetingArrived && !offer.MeetingFinished;

        internal bool PreparePlayerMarriageOffer(string id, Hero foreign, Hero playerMatch, Hero leader)
        {
            if (_marriageOffers.ContainsKey(id)) return true;
            if (_marriageOffers.Values.Any(o => o.Outstanding) || leader?.Clan == null || leader.Clan.Leader != leader
                || foreign?.Clan != leader.Clan || playerMatch?.Clan != Clan.PlayerClan || leader.Clan == Clan.PlayerClan
                || !SuitableWeddingPair(playerMatch, foreign)) return false;
            var terms = QuotePlayerMarriage(id, playerMatch, foreign, leader);
            if (terms == null) return false;
            var offer = new ReignPlayerMarriageOffer
            {
                Id = id, LeaderId = leader.StringId, PlayerMatchId = playerMatch.StringId, ProspectId = foreign.StringId,
                OfferedDay = WeddingNow, CampaignId = ReignCampaignIdentity.CurrentCampaignId(), TimelineId = WeddingTimeline, Terms = terms
            };
            _marriageOffers.Add(id, offer);
            RecordProposalHistory(offer, "proposed", leader.Name + " proposed " + DescribeMarriageOffer(offer));
            return true;
        }

        internal ActionAgreement QuotePlayerMarriage(string id, Hero playerMatch, Hero prospect, Hero leader, bool automatic = true)
        {
            long adjustment = 0;
            if (automatic)
            {
                var appraisal = new MarriageBarterable(leader, leader.PartyBelongedTo?.Party, playerMatch, prospect);
                adjustment = -(long)appraisal.GetUnitValueForFaction(leader.Clan);
            }
            int net = ArrangedMarriagePolicy.PlayerQuote(Clan.PlayerClan.Tier, leader.Clan.Tier, adjustment);

            Hero player = Hero.MainHero;
            ActionAgreement Package(ActionClause asset = null, int value = 0)
            {
                var terms = new ActionAgreement { Id = id, PlayerId = player.StringId, NpcId = leader.StringId, CreatedDay = WeddingNow };
                int remaining = net - value;
                if (remaining != 0)
                    terms.Clauses.Add(ReignWeddingBargain.Asset("give.gold", remaining > 0 ? player : leader,
                        remaining > 0 ? leader : player, Math.Abs(remaining)));
                if (asset != null) terms.Clauses.Add(asset);
                return terms;
            }

            bool Valid(ActionAgreement candidate)
            {
                try { ReignWeddingBargain.Check(candidate, playerMatch, prospect); return true; }
                catch (InvalidOperationException) { return false; }
            }

            var cash = Package();
            if (!automatic) return cash;
            if (net == 0 && Valid(cash)) return cash;
            Hero payer = net >= 0 ? player : leader;
            Hero payee = payer == player ? leader : player;
            var best = Valid(cash) ? cash : null;
            long bestCost = best == null ? long.MaxValue : Math.Abs(net);
            foreach (string kind in new[] { "item", "prisoner", "settlement" })
            foreach (var row in ReignActionEntities.Find(kind, payer.StringId).Where(x => x.Available))
            {
                try
                {
                    var asset = ReignWeddingBargain.Asset("give." + kind, payer, payee, 1, row.Id);
                    foreach (var argument in row.Arguments) asset.Arguments[argument.Key] = argument.Value;
                    long unit = ReignActionQuotes.Value(asset, out _);
                    if (unit <= 0 || unit > int.MaxValue) continue;
                    int quantity = kind == "item" ? (int)Math.Max(1, Math.Min(row.Quantity, Math.Abs(net) / unit)) : 1;
                    asset.Arguments["quantity"] = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    long value = unit * quantity;
                    if (value > int.MaxValue - Math.Abs(net)) continue;
                    // Preserve the native net appraisal and prefer less cash. A
                    // reverse payment costs more, as does substituting an outsized fief.
                    long cost = Math.Abs(Math.Abs(net) - value) + Math.Max(0, value - Math.Abs(net));
                    if (cost >= bestCost) continue;
                    var candidate = Package(asset, (payer == player ? 1 : -1) * (int)value);
                    if (Valid(candidate)) { best = candidate; bestCost = cost; }
                }
                catch (InvalidOperationException) { }
                catch (OverflowException) { }
            }
            return best;
        }

        internal static string DescribeMarriageTerms(ActionAgreement terms)
        {
            if (terms?.Clauses.Count > 0 != true) return "No payment or transfer.";
            return string.Join("; ", terms.Clauses.Select(c =>
            {
                string from = FindHero(c.ActorId)?.Name?.ToString() ?? c.ActorId;
                string to = FindHero(c.RecipientId)?.Name?.ToString() ?? c.RecipientId;
                string asset = ReignActionService.Arg(c, "assetId");
                string quantity = ReignActionService.Arg(c, "quantity", "1");
                if (c.ActionId == "give.gold") return from + " pays " + to + " " + quantity + " denars";
                if (c.ActionId == ReignWeddingBargain.ReleasePrisoner) return from + " frees " + (FindHero(asset)?.Name?.ToString() ?? asset);
                if (c.ActionId == "give.settlement") asset = Settlement.Find(asset)?.Name?.ToString() ?? asset;
                if (c.ActionId == "give.item") asset = ReignObjectResolver.FindItem(asset)?.Name?.ToString() ?? asset;
                return from + " transfers " + quantity + " " + asset + " to " + to;
            })) + ".";
        }

        internal static string DescribeMarriageOffer(ReignPlayerMarriageOffer offer)
        {
            var first = FindHero(offer.PlayerMatchId);
            var second = FindHero(offer.ProspectId);
            var receivingClan = first == null || second == null ? null
                : TaleWorlds.CampaignSystem.Campaign.Current.Models.MarriageModel.GetClanAfterMarriage(first, second);
            var moving = first?.Clan != receivingClan ? first : second?.Clan != receivingClan ? second : null;
            string placement = moving != null && receivingClan != null
                ? moving.Name + " will join " + receivingClan.Name + ". " : "";
            return (first?.Name?.ToString() ?? offer.PlayerMatchId) + " and " + (second?.Name?.ToString() ?? offer.ProspectId)
                + " in marriage. " + placement + DescribeMarriageTerms(offer.Terms);
        }

        private void RecordProposalHistory(ReignPlayerMarriageOffer offer, string phase, string summary)
        {
            string key = offer.Id + ":" + phase;
            if (_marriageHistory.ContainsKey(key)) return;
            _marriageHistory[key] = new JObject
            {
                ["offerId"] = offer.Id, ["phase"] = phase, ["day"] = WeddingNow, ["summary"] = summary,
                ["audience"] = new JArray(offer.PlayerMatchId, offer.ProspectId)
            };
            ReignWorldHistoryCampaignBehavior.Instance?.RecordReignSystemEvent("arranged_marriage_offer", phase, "relationships", offer.Id,
                summary, "participants", offer.LeaderId, "", new JObject { ["offerId"] = offer.Id },
                new JArray(offer.PlayerMatchId, offer.ProspectId));
        }

        internal string PrivateMarriageHistory(Hero speaker)
        {
            if (speaker == null || speaker == Hero.MainHero) return "";
            return string.Join("\n", _marriageHistory.Values.Where(row => (row["audience"] as JArray)?.Values<string>().Contains(speaker.StringId) == true)
                .OrderByDescending(row => row.Value<double>("day")).Take(8).OrderBy(row => row.Value<double>("day"))
                .Select(row => row.Value<string>("summary")));
        }

        internal IEnumerable<Tuple<Settlement, double>> MeetingTowns(ReignPlayerMarriageOffer offer, bool mail)
        {
            var prospect = FindHero(offer.ProspectId);
            var leader = FindHero(offer.LeaderId);
            var place = prospect?.CurrentSettlement ?? prospect?.HomeSettlement;
            var origin = prospect?.PartyBelongedTo?.GetPosition2D ?? place?.GetPosition2D;
            if (prospect == null || leader == null || !origin.HasValue || offer.MeetingTown.Length != 0) yield break;
            double courier = mail ? ReignServerClient.ActionDeliveryDays(Hero.MainHero, leader, WeddingNow) : 0;
            foreach (var town in Settlement.All.Where(t => SafeWeddingTown(t, Clan.PlayerClan, prospect.Clan)).OrderBy(t => t.Name.ToString()))
            {
                double days = courier + Math.Max(1d / 24d, Math.Sqrt(origin.Value.DistanceSquared(town.GetPosition2D)) / 96d);
                if (WeddingNow + days < offer.ExpiresDay) yield return Tuple.Create(town, days);
            }
        }

        internal string AnswerMarriageOffer(string id, Hero contact, string response, bool mail, string venue = "")
        {
            if (!_marriageOffers.TryGetValue(id, out var offer) || !offer.Open || contact?.StringId != offer.LeaderId
                || contact.Clan?.Leader != contact || offer.TimelineId != WeddingTimeline)
                return "This offer is no longer open with this clan leader.";
            if (response == "meeting")
            {
                var town = MeetingTowns(offer, mail).FirstOrDefault(row => row.Item1.StringId == venue);
                if (town == null) return "The prospect cannot safely arrive before the deadline.";
                offer.MeetingTown = venue;
                offer.MeetingDispatch = WeddingNow;
                offer.ArrivalDay = WeddingNow + town.Item2;
                offer.MeetingSent = !mail;
                return "The meeting was requested at " + town.Item1.Name + ".";
            }
            if (response != "accept" && response != "refuse") return "The reply is unavailable.";
            offer.Response = response;
            offer.ResponseDay = WeddingNow;
            if (mail) offer.State = "reply_courier";
            else FinalizeMarriageOfferAnswer(offer);
            return mail ? "The signed reply is queued for the courier." : offer.State == "accepted" ? "The match is agreed." : "The offer is closed.";
        }

        private void FinalizeMarriageOfferAnswer(ReignPlayerMarriageOffer offer)
        {
            ReignNpcLivesBehavior.Instance?.EndWeddingAttendance(offer.Id);
            var leader = FindHero(offer.LeaderId);
            var first = FindHero(offer.PlayerMatchId);
            var second = FindHero(offer.ProspectId);
            bool correctLeader = leader != null && leader.Clan?.Leader == leader && second?.Clan == leader.Clan;
            bool accepted = offer.Response == "accept";
            ReignWedding wedding = null;
            bool booked = accepted && correctLeader && first?.Clan == Clan.PlayerClan
                && RequestWedding(offer.Id, first, second, offer.Terms, offer.LeaderId, ArrangedMarriagePolicy.SuccessRelation, out wedding);
            if (booked) wedding.DirectorAction = offer.DirectorOffer && !offer.DirectorReported;
            offer.State = booked ? "accepted" : accepted ? "unavailable" : "refused";
            if (!accepted && leader != null) ApplyPlayerMarriageRelation(offer.LeaderId, ArrangedMarriagePolicy.PlayerRefusalRelation);
            RecordProposalHistory(offer, offer.State, Hero.MainHero.Name + (booked ? " accepted the proposed match. " :
                accepted ? " accepted, but the match is no longer available. " : " refused the proposed match. ") + DescribeMarriageOffer(offer));
        }

        internal void ObserveMarriageLetters(IEnumerable<ReignLetter> letters)
        {
            foreach (var letter in letters)
            {
                if (!_marriageOffers.TryGetValue(letter.Reason ?? "", out var offer) || offer.TimelineId != WeddingTimeline) continue;
                if (letter.Source == "reign_marriage_offer" && offer.State == "courier" && letter.SenderId == offer.LeaderId
                    && letter.RecipientId == Hero.MainHero.StringId)
                {
                    offer.DeliveredDay = letter.DeliveredDay;
                    offer.ExpiresDay = offer.DeliveredDay + ArrangedMarriagePolicy.PlayerReplyDays;
                    offer.State = "open";
                }
                if (letter.RecipientId != offer.LeaderId || letter.SenderId != Hero.MainHero.StringId) continue;
                if (letter.Source == "reign_marriage_answer" && offer.State == "reply_courier") FinalizeMarriageOfferAnswer(offer);
                if (letter.Source == "reign_marriage_meeting" && offer.Open && !offer.MeetingFinished)
                    offer.MeetingSent = true;
            }
        }

        private void TickMarriageOffers()
        {
            bool maySend = DateTime.UtcNow >= _marriageRetryUtc;
            foreach (var offer in _marriageOffers.Values.ToList())
            {
                if (offer.TimelineId != WeddingTimeline) continue;
                if (offer.State == "open" && !offer.Open)
                {
                    offer.State = "expired";
                    ReignNpcLivesBehavior.Instance?.EndWeddingAttendance(offer.Id);
                }
                if (offer.State == "courier" && !offer.LetterSent && maySend)
                    _ = SendMarriageMail(offer, "reign_marriage_offer", FindHero(offer.LeaderId), Hero.MainHero,
                        "I propose " + DescribeMarriageOffer(offer) + "\n\nThis offer is open for thirty days from delivery.", offer.OfferedDay);
                if (offer.State == "reply_courier" && !offer.ResponseSent && maySend)
                    _ = SendMarriageMail(offer, "reign_marriage_answer", Hero.MainHero, FindHero(offer.LeaderId),
                        offer.Response == "accept" ? "I accept the arranged marriage and its agreed terms." : "I refuse the arranged marriage.", offer.ResponseDay);
                if (!offer.Open || offer.MeetingTown.Length == 0 || offer.MeetingFinished || offer.MeetingArrived) continue;
                if (!offer.MeetingDispatched && !offer.MeetingSent && maySend)
                    _ = SendMarriageMail(offer, "reign_marriage_meeting", Hero.MainHero, FindHero(offer.LeaderId),
                        "I would like to meet the prospect at " + Settlement.Find(offer.MeetingTown)?.Name + ".", offer.MeetingDispatch);
                if (!offer.MeetingSent || WeddingNow < offer.ArrivalDay) continue;
                var town = Settlement.Find(offer.MeetingTown);
                var prospect = FindHero(offer.ProspectId);
                string reason = !SafeWeddingTown(town, Clan.PlayerClan, prospect?.Clan)
                    ? "The venue is no longer safe."
                    : ReignNpcLivesBehavior.Instance?.BeginWeddingAttendance(offer.Id, prospect, town) ?? "The prospect is unavailable.";
                if (reason.Length != 0)
                {
                    offer.MeetingFinished = true;
                    InformationManager.DisplayMessage(new InformationMessage("The proposed meeting was called off. " + reason));
                }
                else
                {
                    offer.MeetingArrived = true;
                    InformationManager.DisplayMessage(new InformationMessage(prospect.Name + " has arrived at " + town.Name + " for your meeting."));
                }
            }
        }

        internal IEnumerable<Tuple<Hero, Hero>> PlayerMarriageCandidates(Hero leader)
        {
            if (leader?.Clan == null || leader.Clan.Leader != leader || leader.Clan == Clan.PlayerClan) yield break;
            var model = TaleWorlds.CampaignSystem.Campaign.Current.Models.MarriageModel;
            if (!model.ShouldNpcMarriageBetweenClansBeAllowed(Clan.PlayerClan, leader.Clan)) yield break;
            foreach (var first in new[] { Hero.MainHero }.Concat(Clan.PlayerClan.Heroes).Where(h => h != null && h.IsAlive && !h.IsChild).Distinct())
                foreach (var other in leader.Clan.Heroes.Where(h => h != null && h.IsAlive && !h.IsChild))
                    if (SuitableWeddingPair(first, other)) yield return Tuple.Create(first, other);
        }

        internal bool CanAppealWedding(string id, Hero spouse)
        {
            var match = Wedding(id);
            return spouse != null && spouse != Hero.MainHero && spouse.IsAlive && !spouse.IsPrisoner && match != null
                && spouse.CurrentSettlement?.StringId == match.VenueId && match == AvailableWedding(Settlement.CurrentSettlement ?? TaleWorlds.CampaignSystem.Party.MobileParty.MainParty?.CurrentSettlement)
                && (match.FirstId == spouse.StringId || match.SecondId == spouse.StringId)
                && !match.Appeals.ContainsKey(spouse.StringId) && match.PendingAppeal.Length == 0
                && ReignBeta.UI.ReignSocialEventScreenManager.ActiveViewModel?.ActiveWeddingId == id;
        }

        private static bool IsMarriageAction(ActionClause c) => c.ActionId == "marriage.propose" || c.ActionId == "marriage.appeal";
        internal List<string> CheckMarriageAction(ActionAgreement a)
        {
            var errors = new List<string>();
            var actions = a.Clauses.Where(IsMarriageAction).ToList();
            if (actions.Count != 1 || actions[0].ActorId != a.PlayerId || actions[0].RecipientId != a.NpcId
                || a.Origin != ActionOrigin.Conversation || a.PlayerId != Hero.MainHero?.StringId || a.AdvancePayment
                || a.Clauses.Any(c => c.Installments != 1 || c.Timing != TermTiming.Immediate || c.ConditionKind.Length != 0))
            {
                errors.Add("This marriage action must be one exact player proposal or appeal.");
                return errors;
            }
            var action = actions[0];
            var target = FindHero(a.NpcId);
            if (action.ActionId == "marriage.appeal")
            {
                var match = Wedding(ReignActionService.Arg(action, "weddingId"));
                if (a.Clauses.Count != 1 || (!CanAppealWedding(match?.Id ?? "", target) && match?.PendingAppeal != a.Id))
                    errors.Add("This prospective spouse is unavailable for an appeal in this ceremony.");
                return errors;
            }
            var first = FindHero(ReignActionService.Arg(action, "playerMatchId"));
            var other = FindHero(ReignActionService.Arg(action, "prospectId"));
            if (first?.Clan != Clan.PlayerClan || target?.Clan == null || target.Clan.Leader != target
                || target.Clan == Clan.PlayerClan || other?.Clan != target.Clan || !SuitableWeddingPair(first, other))
            {
                errors.Add("The named couple or clan leader is no longer eligible.");
                return errors;
            }
            try { ReignWeddingBargain.Check(ConsiderationOf(a), first, other); }
            catch (Exception ex) { errors.Add(ex.Message); }
            return errors;
        }

        private static ActionAgreement ConsiderationOf(ActionAgreement proposal)
        {
            var terms = ReignActionService.Copy(proposal);
            terms.Clauses.RemoveAll(IsMarriageAction);
            return terms;
        }

        internal void RecordPlayerMarriageProposal(ActionAgreement proposal)
        {
            var action = proposal.Clauses.FirstOrDefault(IsMarriageAction);
            if (action == null) return;
            if (action.ActionId == "marriage.appeal")
            {
                var match = Wedding(ReignActionService.Arg(action, "weddingId"));
                if (match == null || match.State != WeddingState.Ceremony)
                    throw new InvalidOperationException("This wedding is no longer active.");
                match.PendingAppeal = proposal.Id;
                match.AppealUntilUtc = DateTime.UtcNow.AddSeconds(90);
                return;
            }
            var offer = new ReignPlayerMarriageOffer { Id = proposal.Id, LeaderId = proposal.NpcId,
                PlayerMatchId = ReignActionService.Arg(action, "playerMatchId"), ProspectId = ReignActionService.Arg(action, "prospectId"),
                Terms = ConsiderationOf(proposal) };
            RecordProposalHistory(offer, "proposed", Hero.MainHero.Name + " proposed " + DescribeMarriageOffer(offer));
        }

        internal string ReceiveMarriageDecision(ActionAgreement proposal, string answer)
        {
            var clause = proposal.Clauses.First(IsMarriageAction);
            bool accepted = answer == "accept";
            if (clause.ActionId == "marriage.appeal")
            {
                var match = Wedding(ReignActionService.Arg(clause, "weddingId"));
                if (match == null || match.State != WeddingState.Ceremony || match.TimelineId != WeddingTimeline
                    || match.PendingAppeal != proposal.Id || DateTime.UtcNow >= match.AppealUntilUtc
                    || match.Appeals.ContainsKey(proposal.NpcId))
                    return "This appeal no longer belongs to an active wedding.";
                match.PendingAppeal = "";
                if (!accepted && answer != "reject") return "The prospective spouse did not decide.";
                match.Appeals[proposal.NpcId] = answer;
                if (accepted)
                {
                    var spouse = FindHero(proposal.NpcId);
                    var other = match.FirstId == proposal.NpcId ? match.SecondId : match.FirstId;
                    RecordProposalHistory(new ReignPlayerMarriageOffer { Id = match.Id, LeaderId = proposal.NpcId,
                        PlayerMatchId = proposal.NpcId, ProspectId = other }, "withdrawn",
                        spouse?.Name + " withdrew from the arranged wedding.");
                    CancelWedding(match, "A prospective spouse withdrew.");
                }
                return accepted ? "The prospective spouse called off the wedding." : "The prospective spouse chose to continue.";
            }

            if (!accepted && answer != "reject") return "The clan leader did not accept or refuse this match.";
            var offer = new ReignPlayerMarriageOffer { Id = proposal.Id, LeaderId = proposal.NpcId,
                PlayerMatchId = ReignActionService.Arg(clause, "playerMatchId"), ProspectId = ReignActionService.Arg(clause, "prospectId"),
                Terms = ConsiderationOf(proposal) };
            var errors = CheckMarriageAction(proposal);
            bool scheduled = accepted && errors.Count == 0 && RequestWedding(offer.Id, FindHero(offer.PlayerMatchId),
                FindHero(offer.ProspectId), offer.Terms, offer.LeaderId, ArrangedMarriagePolicy.SuccessRelation, out _);
            string summary = scheduled ? " accepted the arranged match. " : accepted ? " accepted, but the match is no longer available. " : " refused the arranged match. ";
            RecordProposalHistory(offer, scheduled ? "accepted" : accepted ? "unavailable" : "refused",
                FindHero(offer.LeaderId)?.Name + summary + DescribeMarriageOffer(offer));
            return scheduled ? "The arranged match is agreed. Choose wedding plans when ready."
                : accepted ? "The agreed match is no longer available." : "The clan leader refused the arranged match.";
        }

        internal JObject ExerciseWeddingFixture(JObject args, string operationId)
        {
            if (!WeddingFeatureReady || args == null || args.Value<string>("profile") == null)
                throw new InvalidOperationException("The wedding fixture requires its exact profile.");
            string id = "wedding_fixture_" + operationId;
            var evidence = new JObject
            {
                ["schema"] = "reign-arranged-wedding-native-v1", ["offerId"] = id,
                ["campaignId"] = ReignCampaignIdentity.CurrentCampaignId(), ["timelineId"] = WeddingTimeline,
                ["worldDay"] = WeddingNow, ["settlementId"] = Hero.MainHero.CurrentSettlement?.StringId ?? ""
            };
            string profile = args.Value<string>("profile");
            if (profile == "discover" && args.Count == 1)
            {
                var nearby = Hero.MainHero.CurrentSettlement;
                var leaders = Clan.All.Where(c => c != Clan.PlayerClan && !c.IsEliminated && !c.IsBanditFaction
                    && c.Leader?.IsAlive == true && !c.Leader.IsPrisoner)
                    .OrderBy(c => nearby != null && c.Leader.CurrentSettlement == nearby ? 0 : 1)
                    .ThenBy(c => c.StringId, StringComparer.Ordinal);
                var candidates = leaders.SelectMany(c => PlayerMarriageCandidates(c.Leader)
                    .Where(pair => pair.Item2 != c.Leader)
                    .Select(pair => new JObject
                    {
                        ["playerMatchId"] = pair.Item1.StringId, ["playerMatchName"] = pair.Item1.Name.ToString(),
                        ["prospectId"] = pair.Item2.StringId, ["prospectName"] = pair.Item2.Name.ToString(),
                        ["leaderId"] = c.Leader.StringId, ["leaderName"] = c.Leader.Name.ToString(),
                        ["tierDifference"] = c.Tier - Clan.PlayerClan.Tier
                    })).Take(16);
                evidence["candidates"] = new JArray(candidates);
                return evidence;
            }
            if (profile == "stage" && args.Count == 4 && new[] { "playerMatchId", "prospectId", "leaderId" }
                .All(key => args[key]?.Type == JTokenType.String && args.Value<string>(key).Length <= 128))
            {
                var first = FindHero(args.Value<string>("playerMatchId"));
                var prospect = FindHero(args.Value<string>("prospectId"));
                var leader = FindHero(args.Value<string>("leaderId"));
                if (first == null || prospect == null || leader == null
                    || !PlayerMarriageCandidates(leader).Any(pair => pair.Item1 == first && pair.Item2 == prospect))
                    throw new InvalidOperationException("The exact requested marriage pair is not eligible for an ordinary offer.");
                if (_marriageOffers.TryGetValue(id, out var existing)
                    && (existing.PlayerMatchId != first.StringId || existing.ProspectId != prospect.StringId || existing.LeaderId != leader.StringId))
                    throw new InvalidOperationException("That fixture operation already binds a different couple or leader.");
                if (!PreparePlayerMarriageOffer(id, prospect, first, leader))
                    throw new InvalidOperationException("The ordinary offer evaluator found no feasible package, or another letter is already open.");
                evidence["preparation"] = "normal_offer_evaluation";
            }
            else if (profile != "observe" || args.Count != 1)
                throw new InvalidOperationException("Choose an exact discover, stage or observe profile.");
            if (!_marriageOffers.TryGetValue(id, out var offer))
                throw new InvalidOperationException("The exact offer is absent from this save.");
            var wedding = Wedding(id);
            JObject Observe(Hero hero) => hero == null ? null : new JObject
            {
                ["id"] = hero.StringId, ["name"] = hero.Name.ToString(), ["clanId"] = hero.Clan?.StringId ?? "",
                ["spouseId"] = hero.Spouse?.StringId ?? "", ["gold"] = hero.Gold, ["prisoner"] = hero.IsPrisoner,
                ["settlementId"] = hero.CurrentSettlement?.StringId ?? "", ["partyId"] = hero.PartyBelongedTo?.StringId ?? "",
                ["governorOfId"] = hero.GovernorOf?.Settlement?.StringId ?? "", ["relationToPlayer"] = hero.GetRelation(Hero.MainHero)
            };
            evidence["offer"] = JObject.FromObject(offer);
            if (wedding != null)
            {
                evidence["wedding"] = JObject.FromObject(wedding);
                evidence["guests"] = new JArray(wedding.Guests.Take(80).Select(guest => Observe(FindHero(guest))));
                evidence["guestCount"] = wedding.Guests.Count;
            }
            evidence["player"] = Observe(Hero.MainHero);
            evidence["leader"] = Observe(FindHero(offer.LeaderId));
            evidence["playerMatch"] = Observe(FindHero(offer.PlayerMatchId));
            evidence["prospect"] = Observe(FindHero(offer.ProspectId));
            return evidence;
        }

        private async Task SendMarriageMail(ReignPlayerMarriageOffer offer, string source, Hero sender, Hero recipient, string text, double dispatch)
        {
            string key = offer.Id + ":" + source;
            if (sender == null || recipient == null || !_marriageMailInFlight.Add(key)) return;
            string timeline = WeddingTimeline;
            int generation = ReignSaveSyncCoordinator.CampaignGeneration;
            bool sent = await ReignServerClient.SendMarriageLetterAsync(source, offer.Id, sender, recipient, text, dispatch).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() =>
            {
                _marriageMailInFlight.Remove(key);
                if (!ReferenceEquals(Instance, this) || timeline != WeddingTimeline
                    || generation != ReignSaveSyncCoordinator.CampaignGeneration) return;
                if (!sent) { _marriageRetryUtc = DateTime.UtcNow.AddSeconds(10); return; }
                if (source == "reign_marriage_offer") offer.LetterSent = true;
                if (source == "reign_marriage_answer") offer.ResponseSent = true;
                if (source == "reign_marriage_meeting") offer.MeetingDispatched = true;
            }).ConfigureAwait(false);
        }
    }
}

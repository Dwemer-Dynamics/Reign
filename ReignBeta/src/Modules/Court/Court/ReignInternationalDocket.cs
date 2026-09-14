using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Court
{
    public sealed partial class ReignRulerDocketState
    {
        public string InternationalWorldJson { get; set; } = "{}";
        public string InternationalProfilesJson { get; set; } = "{}";
        public string InternationalPowerJson { get; set; } = "{}";
        public List<string> InternationalQueueJson { get; set; } = new List<string>();
        public List<string> InternationalReceiptsJson { get; set; } = new List<string>();
    }

    public sealed partial class ReignCourtCampaignBehavior
    {
        private bool _internationalRefreshInFlight;
        private DateTime _internationalRefreshAfterUtc = DateTime.MinValue;
        private readonly HashSet<string> _internationalPostingInFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _internationalReferralInFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool HasPendingInternationalCourtLifeWork => _internationalRefreshInFlight || _internationalPostingInFlight.Count > 0
            || _internationalReferralInFlight.Count > 0 || (EnsureRulerDocketState().InternationalReceiptsJson?.Count ?? 0) > 0;

        public async Task<bool> PrepareInternationalFixtureAsync()
        {
            ReignRulerDocketState state = EnsureRulerDocketState();
            JObject world = ReignServerClient.BuildDiplomacyWorldSnapshot();
            await RefreshInternationalCourtAsync(world, state).ConfigureAwait(false);
            return !string.IsNullOrWhiteSpace(state.InternationalWorldJson) && state.InternationalWorldJson != "{}";
        }

        public ReignCourtLifeMatter TryCreateInternationalTemplateFixture(string templateId, string campaignId,
            string timelineId, int day, int slot, out string reason)
        {
            reason = string.Empty;
            ReignInternationalTemplate template = ReignInternationalDocketCatalog.Find(templateId);
            Kingdom ours = Clan.PlayerClan?.Kingdom;
            if (template == null || ours?.Leader != Hero.MainHero || CurrentCapital == null)
            { reason = "A known template and player royal capital are required."; return null; }
            foreach (Kingdom origin in Kingdom.All.Where(x => InternationalOriginEligible(x, ours)))
            {
                if (!InternationalTemplateEligible(template, origin, ours)) continue;
                ReignCourtLifeMatter matter = BuildInternationalMatter(template, origin, campaignId, timelineId, day, slot, null);
                if (matter != null) return matter;
            }
            reason = "No actual eligible kingdom, ambassador, noble, captive, treaty, or strategic/personality prerequisites matched this template.";
            return null;
        }

        public ReignCourtLifeMatter TryCreateInternationalMatterForSlot(string campaignId, string timelineId, int day, int slot)
        {
            Kingdom ours = Clan.PlayerClan?.Kingdom;
            Settlement capital = CurrentCapital;
            if (ours?.Leader != Hero.MainHero || capital == null) return null;
            ReignRulerDocketState state = EnsureRulerDocketState();
            string seed = campaignId + "|" + timelineId + "|international|" + day + "|" + slot;
            ReignNobleMatterSeverity severity = ReignInternationalDocketRules.SeverityForRoll(ReignCourtLifeRules.StableRoll(seed + "|severity", 100));
            state.InternationalQueueJson = state.InternationalQueueJson ?? new List<string>();
            foreach (string encoded in state.InternationalQueueJson)
            {
                JObject queued = ParseObject(encoded);
                if (queued.Value<int?>("severityRank") != (int)severity) continue;
                string id = queued.Value<string>("matterId");
                if (state.CourtLifeMatters.Any(x => x.MatterId == id)) continue;
                Kingdom origin = Kingdom.All.FirstOrDefault(x => x?.StringId == queued.Value<string>("foreignKingdomId"));
                bool sovereignProposal = queued.Value<string>("kind") == "proposal";
                if (sovereignProposal ? origin?.Leader?.IsAlive != true || origin == ours || origin.IsEliminated : !InternationalOriginEligible(origin, ours)) continue;
                ReignCourtLifeMatter imported = BuildInternationalMatter(null, origin, campaignId, timelineId, day, slot, queued);
                if (imported != null) return imported;
            }
            List<ReignInternationalTemplate> templates = ReignInternationalDocketCatalog.ForSeverity(severity)
                .OrderBy(x => ReignCourtLifeRules.StableRoll(seed + "|" + x.Id, int.MaxValue)).ToList();
            List<Kingdom> origins = Kingdom.All.Where(x => InternationalOriginEligible(x, ours))
                .Where(x => !state.CourtLifeMatters.Any(m => m.Source == ReignDocketSource.International && m.IsPending && m.KingdomId == x.StringId))
                .OrderBy(x => ReignCourtLifeRules.StableRoll(seed + "|" + x.StringId, int.MaxValue)).ToList();
            foreach (ReignInternationalTemplate template in templates)
            foreach (Kingdom origin in origins)
            {
                if (!InternationalTemplateEligible(template, origin, ours)) continue;
                ReignCourtLifeMatter matter = BuildInternationalMatter(template, origin, campaignId, timelineId, day, slot, null);
                if (matter != null) return matter;
            }
            return null;
        }

        private bool InternationalOriginEligible(Kingdom origin, Kingdom ours)
        {
            return origin != null && origin != ours && !origin.IsEliminated && origin.Leader?.IsAlive == true
                && !ours.IsAtWarWith(origin) && (_foreignAmbassadors.Any(x => x.IsActive && x.OriginKingdomStringId == origin.StringId)
                    || EligibleForeignAmbassadorCandidates(origin).Count > 0);
        }

        private bool InternationalTemplateEligible(ReignInternationalTemplate template, Kingdom origin, Kingdom ours)
        {
            ReignRulerDocketState state = EnsureRulerDocketState();
            JObject world = ParseObject(state.InternationalWorldJson);
            JObject originRow = (world["kingdoms"] as JArray)?.OfType<JObject>().FirstOrDefault(x => x.Value<string>("kingdomId") == origin.StringId);
            if (template.Remedy == "sign_trade_agreement" && (world["agreements"] as JArray ?? new JArray()).OfType<JObject>().Any(x =>
                x.Value<string>("kind") == "trade_agreement" && ((x.Value<string>("actorKingdomId") == origin.StringId && x.Value<string>("targetKingdomId") == ours.StringId)
                    || (x.Value<string>("targetKingdomId") == origin.StringId && x.Value<string>("actorKingdomId") == ours.StringId)))) return false;
            if (template.BorderOnly && !(originRow?["borderKingdomIds"] as JArray ?? new JArray()).Values<string>().Contains(ours.StringId)) return false;
            if (template.Extortion)
            {
                JObject traits = ParseObject(state.InternationalProfilesJson)[origin.StringId] as JObject;
                JObject power = ParseObject(state.InternationalPowerJson);
                double ownPower = power.Value<double?>(origin.StringId) ?? 0;
                double playerPower = power.Value<double?>(ours.StringId) ?? 0;
                if (traits == null || ownPower <= 0 || playerPower <= 0 || !ReignInternationalDocketRules.ExtortionEligible(ownPower, playerPower,
                    (int)(traits.Value<double?>("boldness") ?? 50), (int)(traits.Value<double?>("honor") ?? 50))) return false;
            }
            if (template.Remedy == "gift" && origin.Leader.Gold < template.Gold) return false;
            if (template.RequiresCaptive && FindInternationalCaptive(origin) == null) return false;
            if (template.RequiresCaptive && template.Id.Contains("ransom") && origin.Leader.Gold < template.Gold) return false;
            if (template.Id.EndsWith("treaty-redress", StringComparison.Ordinal) || template.Id.EndsWith("broken-guarantee", StringComparison.Ordinal))
            {
                if (!(world["agreements"] as JArray ?? new JArray()).OfType<JObject>().Any(x =>
                    (x.Value<string>("actorKingdomId") == origin.StringId && x.Value<string>("targetKingdomId") == ours.StringId)
                    || (x.Value<string>("targetKingdomId") == origin.StringId && x.Value<string>("actorKingdomId") == ours.StringId))) return false;
            }
            return true;
        }

        private Hero FindInternationalCaptive(Kingdom origin)
        {
            return Hero.AllAliveHeroes.FirstOrDefault(x => x != null && x.IsAlive && x.IsLord && !x.IsChild && x.IsPrisoner
                && x.Clan?.Kingdom == origin && x.PartyBelongedToAsPrisoner?.Owner == Hero.MainHero);
        }

        private ReignCourtLifeMatter BuildInternationalMatter(ReignInternationalTemplate template, Kingdom origin,
            string campaign, string timeline, int day, int slot, JObject queued)
        {
            Kingdom ours = Clan.PlayerClan.Kingdom;
            string id = queued?.Value<string>("matterId") ?? "international_" + ReignCourtTerms.Hash(campaign + "|" + timeline + "|" + day + "|" + slot).Substring(0, 24);
            bool proposal = queued?.Value<string>("kind") == "proposal";
            bool constructive = proposal || template?.Constructive == true || queued?.Value<bool?>("constructive") == true;
            bool wartimeProposal = proposal && ours.IsAtWarWith(origin);
            Hero domestic = null;
            Hero foreignLord = null;
            if (!constructive && template?.Extortion != true)
            {
                domestic = (queued?["domesticLordIds"] as JArray)?.Values<string>().Select(FindHero).FirstOrDefault(x => x?.IsAlive == true && !x.IsPrisoner && x.PartyBelongedTo == null)
                    ?? Hero.AllAliveHeroes.Where(x => x != null && x.IsAlive && x.IsActive && x.IsLord && !x.IsChild && !x.IsPrisoner
                        && x != Hero.MainHero && x.Clan?.Kingdom == ours && x.PartyBelongedTo == null && x.GovernorOf == null)
                        .OrderBy(x => StableCourtOrder(x.StringId + "|international|" + slot, day)).FirstOrDefault();
                foreignLord = (queued?["foreignLordIds"] as JArray)?.Values<string>().Select(FindHero).FirstOrDefault(x => x?.IsAlive == true)
                    ?? origin.Clans.SelectMany(x => x.Heroes).Where(x => x.IsAlive && x.IsLord && !x.IsChild && x != origin.Leader)
                        .OrderBy(x => StableCourtOrder(x.StringId + "|international|" + slot, day)).FirstOrDefault();
                if (domestic == null || foreignLord == null) return null;
            }
            ForeignAmbassadorPosting posting = _foreignAmbassadors.FirstOrDefault(x => x.IsActive && x.OriginKingdomStringId == origin.StringId);
            ReignNobleMatterSeverity severity = template?.Severity ?? (ReignNobleMatterSeverity)(queued?.Value<int?>("severityRank") ?? 0);
            var matter = new ReignCourtLifeMatter
            {
                MatterId = id, CampaignId = campaign, TimelineId = timeline, ReignId = CurrentReignId(), Source = ReignDocketSource.International,
                TemplateId = template?.Id ?? (proposal ? "international-npc-proposal" : "international-political-incident"),
                Title = template?.Title ?? queued?.Value<string>("title") ?? "International affairs",
                Summary = template?.Premise ?? queued?.Value<string>("summary") ?? string.Empty,
                KingdomId = origin.StringId, SettlementId = CurrentCapital.StringId, ReceivedDay = day, Severity = severity,
                AvailableDay = posting?.ArrivalDay ?? day + 3, State = ReignCourtLifeMatterState.Arriving,
                TranscriptId = "court_life_" + id
            };
            JObject data = queued == null ? new JObject() : (JObject)queued.DeepClone();
            data["foreign"] = InternationalKingdomIdentity(origin, false);
            data["player"] = InternationalKingdomIdentity(ours, true);
            data["foreignRulerId"] = origin.Leader.StringId;
            data["domesticLordId"] = domestic?.StringId ?? string.Empty;
            data["foreignLordId"] = foreignLord?.StringId ?? string.Empty;
            data["remedy"] = template?.Remedy ?? (proposal ? "proposal" : constructive ? "goodwill" : "compensation");
            data["constructive"] = constructive;
            // Constructive catalog entries are offers initiated by the named foreign ruler.
            // Their exact starting terms do not need the envoy to accept the ruler's own offer again.
            data["sovereignAuthorized"] = template?.Constructive == true;
            data["courierDelivery"] = wartimeProposal;
            data["extortion"] = template?.Extortion == true;
            data["channel"] = template?.Channel ?? queued?.Value<string>("channel") ?? "treaty_strain";
            data["gold"] = template?.Gold ?? new[] { 500, 2000, 5000, 10000 }[(int)severity];
            Hero captive = template?.RequiresCaptive == true ? FindInternationalCaptive(origin) : null;
            data["captiveId"] = captive?.StringId ?? string.Empty;
            data["captive"] = InternationalCaptiveIdentity(captive, origin);
            data["ransom"] = template?.RequiresCaptive == true && template.Id.Contains("ransom");
            data["privateTruth"] = template?.Constructive == true || template?.Extortion == true ? string.Empty
                : new[] { "The domestic claimant's account is better supported.", "The foreign claimant's account is better supported.", "Both households relied on incomplete accounts." }
                    [ReignCourtLifeRules.StableRoll(id + "|truth", 3)];
            data["knowledgeRule"] = template?.Extortion == true
                ? "This is an authorized demand backed by the stronger kingdom's political leverage, not compensation for an alleged injury. You know the demand and its purpose. Do not invent a victim, unpaid damages, missing evidence, troops, sanctions, or an automatic declaration of war. Payment buys relief from this demand, not an enforceable peace treaty."
                : "Allegations are contested. Do not invent dead heroes, stolen inventory, transferred money, treaties, or proof. State only your own account until evidence is disclosed. The foreign lord is represented by their envoy, not physically at court unless listed as an active participant.";
            if (template?.Extortion == true) data["termsSummary"] = origin.Leader.Name + " demands " + data.Value<int>("gold")
                + " gold from " + Hero.MainHero.Name + " for the foreign crown, using the kingdom's superior power to seek submission. This is a coercive payment demand, not restitution for an established injury.";
            if (template?.Remedy == "gift") data["termsSummary"] = origin.Name + " offers " + template.Gold + " gold as an unconditional gift.";
            if (template?.Remedy == "sign_trade_agreement") data["termsSummary"] = "A national trade agreement lasting 120 days; no gold payment.";
            if (template == null && data.Value<string>("kind") == "incident" && string.IsNullOrWhiteSpace(data.Value<string>("termsSummary")))
                data["termsSummary"] = "A player response to the " + matter.Title + " political incident. Acceptance records a favorable diplomatic response; it does not create a treaty, payment, tariff exemption, caravan guarantee, or other term not stated in the incident.";
            if (template?.RequiresCaptive == true) data["termsSummary"] = data.Value<bool>("ransom")
                ? "Release " + captive?.Name + " in exchange for " + template.Gold + " gold from the foreign ruler."
                : "Release " + captive?.Name + " from the player's custody without a ransom payment.";
            if (domestic != null) matter.Participants.Add(InternationalParticipant(domestic, "domestic_lord", "Explain your household's account of the dispute. You cannot command the foreign lord. "
                + InternationalParticipantKnowledge(data, true) + " " + data.Value<string>("knowledgeRule")));
            matter.PayloadJson = data.ToString(Formatting.None);
            SetInternationalOptions(matter, data, template?.ResolutionIds ?? (constructive ? new[] { "accept", "refuse", "refer" } : new[] { "favor_domestic", "favor_foreign", "compensate", "compromise", "refer" }));
            if (wartimeProposal)
            {
                matter.Participants.Add(new ReignCourtLifeParticipant { ActorId = "royal_courier_" + id, Name = "Royal messenger", Role = "diplomatic_courier", Age = 30,
                    PrivateContext = "You deliver the foreign ruler's authenticated written offer to the court. Read its exact terms faithfully, clarify what the letter says, and do not claim authority to change it. You do not represent a resident enemy ambassador or grant terms of your own. " + data.Value<string>("termsSummary") });
                matter.State = ReignCourtLifeMatterState.Pending;
                matter.AvailableDay = day;
                matter.Options.RemoveAll(x => x.OptionId == "refer");
            }
            else if (posting?.IsResident == true) PrepareInternationalAudience(matter, posting);
            return matter;
        }

        private static JObject InternationalKingdomIdentity(Kingdom kingdom, bool player)
            => new JObject { ["kingdomId"] = kingdom.StringId, ["name"] = kingdom.Name.ToString(),
                ["leaderHeroId"] = kingdom.Leader.StringId, ["leaderName"] = kingdom.Leader.Name.ToString(), ["isPlayerKingdom"] = player };

        private static JObject InternationalCaptiveIdentity(Hero captive, Kingdom origin)
        {
            if (captive == null) return new JObject();
            Clan house = captive.Clan;
            string houseName = house?.Name?.ToString() ?? "an unnamed noble house";
            bool rulingHouse = origin?.Leader?.Clan == house;
            bool houseHead = house?.Leader == captive;
            string standing = rulingHouse
                ? "a member of the ruling House " + houseName
                : houseHead ? "the head of House " + houseName
                : (house?.Tier ?? 0) >= 4 ? "a senior member of the great House " + houseName
                : "a member of the noble House " + houseName;
            return new JObject {
                ["heroId"] = captive.StringId,
                ["name"] = captive.Name?.ToString() ?? captive.StringId,
                ["houseId"] = house?.StringId ?? string.Empty,
                ["houseName"] = houseName,
                ["houseTier"] = house?.Tier ?? 0,
                ["standing"] = standing,
                ["isRulingHouse"] = rulingHouse,
                ["isHouseHead"] = houseHead,
                ["kingdomId"] = origin?.StringId ?? string.Empty,
                ["kingdomName"] = origin?.Name?.ToString() ?? string.Empty,
                ["custodyStatus"] = "Currently held personally by " + (Hero.MainHero?.Name?.ToString() ?? "the player ruler") + ".",
                ["captureCircumstancesKnown"] = false
            };
        }

        internal void EnsureInternationalCaptiveContext(ReignCourtLifeMatter matter)
        {
            if (matter?.Source != ReignDocketSource.International) return;
            JObject data = ParseObject(matter.PayloadJson);
            string captiveId = data.Value<string>("captiveId");
            if (string.IsNullOrWhiteSpace(captiveId) || data["captive"] is JObject existing
                && !string.IsNullOrWhiteSpace(existing.Value<string>("name"))
                && !string.IsNullOrWhiteSpace(existing.Value<string>("houseName"))
                && !string.IsNullOrWhiteSpace(existing.Value<string>("standing"))) return;
            Hero captive = FindHero(captiveId);
            Kingdom origin = Kingdom.All.FirstOrDefault(x => x?.StringId == matter.KingdomId);
            if (captive == null || origin == null) return;
            data["captive"] = InternationalCaptiveIdentity(captive, origin);
            matter.PayloadJson = data.ToString(Formatting.None);
        }

        private static ReignCourtLifeParticipant InternationalParticipant(Hero hero, string role, string context)
            => new ReignCourtLifeParticipant { ActorId = hero.StringId, HeroId = hero.StringId, Name = hero.Name.ToString(),
                Age = hero.Age, IsFemale = hero.IsFemale, Role = role, PrivateContext = context };

        private void SetInternationalOptions(ReignCourtLifeMatter matter, JObject data, IEnumerable<string> ids)
        {
            int gold = data.Value<int?>("gold") ?? 0;
            var labels = new Dictionary<string, string> { ["accept"] = "Accept the exact offer", ["refuse"] = "Refuse the request",
                ["favor_domestic"] = "Uphold your noble's position", ["favor_foreign"] = "Uphold the foreign noble's position",
                ["compensate"] = "Pay " + gold + (data.Value<bool?>("extortion") == true ? " gold to meet the demand" : " gold in compensation"), ["compromise"] = "Offer " + gold / 2 + " gold to settle",
                ["refer"] = "Refer exact terms to the foreign ruler" };
            matter.Options = ids.Select(option => new ReignCourtLifeOption { OptionId = option, Label = labels[option],
                Description = option == "refer" ? "Await the sovereign's reply before executing an agreement."
                    : option == "accept" ? (data.Value<string>("termsSummary") ?? "Conclude only the offered terms after acceptance and authority are established.")
                    : option == "favor_domestic" || option == "favor_foreign" ? "Issue a sovereign ruling within your authority; no foreign property or office is transferred."
                    : labels[option],
                TermsJson = option == "accept" && data["candidate"]?["terms"] is JObject exact
                    ? exact.ToString(Formatting.None)
                    : InternationalOfferedTerms(data, option, gold).ToString(Formatting.None) }).ToList();
            foreach (ReignCourtLifeOption option in matter.Options.Where(x => IsInternationalPaymentOption(data, x.OptionId)))
                option.Description = DescribeInternationalTerms(ParseObject(option.TermsJson));
        }

        private static JObject InternationalOfferedTerms(JObject data, string option, int gold)
        {
            string remedy = data.Value<string>("remedy");
            var terms = new JObject { ["remedy"] = remedy };
            if (option == "compensate" || option == "compromise" || (option == "accept" && (remedy == "gift" || data.Value<bool?>("ransom") == true)))
            {
                terms["gold"] = option == "compromise" ? gold / 2 : gold;
                terms["payerHeroId"] = option == "accept" ? data.Value<string>("foreignRulerId") : data["player"]?.Value<string>("leaderHeroId");
                terms["recipientHeroId"] = option == "accept" ? data["player"]?.Value<string>("leaderHeroId")
                    : data.Value<bool?>("extortion") == true || string.IsNullOrWhiteSpace(data.Value<string>("foreignLordId"))
                        ? data.Value<string>("foreignRulerId") : data.Value<string>("foreignLordId");
            }
            if (option == "accept" && remedy == "sign_trade_agreement") terms["durationDays"] = 120;
            if (option == "accept" && remedy == "prisoner")
            { terms["captiveId"] = data.Value<string>("captiveId"); terms["ransom"] = data.Value<bool?>("ransom") == true; }
            return terms;
        }

        private static bool IsInternationalPaymentOption(JObject data, string option)
            => option == "compensate" || option == "compromise" || (option == "accept"
                && (data.Value<string>("remedy") == "gift" || (data.Value<string>("remedy") == "prisoner" && data.Value<bool?>("ransom") == true)));

        private bool RefreshLegacyInternationalPaymentTerms(ReignCourtLifeMatter matter)
        {
            if (matter.Source != ReignDocketSource.International || !matter.IsPending || matter.EffectsCommitted) return false;
            JObject data = ParseObject(matter.PayloadJson);
            if (data.Value<bool?>("nativeEffectCommitted") == true || data.Value<bool?>("nativeActionStarted") == true
                || data["referredResolutionTerms"] != null || matter.State == ReignCourtLifeMatterState.Deferred) return false;
            bool changed = false;
            foreach (ReignCourtLifeOption option in matter.Options.Where(x => IsInternationalPaymentOption(data, x.OptionId)))
            {
                JObject terms = ParseObject(option.TermsJson);
                if (terms["payerHeroId"] != null && terms["recipientHeroId"] != null)
                {
                    bool incoming = option.OptionId == "accept";
                    if (ReignInternationalDocketRules.PaymentPartiesAllowed(
                        terms.Value<string>("payerHeroId"), terms.Value<string>("recipientHeroId"),
                        Hero.MainHero?.StringId, data.Value<string>("foreignRulerId"),
                        data.Value<string>("domesticLordId"), data.Value<string>("foreignLordId"),
                        incoming, data.Value<bool?>("extortion") == true)) continue;
                    matter.State = ReignCourtLifeMatterState.Invalidated;
                    matter.DecisionSummary = incoming
                        ? "The sovereign who offered these payment terms has been replaced; a new offer is required."
                        : "A named payment party is no longer eligible; fresh terms are required.";
                    matter.AcceptedDecisionJson = "{}";
                    matter.SelectedOptionId = string.Empty;
                    StateChanged?.Invoke();
                    return true;
                }
                JObject defaults = InternationalOfferedTerms(data, option.OptionId, terms.Value<int?>("gold") ?? 0);
                terms["payerHeroId"] = defaults["payerHeroId"];
                terms["recipientHeroId"] = defaults["recipientHeroId"];
                option.TermsJson = terms.ToString(Formatting.None);
                option.Description = DescribeInternationalTerms(terms);
                changed = true;
            }
            if (changed)
            {
                // Legacy assent named only an amount. Never silently bind it to a person.
                matter.AcceptedDecisionJson = "{}";
                matter.SelectedOptionId = string.Empty;
                data["sovereignAuthorized"] = false;
                matter.PayloadJson = data.ToString(Formatting.None);
                StateChanged?.Invoke();
            }
            return changed;
        }

        private JArray InternationalPaymentParties(ReignCourtLifeMatter matter)
        {
            if (matter.Source != ReignDocketSource.International) return new JArray();
            JObject data = ParseObject(matter.PayloadJson);
            return new JArray(new[] { Hero.MainHero, FindHero(data.Value<string>("foreignRulerId")),
                FindHero(data.Value<string>("domesticLordId")), FindHero(data.Value<string>("foreignLordId")) }
                .Where(x => x != null && x.IsAlive).Distinct().Select(x => new JObject {
                    ["heroId"] = x.StringId, ["name"] = x.Name.ToString(), ["householdName"] = x.Clan?.Name?.ToString() ?? string.Empty }));
        }

        private static string InternationalParticipantKnowledge(JObject data, bool domestic)
        {
            string truth = data.Value<string>("privateTruth") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(truth)) return string.Empty;
            bool accountSupported = truth.StartsWith(domestic ? "The domestic" : "The foreign", StringComparison.Ordinal);
            return accountSupported
                ? "Your household has a consistent first-hand account. Describe only details compatible with the situation template; you have no knowledge of the other household's private intentions."
                : "Your household's account comes from incomplete reports. You may press its position according to your personality, but acknowledge uncertainty when directly asked; you possess no independent proof.";
        }

        private void PrepareInternationalAudience(ReignCourtLifeMatter matter, ForeignAmbassadorPosting posting)
        {
            Hero envoy = FindHero(posting.HeroStringId);
            Kingdom origin = Kingdom.All.FirstOrDefault(x => x?.StringId == matter.KingdomId);
            if (envoy == null || envoy.IsPrisoner || !envoy.IsAlive || origin?.Leader == null || !posting.IsResident) return;
            JObject data = ParseObject(matter.PayloadJson);
            ReignInternationalTemplate savedTemplate = ReignInternationalDocketCatalog.Find(matter.TemplateId);
            if (data.Property("sovereignAuthorized") == null && savedTemplate?.Constructive == true
                && data.Value<bool?>("counterProposed") != true
                && string.IsNullOrWhiteSpace(data.Value<string>("referralId")) && data["candidate"] == null)
            {
                // Repair pending catalog offers created before sovereign authority was persisted.
                // Referred or changed terms still require fresh acceptance from the foreign ruler.
                data["sovereignAuthorized"] = true;
            }
            data["ambassadorContext"] = new JObject { ["postingId"] = posting.PostingId,
                ["representedKingdomId"] = origin.StringId, ["representedRulerId"] = origin.Leader.StringId,
                ["hostKingdomId"] = Clan.PlayerClan.Kingdom.StringId, ["authorityRevision"] = posting.AuthorityRevision,
                ["authorityCharter"] = ParseObject(posting.AuthorityCharterJson), ["internationalMatterId"] = matter.MatterId };
            string sovereignAuthority = data.Value<bool?>("sovereignAuthorized") == true
                ? "The represented ruler has already authorized the current exact offer. Present those terms as valid for the other crown to accept. A standing-charter permission marked false limits your ability to invent or independently change terms; it does not cancel this ruler-authorized offer. Do not claim that the represented ruler must approve these same exact terms again. "
                : string.Empty;
            string politicalIncidentContext = InternationalPoliticalIncidentContext(matter, data, origin);
            // Resolve context before selecting the list to mutate: ensuring state
            // normalizes matters and replaces their participant lists.
            ReignCourtLifeParticipant ambassador = InternationalParticipant(envoy, "ambassador",
                "Represent " + origin.Name + " and its current ruler " + origin.Leader.Name + ". "
                + (data.Value<bool?>("extortion") == true ? "Your ruler deliberately briefed and authorized you to present this coercive monetary demand. Deliver it as an informed envoy, with composure and the represented ruler's manner. Your own personality shapes tact, candor and private discomfort, but does not erase your mandate. This audience concerns current matter " + matter.MatterId + ", which is unresolved and distinct from every earlier demand. A payment or settlement recorded for another matter does not satisfy this one. Do not say this current demand was already paid or refuse to present it because a similar demand appears in memory. If challenged, explain the demand's political purpose; do not invent an injury or act bewildered because no evidence of damages exists. You may acknowledge coercion honestly without spontaneously disowning your mission. Refusal will be reported and may worsen diplomatic pressure; it does not itself declare war or authorize invented reprisals. Hospitality does not cancel the demand, and you cannot promise a peace treaty in exchange. " : string.Empty)
                + politicalIncidentContext
                + "Represented noble: " + (FindHero(data.Value<string>("foreignLordId"))?.Name?.ToString() ?? "the crown") + ". "
                + "Current offer: " + data.Value<string>("termsSummary") + ". "
                + sovereignAuthority
                + "Represented ruler's authoritative personality: " + ParseObject(EnsureRulerDocketState().InternationalProfilesJson)[origin.StringId] + ". "
                + InternationalParticipantKnowledge(data, false) + " " + data.Value<string>("knowledgeRule") + " Charter: " + posting.AuthorityCharterJson);
            matter.Participants.RemoveAll(x => x.Role == "ambassador");
            matter.Participants.Add(ambassador);
            if (matter.State == ReignCourtLifeMatterState.Arriving)
            {
                foreach (ReignCourtLifeParticipant participant in matter.Participants.Where(x => x.Role == "domestic_lord"))
                {
                    Hero hero = FindHero(participant.HeroId);
                    if (hero == null || hero.IsPrisoner || hero.PartyBelongedTo != null || !TryBringNobleToCapital(hero, CurrentCapital, out _)) return;
                }
                matter.AvailableDay = CampaignTime.Now.ToDays;
                matter.State = ReignCourtLifeMatterState.Pending;
            }
            matter.PayloadJson = data.ToString(Formatting.None);
        }

        private static string InternationalPoliticalIncidentContext(ReignCourtLifeMatter matter, JObject data, Kingdom origin)
        {
            if (data.Value<string>("kind") != "incident") return string.Empty;
            string foreignLords = string.Join(", ", (data["foreignLordIds"] as JArray ?? new JArray()).Values<string>());
            string domesticLords = string.Join(", ", (data["domesticLordIds"] as JArray ?? new JArray()).Values<string>());
            string ruling = data.Value<string>("foreignRuling") ?? string.Empty;
            string rulingContext = ruling == "preserve_relations"
                ? origin.Leader.Name + " already heard the political incident and ruled against escalating on behalf of the foreign petitioning lords in order to preserve relations with the player's realm. Because the other crown is player-controlled, that choice has already reduced the foreign kingdom's diplomatic pressure toward the player kingdom. Do not say that no incident reached the ruler or that no ruling occurred. "
                : ruling == "support_lords"
                    ? origin.Leader.Name + " already heard the political incident and ruled in favor of the foreign petitioning lords. That choice has already applied the incident's diplomatic pressure toward the player kingdom. Do not say that no incident reached the ruler or that no ruling occurred. "
                    : "Do not invent a completed foreign ruling; state only the ruling status supplied by the incident. ";
            return "This audience continues an already generated political incident involving the player kingdom. Incident: "
                + matter.Title + ". Authoritative situation: " + matter.Summary + ". Foreign petitioning lords: "
                + (string.IsNullOrWhiteSpace(foreignLords) ? "none named" : foreignLords) + ". Player-side lords named in the incident: "
                + (string.IsNullOrWhiteSpace(domesticLords) ? "none named" : domesticLords) + ". " + rulingContext
                + "The pressure consequence is already committed and must not be promised, denied, reversed, or applied again in dialogue. The player's response remains pending. Explain that response as a court decision about this incident, not as proof that an unstated treaty, payment, duration, protection, or market rule already exists. ";
        }

        public void ProcessInternationalCourtLife(double now)
        {
            if (Clan.PlayerClan?.Kingdom?.Leader != Hero.MainHero) return;
            ReignRulerDocketState state = EnsureRulerDocketState();
            foreach (ReignCourtLifeMatter matter in state.CourtLifeMatters.Where(x => x.Source == ReignDocketSource.International && x.IsPending).ToList())
            {
                Kingdom origin = Kingdom.All.FirstOrDefault(x => x?.StringId == matter.KingdomId);
                JObject data = ParseObject(matter.PayloadJson);
                // A completed treaty is already a historical fact. Record its court outcome
                // before evaluating whether a new audience would still be eligible today.
                if (TryReconcileCompletedInternationalAction(matter, data)) continue;
                RefreshLegacyInternationalPaymentTerms(matter);
                if (!matter.IsPending) continue;
                data = ParseObject(matter.PayloadJson);
                if (origin?.Leader == null || origin.IsEliminated)
                { matter.State = ReignCourtLifeMatterState.Invalidated; matter.DecisionSummary = "The represented realm no longer has an available sovereign."; continue; }
                if (!InternationalClaimantsRemainEligible(data, origin))
                { matter.State = ReignCourtLifeMatterState.Invalidated; matter.DecisionSummary = "A named claimant has died or changed allegiance; the household dispute is no longer current."; continue; }
                if (origin.Leader.StringId != data.Value<string>("foreignRulerId"))
                {
                    if (data.Value<string>("kind") == "proposal" || data.Value<bool?>("sovereignAuthorized") == true
                        || !string.IsNullOrWhiteSpace(data.Value<string>("referralId")))
                    { matter.State = ReignCourtLifeMatterState.Invalidated; matter.DecisionSummary = "The sovereign who offered these terms has been replaced; a new offer is required."; continue; }
                    data["foreignRulerId"] = origin.Leader.StringId;
                    data["foreign"] = InternationalKingdomIdentity(origin, false);
                    matter.PayloadJson = data.ToString(Formatting.None);
                }
                if (data.Value<bool?>("courierDelivery") == true) continue;
                if (Clan.PlayerClan.Kingdom.IsAtWarWith(origin))
                { matter.State = ReignCourtLifeMatterState.Arriving; continue; }
                ForeignAmbassadorPosting posting = _foreignAmbassadors.FirstOrDefault(x => x.IsActive && x.OriginKingdomStringId == origin.StringId);
                if (posting == null && CurrentCapital != null && _internationalPostingInFlight.Add(origin.StringId))
                    _ = EstablishInternationalPostingAsync(origin);
                else if (posting?.IsResident == true) PrepareInternationalAudience(matter, posting);
            }
            if (_internationalRefreshInFlight || DateTime.UtcNow < _internationalRefreshAfterUtc) return;
            _internationalRefreshInFlight = true;
            _internationalRefreshAfterUtc = DateTime.UtcNow.AddSeconds(30);
            JObject world = ReignServerClient.BuildDiplomacyWorldSnapshot();
            _ = RefreshInternationalCourtAsync(world, state);
        }

        private async Task EstablishInternationalPostingAsync(Kingdom origin)
        {
            try { await EstablishForeignAmbassadorAsync(origin, true).ConfigureAwait(false); }
            finally { await ReignMainThread.InvokeAsync(() => _internationalPostingInFlight.Remove(origin.StringId)).ConfigureAwait(false); }
        }

        private async Task RefreshInternationalCourtAsync(JObject world, ReignRulerDocketState state)
        {
            try
            {
                ReignCourtServerResponse response = await ReignCourtServerClient.InternationalCourtPendingAsync(world).ConfigureAwait(false);
                if (response.Ok) await ReignMainThread.InvokeAsync(() =>
                {
                    if (!ReferenceEquals(state, EnsureRulerDocketState())) return;
                    state.InternationalWorldJson = world.ToString(Formatting.None);
                    state.InternationalProfilesJson = (response.Raw["rulerProfiles"] as JObject ?? new JObject()).ToString(Formatting.None);
                    state.InternationalPowerJson = (response.Raw["nationalPower"] as JObject ?? new JObject()).ToString(Formatting.None);
                    state.InternationalQueueJson = (response.Raw["matters"] as JArray ?? new JArray()).OfType<JObject>().Select(x => x.ToString(Formatting.None)).ToList();
                    foreach (JObject reply in (response.Raw["replies"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        ReignCourtLifeMatter matter = state.CourtLifeMatters.FirstOrDefault(x => x.MatterId == reply.Value<string>("matter_id"));
                        if (matter == null || !matter.IsPending) continue;
                        JObject data = ParseObject(matter.PayloadJson);
                        if (data.Value<bool?>("replyPromoted") == true) continue;
                        string status = reply.Value<string>("status") ?? "pending";
                        if (status == "pending") continue;
                        data["reply"] = reply.DeepClone();
                        data["replyReady"] = true;
                        matter.PayloadJson = data.ToString(Formatting.None);
                        int? reservedDay = data.Value<int?>("replyReservedDay");
                        // The server resolves referrals on its daily world tick. A reply can therefore
                        // arrive after the day on which the docket reserved its slot. Keep that older
                        // reservation valid so the first successful refresh publishes the reply instead
                        // of waiting for another docket day to reserve it again.
                        if (reservedDay.HasValue && reservedDay.Value <= CurrentDay()) PublishInternationalReply(matter, data);
                    }
                }).ConfigureAwait(false);
                List<string> receipts = await ReignMainThread.InvokeAsync(() => ReferenceEquals(state, EnsureRulerDocketState())
                    ? (state.InternationalReceiptsJson ?? new List<string>()).ToList() : new List<string>()).ConfigureAwait(false);
                foreach (string encoded in receipts)
                {
                    if (!await ReignMainThread.InvokeAsync(() => ReferenceEquals(state, EnsureRulerDocketState())).ConfigureAwait(false)) break;
                    ReignCourtServerResponse delivered = await ReignCourtServerClient.InternationalCourtResolveAsync(ParseObject(encoded)).ConfigureAwait(false);
                    if (delivered.Ok) await ReignMainThread.InvokeAsync(() => state.InternationalReceiptsJson.Remove(encoded)).ConfigureAwait(false);
                }
            }
            catch (Exception ex) { ReignLog.Warn("International court synchronization deferred: " + ex.Message); }
            finally { await ReignMainThread.InvokeAsync(() => _internationalRefreshInFlight = false).ConfigureAwait(false); }
        }

        private static bool InternationalReplyDue(JObject data, int day) => data.Value<bool?>("replyPromoted") != true
            && !string.IsNullOrWhiteSpace(data.Value<string>("referralId"))
            && (data.Value<bool?>("replyReady") == true || (data.Value<double?>("referralDueDay") ?? double.MaxValue) <= day + 8d / 24d);

        public int DueInternationalReplyCount(int day) => EnsureRulerDocketState().CourtLifeMatters.Count(x =>
            x.Source == ReignDocketSource.International && x.IsPending && InternationalReplyDue(ParseObject(x.PayloadJson), day));

        public int PromoteInternationalReplies(int day, int maximum)
        {
            int promoted = 0;
            foreach (ReignCourtLifeMatter matter in EnsureRulerDocketState().CourtLifeMatters.Where(x => x.Source == ReignDocketSource.International && x.IsPending)
                .OrderBy(x => ParseObject(x.PayloadJson).Value<double?>("referralDueDay") ?? double.MaxValue).ThenBy(x => x.MatterId).ToList())
            {
                if (promoted >= maximum) break;
                JObject data = ParseObject(matter.PayloadJson);
                if (!InternationalReplyDue(data, day)) continue;
                // Reserve the daily slot before asynchronous delivery, so a due reply cannot be crowded out by new petitions.
                data["replyReservedDay"] = day;
                matter.PayloadJson = data.ToString(Formatting.None);
                if (data.Value<bool?>("replyReady") == true) PublishInternationalReply(matter, data);
                promoted++;
            }
            return promoted;
        }

        private void PublishInternationalReply(ReignCourtLifeMatter matter, JObject data)
        {
            if (data.Value<bool?>("replyPromoted") == true || data.Value<bool?>("replyReady") != true) return;
            JObject reply = data["reply"] as JObject;
            string status = reply?.Value<string>("status") ?? string.Empty;
            data["replyPromoted"] = true;
            data["replyReady"] = false;
            data["sovereignAuthorized"] = status == "accepted_awaiting_court" || status == "countered";
            data["requiresSovereignReferral"] = false;
            JObject receipt = ParseObject(reply?.Value<string>("authoritative_receipt_json"));
            if (receipt["normalizedAction"] != null) data["normalizedAction"] = receipt["normalizedAction"].DeepClone();
            if (status == "countered")
            {
                data["counterCandidate"] = ParseObject(reply.Value<string>("counter_terms_json"));
                data["candidate"] = data["counterCandidate"].DeepClone();
                if (reply["normalizedAction"] is JObject) data["normalizedAction"] = reply["normalizedAction"].DeepClone();
                else data["sovereignAuthorized"] = false;
                if (data.Value<bool?>("sovereignAuthorized") == true && data.Value<string>("remedy") != "proposal" && data.Value<string>("remedy") != "sign_trade_agreement")
                {
                    JObject counter = data["candidate"] as JObject;
                    if (counter?.Value<string>("command") != data.Value<string>("referredCommand"))
                    {
                        data["remedy"] = "proposal";
                        data["referredResolutionOption"] = "accept";
                        data.Remove("referredResolutionTerms");
                    }
                    else if (data["referredResolutionTerms"] is JObject materialTerms && materialTerms["gold"] != null)
                    {
                        JToken counterGold = counter?["terms"]?["gold"];
                        if (counterGold?.Type != JTokenType.Integer || counterGold.Value<long>() < 0 || counterGold.Value<long>() > 1000000)
                            data["sovereignAuthorized"] = false;
                        else materialTerms["gold"] = counterGold.DeepClone();
                        foreach (string partyKey in new[] { "payerHeroId", "recipientHeroId" })
                        {
                            JToken counterParty = counter?["terms"]?[partyKey];
                            if (counterParty?.Type != JTokenType.String) data["sovereignAuthorized"] = false;
                            else materialTerms[partyKey] = counterParty.DeepClone();
                        }
                        if (!TryResolveInternationalPayment(data, data.Value<string>("referredResolutionOption"), materialTerms, out _, out _, out _))
                            data["sovereignAuthorized"] = false;
                    }
                }
            }
            JObject terms = data["candidate"]?["terms"] as JObject;
            if (terms != null) data["termsSummary"] = DescribeInternationalTerms(terms);
            string resolution = data.Value<string>("referredResolutionOption") ?? "accept";
            SetInternationalOptions(matter, data, data.Value<bool?>("sovereignAuthorized") == true ? new[] { resolution, "refuse" } : new[] { "refuse" });
            if (data["referredResolutionTerms"] is JObject resolutionTerms && data.Value<string>("remedy") != "proposal"
                && data.Value<string>("remedy") != "sign_trade_agreement" && data.Value<bool?>("sovereignAuthorized") == true)
            {
                ReignCourtLifeOption exactOption = matter.Options.First(x => x.OptionId == resolution);
                exactOption.TermsJson = resolutionTerms.ToString(Formatting.None);
                exactOption.Description = DescribeInternationalTerms(resolutionTerms);
                if (resolution == "compromise") exactOption.Label = "Settle for " + resolutionTerms.Value<int>("gold") + " gold";
            }
            matter.Summary += data.Value<bool?>("sovereignAuthorized") == true
                ? status == "countered" ? " The foreign ruler has sent revised terms for your consideration." : " The foreign ruler has accepted the terms and awaits your final judgment."
                : " The foreign ruler has declined the proposed agreement.";
            matter.State = ReignCourtLifeMatterState.Pending;
            matter.AvailableDay = CampaignTime.Now.ToDays;
            matter.PayloadJson = data.ToString(Formatting.None);
            StateChanged?.Invoke();
        }

        private static string DescribeInternationalTerms(JObject terms)
        {
            return string.Join("; ", terms.Properties().Where(p => p.Name != "remedy" && p.Name != "ransom").Select(p =>
                p.Name == "gold" ? p.Value + " gold" : p.Name == "durationDays" ? p.Value + " days"
                : p.Name == "dailyTribute" ? p.Value + " gold in daily tribute"
                : p.Name == "isPublic" ? p.Value.Value<bool>() ? "Public agreement" : "Private agreement"
                : p.Name == "captiveId" ? "Prisoner: " + (Hero.AllAliveHeroes.FirstOrDefault(x => x.StringId == p.Value.ToString())?.Name?.ToString() ?? "the named prisoner")
                : p.Name == "payerHeroId" || p.Name == "recipientHeroId" ? (p.Name == "payerHeroId" ? "Paid by " : "Paid to ")
                    + (Hero.AllAliveHeroes.FirstOrDefault(x => x.StringId == p.Value.ToString())?.Name?.ToString() ?? "the named party (unavailable)")
                : p.Name == "promise" ? p.Value.ToString()
                : System.Text.RegularExpressions.Regex.Replace(p.Name, "([a-z])([A-Z])", "$1 $2").Replace('_', ' ') + ": " + p.Value.ToString(Formatting.None)));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        public JObject BuildPublicSnapshot(Kingdom kingdom)
        {
            ReignGovernmentStateRecord state = EnsureGovernment(kingdom);
            if (state == null)
                return new JObject { ["available"] = false, ["authoritative"] = true };

            ReignGovernmentPartyRecord dominant = FindParty(kingdom.StringId, state.DominantPartyId);
            var parties = new JArray();
            foreach (ReignGovernmentPartyRecord party in GetParties(kingdom.StringId))
            {
                Hero speaker = FindHero(party.SpeakerHeroStringId);
                parties.Add(new JObject
                {
                    ["partyId"] = party.PartyId,
                    ["name"] = party.Name,
                    ["planks"] = new JArray(ParsePlanks(party.PlanksCsv).Select(x => x.ToString())),
                    ["speakerHeroId"] = party.SpeakerHeroStringId,
                    ["speakerName"] = speaker?.Name?.ToString() ?? string.Empty,
                    ["seatCount"] = party.SeatCount,
                    ["isDominant"] = party.IsDominant,
                    ["isGoverningCoalition"] = party.IsGoverningCoalition,
                    ["publicStatement"] = party.LastStatement ?? string.Empty
                });
            }

            var resolutions = new JArray();
            foreach (ReignGovernmentResolutionRecord record in GetActiveResolutions(kingdom.StringId).Take(12))
            {
                ReignGovernmentResolutionTemplate template = ReignGovernmentResolutionCatalog.Find(record.TemplateId);
                var publicResolution = new JObject
                {
                    ["resolutionId"] = record.ResolutionId,
                    ["templateId"] = record.TemplateId,
                    ["title"] = template?.Title ?? record.TemplateId,
                    ["status"] = record.Status,
                    ["proposingPartyId"] = record.PartyId,
                    ["targetSettlementId"] = record.TargetSettlementStringId,
                    ["targetKingdomId"] = record.TargetKingdomStringId,
                    ["targetPolicyId"] = record.TargetPolicyStringId,
                    ["selectedRoute"] = record.SelectedRoute,
                    ["dueDay"] = record.DueDay,
                    ["outcome"] = GovernmentBusinessRules.PublicText(record.Outcome)
                };
                if (IsPrivateRelationProgress((ReignGovernmentResolutionAction)record.RouteActionValue))
                    publicResolution["progressDescription"] = "Awaiting privately verified completion.";
                else
                {
                    publicResolution["requiredAmount"] = record.RequiredAmount;
                    publicResolution["currentValue"] = record.CurrentValue;
                }
                resolutions.Add(publicResolution);
            }

            string authority = state.Level == 1 ? "advisory"
                : state.Level == 2 ? "formal_pressure"
                : state.Level == 3 ? "reconsideration_and_costly_override"
                : state.Level == 4 ? "approval_or_costly_override_for_major_actions"
                : "binding_individual_government_vote";
            List<ReignGovernmentSeatRecord> seats = GetSeats(kingdom.StringId).ToList();
            int lordSeats = seats.Count(x => (ReignGovernmentSeatSource)x.SeatSourceValue
                == ReignGovernmentSeatSource.LandholdingClanLeader);
            int peopleSeats = seats.Count - lordSeats;
            string failureChannel = lordSeats > peopleSeats ? "landholding_clan_relations_and_civil_war"
                : peopleSeats > lordSeats ? "settlement_loyalty_and_native_town_rebellion"
                : "mixed_settlement_and_landholding_clan_pressure";

            return new JObject
            {
                ["available"] = true,
                ["authoritative"] = true,
                ["privacy"] = "public_government_facts_only",
                ["kingdomId"] = kingdom.StringId,
                ["kingdomName"] = kingdom.Name?.ToString() ?? kingdom.StringId,
                ["institution"] = state.InstitutionName,
                ["institutionKind"] = ((ReignGovernmentInstitutionKind)state.InstitutionKindValue).ToString(),
                ["authorityLevel"] = state.Level,
                ["authorityMeaning"] = authority,
                ["dominantPartyId"] = state.DominantPartyId,
                ["dominantPartyName"] = dominant?.Name ?? string.Empty,
                ["governingCoalitionPartyIds"] = new JArray(GetGoverningCoalition(kingdom.StringId)
                    .Select(x => x.PartyId)),
                ["governingCoalitionNames"] = new JArray(GetGoverningCoalition(kingdom.StringId)
                    .Select(x => x.Name)),
                ["nextMeetingDay"] = state.NextMeetingDay,
                ["lastMeetingSummary"] = state.LastMeetingSummary ?? string.Empty,
                ["peoplePressure"] = new JObject
                {
                    ["additionalToNoblePoliticalPressure"] = true,
                    ["peopleOrNotableSeatCount"] = peopleSeats,
                    ["landholdingLordSeatCount"] = lordSeats,
                    ["dominantFailureChannel"] = failureChannel,
                    ["settlementRoute"] = "Ignored notable-backed demands reduce local loyalty and may feed Bannerlord's native town rebellion path.",
                    ["lordRoute"] = "Ignored landholding-lord demands reduce ruler relations and feed Reign's existing civil-war path.",
                    ["probabilityRule"] = "Government does not directly rewrite Public Standing or rebellion probabilities; it changes the native loyalty and relationship inputs those systems already use."
                },
                ["parties"] = parties,
                ["activeResolutions"] = resolutions,
                ["governmentBusiness"] = new JArray(GetBusiness(kingdom.StringId).Take(24).Select(record => new JObject
                {
                    ["businessId"] = record.BusinessId, ["title"] = record.Title, ["summary"] = record.Summary,
                    ["status"] = record.Status, ["urgent"] = record.IsUrgent,
                    ["petitionerHeroId"] = record.PetitionerHeroStringId,
                    ["petitionerName"] = FindHero(record.PetitionerHeroStringId)?.Name?.ToString() ?? string.Empty,
                    ["sponsorHeroId"] = record.SponsorHeroStringId,
                    ["sponsorName"] = FindHero(record.SponsorHeroStringId)?.Name?.ToString() ?? string.Empty,
                    ["sponsorshipReason"] = record.SponsorReason,
                    ["recommendationRulerHeroId"] = record.RecommendationRulerHeroStringId,
                    ["decisionRulerHeroId"] = record.DecisionRulerHeroStringId,
                    ["recommendedOptionId"] = record.RecommendedOptionId, ["winningOptionId"] = record.WinningOptionId,
                    ["executedOptionId"] = record.ExecutedOptionId,
                    ["options"] = new JArray(BusinessOptions(record).Select(option => new JObject
                        { ["id"] = option.Value<string>("id"), ["label"] = option.Value<string>("label"), ["description"] = option.Value<string>("description") })),
                    ["participants"] = new JArray(JArray.Parse(record.ParticipantsJson).OfType<JObject>().Select(person => new JObject
                        { ["heroId"] = person.Value<string>("heroId"), ["name"] = FindHero(person.Value<string>("heroId"))?.Name?.ToString() ?? string.Empty,
                          ["role"] = (person.Value<string>("role") ?? string.Empty).Replace('_', ' ') })),
                    ["ballots"] = JArray.Parse(record.VotesJson), ["outcome"] = record.Outcome,
                    ["recessUntilDay"] = record.RecessUntilDay, ["reconsiderUntilDay"] = record.ReconsiderUntilDay
                })),
                ["speakerRule"] = "Only each party's recorded speaker gives the party's final public position; individual members still cast saved mechanical votes.",
                ["decisionRule"] = "Follow the stated authority meaning. Where the government holds binding authority, the ruler recommends and individual members decide; there is no later ruler veto. Private political attitudes are not public knowledge.",
                ["privateFieldsWithheld"] = new JArray("memberLoyalty", "memberVoteScores", "bribes", "lobbyingDeals")
            };
        }

        public JObject BuildPrivateRulerSnapshot(Kingdom kingdom)
        {
            JObject snapshot = BuildPublicSnapshot(kingdom);
            if (snapshot.Value<bool?>("available") != true || kingdom?.Leader != Hero.MainHero) return snapshot;

            snapshot["privacy"] = "ruler_government_working_record";
            snapshot["members"] = new JArray(GetSeats(kingdom.StringId).Select(seat =>
            {
                Hero member = FindHero(seat.HeroStringId);
                return new JObject
                {
                    ["heroId"] = seat.HeroStringId,
                    ["name"] = member?.Name?.ToString() ?? seat.HeroStringId,
                    ["partyId"] = seat.PartyId,
                    ["representedSettlementId"] = seat.SettlementStringId,
                    ["seatSource"] = ((ReignGovernmentSeatSource)seat.SeatSourceValue).ToString()
                };
            }));
            snapshot["outstandingDeals"] = new JArray(_lobbyRecords.Where(x => Same(x.KingdomStringId, kingdom.StringId)
                    && Same(x.Method, "deal") && x.Succeeded && !x.Fulfilled)
                .Select(x => new JObject
                {
                    ["lobbyId"] = x.LobbyId,
                    ["resolutionId"] = x.ResolutionId,
                    ["memberHeroId"] = x.MemberHeroStringId,
                    ["terms"] = x.Terms,
                    ["expiresDay"] = x.ExpiresDay
                }));
            return snapshot;
        }

        public JObject BuildGovernmentKnowledgeForSpeaker(Hero speaker)
        {
            ReignGovernmentSeatRecord seat = speaker == null
                ? null
                : _seats.FirstOrDefault(item => Same(item.HeroStringId, speaker.StringId));
            Kingdom own = seat == null
                ? speaker?.Clan?.Kingdom
                : Kingdom.All.FirstOrDefault(item => Same(item.StringId, seat.KingdomStringId))
                    ?? speaker?.Clan?.Kingdom;
            if (own == null) return new JObject { ["available"] = false, ["authoritative"] = true };
            JObject snapshot = BuildPublicSnapshot(own);
            snapshot["knowledgeHolderHeroId"] = speaker.StringId;
            snapshot["knowledgeBasis"] = speaker == own.Leader ? "ruler_authoritative"
                : "kingdom_member_public";
            if (seat == null || !Same(seat.KingdomStringId, own.StringId))
                seat = GetSeats(own.StringId)
                    .FirstOrDefault(item => Same(item.HeroStringId, speaker.StringId));
            ReignGovernmentPartyRecord party = seat == null
                ? null
                : FindParty(own.StringId, seat.PartyId);
            snapshot["ownMembership"] = new JObject
            {
                ["isMember"] = seat != null,
                ["seatSource"] = seat == null ? string.Empty
                    : ((ReignGovernmentSeatSource)seat.SeatSourceValue).ToString(),
                ["representedSettlementId"] = seat?.SettlementStringId ?? string.Empty,
                ["partyId"] = party?.PartyId ?? string.Empty,
                ["partyName"] = party?.Name ?? string.Empty,
                ["partyPlanks"] = new JArray(ParsePlanks(party?.PlanksCsv ?? string.Empty)
                    .Select(item => item.ToString())),
                ["isPartySpeaker"] = party != null
                    && Same(party.SpeakerHeroStringId, speaker.StringId),
                ["personalPowerAtStake"] = seat != null,
                ["reductionMeaning"] = seat == null
                    ? "This speaker holds no saved seat in the institution."
                    : "Reducing government authority also reduces this member's institutional and party power; weigh that self-interest together with personality, relationship, promises, and party agenda."
            };
            return snapshot;
        }
    }
}

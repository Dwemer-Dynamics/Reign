using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.World;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court;
#endif
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.ObjectSystem;
using static ReignBeta.Government.Campaign.ReignGovernmentCampaignBehavior;
using ReignBeta.Government.Campaign;
using ReignBeta.WorldSimulation.World;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court.Campaign;
using ReignBeta.Spymaster.Campaign;
#endif
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Government.Testing
{
    internal static partial class ReignGovernmentTestControls
    {
        private static void GovernmentNativePressureChannelCase(ReignGovernmentCampaignBehavior government, JArray assertions,
            Kingdom kingdom, ReignGovernmentStateRecord state, string caseId)
        {
            JObject snapshot = government.BuildPublicSnapshot(kingdom);
            bool separate = snapshot.SelectToken("peoplePressure.additionalToNoblePoliticalPressure")
                    ?.Value<bool>() == true
                && snapshot.SelectToken("peoplePressure.probabilityRule")?.Value<string>()
                    ?.IndexOf("does not directly rewrite", StringComparison.OrdinalIgnoreCase) >= 0;
            if (caseId == "GOV-NATIVE-023")
            {
                Town town = kingdom?.Fiefs.Where(item => item?.Settlement?.IsFortification == true)
                    .OrderBy(item => item.Settlement.StringId, StringComparer.Ordinal).FirstOrDefault();
                ReignGovernmentPartyRecord party = government.GetParties(kingdom?.StringId)
                    .Where(item => item.SeatCount > 0)
                    .OrderBy(item => item.PartyId, StringComparer.Ordinal).FirstOrDefault();
                ReignGovernmentResolutionTemplate template = ReignGovernmentResolutionCatalog.Find("R004")
                    ?? ReignGovernmentResolutionCatalog.Templates.FirstOrDefault();
                if (kingdom == null || state == null || town == null || party == null || template == null)
                {
                    AddGovernmentAssertion(assertions, "government_native_people_pressure_native_unrest", false,
                        "The people-pressure case requires a real fortification, occupied party, and resolution template.", snapshot);
                    return;
                }
                state.Level = 4;
                town.Loyalty = 45f;
                var record = new ReignGovernmentResolutionRecord
                {
                    ResolutionId = "government-cert-people-pressure-" + Guid.NewGuid().ToString("N"),
                    KingdomStringId = kingdom.StringId,
                    TemplateId = template.Id,
                    PartyId = party.PartyId,
                    SpeakerHeroStringId = party.SpeakerHeroStringId,
                    TargetSettlementStringId = town.Settlement.StringId,
                    ProposedDay = CurrentDay(),
                    Status = "proposed",
                    ConsequenceLevel = state.Level,
                    Revision = 1
                };
                government._resolutions.Add(record);
                float before = town.Loyalty;
                bool declined = GovernmentNativeResolveSeasonalFixture(government, record, "reject", kingdom, 4, out string result);
                AddGovernmentAssertion(assertions, "government_native_people_pressure_native_unrest",
                    separate && declined && Same(record.Status, "refused")
                    && town.Loyalty < before
                    && snapshot.SelectToken("peoplePressure.settlementRoute")?.Value<string>()
                        ?.IndexOf("native town rebellion", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Refusing a real notable-backed resolution reduced the represented settlement's native loyalty input while the public contract left Public Standing and rebellion probability formulas untouched.",
                    new JObject
                    {
                        ["settlementId"] = town.Settlement.StringId,
                        ["loyaltyBefore"] = before,
                        ["loyaltyAfter"] = town.Loyalty,
                        ["resolutionId"] = record.ResolutionId,
                        ["resolutionStatus"] = record.Status,
                        ["result"] = result,
                        ["peoplePressure"] = snapshot["peoplePressure"]
                    });
                return;
            }

            Clan affectedClan = kingdom?.Clans.Where(clan => clan != null
                    && clan != kingdom.RulingClan && clan != Clan.PlayerClan
                    && !clan.IsEliminated && clan.Fiefs.Count > 0 && clan.Leader != null)
                .OrderBy(clan => clan.StringId, StringComparer.Ordinal).FirstOrDefault();
            GovernmentTemporaryLandholdingFixture landFixture = null;
            if (affectedClan == null)
            {
                affectedClan = kingdom?.Clans.Where(clan => clan != null
                        && clan != kingdom.RulingClan && clan != Clan.PlayerClan
                        && !clan.IsEliminated && clan.Leader != null)
                    .OrderBy(clan => clan.StringId, StringComparer.Ordinal).FirstOrDefault();
                landFixture = EnsureTemporaryLandholdingClans(government, kingdom, new[] { affectedClan });
            }
            try
            {
            List<ReignGovernmentSeatRecord> seats = government.GetSeats(kingdom?.StringId).ToList();
            if (kingdom == null || state == null || affectedClan == null
                || affectedClan.Fiefs.Count == 0 || seats.Count == 0)
            {
                AddGovernmentAssertion(assertions, "government_native_lord_pressure_civil_war_input", false,
                    "The noble-pressure case requires one non-ruling landholding clan and occupied Government seats.", snapshot);
                return;
            }
            state.Level = 5;
            state.Trust = 100;
            state.ReductionConsentFromLevel = 0;
            state.ReductionConsentHeroIdsCsv = string.Empty;
            state.ReductionConsentClanIdsCsv = string.Empty;
            foreach (ReignGovernmentPartyRecord party in government.GetParties(kingdom.StringId))
            {
                party.PlanksCsv = "RepresentativeAuthority,PopularWelfare";
                party.PartyLoyalty = 100;
            }
            foreach (ReignGovernmentSeatRecord seat in seats)
            {
                seat.PartyLoyalty = 100;
                seat.GovernmentLoyalty = 100;
            }
            int relation = affectedClan.Leader.GetRelation(kingdom.Leader);
            if (relation != 100) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                kingdom.Leader, affectedClan.Leader, 100 - relation, false);
            int relationBefore = affectedClan.Leader.GetRelation(kingdom.Leader);
            bool forced = government.ReduceGovernmentLevel(kingdom, true, out string forcedResult);
            int relationAfter = affectedClan.Leader.GetRelation(kingdom.Leader);
            AddGovernmentAssertion(assertions, "government_native_lord_pressure_civil_war_input",
                separate && forced && relationAfter - relationBefore == -70
                && snapshot.SelectToken("peoplePressure.lordRoute")?.Value<string>()
                    ?.IndexOf("civil-war", StringComparison.OrdinalIgnoreCase) >= 0,
                "Ignoring landholding-lord Government pressure changed the same native ruler-relation value consumed by Reign's separate civil-war system, without replacing noble Political Pressure.",
                new JObject
                {
                    ["clanId"] = affectedClan.StringId,
                    ["leaderHeroId"] = affectedClan.Leader.StringId,
                    ["relationBefore"] = relationBefore,
                    ["relationAfter"] = relationAfter,
                    ["relationDelta"] = relationAfter - relationBefore,
                    ["result"] = forcedResult,
                    ["peoplePressure"] = snapshot["peoplePressure"]
                });
            }
            finally
            {
                landFixture?.Dispose();
            }
        }

        private static void GovernmentNativeNpcRulerCase(ReignGovernmentCampaignBehavior government, JArray assertions, string caseId)
        {
            Kingdom kingdom = Kingdom.All.Where(IsEligibleKingdom)
                .Where(item => item.Leader != Hero.MainHero)
                .OrderBy(item => item.StringId, StringComparer.Ordinal).FirstOrDefault();
            ReignGovernmentStateRecord state = government.EnsureGovernment(kingdom);
            List<ReignGovernmentPartyRecord> parties = government.GetParties(kingdom?.StringId).ToList();
            ReignGovernmentPartyRecord coalition = parties.OrderBy(item => item.PartyId,
                StringComparer.Ordinal).FirstOrDefault();
            if (kingdom == null || state == null || coalition == null)
            {
                AddGovernmentAssertion(assertions, "government_native_npc_ruler_pressure", false,
                    "NPC ruler pressure requires a real non-player kingdom with an occupied coalition.", null);
                return;
            }
            bool opposed = caseId == "GOV-NATIVE-026";
            state.Level = opposed ? 5 : 1;
            state.Trust = 60;
            state.StanceTowardRuler = opposed ? -100 : 100;
            foreach (ReignGovernmentPartyRecord party in parties)
            {
                party.IsGoverningCoalition = party == coalition;
                party.IsDominant = party == coalition;
                party.PlanksCsv = opposed ? "Peace,PopularWelfare" : "RoyalAuthority,MilitaryStrength";
            }
            state.DominantPartyId = coalition.PartyId;
            state.CoalitionPartyIdsCsv = coalition.PartyId;
            var action = new ReignWorldActionRecord
            {
                ActionId = "government-cert-npc-ruler-" + caseId + "-" + Guid.NewGuid().ToString("N"),
                Type = ReignWorldActionType.DiplomacyDeclareWar,
                ActorHeroStringId = kingdom.Leader.StringId,
                ActorClanStringId = kingdom.Leader.Clan?.StringId ?? string.Empty,
                ActorKingdomStringId = kingdom.StringId,
                Reason = opposed ? "opposed aggressive war" : "defensive government-backed war"
            };
            bool allowed = government.TryAuthorizeAction(action, out ReignActionResult blocked);
            var business = government._business.FirstOrDefault(item => Same(item.ActionCorrelationId, action.ActionId));
            foreach (var seat in government.GetSeats(kingdom.StringId))
            {
                Hero member = FindHero(seat.HeroStringId);
                if (member != null && member != kingdom.Leader)
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(kingdom.Leader, member,
                        (opposed ? -100 : 100) - member.GetRelation(kingdom.Leader), false);
            }
            bool voted = business != null && government.RecommendBusiness(business.BusinessId, "accept", kingdom.Leader, out _)
                && government.VoteBusiness(business.BusinessId, kingdom.Leader, out _);
            bool responded = opposed || voted && government.ResolveBusiness(business.BusinessId, "accept", kingdom.Leader,
                business.WinningOptionId != "accept", out _);
            JObject snapshot = government.BuildPublicSnapshot(kingdom);
            bool expected = !allowed && blocked != null && voted && responded && (opposed
                ? business.Status == "decided" && business.WinningOptionId == "reject"
                    && !government.ResolveBusiness(business.BusinessId, "accept", kingdom.Leader, true, out _)
                : business.Status == "authorized");
            AddGovernmentAssertion(assertions, "government_native_npc_ruler_pressure",
                expected
                && snapshot.SelectToken("peoplePressure.additionalToNoblePoliticalPressure")
                    ?.Value<bool>() == true,
                "The real NPC ruler uses the same production recommendation, individual vote and response methods: lower authority permits its response while a binding opposed vote prevents a ruler veto. Autonomous scheduling and actual war execution require separate native evidence.",
                new JObject
                {
                    ["caseId"] = caseId,
                    ["kingdomId"] = kingdom.StringId,
                    ["rulerHeroId"] = kingdom.Leader.StringId,
                    ["authorityLevel"] = state.Level,
                    ["allowed"] = allowed,
                    ["blockMessage"] = blocked?.Message ?? string.Empty,
                    ["business"] = business == null ? null : JObject.FromObject(business)
                });
        }

        private static void GovernmentNativeKnowledgeCase(ReignGovernmentCampaignBehavior government, JArray assertions, Kingdom kingdom)
        {
            JObject snapshot = government.BuildPublicSnapshot(kingdom);
            bool partiesKnown = snapshot["parties"] is JArray parties && parties.Count >= 2
                && parties.Children<JObject>().All(party =>
                    !string.IsNullOrWhiteSpace(party.Value<string>("partyId"))
                    && party["planks"] is JArray && party.Value<int?>("seatCount") >= 0);
            AddGovernmentAssertion(assertions, "government_native_ruler_ambassador_knowledge",
                snapshot.Value<bool?>("authoritative") == true && partiesKnown
                && snapshot.Value<int?>("authorityLevel") >= 1
                && snapshot.Value<int?>("authorityLevel") <= 5
                && snapshot.Value<string>("decisionRule")?.IndexOf("ruler may agree",
                    StringComparison.OrdinalIgnoreCase) >= 0
                && snapshot["governingCoalitionPartyIds"] is JArray,
                "The authoritative public Government knowledge contains current authority, coalition, party agendas, speakers, stance, and the explicit ruler-agrees/government-refuses negotiation rule.", snapshot);
        }

        private static void GovernmentNativeHistoryCase(ReignGovernmentCampaignBehavior government, JArray assertions, Kingdom kingdom, string caseId)
        {
            ReignGovernmentStateRecord state = government.EnsureGovernment(kingdom);
            List<ReignGovernmentPartyRecord> parties = government.GetParties(kingdom?.StringId).ToList();
            if (kingdom == null || state == null || parties.Count == 0)
            {
                AddGovernmentAssertion(assertions, "government_native_history_resolution", false,
                    "History-backed resolution generation requires a loaded realm and occupied parties.", null);
                return;
            }
            var originalPlanks = parties.ToDictionary(item => item.PartyId,
                item => item.PlanksCsv, StringComparer.OrdinalIgnoreCase);
            Village raidVillage = null;
            Village.VillageStates originalVillageState = Village.VillageStates.Normal;
            string spymasterEffectId = string.Empty;
            Town spyTarget = null;
            bool prerequisiteActive = false;
            string temporaryPartyId = string.Empty;
            var originalSeatParties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string planks = caseId == "GOV-NATIVE-028"
                    ? "PopularWelfare,Agriculture,Justice,MilitaryStrength"
                    : "Espionage,Security,Justice,PopularWelfare";
                foreach (ReignGovernmentPartyRecord party in parties)
                {
                    party.PlanksCsv = planks;
                    party.Revision++;
                }
                if (caseId == "GOV-NATIVE-028")
                {
                    raidVillage = Settlement.All.Where(item => item?.IsVillage == true
                            && item.MapFaction == kingdom && item.Village != null)
                        .Select(item => item.Village)
                        .OrderBy(item => item.Settlement.StringId, StringComparer.Ordinal).FirstOrDefault();
                    if (raidVillage != null)
                    {
                        originalVillageState = raidVillage.VillageState;
                        raidVillage.VillageState = Village.VillageStates.Looted;
                        prerequisiteActive = raidVillage.VillageState == Village.VillageStates.Looted;
                    }
                }
                else
                {
                    spyTarget = kingdom.Fiefs.Where(item => item?.Settlement?.IsFortification == true)
                        .OrderBy(item => item.Settlement.StringId, StringComparer.Ordinal).FirstOrDefault();
#if !REIGN_EXCLUDE_COURT
                    spymasterEffectId = (ReignServices.Court is ReignCourtCampaignBehavior spymasterCourt
                        ? ReignSpymasterTestControls.AddGovernmentCertificationSubterfugeEffect(spymasterCourt, spyTarget) : null) ?? string.Empty;
                    prerequisiteActive = !string.IsNullOrWhiteSpace(spymasterEffectId)
                        && (ReignServices.Court?.GetSpymasterLoyaltyDelta(spyTarget) ?? 0f) != 0f;
#endif
                }

                bool MatchingTag(string tag) => caseId == "GOV-NATIVE-028"
                    ? tag.IndexOf("raid", StringComparison.OrdinalIgnoreCase) >= 0
                    : tag.IndexOf("subterfuge", StringComparison.OrdinalIgnoreCase) >= 0
                        || tag.IndexOf("agent", StringComparison.OrdinalIgnoreCase) >= 0
                        || tag.IndexOf("spy", StringComparison.OrdinalIgnoreCase) >= 0
                        || tag.IndexOf("rumor", StringComparison.OrdinalIgnoreCase) >= 0;
                HashSet<string> evidenceTags = CollectEvidenceTags(kingdom);
                List<ReignGovernmentPlank> fixturePlanks = ParsePlanks(planks);
                int season = (int)Math.Floor(CurrentDay() / ReignGovernmentRules.SeasonalMeetingDays);
                for (int index = 0; index < 4096 && string.IsNullOrWhiteSpace(temporaryPartyId); index++)
                {
                    string candidateId = "government_cert_history_" + caseId.ToLowerInvariant()
                        .Replace('-', '_') + "_" + index;
                    ReignGovernmentResolutionTemplate selected = ReignGovernmentResolutionCatalog.Templates
                        .Where(item => ReignGovernmentResolutionCatalog.IsEligible(
                            item, evidenceTags, fixturePlanks))
                        .OrderBy(item => ReignGovernmentRules.StableHash("resolution|" + kingdom.StringId
                            + "|" + candidateId + "|" + season + "|" + item.Id))
                        .FirstOrDefault();
                    if (MatchingTag(selected?.EvidenceTag ?? string.Empty)) temporaryPartyId = candidateId;
                }
                List<ReignGovernmentSeatRecord> fixtureSeats = government.GetSeats(kingdom.StringId).ToList();
                if (!string.IsNullOrWhiteSpace(temporaryPartyId) && fixtureSeats.Count > 0)
                {
                    foreach (ReignGovernmentSeatRecord seat in fixtureSeats)
                    {
                        originalSeatParties[seat.HeroStringId] = seat.PartyId;
                        seat.PartyId = temporaryPartyId;
                        seat.Revision++;
                    }
                    government._parties.Add(new ReignGovernmentPartyRecord
                    {
                        KingdomStringId = kingdom.StringId,
                        PartyId = temporaryPartyId,
                        Name = "Controlled History Certification Caucus",
                        PlanksCsv = planks,
                        SpeakerHeroStringId = fixtureSeats.Select(item => item.HeroStringId)
                            .OrderBy(item => item, StringComparer.Ordinal).FirstOrDefault() ?? string.Empty,
                        PartyLoyalty = 100,
                        Revision = 1
                    });
                    government.RecalculatePartyState(kingdom, state);
                }
                int before = government._resolutions.Count;
                ReignGovernmentMeetingRecord meeting = government.RunSeasonalMeeting(kingdom, state, true);
                List<ReignGovernmentResolutionRecord> generated = government._resolutions.Skip(before).ToList();
                ReignGovernmentResolutionRecord matching = generated.FirstOrDefault(record =>
                    MatchingTag(ReignGovernmentResolutionCatalog.Find(record.TemplateId)?.EvidenceTag ?? string.Empty));
                ReignGovernmentResolutionTemplate template = ReignGovernmentResolutionCatalog.Find(matching?.TemplateId);
                bool tangible = template != null && template.FirstRoute.Target > 0
                    && template.SecondRoute.Target > 0
                    && (template.FirstRoute.Action != template.SecondRoute.Action
                        || !Same(template.FirstRoute.Description, template.SecondRoute.Description));
                bool targetGrounded = matching != null
                    && (!string.IsNullOrWhiteSpace(matching.TargetSettlementStringId)
                        || !string.IsNullOrWhiteSpace(matching.TargetHeroStringId)
                        || !string.IsNullOrWhiteSpace(matching.TargetClanStringId));
                AddGovernmentAssertion(assertions, "government_native_history_resolution",
                    prerequisiteActive && meeting != null && matching != null && tangible && targetGrounded,
                    "A controlled real raid or Spymaster-history condition produced an evidence-tagged, specifically targeted, tangible two-route demand through the production seasonal meeting selector.",
                    new JObject
                    {
                        ["caseId"] = caseId,
                        ["prerequisiteActive"] = prerequisiteActive,
                        ["raidVillageId"] = raidVillage?.Settlement?.StringId ?? string.Empty,
                        ["spymasterTargetId"] = spyTarget?.Settlement?.StringId ?? string.Empty,
                        ["spymasterEffectId"] = spymasterEffectId,
                        ["temporaryPartyId"] = temporaryPartyId,
                        ["meetingId"] = meeting?.MeetingId ?? string.Empty,
                        ["generatedResolutionCount"] = generated.Count,
                        ["matchingResolutionId"] = matching?.ResolutionId ?? string.Empty,
                        ["templateId"] = template?.Id ?? string.Empty,
                        ["evidenceTag"] = template?.EvidenceTag ?? string.Empty,
                        ["targetSettlementId"] = matching?.TargetSettlementStringId ?? string.Empty,
                        ["targetHeroId"] = matching?.TargetHeroStringId ?? string.Empty,
                        ["targetClanId"] = matching?.TargetClanStringId ?? string.Empty,
                        ["firstRoute"] = template?.FirstRoute.Description ?? string.Empty,
                        ["secondRoute"] = template?.SecondRoute.Description ?? string.Empty
                    });
            }
            finally
            {
                if (raidVillage != null) raidVillage.VillageState = originalVillageState;
#if !REIGN_EXCLUDE_COURT
                if (ReignServices.Court is ReignCourtCampaignBehavior spymasterCleanupCourt)
                    ReignSpymasterTestControls.RemoveGovernmentCertificationSubterfugeEffect(spymasterCleanupCourt, spymasterEffectId);
#endif
                foreach (ReignGovernmentSeatRecord seat in government.GetSeats(kingdom?.StringId).ToList())
                {
                    if (originalSeatParties.TryGetValue(seat.HeroStringId, out string partyId))
                        seat.PartyId = partyId;
                }
                if (!string.IsNullOrWhiteSpace(temporaryPartyId))
                    government._parties.RemoveAll(item => Same(item.KingdomStringId, kingdom?.StringId)
                        && Same(item.PartyId, temporaryPartyId));
                foreach (ReignGovernmentPartyRecord party in parties)
                {
                    if (originalPlanks.TryGetValue(party.PartyId, out string value)) party.PlanksCsv = value;
                }
                if (kingdom != null && state != null) government.RecalculatePartyState(kingdom, state);
            }
        }
    }
}

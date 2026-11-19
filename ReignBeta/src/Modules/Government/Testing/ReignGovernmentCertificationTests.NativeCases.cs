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
        private static void GovernmentNativeUiCase(ReignGovernmentCampaignBehavior government, JArray assertions, string caseId,
            Kingdom playerKingdom, ReignGovernmentStateRecord state)
        {
            if (caseId == "GOV-NATIVE-002")
            {
                JObject snapshot = government.BuildPublicSnapshot(playerKingdom);
                AddGovernmentAssertion(assertions, "government_native_court_government_ui",
                    playerKingdom != null && state != null
                    && snapshot.Value<bool?>("authoritative") == true,
                    "The Court Government route is bound to the loaded player realm and its authoritative production snapshot; the required native screenshot and mouse evidence prove the actual button replacement.",
                    snapshot);
                return;
            }

            Kingdom foreign = Kingdom.All.Where(IsEligibleKingdom)
                .Where(item => item != playerKingdom)
                .OrderBy(item => item.StringId, StringComparer.Ordinal).FirstOrDefault();
            JObject foreignSnapshot = government.BuildPublicSnapshot(foreign);
            AddGovernmentAssertion(assertions, "government_native_keep_read_only_ui",
                playerKingdom != null && foreign != null
                && foreignSnapshot.Value<bool?>("authoritative") == true
                && foreignSnapshot["members"] == null
                && foreignSnapshot["outstandingDeals"] == null,
                "The native Keep/Castle Layout route resolves a foreign realm's privacy-safe read-only Government snapshot; the required native screenshot and mouse evidence prove Explore Keep and Family Chambers navigation.",
                foreignSnapshot);
        }

        private static void GovernmentNativePlayerMeetingCase(ReignGovernmentCampaignBehavior government, JArray assertions, Kingdom playerKingdom)
        {
            ReignGovernmentMeetingRecord meeting = government._meetings
                .Where(item => item.PlayerAttended && Same(item.KingdomStringId, playerKingdom?.StringId))
                .OrderByDescending(item => item.MeetingDay).FirstOrDefault();
            List<ReignGovernmentPartyRecord> occupied = government.GetParties(playerKingdom?.StringId)
                .Where(item => item.SeatCount > 0 && !string.IsNullOrWhiteSpace(item.SpeakerHeroStringId))
                .ToList();
            JObject statements = new JObject();
            try
            {
                if (meeting != null) statements = JObject.Parse(
                    string.IsNullOrWhiteSpace(meeting.SpeakerStatementsJson) ? "{}" : meeting.SpeakerStatementsJson);
            }
            catch { statements = new JObject(); }
            JArray attempted = statements["__attemptedPartyIds"] as JArray ?? new JArray();
            List<string> attemptedIds = attempted.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            bool statementsComplete = occupied.All(party =>
                !string.IsNullOrWhiteSpace(statements.Value<string>(party.PartyId)));
            bool exactCalls = meeting != null && occupied.Count > 0
                && attemptedIds.Count == occupied.Count
                && attemptedIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() == occupied.Count
                && occupied.All(party => attemptedIds.Contains(party.PartyId, StringComparer.OrdinalIgnoreCase))
                && meeting.ProviderCallCount == occupied.Count;
            AddGovernmentAssertion(assertions, "government_native_player_meeting_speaker_calls",
                exactCalls && statementsComplete,
                "A naturally attended seasonal meeting attempted exactly one provider turn for every occupied party speaker, retained one final statement per party, and made no per-member calls.",
                new JObject
                {
                    ["meetingId"] = meeting?.MeetingId ?? string.Empty,
                    ["occupiedSpeakerCount"] = occupied.Count,
                    ["providerCallCount"] = meeting?.ProviderCallCount ?? -1,
                    ["attemptedPartyIds"] = new JArray(attemptedIds),
                    ["statementPartyIds"] = new JArray(statements.Properties()
                        .Where(property => !property.Name.StartsWith("__", StringComparison.Ordinal))
                        .Select(property => property.Name))
                });
        }

        private static void GovernmentNativeNpcMeetingCase(ReignGovernmentCampaignBehavior government, JArray assertions)
        {
            Kingdom kingdom = Kingdom.All.Where(IsEligibleKingdom)
                .Where(item => item.Leader != Hero.MainHero)
                .OrderBy(item => item.StringId, StringComparer.Ordinal).FirstOrDefault();
            ReignGovernmentStateRecord state = government.EnsureGovernment(kingdom);
            int priorMeetings = government._meetings.Count;
            ReignGovernmentMeetingRecord meeting = kingdom == null || state == null
                ? null : government.RunSeasonalMeeting(kingdom, state, false);
            List<ReignGovernmentResolutionRecord> proposed = meeting == null
                ? new List<ReignGovernmentResolutionRecord>()
                : government._resolutions.Where(item => meeting.ResolutionIdsCsv.Split(',')
                    .Any(id => Same(id, item.ResolutionId))).ToList();
            bool dispositionsApplied = proposed.Count == 0 || proposed.All(item =>
                !Same(item.Status, "debate"));
            AddGovernmentAssertion(assertions, "government_native_npc_meetings_zero_calls",
                meeting != null && government._meetings.Count == priorMeetings + 1
                && !meeting.PlayerAttended && meeting.ProviderCallCount == 0
                && dispositionsApplied
                && government._meetings.Where(item => !item.PlayerAttended)
                    .All(item => item.ProviderCallCount == 0),
                "A real non-player realm completed a production seasonal meeting, resolved its proposals deterministically, and made zero provider calls.",
                new JObject
                {
                    ["kingdomId"] = kingdom?.StringId ?? string.Empty,
                    ["meetingId"] = meeting?.MeetingId ?? string.Empty,
                    ["providerCallCount"] = meeting?.ProviderCallCount ?? -1,
                    ["proposalCount"] = proposed.Count,
                    ["proposalStatuses"] = new JArray(proposed.Select(item => item.Status))
                });
        }

        private static void GovernmentNativeResolutionRouteCase(ReignGovernmentCampaignBehavior government, JArray assertions,
            Kingdom kingdom, ReignGovernmentStateRecord state, int routeIndex)
        {
            Settlement target = kingdom?.Fiefs.Select(item => item?.Settlement)
                .Where(item => item != null).OrderBy(item => item.StringId, StringComparer.Ordinal)
                .FirstOrDefault();
            ReignGovernmentPartyRecord party = government.GetParties(kingdom?.StringId)
                .Where(item => item.SeatCount > 0 && !string.IsNullOrWhiteSpace(item.SpeakerHeroStringId))
                .OrderBy(item => item.PartyId, StringComparer.Ordinal).FirstOrDefault();
            ReignGovernmentResolutionTemplate template = ReignGovernmentResolutionCatalog.Templates
                .Where(item =>
                {
                    ReignGovernmentResolutionRoute route = routeIndex == 0 ? item.FirstRoute : item.SecondRoute;
                    return !UsesImmediateGold(route.Action)
                        && route.Action != ReignGovernmentResolutionAction.GarrisonMaximum;
                })
                .OrderBy(item => item.Id, StringComparer.Ordinal).FirstOrDefault();
            if (kingdom == null || state == null || target == null || party == null || template == null)
            {
                AddGovernmentAssertion(assertions, "government_native_resolution_route_completed", false,
                    "The disposable realm lacks a real settlement, occupied party speaker, or trackable resolution route.", null);
                return;
            }

            ReignGovernmentResolutionRoute selected = routeIndex == 0 ? template.FirstRoute : template.SecondRoute;
            var record = new ReignGovernmentResolutionRecord
            {
                ResolutionId = "government-cert-route-" + routeIndex + "-" + Guid.NewGuid().ToString("N"),
                KingdomStringId = kingdom.StringId,
                TemplateId = template.Id,
                PartyId = party.PartyId,
                SpeakerHeroStringId = party.SpeakerHeroStringId,
                TargetSettlementStringId = target.StringId,
                ProposedDay = CurrentDay(),
                Status = "proposed",
                ConsequenceLevel = state.Level,
                EvidenceJson = new JObject { ["tag"] = template.EvidenceTag,
                    ["settlementId"] = target.StringId, ["certificationRoute"] = routeIndex }
                    .ToString(Newtonsoft.Json.Formatting.None),
                Revision = 1
            };
            int trustBefore = state.Trust;
            float loyaltyBefore = target.Town?.Loyalty ?? 0f;
            float prosperityBefore = target.Town?.Prosperity ?? 0f;
            float securityBefore = target.Town?.Security ?? 0f;
            float hearthBefore = target.Village?.Hearth ?? 0f;
            government._resolutions.Add(record);
            bool accepted = GovernmentNativeResolveSeasonalFixture(government, record, "route:" + routeIndex, kingdom, 1, out string acceptance);
            int progressed = accepted ? government.RecordResolutionProgress(kingdom.StringId, selected.Action,
                target.StringId, selected.Target, "release certification completed the exact selected route") : 0;
            bool measurableBenefit = (target.Town != null && (target.Town.Loyalty > loyaltyBefore
                    || target.Town.Prosperity > prosperityBefore || target.Town.Security > securityBefore))
                || (target.Village != null && target.Village.Hearth > hearthBefore);
            AddGovernmentAssertion(assertions, "government_native_resolution_route_completed",
                accepted && progressed == 1 && Same(record.Status, "completed")
                && record.SelectedRoute == routeIndex && record.RouteActionValue == (int)selected.Action
                && record.RequiredAmount == selected.Target && measurableBenefit && state.Trust == trustBefore,
                "The ruler selected the requested production route, target-specific progress completed it, and the saved completion reward measurably benefited the represented realm.",
                new JObject
                {
                    ["resolutionId"] = record.ResolutionId,
                    ["templateId"] = template.Id,
                    ["selectedRoute"] = record.SelectedRoute,
                    ["routeAction"] = selected.Action.ToString(),
                    ["requiredAmount"] = record.RequiredAmount,
                    ["status"] = record.Status,
                    ["outcome"] = record.Outcome,
                    ["acceptance"] = acceptance,
                    ["trustBefore"] = trustBefore,
                    ["trustAfter"] = state.Trust,
                    ["loyaltyBefore"] = loyaltyBefore,
                    ["loyaltyAfter"] = target.Town?.Loyalty ?? 0f,
                    ["prosperityBefore"] = prosperityBefore,
                    ["prosperityAfter"] = target.Town?.Prosperity ?? 0f,
                    ["securityBefore"] = securityBefore,
                    ["securityAfter"] = target.Town?.Security ?? 0f,
                    ["hearthBefore"] = hearthBefore,
                    ["hearthAfter"] = target.Village?.Hearth ?? 0f
                });
        }

        private static void GovernmentNativeCoalitionCase(ReignGovernmentCampaignBehavior government, JArray assertions, Kingdom kingdom,
            ReignGovernmentStateRecord state)
        {
            List<ReignGovernmentSeatRecord> seats = government.GetSeats(kingdom?.StringId)
                .OrderBy(item => item.HeroStringId, StringComparer.Ordinal).ToList();
            List<ReignGovernmentPartyRecord> parties = government.GetParties(kingdom?.StringId)
                .OrderBy(item => item.PartyId, StringComparer.Ordinal).ToList();
            while (kingdom != null && parties.Count < 3)
            {
                int index = parties.Count + 1;
                var synthetic = new ReignGovernmentPartyRecord
                {
                    KingdomStringId = kingdom.StringId,
                    PartyId = "government_cert_coalition_party_" + index,
                    Name = "Certification Coalition Party " + index,
                    PlanksCsv = index == 1 ? "Trade,PopularWelfare"
                        : index == 2 ? "Security,LocalAutonomy" : "Justice,Agriculture",
                    PartyLoyalty = 50,
                    Revision = 1
                };
                government._parties.Add(synthetic);
                parties.Add(synthetic);
            }
            if (kingdom == null || state == null || seats.Count < 3 || parties.Count < 3)
            {
                AddGovernmentAssertion(assertions, "government_native_fragmented_coalition", false,
                    "A fragmented coalition case requires at least three real occupied seats.",
                    new JObject { ["seatCount"] = seats.Count, ["partyCount"] = parties.Count });
                return;
            }
            for (int index = 0; index < seats.Count; index++)
            {
                seats[index].PartyId = parties[index % 3].PartyId;
                seats[index].Revision++;
            }
            government.RecalculatePartyState(kingdom, state);
            List<ReignGovernmentPartyRecord> coalition = government.GetGoverningCoalition(kingdom.StringId).ToList();
            int coalitionSeats = coalition.Sum(item => item.SeatCount);
            int largest = parties.Max(item => item.SeatCount);
            JObject snapshot = government.BuildPublicSnapshot(kingdom);
            List<string> publicIds = (snapshot["governingCoalitionPartyIds"] as JArray
                ?? new JArray()).Values<string>().ToList();
            bool exactPublic = coalition.All(item => publicIds.Contains(item.PartyId,
                    StringComparer.OrdinalIgnoreCase))
                && publicIds.All(id => coalition.Any(item => Same(item.PartyId, id)));
            AddGovernmentAssertion(assertions, "government_native_fragmented_coalition",
                largest * 2 <= seats.Count && coalition.Count >= 2
                && coalitionSeats * 2 > seats.Count && exactPublic
                && Same(state.CoalitionPartyIdsCsv, string.Join(",", coalition.Select(item => item.PartyId))),
                "A deliberately fragmented chamber formed a deterministic saved strict-majority coalition, and ruler/ambassador knowledge exposed that exact coalition.",
                new JObject
                {
                    ["seatCount"] = seats.Count,
                    ["largestPartySeats"] = largest,
                    ["coalitionSeatCount"] = coalitionSeats,
                    ["savedCoalitionIds"] = state.CoalitionPartyIdsCsv,
                    ["publicCoalitionIds"] = new JArray(publicIds)
                });
        }

        private static void GovernmentNativeRatifiedReductionCase(ReignGovernmentCampaignBehavior government, JArray assertions,
            Kingdom kingdom, ReignGovernmentStateRecord state)
        {
            List<ReignGovernmentSeatRecord> seats = government.GetSeats(kingdom?.StringId).ToList();
            List<ReignGovernmentPartyRecord> parties = government.GetParties(kingdom?.StringId).ToList();
            if (kingdom == null || state == null || kingdom.Leader != Hero.MainHero
                || seats.Count == 0 || parties.Count == 0)
            {
                AddGovernmentAssertion(assertions, "government_native_ratified_reduction_effects", false,
                    "The ratified reduction requires the real player ruler and an occupied government.", null);
                return;
            }
            state.Level = 5;
            state.Trust = 90;
            state.ReductionConsentFromLevel = 0;
            state.ReductionConsentHeroIdsCsv = string.Empty;
            state.ReductionConsentClanIdsCsv = string.Empty;
            foreach (ReignGovernmentPartyRecord party in parties)
            {
                party.PlanksCsv = "RoyalAuthority,Security";
                party.PartyLoyalty = 100;
                party.Revision++;
            }
            var relationBefore = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (ReignGovernmentSeatRecord seat in seats)
            {
                Hero member = FindHero(seat.HeroStringId);
                if (member != null)
                {
                    int current = member.GetRelation(kingdom.Leader);
                    if (current != 100) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                        kingdom.Leader, member, 100 - current, false);
                    relationBefore[member.StringId] = member.GetRelation(kingdom.Leader);
                }
                seat.PartyLoyalty = 100;
                seat.GovernmentLoyalty = 100;
                seat.Revision++;
            }
            List<Town> towns = kingdom.Fiefs.Where(item => item?.Settlement?.IsFortification == true)
                .ToList();
            foreach (Town town in towns) town.Loyalty = 100f;
            bool reduced = government.ReduceGovernmentLevel(kingdom, false, out string result);
            int votesFor = seats.Count(item => item.LastReductionVoteSupported);
            bool exactLoyalty = towns.Count > 0
                && towns.All(town => Math.Abs(town.Loyalty - 95f) < 0.01f);
            bool relationsUnchanged = relationBefore.All(pair =>
            {
                Hero member = FindHero(pair.Key);
                return member != null && member.GetRelation(kingdom.Leader) == pair.Value;
            });
            AddGovernmentAssertion(assertions, "government_native_ratified_reduction_effects",
                reduced && ReignGovernmentRules.ReductionRatified(votesFor, seats.Count)
                && state.Level == 4 && state.Trust == 90 && exactLoyalty
                && relationsUnchanged
                && state.ReductionConsentFromLevel == 0
                && string.IsNullOrWhiteSpace(state.ReductionConsentHeroIdsCsv)
                && string.IsNullOrWhiteSpace(state.ReductionConsentClanIdsCsv),
                "Every member cast the production reduction vote; the two-thirds ratified path applies exactly -5 settlement loyalty without changing retired Trust or applying forced noble relation losses.",
                new JObject
                {
                    ["votesFor"] = votesFor,
                    ["occupiedSeats"] = seats.Count,
                    ["authorityAfter"] = state.Level,
                    ["trustAfter"] = state.Trust,
                    ["fortificationLoyalties"] = new JArray(towns.Select(town =>
                        new JObject { ["settlementId"] = town.Settlement.StringId,
                            ["loyalty"] = town.Loyalty })),
                    ["result"] = result
                });
        }

        private static void GovernmentNativeForcedReductionCase(ReignGovernmentCampaignBehavior government, JArray assertions,
            Kingdom kingdom, ReignGovernmentStateRecord state)
        {
            List<Clan> candidateClans = kingdom?.Clans.Where(clan => clan != null
                    && clan != kingdom.RulingClan && clan != Clan.PlayerClan
                    && !clan.IsEliminated && clan.Leader != null)
                .OrderBy(clan => clan.StringId, StringComparer.Ordinal).ToList()
                ?? new List<Clan>();
            Clan preferredConsentingClan = candidateClans.FirstOrDefault(clan => clan.Heroes.Any(hero =>
                hero != null && hero != clan.Leader && hero.IsAlive && !hero.IsChild && !hero.IsPrisoner));
            Clan preferredNonConsentingClan = candidateClans.FirstOrDefault(clan => clan != preferredConsentingClan);
            GovernmentTemporaryLandholdingFixture landFixture = EnsureTemporaryLandholdingClans(government, kingdom,
                new[] { preferredConsentingClan, preferredNonConsentingClan });
            try
            {
            List<Clan> landholding = kingdom?.Clans.Where(clan => clan != null
                    && clan != kingdom.RulingClan && clan != Clan.PlayerClan
                    && !clan.IsEliminated && clan.Fiefs.Count > 0 && clan.Leader != null)
                .OrderBy(clan => clan.StringId, StringComparer.Ordinal).ToList()
                ?? new List<Clan>();
            Clan consentingClan = landholding.FirstOrDefault(clan => clan.Heroes.Any(hero =>
                hero != null && hero != clan.Leader && hero.IsAlive && !hero.IsChild && !hero.IsPrisoner));
            Clan nonConsentingClan = landholding.FirstOrDefault(clan => clan != consentingClan);
            Hero sameClanMember = consentingClan?.Heroes.Where(hero => hero != null
                    && hero != consentingClan.Leader && hero.IsAlive && !hero.IsChild && !hero.IsPrisoner)
                .OrderBy(hero => hero.StringId, StringComparer.Ordinal).FirstOrDefault();
            List<ReignGovernmentSeatRecord> originalSeats = government.GetSeats(kingdom?.StringId).ToList();
            Hero individualConsent = originalSeats.Select(item => FindHero(item.HeroStringId))
                .Where(hero => hero != null && hero != Hero.MainHero && hero.Clan != Clan.PlayerClan
                    && hero.Clan != consentingClan && hero.Clan?.Leader != hero)
                .OrderBy(hero => hero.StringId, StringComparer.Ordinal).FirstOrDefault();
            Hero nonLandholder = originalSeats.Select(item => FindHero(item.HeroStringId))
                .Where(hero => hero != null && hero != individualConsent && hero != Hero.MainHero
                    && hero.Clan != Clan.PlayerClan && hero.Clan != consentingClan
                    && hero.Clan?.Leader != hero && hero != nonConsentingClan?.Leader)
                .OrderBy(hero => hero.StringId, StringComparer.Ordinal).FirstOrDefault();
            Hero playerClanMember = Clan.PlayerClan?.Heroes.Where(hero => hero != null
                    && hero != Hero.MainHero && hero.IsAlive && !hero.IsChild && !hero.IsPrisoner)
                .OrderBy(hero => hero.StringId, StringComparer.Ordinal).FirstOrDefault() ?? Hero.MainHero;
            ReignGovernmentPartyRecord party = government.GetParties(kingdom?.StringId)
                .OrderBy(item => item.PartyId, StringComparer.Ordinal).FirstOrDefault();
            bool rolesAvailable = kingdom != null && state != null && kingdom.Leader == Hero.MainHero
                && consentingClan?.Leader != null && nonConsentingClan?.Leader != null
                && sameClanMember != null && individualConsent != null && nonLandholder != null
                && playerClanMember != null && party != null;
            if (!rolesAvailable)
            {
                AddGovernmentAssertion(assertions, "government_native_forced_reduction_effects", false,
                    "The controlled forced-reduction scenario must contain two landholding clans, a second member of the consenting clan, an individual notable, a non-consenting nonlandholder, and a player-clan member.",
                    new JObject
                    {
                        ["landholdingClanCount"] = landholding.Count,
                        ["hasConsentingClanMember"] = sameClanMember != null,
                        ["hasIndividualConsent"] = individualConsent != null,
                        ["hasNonLandholder"] = nonLandholder != null,
                        ["hasPlayerClanMember"] = playerClanMember != null
                    });
                return;
            }

            state.Level = 5;
            state.Trust = 100;
            state.ReductionConsentFromLevel = 0;
            state.ReductionConsentHeroIdsCsv = string.Empty;
            state.ReductionConsentClanIdsCsv = string.Empty;
            foreach (ReignGovernmentPartyRecord item in government.GetParties(kingdom.StringId))
            {
                item.PlanksCsv = "RepresentativeAuthority,PopularWelfare";
                item.PartyLoyalty = 100;
                item.Revision++;
            }
            foreach (Hero hero in new[] { consentingClan.Leader, nonConsentingClan.Leader,
                sameClanMember, individualConsent, nonLandholder, playerClanMember })
                EnsureGovernmentCertificationSeat(government, kingdom, party, hero);
            List<ReignGovernmentSeatRecord> voters = government.GetSeats(kingdom.StringId).ToList();
            foreach (ReignGovernmentSeatRecord seat in voters)
            {
                Hero member = FindHero(seat.HeroStringId);
                if (member != null)
                {
                    int current = member.GetRelation(kingdom.Leader);
                    if (current != 100) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                        kingdom.Leader, member, 100 - current, false);
                }
                seat.PartyId = party.PartyId;
                seat.PartyLoyalty = 100;
                seat.GovernmentLoyalty = 100;
                seat.Revision++;
            }
            bool clanConsentRecorded = government.TryRecordReductionConsent(kingdom.Leader,
                consentingClan.Leader, out string clanConsentResult);
            bool individualConsentRecorded = government.TryRecordReductionConsent(kingdom.Leader,
                individualConsent, out string individualConsentResult);
            var before = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (Hero hero in new[] { consentingClan.Leader, nonConsentingClan.Leader,
                sameClanMember, individualConsent, nonLandholder, playerClanMember })
                before[hero.StringId] = hero.GetRelation(kingdom.Leader);
            List<Town> towns = kingdom.Fiefs.Where(item => item?.Settlement?.IsFortification == true)
                .ToList();
            foreach (Town town in towns) town.Loyalty = 100f;
            bool reduced = government.ReduceGovernmentLevel(kingdom, true, out string reductionResult);
            int votesFor = voters.Count(item => item.LastReductionVoteSupported);
            int Delta(Hero hero) => hero.GetRelation(kingdom.Leader) - before[hero.StringId];
            bool exact = reduced && !ReignGovernmentRules.ReductionRatified(votesFor, voters.Count)
                && state.Level == 4 && state.Trust == 100 && towns.Count > 0
                && towns.All(town => Math.Abs(town.Loyalty - 30f) < 0.01f)
                && Delta(nonConsentingClan.Leader) == -70
                && Delta(nonLandholder) == -40
                && Delta(consentingClan.Leader) == 0
                && Delta(sameClanMember) == 0
                && Delta(individualConsent) == 0
                && Delta(playerClanMember) == 0
                && state.ReductionConsentFromLevel == 0
                && string.IsNullOrWhiteSpace(state.ReductionConsentHeroIdsCsv)
                && string.IsNullOrWhiteSpace(state.ReductionConsentClanIdsCsv);
            AddGovernmentAssertion(assertions, "government_native_forced_reduction_effects",
                clanConsentRecorded && individualConsentRecorded && exact,
                "The actual failed vote followed by forced reduction applies -70 loyalty, -70 to a non-consenting landholding clan leader, -40 to a non-consenting nonlandholder, and no loss to consented members or the player clan; tags are consumed once while legacy Trust stays inert.",
                new JObject
                {
                    ["votesFor"] = votesFor,
                    ["occupiedSeats"] = voters.Count,
                    ["authorityAfter"] = state.Level,
                    ["trustAfter"] = state.Trust,
                    ["nonConsentingClanLeaderDelta"] = Delta(nonConsentingClan.Leader),
                    ["nonConsentingNonLandholderDelta"] = Delta(nonLandholder),
                    ["consentingClanLeaderDelta"] = Delta(consentingClan.Leader),
                    ["consentingClanMemberDelta"] = Delta(sameClanMember),
                    ["individualConsentDelta"] = Delta(individualConsent),
                    ["playerClanMemberDelta"] = Delta(playerClanMember),
                    ["fortificationLoyalties"] = new JArray(towns.Select(town =>
                        new JObject { ["settlementId"] = town.Settlement.StringId,
                            ["loyalty"] = town.Loyalty })),
                    ["clanConsentResult"] = clanConsentResult,
                    ["individualConsentResult"] = individualConsentResult,
                    ["reductionResult"] = reductionResult
                });
            }
            finally
            {
                landFixture?.Dispose();
            }
        }
    }
}

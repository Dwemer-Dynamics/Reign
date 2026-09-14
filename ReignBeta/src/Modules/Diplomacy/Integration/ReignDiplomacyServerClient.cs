using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Government;
using ReignBeta.Settings;
using ReignBeta.Runtime;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Integration
{
    public sealed class ReignDiplomacyEvaluationResult
    {
        public bool Ok;
        public string Error = string.Empty;
        public string Status = string.Empty;
    }

    public sealed class ReignRandomDiplomacyTestResult
    {
        public bool Ok;
        public bool Idempotent;
        public string Error = string.Empty;
        public string Status = string.Empty;
        public string RulerName = string.Empty;
        public string InitiativeSummary = string.Empty;
        public string EventId = string.Empty;
        public string ActionId = string.Empty;
        public string Command = string.Empty;
        public string ActionLabel = string.Empty;
        public string ActorKingdomName = string.Empty;
        public string TargetKingdomName = string.Empty;
        public ReignWorldActionRecord Action;
    }

    public sealed class ReignPoliticalPressureNativeEffect
    {
        public string EffectId = string.Empty;
        public string EffectType = string.Empty;
        public string TargetId = string.Empty;
        public int Amount;
        public string Status = string.Empty;
    }

    public sealed class ReignPoliticalPressureNotice
    {
        public string IncidentId = string.Empty;
        public string TimelineId = string.Empty;
        public string Headline = string.Empty;
        public string Narrative = string.Empty;
        public string Polarity = string.Empty;
        public string Channel = string.Empty;
        public string Severity = string.Empty;
        public string OriginKingdomName = string.Empty;
        public string TargetKingdomName = string.Empty;
        public string OriginRulerName = string.Empty;
        public string TargetRulerName = string.Empty;
        public string OriginStance = string.Empty;
        public string TargetStance = string.Empty;
        public int OriginPressureBefore;
        public int OriginPressureAfter;
        public int TargetPressureBefore;
        public int TargetPressureAfter;
        public float WorldDay;
        public readonly List<ReignPoliticalPressureNativeEffect> NativeEffects =
            new List<ReignPoliticalPressureNativeEffect>();
    }

    public sealed class ReignClanConflictNotice
    {
        public string IncidentId = string.Empty;
        public string TimelineId = string.Empty;
        public string Headline = string.Empty;
        public string Narrative = string.Empty;
        public string KingdomName = string.Empty;
        public string RulerName = string.Empty;
        public string ClanAName = string.Empty;
        public string ClanBName = string.Empty;
        public string WinningSide = string.Empty;
        public bool MediationSucceeded;
        public int CharmSkill;
        public int CharmChanceBasisPoints;
        public int CharmRoll;
        public float WorldDay;
        public readonly List<string> InvolvedA = new List<string>();
        public readonly List<string> InvolvedB = new List<string>();
    }

    public static partial class ReignServerClient
    {
        public static async Task<ReignDiplomacyEvaluationResult> EvaluateWorldDiplomacyAsync(bool retryFailedEvaluation = false)
        {
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && (!settings.UseLocalServer || !settings.AllowAutonomousWorldTicks))
                {
                    return new ReignDiplomacyEvaluationResult { Ok = true, Status = "disabled" };
                }

                JObject snapshot = BuildDiplomacyWorldSnapshot();
                snapshot["retryFailedEvaluation"] = retryFailedEvaluation;
                JObject response = await PostJsonAsync("/diplomacy/director/evaluate", snapshot).ConfigureAwait(false);
                return new ReignDiplomacyEvaluationResult
                {
                    Ok = response.Value<bool?>("ok") == true,
                    Error = ReadString(response, "error", string.Empty),
                    Status = ReadString(response, "status", string.Empty)
                };
            }
            catch (Exception ex)
            {
                ReignLog.Warn("World diplomacy evaluation failed: " + ex.Message);
                return new ReignDiplomacyEvaluationResult { Ok = false, Error = ex.Message, Status = "unavailable" };
            }
        }

        public static async Task<ReignDiplomacyEvaluationResult>
            EvaluatePoliticalPressureAsync()
        {
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && (!settings.UseLocalServer
                    || !settings.AllowAutonomousWorldTicks))
                {
                    return new ReignDiplomacyEvaluationResult
                    {
                        Ok = true,
                        Status = "disabled"
                    };
                }

                JObject response = await PostJsonAsync(
                    "/political-pressure/evaluate",
                    BuildDiplomacyWorldSnapshot()).ConfigureAwait(false);
                return new ReignDiplomacyEvaluationResult
                {
                    Ok = response.Value<bool?>("ok") == true,
                    Error = ReadString(response, "error", string.Empty),
                    Status = ReadString(response, "status", string.Empty)
                };
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Political pressure evaluation failed: "
                    + ex.Message);
                return new ReignDiplomacyEvaluationResult
                {
                    Ok = false,
                    Error = ex.Message,
                    Status = "unavailable"
                };
            }
        }

        public static async Task<List<ReignDiplomacyAnnouncement>> FetchDiplomacyAnnouncementsAsync()
        {
            List<ReignDiplomacyAnnouncement> results = new List<ReignDiplomacyAnnouncement>();
            string route = "/diplomacy/events/next?campaignId=" + Uri.EscapeDataString(GetCampaignId()) + "&limit=10";
            JObject response = await GetJsonAsync(route).ConfigureAwait(false);
            if (response.Value<bool?>("ok") != true || !(response["events"] is JArray events))
            {
                return results;
            }

            foreach (JObject item in events.OfType<JObject>())
            {
                results.Add(new ReignDiplomacyAnnouncement
                {
                    EventId = ReadString(item, "eventId", string.Empty),
                    ActionId = ReadString(item, "actionId", string.Empty),
                    Title = ReadString(item, "title", "Diplomatic Event"),
                    Outcome = ReadString(item, "outcome", string.Empty),
                    Summary = ReadString(item, "summary", string.Empty),
                    Terms = ReadString(item, "termsText", string.Empty),
                    ActorHeroStringId = ReadString(item, "actorHeroId", string.Empty),
                    ActorName = ReadString(item, "actorName", string.Empty),
                    ActorKingdomName = ReadString(item, "actorKingdomName", string.Empty),
                    ActorKingdomStringId = ReadString(item, "actorKingdomId", string.Empty),
                    ActorPublicReason = ReadString(item, "actorPublicReason", string.Empty),
                    TargetHeroStringId = ReadString(item, "targetHeroId", string.Empty),
                    TargetName = ReadString(item, "targetName", string.Empty),
                    TargetKingdomName = ReadString(item, "targetKingdomName", string.Empty),
                    TargetKingdomStringId = ReadString(item, "targetKingdomId", string.Empty),
                    TargetPublicReason = ReadString(item, "targetPublicReason", string.Empty),
                    WorldDay = ReadFloat(item, "worldDay", 0f),
                    Accepted = ReadBool(item, "accepted", false)
                });
            }

            return results;
        }

        public static async Task AcknowledgeDiplomacyAnnouncementAsync(string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                return;
            }

            await PostJsonAsync("/diplomacy/events/acknowledge", new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["eventId"] = eventId
            }).ConfigureAwait(false);
        }

        public static async Task<List<ReignPoliticalPressureNotice>> FetchPoliticalPressureNoticesAsync()
        {
            List<ReignPoliticalPressureNotice> results = new List<ReignPoliticalPressureNotice>();
            string timelineId = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
            string route = "/political-pressure/notices?campaignId=" + Uri.EscapeDataString(GetCampaignId())
                + "&timelineId=" + Uri.EscapeDataString(timelineId) + "&limit=5";
            JObject response = await GetJsonAsync(route).ConfigureAwait(false);
            if (response.Value<bool?>("ok") != true || !(response["notices"] is JArray notices)) return results;
            foreach (JObject item in notices.OfType<JObject>())
            {
                ReignPoliticalPressureNotice notice = new ReignPoliticalPressureNotice
                {
                    IncidentId = ReadString(item, "incident_id", string.Empty),
                    TimelineId = ReadString(item, "timeline_id", timelineId),
                    Headline = ReadString(item, "headline", "Political Pressure"),
                    Narrative = ReadString(item, "narrative", string.Empty),
                    Polarity = ReadString(item, "polarity", string.Empty),
                    Channel = ReadString(item, "channel", string.Empty),
                    Severity = ReadString(item, "severity", string.Empty),
                    OriginKingdomName = ReadString(item, "origin_kingdom_name", string.Empty),
                    TargetKingdomName = ReadString(item, "target_kingdom_name", string.Empty),
                    OriginRulerName = ReadString(item, "origin_ruler_name", string.Empty),
                    TargetRulerName = ReadString(item, "target_ruler_name", string.Empty),
                    OriginStance = ReadString(item, "origin_stance", string.Empty),
                    TargetStance = ReadString(item, "target_stance", string.Empty),
                    OriginPressureBefore = item.Value<int?>("origin_pressure_before") ?? 0,
                    OriginPressureAfter = item.Value<int?>("origin_pressure_after") ?? 0,
                    TargetPressureBefore = item.Value<int?>("target_pressure_before") ?? 0,
                    TargetPressureAfter = item.Value<int?>("target_pressure_after") ?? 0,
                    WorldDay = ReadFloat(item, "world_day", 0f)
                };
                foreach (JObject effect in (item["nativeEffects"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    notice.NativeEffects.Add(new ReignPoliticalPressureNativeEffect
                    {
                        EffectId = ReadString(effect, "effect_id", string.Empty),
                        EffectType = ReadString(effect, "effect_type", string.Empty),
                        TargetId = ReadString(effect, "target_id", string.Empty),
                        Amount = effect.Value<int?>("amount") ?? 0,
                        Status = ReadString(effect, "status", string.Empty)
                    });
                }
                results.Add(notice);
            }
            return results;
        }

        public static async Task AcknowledgePoliticalPressureNoticeAsync(string incidentId,
            string timelineId, float worldDay, JArray effectReceipts)
        {
            if (string.IsNullOrWhiteSpace(incidentId)) return;
            await PostJsonAsync("/political-pressure/acknowledge", new JObject
            {
                ["campaignId"] = GetCampaignId(), ["timelineId"] = timelineId ?? "main",
                ["incidentId"] = incidentId, ["worldDay"] = worldDay,
                ["effectReceipts"] = effectReceipts ?? new JArray()
            }).ConfigureAwait(false);
        }

        public static async Task<List<ReignClanConflictNotice>> FetchClanConflictNoticesAsync()
        {
            List<ReignClanConflictNotice> results = new List<ReignClanConflictNotice>();
            string timelineId = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
            string route = "/clan-conflicts/notices?campaignId=" + Uri.EscapeDataString(GetCampaignId())
                + "&timelineId=" + Uri.EscapeDataString(timelineId) + "&limit=10";
            JObject response = await GetJsonAsync(route).ConfigureAwait(false);
            if (response.Value<bool?>("ok") != true || !(response["notices"] is JArray notices))
                return results;
            foreach (JObject item in notices.OfType<JObject>())
            {
                ReignClanConflictNotice notice = new ReignClanConflictNotice
                {
                    IncidentId = ReadString(item, "incident_id", string.Empty),
                    TimelineId = ReadString(item, "timeline_id", timelineId),
                    Headline = ReadString(item, "headline", "Clan Conflict"),
                    Narrative = ReadString(item, "narrative", string.Empty),
                    KingdomName = ReadString(item, "kingdom_name", string.Empty),
                    RulerName = ReadString(item, "ruler_name", string.Empty),
                    ClanAName = ReadString(item, "clan_a_name", string.Empty),
                    ClanBName = ReadString(item, "clan_b_name", string.Empty),
                    WinningSide = ReadString(item, "winning_side", string.Empty),
                    MediationSucceeded = item.Value<int?>("mediation_succeeded") == 1,
                    CharmSkill = item.Value<int?>("charm_skill") ?? 0,
                    CharmChanceBasisPoints = item.Value<int?>("charm_chance_bp") ?? 0,
                    CharmRoll = item.Value<int?>("charm_roll") ?? 0,
                    WorldDay = ReadFloat(item, "world_day", 0f)
                };
                foreach (JToken name in item["involved_a_names_json"] is JArray namesA
                    ? namesA : ParseJsonArray(ReadString(item, "involved_a_names_json", "[]")))
                    if (name.Type == JTokenType.String) notice.InvolvedA.Add(name.ToString());
                foreach (JToken name in item["involved_b_names_json"] is JArray namesB
                    ? namesB : ParseJsonArray(ReadString(item, "involved_b_names_json", "[]")))
                    if (name.Type == JTokenType.String) notice.InvolvedB.Add(name.ToString());
                results.Add(notice);
            }
            return results;
        }

        public static async Task AcknowledgeClanConflictNoticeAsync(string incidentId,
            string timelineId, float worldDay)
        {
            if (string.IsNullOrWhiteSpace(incidentId)) return;
            await PostJsonAsync("/clan-conflicts/acknowledge", new JObject
            {
                ["campaignId"] = GetCampaignId(), ["timelineId"] = timelineId ?? "main",
                ["incidentId"] = incidentId, ["worldDay"] = worldDay
            }).ConfigureAwait(false);
        }

        public static async Task<ReignRandomDiplomacyTestResult> QueueRandomDiplomacyPopupTestAsync()
        {
            ReignRandomDiplomacyTestResult result = new ReignRandomDiplomacyTestResult();
            try
            {
                JObject snapshot = BuildDiplomacyWorldSnapshot();
                snapshot["debugMcmEnabled"] = ReignBetaSettings.Instance?.McmTestModeEnabled == true;
                snapshot["commandId"] = Guid.NewGuid().ToString("N");
                snapshot["source"] = "mcm_debug";
                JObject response = await PostJsonAsync("/diplomacy/debug/random-event", snapshot).ConfigureAwait(false);
                result.Ok = response.Value<bool?>("ok") == true;
                result.Idempotent = response.Value<bool?>("idempotent") == true;
                result.Error = ReadString(response, "error", string.Empty);
                result.Status = ReadString(response, "status", string.Empty);
                result.RulerName = ReadString(response, "rulerName", string.Empty);
                result.EventId = ReadString(response, "eventId", string.Empty);
                result.ActionId = ReadString(response, "actionId", string.Empty);
                result.Command = ReadString(response, "command", string.Empty);
                result.ActionLabel = ReadString(response, "actionLabel", result.Command.Replace('_', ' '));
                result.ActorKingdomName = ReadString(response, "actorKingdomName", string.Empty);
                result.TargetKingdomName = ReadString(response, "targetKingdomName", string.Empty);
                if (response["record"] is JObject record)
                {
                    result.Action = ParseActionRecord(record);
                }
                if (response["initiativeAttempts"] is JArray attempts)
                {
                    result.InitiativeSummary = string.Join(", ", attempts.OfType<JObject>().Select(x =>
                        ReadString(x, "family", "unknown") + " "
                        + ((x.Value<double?>("finalChance") ?? 0d) * 100d).ToString("0.00") + "% / rolled "
                        + ((x.Value<double?>("roll") ?? 0d) * 100d).ToString("0.00")));
                }
                if (result.Ok && string.Equals(result.Status, "action_queued", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(result.ActionId) && result.Action == null)
                {
                    result.Ok = false;
                    result.Error = "The server queued the diplomacy test but did not return an executable action record.";
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                ReignLog.Warn("Random diplomacy popup test failed: " + ex.Message);
            }
            return result;
        }

        internal static JObject BuildDiplomacyWorldSnapshot()
        {
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            JArray kingdoms = new JArray();
            JArray settlements = new JArray();
            JArray clans = new JArray();
            JArray wars = new JArray();
            JArray agreements = new JArray();
            JArray agreementHistory = new JArray();
            JArray warOrigins = new JArray();
            JArray treatyObligations = new JArray();
            JArray relations = new JArray();
            JArray prisoners = new JArray();
            JArray allianceMarriageCandidates = new JArray();
            IAllianceCampaignBehavior alliances = TaleWorlds.CampaignSystem.Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>();
            ITradeAgreementsCampaignBehavior trades = TaleWorlds.CampaignSystem.Campaign.Current?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
            List<Kingdom> live = Kingdom.All.Where(x => x != null && !x.IsEliminated && x.Leader != null).ToList();

            foreach (Clan clan in Clan.All.Where(x => x != null && !x.IsEliminated && !x.IsBanditFaction))
            {
                clans.Add(new JObject
                {
                    ["clanId"] = clan.StringId ?? string.Empty,
                    ["name"] = clan.Name?.ToString() ?? string.Empty,
                    ["leaderHeroId"] = clan.Leader?.StringId ?? string.Empty,
                    ["leaderName"] = clan.Leader?.Name?.ToString() ?? string.Empty,
                    ["leaderIsAlive"] = clan.Leader?.IsAlive == true,
                    ["leaderIsChild"] = clan.Leader?.IsChild == true,
                    ["kingdomId"] = clan.Kingdom?.StringId ?? string.Empty,
                    ["kingdomName"] = clan.Kingdom?.InformalName?.ToString() ?? string.Empty,
                    ["isMercenary"] = clan.IsClanTypeMercenary || clan.IsUnderMercenaryService,
                    ["isRebelClan"] = clan.IsRebelClan,
                    ["tier"] = clan.Tier,
                    ["gold"] = clan.Gold,
                    ["renown"] = clan.Renown,
                    ["influence"] = clan.Influence,
                    ["fiefCount"] = clan.Fiefs?.Count ?? 0,
                    ["members"] = new JArray(clan.Heroes
                        .Where(x => x != null)
                        .OrderBy(x => x.StringId)
                        .Select(x => new JObject
                        {
                            ["heroId"] = x.StringId ?? string.Empty,
                            ["name"] = x.Name?.ToString() ?? string.Empty,
                            ["isAlive"] = x.IsAlive,
                            ["isChild"] = x.IsChild,
                            ["isLord"] = x.IsLord
                        }))
                });
            }

            foreach (Kingdom kingdom in live)
            {
                Hero ruler = kingdom.Leader;
                ReignRebellionMovementRecord civilWar = ReignRebellionCampaignBehavior.Instance?.Movements.FirstOrDefault(x => x != null
                    && string.Equals(x.Stage, "civil_war", StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(x.ParentKingdomStringId, kingdom.StringId, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(x.RebelKingdomStringId, kingdom.StringId, StringComparison.OrdinalIgnoreCase)));
                int treasury = kingdom.Clans.Where(x => x != null).Sum(x => Math.Max(0, x.Gold));
                float dailyGoldChange = kingdom.Clans.Where(x => x != null).Sum(x => TaleWorlds.CampaignSystem.Campaign.Current.Models.ClanFinanceModel.CalculateClanGoldChange(x).ResultNumber);
                float settlementProsperity = kingdom.Fiefs.Where(x => x != null).Sum(x => x.Prosperity);
                int garrisonStrength = kingdom.Fiefs.Where(x => x?.GarrisonParty?.MemberRoster != null).Sum(x => x.GarrisonParty.MemberRoster.TotalManCount);
                Hero protectedHeir = ProtectedRoyalHeir(kingdom);
                kingdoms.Add(new JObject
                {
                    ["kingdomId"] = kingdom.StringId ?? string.Empty,
                    ["name"] = kingdom.InformalName?.ToString() ?? kingdom.Name?.ToString() ?? string.Empty,
                    ["cultureId"] = kingdom.Culture?.StringId ?? string.Empty,
                    ["leaderHeroId"] = ruler.StringId ?? string.Empty,
                    ["leaderName"] = ruler.Name?.ToString() ?? string.Empty,
                    ["rulingClanId"] = kingdom.RulingClan?.StringId ?? string.Empty,
                    ["isPlayerKingdom"] = kingdom == playerKingdom,
                    ["isRebelRealm"] = civilWar != null && string.Equals(civilWar.RebelKingdomStringId, kingdom.StringId, StringComparison.OrdinalIgnoreCase),
                    ["civilWarOpponentKingdomId"] = civilWar == null ? string.Empty
                        : string.Equals(civilWar.ParentKingdomStringId, kingdom.StringId, StringComparison.OrdinalIgnoreCase) ? civilWar.RebelKingdomStringId : civilWar.ParentKingdomStringId,
                    ["strength"] = kingdom.CurrentTotalStrength,
                    ["treasury"] = treasury,
                    ["leaderGold"] = ruler.Gold,
                    ["dailyGoldChange"] = dailyGoldChange,
                    ["settlementProsperity"] = settlementProsperity,
                    ["garrisonStrength"] = garrisonStrength,
                    ["clanCount"] = kingdom.Clans.Count,
                    ["fiefCount"] = kingdom.Fiefs.Count,
                    ["townCount"] = kingdom.Fiefs.Count(x => x?.Settlement?.IsTown == true),
                    ["castleCount"] = kingdom.Fiefs.Count(x => x?.Settlement?.IsCastle == true),
                    ["rulerIsPrisoner"] = ruler.IsPrisoner,
                    ["capturedNobleCount"] = Hero.AllAliveHeroes.Count(x => x != null && x.IsLord && x.IsPrisoner && x.MapFaction == kingdom),
                    ["rulerAge"] = ruler.Age,
                    ["protectedHeirHeroId"] = protectedHeir?.StringId ?? string.Empty,
                    ["rulerTraits"] = BuildTraitProfile(ruler),
                    ["rulerSkills"] = BuildSkillProfile(ruler),
                    ["government"] = ReignGovernmentCampaignBehavior.Instance?.BuildPublicSnapshot(kingdom)
                        ?? new JObject { ["available"] = false, ["authoritative"] = true },
                    ["enemies"] = new JArray(live.Where(kingdom.IsAtWarWith).Select(x => x.StringId)),
                    ["borderKingdomIds"] = new JArray(live
                        .Where(x => x != kingdom && KingdomsShareBorder(kingdom, x))
                        .Select(x => x.StringId))
                });
                AddAllianceMarriageCandidates(allianceMarriageCandidates, kingdom);

                foreach (Town fief in kingdom.Fiefs.Where(x => x != null))
                {
                    settlements.Add(new JObject
                    {
                        ["settlementId"] = fief.Settlement?.StringId ?? string.Empty,
                        ["name"] = fief.Name?.ToString() ?? string.Empty,
                        ["ownerKingdomId"] = kingdom.StringId ?? string.Empty,
                        ["ownerClanId"] = fief.OwnerClan?.StringId ?? string.Empty,
                        ["cultureId"] = fief.Culture?.StringId ?? string.Empty,
                        ["isTown"] = fief.Settlement?.IsTown == true,
                        ["isCastle"] = fief.Settlement?.IsCastle == true,
                        ["prosperity"] = fief.Prosperity,
                        ["loyalty"] = fief.Loyalty,
                        ["security"] = fief.Security,
                    ["garrison"] = fief.GarrisonParty?.MemberRoster?.TotalManCount ?? 0
                    });
                }
            }

            foreach (Hero prisoner in Hero.AllAliveHeroes.Where(x => x != null && x.IsLord && x.IsPrisoner && x.MapFaction is Kingdom))
            {
                prisoners.Add(new JObject
                {
                    ["heroId"] = prisoner.StringId ?? string.Empty,
                    ["name"] = prisoner.Name?.ToString() ?? string.Empty,
                    ["kingdomId"] = prisoner.MapFaction?.StringId ?? string.Empty,
                    ["holderKingdomId"] = prisoner.PartyBelongedToAsPrisoner?.MapFaction?.StringId ?? string.Empty
                });
            }

            for (int i = 0; i < live.Count; i++)
            {
                for (int j = i + 1; j < live.Count; j++)
                {
                    Kingdom first = live[i];
                    Kingdom second = live[j];
                    relations.Add(new JObject
                    {
                        ["kingdomAId"] = first.StringId,
                        ["kingdomBId"] = second.StringId,
                        ["rulerRelation"] = first.Leader?.GetRelation(second.Leader) ?? 0,
                        ["sameCulture"] = first.Culture == second.Culture,
                        ["sharesBorder"] = KingdomsShareBorder(first, second)
                    });
                    if (first.IsAtWarWith(second))
                    {
                        StanceLink stance = first.GetStanceWith(second);
                        wars.Add(new JObject
                        {
                            ["kingdomAId"] = first.StringId,
                            ["kingdomBId"] = second.StringId,
                            ["startedDay"] = (float)stance.WarStartDate.ToDays,
                            ["casualtiesA"] = stance.GetCasualties(first),
                            ["casualtiesB"] = stance.GetCasualties(second),
                            ["raidsA"] = stance.GetSuccessfulRaids(first),
                            ["raidsB"] = stance.GetSuccessfulRaids(second),
                            ["siegesA"] = stance.GetSuccessfulSieges(first),
                            ["siegesB"] = stance.GetSuccessfulSieges(second)
                        });
                    }

                    if (alliances != null && alliances.IsAllyWithKingdom(first, second))
                    {
                        agreements.Add(AgreementJson("alliance", first, second, (float)alliances.GetAllianceEndDate(first, second).ToDays));
                    }

                    if (trades != null && trades.HasTradeAgreement(first, second, out TradeAgreementsCampaignBehavior.TradeAgreement _))
                    {
                        agreements.Add(AgreementJson("trade_agreement", first, second, (float)trades.GetTradeAgreementEndDate(first, second).ToDays));
                    }

                }
            }

            foreach (ReignDiplomaticAgreementRecord agreement in ReignAICampaignBehavior.Instance?.Agreements ?? new List<ReignDiplomaticAgreementRecord>())
            {
                if (agreement != null)
                {
                    JObject agreementRow = new JObject
                    {
                        ["agreementId"] = agreement.AgreementId ?? string.Empty,
                        ["kind"] = agreement.Kind ?? string.Empty,
                        ["actorKingdomId"] = agreement.ActorKingdomStringId ?? string.Empty,
                        ["targetKingdomId"] = agreement.TargetKingdomStringId ?? string.Empty,
                        ["reason"] = agreement.Reason ?? string.Empty,
                        ["termsJson"] = agreement.TermsJson ?? string.Empty,
                        ["createdDay"] = agreement.CreatedDay,
                        ["expireDay"] = agreement.ExpireDay,
                        ["isPublic"] = agreement.IsPublic,
                        ["isActive"] = agreement.IsActive,
                        ["endedDay"] = agreement.EndedDay,
                        ["endReason"] = agreement.EndReason ?? string.Empty,
                        ["breakerKingdomId"] = agreement.BreakerKingdomStringId ?? string.Empty,
                        ["triggeringWarId"] = agreement.TriggeringWarId ?? string.Empty
                    };
                    agreementHistory.Add(agreementRow);
                    if (agreement.IsActive) agreements.Add(agreementRow.DeepClone());
                }
            }

            foreach (ReignWarOriginRecord origin in ReignAICampaignBehavior.Instance?.WarOrigins ?? new List<ReignWarOriginRecord>())
            {
                if (origin == null) continue;
                warOrigins.Add(new JObject
                {
                    ["warId"] = origin.WarId ?? string.Empty,
                    ["aggressorKingdomId"] = origin.AggressorKingdomStringId ?? string.Empty,
                    ["defenderKingdomId"] = origin.DefenderKingdomStringId ?? string.Empty,
                    ["aggressorRulerHeroId"] = origin.AggressorRulerHeroStringId ?? string.Empty,
                    ["defenderRulerHeroId"] = origin.DefenderRulerHeroStringId ?? string.Empty,
                    ["declarationDay"] = origin.DeclarationDay,
                    ["cause"] = origin.Cause ?? string.Empty,
                    ["sourceActionId"] = origin.SourceActionId ?? string.Empty,
                    ["parentWarId"] = origin.ParentWarId ?? string.Empty,
                    ["originKind"] = origin.OriginKind ?? string.Empty,
                    ["isActive"] = origin.IsActive,
                    ["endedDay"] = origin.EndedDay
                });
            }

            foreach (ReignTreatyObligationRecord obligation in ReignAICampaignBehavior.Instance?.TreatyObligations ?? new List<ReignTreatyObligationRecord>())
            {
                if (obligation == null) continue;
                treatyObligations.Add(new JObject
                {
                    ["obligationId"] = obligation.ObligationId ?? string.Empty,
                    ["agreementId"] = obligation.AgreementId ?? string.Empty,
                    ["triggeringWarId"] = obligation.TriggeringWarId ?? string.Empty,
                    ["resultingWarId"] = obligation.ResultingWarId ?? string.Empty,
                    ["allyKingdomId"] = obligation.AllyKingdomStringId ?? string.Empty,
                    ["allyRulerHeroId"] = obligation.AllyRulerHeroStringId ?? string.Empty,
                    ["defendedKingdomId"] = obligation.DefendedKingdomStringId ?? string.Empty,
                    ["defendedRulerHeroId"] = obligation.DefendedRulerHeroStringId ?? string.Empty,
                    ["aggressorKingdomId"] = obligation.AggressorKingdomStringId ?? string.Empty,
                    ["triggeredDay"] = obligation.TriggeredDay,
                    ["status"] = obligation.Status ?? string.Empty,
                    ["reason"] = obligation.Reason ?? string.Empty
                });
            }

            return new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["timelineId"] = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                ["campaignStartDay"] = ReignCampaignPreparationCampaignBehavior.AutonomousWorldOriginDay,
                ["playerHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["calendar"] = new JObject
                {
                    ["daysPerYear"] = ReignCalendarService.DaysPerYear,
                    ["daysPerSeason"] = ReignCalendarService.DaysPerSeason,
                    ["seasonsPerYear"] = ReignCalendarService.SeasonsPerYear
                },
                ["playerKingdomId"] = playerKingdom?.StringId ?? string.Empty,
                ["kingdoms"] = kingdoms,
                ["clans"] = clans,
                ["settlements"] = settlements,
                ["wars"] = wars,
                ["agreements"] = agreements,
                ["agreementHistory"] = agreementHistory,
                ["warOrigins"] = warOrigins,
                ["treatyObligations"] = treatyObligations,
                ["relations"] = relations,
                ["prisoners"] = prisoners,
                ["allianceMarriageCandidates"] = allianceMarriageCandidates,
                ["supportedCommands"] = new JArray(SupportedDiplomacyCommands())
            };
        }

        private static Hero ProtectedRoyalHeir(Kingdom kingdom)
        {
            return kingdom?.Leader?.Children?
                .Where(x => x != null && x.IsAlive)
                .OrderByDescending(x => x.Age)
                .ThenBy(x => x.StringId)
                .FirstOrDefault();
        }

        private static void AddAllianceMarriageCandidates(JArray candidates, Kingdom kingdom)
        {
            if (candidates == null || kingdom?.RulingClan == null)
            {
                return;
            }

            Hero protectedHeir = ProtectedRoyalHeir(kingdom);
            List<Hero> rulerChildren = (kingdom.Leader?.Children ?? new List<Hero>())
                .Where(x => x != null && x.IsAlive)
                .OrderByDescending(x => x.Age)
                .ThenBy(x => x.StringId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (Hero hero in kingdom.RulingClan.Heroes
                .Where(x => x != null && x.IsAlive && !x.IsChild && x != protectedHeir)
                .Where(x => x.Spouse == null || !x.Spouse.IsAlive)
                .OrderBy(x => x.StringId, StringComparer.OrdinalIgnoreCase))
            {
                int successionRank = rulerChildren.FindIndex(x => x == hero);
                candidates.Add(new JObject
                {
                    ["heroId"] = hero.StringId ?? string.Empty,
                    ["heroName"] = hero.Name?.ToString() ?? hero.StringId ?? string.Empty,
                    ["kingdomId"] = kingdom.StringId ?? string.Empty,
                    ["clanId"] = kingdom.RulingClan.StringId ?? string.Empty,
                    ["isFemale"] = hero.IsFemale,
                    ["age"] = hero.Age,
                    ["isRuler"] = kingdom.Leader == hero,
                    ["isRulerChild"] = successionRank >= 0,
                    ["successionRank"] = successionRank < 0 ? 999 : successionRank + 1,
                    ["isClanLeader"] = hero.Clan?.Leader == hero,
                    ["clanTier"] = hero.Clan?.Tier ?? 0,
                    ["relationToRuler"] = kingdom.Leader == null ? 0 : hero.GetRelation(kingdom.Leader),
                    ["eligibleForPoliticalMarriage"] = true
                });
            }
        }

        private static bool KingdomsShareBorder(Kingdom first, Kingdom second)
        {
            if (first?.Fiefs == null || second?.Fiefs == null || first.Fiefs.Count == 0 || second.Fiefs.Count == 0)
            {
                return false;
            }

            const float borderDistanceSquared = 45f * 45f;
            foreach (Town firstFief in first.Fiefs)
            {
                foreach (Town secondFief in second.Fiefs)
                {
                    if (firstFief?.Settlement != null && secondFief?.Settlement != null
                        && firstFief.Settlement.GetPosition2D.DistanceSquared(secondFief.Settlement.GetPosition2D) <= borderDistanceSquared)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static JObject AgreementJson(string kind, Kingdom first, Kingdom second, float expireDay)
        {
            return new JObject
            {
                ["kind"] = kind,
                ["actorKingdomId"] = first?.StringId ?? string.Empty,
                ["targetKingdomId"] = second?.StringId ?? string.Empty,
                ["expireDay"] = expireDay,
                ["isPublic"] = true
            };
        }

        private static JArray ParseJsonArray(string json)
        {
            try { return JArray.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json); }
            catch { return new JArray(); }
        }

        private static IEnumerable<string> SupportedDiplomacyCommands()
        {
            return new[]
            {
                "declare_war", "make_peace", "offer_tribute_peace", "record_promise", "demand_reparations_peace",
                "demand_settlement_peace", "demand_surrender_peace", "sign_trade_agreement", "sign_non_aggression_pact",
                "sign_alliance", "sign_defensive_pact", "break_treaty", "exchange_prisoners", "ransom_package",
                "hostage_guarantee", "war_indemnity", "recognize_conquest", "return_occupied_settlement",
                "demilitarized_border", "supply_agreement", "loan_or_subsidy", "pay_to_stay_neutral",
                "pay_to_join_war", "guarantee_independence", "protectorate_or_vassalage", "diplomatic_package"
            };
        }
    }
}

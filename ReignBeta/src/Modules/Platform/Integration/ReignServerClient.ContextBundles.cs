using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Court;
using ReignBeta.Events;
using ReignBeta.Government;
using ReignBeta.World;
using ReignPortraits;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using ReignBeta.Characters.Campaign;
using ReignBeta.Characters.Integration;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Dialogue.Runtime;
using ReignBeta.Diplomacy.Campaign;
using ReignBeta.Government.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Runtime;
using ReignBeta.Platform.Settings;
using ReignBeta.Portraits.Integration;
using ReignBeta.Relationships.Campaign;
using ReignBeta.Relationships.Integration;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.Integration;
using ReignBeta.WorldSimulation.News;
using ReignBeta.WorldSimulation.Runtime;
using ReignBeta.WorldSimulation.World;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court.Campaign;
#endif

namespace ReignBeta.Platform.Integration
{
    public static partial class ReignServerClient
    {
        internal static async Task AttachContextBundlesAsync(JObject payload, string mode, Hero speaker, IEnumerable<Hero> activeHeroes)
        {
            if (payload == null)
            {
                return;
            }

            List<Hero> participants = (activeHeroes ?? Enumerable.Empty<Hero>()).Where(x => x != null).Distinct().ToList();
            payload["opportunitySnapshot"] = await ReignMainThread.InvokeAsync(() =>
            {
                if (mode == "dialogue" && participants.Count == 1 && participants[0] == speaker)
                {
                    string marriageHistory = ReignServices.Relationship?.PrivateMarriageHistory(speaker) ?? "";
                    if (marriageHistory.Length != 0)
                        payload["sceneContext"] = (payload.Value<string>("sceneContext") ?? "")
                            + "\nPrivate arranged marriage history for the addressed character\n" + marriageHistory;
                }
                return BuildConversationOpportunitySnapshot(speaker, mode, participants);
            }).ConfigureAwait(false);

            Stopwatch selectorTimer = Stopwatch.StartNew();
            try
            {
                JObject selectPayload = new JObject
                {
                    ["mode"] = mode ?? "dialogue",
                    ["campaignId"] = payload.Value<string>("campaignId") ?? GetCampaignId(),
                    ["correlationId"] = payload.Value<string>("correlationId") ?? NewCorrelationId(mode, speaker?.StringId),
                    ["heroStringId"] = speaker?.StringId
                        ?? payload.Value<string>("speakerHeroStringId")
                        ?? payload.Value<string>("heroStringId")
                        ?? string.Empty,
                    ["playerText"] = payload.Value<string>("playerText") ?? string.Empty,
                    ["sceneContext"] = payload.Value<string>("sceneContext") ?? string.Empty,
                    ["availablePullIds"] = new JArray(ContextPullIds)
                };
                if (string.IsNullOrWhiteSpace(payload.Value<string>("correlationId")))
                {
                    payload["correlationId"] = selectPayload.Value<string>("correlationId") ?? NewCorrelationId(mode, speaker?.StringId);
                    selectPayload["correlationId"] = payload.Value<string>("correlationId");
                }

                JObject selection = await PostJsonAsync("/context/select", selectPayload).ConfigureAwait(false);
                selectorTimer.Stop();
                JArray selectedPulls = selection["selectedPulls"] as JArray ?? new JArray();
                Stopwatch pullTimer = Stopwatch.StartNew();
                JArray bundles = await ReignMainThread.InvokeAsync(() =>
                    BuildContextBundles(selectedPulls, speaker, participants, mode)).ConfigureAwait(false);
                string playerText = payload.Value<string>("playerText") ?? string.Empty;
                bool lookupOnly = LooksLikeWorldHistoryLookupQuestion(playerText);
                // Group assertions are adjudicated after server-side audience projection and acceptance.
                if ((lookupOnly || (mode != "party_chat" && mode != "social_event"))
                    && selectedPulls.OfType<JObject>().Any(x => string.Equals(x.Value<string>("id") ?? x.Value<string>("pullId"), "verify_world_history", StringComparison.OrdinalIgnoreCase)))
                {
                    Stopwatch historyTimer = Stopwatch.StartNew();
                    try
                    {
                        float worldDay = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0f : (float)CampaignTime.Now.ToDays;
                        JObject verification = lookupOnly
                            ? await ReignWorldHistoryClient.VerifyWorldHistoryAsync(playerText, string.Empty, speaker?.StringId ?? string.Empty,
                                speaker?.Clan?.Kingdom?.StringId ?? speaker?.MapFaction?.StringId ?? string.Empty, worldDay,
                                ReignServices.WorldHistory?.TimelineId ?? "main").ConfigureAwait(false)
                            : await ReignWorldHistoryClient.CheckWorldHistoryLieAsync(playerText, Hero.MainHero, speaker, worldDay,
                                ReignServices.WorldHistory?.TimelineId ?? "main", mode).ConfigureAwait(false);
                        historyTimer.Stop();
                        JObject knowledgeSafe;
                        if (lookupOnly)
                        {
                            JObject speakerKnowledge = verification["speakerKnowledge"] as JObject ?? new JObject();
                            knowledgeSafe = new JObject
                            {
                                ["lookupOutcome"] = verification.Value<string>("objectiveVerdict") ?? "not_found",
                                ["speakerVerdict"] = verification.Value<string>("speakerVerdict") ?? "unknown",
                                ["knowledgeBasis"] = speakerKnowledge.Value<string>("basis") ?? "none",
                                ["knownEvidenceEventIds"] = (speakerKnowledge["eventIds"] ?? new JArray()).DeepClone(),
                                ["knownEvents"] = (speakerKnowledge["events"] ?? new JArray()).DeepClone(),
                                ["explanation"] = verification.Value<string>("explanation") ?? string.Empty,
                                ["privacyRule"] = "Use only these speaker-visible canonical events. Do not reveal objective events omitted here.",
                                ["behaviorRule"] = "Answer from the listed canonical events. Preserve unknown locations, motives, cargo, and other details as unknown."
                            };
                        }
                        else
                        {
                            knowledgeSafe = verification["promptPacket"] as JObject ?? new JObject
                            {
                                ["detectionOutcome"] = "not_adjudicable",
                                ["speakerVerdict"] = "unknown",
                                ["behaviorRule"] = "The character lacks reliable evidence. Do not accuse anyone from missing evidence."
                            };
                        }
                        knowledgeSafe["lieCheckId"] = verification.Value<string>("lieCheckId") ?? string.Empty;
                        bundles.Add(ContextBundle("verify_world_history", ContextTitle("verify_world_history"), knowledgeSafe, historyTimer.ElapsedMilliseconds, verification.Value<bool?>("ok") == true, verification.Value<string>("error") ?? string.Empty));
                    }
                    catch (Exception ex)
                    {
                        historyTimer.Stop();
                        bundles.Add(ContextBundle("verify_world_history", ContextTitle("verify_world_history"), new JObject
                        {
                            ["speakerVerdict"] = "unknown", ["explanation"] = "World-history evidence was unavailable. Missing evidence is not proof of a lie."
                        }, historyTimer.ElapsedMilliseconds, false, ex.Message));
                    }
                }
                pullTimer.Stop();

                payload["selectedContextPulls"] = selectedPulls;
                payload["contextBundles"] = bundles;
                payload["contextSelection"] = selection;
                payload["clientContextPullTiming"] = new JObject
                {
                    ["selectorMs"] = selectorTimer.ElapsedMilliseconds,
                    ["pullMs"] = pullTimer.ElapsedMilliseconds,
                    ["bundleCount"] = bundles.Count,
                    ["bundleChars"] = bundles.ToString(Formatting.None).Length
                };

                ReignLog.Info("Context selector mode=" + (mode ?? "")
                    + " hero=" + (speaker?.StringId ?? "")
                    + " selected=" + string.Join(",", selectedPulls.OfType<JObject>().Select(x => x.Value<string>("id")).Where(x => !string.IsNullOrWhiteSpace(x)))
                    + " selectorMs=" + selectorTimer.ElapsedMilliseconds
                    + " pullMs=" + pullTimer.ElapsedMilliseconds
                    + " bundles=" + bundles.Count
                    + " bundleChars=" + bundles.ToString(Formatting.None).Length
                    + " fallback=" + (selection.Value<bool?>("fallback") == true));
            }
            catch (Exception ex)
            {
                selectorTimer.Stop();
                JArray selectedPulls = new JArray
                {
                };
                JArray bundles = await ReignMainThread.InvokeAsync(() =>
                    BuildContextBundles(selectedPulls, speaker, participants, mode)).ConfigureAwait(false);
                payload["selectedContextPulls"] = selectedPulls;
                payload["contextBundles"] = bundles;
                payload["contextSelection"] = new JObject
                {
                    ["ok"] = false,
                    ["fallback"] = true,
                    ["error"] = ex.Message,
                    ["durationMs"] = selectorTimer.ElapsedMilliseconds
                };
                payload["clientContextPullTiming"] = new JObject
                {
                    ["selectorMs"] = selectorTimer.ElapsedMilliseconds,
                    ["pullMs"] = 0,
                    ["bundleCount"] = bundles.Count,
                    ["bundleChars"] = bundles.ToString(Formatting.None).Length
                };
                ReignLog.Warn("Context selector failed; continuing without live context pulls: " + ex.Message);
            }
        }

        private static JArray BuildContextBundles(JArray selectedPulls, Hero speaker, IEnumerable<Hero> activeHeroes, string mode)
        {
            JArray bundles = new JArray();
            if (selectedPulls == null)
            {
                return bundles;
            }

            foreach (JObject pull in selectedPulls.OfType<JObject>())
            {
                string id = pull.Value<string>("id") ?? pull.Value<string>("pullId") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                JObject arguments = pull["arguments"] as JObject ?? new JObject();
                Stopwatch timer = Stopwatch.StartNew();
                try
                {
                    JObject data = null;
                    switch (id)
                    {
                        case "check_inventory_appearance":
                            data = BuildInventoryAppearanceBundle(speaker);
                            break;
                        case "check_player_appearance_status":
                            data = BuildPlayerAppearanceStatusBundle();
                            break;
                        case "nearby_settlements":
                            data = BuildNearbySettlementsBundle(arguments, speaker);
                            break;
                        case "nearby_bandit_parties":
                            data = BuildNearbyPartiesBundle(arguments, speaker, true);
                            break;
                        case "nearby_lord_parties":
                            data = BuildNearbyPartiesBundle(arguments, speaker, false);
                            break;
                        case "current_settlement_facts":
                            data = BuildCurrentSettlementFactsBundle(speaker);
                            break;
                        case "kingdom_diplomacy_status":
                            data = BuildKingdomDiplomacyBundle(speaker);
                            break;
                        case "clan_wealth_and_influence":
                            data = BuildClanWealthInfluenceBundle(speaker);
                            break;
                        case "appraise_trade_offer":
                            data = BuildTradeAppraisalBundle(speaker);
                            break;
                        case "relevant_memory":
                        case "relationship_history":
                        case "verify_world_history":
                            data = null;
                            break;
                        default:
                            data = new JObject { ["note"] = "Unknown pull id requested by selector." };
                            break;
                    }

                    timer.Stop();
                    if (data != null)
                    {
                        bundles.Add(ContextBundle(id, ContextTitle(id), data, timer.ElapsedMilliseconds, true, string.Empty));
                    }
                }
                catch (Exception ex)
                {
                    timer.Stop();
                    bundles.Add(ContextBundle(id, ContextTitle(id), new JObject(), timer.ElapsedMilliseconds, false, ex.Message));
                    ReignLog.Warn("Context pull failed id=" + id + ": " + ex.Message);
                }
            }

            return bundles;
        }

        private static JObject BuildInventoryAppearanceBundle(Hero speaker)
        {
            Hero target = speaker ?? Hero.MainHero;
            return new JObject
            {
                ["subject"] = HeroReference(target),
                ["inventory"] = BuildInventoryProfile(target),
                ["appearance"] = BuildAppearanceProfile(target),
                ["wealth"] = BuildWealthProfile(target)
            };
        }

        private static JObject BuildPlayerAppearanceStatusBundle()
        {
            Hero player = Hero.MainHero;
            return new JObject
            {
                ["subject"] = HeroReference(player),
                ["appearance"] = CompactAppearanceProfile(player),
                ["wealth"] = CompactWealthProfile(player),
                ["interpretationRule"] = "NPCs should react first to visible presentation, clothing, weapons, and apparent station. True lordship or clan status may be missed if the presentation is poor or disguised."
            };
        }

        private static JObject BuildConversationOpportunitySnapshot(Hero speaker, string mode, IEnumerable<Hero> activeHeroes)
        {
            Hero player = Hero.MainHero;
            List<Hero> witnesses = (activeHeroes ?? Enumerable.Empty<Hero>())
                .Where(x => x != null && x != speaker && x != player && !string.IsNullOrWhiteSpace(x.StringId)).Distinct().Take(16).ToList();
            bool adults = speaker != null && player != null && speaker.Age >= 18f && player.Age >= 18f;
            bool closeKin = AreCloseKin(speaker, player);
            bool nativeSuitable = adults && speaker != player && speaker.IsFemale != player.IsFemale && !closeKin;
            string normalizedMode = string.IsNullOrWhiteSpace(mode) ? "dialogue" : mode.ToLowerInvariant();
            bool privateScene = normalizedMode == "correspondence";
            double exposure = privateScene ? 0.08d : normalizedMode == "social_event" || normalizedMode == "party_chat" ? 0.85d : 0.40d;
            exposure = Math.Min(1d, exposure + witnesses.Count * 0.03d);
            bool coercive = speaker?.IsPrisoner == true || player?.IsPrisoner == true;
            JObject snapshot = new JObject
            {
                ["model"] = "reign_relative_opportunity_v1",
                ["observer"] = BuildOpportunityHeroSnapshot(speaker),
                ["target"] = BuildOpportunityHeroSnapshot(player),
                ["identityKnown"] = false,
                ["suitability"] = new JObject
                {
                    ["adults"] = adults,
                    ["nativeSuitable"] = nativeSuitable,
                    ["closeKin"] = closeKin,
                    ["marriageBlocks"] = false
                },
                ["scene"] = new JObject
                {
                    ["private"] = privateScene,
                    ["exposure"] = exposure,
                    ["coercive"] = coercive,
                    ["witnessIds"] = new JArray(witnesses.Select(x => x.StringId))
                }
            };
            ReignTestHarness.Current
                .ApplyManipulationFixtureOpportunityEvidence(snapshot);
            return snapshot;
        }

        private static JObject BuildOpportunityHeroSnapshot(Hero hero)
        {
            JObject wealth = CompactWealthProfile(hero);
            JObject appearance = CompactAppearanceProfile(hero);
            float clanStrength = 0f;
            float influence = 0f;
            try
            {
                clanStrength = hero?.Clan?.CurrentTotalStrength ?? 0f;
                influence = hero?.Clan?.Influence ?? 0f;
            }
            catch
            {
                clanStrength = 0f;
                influence = 0f;
            }
            int leadership = 0;
            try { leadership = hero?.GetSkillValue(DefaultSkills.Leadership) ?? 0; } catch { leadership = 0; }
            return new JObject
            {
                ["heroStringId"] = hero?.StringId ?? string.Empty,
                ["name"] = hero?.Name?.ToString() ?? string.Empty,
                ["age"] = hero?.Age ?? 0f,
                ["isFemale"] = hero?.IsFemale ?? false,
                ["isRuler"] = hero != null && hero.Clan?.Kingdom?.Leader == hero,
                ["isLord"] = hero?.IsLord ?? false,
                ["isPrisoner"] = hero?.IsPrisoner ?? false,
                ["spouseId"] = hero?.Spouse?.StringId ?? string.Empty,
                ["fatherId"] = hero?.Father?.StringId ?? string.Empty,
                ["motherId"] = hero?.Mother?.StringId ?? string.Empty,
                ["childrenIds"] = new JArray((hero?.Children ?? new List<Hero>()).Where(x => x != null).Select(x => x.StringId)),
                ["clanTier"] = hero?.Clan?.Tier ?? 0,
                ["fiefCount"] = hero?.Clan?.Fiefs?.Count ?? 0,
                ["renown"] = hero?.Clan?.Renown ?? 0f,
                ["influence"] = influence,
                ["leadership"] = leadership,
                ["partyStrength"] = HeroPartyStrength(hero),
                ["clanStrength"] = clanStrength,
                ["wealth"] = wealth,
                ["appearance"] = appearance
            };
        }

        private static JObject CompactWealthProfile(Hero hero)
        {
            JObject full = BuildNpcObservableWealthProfile(hero);
            return new JObject
            {
                ["version"] = full["version"], ["economicCapacityKnown"] = full["economicCapacityKnown"],
                ["partyInventoryState"] = full["partyInventoryState"],
                ["gold"] = full["gold"], ["personalWealthTier"] = full["personalWealthTier"],
                ["visibleWealthTier"] = full["visibleWealthTier"], ["partyInventoryValue"] = full["partyInventoryValue"],
                ["clanGold"] = full["clanGold"], ["clanWealthTier"] = full["clanWealthTier"],
                ["clanTier"] = full["clanTier"], ["clanRenown"] = full["clanRenown"],
                ["clanFiefCount"] = full["clanFiefCount"], ["clanSocialCredit"] = full["clanSocialCredit"]
            };
        }

        private static JObject CompactAppearanceProfile(Hero hero)
        {
            JObject full = BuildAppearanceProfile(hero);
            return new JObject
            {
                ["trueStatusScore"] = full["trueStatusScore"], ["trueStatusLabel"] = full["trueStatusLabel"],
                ["visibleStatusScore"] = full["visibleStatusScore"], ["visibleStatusLabel"] = full["visibleStatusLabel"],
                ["statusMismatch"] = full["statusMismatch"], ["presentationEffect"] = full["presentationEffect"],
                ["firstView"] = full["firstView"], ["civilianEquipmentValue"] = full["civilianEquipmentValue"],
                ["battleEquipmentValue"] = full["battleEquipmentValue"], ["attractiveness"] = 50
            };
        }

        private static bool AreCloseKin(Hero first, Hero second)
        {
            if (first == null || second == null) return false;
            if (first.Father == second || first.Mother == second || second.Father == first || second.Mother == first) return true;
            if (first.Father != null && first.Father == second.Father) return true;
            if (first.Mother != null && first.Mother == second.Mother) return true;
            return (first.Children ?? new List<Hero>()).Contains(second) || (second.Children ?? new List<Hero>()).Contains(first);
        }

        private static JObject BuildNearbySettlementsBundle(JObject arguments, Hero speaker)
        {
            int radius = ReadInt(arguments, "radius", 80);
            int maxCount = ReadInt(arguments, "maxCount", 8);
            bool hasOrigin = TryGetReferencePosition(speaker, out Vec2 origin);
            JArray settlements = new JArray();
            if (hasOrigin)
            {
                float radiusSq = radius <= 0 ? float.MaxValue : radius * radius;
                foreach (Settlement settlement in Settlement.All
                    .Where(x => x != null && !x.IsHideout)
                    .Select(x => new { Settlement = x, DistanceSq = origin.DistanceSquared(x.GetPosition2D) })
                    .Where(x => radiusSq == float.MaxValue || x.DistanceSq <= radiusSq)
                    .OrderBy(x => x.DistanceSq)
                    .Take(Math.Max(1, maxCount))
                    .Select(x => x.Settlement))
                {
                    settlements.Add(SettlementSummary(settlement, origin, true));
                }
            }

            return new JObject
            {
                ["originKnown"] = hasOrigin,
                ["radius"] = radius,
                ["maxCount"] = maxCount,
                ["items"] = settlements
            };
        }

        private static JObject BuildNearbyPartiesBundle(JObject arguments, Hero speaker, bool banditsOnly)
        {
            int radius = ReadInt(arguments, "radius", banditsOnly ? 60 : 90);
            int maxCount = ReadInt(arguments, "maxCount", banditsOnly ? 8 : 10);
            bool hasOrigin = TryGetReferencePosition(speaker, out Vec2 origin);
            JArray parties = new JArray();
            if (hasOrigin)
            {
                float radiusSq = radius <= 0 ? float.MaxValue : radius * radius;
                IEnumerable<MobileParty> query = MobileParty.All
                    .Where(x => x != null && x.IsActive && x != MobileParty.MainParty)
                    .Where(x => banditsOnly ? PartyLooksBandit(x) : x.LeaderHero != null && x.LeaderHero.IsLord);

                foreach (MobileParty party in query
                    .Select(x => new { Party = x, DistanceSq = origin.DistanceSquared(x.GetPosition2D) })
                    .Where(x => radiusSq == float.MaxValue || x.DistanceSq <= radiusSq)
                    .OrderBy(x => x.DistanceSq)
                    .Take(Math.Max(1, maxCount))
                    .Select(x => x.Party))
                {
                    parties.Add(PartySummary(party, origin));
                }
            }

            return new JObject
            {
                ["originKnown"] = hasOrigin,
                ["radius"] = radius,
                ["maxCount"] = maxCount,
                ["kind"] = banditsOnly ? "bandits_or_outlaws" : "lord_parties",
                ["items"] = parties
            };
        }

        private static JObject BuildCurrentSettlementFactsBundle(Hero speaker)
        {
            Settlement settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement ?? speaker?.CurrentSettlement;
            if (settlement == null)
            {
                return new JObject { ["available"] = false, ["reason"] = "No current settlement is known to the client." };
            }

            return new JObject
            {
                ["available"] = true,
                ["settlement"] = SettlementSummary(settlement, Vec2.Zero, false)
            };
        }

        private static JObject BuildKingdomDiplomacyBundle(Hero speaker)
        {
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            Kingdom speakerKingdom = speaker?.Clan?.Kingdom;
            JObject result = new JObject
            {
                ["playerKingdom"] = KingdomSummary(playerKingdom),
                ["speakerKingdom"] = KingdomSummary(speakerKingdom),
                ["playerGovernment"] = ReignServices.Government?.BuildPublicSnapshot(playerKingdom)
                    ?? new JObject { ["available"] = false, ["authoritative"] = true },
                ["speakerGovernment"] = ReignServices.Government?.BuildPublicSnapshot(speakerKingdom)
                    ?? new JObject { ["available"] = false, ["authoritative"] = true },
                ["sameKingdom"] = playerKingdom != null && speakerKingdom != null && playerKingdom == speakerKingdom,
                ["atWarWithEachOther"] = playerKingdom != null && speakerKingdom != null && playerKingdom.IsAtWarWith(speakerKingdom)
            };

            result["playerWars"] = KingdomWars(playerKingdom, 12);
            result["speakerWars"] = KingdomWars(speakerKingdom, 12);
            return result;
        }

        internal static JObject BuildNativePoliticalContext(Hero observer)
        {
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            Kingdom observerKingdom = observer?.Clan?.Kingdom;
            Settlement settlement = Settlement.CurrentSettlement
                ?? MobileParty.MainParty?.CurrentSettlement
                ?? observer?.CurrentSettlement;
            // Transient civic evidence never enters the formal identity roster.
            // Keep this inline to preserve Bannerlord's generated type inventory.
            Settlement civicHome = null;
            string civicSource = "";
            if (observer != null && observer.IsNotable)
            {
                civicHome = observer.HomeSettlement;
                if (civicHome != null) civicSource = "native_home_settlement";
                else if (settlement != null && settlement.Notables.Contains(observer))
                {
                    civicHome = settlement;
                    civicSource = "native_notable_roster";
                }
            }
            Kingdom civicRealm = civicHome?.OwnerClan?.Kingdom;
            JObject civicAffiliation = new JObject
            {
                ["authoritative"] = true, ["available"] = civicHome != null,
                ["source"] = civicSource, ["heroStringId"] = observer?.StringId ?? "",
                ["settlementId"] = civicHome?.StringId ?? "", ["kingdomId"] = civicRealm?.StringId ?? "",
                ["kingdomName"] = civicRealm?.Name?.ToString() ?? "",
                ["sovereignHeroStringId"] = civicRealm?.Leader?.StringId ?? "",
                ["ownerClanId"] = civicHome?.OwnerClan?.StringId ?? "",
                ["governorHeroId"] = civicHome?.Town?.Governor?.StringId ?? "",
                ["localEncounter"] = civicHome != null && civicHome == settlement
            };
            return new JObject
            {
                ["authoritative"] = true,
                ["observedWorldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null
                    ? 0d
                    : CampaignTime.Now.ToDays,
                ["observer"] = BuildIdentityRosterEntry(observer),
                ["observerCivicAffiliation"] = civicAffiliation,
                ["subject"] = BuildPlayerIdentityContext(),
                ["playerGovernment"] = ReignServices.Government?.BuildPublicSnapshot(playerKingdom)
                    ?? new JObject { ["available"] = false, ["authoritative"] = true },
                ["observerGovernment"] = ReignServices.Government?.BuildGovernmentKnowledgeForSpeaker(observer)
                    ?? new JObject { ["available"] = false, ["authoritative"] = true },
                ["settlement"] = settlement == null
                    ? new JObject { ["available"] = false }
                    : SettlementSummary(settlement, Vec2.Zero, false),
                ["diplomacy"] = new JObject
                {
                    ["volatileCurrentState"] = true,
                    ["playerKingdom"] = KingdomSummary(playerKingdom),
                    ["observerKingdom"] = KingdomSummary(observerKingdom),
                    ["playerEnemyKingdoms"] = KingdomWars(playerKingdom, 32),
                    ["observerEnemyKingdoms"] = KingdomWars(observerKingdom, 32),
                    ["sameKingdom"] = playerKingdom != null
                        && observerKingdom != null
                        && playerKingdom == observerKingdom
                }
            };
        }

        private static JObject BuildClanWealthInfluenceBundle(Hero speaker)
        {
            JObject playerWealth =
                BuildNpcObservableWealthProfile(Hero.MainHero);
            JObject playerClan = ClanStanding(Clan.PlayerClan);
            ReignTestHarness.Current
                .ApplyNpcObservableClanEvidence(
                    Hero.MainHero, playerClan);
            return new JObject
            {
                ["player"] = new JObject
                {
                    ["hero"] = HeroReference(Hero.MainHero),
                    ["wealth"] = playerWealth,
                    ["clan"] = playerClan
                },
                ["speaker"] = new JObject
                {
                    ["hero"] = HeroReference(speaker),
                    ["wealth"] = BuildWealthProfile(speaker),
                    ["clan"] = ClanStanding(speaker?.Clan)
                }
            };
        }

        private static JObject BuildTradeAppraisalBundle(Hero speaker)
        {
            JArray workshops = new JArray();
            foreach (JObject workshop in BuildResolverWorkshops().OfType<JObject>()
                .Where(x =>
                    string.Equals(x.Value<string>("ownerHeroStringId"), Hero.MainHero?.StringId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.Value<string>("ownerHeroStringId"), speaker?.StringId, StringComparison.OrdinalIgnoreCase))
                .Take(24))
            {
                workshops.Add(workshop);
            }

            Settlement current = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement ?? speaker?.CurrentSettlement;
            return new JObject
            {
                ["pricingAuthority"] = "Bannerlord Reign game-side value service. The LLM may discuss value, but resolver/validator/executor make final price and legality decisions.",
                ["fairnessRule"] = "Normal gameplay rejects unfair exchanges unless the NPC truly offers better terms. Beta prompt override can bypass fairness for testing but not missing ownership/assets.",
                ["player"] = new JObject
                {
                    ["hero"] = HeroReference(Hero.MainHero),
                    ["wealth"] =
                        BuildNpcObservableWealthProfile(Hero.MainHero),
                    ["inventory"] = BuildInventoryProfile(Hero.MainHero),
                    ["appearance"] = BuildAppearanceProfile(Hero.MainHero),
                    ["prisoners"] = BuildPrisonerArray(MobileParty.MainParty, 24)
                },
                ["speaker"] = new JObject
                {
                    ["hero"] = HeroReference(speaker),
                    ["wealth"] = BuildWealthProfile(speaker),
                    ["inventory"] = BuildInventoryProfile(speaker),
                    ["appearance"] = BuildAppearanceProfile(speaker),
                    ["prisoners"] = BuildPrisonerArray(speaker?.PartyBelongedTo, 24)
                },
                ["currentSettlement"] = current == null ? new JObject { ["available"] = false } : SettlementSummary(current, Vec2.Zero, false),
                ["ownedWorkshopsInvolved"] = workshops
            };
        }

        private static JObject ContextBundle(string id, string title, JObject data, long durationMs, bool ok, string error)
        {
            JObject bundle = new JObject
            {
                ["id"] = id ?? string.Empty,
                ["title"] = title ?? string.Empty,
                ["ok"] = ok,
                ["source"] = "game_client",
                ["durationMs"] = durationMs,
                ["data"] = data ?? new JObject()
            };
            if (!ok && !string.IsNullOrWhiteSpace(error))
            {
                bundle["error"] = error;
            }

            return bundle;
        }

        private static JObject HeroReference(Hero hero)
        {
            return new JObject
            {
                ["heroStringId"] = hero?.StringId ?? string.Empty,
                ["name"] = hero?.Name?.ToString() ?? string.Empty,
                ["clanId"] = hero?.Clan?.StringId ?? string.Empty,
                ["clanName"] = hero?.Clan?.Name?.ToString() ?? string.Empty,
                ["kingdomId"] = hero?.Clan?.Kingdom?.StringId ?? hero?.MapFaction?.StringId ?? string.Empty,
                ["kingdomName"] = hero?.Clan?.Kingdom?.InformalName?.ToString() ?? hero?.MapFaction?.Name?.ToString() ?? string.Empty,
                ["occupation"] = hero?.Occupation.ToString() ?? string.Empty,
                ["spouseId"] = hero?.Spouse?.StringId ?? string.Empty,
                ["fatherId"] = hero?.Father?.StringId ?? string.Empty,
                ["motherId"] = hero?.Mother?.StringId ?? string.Empty,
                ["currentSettlementId"] = hero?.CurrentSettlement?.StringId ?? string.Empty,
                ["homeSettlementId"] = hero?.HomeSettlement?.StringId ?? string.Empty,
                ["partyId"] = hero?.PartyBelongedTo?.StringId ?? string.Empty,
                ["governorOfSettlementId"] = hero?.GovernorOf?.Settlement?.StringId ?? string.Empty,
                ["prisonerHolderPartyId"] = hero?.PartyBelongedToAsPrisoner?.Id ?? string.Empty,
                ["isKingdomLeader"] = hero?.IsKingdomLeader ?? false,
                ["isClanLeader"] = hero?.IsClanLeader ?? false,
                ["isLord"] = hero?.IsLord ?? false,
                ["isNotable"] = hero?.IsNotable ?? false,
                ["isWanderer"] = hero?.IsWanderer ?? false,
                ["isPrisoner"] = hero?.IsPrisoner ?? false
            };
        }

        private static bool TryGetReferencePosition(Hero speaker, out Vec2 origin)
        {
            origin = Vec2.Zero;
            try
            {
                if (MobileParty.MainParty != null)
                {
                    origin = MobileParty.MainParty.GetPosition2D;
                    return true;
                }

                if (speaker?.PartyBelongedTo != null)
                {
                    origin = speaker.PartyBelongedTo.GetPosition2D;
                    return true;
                }

                Settlement settlement = Settlement.CurrentSettlement ?? speaker?.CurrentSettlement;
                if (settlement != null)
                {
                    origin = settlement.GetPosition2D;
                    return true;
                }
            }
            catch
            {
                origin = Vec2.Zero;
            }

            return false;
        }

        private static JObject SettlementSummary(Settlement settlement, Vec2 origin, bool includeDistance)
        {
            JObject result = new JObject
            {
                ["settlementId"] = settlement?.StringId ?? string.Empty,
                ["name"] = settlement?.Name?.ToString() ?? string.Empty,
                ["type"] = SettlementType(settlement),
                ["cultureId"] = settlement?.Culture?.StringId ?? string.Empty,
                ["ownerClanId"] = settlement?.OwnerClan?.StringId ?? string.Empty,
                ["ownerClanName"] = settlement?.OwnerClan?.Name?.ToString() ?? string.Empty,
                ["factionId"] = settlement?.MapFaction?.StringId ?? string.Empty,
                ["factionName"] = settlement?.MapFaction?.Name?.ToString() ?? string.Empty,
                ["kingdomId"] = settlement?.MapFaction?.StringId ?? settlement?.OwnerClan?.Kingdom?.StringId ?? string.Empty,
                ["kingdomName"] = settlement?.MapFaction?.Name?.ToString() ?? settlement?.OwnerClan?.Kingdom?.InformalName?.ToString() ?? string.Empty,
                ["isTown"] = settlement?.IsTown ?? false,
                ["isCastle"] = settlement?.IsCastle ?? false,
                ["isVillage"] = settlement?.IsVillage ?? false,
                ["isHideout"] = settlement?.IsHideout ?? false,
                ["isFortification"] = settlement?.IsFortification ?? false,
                ["isUnderSiege"] = settlement?.IsUnderSiege ?? false,
                ["isUnderRaid"] = settlement?.IsUnderRaid ?? false,
                ["militia"] = settlement?.Militia ?? 0f
            };

            if (includeDistance && settlement != null)
            {
                result["distance"] = Math.Sqrt(origin.DistanceSquared(settlement.GetPosition2D));
            }

            Town town = settlement?.Town;
            if (town != null)
            {
                result["prosperity"] = town.Prosperity;
                result["loyalty"] = town.Loyalty;
                result["security"] = town.Security;
                result["garrison"] = town.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;
                result["boundVillages"] = settlement.BoundVillages?.Count ?? 0;
                result["governorHeroId"] = town.Governor?.StringId ?? string.Empty;
                result["governorName"] = town.Governor?.Name?.ToString() ?? string.Empty;
            }

            Village village = settlement?.Village;
            if (village != null)
            {
                result["boundSettlementId"] = village.Bound?.StringId ?? string.Empty;
                result["boundSettlementName"] = village.Bound?.Name?.ToString() ?? string.Empty;
                result["villageState"] = village.VillageState.ToString();
                result["isDeserted"] = village.IsDeserted;
            }

            return result;
        }

        private static string SettlementType(Settlement settlement)
        {
            if (settlement == null)
            {
                return "unknown";
            }

            if (settlement.IsTown)
            {
                return "town";
            }

            if (settlement.IsCastle)
            {
                return "castle";
            }

            if (settlement.IsVillage)
            {
                return "village";
            }

            if (settlement.IsHideout)
            {
                return "hideout";
            }

            return "settlement";
        }

        private static bool PartyLooksBandit(MobileParty party)
        {
            if (party == null)
            {
                return false;
            }

            if (party.IsBandit)
            {
                return true;
            }

            string text = ((party.StringId ?? "") + " "
                + SafePartyName(party) + " "
                + (party.MapFaction?.StringId ?? "") + " "
                + SafeFactionName(party.MapFaction)).ToLowerInvariant();
            return text.Contains("bandit")
                || text.Contains("looter")
                || text.Contains("raider")
                || text.Contains("outlaw")
                || text.Contains("sea_raider")
                || text.Contains("mountain_bandit")
                || text.Contains("forest_bandit")
                || text.Contains("steppe_bandit")
                || text.Contains("desert_bandit");
        }

        private static JObject PartySummary(MobileParty party, Vec2 origin)
        {
            return new JObject
            {
                ["partyId"] = party?.StringId ?? string.Empty,
                ["name"] = SafePartyName(party),
                ["leaderHeroId"] = party?.LeaderHero?.StringId ?? string.Empty,
                ["leaderName"] = party?.LeaderHero?.Name?.ToString() ?? string.Empty,
                ["factionId"] = party?.MapFaction?.StringId ?? string.Empty,
                ["factionName"] = SafeFactionName(party?.MapFaction),
                ["isBandit"] = party?.IsBandit ?? false,
                ["isLordParty"] = party?.IsLordParty ?? false,
                ["troops"] = party?.MemberRoster?.TotalManCount ?? 0,
                ["wounded"] = party?.MemberRoster?.TotalWounded ?? 0,
                ["distance"] = party == null ? 0d : Math.Sqrt(origin.DistanceSquared(party.GetPosition2D))
            };
        }

        private static string SafePartyName(MobileParty party)
        {
            try
            {
                return party?.Name?.ToString() ?? party?.StringId ?? string.Empty;
            }
            catch
            {
                return party?.StringId ?? string.Empty;
            }
        }

        private static string SafeFactionName(IFaction faction)
        {
            try
            {
                return faction?.Name?.ToString() ?? string.Empty;
            }
            catch
            {
                return faction?.StringId ?? string.Empty;
            }
        }

        private static JObject KingdomSummary(Kingdom kingdom)
        {
            if (kingdom == null)
            {
                return new JObject { ["available"] = false };
            }

            JArray enemyKingdoms = KingdomWars(kingdom, 32);
            return new JObject
            {
                ["available"] = true,
                ["kingdomId"] = kingdom.StringId ?? string.Empty,
                ["name"] = kingdom.InformalName?.ToString() ?? kingdom.Name?.ToString() ?? string.Empty,
                ["leaderHeroId"] = kingdom.Leader?.StringId ?? string.Empty,
                ["leaderName"] = kingdom.Leader?.Name?.ToString() ?? string.Empty,
                ["clanCount"] = kingdom.Clans?.Count ?? 0,
                ["townCount"] = kingdom.Towns?.Count ?? 0,
                ["armyCount"] = kingdom.Armies?.Count ?? 0,
                ["warCount"] = enemyKingdoms.Count,
                ["enemyKingdomIds"] = new JArray(enemyKingdoms
                    .OfType<JObject>()
                    .Select(x => x.Value<string>("kingdomId") ?? string.Empty)
                    .Where(x => !string.IsNullOrWhiteSpace(x))),
                ["strength"] = kingdom.CurrentTotalStrength
            };
        }

        private static JArray KingdomWars(Kingdom kingdom, int limit)
        {
            JArray wars = new JArray();
            if (kingdom?.FactionsAtWarWith == null)
            {
                return wars;
            }

            foreach (var faction in kingdom.FactionsAtWarWith
                .Where(x => x != null && x.IsKingdomFaction)
                .GroupBy(x => x.StringId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .Take(Math.Max(1, limit)))
            {
                wars.Add(new JObject
                {
                    ["kingdomId"] = faction?.StringId ?? string.Empty,
                    ["factionId"] = faction?.StringId ?? string.Empty,
                    ["name"] = faction?.Name?.ToString() ?? string.Empty,
                    ["isKingdom"] = faction?.IsKingdomFaction ?? false,
                    ["strength"] = faction?.CurrentTotalStrength ?? 0f
                });
            }

            return wars;
        }

        private static JObject ClanStanding(Clan clan)
        {
            if (clan == null)
            {
                return new JObject { ["available"] = false };
            }

            return new JObject
            {
                ["available"] = true,
                ["clanId"] = clan.StringId ?? string.Empty,
                ["name"] = clan.Name?.ToString() ?? string.Empty,
                ["leaderHeroId"] = clan.Leader?.StringId ?? string.Empty,
                ["leaderName"] = clan.Leader?.Name?.ToString() ?? string.Empty,
                ["tier"] = clan.Tier,
                ["gold"] = clan.Gold,
                ["wealthTier"] = WealthTier(clan.Gold),
                ["renown"] = clan.Renown,
                ["influence"] = clan.Influence,
                ["fiefCount"] = clan.Fiefs?.Count ?? 0,
                ["kingdomId"] = clan.Kingdom?.StringId ?? string.Empty,
                ["kingdomName"] = clan.Kingdom?.InformalName?.ToString() ?? string.Empty
            };
        }

        private static string ContextTitle(string id)
        {
            switch (id ?? string.Empty)
            {
                case "check_inventory_appearance":
                    return "Check Inventory And Appearance";
                case "check_player_appearance_status":
                    return "Check Player Appearance And Status";
                case "nearby_settlements":
                    return "Nearby Settlements";
                case "nearby_bandit_parties":
                    return "Nearby Bandit Parties";
                case "nearby_lord_parties":
                    return "Nearby Lord Parties";
                case "current_settlement_facts":
                    return "Current Settlement Facts";
                case "kingdom_diplomacy_status":
                    return "Kingdom Diplomacy Status";
                case "clan_wealth_and_influence":
                    return "Clan Wealth And Influence";
                case "appraise_trade_offer":
                    return "Appraise Trade Offer";
                case "verify_world_history":
                    return "Verify World History";
                case "relevant_memory":
                    return "Relevant Memory";
                case "relationship_history":
                    return "Relationship History";
                default:
                    return id ?? string.Empty;
            }
        }
    }
}

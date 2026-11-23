using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.PartyAgency;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;
using ReignBeta.Dialogue.Runtime;
using ReignBeta.Platform.Integration;
using ReignBeta.Reputation.Campaign;
using ReignBeta.WorldSimulation.Arrests;
using ReignBeta.WorldSimulation.PartyAgency;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        internal static bool IsDisposablePartyAgencyFixtureConversationActive(Hero hero)
        {
            if (hero == null || _individualVm == null
                || string.IsNullOrWhiteSpace(_activeRunId)
                || !string.Equals(_partyAgencyFixtureTargetId, hero.StringId,
                    StringComparison.OrdinalIgnoreCase))
                return false;

            string activeSave = ActiveSaveName();
            return activeSave.StartsWith("ReignTest_", StringComparison.Ordinal)
                && activeSave.EndsWith("_Current", StringComparison.Ordinal)
                && TaleWorlds.CampaignSystem.Campaign.Current?.TimeControlMode
                    == CampaignTimeControlMode.Stop;
        }

        private static async Task<LiveCommandResult> PreparePartyAgencyFixtureAsync(
            JObject command)
        {
            if (command?.Value<bool?>("confirmDisposableCampaign") != true)
                return LiveCommandResult.Failed(
                    "prepare_party_agency_fixture requires confirmDisposableCampaign=true.");
            string expectedSave = command.Value<string>("expectedSaveName") ?? string.Empty;
            string activeSave = ActiveSaveName();
            if (!expectedSave.StartsWith("ReignTest_", StringComparison.Ordinal)
                || !expectedSave.EndsWith("_Current", StringComparison.Ordinal)
                || !string.Equals(expectedSave, activeSave, StringComparison.Ordinal))
                return LiveCommandResult.Failed(
                    "The Party Agency fixture is not authorized for the exact active enrolled disposable save.",
                    new JObject
                    {
                        ["expectedSaveName"] = expectedSave,
                        ["activeSaveName"] = activeSave
                    });
            if (TaleWorlds.CampaignSystem.Campaign.Current?.TimeControlMode
                != CampaignTimeControlMode.Stop)
                return LiveCommandResult.Failed(
                    "Native campaign time must be paused before preparing a Party Agency fixture.");
            if (_individualVm != null || _partyVm != null || _eventVm != null)
                await CloseSessionAsync(false).ConfigureAwait(false);

            return await ReignMainThread.InvokeAsync(() =>
            {
                try
                {
                    string caseId = command.Value<string>("caseId") ?? string.Empty;
                    string variant = (command.Value<string>("variant") ?? string.Empty)
                        .Trim().ToLowerInvariant();
                    string fixtureRole = (command.Value<string>("fixtureRole") ?? variant)
                        .Trim().ToLowerInvariant();
                    string runId = command.Value<string>("runId") ?? _activeRunId ?? string.Empty;
                    string candidateHint = command.Value<string>("candidateHint") ?? string.Empty;
                    Settlement anchor = Settlement.CurrentSettlement
                        ?? MobileParty.MainParty?.CurrentSettlement
                        ?? Settlement.All.FirstOrDefault(settlement => settlement?.IsFortification == true);
                    if (anchor == null)
                        return LiveCommandResult.Failed(
                            "No safe settlement anchor exists for the native Party Agency fixture.");

                    Hero target = caseId == "PA-NATIVE-018" || caseId == "PA-NATIVE-019"
                        ? SelectPartyAgencyHostilityTarget(candidateHint)
                        : SelectPartyAgencyFixtureTarget(fixtureRole, candidateHint);
                    if (target == null)
                        return LiveCommandResult.Failed(
                            "No native hero can represent Party Agency variant '" + variant + "'.",
                            new JObject
                            {
                                ["variant"] = variant,
                                ["fixtureRole"] = fixtureRole,
                                ["candidateHint"] = candidateHint,
                                ["requiresBaselineReload"] = true
                            });

                    var createdParties = new List<MobileParty>();
                    MobileParty targetParty = target.PartyBelongedTo;
                    MobileParty helperParty = null;
                    switch (fixtureRole)
                    {
                        case "partyless":
                        case "clan_leader":
                        case "kingdom_ruler":
                            break;
                        case "ordinary_party_member":
                            helperParty = CreatePartyAgencyFixtureParty(
                                SelectPartyAgencyHelper(target), anchor, runId, "member_host");
                            createdParties.Add(helperParty);
                            AddHeroToPartyAction.Apply(target, helperParty, false);
                            targetParty = helperParty;
                            break;
                        case "party_leader":
                        case "quest_bound":
                            targetParty = CreatePartyAgencyFixtureParty(
                                target, anchor, runId, "target");
                            createdParties.Add(targetParty);
                            if (fixtureRole == "quest_bound") targetParty.SetPartyUsedByQuest(true);
                            break;
                        case "army_member":
                        case "army_leader":
                            targetParty = CreatePartyAgencyFixtureParty(
                                target, anchor, runId, "target");
                            createdParties.Add(targetParty);
                            Hero helper = SelectPartyAgencyHelper(target);
                            helperParty = CreatePartyAgencyFixtureParty(
                                helper, anchor, runId, "army_helper");
                            createdParties.Add(helperParty);
                            MobileParty leaderParty = fixtureRole == "army_leader"
                                ? targetParty : helperParty;
                            MobileParty memberParty = fixtureRole == "army_leader"
                                ? helperParty : targetParty;
                            Kingdom kingdom = leaderParty?.LeaderHero?.Clan?.Kingdom;
                            if (kingdom == null || memberParty?.MapFaction != kingdom)
                                throw new InvalidOperationException(
                                    "The native army fixture requires two parties in the same kingdom.");
                            var members = new MBList<MobileParty> { memberParty };
                            kingdom.CreateArmy(leaderParty.LeaderHero, anchor,
                                Army.ArmyTypes.Besieger, members);
                            if (leaderParty.Army == null)
                                throw new InvalidOperationException(
                                    "Bannerlord did not create the requested native army formation.");
                            if (memberParty.Army != leaderParty.Army)
                                memberParty.Army = leaderParty.Army;
                            break;
                        case "governor":
                            if (target.GovernorOf == null)
                            {
                                Town town = Settlement.All
                                    .FirstOrDefault(settlement => settlement?.Town != null
                                        && settlement.OwnerClan == target.Clan)?.Town;
                                if (town == null) throw new InvalidOperationException(
                                    "No target-clan town exists for the governor rejection fixture.");
                                if (target.CurrentSettlement != town.Settlement)
                                    EnterSettlementAction.ApplyForCharacterOnly(
                                        target, town.Settlement);
                                ChangeGovernorAction.Apply(town, target);
                                if (target.GovernorOf != town || town.Governor != target)
                                    throw new InvalidOperationException(
                                        "Bannerlord did not install the requested native governor fixture.");
                            }
                            break;
                        case "prisoner":
                            // PA-NATIVE-008 proves rejection of foreign custody. Player-held
                            // captives can now consent to release into temporary service.
                            Settlement foreignDungeon = Settlement.All.FirstOrDefault(settlement =>
                                settlement?.IsFortification == true && settlement.OwnerClan != null
                                && settlement.OwnerClan != Clan.PlayerClan && !settlement.IsUnderSiege);
                            if (foreignDungeon == null)
                                throw new InvalidOperationException("No foreign dungeon exists for the prisoner rejection fixture.");
                            if (target.IsPrisoner && target.PartyBelongedToAsPrisoner != foreignDungeon.Party)
                                throw new InvalidOperationException("The selected prisoner is not in the fixture's foreign custody; reload and select a free target.");
                            if (!target.IsPrisoner)
                                TakePrisonerAction.Apply(foreignDungeon.Party, target);
                            break;
                        case "dead":
                        case "child":
                            break;
                        case "companion":
                            if (target.CompanionOf == null)
                            {
                                if (Clan.PlayerClan == null)
                                    throw new InvalidOperationException(
                                        "The native companion fixture requires the player clan.");
                                AddCompanionAction.Apply(Clan.PlayerClan, target);
                                if (target.CompanionOf != Clan.PlayerClan)
                                    throw new InvalidOperationException(
                                        "Bannerlord did not install the requested native companion fixture.");
                            }
                            break;
                        default:
                            throw new InvalidOperationException(
                                "Unsupported Party Agency fixture role '" + fixtureRole + "'.");
                    }

                    if (caseId == "PA-NATIVE-015" && targetParty != null)
                        PositionPartyAgencyReturnFixture(targetParty, anchor, variant);

                    _partyAgencyFixtureTargetId = target.StringId;
                    string key = runId + "|" + caseId + "|" + variant;
                    JObject role = PartyAgencyFixtureRoleEvidence(target);
                    JObject evidence = new JObject
                    {
                        ["ok"] = true,
                        ["receiptId"] = "party_agency_native_" + Guid.NewGuid().ToString("N"),
                        ["operation"] = "prepare_party_agency_fixture",
                        ["caseId"] = caseId,
                        ["variant"] = variant,
                        ["fixtureRole"] = fixtureRole,
                        ["targetHeroId"] = target.StringId,
                        ["targetName"] = target.Name?.ToString() ?? string.Empty,
                        ["activeSaveName"] = activeSave,
                        ["anchorSettlementId"] = anchor.StringId,
                        ["createdPartyIds"] = new JArray(createdParties
                            .Where(party => party != null).Select(party => party.StringId)),
                        ["role"] = role,
                        ["beforeNativeState"] = CapturePartyAgencyNativeState(target),
                        ["unrelatedStateBaseline"] = caseId == "PA-NATIVE-021"
                            ? CapturePartyAgencyUnrelatedState(target) : null,
                        ["naturalLanguageDecisionPending"] = true,
                        ["directGuestActionInvoked"] = false,
                        ["requiresBaselineReload"] = true
                    };
                    PartyAgencyNativeEvidence[key] = evidence;
                    return LiveCommandResult.Completed(
                        "The guarded native Party Agency role fixture is ready; guest consent remains pending in production dialogue.",
                        evidence);
                }
                catch (Exception ex)
                {
                    return LiveCommandResult.Failed(
                        "Native Party Agency fixture preparation failed: " + ex.Message,
                        new JObject
                        {
                            ["requiresBaselineReload"] = true,
                            ["directGuestActionInvoked"] = false
                        });
                }
            }).ConfigureAwait(false);
        }

        private static bool PartyAgencyDisposableCommandAuthorized(JObject command,
            out string activeSave, out LiveCommandResult failure)
        {
            activeSave = ActiveSaveName();
            string expectedSave = command?.Value<string>("expectedSaveName") ?? string.Empty;
            bool ok = command?.Value<bool?>("confirmDisposableCampaign") == true
                && expectedSave.StartsWith("ReignTest_", StringComparison.Ordinal)
                && expectedSave.EndsWith("_Current", StringComparison.Ordinal)
                && string.Equals(expectedSave, activeSave, StringComparison.Ordinal)
                && TaleWorlds.CampaignSystem.Campaign.Current?.TimeControlMode
                    == CampaignTimeControlMode.Stop;
            failure = ok ? null : LiveCommandResult.Failed(
                "The Party Agency native exercise requires paused time on the exact active enrolled disposable save.");
            return ok;
        }

        private static Hero FindPartyAgencyFixtureHero()
        {
            if (string.IsNullOrWhiteSpace(_partyAgencyFixtureTargetId)) return null;
            return Hero.AllAliveHeroes.Concat(Hero.DeadOrDisabledHeroes)
                .FirstOrDefault(hero => hero != null && string.Equals(
                    hero.StringId, _partyAgencyFixtureTargetId,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static Hero SelectPartyAgencyFixtureTarget(string variant, string hint)
        {
            IEnumerable<Hero> allHeroes = Hero.AllAliveHeroes.Concat(Hero.DeadOrDisabledHeroes)
                .Where(hero => hero != null && hero != Hero.MainHero);
            IEnumerable<Hero> all = allHeroes.Where(hero => hero.IsLord);
            Func<Hero, bool> ordinary = hero => hero.IsAlive && !hero.IsPrisoner
                && hero.Clan != null && hero.Clan != Clan.PlayerClan
                && hero.CompanionOf == null && hero.GovernorOf == null
                && ReignConversationEligibility.IsAdultLivingNpc(hero);
            Func<Hero, bool> partyless = hero => ordinary(hero)
                && hero.PartyBelongedTo == null && hero.CanLeadParty();
            Func<Hero, bool> predicate;
            switch (variant)
            {
                case "clan_leader": predicate = hero => ordinary(hero)
                    && hero.Clan?.Leader == hero; break;
                case "kingdom_ruler": predicate = hero => ordinary(hero)
                    && hero.Clan?.Kingdom?.Leader == hero; break;
                case "governor": predicate = hero => hero.IsAlive
                    && hero.GovernorOf != null; break;
                case "prisoner": predicate = partyless; break;
                case "dead": predicate = hero => hero.IsDead; break;
                case "child": predicate = hero => hero.IsAlive
                    && !ReignConversationEligibility.IsAdultLivingNpc(hero); break;
                case "companion": predicate = hero => hero.IsAlive
                    && hero.CompanionOf != null; break;
                case "partyless": predicate = partyless; break;
                default: predicate = partyless; break;
            }
            IEnumerable<Hero> candidatePool = variant == "companion" ? allHeroes : all;
            List<Hero> candidates = candidatePool.Where(predicate).ToList();
            if (candidates.Count == 0 && variant == "governor")
                candidates = all.Where(partyless).Where(hero => Settlement.All.Any(
                    settlement => settlement?.Town != null
                        && settlement.OwnerClan == hero.Clan)).ToList();
            else if (candidates.Count == 0 && variant == "prisoner")
                candidates = all.Where(partyless).ToList();
            else if (candidates.Count == 0 && variant == "companion")
                candidates = allHeroes.Where(hero => hero.IsAlive && !hero.IsPrisoner
                    && hero.CompanionOf == null && hero.GovernorOf == null
                    && hero.PartyBelongedTo == null
                    && hero.CharacterObject?.Occupation == Occupation.Wanderer
                    && ReignConversationEligibility.IsAdultLivingNpc(hero)).ToList();
            if (!string.IsNullOrWhiteSpace(hint))
            {
                Hero hinted = candidates.FirstOrDefault(hero =>
                    string.Equals(hero.StringId, hint, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(hero.Name?.ToString(), hint,
                        StringComparison.OrdinalIgnoreCase));
                if (hinted != null) return hinted;
            }
            return candidates.OrderBy(hero => hero.Name?.ToString() ?? hero.StringId,
                StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        }

        private static Hero SelectPartyAgencyHostilityTarget(string hint)
        {
            IFaction playerFaction = Hero.MainHero?.MapFaction;
            List<Hero> candidates = Hero.AllAliveHeroes.Where(hero => hero != null
                    && hero != Hero.MainHero && hero.IsLord && hero.IsAlive
                    && !hero.IsPrisoner && hero.Clan != null
                    && hero.Clan != Clan.PlayerClan && hero.Clan.Kingdom != null
                    && hero.CompanionOf == null && hero.GovernorOf == null
                    && hero.PartyBelongedTo == null
                    && ReignConversationEligibility.IsAdultLivingNpc(hero)
                    && hero.CanLeadParty() && playerFaction != null
                    && hero.MapFaction != playerFaction
                    && !FactionManager.IsAtWarAgainstFaction(
                        playerFaction, hero.MapFaction))
                .OrderBy(hero => hero.Name?.ToString() ?? hero.StringId,
                    StringComparer.OrdinalIgnoreCase).ToList();
            if (!string.IsNullOrWhiteSpace(hint))
            {
                Hero hinted = candidates.FirstOrDefault(hero =>
                    string.Equals(hero.StringId, hint, StringComparison.OrdinalIgnoreCase)
                    || (hero.Name?.ToString() ?? string.Empty)
                        .IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0);
                if (hinted != null) return hinted;
            }
            return candidates.FirstOrDefault();
        }

        private static Hero SelectPartyAgencyHelper(Hero target)
        {
            Kingdom kingdom = target?.Clan?.Kingdom;
            Hero helper = Hero.AllAliveHeroes.FirstOrDefault(hero => hero != null
                && hero != target && hero != Hero.MainHero && hero.IsLord
                && hero.IsAlive && !hero.IsPrisoner && hero.PartyBelongedTo == null
                && hero.CompanionOf == null && hero.GovernorOf == null
                && hero.Clan?.Kingdom == kingdom && hero.CanLeadParty());
            if (helper == null)
                throw new InvalidOperationException(
                    "No same-kingdom partyless helper lord exists for this fixture.");
            return helper;
        }

        private static MobileParty CreatePartyAgencyFixtureParty(Hero leader,
            Settlement anchor, string runId, string role)
        {
            if (leader == null || leader.PartyBelongedTo != null)
                throw new InvalidOperationException(
                    "A native fixture party requires a partyless leader.");
            string suffix = Math.Abs((runId + role + leader.StringId).GetHashCode())
                .ToString("x8");
            MobileParty party = LordPartyComponent.CreateLordParty(
                leader.StringId + "_reign_party_agency_" + suffix,
                leader, anchor.GatePosition, 2f, anchor, leader);
            CharacterObject troop = MBObjectManager.Instance
                .GetObject<CharacterObject>("imperial_infantryman")
                ?? MBObjectManager.Instance.GetObject<CharacterObject>("imperial_recruit");
            if (troop != null) party.MemberRoster.AddToCounts(troop, 24);
            if (DefaultItems.Grain != null) party.ItemRoster.AddToCounts(DefaultItems.Grain, 60);
            party.SetMoveModeHold();
            return party;
        }

        private static void PositionPartyAgencyReturnFixture(MobileParty party,
            Settlement anchor, string variant)
        {
            CampaignVec2 origin = MobileParty.MainParty?.Position ?? anchor.GatePosition;
            if (PartyAgencyReturnVariantMatches(variant, "min", "minimum"))
            {
                party.Position = origin;
                return;
            }

            var safeFortifications = Settlement.All.Where(settlement => settlement != null
                    && settlement.IsFortification && !settlement.IsUnderSiege
                    && party.MapFaction != null
                    && !party.MapFaction.IsAtWarWith(settlement.MapFaction))
                .Select(settlement => new
                {
                    Settlement = settlement,
                    Distance = settlement.GatePosition.ToVec2()
                        .Distance(origin.ToVec2())
                }).ToList();
            if (safeFortifications.Count == 0)
                throw new InvalidOperationException(
                    "No safe friendly fortification exists for the return fixture.");
            party.Position = PartyAgencyReturnVariantMatches(variant, "max", "maximum")
                ? safeFortifications.OrderByDescending(item => item.Distance)
                    .First().Settlement.GatePosition
                : safeFortifications.OrderBy(item => Math.Abs(item.Distance - 60f))
                    .First().Settlement.GatePosition;
        }

        private static bool PartyAgencyReturnDurationMatches(string variant,
            double plannedReturnHours)
        {
            if (PartyAgencyReturnVariantMatches(variant, "min", "minimum"))
                return plannedReturnHours >= 0.9d && plannedReturnHours <= 1.2d;
            if (PartyAgencyReturnVariantMatches(variant, "max", "maximum"))
                return plannedReturnHours >= 11.8d && plannedReturnHours <= 12.2d;
            return plannedReturnHours > 1.2d && plannedReturnHours < 11.8d;
        }

        private static bool PartyAgencyReturnVariantMatches(string variant,
            string shortName, string manifestName)
        {
            return string.Equals(variant, shortName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(variant, manifestName,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static JObject PartyAgencyFixtureRoleEvidence(Hero hero)
        {
            MobileParty party = hero?.PartyBelongedTo;
            return new JObject
            {
                ["alive"] = hero?.IsAlive == true,
                ["adultLivingNpc"] = ReignConversationEligibility.IsAdultLivingNpc(hero),
                ["prisoner"] = hero?.IsPrisoner == true,
                ["companion"] = hero?.CompanionOf != null,
                ["governor"] = hero?.GovernorOf != null,
                ["partyId"] = party?.StringId ?? string.Empty,
                ["partyLeader"] = party?.LeaderHero == hero,
                ["ordinaryPartyMember"] = party != null && party.LeaderHero != hero,
                ["armyMember"] = party?.Army != null
                    && party.Army.LeaderParty != party,
                ["armyLeader"] = party?.Army?.LeaderParty == party,
                ["clanLeader"] = hero?.Clan?.Leader == hero,
                ["kingdomRuler"] = hero?.Clan?.Kingdom?.Leader == hero,
                ["questBoundParty"] = party?.IsCurrentlyUsedByAQuest == true
            };
        }

        private static JObject CapturePartyAgencyNativeState(Hero hero)
        {
            ReignTemporaryPartyGuestRecord record =
                ReignServices.TemporaryPartyGuest?.Records
                    .FirstOrDefault(item => item?.HeroStringId == hero?.StringId);
            MobileParty source = record == null ? hero?.PartyBelongedTo
                : MobileParty.All.FirstOrDefault(party => party != null
                    && string.Equals(party.StringId, record.SourcePartyStringId,
                        StringComparison.OrdinalIgnoreCase));
            return new JObject
            {
                ["heroId"] = hero?.StringId ?? string.Empty,
                ["clanId"] = hero?.Clan?.StringId ?? string.Empty,
                ["kingdomId"] = hero?.Clan?.Kingdom?.StringId ?? string.Empty,
                ["companionClanId"] = hero?.CompanionOf?.StringId ?? string.Empty,
                ["sourcePartyId"] = source?.StringId ?? string.Empty,
                ["sourcePartyExists"] = source != null,
                ["sourcePartyVisible"] = source?.IsVisible == true,
                ["sourcePartyActive"] = source?.IsActive == true,
                ["sourcePartyDisbanding"] = source?.IsDisbanding == true,
                ["sourcePartyInMapEvent"] = source?.MapEvent != null,
                ["sourcePartyInSiege"] = source?.SiegeEvent != null
                    || source?.BesiegerCamp != null,
                ["sourcePartyUsedByQuest"] = source?.IsCurrentlyUsedByAQuest == true,
                ["sourcePartyDefaultBehavior"] = source?.DefaultBehavior.ToString()
                    ?? string.Empty,
                ["sourcePartyTargetPartyId"] = source?.TargetParty?.StringId ?? string.Empty,
                ["sourcePartyTargetSettlementId"] = source?.TargetSettlement?.StringId
                    ?? string.Empty,
                ["sourcePartyClanId"] = source?.ActualClan?.StringId ?? string.Empty,
                ["sourcePartyIsLordParty"] = source?.LordPartyComponent != null,
                ["sourcePartyProtected"] = source == null
                    || ReignServices.TemporaryPartyGuest?.IsProtectedCamp(source) == true,
                ["sourcePartyX"] = source?.Position.X ?? 0f,
                ["sourcePartyY"] = source?.Position.Y ?? 0f,
                ["troopSignature"] = source == null ? string.Empty : string.Join("|",
                    source.MemberRoster.GetTroopRoster()
                        .OrderBy(element => element.Character?.StringId)
                        .Select(element => (element.Character?.StringId ?? string.Empty)
                            + ":" + element.Number + ":" + element.WoundedNumber)),
                ["protectedTroopSignature"] = source == null ? string.Empty : string.Join("|",
                    source.MemberRoster.GetTroopRoster()
                        .Where(element => !string.Equals(element.Character?.StringId,
                            hero?.StringId, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(element => element.Character?.StringId)
                        .Select(element => (element.Character?.StringId ?? string.Empty)
                            + ":" + element.Number + ":" + element.WoundedNumber)),
                ["prisonerSignature"] = source == null ? string.Empty : string.Join("|",
                    source.PrisonRoster.GetTroopRoster()
                        .OrderBy(element => element.Character?.StringId)
                        .Select(element => (element.Character?.StringId ?? string.Empty)
                            + ":" + element.Number + ":" + element.WoundedNumber)),
                ["itemSignature"] = source == null ? string.Empty : string.Join("|",
                    source.ItemRoster.OrderBy(element =>
                            element.EquipmentElement.Item?.StringId)
                        .Select(element =>
                            (element.EquipmentElement.Item?.StringId ?? string.Empty)
                            + ":" + element.Amount)),
                ["battleEquipmentSignature"] = PartyAgencyEquipmentSignature(hero?.BattleEquipment),
                ["civilianEquipmentSignature"] = PartyAgencyEquipmentSignature(hero?.CivilianEquipment),
                ["equipmentChangeAllowed"] = hero?.CanHeroEquipmentBeChanged() == true,
                ["temporaryGuestEquipmentLocked"] = ReignServices.TemporaryPartyGuest
                    ?.IsTemporaryGuestEquipmentLocked(hero) == true,
                ["morale"] = source?.Morale ?? 0f,
                ["recordPhase"] = record?.Phase.ToString() ?? string.Empty
            };
        }

        private static string PartyAgencyEquipmentSignature(Equipment equipment)
        {
            if (equipment == null) return string.Empty;
            return string.Join("|", Enumerable.Range(0, (int)EquipmentIndex.NumEquipmentSetSlots)
                .Select(index => new
                {
                    Slot = (EquipmentIndex)index,
                    Element = equipment[(EquipmentIndex)index]
                })
                .Select(value => value.Slot + ":"
                    + (value.Element.Item?.StringId ?? string.Empty) + ":"
                    + (value.Element.ItemModifier?.StringId ?? string.Empty)));
        }

        private static JObject CapturePartyAgencyUnrelatedState(Hero hero)
        {
            ReignArrestCampaignBehavior arrests = ReignServices.Arrest;
            int arrestCases = arrests?.Cases.Count(item => item != null
                && string.Equals(item.AccusedHeroStringId, hero?.StringId,
                    StringComparison.OrdinalIgnoreCase)) ?? 0;
            return new JObject
            {
                ["nativeRelation"] = hero?.GetRelation(Hero.MainHero) ?? 0,
                ["traits"] = new JObject
                {
                    ["valor"] = hero?.GetTraitLevel(DefaultTraits.Valor) ?? 0,
                    ["generosity"] = hero?.GetTraitLevel(DefaultTraits.Generosity) ?? 0,
                    ["honor"] = hero?.GetTraitLevel(DefaultTraits.Honor) ?? 0,
                    ["mercy"] = hero?.GetTraitLevel(DefaultTraits.Mercy) ?? 0,
                    ["calculating"] = hero?.GetTraitLevel(DefaultTraits.Calculating) ?? 0
                },
                ["isPrisoner"] = hero?.IsPrisoner == true,
                ["arrestCaseCount"] = arrestCases,
                ["rulerReputationState"] = CapturePrivatePartyAgencyState(
                    ReignServices.RulerReputation, "_state"),
                ["courtPersonalityReputationState"] = CapturePrivatePartyAgencyState(
                    ReignServices.CourtPersonalityReputation, "_state"),
                ["combatReputationState"] = CapturePrivatePartyAgencyState(
                    TaleWorlds.CampaignSystem.Campaign.Current
                        ?.GetCampaignBehavior<ReignCombatReputationCampaignBehavior>(),
                    "_persistedState")
            };
        }

        private static JToken CapturePrivatePartyAgencyState(object instance,
            string fieldName)
        {
            if (instance == null) return JValue.CreateNull();
            FieldInfo field = instance.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            object value = field?.GetValue(instance);
            if (value is JToken token) return token.DeepClone();
            return value == null ? JValue.CreateNull() : new JValue(value.ToString());
        }

        private static bool PartyAgencyNativeStateFidelity(JObject before, JObject after)
        {
            if (before?.Value<bool?>("sourcePartyExists") != true) return true;
            if (after?.Value<bool?>("sourcePartyExists") != true
                || after.Value<bool?>("sourcePartyVisible") != true
                || after.Value<bool?>("sourcePartyProtected") != true
                || after.Value<bool?>("sourcePartyActive") != true
                || after.Value<bool?>("sourcePartyDisbanding") == true
                || after.Value<bool?>("sourcePartyInMapEvent") == true
                || after.Value<bool?>("sourcePartyInSiege") == true
                || after.Value<bool?>("sourcePartyUsedByQuest") != true
                || !string.Equals(after.Value<string>("sourcePartyDefaultBehavior"),
                    "Hold", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrEmpty(after.Value<string>("sourcePartyTargetPartyId"))
                || !string.IsNullOrEmpty(after.Value<string>("sourcePartyTargetSettlementId")))
                return false;
            foreach (string key in new[]
            {
                "sourcePartyId", "sourcePartyClanId", "protectedTroopSignature",
                "prisonerSignature", "itemSignature"
            })
                if (!string.Equals(before.Value<string>(key), after.Value<string>(key),
                        StringComparison.Ordinal)) return false;
            if (before.Value<bool?>("sourcePartyIsLordParty")
                != after.Value<bool?>("sourcePartyIsLordParty")) return false;
            return Math.Abs(before.Value<float>("sourcePartyX")
                    - after.Value<float>("sourcePartyX")) < 0.01f
                && Math.Abs(before.Value<float>("sourcePartyY")
                    - after.Value<float>("sourcePartyY")) < 0.01f
                && Math.Abs(before.Value<float>("morale")
                    - after.Value<float>("morale")) < 0.01f;
        }
    }
}

$root = 'C:\Users\speed\Desktop\ReignBeta'
$serverPath = 'C:\Users\speed\Desktop\ReignServer\Program.cs'

$typePath = Join-Path $root 'src\World\ReignWorldActionType.cs'
$recordPath = Join-Path $root 'src\World\ReignWorldActionRecord.cs'
$resolverPath = Join-Path $root 'src\World\ReignObjectResolver.cs'
$validatorPath = Join-Path $root 'src\World\ReignActionValidator.cs'
$diplomacyPath = Join-Path $root 'src\World\ReignDiplomacyExecutor.cs'
$behaviorPath = Join-Path $root 'src\Campaign\ReignAICampaignBehavior.cs'
$settingsPath = Join-Path $root 'src\Settings\ReignBetaSettings.cs'
$clientPath = Join-Path $root 'src\Integration\ReignServerClient.cs'
$politicsPath = Join-Path $root 'src\World\ReignPoliticsExecutor.cs'

[System.IO.File]::WriteAllText($typePath, @'
namespace ReignBeta.World
{
    public enum ReignWorldActionType
    {
        Unknown = 0,
        DiplomacyDeclareWar = 1,
        DiplomacyMakePeace = 2,
        DiplomacyOfferTributePeace = 3,
        DiplomacyRecordPromise = 4,
        DiplomacyDemandReparationsPeace = 5,
        DiplomacyDemandSettlementPeace = 6,
        DiplomacyDemandSurrenderPeace = 7,
        StrategyRecruitAndRecover = 100,
        StrategyFormArmy = 101,
        StrategyAttackSettlement = 102,
        StrategyCaptureSettlement = 103,
        PoliticsStartRulingClanRebellion = 200,
        PoliticsInstallRulingClan = 201
    }
}
'@, [System.Text.UTF8Encoding]::new($false))

$record = [System.IO.File]::ReadAllText($recordPath)
if (-not $record.Contains('public string ActorClanStringId;')) {
    $record = $record.Replace(
        '        [SaveableField(23)]' + "`r`n" + '        public float AcceptedDay;',
        '        [SaveableField(23)]' + "`r`n" + '        public float AcceptedDay;' + "`r`n`r`n" +
        '        [SaveableField(24)]' + "`r`n" + '        public string ActorClanStringId;' + "`r`n`r`n" +
        '        [SaveableField(25)]' + "`r`n" + '        public string TargetClanStringId;' + "`r`n`r`n" +
        '        [SaveableField(26)]' + "`r`n" + '        public string SupporterClanIdsCsv;'
    )
    $record = $record.Replace(
        '            TargetSettlementStringId = string.Empty;' + "`r`n" + '            Reason = string.Empty;',
        '            TargetSettlementStringId = string.Empty;' + "`r`n" + '            ActorClanStringId = string.Empty;' + "`r`n" + '            TargetClanStringId = string.Empty;' + "`r`n" + '            SupporterClanIdsCsv = string.Empty;' + "`r`n" + '            Reason = string.Empty;'
    )
}
[System.IO.File]::WriteAllText($recordPath, $record, [System.Text.UTF8Encoding]::new($false))

$resolver = [System.IO.File]::ReadAllText($resolverPath)
if (-not $resolver.Contains('public static Clan FindClan')) {
    $resolver = $resolver.Replace(
        '        public static Kingdom FindKingdom(string stringId)',
        '        public static Clan FindClan(string stringId)' + "`r`n" +
        '        {' + "`r`n" +
        '            if (string.IsNullOrWhiteSpace(stringId))' + "`r`n" +
        '            {' + "`r`n" +
        '                return null;' + "`r`n" +
        '            }' + "`r`n`r`n" +
        '            return Clan.All.FirstOrDefault(x => x != null && x.StringId == stringId);' + "`r`n" +
        '        }' + "`r`n`r`n" +
        '        public static Kingdom FindKingdom(string stringId)'
    )
}
[System.IO.File]::WriteAllText($resolverPath, $resolver, [System.Text.UTF8Encoding]::new($false))

$validator = @'
using System.Linq;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.World
{
    public static class ReignActionValidator
    {
        public static bool Validate(ReignWorldActionRecord action, out string reason)
        {
            reason = string.Empty;

            if (action == null)
            {
                reason = "Action is null.";
                return false;
            }

            if (action.Type == ReignWorldActionType.Unknown)
            {
                reason = "Action type is unknown.";
                return false;
            }

            if (action.TypeValue >= 1 && action.TypeValue < 100)
            {
                return ValidateDiplomacy(action, out reason);
            }

            if (action.TypeValue >= 200)
            {
                return ValidatePolitics(action, out reason);
            }

            if (action.TypeValue >= 100)
            {
                return ValidateStrategy(action, out reason);
            }

            reason = "Unsupported action family.";
            return false;
        }

        private static bool ValidateDiplomacy(ReignWorldActionRecord action, out string reason)
        {
            Kingdom actor = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            Kingdom target = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);

            if (!IsLiveKingdom(actor))
            {
                reason = "Actor kingdom is missing or eliminated.";
                return false;
            }

            if (!IsLiveKingdom(target))
            {
                reason = "Target kingdom is missing or eliminated.";
                return false;
            }

            if (actor == target)
            {
                reason = "Actor and target kingdom are the same.";
                return false;
            }

            if (action.Type == ReignWorldActionType.DiplomacyDeclareWar && actor.IsAtWarWith(target))
            {
                reason = "Kingdoms are already at war.";
                return false;
            }

            if (IsPeaceDemand(action.Type) && !actor.IsAtWarWith(target))
            {
                reason = "Kingdoms are not at war.";
                return false;
            }

            if (action.Type == ReignWorldActionType.DiplomacyDemandSettlementPeace || action.Type == ReignWorldActionType.DiplomacyDemandSurrenderPeace)
            {
                Settlement settlement = ReignObjectResolver.FindSettlement(action.TargetSettlementStringId);
                if (settlement == null)
                {
                    reason = "A settlement target is required for settlement surrender.";
                    return false;
                }

                if (!settlement.IsFortification)
                {
                    reason = "Only towns and castles can be demanded in peace terms.";
                    return false;
                }

                if (settlement.MapFaction != target)
                {
                    reason = "Demanded settlement is not owned by the surrendering kingdom.";
                    return false;
                }

                if (actor.Leader == null)
                {
                    reason = "Demanding kingdom has no leader to receive surrendered settlements.";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        private static bool ValidateStrategy(ReignWorldActionRecord action, out string reason)
        {
            MobileParty party = ReignObjectResolver.FindHeroParty(action.ActorHeroStringId);
            Settlement target = ReignObjectResolver.FindSettlement(action.TargetSettlementStringId);

            if (party == null || !party.IsActive || party.LeaderHero == null)
            {
                reason = "Actor hero does not lead an active party.";
                return false;
            }

            if (party.IsMainParty)
            {
                reason = "Strategy executor will not override the main player party.";
                return false;
            }

            if (!party.IsLordParty)
            {
                reason = "Actor party is not a lord party.";
                return false;
            }

            if (party.MapEvent != null || party.BesiegedSettlement != null)
            {
                reason = "Actor party is already in a battle or siege.";
                return false;
            }

            if (target == null)
            {
                reason = "Target settlement is missing.";
                return false;
            }

            if (target.IsHideout)
            {
                reason = "Hideouts are not valid strategic settlement targets.";
                return false;
            }

            if (action.Type == ReignWorldActionType.StrategyAttackSettlement || action.Type == ReignWorldActionType.StrategyCaptureSettlement)
            {
                if (target.MapFaction == null || party.MapFaction == null || !target.MapFaction.IsAtWarWith(party.MapFaction))
                {
                    reason = "Target settlement is not hostile to the actor party.";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        private static bool ValidatePolitics(ReignWorldActionRecord action, out string reason)
        {
            Kingdom kingdom = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            Clan claimant = ReignObjectResolver.FindClan(action.ActorClanStringId);

            if (!IsLiveKingdom(kingdom))
            {
                reason = "Target kingdom is missing or eliminated.";
                return false;
            }

            if (claimant == null || claimant.IsEliminated)
            {
                reason = "Claimant clan is missing or eliminated.";
                return false;
            }

            if (claimant == kingdom.RulingClan)
            {
                reason = "Claimant clan already rules the kingdom.";
                return false;
            }

            if (action.Type == ReignWorldActionType.PoliticsInstallRulingClan && claimant.Kingdom != kingdom)
            {
                reason = "Claimant clan must still belong to the kingdom to take the throne directly.";
                return false;
            }

            if (action.Type == ReignWorldActionType.PoliticsStartRulingClanRebellion && claimant.Kingdom != kingdom)
            {
                reason = "Claimant clan must belong to the kingdom before starting a rebellion.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool IsPeaceDemand(ReignWorldActionType type)
        {
            return type == ReignWorldActionType.DiplomacyMakePeace
                || type == ReignWorldActionType.DiplomacyOfferTributePeace
                || type == ReignWorldActionType.DiplomacyDemandReparationsPeace
                || type == ReignWorldActionType.DiplomacyDemandSettlementPeace
                || type == ReignWorldActionType.DiplomacyDemandSurrenderPeace;
        }

        private static bool IsLiveKingdom(Kingdom kingdom)
        {
            return kingdom != null && !kingdom.IsEliminated;
        }
    }
}
'@
[System.IO.File]::WriteAllText($validatorPath, $validator, [System.Text.UTF8Encoding]::new($false))

$diplomacy = @'
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.World
{
    public static class ReignDiplomacyExecutor
    {
        public static ReignActionResult Execute(ReignWorldActionRecord action)
        {
            if (!ReignActionValidator.Validate(action, out string validationFailure))
            {
                return ReignActionResult.Fail(validationFailure);
            }

            Kingdom actor = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            Kingdom target = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);

            switch (action.Type)
            {
                case ReignWorldActionType.DiplomacyDeclareWar:
                    DeclareWarAction.ApplyByKingdomDecision(actor, target);
                    return ReignActionResult.Done(actor.InformalName + " declared war on " + target.InformalName + ".");

                case ReignWorldActionType.DiplomacyMakePeace:
                    MakePeaceAction.ApplyByKingdomDecision(actor, target, 0, 0);
                    return ReignActionResult.Done(actor.InformalName + " made peace with " + target.InformalName + ".");

                case ReignWorldActionType.DiplomacyOfferTributePeace:
                    MakePeaceAction.ApplyByKingdomDecision(actor, target, ReadIntTerm(action.TermsJson, "dailyTribute", 0), ReadIntTerm(action.TermsJson, "durationDays", 30));
                    return ReignActionResult.Done(actor.InformalName + " made tribute peace with " + target.InformalName + ".");

                case ReignWorldActionType.DiplomacyDemandReparationsPeace:
                    ApplyReparationsAndPeace(actor, target, action);
                    return ReignActionResult.Done(target.InformalName + " accepted reparations demanded by " + actor.InformalName + ".");

                case ReignWorldActionType.DiplomacyDemandSettlementPeace:
                    int transferred = TransferDemandedSettlements(actor, target, action);
                    ApplyReparationsAndPeace(actor, target, action);
                    return ReignActionResult.Done(target.InformalName + " surrendered " + transferred + " settlement(s) to " + actor.InformalName + " for peace.");

                case ReignWorldActionType.DiplomacyDemandSurrenderPeace:
                    int surrendered = TransferDemandedSettlements(actor, target, action);
                    ApplyReparationsAndPeace(actor, target, action);
                    return ReignActionResult.Done(target.InformalName + " accepted surrender terms from " + actor.InformalName + ", including " + surrendered + " settlement(s).");

                case ReignWorldActionType.DiplomacyRecordPromise:
                    return ReignActionResult.Done("Diplomatic promise recorded: " + action.Reason);

                default:
                    return ReignActionResult.Fail("Unsupported diplomacy action type: " + action.Type);
            }
        }

        private static void ApplyReparationsAndPeace(Kingdom actor, Kingdom target, ReignWorldActionRecord action)
        {
            int lumpGold = ReadIntTerm(action.TermsJson, "reparationsGold", 0);
            if (lumpGold > 0 && target.Leader != null && actor.Leader != null)
            {
                GiveGoldAction.ApplyBetweenCharacters(target.Leader, actor.Leader, lumpGold, true);
            }

            int dailyTribute = ReadIntTerm(action.TermsJson, "dailyTribute", ReadIntTerm(action.TermsJson, "reparationsDailyTribute", 0));
            int durationDays = ReadIntTerm(action.TermsJson, "durationDays", 30);
            if (dailyTribute > 0)
            {
                MakePeaceAction.ApplyByKingdomDecision(target, actor, dailyTribute, durationDays);
                return;
            }

            MakePeaceAction.ApplyByKingdomDecision(actor, target, 0, 0);
        }

        private static int TransferDemandedSettlements(Kingdom actor, Kingdom target, ReignWorldActionRecord action)
        {
            List<string> settlementIds = ReadStringListTerm(action.TermsJson, "settlementIds");
            if (!string.IsNullOrWhiteSpace(action.TargetSettlementStringId) && !settlementIds.Contains(action.TargetSettlementStringId))
            {
                settlementIds.Insert(0, action.TargetSettlementStringId);
            }

            int transferred = 0;
            foreach (string settlementId in settlementIds.Distinct())
            {
                Settlement settlement = ReignObjectResolver.FindSettlement(settlementId);
                if (settlement == null || !settlement.IsFortification || settlement.MapFaction != target)
                {
                    continue;
                }

                ChangeOwnerOfSettlementAction.ApplyByGift(settlement, actor.Leader);
                transferred++;
            }

            return transferred;
        }

        private static int ReadIntTerm(string json, string key, int fallback)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return fallback;
            }

            try
            {
                JObject obj = JObject.Parse(json);
                JToken token = obj[key];
                return token == null ? fallback : token.Value<int>();
            }
            catch
            {
                ReignLog.Warn("Failed to parse diplomacy terms JSON: " + json);
                return fallback;
            }
        }

        private static List<string> ReadStringListTerm(string json, string key)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            try
            {
                JObject obj = JObject.Parse(json);
                JToken token = obj[key];
                if (token is JArray array)
                {
                    result.AddRange(array.Select(x => x.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)));
                }
                else if (token != null)
                {
                    string value = token.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        result.Add(value);
                    }
                }
            }
            catch
            {
                ReignLog.Warn("Failed to parse settlement terms JSON: " + json);
            }

            return result;
        }
    }
}
'@
[System.IO.File]::WriteAllText($diplomacyPath, $diplomacy, [System.Text.UTF8Encoding]::new($false))

$politics = @'
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace ReignBeta.World
{
    public static class ReignPoliticsExecutor
    {
        public static ReignActionResult Execute(ReignWorldActionRecord action)
        {
            if (!ReignActionValidator.Validate(action, out string validationFailure))
            {
                return ReignActionResult.Fail(validationFailure);
            }

            Kingdom kingdom = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            Clan claimant = ReignObjectResolver.FindClan(action.ActorClanStringId);

            switch (action.Type)
            {
                case ReignWorldActionType.PoliticsStartRulingClanRebellion:
                    return StartRulingClanRebellion(action, kingdom, claimant);

                case ReignWorldActionType.PoliticsInstallRulingClan:
                    ChangeRulingClanAction.Apply(kingdom, claimant);
                    return ReignActionResult.Done(claimant.Name + " took control of " + kingdom.InformalName + ".");

                default:
                    return ReignActionResult.Fail("Unsupported politics action type: " + action.Type);
            }
        }

        private static ReignActionResult StartRulingClanRebellion(ReignWorldActionRecord action, Kingdom kingdom, Clan claimant)
        {
            List<Clan> rebelClans = new List<Clan> { claimant };
            foreach (string clanId in ReadSupporterClanIds(action))
            {
                Clan clan = ReignObjectResolver.FindClan(clanId);
                if (clan != null && clan.Kingdom == kingdom && clan != kingdom.RulingClan && !rebelClans.Contains(clan))
                {
                    rebelClans.Add(clan);
                }
            }

            int changed = 0;
            foreach (Clan clan in rebelClans)
            {
                if (clan.Kingdom != kingdom || clan == kingdom.RulingClan)
                {
                    continue;
                }

                ChangeKingdomAction.ApplyByLeaveWithRebellionAgainstKingdom(clan, true);
                if (!clan.IsAtWarWith(kingdom))
                {
                    DeclareWarAction.ApplyByRebellion(clan, kingdom);
                }

                changed++;
            }

            return ReignActionResult.Done(claimant.Name + " started a rebellion against " + kingdom.RulingClan.Name + " with " + changed + " clan(s).");
        }

        private static IEnumerable<string> ReadSupporterClanIds(ReignWorldActionRecord action)
        {
            foreach (string id in SplitCsv(action.SupporterClanIdsCsv))
            {
                yield return id;
            }

            if (string.IsNullOrWhiteSpace(action.TermsJson))
            {
                yield break;
            }

            JObject obj;
            try
            {
                obj = JObject.Parse(action.TermsJson);
            }
            catch
            {
                yield break;
            }

            JToken token = obj["supporterClanIds"];
            if (token is JArray array)
            {
                foreach (JToken item in array)
                {
                    string id = item.ToString();
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        yield return id;
                    }
                }
            }
        }

        private static IEnumerable<string> SplitCsv(string value)
        {
            return (value ?? string.Empty)
                .Split(',')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0);
        }
    }
}
'@
[System.IO.File]::WriteAllText($politicsPath, $politics, [System.Text.UTF8Encoding]::new($false))

$behavior = [System.IO.File]::ReadAllText($behaviorPath)
$behavior = $behavior.Replace(
    '            ReignActionResult result = action.TypeValue < 100' + "`r`n" +
    '                ? ReignDiplomacyExecutor.Execute(action)' + "`r`n" +
    '                : ReignStrategyExecutor.Execute(action);',
    '            ReignActionResult result = action.TypeValue < 100' + "`r`n" +
    '                ? ReignDiplomacyExecutor.Execute(action)' + "`r`n" +
    '                : action.TypeValue >= 200' + "`r`n" +
    '                    ? ReignPoliticsExecutor.Execute(action)' + "`r`n" +
    '                    : ReignStrategyExecutor.Execute(action);'
)
$behavior = $behavior.Replace(
    '            if (action.TypeValue < 100)' + "`r`n" +
    '            {' + "`r`n" +
    '                return settings.ExecuteDiplomacyActions;' + "`r`n" +
    '            }' + "`r`n`r`n" +
    '            return settings.ExecuteStrategyPlans;',
    '            if (action.TypeValue < 100)' + "`r`n" +
    '            {' + "`r`n" +
    '                return settings.ExecuteDiplomacyActions;' + "`r`n" +
    '            }' + "`r`n`r`n" +
    '            if (action.TypeValue >= 200)' + "`r`n" +
    '            {' + "`r`n" +
    '                return settings.ExecuteInternalPoliticsActions;' + "`r`n" +
    '            }' + "`r`n`r`n" +
    '            return settings.ExecuteStrategyPlans;'
)
[System.IO.File]::WriteAllText($behaviorPath, $behavior, [System.Text.UTF8Encoding]::new($false))

$settings = [System.IO.File]::ReadAllText($settingsPath)
if (-not $settings.Contains('_executeInternalPoliticsActions')) {
    $settings = $settings.Replace(
        '        private bool _executeStrategyPlans = true;',
        '        private bool _executeStrategyPlans = true;' + "`r`n" + '        private bool _executeInternalPoliticsActions = true;'
    )
    $settings = $settings.Replace(
        '        [SettingPropertyBool("Allow Autonomous World Ticks"',
        '        [SettingPropertyBool("Execute Internal Politics Actions", Order = 2, RequireRestart = false)]' + "`r`n" +
        '        [SettingPropertyGroup("Execution", GroupOrder = 1)]' + "`r`n" +
        '        public bool ExecuteInternalPoliticsActions' + "`r`n" +
        '        {' + "`r`n" +
        '            get { return _executeInternalPoliticsActions; }' + "`r`n" +
        '            set' + "`r`n" +
        '            {' + "`r`n" +
        '                if (value != _executeInternalPoliticsActions)' + "`r`n" +
        '                {' + "`r`n" +
        '                    _executeInternalPoliticsActions = value;' + "`r`n" +
        '                    OnPropertyChanged();' + "`r`n" +
        '                }' + "`r`n" +
        '            }' + "`r`n" +
        '        }' + "`r`n`r`n" +
        '        [SettingPropertyBool("Allow Autonomous World Ticks"'
    )
}
[System.IO.File]::WriteAllText($settingsPath, $settings, [System.Text.UTF8Encoding]::new($false))

$client = [System.IO.File]::ReadAllText($clientPath)
if (-not $client.Contains('ActorClanStringId =')) {
    $client = $client.Replace(
        '                ActorKingdomStringId = ReadString(obj, "actorKingdomStringId", ReadString(obj, "actorKingdomId", "")),',
        '                ActorKingdomStringId = ReadString(obj, "actorKingdomStringId", ReadString(obj, "actorKingdomId", "")),' + "`r`n" +
        '                ActorClanStringId = ReadString(obj, "actorClanStringId", ReadString(obj, "actorClanId", "")),'
    )
    $client = $client.Replace(
        '                TargetKingdomStringId = ReadString(obj, "targetKingdomStringId", ReadString(obj, "targetKingdomId", "")),',
        '                TargetKingdomStringId = ReadString(obj, "targetKingdomStringId", ReadString(obj, "targetKingdomId", "")),' + "`r`n" +
        '                TargetClanStringId = ReadString(obj, "targetClanStringId", ReadString(obj, "targetClanId", "")),'
    )
    $client = $client.Replace(
        '                TermsJson = ReadString(obj, "termsJson", ""),',
        '                TermsJson = ReadString(obj, "termsJson", ""),' + "`r`n" +
        '                SupporterClanIdsCsv = ReadSupporterClanIds(obj),'
    )
}
$client = $client.Replace(
    '                case "diplomacyrecordpromise":' + "`r`n" +
    '                case "record_promise":' + "`r`n" +
    '                    return ReignWorldActionType.DiplomacyRecordPromise;',
    '                case "diplomacyrecordpromise":' + "`r`n" +
    '                case "record_promise":' + "`r`n" +
    '                    return ReignWorldActionType.DiplomacyRecordPromise;' + "`r`n" +
    '                case "diplomacydemandreparationspeace":' + "`r`n" +
    '                case "demand_reparations_peace":' + "`r`n" +
    '                    return ReignWorldActionType.DiplomacyDemandReparationsPeace;' + "`r`n" +
    '                case "diplomacydemandsettlementpeace":' + "`r`n" +
    '                case "demand_settlement_peace":' + "`r`n" +
    '                    return ReignWorldActionType.DiplomacyDemandSettlementPeace;' + "`r`n" +
    '                case "diplomacydemandsurrenderpeace":' + "`r`n" +
    '                case "demand_surrender_peace":' + "`r`n" +
    '                    return ReignWorldActionType.DiplomacyDemandSurrenderPeace;'
)
$client = $client.Replace(
    '                case "strategycapturesettlement":' + "`r`n" +
    '                case "capture_settlement":' + "`r`n" +
    '                case "capture_settlement_plan":' + "`r`n" +
    '                    return ReignWorldActionType.StrategyCaptureSettlement;',
    '                case "strategycapturesettlement":' + "`r`n" +
    '                case "capture_settlement":' + "`r`n" +
    '                case "capture_settlement_plan":' + "`r`n" +
    '                    return ReignWorldActionType.StrategyCaptureSettlement;' + "`r`n" +
    '                case "politicsstartrulingclanrebellion":' + "`r`n" +
    '                case "start_ruling_clan_rebellion":' + "`r`n" +
    '                case "challenge_ruling_clan":' + "`r`n" +
    '                    return ReignWorldActionType.PoliticsStartRulingClanRebellion;' + "`r`n" +
    '                case "politicsinstallrulingclan":' + "`r`n" +
    '                case "install_ruling_clan":' + "`r`n" +
    '                    return ReignWorldActionType.PoliticsInstallRulingClan;'
)
if (-not $client.Contains('private static string ReadSupporterClanIds')) {
    $client = $client.Replace(
        '        private static string BuildSummary()',
        '        private static string ReadSupporterClanIds(JObject obj)' + "`r`n" +
        '        {' + "`r`n" +
        '            JToken token = obj?["supporterClanIds"];' + "`r`n" +
        '            if (token is JArray array)' + "`r`n" +
        '            {' + "`r`n" +
        '                return string.Join(",", array.Select(x => x.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)));' + "`r`n" +
        '            }' + "`r`n`r`n" +
        '            return ReadString(obj, "supporterClanIdsCsv", "");' + "`r`n" +
        '        }' + "`r`n`r`n" +
        '        private static string BuildSummary()'
    )
}
[System.IO.File]::WriteAllText($clientPath, $client, [System.Text.UTF8Encoding]::new($false))

$server = [System.IO.File]::ReadAllText($serverPath)
$server = $server.Replace(
    '                CatalogCommand("record_promise", "diplomacy", "DiplomacyRecordPromise", "actorKingdomId,targetKingdomId,reason", "Records a diplomatic promise or obligation for future memory."),',
    '                CatalogCommand("record_promise", "diplomacy", "DiplomacyRecordPromise", "actorKingdomId,targetKingdomId,reason", "Records a diplomatic promise or obligation for future memory."),' + "`r`n" +
    '                CatalogCommand("demand_reparations_peace", "diplomacy", "DiplomacyDemandReparationsPeace", "actorKingdomId,targetKingdomId,reason,terms.reparationsGold,terms.dailyTribute,terms.durationDays", "Target kingdom pays gold and/or daily tribute to receive peace."),' + "`r`n" +
    '                CatalogCommand("demand_settlement_peace", "diplomacy", "DiplomacyDemandSettlementPeace", "actorKingdomId,targetKingdomId,targetSettlementId,reason,terms.settlementIds", "Target kingdom surrenders one or more towns/castles in exchange for peace."),' + "`r`n" +
    '                CatalogCommand("demand_surrender_peace", "diplomacy", "DiplomacyDemandSurrenderPeace", "actorKingdomId,targetKingdomId,targetSettlementId,reason,terms.settlementIds,terms.reparationsGold,terms.dailyTribute", "Target kingdom accepts broad surrender terms: settlements, gold, tribute, and peace.")'
)
$server = $server.Replace(
    '                CatalogCommand("capture_settlement_plan", "strategy", "StrategyCaptureSettlement", "actorHeroId,targetSettlementId,reason,minimumTroops,desiredStrength", "Multi-stage plan: validate war, recruit, form army, move, and besiege."),',
    '                CatalogCommand("capture_settlement_plan", "strategy", "StrategyCaptureSettlement", "actorHeroId,targetSettlementId,reason,minimumTroops,desiredStrength", "Multi-stage plan: validate war, recruit, form army, move, and besiege."),' + "`r`n" +
    '                CatalogCommand("start_ruling_clan_rebellion", "politics", "PoliticsStartRulingClanRebellion", "actorKingdomId,actorClanId,reason,terms.supporterClanIds", "A claimant clan and supporters break away in rebellion against the ruling clan."),' + "`r`n" +
    '                CatalogCommand("install_ruling_clan", "politics", "PoliticsInstallRulingClan", "actorKingdomId,actorClanId,reason", "Installs a claimant clan as the kingdom ruling clan after a successful internal challenge.")'
)
$server = $server.Replace(
    '                ["targetHeroStringId"] = ReadFirstString(raw, "targetHeroStringId", "targetHeroId"),' + "`r`n" +
    '                ["targetKingdomStringId"] = ReadFirstString(raw, "targetKingdomStringId", "targetKingdomId"),',
    '                ["targetHeroStringId"] = ReadFirstString(raw, "targetHeroStringId", "targetHeroId"),' + "`r`n" +
    '                ["actorClanStringId"] = ReadFirstString(raw, "actorClanStringId", "actorClanId", "claimantClanId"),' + "`r`n" +
    '                ["targetClanStringId"] = ReadFirstString(raw, "targetClanStringId", "targetClanId"),' + "`r`n" +
    '                ["supporterClanIdsCsv"] = ReadSupporterClanIds(raw, terms),' + "`r`n" +
    '                ["targetKingdomStringId"] = ReadFirstString(raw, "targetKingdomStringId", "targetKingdomId"),'
)
$server = $server.Replace(
    '            if (mappedType.StartsWith("Diplomacy", StringComparison.OrdinalIgnoreCase))' + "`r`n" +
    '            {' + "`r`n" +
    '                Require(record, "actorKingdomStringId", errors);' + "`r`n" +
    '                Require(record, "targetKingdomStringId", errors);' + "`r`n" +
    '            }' + "`r`n" +
    '            else if (mappedType.StartsWith("Strategy", StringComparison.OrdinalIgnoreCase))',
    '            if (mappedType.StartsWith("Diplomacy", StringComparison.OrdinalIgnoreCase))' + "`r`n" +
    '            {' + "`r`n" +
    '                Require(record, "actorKingdomStringId", errors);' + "`r`n" +
    '                Require(record, "targetKingdomStringId", errors);' + "`r`n" +
    '                if ((mappedType == "DiplomacyDemandSettlementPeace" || mappedType == "DiplomacyDemandSurrenderPeace") && string.IsNullOrWhiteSpace(ReadString(record, "targetSettlementStringId", "")))' + "`r`n" +
    '                {' + "`r`n" +
    '                    string firstSettlement = FirstStringFromTerms(terms, "settlementIds");' + "`r`n" +
    '                    if (!string.IsNullOrWhiteSpace(firstSettlement))' + "`r`n" +
    '                    {' + "`r`n" +
    '                        record["targetSettlementStringId"] = firstSettlement;' + "`r`n" +
    '                    }' + "`r`n" +
    '                }' + "`r`n" +
    '                if ((mappedType == "DiplomacyDemandSettlementPeace" || mappedType == "DiplomacyDemandSurrenderPeace") && string.IsNullOrWhiteSpace(ReadString(record, "targetSettlementStringId", "")))' + "`r`n" +
    '                {' + "`r`n" +
    '                    errors.Add("targetSettlementStringId or terms.settlementIds is required for settlement surrender.");' + "`r`n" +
    '                }' + "`r`n" +
    '            }' + "`r`n" +
    '            else if (mappedType.StartsWith("Politics", StringComparison.OrdinalIgnoreCase))' + "`r`n" +
    '            {' + "`r`n" +
    '                Require(record, "actorKingdomStringId", errors);' + "`r`n" +
    '                Require(record, "actorClanStringId", errors);' + "`r`n" +
    '            }' + "`r`n" +
    '            else if (mappedType.StartsWith("Strategy", StringComparison.OrdinalIgnoreCase))'
)
$server = $server.Replace(
    '                case "diplomacyrecordpromise":' + "`r`n" +
    '                case "recordpromise": return "record_promise";',
    '                case "diplomacyrecordpromise":' + "`r`n" +
    '                case "recordpromise": return "record_promise";' + "`r`n" +
    '                case "diplomacydemandreparationspeace":' + "`r`n" +
    '                case "demandreparationspeace": return "demand_reparations_peace";' + "`r`n" +
    '                case "diplomacydemandsettlementpeace":' + "`r`n" +
    '                case "demandsettlementpeace": return "demand_settlement_peace";' + "`r`n" +
    '                case "diplomacydemandsurrenderpeace":' + "`r`n" +
    '                case "demandsurrenderpeace":' + "`r`n" +
    '                case "surrender_peace": return "demand_surrender_peace";'
)
$server = $server.Replace(
    '                case "strategycapturesettlement":' + "`r`n" +
    '                case "capture_settlement": return "capture_settlement_plan";',
    '                case "strategycapturesettlement":' + "`r`n" +
    '                case "capture_settlement": return "capture_settlement_plan";' + "`r`n" +
    '                case "politicsstartrulingclanrebellion":' + "`r`n" +
    '                case "challenge_ruling_clan": return "start_ruling_clan_rebellion";' + "`r`n" +
    '                case "politicsinstallrulingclan": return "install_ruling_clan";'
)
$server = $server.Replace(
    '                case "record_promise": return "DiplomacyRecordPromise";',
    '                case "record_promise": return "DiplomacyRecordPromise";' + "`r`n" +
    '                case "demand_reparations_peace": return "DiplomacyDemandReparationsPeace";' + "`r`n" +
    '                case "demand_settlement_peace": return "DiplomacyDemandSettlementPeace";' + "`r`n" +
    '                case "demand_surrender_peace": return "DiplomacyDemandSurrenderPeace";'
)
$server = $server.Replace(
    '                case "capture_settlement_plan": return "StrategyCaptureSettlement";',
    '                case "capture_settlement_plan": return "StrategyCaptureSettlement";' + "`r`n" +
    '                case "start_ruling_clan_rebellion": return "PoliticsStartRulingClanRebellion";' + "`r`n" +
    '                case "install_ruling_clan": return "PoliticsInstallRulingClan";'
)
if (-not $server.Contains('private static string ReadSupporterClanIds')) {
    $server = $server.Replace(
        '        private static void Require(Dictionary<string, object> record, string key, List<string> errors)',
        '        private static string ReadSupporterClanIds(Dictionary<string, object> raw, Dictionary<string, object> terms)' + "`r`n" +
        '        {' + "`r`n" +
        '            List<string> ids = ReadStringList(raw, "supporterClanIds");' + "`r`n" +
        '            ids.AddRange(ReadStringList(terms, "supporterClanIds"));' + "`r`n" +
        '            return string.Join(",", ids.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());' + "`r`n" +
        '        }' + "`r`n`r`n" +
        '        private static string FirstStringFromTerms(Dictionary<string, object> terms, string key)' + "`r`n" +
        '        {' + "`r`n" +
        '            return ReadStringList(terms, key).FirstOrDefault() ?? string.Empty;' + "`r`n" +
        '        }' + "`r`n`r`n" +
        '        private static void Require(Dictionary<string, object> record, string key, List<string> errors)'
    )
}
[System.IO.File]::WriteAllText($serverPath, $server, [System.Text.UTF8Encoding]::new($false))

Select-String -LiteralPath $serverPath,$typePath,$diplomacyPath,$politicsPath,$behaviorPath,$settingsPath,$clientPath -Pattern 'DemandReparations|DemandSettlement|DemandSurrender|RulingClanRebellion|InstallRulingClan|ExecuteInternalPolitics|demand_surrender_peace|start_ruling_clan_rebellion' -Context 0,1

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private const string GauntletMutationConfirmation =
            "armed_gauntlet_fixture";
        private static readonly Dictionary<string, JObject>
            GauntletInverseReceipts =
                new Dictionary<string, JObject>(
                    StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> GauntletPresentHeroIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, HashSet<string>>
            ActionProbeBeforeIds = new Dictionary<string, HashSet<string>>(
                StringComparer.OrdinalIgnoreCase);
        private static string _gauntletRoom = string.Empty;

        private static async Task<LiveCommandResult> GauntletSnapshotAsync(
            JObject command)
        {
            string guard = ValidateGauntletCommand(command, false);
            if (!string.IsNullOrWhiteSpace(guard))
                return LiveCommandResult.Failed(guard);
            JObject snapshot = await ReignMainThread.InvokeAsync(() =>
            {
                if (string.Equals(command.Value<string>("actionProbePhase"),
                        "before", StringComparison.OrdinalIgnoreCase))
                {
                    string caseId = command.Value<string>("actionProbeCaseId")
                        ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(caseId))
                        ActionProbeBeforeIds[_activeRunId + ":" + caseId] =
                            new HashSet<string>((ReignAICampaignBehavior.Instance?.Actions
                                ?? new List<ReignWorldActionRecord>())
                                .Where(action => action != null)
                                .Select(action => action.ActionId),
                                StringComparer.OrdinalIgnoreCase);
                }
                JObject result = BuildGauntletNativeSnapshot(command);
                if (string.Equals(command.Value<string>("actionProbePhase"),
                        "after", StringComparison.OrdinalIgnoreCase))
                    ActionProbeBeforeIds.Remove(_activeRunId + ":"
                        + (command.Value<string>("actionProbeCaseId") ?? string.Empty));
                return result;
            }).ConfigureAwait(false);
            return LiveCommandResult.Completed(
                "Authoritative gauntlet state captured.",
                snapshot);
        }

        private static async Task<LiveCommandResult> ActionProbeWaitAsync(
            JObject command)
        {
            string guard = ValidateGauntletCommand(command, false);
            if (!string.IsNullOrWhiteSpace(guard))
                return LiveCommandResult.Failed(guard);
            string caseId = command.Value<string>("actionProbeCaseId")
                ?? string.Empty;
            string expectedType = command.Value<string>("expectedType")
                ?? string.Empty;
            string key = _activeRunId + ":" + caseId;
            if (string.IsNullOrWhiteSpace(caseId)
                || string.IsNullOrWhiteSpace(expectedType)
                || !ActionProbeBeforeIds.TryGetValue(key, out HashSet<string> beforeIds))
                return LiveCommandResult.Failed(
                    "An action probe requires its exact before snapshot and expected type.");

            int seconds = Math.Max(1, Math.Min(30,
                command.Value<int?>("waitSeconds") ?? 15));
            DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
            JObject observation = new JObject();
            do
            {
                observation = await ReignMainThread.InvokeAsync(() =>
                {
                    List<ReignWorldActionRecord> fresh =
                        (ReignAICampaignBehavior.Instance?.Actions
                            ?? new List<ReignWorldActionRecord>())
                        .Where(action => action != null
                            && !beforeIds.Contains(action.ActionId))
                        .ToList();
                    ReignWorldActionRecord expected = fresh.LastOrDefault(action =>
                        string.Equals(action.Type.ToString(), expectedType,
                            StringComparison.OrdinalIgnoreCase));
                    ReignActionResult result = expected == null ? null
                        : ReignAICampaignBehavior.Instance?.GetLastActionResult(
                            expected.ActionId);
                    bool terminal = expected != null
                        && (expected.Status == ReignWorldActionStatus.Completed
                            || expected.Status == ReignWorldActionStatus.Failed
                            || expected.Status == ReignWorldActionStatus.Rejected
                            || expected.Status == ReignWorldActionStatus.Cancelled);
                    return new JObject
                    {
                        ["caseId"] = caseId,
                        ["expectedType"] = expectedType,
                        ["freshActionIds"] = new JArray(fresh.Select(action =>
                            action.ActionId ?? string.Empty)),
                        ["expectedActionId"] = expected?.ActionId ?? string.Empty,
                        ["expectedStatus"] = expected?.Status.ToString() ?? string.Empty,
                        ["terminal"] = terminal,
                        ["resultCode"] = result?.ResultCode ?? string.Empty,
                        ["resultOutcome"] = result?.Outcome ?? string.Empty,
                        ["resultSuccess"] = result?.Success ?? false,
                        ["resultCompleted"] = result?.Completed ?? false,
                        ["failureReason"] = expected?.FailureReason ?? string.Empty
                    };
                }).ConfigureAwait(false);
                if (observation.Value<bool>("terminal")) break;
                await Task.Delay(250).ConfigureAwait(false);
            } while (DateTime.UtcNow < deadline);
            ActionProbeBeforeIds.Remove(key);
            observation["waitSeconds"] = seconds;
            return LiveCommandResult.Completed(
                observation.Value<bool>("terminal")
                    ? "Observed the native action's terminal state."
                    : "Action remains pending or was not detected during the bounded wait.",
                observation);
        }

        private static async Task<LiveCommandResult> GauntletApplyFixtureAsync(
            JObject command)
        {
            string guard = ValidateGauntletCommand(command, true);
            if (!string.IsNullOrWhiteSpace(guard))
                return LiveCommandResult.Failed(guard);
            JObject fixture = command["fixture"] as JObject ?? new JObject();
            return await ReignMainThread.InvokeAsync(() =>
            {
                string validation = ValidateFixtureTargets(fixture);
                if (!string.IsNullOrWhiteSpace(validation))
                    return LiveCommandResult.Failed(validation);

                JObject before = BuildGauntletNativeSnapshot(command);
                string receiptId =
                    "gauntlet-inverse-" + Guid.NewGuid().ToString("N");
                JObject inverse = new JObject
                {
                    ["receiptId"] = receiptId,
                    ["campaignId"] = ReignCampaignIdentity.CurrentCampaignId(),
                    ["gameInstanceId"] = _gameInstanceId,
                    ["runId"] = _activeRunId,
                    ["room"] = _gauntletRoom,
                    ["presentHeroIds"] = new JArray(GauntletPresentHeroIds),
                    ["playerGold"] = Hero.MainHero?.Gold ?? 0,
                    ["relations"] = CaptureRelationInverse(
                        fixture["relations"] as JArray)
                };

                if (fixture.Property("playerGold") != null
                    && Hero.MainHero != null)
                {
                    int target = Math.Max(
                        0,
                        fixture.Value<int>("playerGold"));
                    Hero.MainHero.ChangeHeroGold(
                        target - Hero.MainHero.Gold);
                }
                ApplyRelations(fixture["relations"] as JArray);
                if (fixture["presentHeroIds"] is JArray roster)
                {
                    GauntletPresentHeroIds.Clear();
                    foreach (string id in roster.Values<string>())
                        GauntletPresentHeroIds.Add(id);
                }
                if (fixture.Property("room") != null)
                    _gauntletRoom =
                        fixture.Value<string>("room") ?? string.Empty;

                GauntletInverseReceipts[receiptId] = inverse;
                JObject after = BuildGauntletNativeSnapshot(command);
                return LiveCommandResult.Completed(
                    "Guarded gauntlet fixture applied.",
                    new JObject
                    {
                        ["receiptId"] = receiptId,
                        ["before"] = before,
                        ["after"] = after,
                        ["inverse"] = inverse
                    });
            }).ConfigureAwait(false);
        }

        private static async Task<LiveCommandResult> GauntletRestoreFixtureAsync(
            JObject command)
        {
            string guard = ValidateGauntletCommand(command, true);
            if (!string.IsNullOrWhiteSpace(guard))
                return LiveCommandResult.Failed(guard);
            string receiptId =
                command.Value<string>("receiptId") ?? string.Empty;
            if (!GauntletInverseReceipts.TryGetValue(
                    receiptId,
                    out JObject inverse))
                return LiveCommandResult.Failed(
                    "The inverse receipt is missing or was already restored.");
            if (!string.Equals(
                    inverse.Value<string>("campaignId"),
                    ReignCampaignIdentity.CurrentCampaignId(),
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    inverse.Value<string>("gameInstanceId"),
                    _gameInstanceId,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    inverse.Value<string>("runId"),
                    _activeRunId,
                    StringComparison.OrdinalIgnoreCase))
                return LiveCommandResult.Failed(
                    "The inverse receipt belongs to another campaign, game instance, or run.");

            return await ReignMainThread.InvokeAsync(() =>
            {
                if (Hero.MainHero != null)
                {
                    int gold = inverse.Value<int?>("playerGold")
                        ?? Hero.MainHero.Gold;
                    Hero.MainHero.ChangeHeroGold(
                        gold - Hero.MainHero.Gold);
                }
                ApplyRelations(inverse["relations"] as JArray);
                GauntletPresentHeroIds.Clear();
                foreach (string id in
                    (inverse["presentHeroIds"] as JArray
                        ?? new JArray()).Values<string>())
                    GauntletPresentHeroIds.Add(id);
                _gauntletRoom =
                    inverse.Value<string>("room") ?? string.Empty;
                JObject restored = BuildGauntletNativeSnapshot(command);
                GauntletInverseReceipts.Remove(receiptId);
                return LiveCommandResult.Completed(
                    "Gauntlet fixture restored exactly once.",
                    new JObject
                    {
                        ["receiptId"] = receiptId,
                        ["restored"] = restored
                    });
            }).ConfigureAwait(false);
        }

        private static async Task<LiveCommandResult> GauntletAdvanceTimeAsync(
            JObject command)
        {
            string guard = ValidateGauntletCommand(command, true);
            if (!string.IsNullOrWhiteSpace(guard))
                return LiveCommandResult.Failed(guard);
            double days = Math.Max(
                0d,
                Math.Min(31.5d, command.Value<double?>("days") ?? 0d));
            if (days <= 0d)
                return LiveCommandResult.Failed(
                    "A positive time advance of at most one season is required.");
            double start = CampaignTime.Now.ToDays;
            double target = start + days;
            CampaignTimeControlMode prior =
                await ReignMainThread.InvokeAsync(() =>
                {
                    TaleWorlds.CampaignSystem.Campaign campaign =
                        TaleWorlds.CampaignSystem.Campaign.Current;
                    CampaignTimeControlMode mode =
                        campaign.TimeControlMode;
                    campaign.TimeControlMode =
                        CampaignTimeControlMode.StoppableFastForward;
                    return mode;
                }).ConfigureAwait(false);
            DateTime deadline = DateTime.UtcNow.AddSeconds(
                Math.Max(15, Math.Min(
                    600,
                    command.Value<int?>("timeoutSeconds") ?? 180)));
            while (CampaignTime.Now.ToDays < target
                && DateTime.UtcNow < deadline)
                await Task.Delay(250).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() =>
                TaleWorlds.CampaignSystem.Campaign.Current.TimeControlMode =
                    prior).ConfigureAwait(false);
            bool reached = CampaignTime.Now.ToDays >= target;
            JObject data = new JObject
            {
                ["startDay"] = start,
                ["targetDay"] = target,
                ["worldDay"] = CampaignTime.Now.ToDays,
                ["reached"] = reached
            };
            return reached
                ? LiveCommandResult.Completed(
                    "Campaign time advanced through the native clock.",
                    data)
                : LiveCommandResult.Failed(
                    "The native clock did not reach the requested day before timeout.",
                    data);
        }

        private static async Task<LiveCommandResult>
            GauntletRosterMutationAsync(JObject command, bool arrive)
        {
            string guard = ValidateGauntletCommand(command, true);
            if (!string.IsNullOrWhiteSpace(guard))
                return LiveCommandResult.Failed(guard);
            return await ReignMainThread.InvokeAsync(() =>
            {
                string heroId = command.Value<string>("heroId")
                    ?? string.Empty;
                Hero hero = FindGauntletHero(heroId);
                if (!IsLivingNpc(hero))
                    return LiveCommandResult.Failed(
                        "Arrival/departure requires an adult living NPC.");
                if (arrive) GauntletPresentHeroIds.Add(hero.StringId);
                else GauntletPresentHeroIds.Remove(hero.StringId);
                return LiveCommandResult.Completed(
                    arrive
                        ? "Hero added to the fixture presence roster."
                        : "Hero removed from the fixture presence roster.",
                    new JObject
                    {
                        ["heroId"] = hero.StringId,
                        ["presentHeroIds"] =
                            new JArray(GauntletPresentHeroIds)
                    });
            }).ConfigureAwait(false);
        }

        private static string ValidateGauntletCommand(
            JObject command,
            bool mutating)
        {
            string gauntletRunId =
                command.Value<string>("gauntletRunId") ?? string.Empty;
            string activeLiveRunId =
                command.Value<string>("runId") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(gauntletRunId)
                || string.IsNullOrWhiteSpace(activeLiveRunId)
                || !string.Equals(
                    activeLiveRunId,
                    _activeRunId,
                    StringComparison.OrdinalIgnoreCase))
                return "A gauntlet operation requires an active live run and its stable gauntletRunId.";
            if (!string.Equals(
                    command.Value<string>("campaignId"),
                    ReignCampaignIdentity.CurrentCampaignId(),
                    StringComparison.OrdinalIgnoreCase))
                return "The gauntlet command campaign does not match the loaded campaign.";
            if (!string.Equals(
                    command.Value<string>("gameInstanceId"),
                    _gameInstanceId,
                    StringComparison.OrdinalIgnoreCase))
                return "The gauntlet command targets a stale game instance.";
            if (mutating
                && (!command.Value<bool?>("derivativeSave").GetValueOrDefault()
                    || !string.Equals(
                        command.Value<string>("confirmation"),
                        GauntletMutationConfirmation,
                        StringComparison.Ordinal)))
                return "Native mutation requires a derivative save and the armed gauntlet fixture confirmation.";
            return string.Empty;
        }

        private static string ValidateFixtureTargets(JObject fixture)
        {
            List<string> ids = new List<string>();
            ids.AddRange((fixture["presentHeroIds"] as JArray
                ?? new JArray()).Values<string>());
            foreach (JObject row in (fixture["relations"] as JArray
                ?? new JArray()).OfType<JObject>())
            {
                ids.Add(row.Value<string>("subjectId") ?? string.Empty);
                ids.Add(row.Value<string>("targetId") ?? string.Empty);
            }
            if (ids.Any(string.IsNullOrWhiteSpace))
                return "Fixture target IDs may not be empty.";
            if (ids.Count != ids.Distinct(
                    StringComparer.OrdinalIgnoreCase).Count()
                && (fixture["presentHeroIds"] as JArray
                    ?? new JArray()).Values<string>()
                    .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .Any(group => group.Count() > 1))
                return "The fixture presence roster contains duplicate IDs.";
            foreach (string id in ids.Distinct(
                StringComparer.OrdinalIgnoreCase))
            {
                Hero hero = FindGauntletHero(id);
                if (hero != Hero.MainHero && !IsLivingNpc(hero))
                    return "Fixture target '" + id
                        + "' is missing, dead, or underage.";
            }
            return string.Empty;
        }

        private static JObject BuildGauntletNativeSnapshot(JObject command)
        {
            Settlement settlement = Settlement.CurrentSettlement
                ?? MobileParty.MainParty?.CurrentSettlement;
            List<Hero> heroes = ResolveSnapshotHeroes(command);
            JArray heroRows = new JArray(heroes.Select(hero =>
            {
                JObject row = HeroTargetJson(hero);
                row["gold"] = hero.Gold;
                row["isPrisoner"] = hero.IsPrisoner;
                row["spouseId"] = hero.Spouse?.StringId ?? string.Empty;
                row["fatherId"] = hero.Father?.StringId ?? string.Empty;
                row["motherId"] = hero.Mother?.StringId ?? string.Empty;
                row["childrenIds"] = new JArray(
                    hero.Children.Select(child => child.StringId));
                row["governorOfSettlementId"] =
                    hero.GovernorOf?.Settlement?.StringId ?? string.Empty;
                JArray observedItemIds = command["itemIds"] as JArray;
                if (observedItemIds != null && observedItemIds.Count > 0)
                    row["observedInventory"] = BuildObservedInventory(
                        hero, observedItemIds.Values<string>());
                return row;
            }));
            JArray relations = new JArray();
            foreach (Hero subject in heroes)
            foreach (Hero target in heroes)
            {
                if (subject == target) continue;
                relations.Add(new JObject
                {
                    ["subjectId"] = subject.StringId,
                    ["targetId"] = target.StringId,
                    ["value"] = subject.GetRelation(target)
                });
            }
            JArray actions = new JArray((ReignAICampaignBehavior.Instance?.Actions
                ?? new List<ReignWorldActionRecord>())
                .Where(action => action != null)
                .Reverse().Take(64).Reverse()
                .Select(action =>
                {
                    ReignActionResult result = ReignAICampaignBehavior.Instance
                        ?.GetLastActionResult(action.ActionId);
                    return new JObject
                    {
                        ["actionId"] = action.ActionId ?? string.Empty,
                        ["type"] = action.Type.ToString(),
                        ["status"] = action.Status.ToString(),
                        ["actorHeroId"] = action.ActorHeroStringId ?? string.Empty,
                        ["targetHeroId"] = action.TargetHeroStringId ?? string.Empty,
                        ["targetSettlementId"] = action.TargetSettlementStringId ?? string.Empty,
                        ["negotiatedCommand"] = action.NegotiatedCommand ?? string.Empty,
                        ["failureReason"] = action.FailureReason ?? string.Empty,
                        ["termsJson"] = (action.TermsJson ?? string.Empty).Substring(0,
                            Math.Min(4000, (action.TermsJson ?? string.Empty).Length)),
                        ["resultCode"] = result?.ResultCode ?? string.Empty,
                        ["outcome"] = result?.Outcome ?? string.Empty,
                        ["completed"] = result?.Completed ?? false,
                        ["success"] = result?.Success ?? false,
                        ["effects"] = result?.Effects == null
                            ? new JArray()
                            : new JArray(result.Effects.Select(JObject.FromObject))
                    };
                }));
            return new JObject
            {
                ["campaignId"] = ReignCampaignIdentity.CurrentCampaignId(),
                ["gameInstanceId"] = _gameInstanceId,
                ["worldDay"] = CampaignTime.Now.ToDays,
                ["season"] = CampaignTime.Now.GetSeasonOfYear.ToString(),
                ["hour"] = CampaignTime.Now.ToHours % 24d,
                ["timeMode"] =
                    TaleWorlds.CampaignSystem.Campaign.Current
                        .TimeControlMode.ToString(),
                ["settlementId"] = settlement?.StringId ?? string.Empty,
                ["settlementName"] = settlement?.Name?.ToString() ?? string.Empty,
                ["room"] = _gauntletRoom,
                ["presentHeroIds"] = new JArray(GauntletPresentHeroIds),
                ["heroes"] = heroRows,
                ["relations"] = relations,
                ["actions"] = actions,
                ["playerGold"] = Hero.MainHero?.Gold ?? 0,
                ["playerClanId"] = Clan.PlayerClan?.StringId ?? string.Empty,
                ["playerClanTier"] = Clan.PlayerClan?.Tier ?? 0,
                ["playerKingdomId"] =
                    Clan.PlayerClan?.Kingdom?.StringId ?? string.Empty,
                ["saveSync"] =
                    ReignSaveSyncCampaignBehavior.NativeSaveObservation()
            };
        }

        private static JObject BuildObservedInventory(
            Hero hero,
            IEnumerable<string> requestedItemIds)
        {
            HashSet<string> itemIds = new HashSet<string>(
                (requestedItemIds ?? Enumerable.Empty<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Take(16),
                StringComparer.OrdinalIgnoreCase);
            MobileParty party = hero?.PartyBelongedTo;
            JArray items = new JArray();
            if (party?.ItemRoster != null)
            {
                foreach (IGrouping<string, ItemRosterElement> group in party.ItemRoster
                    .Where(element => element.EquipmentElement.Item != null
                        && itemIds.Contains(element.EquipmentElement.Item.StringId))
                    .GroupBy(element =>
                        (element.EquipmentElement.Item?.StringId ?? string.Empty)
                        + "|" + (element.EquipmentElement.ItemModifier?.StringId
                            ?? string.Empty),
                        StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
                {
                    ItemRosterElement first = group.First();
                    items.Add(new JObject
                    {
                        ["itemId"] = first.EquipmentElement.Item?.StringId
                            ?? string.Empty,
                        ["name"] = first.EquipmentElement.Item?.Name?.ToString()
                            ?? string.Empty,
                        ["itemModifierId"] = first.EquipmentElement.ItemModifier
                            ?.StringId ?? string.Empty,
                        ["amount"] = group.Sum(element => element.Amount)
                    });
                }
            }
            return new JObject
            {
                ["partyId"] = party?.StringId ?? string.Empty,
                ["controlsPartyInventory"] = party != null,
                ["requestedItemIds"] = new JArray(itemIds.OrderBy(id => id,
                    StringComparer.OrdinalIgnoreCase)),
                ["items"] = items
            };
        }

        private static List<Hero> ResolveSnapshotHeroes(JObject command)
        {
            HashSet<string> ids = new HashSet<string>(
                (command["heroIds"] as JArray
                    ?? new JArray()).Values<string>(),
                StringComparer.OrdinalIgnoreCase);
            foreach (string id in GauntletPresentHeroIds) ids.Add(id);
            if (Hero.MainHero != null) ids.Add(Hero.MainHero.StringId);
            return Hero.AllAliveHeroes
                .Where(hero => ids.Contains(hero.StringId))
                .OrderBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static JArray CaptureRelationInverse(JArray rows)
        {
            JArray inverse = new JArray();
            foreach (JObject row in (rows ?? new JArray()).OfType<JObject>())
            {
                Hero subject = FindGauntletHero(
                    row.Value<string>("subjectId"));
                Hero target = FindGauntletHero(
                    row.Value<string>("targetId"));
                if (subject == null || target == null) continue;
                inverse.Add(new JObject
                {
                    ["subjectId"] = subject.StringId,
                    ["targetId"] = target.StringId,
                    ["value"] = subject.GetRelation(target)
                });
            }
            return inverse;
        }

        private static void ApplyRelations(JArray rows)
        {
            foreach (JObject row in (rows ?? new JArray()).OfType<JObject>())
            {
                Hero subject = FindGauntletHero(
                    row.Value<string>("subjectId"));
                Hero target = FindGauntletHero(
                    row.Value<string>("targetId"));
                if (subject == null || target == null || subject == target)
                    continue;
                int targetValue = Math.Max(
                    -100,
                    Math.Min(100, row.Value<int?>("value") ?? 0));
                int delta = targetValue - subject.GetRelation(target);
                if (delta != 0)
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                        subject,
                        target,
                        delta,
                        false);
            }
        }

        private static Hero FindGauntletHero(string id)
        {
            return Hero.AllAliveHeroes.FirstOrDefault(hero =>
                string.Equals(
                    hero.StringId,
                    id,
                    StringComparison.OrdinalIgnoreCase));
        }
    }
}

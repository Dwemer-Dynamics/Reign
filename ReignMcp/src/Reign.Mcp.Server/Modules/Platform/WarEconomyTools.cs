using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ModelContextProtocol.Server;
using Reign.Core.Contracts.Economy;

namespace Reign.Mcp.Server;

public static partial class TestingTools
{
    [McpServerTool(Name = "reign_get_war_economy_test_manifest", ReadOnly = true,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Reads war-economy scenarios, coverage requirements and protected-save gates without launching or advancing Bannerlord.")]
    public static JsonElement GetWarEconomyTestManifest(ReignMcpOptions options)
        => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(options.WorkspaceRoot,
            "ReignServer/tests/ReignLiveTest/Features/WorldSimulation/war-economy-manifest.json")));

    [McpServerTool(Name = "reign_start_war_economy_test", ReadOnly = false,
        Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Npc_caravan_probe accepts exact NPC clan ID in partyId and a friendly town in settlementId, inland for caravans or a suitable port for War Sails convoys, temporarily bounds one NPC payer, then exercises the configured cash-share boundary, rollback, formation and direct native destruction. Household_stipend_probe accepts exact recipient clan in partyId and home in settlementId, temporarily bounds its two parties and verifies tenure, reserve and transfer. Both need full Current restore, make no providers calls and certify no actual combat. Raid_food_probe checks loaded food models and a bounded temporary fishing boat; raid_partial_setup accepts fixed native buffer cases0/4/6/10/24/25/40 with exact-path counters, resource deltas and mandatory Current restore. Neither certifies combat or natural fishing; independent provider-guard proof is required. Trace6 adds opening/closing/per-mutation wallet ownership including dead/disabled heroes; unknown entry cash blocks origin acceptance. Replacement_setup removes at most100 and half the healthy regulars in one exact free NPC lord party, preserving heroes, wounded, garrisons, cash, stocks and population; it is a persistent synthetic field-loss scenario, not a full battle, and requires Current restore after the branch. Cohort control accepts cohort_preview/cohort_enable/cohort_disable on an exact paused armed Current with retained recovery. Configure1-6300 native days, .25-12wall hours, explicit/auto/none peace kingdoms and locked war pairs, extra-war blocking, rebellion suppression and .25-7day sampling. Preview reports mismatches; arrange real relations through structured native actions first. Enable binds4war/peace hooks and optionally3rebellion hooks. Guards check each quarter day; latest256samples plus total/eviction counters are retained. Same activation replay never extends expiry. Disable and a new operation permit another segment without reload. No resources or flags are persisted; reload ends continuity. Ship funding probe runs17 fixed cases using5 synthetic hulls,2 auxiliary NPC parties,4 bounded seeded wallets, one temporary existing governor and2 exact-hero perk overrides. It checks native player/1%NPC prices, bought/captured resale, funded trades/bonuses and zero destroyed-hull cash. Exact paused disposable Current and retained checkpoint required; full restore mandatory; direct capture action is not full battle proof. Civilian crew probe uses one safe coastal NPC village, at most2000 temporarily removed/restored warehouse units, one-callback thread-local fishing chance/capacity overrides, and at most two native NPC parties (one fishing/one caravan). It exercises fishing creation/reinforcement and caravan factory/crew initialization, measures all village hearths unchanged, cleans up only exact created parties and requires full Current restore. It grants no hearths or player assets and does not test caravan funding. Readiness probe invokes one daily bounded least-experienced troop drawdown on an existing safe NPC peace party, verifies duplicate-callback conservation and fixed realm hearth return, then requires full Current restore. Raid setup sets one safe NPC village hit points to zero and invokes the real native Looted state action with an existing enemy NPC party. It leaves the scenario active, creates no battle or invented loot/hearth destruction, and requires exact Current restore after the branch. Runs a bounded economy observation, native recruitment, reversible production/relief/funding/civilian-food probe, funding trace control or save-reload assertion on the exact paused armed disposable Current. Never advances time, saves or invokes providers. Controlled probes explicitly arrange bounded native inputs and restore resources. Funding and civilian-food probes require Current reload before natural time/save; they do not establish political consent. Household probes create/destroy at most three NPC parties without adding cash, test duplicate initialization and reused string IDs, temporarily empty the actual payer for the final case, then require full Current restore. Civilian food uses six fixed baseline/priority pairs including negative native demand,12 native calls, at most450 grain and200 non-food units per case. Funding source market_purchase uses one existing NPC lord and caravan, five fixed cases each,10 native calls, at most10 market units and100000 payer cash per case, restores cash, stock, tariff pool and RNG, then requires exact Current reload. Funding tracing observes actual wallets and population for at most three native days/one real hour and8192 rows; missing/expired/dropped evidence cannot pass. Recruitment spends actual resources. Naval garrison recruitment uses two existing caravans, a temporary thread-local governor perk override and temporary empty payer; no persisted perks or funding are granted. It requires full Current restore before natural time/save.")]
    public static async Task<ApiEnvelope> StartWarEconomyTest(ReignApiClient api, ReignMcpOptions options,
        CampaignTestService campaignTests, string campaignId, string campaignTestRunId,
        [AllowedValues("snapshot", "npc_caravan_probe", "household_stipend_probe", "recruitment", "production_probe", "relief_probe", "funding_probe", "market_food_probe", "household_probe", "civilian_crew_probe", "ship_funding_probe", "raid_food_probe", "raid_partial_setup", "raid_setup", "readiness_probe", "replacement_setup", "funding_trace", "cohort_control", "save_prepare", "save_verify")] string profile = "snapshot",
        string fixtureRunId = "war_economy", string operationId = "snapshot", string partyId = "",
        string settlementId = "", [AllowedValues("", "ai_volunteer", "native_auto_garrison", "native_base_garrison", "native_naval_garrison", "lord_purse", "kingdom_support", "market_balance", "loot_sale", "horse_sale", "prisoner_sale", "gold_barter", "market_purchase", "trace_begin", "trace_observe", "trace_end", "cohort_preview", "cohort_enable", "cohort_disable")] string source = "",
        string expectedFeatureFingerprint = "", string previousGameInstanceId = "",
        [Description("Only raid_partial_setup: exactly0,4,6,10,24,25,40 percent of native raid buffer; resources change and Current restore is required.")][Range(0,40)] int damagePercent = 10,
        [Description("Exact text: start Reign war economy test on disposable save")] string confirmation = "",
        CancellationToken cancellationToken = default,
        [Description("Cohort only: native days from activation,1-6300; default126. Separate guarded advance supplies days or absolute targetDay and never saves unless requested.")] double cohortDurationDays = 126,
        [Description("Cohort wall-clock safety expiry,.25-12 hours.")] double cohortWallHours = 6,
        [Description("Retain a matrix sample every .25-7 native days; last256 retained, eviction disclosed. Independent guard checks remain every quarter day.")] double cohortSampleIntervalDays = 1,
        [Description("Cohort only: prevent new wars among captured NPC kingdoms beyond the opening matrix.")] bool cohortBlockOtherWars = true,
        [Description("Cohort only: suppress native and Reign rebellions. False leaves them native and reports resulting violations.")] bool cohortSuppressRebellions = true,
        [Description("Cohort only: auto captures currently peaceful NPC kingdoms; none selects no peace controls; otherwise comma-separated exact NPC kingdom IDs.")] string peaceKingdomIds = "auto",
        [Description("Cohort only: auto locks current NPC wars; none leaves wars free to end; otherwise comma-separated first:second exact kingdom pairs. Preview reports relations to arrange through native structured actions before enable.")] string warPairs = "auto")
    {
        if (!options.AllowVerificationControl) throw new InvalidOperationException("Verification control is disabled.");
        InputGuard.RequireConfirmation(confirmation, "start Reign war economy test on disposable save");
        campaignId = RequiredIdentifier(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredIdentifier(campaignTestRunId, nameof(campaignTestRunId));
        fixtureRunId = RequiredIdentifier(fixtureRunId, nameof(fixtureRunId));
        operationId = RequiredIdentifier(operationId, nameof(operationId));
        if (profile is not ("snapshot" or "npc_caravan_probe" or "household_stipend_probe" or "recruitment" or "production_probe" or "relief_probe" or "funding_probe" or "market_food_probe" or "household_probe" or "civilian_crew_probe" or "ship_funding_probe" or "raid_food_probe" or "raid_partial_setup" or "raid_setup" or "readiness_probe" or "replacement_setup" or "funding_trace" or "cohort_control" or "save_prepare" or "save_verify"))
            throw new ArgumentException("Unsupported economy profile.", nameof(profile));
        if (profile == "cohort_control" && source is not ("cohort_preview" or "cohort_enable" or "cohort_disable"))
            throw new ArgumentException("Cohort control requires preview, enable or disable.", nameof(source));
        if (profile != "cohort_control" || source != "cohort_disable")
            _ = new WarEconomyCohortPlan(cohortDurationDays, cohortWallHours, cohortSampleIntervalDays,
                cohortBlockOtherWars, cohortSuppressRebellions, peaceKingdomIds, warPairs);
        if (profile != "cohort_control" && (cohortDurationDays != 126 || cohortWallHours != 6 || cohortSampleIntervalDays != 1
            || !cohortBlockOtherWars || !cohortSuppressRebellions || peaceKingdomIds != "auto" || warPairs != "auto"))
            throw new ArgumentException("Cohort parameters apply only to cohort_control.");
        if (profile is "npc_caravan_probe" or "household_stipend_probe")
        {
            partyId = RequiredIdentifier(partyId, nameof(partyId));
            settlementId = RequiredIdentifier(settlementId, nameof(settlementId));
            if (!string.IsNullOrEmpty(source)) throw new ArgumentException("This fixture has no selectable source.", nameof(source));
        }
        if (profile == "recruitment")
        {
            partyId = RequiredIdentifier(partyId, nameof(partyId));
            settlementId = RequiredIdentifier(settlementId, nameof(settlementId));
            if (source is not ("ai_volunteer" or "native_auto_garrison" or "native_base_garrison" or "native_naval_garrison"))
                throw new ArgumentException("The requested source needs its own native acceptance route.", nameof(source));
        }
        if (profile is "production_probe" or "relief_probe" or "market_food_probe" or "household_probe" or "civilian_crew_probe" or "ship_funding_probe" or "raid_food_probe" or "raid_partial_setup" or "raid_setup")
            settlementId = RequiredIdentifier(settlementId, nameof(settlementId));
        if (profile is "raid_partial_setup" or "raid_setup" or "readiness_probe" or "ship_funding_probe" or "replacement_setup") partyId = RequiredIdentifier(partyId, nameof(partyId));
        if ((profile is "market_food_probe" or "household_probe" or "civilian_crew_probe" or "ship_funding_probe" or "raid_food_probe" or "raid_partial_setup" or "raid_setup" or "readiness_probe" or "replacement_setup") && !string.IsNullOrEmpty(source))
            throw new ArgumentException("This fixed fixture has no selectable funding source.", nameof(source));
        if (profile == "funding_probe")
        {
            if (source is not ("lord_purse" or "kingdom_support" or "market_balance" or "loot_sale" or "horse_sale" or "prisoner_sale" or "gold_barter" or "market_purchase"))
                throw new ArgumentException("Unsupported native funding probe.", nameof(source));
            partyId = RequiredIdentifier(partyId, nameof(partyId));
            settlementId = RequiredIdentifier(settlementId, nameof(settlementId));
        }
        if (profile == "funding_trace")
        {
            if (source is not ("trace_begin" or "trace_observe" or "trace_end"))
                throw new ArgumentException("Unsupported funding trace control.", nameof(source));
            if (source == "trace_begin") settlementId = RequiredIdentifier(settlementId, nameof(settlementId));
        }
        if (profile == "raid_partial_setup" && !new[] {0,4,6,10,24,25,40}.Contains(damagePercent))
            throw new ArgumentOutOfRangeException(nameof(damagePercent));
        expectedFeatureFingerprint = InputGuard.BoundedText(expectedFeatureFingerprint, nameof(expectedFeatureFingerprint), 64);
        previousGameInstanceId = InputGuard.BoundedText(previousGameInstanceId, nameof(previousGameInstanceId), 120);
        if (profile == "save_verify" && (expectedFeatureFingerprint.Length != 64 || previousGameInstanceId.Length == 0))
            throw new ArgumentException("save_verify requires the retained fingerprint and prior native instance.");
        var authorization = await campaignTests.RequireArmedOwnedPausedActionSaveAsync(campaignTestRunId,
            campaignId, cancellationToken).ConfigureAwait(false);
        if (profile is "recruitment" or "production_probe" or "relief_probe" or "funding_probe" or "market_food_probe" or "household_probe" or "civilian_crew_probe" or "ship_funding_probe" or "raid_food_probe" or "raid_partial_setup" or "raid_setup" or "readiness_probe" or "replacement_setup" or "funding_trace" or "cohort_control" or "npc_caravan_probe" or "household_stipend_probe") campaignTests.RequireRetainedRecoveryCheckpoint(authorization);
        return await api.PostAsync("/tests/live/run/start", new
        {
            schemaVersion = 2, campaignId, mode = "passive_world", presentation = "visible", effects = "full",
            label = "MCP war economy - " + profile, autoCompleteWhenIdle = true,
            steps = new[] { new { operation = "world_economy_test", profile, fixtureRunId, operationId, partyId,
                settlementId, source, damagePercent, expectedFeatureFingerprint, previousGameInstanceId, campaignTestRunId,
                cohortDurationDays, cohortWallHours, cohortSampleIntervalDays, cohortBlockOtherWars,
                cohortSuppressRebellions, peaceKingdomIds, warPairs,
                expectedSaveName = authorization.CurrentSaveName, timeoutSeconds = 180 } }
        }, cancellationToken).ConfigureAwait(false);
    }
}

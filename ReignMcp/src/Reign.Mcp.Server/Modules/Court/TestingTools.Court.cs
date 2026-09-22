using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

public static partial class TestingTools
{
    [McpServerTool(Name = "reign_get_ruler_docket_test_manifest", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the launch-ready ruler-docket, petition, expedition, Chancellor, persistence, dialogue, art, and guarded natural-play acceptance plan without mutating a campaign.")]
    public static IReadOnlyDictionary<string, object> GetRulerDocketTestManifest()
    {
        return new Dictionary<string, object>
        {
            ["schemaVersion"] = 13,
            ["courtLifeExistingMatter"] = "court_life_preflight returns bounded pendingMatterInventory filtered by source/template. Optional courtLifeExistingMatterId on International or Patronage court_life_prepare binds that exact pending matter without generation, history refresh or native changes. Reject identity/scope mismatch, committed or in-flight effects, existing markers and fixture mutations. Preserve the conversation; continue without replay.",
            ["courtLifeEvidenceSchema"] = "reign-court-life-evidence-v1",
            ["courtLifeCaptiveFixture"] = "Optional courtLifeFixtureCaptiveHeroId from read-only captiveInventory with exclusionCounts and existingPlayerHeldCaptives; International court_life_prepare and RequiresCaptive template only. Exact armed Current and fixture confirmation; checkpoint before setup. Native capture preserves production eligibility, persists intent and before/after custody, and blocks blind partial retries. Restore the guarded pre-fixture checkpoint on partial failure. International native evidence includes exact captive, foreign ruler gold and kingdom-pair war status (reign-court-life-custody-v1).",
            ["courtLifeFamilyTransitionContract"] = "Family preparation persists the exact scoped visit/member baseline. Dismissed requires that exact server visit to become dismissed and its member counter to rise by one; neglected additionally requires a new patience-threshold crossing from zero to one. A successful neglected observation retains familyNeglectEvidence for the same visit. Reconciled requires that prior proof and a later zero/zero member state; an initially clear member cannot pass. Snapshots remain observational and create no transition proof. court_life_snapshot requires the exact visible fixture matter and disposable-save gate, but may capture a resolved or busy audience without AutomationCanDecide or completed provider replies. It proves neither a completed turn nor decision authority; converse and choose-confirm retain their full readiness gates.",
            ["preparedOnly"] = true,
            ["profiles"] = new[] { "preflight", "petition_case", "petition_insufficient", "petition_invalid_identity", "no_expiry_prepare", "no_expiry_verify", "expedition_return_prepare", "expedition_return_verify", "chancellor_preflight", "chancellor_case", "chancellor_schedule_prepare", "chancellor_schedule_verify", "chancellor_eligibility_dismissal", "emergency_lifecycle", "legacy_migration", "natural_soak_prepare", "natural_soak_verify", "save_prepare", "save_verify", "noble_preflight", "noble_template_case", "noble_investigation_prepare", "noble_investigation_verify", "noble_reversal", "noble_execution", "noble_court_stay_prepare", "noble_court_stay_verify", "noble_save_prepare", "noble_save_verify", "royal_proclamation", "court_life_preflight", "court_life_family_fixture", "court_life_prepare", "court_life_open", "court_life_converse", "court_life_choose_confirm", "court_life_snapshot", "court_life_observe", "court_life_clock_observe", "court_life_save_prepare", "court_life_save_verify", "cleanup_marker" },
            ["petitionMatrix"] = new[] { "Food x Minor/Serious/Severe", "TownGold x Minor/Serious/Severe", "VillageGold x Minor/Serious/Severe", "Soldiers x Minor/Serious/Severe" },
            ["nobleTemplateMatrix"] = new[] { "20 Petty", "20 Serious", "18 Grave", "10 Exceptional", "68 total; exceptional-murder is destructive and must run from a restored Current", "every unique fixtureRunId deterministically chooses among the distinct participant sets produced across bounded production day/slot variants and reports the chosen variant count plus hero, clan, culture, gender, age, occupation, marital state, clan-leader state, and role for aggregate diversity auditing" },
            ["nobleDecisionBranches"] = new[] { "side-a", "side-b", "defer", "convict-correct", "convict-wrong", "acquit-all", "living reversal", "protected-custody execution", "overnight Court stay release" },
            ["providerTiming"] = new { secondsPerExpectedSpeaker = 120, minimumWindowSeconds = 120, maximumWindowSeconds = 480, liveCommandTimeoutSeconds = 1800 },
            ["decisionBranches"] = new[] { "grant-direct", "fund-instead for Food and Soldiers", "refuse", "insufficient direct resource remains pending", "invalidated identity permits technical return only" },
            ["naturalPlay"] = new[] { "create the exact native settlement need or noble eligibility on the armed disposable Current", "derive the petitioner, noble participants, hidden truth, evidence, and immutable terms through production candidate/snapshot logic", "open the visible production Court Petition movie", "type one plausible ruler question through the production conversation control and record every persisted player and NPC line verbatim in structured evidence", "reject the case if any NPC-visible line contains percentages, tiers, scores, relationship values, penalties, or game mechanics", "invoke the same bound ViewModel command as the player control only after conversation", "observe production family, death, custody, reputation, relation, commitment, expedition, history, memory, and world-history state", "advance time only through the guarded campaign-test controller", "checkpoint and restart only through the guarded Current" },
            ["launchCoverage"] = new[] { "daily 1d5 and one-per-town selection", "truthful need and no healthy-state request", "no expiry across day boundaries", "food/gold/soldier costs", "random healthy soldier manifest, reserve, casualty, return, and fallback destination", "temporary hearth/prosperity/output/security effects", "directional relations", "participant-diverse noble cases across heroes, clans, cultures, genders, ages, occupations, marital states, clan leaders, roles, and both ruling sides", "Chancellor salary, unpaid inactivity, suppression, scheduling, capture, emergency succession, handoff, world history, and full private memory", "legacy migration", "provider-backed opening, typed conversation, and closing petitioner reactions with no dialogue-side mechanics", "first-person petition art with integrated transparent portrait apertures", "independent deduplicated cards with finite clamped scrolling and no scroll at four or fewer", "save/reload", "modern UI and attached Chancellor rail" },
            ["promptOverridePolicy"] = "A one-request override may be used only after ordinary provider behavior is sampled and only to prove petitioner reaction or explicit Chancellor acceptance language. It cannot create, decide, grant, refuse, pay, suppress, dispatch, kill, return, remember, or otherwise satisfy a mechanical assertion.",
            ["safety"] = new[] { "all mutations require the exact armed campaign-test Current", "the client independently compares the active native save", "the preserved baseline is never modified", "marriage, divorce, murder, execution, and relationship eligibility fixtures require restored disposable branches", "fixture situations require exact confirmation and are undone by baseline rollback", "the feature harness never advances time or saves" }
        };
    }

    [McpServerTool(Name = "reign_start_ruler_docket_test", ReadOnly = false, Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Starts one guarded ruler-docket native acceptance profile. Petition and noble profiles create production eligibility, open the real audience, exchange natural-language turns, use bound player controls, and observe authoritative native effects.")]
    public static async Task<ApiEnvelope> StartRulerDocketTest(
        ReignApiClient api, ReignMcpOptions options, CampaignTestService campaignTests,
        string campaignId, string campaignTestRunId,
        [AllowedValues("preflight", "petition_case", "petition_insufficient", "petition_invalid_identity", "no_expiry_prepare", "no_expiry_verify", "expedition_return_prepare", "expedition_return_verify", "chancellor_preflight", "chancellor_case", "chancellor_schedule_prepare", "chancellor_schedule_verify", "chancellor_eligibility_dismissal", "emergency_lifecycle", "legacy_migration", "natural_soak_prepare", "natural_soak_verify", "save_prepare", "save_verify", "noble_preflight", "noble_template_case", "noble_investigation_prepare", "noble_investigation_verify", "noble_reversal", "noble_execution", "noble_court_stay_prepare", "noble_court_stay_verify", "noble_save_prepare", "noble_save_verify", "royal_proclamation", "court_life_preflight", "court_life_family_fixture", "court_life_prepare", "court_life_open", "court_life_converse", "court_life_choose_confirm", "court_life_snapshot", "court_life_observe", "court_life_clock_observe", "court_life_save_prepare", "court_life_save_verify", "cleanup_marker")] string profile = "preflight",
        [AllowedValues("Food", "TownGold", "VillageGold", "Soldiers")] string kind = "Food",
        [AllowedValues("Minor", "Serious", "Severe")] string severity = "Minor",
        [AllowedValues("grant-direct", "fund-instead", "refuse")] string action = "grant-direct",
        [AllowedValues("inactive-zero-pay", "active-paid", "unpaid-inactive", "suppression")] string chancellorScenario = "inactive-zero-pay",
        [Range(0, int.MaxValue)] int chancellorSalary = 500,
        bool forceExpeditionFallback = true,
        [Range(7, 180)] int naturalSoakDays = 30,
        string templateId = "petty-seat-precedence",
        [AllowedValues("side-a", "side-b", "defer", "convict-correct", "convict-wrong", "acquit-all")] string nobleAction = "side-a",
        string proclamationText = "By our hand, this exact test proclamation enters the realm's public record.",
        string rulerQuestion = "Before I rule, each of you state the strongest fact supporting your position and answer the other side's central claim.",
        [Range(-1, 3)] int expectedAcceptanceTier = -1,
        [AllowedValues("any", "correct", "wrong", "insufficient")] string expectedInvestigationOutcome = "any",
        string fixtureRunId = "ruler_docket_launch",
        [Description("Exact text required: start Reign ruler docket test on disposable save")] string confirmation = "",
        [AllowedValues("International", "Family", "Patronage", "Visitor")] string courtLifeSource = "Patronage",
        string courtLifeTemplateId = "",
        string courtLifeHeroId = "",
        string courtLifeOptionId = "",
        [AllowedValues("snapshot", "pending", "resolved", "delivery_complete", "arrived", "departed", "dismissed", "neglected", "reconciled")] string courtLifeExpectation = "snapshot",
        [Range(-1, 100)] int courtLifeExpectedTurns = -1,
        [Range(-1, int.MaxValue)] int courtLifeFixtureGold = -1,
        [Range(-1, 100)] int courtLifeFixtureLoyalty = -1,
        [Description("Family fixture only. Explicitly authorized disposable-baseline setup may age an adult ruler to 34; default preserves age. Never use without user authority to adjust the test ruler.")] bool courtLifeFixtureAgeRulerTo34 = false,
        [Description("Optional exact hero ID from court_life_preflight captiveInventory. Only International court_life_prepare with a captive template: take that eligible adult foreign noble prisoner on the armed disposable Current. Requires a pre-fixture checkpoint; partial failures require guarded checkpoint rollback before retry. Empty preserves custody.")] string courtLifeFixtureCaptiveHeroId = "",
        [Description("Optional exact pending matter ID from court_life_preflight pendingMatterInventory. International or Patronage court_life_prepare only: bind the existing encounter without generation or fixture mutations; preserve and continue its conversation.")] string courtLifeExistingMatterId = "",
        CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl) throw new InvalidOperationException("Verification control is disabled. Set REIGN_MCP_ALLOW_VERIFICATION_CONTROL=true in the trusted MCP configuration.");
        InputGuard.RequireConfirmation(confirmation, "start Reign ruler docket test on disposable save");
        campaignId = RequiredIdentifier(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredIdentifier(campaignTestRunId, nameof(campaignTestRunId));
        fixtureRunId = RequiredIdentifier(fixtureRunId, nameof(fixtureRunId));
        profile = profile.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(courtLifeExistingMatterId))
        {
            courtLifeExistingMatterId = RequiredIdentifier(courtLifeExistingMatterId, nameof(courtLifeExistingMatterId));
            if (profile != "court_life_prepare" || (courtLifeSource != "International" && courtLifeSource != "Patronage"))
                throw new ArgumentException("Existing matter binding requires International or Patronage court_life_prepare.");
            if (courtLifeFixtureGold >= 0 || courtLifeFixtureLoyalty >= 0 || courtLifeFixtureAgeRulerTo34
                || !string.IsNullOrEmpty(courtLifeFixtureCaptiveHeroId) || !string.IsNullOrEmpty(courtLifeHeroId))
                throw new ArgumentException("Existing matter binding cannot be combined with fixture mutations or participant selection.");
        }
        if (!string.IsNullOrEmpty(courtLifeFixtureCaptiveHeroId))
        {
            courtLifeFixtureCaptiveHeroId = RequiredIdentifier(courtLifeFixtureCaptiveHeroId, nameof(courtLifeFixtureCaptiveHeroId));
            if (profile != "court_life_prepare" || courtLifeSource != "International")
                throw new ArgumentException("Captive setup requires International court_life_prepare.");
        }
        CampaignTestAuthorization authorization = await campaignTests.RequireArmedOwnedSaveAsync(
            campaignTestRunId, campaignId, cancellationToken).ConfigureAwait(false);
        Dictionary<string, object> Phase(string phase, bool mutating = false) => new()
        {
            ["operation"] = "ruler_docket_test",
            ["phase"] = phase,
            ["fixtureRunId"] = fixtureRunId,
            ["kind"] = kind,
            ["severity"] = severity,
            ["action"] = action,
            ["scenario"] = chancellorScenario,
            ["salary"] = chancellorSalary,
            ["forceFallback"] = forceExpeditionFallback,
            ["soakDays"] = naturalSoakDays,
            ["templateId"] = templateId,
            ["nobleAction"] = nobleAction,
            ["proclamationText"] = proclamationText,
            ["rulerQuestion"] = rulerQuestion,
            ["expectedAcceptanceTier"] = expectedAcceptanceTier,
            ["expectedInvestigationOutcome"] = expectedInvestigationOutcome,
            ["courtLifeSource"] = courtLifeSource,
            ["courtLifeTemplateId"] = courtLifeTemplateId,
            ["courtLifeExistingMatterId"] = courtLifeExistingMatterId,
            ["courtLifeHeroId"] = courtLifeHeroId,
            ["courtLifeOptionId"] = courtLifeOptionId,
            ["courtLifeExpectation"] = courtLifeExpectation,
            ["courtLifeExpectedTurns"] = courtLifeExpectedTurns,
            ["courtLifeFixtureGold"] = courtLifeFixtureGold,
            ["courtLifeFixtureLoyalty"] = courtLifeFixtureLoyalty,
            ["courtLifeFixtureAgeRulerTo34"] = courtLifeFixtureAgeRulerTo34,
            ["courtLifeFixtureCaptiveHeroId"] = courtLifeFixtureCaptiveHeroId,
            ["expectedSaveName"] = authorization.CurrentSaveName,
            ["campaignTestRunId"] = campaignTestRunId,
            ["confirmation"] = mutating ? "prepare Reign ruler docket fixture on disposable save" : string.Empty,
            ["timeoutSeconds"] = 1800
        };
        Dictionary<string, object> NoblePhase(string phase, bool mutating = false)
        {
            Dictionary<string, object> value = Phase(phase, mutating);
            value["action"] = nobleAction;
            return value;
        }
        var steps = new List<Dictionary<string, object>>();
        Dictionary<string, object> OpenCourt() => new()
        {
            ["operation"] = "ui_open",
            ["targetSearch"] = "court",
            ["courtLifeSource"] = courtLifeSource,
            ["courtLifeTemplateId"] = courtLifeTemplateId,
            ["courtLifeHeroId"] = courtLifeHeroId,
            ["courtLifeOptionId"] = courtLifeOptionId,
            ["courtLifeExpectation"] = courtLifeExpectation,
            ["courtLifeExpectedTurns"] = courtLifeExpectedTurns,
            ["courtLifeFixtureGold"] = courtLifeFixtureGold,
            ["courtLifeFixtureLoyalty"] = courtLifeFixtureLoyalty,
            ["expectedSaveName"] = authorization.CurrentSaveName,
            ["campaignTestRunId"] = campaignTestRunId,
            ["timeoutSeconds"] = 60
        };
        if (profile is "preflight" or "petition_case" or "petition_insufficient" or
            "petition_invalid_identity" or "no_expiry_prepare" or "chancellor_preflight" or
            "chancellor_case" or "chancellor_schedule_prepare" or
            "chancellor_eligibility_dismissal" or "emergency_lifecycle" or
            "noble_preflight" or "noble_template_case" or "noble_investigation_prepare" or
            "noble_reversal" or "noble_execution" or "noble_court_stay_prepare" or
            "noble_save_prepare" or "royal_proclamation")
            steps.Add(OpenCourt());
        switch (profile)
        {
            case "court_life_family_fixture":
                steps.Add(OpenCourt()); steps.Add(Phase(profile, true)); break;
            case "court_life_preflight": case "court_life_prepare": case "court_life_open":
                steps.Add(OpenCourt()); steps.Add(Phase(profile, profile == "court_life_prepare")); break;
            case "court_life_converse": case "court_life_choose_confirm": case "court_life_snapshot":
            case "court_life_observe": case "court_life_clock_observe": case "court_life_save_prepare": case "court_life_save_verify":
                steps.Add(Phase(profile)); break;
            case "preflight": steps.Add(Phase("preflight")); break;
            case "petition_case":
                steps.Add(Phase("preflight")); steps.Add(Phase("prepare", true));
                steps.Add(Phase("open")); steps.Add(Phase("snapshot"));
                steps.Add(Phase("decide")); steps.Add(Phase("observe"));
                break;
            case "petition_insufficient":
                steps.Add(Phase("preflight")); steps.Add(Phase("prepare", true));
                steps.Add(Phase("open")); steps.Add(Phase("guardrail_insufficient"));
                break;
            case "petition_invalid_identity":
                steps.Add(Phase("preflight")); steps.Add(Phase("prepare", true));
                steps.Add(Phase("invalidate_identity")); steps.Add(Phase("open"));
                steps.Add(Phase("technical_return"));
                break;
            case "no_expiry_prepare":
                steps.Add(Phase("preflight")); steps.Add(Phase("prepare", true));
                break;
            case "no_expiry_verify": steps.Add(Phase("no_expiry")); break;
            case "expedition_return_prepare": steps.Add(Phase("expedition_prepare", true)); break;
            case "expedition_return_verify": steps.Add(Phase("expedition_verify")); break;
            case "chancellor_preflight": steps.Add(Phase("chancellor_preflight")); break;
            case "chancellor_case":
                steps.Add(Phase("chancellor_preflight"));
                steps.Add(Phase("chancellor_prepare", true));
                steps.Add(Phase("chancellor_exercise"));
                break;
            case "chancellor_schedule_prepare":
                steps.Add(Phase("chancellor_preflight"));
                steps.Add(Phase("chancellor_prepare", true));
                steps.Add(Phase("chancellor_schedule_prepare"));
                break;
            case "chancellor_schedule_verify": steps.Add(Phase("chancellor_schedule_verify")); break;
            case "chancellor_eligibility_dismissal":
                steps.Add(Phase("chancellor_preflight"));
                steps.Add(Phase("chancellor_eligibility_dismissal", true));
                break;
            case "emergency_lifecycle":
                steps.Add(Phase("chancellor_preflight"));
                steps.Add(Phase("emergency_prepare", true));
                steps.Add(Phase("emergency_release"));
                steps.Add(Phase("emergency_complete"));
                steps.Add(Phase("emergency_observe"));
                break;
            case "legacy_migration": steps.Add(Phase("legacy_migration", true)); break;
            case "natural_soak_prepare": steps.Add(Phase("natural_soak_prepare", true)); break;
            case "natural_soak_verify": steps.Add(Phase("natural_soak_verify")); break;
            case "save_prepare": steps.Add(Phase("save_prepare")); break;
            case "save_verify": steps.Add(Phase("save_verify")); break;
            case "noble_preflight": steps.Add(NoblePhase("noble_preflight")); break;
            case "noble_template_case":
                steps.Add(NoblePhase("noble_preflight"));
                steps.Add(NoblePhase("noble_prepare", true));
                steps.Add(NoblePhase("noble_open"));
                steps.Add(NoblePhase("noble_snapshot"));
                steps.Add(NoblePhase("noble_decide"));
                steps.Add(NoblePhase("noble_observe"));
                break;
            case "noble_investigation_prepare":
                steps.Add(Phase("chancellor_preflight"));
                steps.Add(Phase("chancellor_prepare", true));
                steps.Add(NoblePhase("noble_preflight"));
                Dictionary<string, object> investigationMatter = NoblePhase("noble_prepare", true);
                investigationMatter["templateId"] = "exceptional-murder";
                investigationMatter["action"] = "defer";
                steps.Add(investigationMatter);
                steps.Add(NoblePhase("noble_open"));
                Dictionary<string, object> defer = NoblePhase("noble_decide");
                defer["action"] = "defer";
                steps.Add(defer);
                steps.Add(NoblePhase("noble_observe"));
                break;
            case "noble_investigation_verify":
                steps.Add(NoblePhase("noble_investigation_verify"));
                break;
            case "noble_reversal":
                steps.Add(NoblePhase("noble_preflight"));
                steps.Add(NoblePhase("noble_prepare", true));
                steps.Add(NoblePhase("noble_open"));
                steps.Add(NoblePhase("noble_decide"));
                steps.Add(NoblePhase("noble_observe"));
                steps.Add(NoblePhase("noble_reverse"));
                break;
            case "noble_execution":
                steps.Add(NoblePhase("noble_preflight"));
                Dictionary<string, object> murder = NoblePhase("noble_prepare", true);
                murder["templateId"] = "exceptional-murder";
                murder["action"] = "convict-correct";
                steps.Add(murder);
                steps.Add(NoblePhase("noble_open"));
                Dictionary<string, object> convict = NoblePhase("noble_decide");
                convict["action"] = "convict-correct";
                steps.Add(convict);
                steps.Add(NoblePhase("noble_observe"));
                steps.Add(NoblePhase("noble_execute"));
                break;
            case "noble_court_stay_prepare":
                steps.Add(NoblePhase("noble_preflight"));
                steps.Add(NoblePhase("noble_prepare", true));
                steps.Add(NoblePhase("noble_open"));
                steps.Add(NoblePhase("noble_decide"));
                steps.Add(NoblePhase("noble_observe"));
                steps.Add(NoblePhase("noble_stay_prepare"));
                break;
            case "noble_court_stay_verify":
                steps.Add(NoblePhase("noble_stay_verify"));
                break;
            case "noble_save_prepare":
                steps.Add(NoblePhase("noble_preflight"));
                steps.Add(NoblePhase("noble_prepare", true));
                steps.Add(NoblePhase("noble_save_prepare"));
                break;
            case "noble_save_verify":
                steps.Add(NoblePhase("noble_save_verify"));
                break;
            case "royal_proclamation":
                steps.Add(NoblePhase("royal_proclamation", true));
                break;
            case "cleanup_marker": steps.Add(Phase("cleanup_marker")); break;
            default: throw new ArgumentException("Unsupported ruler-docket profile.", nameof(profile));
        }
        return await api.PostAsync("/tests/live/run/start", new
        {
            schemaVersion = 1, campaignId, mode = "spymaster",
            label = "MCP ruler docket " + profile, presentation = "visible",
            effects = "full", autoCompleteWhenIdle = true, steps
        }, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "reign_get_royal_council_test_manifest", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the prepared Royal Council deterministic, UI, provider-backed, persistence, and capital-relocation acceptance profiles without starting Bannerlord commands.")]
    public static IReadOnlyDictionary<string, object> GetRoyalCouncilTestManifest()
    {
        return new Dictionary<string, object>
        {
            ["schemaVersion"] = 1,
            ["preparedOnly"] = true,
            ["profiles"] = new[] { "preflight", "deterministic", "ui", "opening_briefings", "routing", "save_prepare", "save_verify", "capital_relocation", "cleanup_marker" },
            ["requires"] = new[] { "visible Reign server", "armed live-test bridge", "exact disposable ruler campaign", "capital-scoped Court session", "four distinct staffed advisors for provider-backed profiles", "aligned Save Sync" },
            ["productionPaths"] = new[] { "office migration and legacy aliases", "four-seat uniqueness and vacancy navigation", "bounded role packets", "deterministic zero-call routing", "advisory-only response boundary", "private attributed record", "resident-advisor movement restrictions", "Royal Council/Ambassador/Economic Report Gauntlet UI", "save/reload and capital relocation" },
            ["safety"] = new[] { "all mutating profiles require the exact disposable-save confirmation", "irrelevant prompts must make zero provider calls", "Royal Council responses expose no native actions or relationship/reputation mutations", "cleanup is run-owned" }
        };
    }

    [McpServerTool(Name = "reign_start_royal_council_test", ReadOnly = false, Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Starts one prepared Royal Council acceptance profile through the visible armed bridge. Provider-backed, save, and relocation profiles must run only on a verified disposable campaign.")]
    public static async Task<ApiEnvelope> StartRoyalCouncilTest(
        ReignApiClient api, ReignMcpOptions options, CampaignTestService campaignTests,
        string campaignId, string campaignTestRunId,
        [AllowedValues("preflight", "deterministic", "ui", "opening_briefings", "routing", "save_prepare", "save_verify", "capital_relocation", "cleanup_marker")] string profile = "preflight",
        string fixtureRunId = "royal_council_launch",
        [Description("Exact text required: start Reign Royal Council test on disposable save")] string confirmation = "",
        CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl) throw new InvalidOperationException("Verification control is disabled. Set REIGN_MCP_ALLOW_VERIFICATION_CONTROL=true in the trusted MCP configuration.");
        InputGuard.RequireConfirmation(confirmation, "start Reign Royal Council test on disposable save");
        campaignId = RequiredIdentifier(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredIdentifier(campaignTestRunId, nameof(campaignTestRunId));
        fixtureRunId = RequiredIdentifier(fixtureRunId, nameof(fixtureRunId));
        profile = profile.Trim().ToLowerInvariant();
        if (profile is not ("preflight" or "deterministic" or "ui" or "opening_briefings" or "routing" or "save_prepare" or "save_verify" or "capital_relocation" or "cleanup_marker")) throw new ArgumentException("Unsupported Royal Council profile.", nameof(profile));
        CampaignTestAuthorization authorization = await campaignTests.RequireArmedOwnedSaveAsync(
            campaignTestRunId, campaignId, cancellationToken).ConfigureAwait(false);
        var steps = new List<object>();
        object Ui(string operation, string text = "", string value = "") => new { operation, targetSearch = "royal-council", text, value, expectedSaveName = authorization.CurrentSaveName, campaignTestRunId, timeoutSeconds = 300 };
        if (profile is "preflight" or "deterministic")
        {
            steps.Add(Ui("ui_open")); steps.Add(Ui("ui_snapshot")); steps.Add(new { operation = "ui_close", timeoutSeconds = 60 });
        }
        else if (profile == "ui")
        {
            steps.Add(Ui("ui_open")); steps.Add(Ui("ui_snapshot")); steps.Add(new { operation = "ui_back", timeoutSeconds = 60 }); steps.Add(new { operation = "ui_close", timeoutSeconds = 60 });
        }
        else if (profile == "opening_briefings")
        {
            steps.Add(Ui("ui_open")); steps.Add(Ui("ui_action", "open-briefings")); steps.Add(Ui("ui_snapshot")); steps.Add(new { operation = "ui_close", timeoutSeconds = 60 });
        }
        else if (profile == "routing")
        {
            steps.Add(Ui("ui_open"));
            steps.Add(Ui("ui_action", "send", "War Councilor, report hostile forces and recent conflicts."));
            steps.Add(Ui("ui_action", "send", "Spymaster and Foreign Advisor, report agents, pressure, and ruler relations."));
            steps.Add(Ui("ui_action", "send", "What color is the moon?"));
            steps.Add(Ui("ui_action", "send", "Expand on the foreign pressure."));
            steps.Add(Ui("ui_snapshot")); steps.Add(new { operation = "ui_close", timeoutSeconds = 60 });
        }
        else
        {
            steps.Add(new { operation = "royal_council_test", phase = profile, fixtureRunId, expectedSaveName = authorization.CurrentSaveName, campaignTestRunId,
                confirmation = profile == "capital_relocation" ? "exercise Reign Royal Council relocation on disposable save" : string.Empty, timeoutSeconds = 300 });
        }
        return await api.PostAsync("/tests/live/run/start", new { schemaVersion = 1, campaignId, mode = "spymaster", label = "MCP Royal Council " + profile, presentation = "visible", effects = "full", autoCompleteWhenIdle = true, steps }, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "reign_get_capital_ambassador_test_manifest", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the prepared capital and resident-foreign-ambassador acceptance profiles, isolation rules, and branching requirements without starting Bannerlord commands.")]
    public static IReadOnlyDictionary<string, object> GetCapitalAmbassadorTestManifest()
    {
        return new Dictionary<string, object>
        {
            ["schemaVersion"] = 2,
            ["preparedOnly"] = true,
            ["profiles"] = new[] { "preflight", "prepare_ownership", "deterministic", "server_matrix", "prepare_travel", "prepare_resident", "observe", "lifecycle_a", "move_capital", "simulate_loss", "simulate_war", "lifecycle_b_transition", "save_prepare", "save_verify", "ui", "official_dialogue", "official_dialogue_corpus", "cleanup_marker" },
            ["requires"] = new[] { "visible Reign server", "armed live-test bridge", "loaded disposable ruler campaign", "player inside a safe owned town", "at least two safe owned towns", "one safe owned castle", "one peaceful foreign kingdom with an eligible 200-Charm adult lord or lady", "aligned Save Sync" },
            ["productionPaths"] = new[] { "capital designation, relocation, native capture, and recovery", "capital versus local Court scope", "foreign ruler eligibility and deterministic envoy selection", "one-to-three-day travel and partyless Keep residency", "capital-move migration, shelter, war recall, and removal", "authority charter and immutable official context", "commercial grants", "referrals and counteroffers", "official-turn archive and successor inheritance", "tone pressure cap", "Ambassador Gauntlet UI", "eight-exchange natural ambassador_official corpus plus one bounded charter-permitted grant follow-up when needed", "resident and recalled native save/load" },
            ["branching"] = new[] { "prepare_resident may retire exactly one deterministic pre-existing posting through the server and native return-home path when every otherwise eligible origin is occupied on the disposable baseline", "prepare_resident, checkpoint, restart, and save_verify for the resident boundary", "restore that Current before lifecycle_a", "restore that Current before the lifecycle_b dialogue and transition branch", "checkpoint, restart, and save_verify after lifecycle_b_transition", "legacy move/loss/war profiles remain diagnostic-only" },
            ["passOnce"] = new[] { "deterministic", "server_matrix" },
            ["naturalLanguageCorpus"] = "Eight base natural player utterances plus their NPC replies cover request, refusal, ambiguity, clarification, referral, and counteroffer through visible production ambassador_official dialogue. When the deterministic charter denies the base corpus's requested commercial action, one bounded ninth natural exchange must target an actually granted charter permission and prove the grant without exceeding eighteen total turns.",
            ["failureClasses"] = "The server matrix proves one representative provider timeout, interrupted transport write, and retry-exhaustion contract.",
            ["safety"] = new[] { "all mutating profiles require the exact disposable-save confirmation", "never run on the preserved user campaign", "pre-existing posting replacement is bounded to one deterministic active posting and requires server completion plus native return-home evidence", "synthetic server matrix uses a namespaced campaign and deletes its database/files in finally", "fixture hooks are unreachable unless the live-test bridge is explicitly armed", "cleanup_marker removes only the in-save marker; restore the baseline to undo campaign/server mutations and then delete only exact test saves" }
        };
    }

    [McpServerTool(Name = "reign_start_capital_ambassador_test", ReadOnly = false, Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Starts one prepared capital/ambassador live-game acceptance phase through the armed bridge. Mutating profiles designate or move a capital, establish or relocate an envoy, write official archives, or simulate capture/war callbacks, so run only on a verified disposable baseline.")]
    public static Task<ApiEnvelope> StartCapitalAmbassadorTest(
        ReignApiClient api,
        ReignMcpOptions options,
        string campaignId,
        [AllowedValues("preflight", "prepare_ownership", "deterministic", "server_matrix", "prepare_travel", "prepare_resident", "observe", "lifecycle_a", "move_capital", "simulate_loss", "simulate_war", "lifecycle_b_transition", "save_prepare", "save_verify", "ui", "official_dialogue", "official_dialogue_corpus", "cleanup_marker")]
        string profile = "preflight",
        string fixtureRunId = "capital_ambassador_launch",
        [Description("Exact text required: start Reign capital ambassador test on disposable save")] string confirmation = "",
        CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl)
            throw new InvalidOperationException("Verification control is disabled. Set REIGN_MCP_ALLOW_VERIFICATION_CONTROL=true in the trusted MCP configuration.");
        InputGuard.RequireConfirmation(confirmation, "start Reign capital ambassador test on disposable save");
        campaignId = RequiredIdentifier(campaignId, nameof(campaignId));
        fixtureRunId = RequiredIdentifier(fixtureRunId, nameof(fixtureRunId));
        profile = profile.Trim().ToLowerInvariant();
        if (profile is not ("preflight" or "prepare_ownership" or "deterministic" or "server_matrix" or "prepare_travel" or "prepare_resident" or "observe" or "lifecycle_a" or "move_capital" or "simulate_loss" or "simulate_war" or "lifecycle_b_transition" or "save_prepare" or "save_verify" or "ui" or "official_dialogue" or "official_dialogue_corpus" or "cleanup_marker"))
            throw new ArgumentException("Unsupported capital/ambassador profile.", nameof(profile));

        var steps = new List<object>();
        object Phase(string phase) => new
        {
            operation = "capital_ambassador_test", phase, fixtureRunId, timeoutSeconds = phase == "server_matrix" ? 600 : 180
        };
        switch (profile)
        {
            case "preflight": steps.Add(Phase("preflight")); break;
            case "prepare_ownership":
                steps.Add(new { operation = "capital_ambassador_test", phase = "prepare_ownership", fixtureRunId, confirmation = "grant Reign capital fixture town on disposable save", timeoutSeconds = 180 });
                break;
            case "deterministic": steps.Add(Phase("deterministic")); break;
            case "server_matrix": steps.Add(Phase("server_matrix")); break;
            case "prepare_travel":
                steps.Add(new { operation = "capital_ambassador_test", phase = "prepare", fixtureRunId, forceResident = false, confirmation = "prepare Reign capital ambassador disposable fixture", timeoutSeconds = 300 });
                break;
            case "prepare_resident":
                steps.Add(new { operation = "capital_ambassador_test", phase = "prepare", fixtureRunId, forceResident = true, confirmation = "prepare Reign capital ambassador disposable fixture", timeoutSeconds = 300 });
                break;
            case "observe": steps.Add(Phase("observe")); break;
            case "lifecycle_a": steps.Add(Phase("lifecycle_a")); break;
            case "move_capital": steps.Add(Phase("move_capital")); break;
            case "simulate_loss": steps.Add(Phase("simulate_loss")); break;
            case "simulate_war": steps.Add(Phase("simulate_war")); break;
            case "lifecycle_b_transition": steps.Add(Phase("lifecycle_b_transition")); break;
            case "save_prepare":
                steps.Add(Phase("mark_reload"));
                break;
            case "save_verify": steps.Add(Phase("verify_reload")); break;
            case "ui":
                steps.Add(new { operation = "ui_open", targetSearch = "ambassador", timeoutSeconds = 60 });
                steps.Add(new { operation = "ui_snapshot", targetSearch = "ambassador", timeoutSeconds = 60 });
                steps.Add(new { operation = "ui_back", timeoutSeconds = 30 });
                steps.Add(new { operation = "ui_close", timeoutSeconds = 30 });
                break;
            case "official_dialogue":
                steps.Add(new { operation = "open", mode = "ambassador_official", presentation = "visible", timeoutSeconds = 90 });
                steps.Add(new { operation = "send", text = "State your represented ruler, your immutable authority charter, and every permission you may grant directly.", timeoutSeconds = 180 });
                steps.Add(new { operation = "send", text = "I request a military alliance. If you cannot grant it, refer the exact terms to your ruler and explain the expected delay.", timeoutSeconds = 180 });
                steps.Add(new { operation = "close", timeoutSeconds = 90 });
                break;
            case "official_dialogue_corpus":
                steps.Add(new { operation = "open", mode = "ambassador_official", presentation = "visible", timeoutSeconds = 90 });
                steps.Add(new { operation = "send", text = "Before we discuss terms, tell me plainly whom you represent and what you may decide without writing home.", timeoutSeconds = 300 });
                steps.Add(new { operation = "send", text = "Can you grant my caravans safe passage for ninety days, or must your ruler approve it?", timeoutSeconds = 300 });
                steps.Add(new { operation = "send", text = "Then give me command of your kingdom's armies and hand over one of its towns.", timeoutSeconds = 300 });
                steps.Add(new { operation = "send", text = "What about that arrangement we just discussed—can you settle it here?", timeoutSeconds = 300 });
                steps.Add(new { operation = "send", text = "To be clear, I meant caravan protection for ninety days, not land or military command.", timeoutSeconds = 300 });
                steps.Add(new { operation = "send", text = "I also want a formal alliance. Refer those exact terms to your ruler and tell me when I should expect an answer.", timeoutSeconds = 300 });
                steps.Add(new { operation = "send", text = "If the alliance is too broad, what narrower pact or counteroffer would your ruler consider?", timeoutSeconds = 300 });
                steps.Add(new { operation = "send", text = "Finally, can you grant a six-month supply agreement at a four-percent tariff under the authority you actually hold?", timeoutSeconds = 300 });
                steps.Add(new { operation = "close", timeoutSeconds = 90 });
                break;
            case "cleanup_marker": steps.Add(Phase("cleanup_marker")); break;
        }
        string mode = profile == "official_dialogue" || profile == "official_dialogue_corpus" || profile == "ui" ? "ambassador_official" : "spymaster";
        return api.PostAsync("/tests/live/run/start", new
        {
            schemaVersion = 2, campaignId, mode,
            label = "MCP capital/ambassador " + profile,
            presentation = "visible", effects = "full", autoCompleteWhenIdle = true, steps
        }, cancellationToken);
    }
}

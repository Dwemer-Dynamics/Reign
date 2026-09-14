using System.ComponentModel;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

[McpServerPromptType]
public static class ReignPrompts
{
    [McpServerPrompt(Name = "diagnose_reign_world")]
    [Description("Investigate a World Test warning or missing autonomous-world outcome using campaign evidence before source hypotheses.")]
    public static ChatMessage DiagnoseWorld(
        [Description("Campaign identifier or 'latest'.")] string campaignId = "latest",
        [Description("Timeline identifier.")] string timelineId = "main",
        [Description("The symptom or expected behavior to investigate.")] string symptom = "")
    {
        symptom = InputGuard.BoundedText(symptom, nameof(symptom), 1000);
        return new ChatMessage(ChatRole.User,
            $"""
            Diagnose this Bannerlord Reign world-system issue.

            Campaign: {campaignId}
            Timeline: {timelineId}
            Symptom: {symptom}

            Evidence order:
            1. Confirm Reign runtime health with reign_get_status.
            2. Resolve the campaign/timeline with reign_list_campaigns.
            3. Inspect reign_get_world_overview before choosing a subsystem drill-down.
            4. Correlate World Test details with audit, world history, action failures, relationship status, or rebellions as applicable.
            5. Distinguish insufficient observation, expected no-event behavior, stale native receipt, server failure, and actual rules failure.
            6. Search source only after campaign evidence narrows the hypothesis.
            7. Report exact evidence and uncertainty. Do not mutate the campaign or claim an offline result proves native behavior.
            """);
    }

    [McpServerPrompt(Name = "review_reign_verification_failure")]
    [Description("Review a Verification Lab failure, identify its owning layer, and propose the smallest evidence-backed next step.")]
    public static ChatMessage ReviewVerification(
        [Description("Verification run identifier.")] string runId,
        [Description("Optional failing check or suite name.")] string check = "")
    {
        runId = InputGuard.OptionalIdentifier(runId, nameof(runId));
        check = InputGuard.BoundedText(check, nameof(check), 300);
        return new ChatMessage(ChatRole.User,
            $"""
            Review Reign verification run {runId}. Focus: {check}.

            Load the exact run with reign_get_verification_results. Classify each relevant failure as contract, pipeline, provider, persistence, shadow-world, live-LLM, or native-game. Use reign_search_source and reign_read_source only for the owning implementation and contract. Check current runtime state only if the failure depends on the live server. Explain whether the evidence authorizes a code fix, a fixture correction, or a native acceptance rerun. Never equate offline success with game-tier completion.
            """);
    }

    [McpServerPrompt(Name = "plan_reign_feature")]
    [Description("Plan a Reign feature against the canonical roadmap, architecture, verification layers, and native-authority boundary.")]
    public static ChatMessage PlanFeature(
        [Description("Feature or change to design.")] string feature,
        [Description("Optional campaign/runtime behavior that must remain unchanged.")] string constraints = "")
    {
        feature = InputGuard.BoundedText(feature, nameof(feature), 1200);
        constraints = InputGuard.BoundedText(constraints, nameof(constraints), 1200);
        return new ChatMessage(ChatRole.User,
            $"""
            Design this Reign change: {feature}

            Constraints: {constraints}

            Read reign://workspace/roadmap, reign://workspace/architecture, and reign://workspace/runtime-extraction-plan. Inspect the current source before proposing new abstractions. Preserve the rule that the model proposes while authoritative C#/Bannerlord code validates and executes. Separate server logic, game-native hooks, persistence, UI, telemetry, deterministic verification, live-LLM checks, and native game acceptance. Identify what can live externally in MCP versus what must remain in the release runtime. Do not deploy or control the Reign lifetime group.
            """);
    }
}

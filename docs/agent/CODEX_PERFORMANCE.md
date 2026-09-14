# Codex performance controls

This document records the provider-scoped performance controls added to Reign's Control Center. They are available only when `llmProvider` is `codex_subscription`. NanoGPT, OpenRouter, custom OpenAI-compatible endpoints, their model banks, prompts, request parameters, and image routes keep their existing behavior.

## Settings contract

The persisted Codex bank is `settings.codexOptions` with schema `reign-codex-options-v1` and version `1`:

```json
{
  "schema": "reign-codex-options-v1",
  "version": 1,
  "reasoningMode": "selective",
  "reasoningEffort": "medium",
  "fastMode": false,
  "structuredOutputs": false,
  "compactMetadata": false,
  "stablePromptMapping": false,
  "parallelContextPreparation": false,
  "reuseThreads": false,
  "asyncThreadCleanup": false
}
```

The defaults are deliberately off. The existing flat reasoning values remain the legacy/non-Codex settings. When Codex is selected, the visible reasoning controls edit the Codex bank; leaving Codex restores the unsaved legacy values. A save must send the captured legacy values back to the flat fields and send the Codex values under `codexOptions`, so changing Codex reasoning cannot rewrite another provider's settings.

Fast mode changes the requested processing tier only. It does not choose a model, change reasoning effort, shorten dialogue, change the response contract, or bypass repair/finalization. An unsupported tier may be reported as Standard on the same model. The UI explains that Fast mode can increase usage and does not promise an end-to-end speedup.

The six independent experiment switches are:

| Setting | Scope |
|---|---|
| `structuredOutputs` | Use a typed schema only when the actual Reign response contract is supported. |
| `compactMetadata` | Request concise metadata while retaining meanings, evidence IDs, uncertainty, and classifications. |
| `stablePromptMapping` | Reuse the existing stable prompt segments when runtime cache support is known. |
| `parallelContextPreparation` | Parallelize independent context preparation only after a consistent snapshot is available, with four workers maximum. |
| `reuseThreads` | Reuse short ordinary-conversation threads only while identity, visibility, state, model, reasoning, Fast mode, and schema remain compatible. |
| `asyncThreadCleanup` | Return a validated result before bounded deletion of inactive Reign-owned threads completes. |

Unknown or customized contracts use the legacy request path. A switch being enabled does not remove semantic validation, action authority checks, repair passes, acceptance markers, or finalization.

## Model catalog and verification

Catalog refresh uses the official paginated runtime `model/list`, including hidden entries, and remains free of generation calls. The Control Center reports the runtime version, catalog freshness, discovery errors, supported reasoning efforts, and model verification state. States are distinct: `listed`, `unverified`, `verified`, `unavailable`, and `unknown`/inconclusive.

The candidate field defaults to `gpt-5.4`. The **Check model availability** action is separate from discovery and saving. It asks for an explicit confirmation and sends one bounded request with:

```json
{
  "model": "gpt-5.4",
  "maxRequests": 1,
  "bounded": true,
  "confirmation": "verify one Codex model request"
}
```

An unsupported-model response is unavailable. A timeout or ambiguous transport failure is inconclusive. A response is verified only when the runtime provides authoritative model identity; an echoed request field or Reign-synthesized response is not proof. GPT-5.4 access and the live Fast tier remain unproven until this user-assisted operation is run.

## Deferred `codex_performance` suite

The report helpers build a provider-free, gated plan and summarize receipts from a later Verification Lab run. A plan requires an exact model, explicit case IDs, one changed option, a positive total provider-call cap, and source/configuration fingerprints. Primary generations, repairs, retries, probes, and judge calls all count against that cap.

The later suite must compare identical cases and model while changing one option at a time. Reports include context, queue, thread startup, first text, generation, repair, finalization, and cleanup timing with sample count, median, p95, and unknown counts. Outcome categories remain separate for clean first attempts, successful repairs, accepted repair overrides, and unusable responses. Missing usage is `null`/unknown rather than zero. Reports retain exact model/runtime, settings/schema/prompt/source/validation fingerprints and requested/applied/unsupported/unknown capability states.

The report builder performs no provider call, campaign mutation, save mutation, deployment, or recommendation. No `codex_performance` live run is started automatically.

## Validation and acceptance boundaries

The browser contract entry point is `ReignBetaServer/src/Modules/Platform/codex-performance-ui-contract.mjs`; it reuses the isolated provider fixture in `chat-provider-ui-contract.mjs` so Codex controls are checked together with provider switching and parity. Intercepted traffic covers unsaved Codex draft preservation, nested settings defaults, Fast-mode placement immediately after reasoning effort, all experiment defaults, save payload shape, catalog rendering, and the explicitly confirmed bounded verification route at 1672, 1024, and 600 pixels.

Native Bannerlord raster/fidelity checks do not apply to this web-settings change. The provider-free browser contract does not prove GPT-5.4 entitlement, actual Fast-mode service behavior, Codex latency, roleplaying continuity, or human acceptance. Those remain deferred user-assisted comparisons with the declared call cap.

Codex comparison fixtures must identify a shipped character or include a completed character snapshot (constructed, traits, and a complete narrative when using narrative v5). Each isolated arm preserves the supplied character stack; fixture personality authoring is not part of the latency comparison. Both arms must keep the same reasoning mode and schema version. Served-model consistency uses authoritative runtime evidence; a request-model echo leaves verification unknown.

## Deployment evidence — 2026-09-10

Implemented by three Luna max agents and finished with Astra medium. Validation `20260911-011112-502ac3b9` passed changed/Tier 3/Release in 375.8 seconds with enforced repository hygiene, 524 MCP tests, and the persistence boundary. Artifact-bound Codex, prompt-efficiency and interaction-architecture checks passed; browser evidence covers 15 dedicated screenshots and 66 shared cases at 1672, 1024 and 600 pixels.

The exact validated server artifacts were deployed with hashes and rollback copies. The visible supported launcher restored the dedicated Control Center and unified lifetime group. All model selections and selective/medium reasoning were preserved; Fast mode and all six experiments remain Off. The project-local MCP connection uses the validated assembly and exposes the gated comparison input. See `.codex-build/codex-performance/REVIEW_PACKET.md` for reports, source fingerprint, reviewed diff, deployment receipt, screenshots, and deferred acceptance. GPT-5.4 access, actual Fast service and live roleplay performance remain unproven; no provider-consuming or native campaign test was run.

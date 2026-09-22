# Reign MCP

Enrolled ruler-docket setup requires a paused campaign. Its native `ui_open` reasserts pause after settlement-menu exit and outgoing screen cleanup, and returns `docketClock` (`worldDayBefore`, `worldDayAfter`, `timePaused`). Any time drift fails setup. Use the guarded campaign-test controller to pause/drain or advance days; observation phases do neither. Ordinary player Court time controls are unchanged.

Ruler Docket launch testing is exposed through `reign_get_ruler_docket_test_manifest` and `reign_start_ruler_docket_test`. Mutating phases require the exact armed campaign-test Current and exercise ordinary petitions plus all 68 noble-matter templates through the visible production hearing, natural-language conversation, and bound judgment controls. Unique fixture-run identities choose among distinct participant sets produced by the real day/slot selector, and structured receipts support an aggregate launch gate for variety across nobles, clans, cultures, demographics, roles, and ruling sides. Each receipt includes every persisted attributed conversation line and a deterministic NPC-statistics verdict; percentages, hidden tiers, relationship scores/values/losses, penalties, and game-mechanics language fail the native case, while ordinary in-world quantities remain valid. Natural reconciliation includes nobles who honor the judgment, acknowledge after the ruling that it is given or heard, and explicitly promise not to keep, renew, continue, or pursue the quarrel or declare that the quarrel ends here; direct refusal or appeal takes precedence over embedded acceptance words. Marriage, divorce, murder, execution, reversal, court-stay, persistence, proclamation, and 180-day soak branches remain restore-isolated and guarded.

Reign MCP is an optional local STDIO server for inspecting, testing, and building Bannerlord Reign through Model Context Protocol clients such as Codex.

Social Reputation capability tests may set `promptOverrideAssisted=true` only on `reign_start_social_reputation_test` with the `player_affair` profile. Acceptance-sensitive turns retain their natural player wording and carry a separate request-scoped directive. The server activates it only after revalidating the armed live-test run, exact executing Affair command, prepared enrollment, current game instance, and active disposable save. It never modifies global prompt files or settings, and every affected response returns an authorization receipt proving request-only scope.

After that profile has produced an eligible natural Affair exposure miss, the same guarded profile may set `forceRareAffairExposureAfterNaturalMiss=true` on a retry. The server accepts the one-shot only for the retry's exact enrolled run/case/validated signal, preserves the production chance and natural roll, and records consumption in the authoritative evidence.

It is a façade over Reign's existing loopback APIs and workspace contracts. It does not replace Reign's HTTP server, World Test storage, Verification Lab, or authoritative C#/Bannerlord validation.

## Current scope

- Composite server/provider/background/memory/telemetry status
- World Test campaign discovery, overview, and subsystem drill-downs
- World history, actions, rebellions, relationships, logs, audits, and character summaries
- Verification Lab and live-test observation
- Explicitly gated Verification Lab start/cancel controls
- Workspace-confined source search/read and canonical roadmap resource
- Explicitly gated isolated builds and non-listening quick/offline verification
- Automatic project discovery, fail-closed catalog coverage, build/test plans,
  structured validation reports, and a shared non-listening CI entry point
- Reusable diagnostic, verification-review, and feature-planning prompts
- Generated testing-tool, harness, scenario, and safety-gate catalog
- Guarded autonomous campaign testing on enrolled disposable save copies
- Fingerprint-bound Rebellion certification with organic direct/mail dialogue,
  deterministic state-machine profiles, staged guarded save/reload, mixed-holding
  transfer, foreign-family reintegration, and fail-closed cleanup evidence
- Fingerprint-bound Government release certification with production dialogue,
  native/UI/persistence/pressure/soak cases, strict readiness evaluation, and
  provenance-preserving carry-forward limited to explicitly audited unaffected evidence
- Receipt-bound Bannerlord restart and Party Agency save-roundtrip verification
  for exact checkpointed campaign-test saves
- Party Agency native evidence for immutable temporary-guest battle and civilian equipment
- Self-contained natural-language Party Agency hostility-review certification fixtures
- Party Agency recovery fixtures that inject missing-source or invalid-target state before
  exercising the production return lifecycle on an enrolled disposable save, with
  pre-return native-state proof that cannot be masked by camp protection or later fallback
- Provenance-preserving carry-forward of explicitly selected Party Agency passes unaffected by audited equipment-lock-only, guest-relationship-isolation-only, hostile-battle-fixture-only, hostility-language-fixture-only, partyless-invitation-fixture-only, remaining-native-invitation-fixture-only, ineligible-native-fixture-only, companion-native-fixture-only, native-time-driver-only, accepted-lifecycle-router-only, simultaneous-review-queue-only, departure-language-fixture-only, return-timing-evidence-only, recovery-fixture-only, missing-return-recovery-fixture-only, final-fingerprint-harness-only, or transcript-evidence-parser-only fixes

The server intentionally provides no deployment, arbitrary HTTP/shell/process
execution, baseline deletion/import/rollback, Save Sync rollback, or unrestricted
native game-action tools. Its limited lifecycle and campaign mutations use fixed
controllers, exact confirmations, immutable baseline enrollment, disposable-save
ownership, queue-drain gates, and Save Sync capacity checks.

## Build and test

Use `reign_get_validation_plan` before code edits and `reign_validate` afterward.
The manifest-selected report under `.codex-build/reign-mcp/validation` is the
authoritative source of deployable artifacts. CI and MCP-recovery workflows use:

```powershell
.\scripts\reign-validate.ps1 -Profile changed -ChangedPath <workspace-relative-paths>
```

Use `-Profile product` for explicit product-wide confidence and `-Profile all`
only for ecosystem release validation. The command does not start, stop,
replace, or deploy Reign.

## Deploy and coordinate

Call `reign_get_validation_status` before heavy validation and pass the current
Codex UUID as `requestingTaskId` to `reign_validate`. This read-only status shows
current owner/progress without reserving or taking over the OS lease. Follow
[task coordination](../docs/agent/TASK_COORDINATION.md) for actual overlap.

Deploy the exact `mcp/out` artifact from the successful report, preserving the
project's existing configuration and tool gates. Do not rebuild during packaging.
Use [deployment-runbook.md](docs/deployment-runbook.md) for activation, read-only
acceptance and rollback. [The testing guide](../docs/agent/TESTING_TOOL_GUIDE.md)
is a concise index of focused procedures retained in `reign.testing.json`.

## Configuration

The activation script renders `deployment/config.toml.template` against the
verified package. `.codex/config.toml.example` remains a development example
that runs from the build output. Keep the MCP optional.

Environment variables:

| Variable | Default | Meaning |
| --- | --- | --- |
| `REIGN_WORKSPACE_ROOT` | auto-discovered | Bannerlord Events workspace |
| `REIGN_SERVER_URL` | `http://127.0.0.1:5101` | Numeric loopback Reign origin |
| `REIGN_MCP_ALLOW_BUILD` | `false` | Enables isolated allowlisted builds |
| `REIGN_MCP_ALLOW_RESTORE` | `false` | Allows a build or validation to restore packages |
| `REIGN_MCP_ALLOW_VERIFICATION_CONTROL` | `false` | Enables approval-gated start/cancel through the live server |
| `REIGN_MCP_ALLOW_OFFLINE_VERIFICATION` | `false` | Enables approval- and confirmation-gated non-listening quick/offline verification |

For scoped provider-free UI checks, `audit-rendered-preview.mjs --targets clan-accords,economic-report` selects those interfaces and their declared states. Strict catalog IDs and full inventory checks remain required. Its `partialScope=true` report never establishes full-catalog completion or native acceptance; no deployment or campaign authority is granted. See the testing guide for evidence locations and the separate unfiltered readiness gate.

See [architecture.md](docs/architecture.md), [security-model.md](docs/security-model.md),
[tool-catalog.md](docs/tool-catalog.md), the workspace
[testing guide](../docs/agent/TESTING_TOOL_GUIDE.md), and
[deployment-runbook.md](docs/deployment-runbook.md). Project discovery and
enforcement are documented in
[validation-governance.md](docs/validation-governance.md).

`court_life_family_fixture` is a separately authorized household setup profile on the exact armed disposable Current. It requires an empty household and a living male ruler aged at least 32. It creates an adult wife through native marriage eligibility and MarriageAction, then a four-year-old daughter and fourteen-year-old son using native CreateChild at their final integer ages. It preserves the children's NotSpawned state, establishes parentage once and capital residence, and returns `reign-court-life-family-fixture-v1` evidence with native family inventory and durable creation IDs. Reusing the same fixture ID verifies/reuses that household instead of creating duplicates. Inspect partial failures before retry; restore the preparation checkpoint for conflicting identities. It never injects attention, favor, neglect, NPC acceptance, or court decisions, advances time, or saves. Drain production family synchronization before checkpointing. `court_life_preflight` reports native player/family inventory for independent checks after reload.

Creating a reusable named baseline is a separate user-authorized operation. Start a separate preparation run from the exact original baseline, create its guarded disposable Current, apply only the authorized household setup, drain and checkpoint, then save through native Save As and verify Save Sync and a different-process reload. Do not rename an acceptance save containing prior scenarios into a baseline. Preserve the original baseline and create another enrolled disposable Current from the new baseline before further testing.
# Paired local repositories

The launch workspace uses sibling `Reign` (private client and integration tools) and `ReignServer` (public server and shared source) checkouts. `ReignServer/scripts/Connect-Repositories.ps1` establishes the declared source junctions. The MCP workspace root is the Reign checkout; validation audits both repositories and retains one OS lease. See `../docs/agent/testing/RELEASE_PACKAGING.md`. The old GitHub destination is retired after verified cutover; development stays local.

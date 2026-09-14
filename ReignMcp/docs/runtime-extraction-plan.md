# Runtime test-tool extraction plan

## Objective

Reduce testing code, polling, UI, and orchestration in the ordinary Bannerlord Reign module while retaining the smallest reliable bridge needed to observe and prove native behavior.

MCP does not execute inside Bannerlord. It can replace external presentation and coordination, but it cannot replace code that must read or act on Bannerlord's main thread.

## Current runtime candidates

The current client always registers or ticks several test-oriented components:

| Component | Current runtime presence | Target |
| --- | --- | --- |
| `ReignLiveInteractionTestHost.ApplicationTick` | Called every application tick; idle heartbeat settles to ten seconds | Diagnostic module/profile only |
| `ReignNpcDialogueAuditRunner.ApplicationTick` | Called every application tick with an early debug-mode guard | Diagnostic module/profile only |
| `ReignPartyDialogueAuditRunner.ApplicationTick` | Called every application tick with an early debug-mode guard | Diagnostic module/profile only |
| `ReignActionGauntlet.ApplicationTick` | Called every application tick and polls verification/overnight command files | Diagnostic module/profile only |
| `ReignSocialBalanceHarnessCampaignBehavior` | Registered in every campaign | Diagnostic module/profile only |
| MCM Debug controls and destructive test-realm actions | Shipped in ordinary settings UI behind a toggle | Diagnostic module/profile only |
| `ReignWorldTestClient` daily heartbeat | Once-per-campaign-day full native observation | Keep, then slim and rename as release telemetry |
| Relationship/native projection receipts | Required to prove authoritative convergence | Keep in release |
| Save Sync, world history, diplomacy, rebellion, relationships | Gameplay systems with diagnostics | Keep in release; expose status externally |

This inventory is architectural, not permission to remove code. Save compatibility and native acceptance must be proven before changing the release module.

## Target packages

### ReignBeta release module

Keep:

- authoritative game systems and validators;
- small versioned telemetry DTOs;
- once-per-day or revision-triggered observation;
- native action and projection receipts;
- error/health reporting;
- no test-control polling while ordinary play is active.

Remove or compile out:

- test scenario definitions;
- test command dispatch and lifecycle control;
- audit-runner plans;
- social-balance mutation commands;
- action-gauntlet command-file polling;
- test-realm preparation;
- test-only MCM controls and reports.

### ReignBeta diagnostics module

Create an optional development-only Bannerlord submodule that references the release assembly and contains:

- live interaction command executor;
- NPC and party audit runners;
- action gauntlets and native game-tier bridge;
- social-balance harness;
- disposable-save fixtures and test-realm preparation;
- debug MCM controls.

It should be absent from normal installation and enabled only on disposable test saves. Its commands must remain armed, idempotent, revision-bound, and campaign-scoped.

### Reign server

Retain:

- stable telemetry ingest and query APIs;
- World Test aggregation/database schema;
- Verification Lab and live-test run state;
- safety validation, redaction, and bounded retention.

After MCP parity, the Control Center's World Test presentation may become optional. The underlying APIs and aggregation remain useful to non-MCP clients and should not be removed merely because MCP exists.

### Reign MCP

Own:

- discoverable World Test views and evidence-first diagnostics;
- status correlation across subsystems;
- source, roadmap, and contract inspection;
- isolated builds and offline verification;
- test planning and result interpretation;
- gated coordination of server-side verification.

MCP must not become a native action executor or bypass the diagnostics module's arming boundary.

## Slim release telemetry contract

Replace the test-branded heartbeat over time with a versioned `world-observation` contract:

- campaign/timeline identity and observed day;
- feature-state booleans;
- population and kingdom/clan totals;
- active wars and agreement identifiers;
- bounded recent authoritative action outcomes;
- rebellion roll/movement/membership revisions;
- relationship projection generation and aggregate receipt counts;
- per-subsystem revision/hash so unchanged arrays can be omitted;
- payload byte count, collection duration, and next scheduled observation.

Collection rules:

- never scan native collections per application frame;
- collect on the Bannerlord main thread at a bounded daily/revision cadence;
- serialize and send off-thread after immutable capture;
- cap every collection and send deltas when revisions are unchanged;
- retry only after server rejection without advancing confirmed cadence;
- suspend during Save Sync alignment and resume once;
- make telemetry disablement visible as `disabled`, never as healthy.

## Migration sequence

1. **Measure**: record per-component application-tick time, allocation, heartbeat construction time/bytes, and idle HTTP cadence in the existing diagnostic build.
2. **Stabilize MCP parity**: prove World Test overview/details, live-test status/report, verification results, actions, audit, and logs through MCP.
3. **Define interfaces**: extract only the minimal public diagnostic contracts required by an optional diagnostic module.
4. **Split test code**: move live host, audits, gauntlets, social-balance harness, and debug MCM controls without changing production behavior.
5. **Slim telemetry**: version and delta-compress the daily native observation while preserving server compatibility during transition.
6. **Build two profiles**: release without diagnostics; diagnostic with the optional module and explicit visual identification.
7. **Verify save compatibility**: load existing saves in both profiles, including Save Sync branches and a diagnostics-created disposable save.
8. **Compare behavior/performance**: identical gameplay outputs plus measured reduction in tick time, allocation, payload volume, DLL size, and idle requests.
9. **Deploy only after acceptance**: stop the visible Reign lifetime group, replace verified artifacts, and restart through the supported visible launcher.

## Completion gates

- Release client contains no live-test command dispatcher, audit plans, action gauntlet, social-balance mutators, or test MCM buttons.
- Release client still provides enough telemetry to diagnose every autonomous subsystem represented by World Test.
- Diagnostic module cannot arm against an ordinary campaign without explicit disposable-save confirmation.
- Existing saves load without loss of gameplay state or Save Sync alignment.
- Offline verification remains 100% green.
- Native diagnostic suites remain available and pass from the optional module.
- A measured before/after report demonstrates the actual performance and binary-size effect; no performance claim is based solely on code removal.


# New-Game Readiness Seal Design

**Date:** 2026-07-28

**Status:** Approved design

**Scope:** Bannerlord Reign client initialization, authoritative server preparation, native projection, and Save Sync readiness

## Purpose

A new Reign campaign must remain paused behind its existing preparation popup until every authoritative, native, and gameplay-critical initialization task is complete. When the popup closes and the player receives control, Reign must make a hard promise: every subsequent native save can receive a matching Save Sync snapshot while the local server is healthy and the 15-state retention limit has capacity.

Optional derived work must not hold the campaign. Portrait image rendering, semantic embeddings, vector-index optimization, snapshot deduplication, compaction, and other rebuildable caches may continue in the background behind their existing feature gates.

## Observed Failure

The `BaseTwo` native save reproduced the new-game race in campaign `E2I7x0FX5yGt`.

The preparation popup closed after notable personality assignment and native-trait confirmation. The shared initialization gate was marked ready at that point. Deferred systems then began their first work:

- native relationship baselines;
- Reign calendar initialization;
- identity and portrait-roster metadata synchronization;
- initial world-history capture and upload;
- ambient relationship input and server evaluation;
- native relationship target projection and receipt upload;
- initial recurring world and relationship producers.

Those systems produced sustained world-history and relationship traffic for several minutes after the player regained control. `BaseTwo` was saved during that traffic. Bannerlord completed the native file, but the one-second Save Sync drain could not reach a stable world-history watermark, so Reign correctly declined to register an inconsistent rollback snapshot.

The defect is therefore not the warning dialog or the one-second budget. The defect is that the current gate equates “notable personalities are complete” with “the campaign is ready,” then releases all remaining initialization producers at once.

## Chosen Architecture

Reign will use one staged initialization coordinator as the sole owner of new-game readiness. Individual systems will expose bounded, idempotent preparation operations, but they will not independently release the gate.

The existing `ReignCampaignInitializationGate` remains the public readiness boundary used by gameplay systems. Its `MarkReady` operation will no longer be callable by personality generation. Only the coordinator may seal and release a campaign after the complete pipeline and final quiescence check succeed.

The coordinator will use a campaign-generation identity. Every asynchronous result and main-thread callback must match the active generation before it can mutate state, report progress, or release the gate. Loading another campaign, returning to the menu, or starting a new game invalidates the old generation.

## Readiness Boundary

The blocking readiness boundary includes:

1. Permanent notable personality assignment.
2. Application and server confirmation of native personality traits.
3. Deterministic native relationship baselines.
4. Reign calendar and age-anchor initialization.
5. Ruler, court, rebellion, diplomacy, family, and other deterministic session seeds required before their first event.
6. Authoritative timeline creation or restoration.
7. Full identity-network and portrait-roster metadata synchronization.
8. Initial authoritative world, diplomacy, relationship, and character-state snapshots.
9. Initial ambient relationship evaluation when enabled.
10. Application of all initial native relationship targets.
11. Durable upload and acknowledgement of native relationship receipts.
12. Durable upload of every world-history event produced through the initialization watermark.
13. A final server readiness acknowledgement for the expected campaign and timeline.
14. Persistence of the readiness-seal version in the native campaign state.

The readiness boundary excludes:

- generation of new portrait images;
- optional portrait derivatives that can be rebuilt on demand;
- semantic embeddings and vector-index reconstruction;
- derived search indexes and distant-character read models;
- snapshot optimization, hashing, deduplication, and compaction;
- optional diagnostics, audits, and test fixtures;
- future routine passive work that is scheduled only after release.

## Pipeline

### Stage 1: Establish Campaign Generation

The coordinator captures the durable campaign ID, timeline identity, and a new in-process generation token. It resets progress, closes the gate, disables recurring passive producers, and opens the preparation popup only after Bannerlord's campaign map screen is stable.

For a new campaign, the coordinator starts at the first incomplete stage. For a partially initialized save, it resumes from persisted checkpoints. For a sealed save, it does not run the new-game pipeline.

### Stage 2: Generate Personalities

The existing server batch assigns permanent notable personalities. Reign applies the five supported native traits on Bannerlord's main thread and confirms the complete observation set to the server.

This stage retains its current all-or-nothing validation. Missing assignments, incomplete traits, disappeared living heroes, or failed confirmation keep the gate closed.

Completion of this stage updates progress but does not release the campaign.

### Stage 3: Apply Deterministic Native Foundations

The coordinator invokes explicit idempotent preparation entry points for native state:

- relationship baselines;
- calendar and age anchors;
- ruler and court seed state;
- rebellion, diplomacy, family, reputation, and related session foundations.

These operations must return completion evidence instead of scheduling themselves through `RunWhenReadyOnMainThread`. A completed operation may be called again without duplicating effects or rewards.

### Stage 4: Synchronize Authoritative Server Foundations

The coordinator opens or confirms the campaign timeline, synchronizes the identity network and portrait-roster metadata, and submits the initial authoritative snapshots required by gameplay systems.

Each request must carry the active campaign and timeline identities. A response for a stale generation is discarded. Success means the server has durably committed the operation, not merely accepted background work.

### Stage 5: Reconcile Initial Relationships

When relationship simulation is enabled, the coordinator captures the initial ambient relationship input and waits for the server's authoritative result. All returned native targets are merged into one initialization projection plan.

The coordinator advances that plan across controlled main-thread frames, using bounded per-frame work so the popup remains responsive. It continues until:

- no native targets remain;
- no projection failures remain unresolved;
- every receipt has been uploaded and acknowledged;
- the server reports the initialization plan complete.

Routine continuous relationship polling remains disabled during this stage so it cannot introduce an unbounded moving target.

### Stage 6: Seal Save-Critical State

The coordinator establishes an initialization watermark and prevents recurring producers from scheduling new work. It flushes all routine world-history buckets, drains world-history events through the watermark, drains save-critical relationship envelopes, and waits for workers to stop.

Quiescence must be observed twice consecutively, separated by at least one application frame. Both observations must show:

- no initialization task in flight;
- no native relationship target pending;
- no native relationship receipt pending;
- no save-critical relationship envelope pending;
- no in-memory world-history event pending through the watermark;
- no disk-backed world-history event pending through the watermark;
- no save-critical upload worker still processing the watermark.

The coordinator then requests a server readiness acknowledgement containing the campaign ID, timeline ID, expected history sequence, initialization-plan identity, and seal version. The server must confirm that its durable state has reached each expected watermark.

Only after that acknowledgement does Reign persist the seal and release the gate.

### Stage 7: Release and Start Passive Systems

The coordinator persists the versioned seal in native campaign state, marks the shared gate ready, closes the popup, and displays one completion message.

Recurring passive systems are then enabled. Their first cadence begins from the sealed state rather than treating the initial world as overdue. This prevents an immediate duplicate initialization storm.

## Persistent State and Compatibility

The native save will persist a compact initialization journal containing:

- seal schema version;
- campaign ID and timeline ID;
- completed deterministic stages;
- personality completion;
- authoritative foundation completion;
- relationship initialization-plan identity and completion;
- sealed world-history sequence;
- final seal status.

The journal must remain well below Bannerlord's signed 16-bit archive-entry limit and use the existing chunked save-payload codec if it ever exceeds one compact entry.

Compatibility behavior:

- **Fresh new game:** run the full pipeline.
- **Partially initialized new-game save such as `BaseTwo`:** retain completed personalities and resume the missing stages before releasing gameplay.
- **Established legacy save with completed personality initialization but no seal:** run a one-time compatibility audit, synchronize identities, drain existing critical queues, verify server alignment, and persist a seal. Do not replay new-game relationship baselines or regenerate personalities.
- **Current sealed save:** perform ordinary Save Sync load alignment only; do not rerun the initialization pipeline.
- **Seal from an older schema:** run only the migration stages introduced after that schema.

## Save Contract After Release

After the seal, Save Sync registration may assume there is no inherited initialization backlog. A normal save captures a finite history watermark and drains only work at or before that watermark; events created afterward belong to the next state and cannot keep the current registration moving indefinitely.

The guarantee applies when:

- the local Reign server is online and healthy;
- the active campaign and timeline are aligned;
- Save Sync has capacity under the 15-unique-state policy;
- the filesystem can create the native save and raw rollback snapshot.

Server unavailability, storage exhaustion, or filesystem failure remain legitimate and explicitly reported failures. Initialization backlog is no longer a legitimate post-release failure mode.

## User Experience

The existing modal preparation screen remains the single user-facing surface. Its status text reports the active stage and bounded progress, for example:

- Assigning personalities: `642 / 1206`
- Applying native foundations
- Synchronizing identities and world state
- Reconciling relationships: `1850 / 3954`
- Sealing save-ready state

The campaign remains paused until success. Optional derived tasks are not shown as blockers.

On failure, the popup shows:

- the failed stage;
- a concise actionable reason;
- a retry action;
- confirmation that completed stages will not be repeated.

There is no automatic timeout that releases an unsealed campaign and no “continue without preparation” bypass.

## Failure and Cancellation Semantics

Every stage must be idempotent. A failed request may be retried without duplicating database rows, gameplay rewards, relationship changes, or history events.

Critical failures keep the gate closed. Retries resume at the first incomplete checkpoint. A server restart is handled as a retryable failure after health and campaign identity are re-established.

Generation cancellation is mandatory. Every asynchronous boundary checks the generation token before:

- modifying a Hero or other Bannerlord object;
- updating a persisted checkpoint;
- changing popup state;
- advancing a server watermark;
- marking the campaign ready.

Stale work may finish externally, but its result cannot affect the active campaign. Server operations use campaign-scoped idempotency identities so a late retry is harmless.

## Diagnostics

The live runtime snapshot will expose:

- readiness pending/ready state;
- seal schema version and sealed status;
- active generation ID;
- current stage;
- completed and total work;
- elapsed time;
- last failure and retry count;
- world-history memory and disk queue counts;
- relationship envelope, target, and receipt counts;
- expected and acknowledged server watermarks.

Logs will record one start and completion entry per stage, including duration and counts. Repeated frame-level polling will not produce log spam.

## Testing

### Deterministic Tests

Tests will verify:

- legal stage ordering and refusal to seal when a required stage is incomplete;
- personality completion cannot mark the shared gate ready;
- checkpointed retry resumes without repeating completed stages;
- stale generation callbacks are ignored;
- two stable quiescence observations are required;
- new work between observations prevents sealing;
- seal persistence occurs before gate release;
- optional derived work is excluded from readiness;
- legacy, partial, old-schema, and current-schema paths select the correct stages;
- captured save watermarks are finite and ignore later events;
- readiness diagnostics report accurate stage and queue state.

### Integration and Fault Tests

Tests will inject failure into every critical stage and confirm:

- the campaign remains gated;
- the failed stage is reported;
- retry succeeds without duplicate native or server effects;
- cancellation prevents cross-campaign mutation.

Save Sync tests will cover:

- immediate save on the first playable frame after seal;
- save during ordinary passive activity;
- repeated save, finalized snapshot, and load/rollback cycles;
- overwrite and native deletion synchronization;
- server restart during initialization;
- server offline after release;
- 15-state capacity behavior;
- partially initialized `BaseTwo`-style migration.

### Live Acceptance

Before deployment is considered complete:

1. Start a fresh campaign with the validated build.
2. Observe every initialization stage while the campaign remains paused.
3. Confirm the final seal and empty save-critical queues before the popup closes.
4. Save immediately on the first playable frame.
5. Verify that the server registers and finalizes the matching snapshot.
6. Load that save and verify successful rollback/alignment.
7. Repeat the save-to-load cycle at least twice while passive systems are active.
8. Confirm no “Save Sync Deferred” warning and no unprotected native save.
9. Run the canonical all-project Reign validation profile and retain its report.

## Deployment

This is a cross-cutting client/server readiness change and requires the canonical `all` validation profile. Deployment must use the validated artifacts, preserve rollback copies, and follow the visible unified Reign server lifecycle.

The running game must be closed before replacing client files. If the server contract changes, the visible Control Center/server lifetime group must also be closed before deployment and restarted only through the supported unified launch path.

The Reign roadmap entry for Save Sync fast-path readiness must receive a dated progress note only after implementation and live acceptance are complete.

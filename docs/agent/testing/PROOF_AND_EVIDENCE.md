# Proof selection and evidence

Read only the procedure relevant to the current task. Existing provider, campaign, and deployment gates remain authoritative. Dated procedure history was retained during the 2026-09-12 guide split.

## Choose the lowest proof layer

1. Use focused unit or contract coverage for deterministic logic.
2. Use Verification Lab `quick` or `offline` for provider-free subsystem behavior.
3. Use `live-llm` only when provider behavior is part of the requirement.
4. Use an enrolled disposable Bannerlord campaign for native integration, persistence, UI, time, or performance claims.
5. Reserve human review for subjective dialogue, visuals, and play quality.

Offline success never substitutes for native acceptance when Bannerlord behavior is material.

Rebellion release work is owned by
`rebellion-certification-manifest.json`. Use one authoritative proof per
distinct transition or failure mode; do not multiply equivalent channel,
timing, ruler-transition, or verdict permutations. Prepare immutable
enrollment/build/provider/catalog/Bannerlord fingerprints, run the compact
server contract, then start only missing manifest cases through the guarded
certification tool. The server-only contract credits only the five bounded
ambiguity, negation, and correction adapters. The six direct/mail
pledge/refusal/reporting cases and every client state-machine case require their
own completed live report. Each organic case is a bounded two-turn exchange:
the opening request samples the noble's concerns, then a manifest-owned natural
follow-up answers identity, plan, risk, and terms and requests a final decision.
Every decisive follow-up repeats that the request concerns rebellion against the
ruler so the production secret-preparation adapter does not depend on unstated
prior-turn context.
Text classification is subordinate to the structured `actionGate`: pledge
requires an actionable accepted commitment. An unconditional final refusal
requires `needed=true` with `commitment=refused`. Negotiation or deferral
requires `needed=false` with `commitment=conditional` and persists nothing; a
refused label without an actionable gate also fails closed. An eligible
concealed report uses either `commitment=final_private_report` or a visible
`commitment=refused` plus explicit non-negated private report intent, while the
visible reply remains a refusal or guarded neutrality. Natural final
refusal therefore does not depend on a second brittle phrase match. Natural final
future-tense and nominal forms such as `I'll pledge`, `we will pledge`, or
`that's my pledge` must route to secret preparation, never immediate declaration.
Normalized contraction forms such as `cant`/`can t`, `wont`/`won t`, and
`dont`/`don t` remain explicit negations.
A reply beginning with a standalone `No` is a refusal only after report and
positive-pledge recognition have failed. Explicit final phrases such as `will
not throw my clan` and `stands aside` also resolve refusal after scene-setting
prose, while negotiation-only `no land`/`no gold` does not.
Persisted provenance maps production-normalized `in_person`/`in person` to `individual_chat`
and preserves `correspondence` distinctly.
Mail certification enrolls the exact known recipient clan leader under the
disposable fixture sovereign. It dispatches the concern-sampling letter, advances
six native days and waits for correspondence quiescence, then dispatches the
decisive follow-up, advances another six native days, and waits again before the
decision oracle. During guarded passive-world advancement only, the standard
Reign letter-arrival inquiry is dismissed as `Not Now`; the harness never opens
the reply or treats notice dismissal as evidence. A successful dispatch is never
accepted as reply or decision evidence.
For a planned rebellion, the delivered NPC reply uses the same `actionGate`
contract as Individual Chat and routes pledge, refusal, or concealed report
through `resolve_rebellion_pledge` with `requestChannel=correspondence`.
`rebellionDecision=accept_recruitment` remains reserved for an already-declared
rebellion and cannot substitute for secret-preparation evidence.
Pledge follow-ups name the allied leaders and exact promised holding so an
otherwise willing noble is not left negotiating an unspecified condition.
Report cases ask only whether the clan will support or refuse the rebellion; they
never tell, invite, or coach the NPC to report. Production supplies authoritative
sovereign identity, NPC-to-sovereign and NPC-to-player relations, Reign Loyalty,
and native Honor. The server accepts a private report only for the NPC's own
sovereign, relation at least +10, and either Loyalty at least 61 or positive
Honor. Ineligible report intent fails closed. The immediate result remains
indistinguishable from refusal; only the delayed ruler summons reveals the report.
For `RB-LANG-003` and `RB-LANG-006` only, `native_setup` enrolls living, free
leaders of active non-ruling vassal clans that the production pledge action can
accept. It records exact clan-leader identity plus prior native relation and Honor
values, then temporarily gives each leader at most -10 relation to the player, at
least +35 to the current sovereign, and Honor at least +1. The guarded checkpoint
reload restores those native values. These moderate values satisfy the production
plausibility gate without defining the final choice. The NPC still decides from
natural manifest turns through the configured model; the fixture never injects a
decision action or prompt directive.
Clarification or negotiation is not a decision. Visible organic cases must originate in Individual Chat or Correspondence; a direct
pledge, report, summons, or verdict action in the harness stream invalidates
the evidence. `travel_prepare` stages a naturally reported production plot;
`reign_checkpoint_campaign_test` owns the quiescent rolling checkpoint. After
guarded restart and native advancement, `travel_verify` proves the
report and summons travel, stable ruler/informer identity, and the deadline
anchored exactly seven days after delivery. Mixed transfer and foreign-family
reintegration use production native actions on the exact Current save and are
discarded by guarded reload. Only explicitly marked language/deterministic
`passOnce` evidence may be carried, and only as its original durable report
reference across identical immutable fingerprints. Native, save/load,
transfer, reintegration, and cleanup evidence is never carried.

## Relationship throughput evidence

Transport probe v2 adds synchronous/asynchronous command execution comparisons while preserving each request's SQL, payload, connection and TLS policy. Four connections each run eight payload sizes in both modes, with one warm-up and eight measured requests: 576 length reads plus four TLS-status reads (580 total). The 30-second request budget and 10-second command/connect limits remain. This diagnostic does not enable asynchronous execution in production or split a logical database read into multiple snapshots.

Replay v3 captures the original lifecycle conception GUIDs in `conception-inputs.json`, with a reported SHA-256. Those identifiers are random inputs to existing commitment rolls. Both candidate routes reuse the original identifier for the same campaign/timeline/pair/day; duplicate, extra or omitted conception attempts fail. The thread-local override requires the owning non-listening replay and clears between routes and on failure. Normal gameplay retains its existing GUID generation and roll seeds. Retain the new input file alongside the existing replay evidence. Raw diagnostics now include all commitment and reservation rows; roll differences remain failures.

Before warm-up, the read-only transport probe writes `transport.json` (`reign_relationship_transport_probe_v2`). It uses the configured literal-loopback ReignValidation endpoint and TLS policy, four unpooled connections with 8/16/64/256 KiB write buffers, and eight fixed payload lengths from 128 bytes to 128 KiB. One warm-up and eight measured `SELECT length(@payload)` reads per combination and execution mode give at most 576 length requests plus four unmeasured TLS-status reads. It stops starting requests after 30 seconds and uses 10-second connection/command timeouts. Skipped/incomplete probes do not establish transport performance. Evidence includes sizes, TLS status and median/maximum latency, without credentials or connection strings. No listener, installed configuration, database settings or campaign data changes are permitted. Probe work is excluded from replay timing.

Retry-boundary follow-up: a recovered partially processed day remains immutable when `started_ts` is nonzero, including its cursor. An unchanged later observation advances the latest known day without changing the content timestamp, preventing an older update from replacing newer knowledge. History pruning also protects the earliest pending/processing day. Three additional fixtures cover these boundaries.

Queued hero facts are versioned by timeline/hero/day in `relationship_hero_observations`. Ingestion writes versions and latest observed state in the input transaction; already-processing/processed retries cannot replace its facts. Reads resolve each presence day's explicit rows or the newest observation at/before that day, including matching and fling inputs. Cleanup retains one predecessor and seven recent days plus every queued future version. Tests compare normal arrivals, the legacy latest-row loader under a burst, and the corrected burst; separate cases cover timeline isolation, cleanup anchors, unreconstructable legacy history and completed-input retries. A historical day with only future facts fails explicitly. Drain the installed queue before deployment, since history lost by older builds cannot be invented by migration.

The exact-document parse cache can retain ingestion's already-normalized document as an independent deep copy, avoiding a second parse without making the cache authoritative. Offline relationship phase samples additionally contain `sqlReadProfile`: per-statement hash, referenced table names, call/row counts and setup/execution/materialization timing. Parameter values and raw SQL are excluded. These detailed profiles run only in the non-listening verification CLI; ordinary runtime telemetry keeps aggregate counters.

Replay v2 deserializes identical retained input bytes independently for three routes, with a cleared parsed-document cache before the same warm-up. The original and candidate-equivalence routes compare daily processed chemistry/lifecycle rows and every row of the other selected tables; all three routes compare complete final state. A separate candidate route measures two-second arrivals without daily hash or file observers. Its samples remain in memory until the measured run ends; a process failure may therefore lose those timed samples and cannot establish acceptance. Functional routes retain append-only samples and raw action/conception/lifecycle diagnostics. Opaque native IDs map to unique full semantic records, including parsed payloads and referenced conceptions; reservations and commitment rows remain compared. Ambiguous identities fail rather than hiding duplicate actions. Regression cases prove ID/key-order equivalence and detection of payload, parentage and duplicate-action changes. Verification retention resolves each saved report's `sandboxPath`, because short `r-...` directory names differ from full `verify-...` run IDs. Startup exceptions must return a nonzero CLI exit code; an exit-zero database-unavailable message is a failed test launch, never behavioral proof.

Use `reign_run_offline_verification` with the exact successful Release `validationRunId`, tier `quick`, confirmation `run isolated offline verification`, and suite `relationship_throughput_contracts` for focused correctness. `relationship_throughput_smoke` additionally compares five measured days; `relationship_throughput` measures 100 candidate days with independent two-second arrivals and fails the strict subsecond p95 budget. The isolated synthetic campaign contains 4,305 NPCs, 82,073 historical pairs and 650 groups/day. Retain `relationship-throughput/inputs.json`, its SHA-256, `original.json`, `candidate-equivalence.json`, `candidate.json`, per-route samples/referenced-state diagnostics and `report.json`; read actual evaluation counts rather than assuming groups equal pairs. Cleanup removes only the newly created replay campaign. These suites cannot start a listener or consume native/provider calls. Synthetic evidence does not replace captured native acceptance or the 126-day soak.

The September 13 follow-up is tracked in [the implementation record](../RELATIONSHIP_THROUGHPUT_IMPLEMENTATION.md). The compact `view=readiness` response bypasses historical-overview caching; eight observations at least two seconds apart remain required. `blockedObservationMs` contains overlapping sampled intervals, not additive critical-path durations. Queue `received_ts` is now the first accepted timestamp and is retained on retransmission. Existing rows are not evidence of an earlier timestamp than they already store. Observation-required native targets must receive an actual native receipt before clearing legacy flags. The database preparation script defaults to read-only Plan and is a separate, explicitly gated deployment stage.

The existing server operational log emits `relationships.day_completed` with schema `reign_relationship_day_performance_v1` after a durable relationship day completes. It includes campaign/timeline/day, the server module build identity, pair count, worker duration, phase timings, scoped standing/observer/player query counts, and fling preparation, profile-document and roll-write counters. Capture these bounded records alongside the exact validation artifact/hash receipt and the campaign-test report. Logs survive restart subject to normal bounded-log retention; the worker status still describes only its last in-memory day.

`workerSeconds` covers input loading, campaign-lock wait and snapshot processing. `independentLifecycleMs` is separate from `pairProcessingMs`. `flingProcessingMs`, `finalFlushMs`, court popularity and native counting are subdivisions of `finalizationMs`; `flings.preparationMs` is nested inside fling processing. Do not sum nested phases. Relationship processing time, input queue wait, the wider quiescence gate and checkpoint saving are separate measurements.

The relationship boundary self-tests compare scoped standing against the uncached observer calculation (including Favoring and native player direction), exercise mutation invalidation and disposal, compare indexed co-presence against the group scan, verify active-campaign/maintenance ranking, and preserve fling eligibility, profile precedence, roll-before-encounter ordering and replay of quiet groups. Successful offline contracts establish behavior for those fixtures, not native throughput.

Mutation checks must verify query counts for all participating heroes after a write, including roster/player changes, previously missing rows becoming present and nested contexts invalidating their outer scope. Testing only the first refreshed subject missed a native regression where a cleared context fell back to thousands of point reads. Facts must refresh immediately while retaining a batched reload of the participant set.

The canonical relationship facet/boundary explicitly runs `RunWorldRelationshipModelSelfTests`, so observer equivalence, bounded reads, invalidation and scheduler contracts cannot be omitted by selecting only the relationship module. Lifecycle command reuse must preserve immediate visibility, complete persisted rows, deterministic roll receipts and rollback; `prepared_lifecycle_matches_serial_trajectory` and `prepared_lifecycle_reuses_command_and_rolls_back` exercise those requirements. The `baselines` offline suite also includes the world-model contracts.

`roster_batch_matches_complete_serial_rows` compares every persisted column for 128 diverse NPCs against the original individual roster writer and verifies rollback. Native identity synchronization still preserves departing implicit knowledge before replacing the roster inside the existing transaction; only its insert transport is batched. This removes another source of relationship transaction waits without changing who knows whom.

For performance acceptance, deploy the exact canonical validated artifact, reopen the existing enrolled disposable Current checkpoint, and advance through the guarded campaign tools. Retain the immutable baseline. Compare the same checkpoint/day when possible, then measure several consecutive fresh days and their queue waits under accelerated native time. Stop paused and caught up; save only after the full quiescence gate. Report pair/group counts, measured distribution, build identity and wider-gate time separately. A replay from the same save is comparable but is not identical input unless its input hashes match. A normal-speed burst cannot establish a faster arrival budget. Input `received_ts` can be refreshed by retransmission; negative differences from `started_ts` are not valid queue-age measurements. All existing provider, enrollment, save capacity, runtime lifetime and cleanup gates remain in force.

## Discover before operating

- Read `AGENTS.md` and `reign.modules.json`.
- Query `reign_get_testing_catalog` and the affected feature manifest.
- Inspect `reign_get_workspace_status`, `reign_get_status`, and `reign_get_live_test_status` before starting processes or runs.
- Use bounded MCP world, log, audit, correlation, and readiness tools instead of downloading large raw HTTP responses.
- Preserve the user’s unrelated worktree changes and original saves.

## Repository hygiene evidence

`reign_validate` reads `reign.repository.json` and writes `repository-hygiene-report.json` beside the validation report. The policy schema is `reign-repository-policy-v1`; the report records normalized paths and classifier IDs but never matched secret values. Protected saves, databases, logs, deployments, backups, decompiled trees, and other runtime material are classified by path rather than inspected.

The bootstrap sequence is `audit` then `enforce`. Audit mode exposes issues without changing overall validation success; enforce mode blocks before project builds when required recovery files are untracked, human-authored files under managed roots are untracked, prohibited/runtime paths are tracked, a tracked file exceeds `26214400` bytes without allowlisting, or a tracked path/content matches a secret-risk classifier. Never fix a failure by weakening a protected category. Intentionally track authored work, preserve exclusions for private/generated state, or add a narrowly reviewed large-file allowlist entry.

For important commits and pushes, require an enforce-mode report with zero issues. The hygiene report is part of canonical completion evidence, and passing compilation does not override it.

Validator Git children always receive an isolated redirected input stream that is explicitly closed before the process wait. This prevents validation invoked over MCP stdio from inheriting the JSON-RPC input handle and hanging; use the documented validator fallback only while repairing or recovering MCP transport.

## Capability maintenance

`contracts.campaign_provider_wait` uses the isolated Verification Lab `contracts` suite and real Save Sync registration on a uniquely named test campaign. Controlled events hold a provider response and a duplicate character/turn request while registration completes. Restore variants prove both late success and late failure are rejected, including attempted file and SQL writes from a broad catch; ordinary provider failures reacquire their read lease and may retain failure evidence. No paid provider, native game or installed runtime is used. The production release scope is API character construction/social-event calls and their work-lock waits; background/write owners, other request types and the Codex conversation-state adapter retain existing exclusion. Any campaign restore/import/deletion conservatively invalidates suspended HTTP requests in the process, while snapshot creation does not. Cleanup removes only the exact run-owned fixture campaign and its snapshots.

Any change to a tool, command, feature profile, scenario, confirmation phrase, save convention, evidence schema, or cleanup route must update `reign.testing.json`, this guide, relevant help/security documentation, and catalog contract tests. New files must be owned by `reign.modules.json`. Completion requires the manifest-selected `reign_validate` report plus applicable harness evidence and an explicit statement of remaining native or human gaps.

Government certification retries preserve failure evidence. Starting an already-recorded case instance creates a fresh bounded `-attempt-N` live-run ID, and release readiness evaluates the newest execution first without deleting or overwriting earlier reports.

Government soak recovery preserves completed advancement evidence across process restarts. Retain the passed `GOV-NATIVE-030` live-run report and its exact feature fingerprint; when Bannerlord, the client, or Codex restarts before `GOV-NATIVE-031`, pass that report id as `soakPreparationLiveRunId`. The MCP requires a completed preparation assertion and exact fingerprint match, then rehydrates only the bounded start day and collection counts into `soak_verify`. It must never substitute a new preparation snapshot or repeat the 210-day soak.

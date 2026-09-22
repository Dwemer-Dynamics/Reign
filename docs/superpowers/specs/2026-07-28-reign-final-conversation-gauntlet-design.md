# Reign Final Conversation Gauntlet Design

**Date:** 2026-07-28

**Status:** Approved design
**Scope:** A permanent exhaustive validation catalog with a risk-weighted live covering set capped at 500 external provider calls.

## Objective

Validate that Reign NPCs:

1. Use authoritative world information and respect knowledge boundaries.
2. Retain identity, personality, history, and emotional continuity.
3. Understand time, place, presence, mode, and current events.
4. Remember appropriate information without ownership or privacy leakage.
5. Act according to personality, relationships, culture, status, goals, and self-interest.
6. Select, validate, resolve, and execute actions correctly and exactly once.
7. Produce natural, specific dialogue without exposing system mechanics.
8. Change when relevant state changes and remain stable when irrelevant state changes.

The catalog remains exhaustive. Provider-backed execution is deliberately representative: deterministic and replayable checks cover every generated row, while a risk-weighted covering set sends only cases that require fresh behavioral evidence through the production LLM.

The complete unattended program, including qualification gaps, final scenes, long-horizon sequences, memory summaries, auxiliary model work, and retries, must never exceed 500 external provider requests.

## Non-Goals

- Exact matching against one canonical NPC sentence.
- Sending every action-by-requirement or pairwise row to the LLM.
- Automatic production-code repair during the final one-pass run.
- Treating a semantic judge as authoritative over hard evidence.
- Claiming that 10-15 qualitative assertions prove a 99.5% population rate.
- Requiring a human reviewer while the automated run executes.

## Existing Foundations

Extend rather than replace:

- The Live Interaction Bridge and production conversation adapters.
- The production prompt builder, retrieval pipeline, parser, relationship adjudicator, action router, resolver, validator, executor, memory writer, and persistence path.
- Conversation Readiness evidence and blinded review support.
- Replay corpus and correlation-linked audit records.
- Verification Lab deterministic registry, shadow-world, provider-fault, and action checks.
- Save Sync, lifecycle, checkpoint, and BLSE continuation controls.
- Existing temporary qualification fixtures and restoration manifests.

## Two-Stage Program

### Stage A: remaining qualification gaps

Stage A consumes only missing or incompatible proof from the existing readiness ledger. Already passing same-build evidence is not regenerated.

- Maximum external provider calls: 40.
- Expected shape: up to 20 single-speaker response/summary pairs, or an equivalent mix containing group cases.
- Failures are recorded non-fail-fast.
- Stage B cannot begin until Stage A passes.
- When no proof is missing, Stage A consumes zero calls.

### Stage B: final one-pass review

Stage B executes the complete deterministic catalog and the approved live covering set.

- Every deterministic catalog row executes once.
- Every selected live scenario executes once.
- Every named final scene executes once.
- A failed assertion never stops the remaining catalog.
- Failed scenarios are not automatically rerun.
- Provider recovery remains internal to the original scenario and is bounded to three correlated attempts.
- No production code changes occur during the run.
- The run terminates as `completed` or `completed_with_failures`.
- User and Codex review the report and human-review pack before later repairs.

## Hard Provider Budget

Every outbound request to the configured external LLM requires an atomic run-budget reservation.

| Work | Maximum planned calls |
|---|---:|
| Remaining Stage A qualification gaps | 40 |
| Representative atomic and matrix pack | 120 |
| Twenty named final scenes | 60 |
| `LNG-001`, one NPC across 100 exchanges | 110 |
| `LNG-002`, twenty-NPC court soak | 44 |
| Auxiliary work and retry reserve | 126 |
| **Absolute run maximum** | **500** |

The budget counts:

- NPC responses.
- Scene and memory summaries.
- Action-router and action-repair requests.
- Shared Relationship History generation.
- Structured-output repair attempts.
- Provider retries.
- Any other request routed to the configured external provider.

The budget does not count deterministic retrieval, FTS queries, vector lookup, local embedding work, RNG-only tests, or non-generative bookkeeping. Local MiniMe work is reported separately.

Rules:

- Reserve a call atomically immediately before provider dispatch.
- Persist run, case, purpose, correlation, attempt, provider, model, timestamp, and outcome for every reservation.
- Reconcile a durable correlation before retrying.
- Permit at most three total attempts for one provider operation.
- Never dispatch call 501.
- Reaching the limit produces `budget_exhausted` and an incomplete run, never a pass.
- Unused Stage A capacity becomes reserve; it does not authorize redundant cases.

## Exhaustive Catalog and Coverage Classes

The runtime catalog continues to discover:

- Actions and action families.
- Personality and Court traits.
- Cultures.
- Conversation modes.
- Occupations and social roles.
- Resolver target types.
- Rumors and reputations.
- Fixture adapters and lifecycle capabilities.

Every catalog row belongs to one execution class:

1. **Deterministic exhaustive:** parsing, schema, resolver, validator, eligibility, idempotency, persistence, rollback, probability, catalog completeness, prompt construction, and state mutation.
2. **Replay-backed:** production parser, validator, resolver, persistence, and evaluator behavior rerun from immutable evidence without another provider request.
3. **Fresh live semantic:** cases where NPC judgment, voice, social behavior, memory use, or natural language action selection requires a new provider response.
4. **Long-horizon live:** the dedicated 100-exchange and twenty-NPC sequences.
5. **Human review:** blinded representative outputs selected after execution without additional generation.

Every original identifier remains mapped:

- `CON-001` through `CON-016`.
- `IDN-001` through `IDN-016`.
- `SIT-001` through `SIT-020`.
- `WLD-001` through `WLD-022`.
- `PER-001` through `PER-024`.
- `REL-001` through `REL-025`.
- `ROM-001` through `ROM-015`.
- `MEM-001` through `MEM-040`.
- `GRP-001` through `GRP-028`.
- `RUM-001` through `RUM-028`.
- `ACT-001` through `ACT-035`.
- `STA-001` through `STA-012`.
- `MOD-001` through `MOD-016`.
- `SOC-001` through `SOC-018`.
- `ADV-001` through `ADV-018`.
- `ROB-001` through `ROB-024`.
- `DIF-001` through `DIF-020`.
- `STO-001` through `STO-010`.
- `LNG-001` through `LNG-020`.
- Final Gauntlet scenes 1 through 20.

A requirement may map to a dense live fixture shared with other requirements only when it has its own assertion, evidence pointer, and denominator. Coverage cannot be inferred from a neighboring assertion.

## Risk-Weighted Live Covering Set

The selector minimizes external calls while maximizing uncovered behavioral risk.

Priority order:

1. Previously failing, unqualified, or stale readiness areas.
2. Identity, authoritative world state, privacy, ownership, and exactly-once guarantees.
3. Group awareness and speaker-specific knowledge.
4. Long-term memory, Dynamic Characteristics, and Shared Relationship History.
5. Manipulation, clan recognition, lies, and relationship consequences.
6. Personality differentiation, agency, and factual specificity.
7. Mode, culture, relationship, status, location, time, and witness contrasts.

The representative atomic/matrix pack normally contains:

- 40 single-speaker fixtures, budgeted as one response and one scene summary each.
- 10 three-NPC party or event fixtures, budgeted as three sequential replies and one shared summary each.

These 50 dense fixtures provide 10-15 independent assertions per major qualitative area. They are not required to provide 10-15 separate conversations per area.

### Action coverage

- All registered actions receive exhaustive deterministic success, rejection, resolution, eligibility, idempotency, persistence, and rollback checks.
- Approximately 10-15 live cases span diplomacy, strategy, politics, movement, violence, transfers, employment, clan/kingdom membership, and compound trade.
- Live cases cover direct, indirect, paraphrased, ambiguous, ineligible, and failure-aware phrasing.
- Purely mechanical action rows never consume provider calls.

### Pairwise and personality coverage

- The full pairwise matrix and complete male/female Honor x Boldness checkerboard remain exhaustive offline.
- Live cases use a covering array spanning trait extremes and center, sex, culture, status difference, wealth, privacy, relationship band, political state, family context, memory state, and stress.
- Changing one variable retains explicit invariants for all unrelated facts and state.

### Probability coverage

- Seeded boundary tests execute below, at, and above each threshold.
- Large statistical samples are RNG-only.
- No LLM call is spent estimating configured mechanical probabilities.

## Fixture and Evidence Contract

Fixtures use the production path and contain:

- Stable fixture, sequence, scene, exchange, turn, and correlation IDs.
- Category, mode, seed, fixture version, and evaluator types.
- Authoritative time, location, room, political state, present characters, and entities.
- Speaker/player identities, roles, traits, relationships, family, goals, conditions, and knowledge permissions.
- Retrieved history, Dynamic Characteristics, memories, rumors, reputations, summaries, and source lineage.
- Player input.
- Hard requirements and prohibited content.
- Permitted actions and expected/prohibited mutations.
- Differential invariants, sequence dependencies, and statistical thresholds.
- Preparation and restoration instructions.
- Estimated and actual provider-call purposes.

Each fresh model-backed case captures:

- Entire outbound prompt and section budgets.
- Every retrieval candidate and selected evidence.
- Raw and parsed response.
- Provider/model configuration, token usage, latency, retries, and errors.
- Action routing, resolution, validation, and execution evidence.
- State before and after.
- Memory, summary, Dynamic Characteristic, rumor, reputation, relationship, benefit, and promise changes.
- Seeds and probability rolls.
- Code, prompt, catalog, fixture, evaluator, and state fingerprints.

Failures enter the replay corpus automatically.

## Long-Horizon Sequences

### LNG-001: one NPC, 100 exchanges

- Ten scenes of ten exchanges.
- One focal NPC throughout.
- Controlled introduction of names, numbers, locations, preferences, minor details, promises, secrets, hostility, corrections, reconciliation, and Dynamic Characteristics.
- Recall before and after displacement beyond the recent raw-history window.
- Ten production scene summaries.
- Save/reload, consolidation, rolling arcs, FTS, and semantic retrieval checks.
- Planned provider budget: 110.

### LNG-002: twenty-NPC court soak

- Immutable roster of twenty eligible adults.
- Four public groups of five NPCs.
- Each group receives distinct information.
- All twenty group replies are sequential within their group.
- Four production group summaries.
- Ten later private recall probes and ten private summaries.
- Assertions cover identity separation, participant/witness ownership, cross-group leakage, personality contamination, relationship targeting, relevance, and repetitive language.
- Planned provider budget: 44.

Other `LNG` requirements use deterministic campaign-state mutation, synthetic high-volume history, existing same-build evidence, or assertions embedded in these two sequences. They do not each create another long live run.

## Twenty Named Final Scenes

All twenty supplied scenes run exactly once:

- Ten use single-speaker, Court, or correspondence flows.
- Ten use three-NPC party/social-event flows unless the scenario requires a specific larger roster.
- Only required speakers generate.
- Each scene carries multiple independently graded requirements.
- The planned budget is 60 calls, including production summaries.
- Action-router or repair calls consume auxiliary reserve.

## Evaluation and Pass Conditions

Hard assertions are authoritative and cover schema, identity, current state, knowledge ownership, witnesses, resolution, validation, exactly-once execution, state mutation, save/reload, and restart behavior.

Semantic review scores:

- Factual grounding.
- Epistemic realism.
- Character fidelity.
- Social awareness.
- Emotional continuity.
- Agency.
- Situational embodiment.
- Memory integration.
- Responsiveness.
- Linguistic naturalness.
- Specificity.
- Non-repetition.

Differential checks require the intended change while preserving named invariants. Sequence checks grade continuity and persistence. Statistical checks use deterministic or RNG-only evidence.

Pass requirements:

- 100% deterministic catalog completion.
- 100% schema, identity, ownership, privacy, lineage, resolver, validator, idempotency, and state-integrity assertions.
- Zero critical failures.
- At least 95% success in qualitative categories.
- Every semantic requirement mapped to fresh compatible live evidence.
- No provider operation exhausted all three attempts.
- Completion within 500 external provider calls.
- No role-play rubric dimension below acceptable in the representative review pack.

## Human Review Pack

Generate a blinded 10-12-scene pack without new LLM calls. Selection spans modes, roles, cultures, traits, relationships, privacy, group sizes, and pass/borderline/failure outcomes.

The reviewer sees natural context, necessary prior dialogue, player input, NPC output, and the twelve scoring dimensions. Mechanical traits, expected verdicts, automatic scores, internal IDs, and case status remain hidden.

A separate answer key links each scene to its fixture and evidence. No fix or rerun begins until user and Codex review the completed pack.

## Reporting

Reports include:

- Planned, reserved, completed, recovered, failed, and remaining provider-call counts.
- Actual call counts by case and purpose.
- Deterministic, replayed, and fresh-live coverage separately.
- Requirement-to-evidence traceability.
- Every failed, blocked, skipped, provider-exhausted, and budget-exhausted case.
- Prompt, token, retrieval, latency, action, memory, relationship, save/reload, and persistence evidence.
- Root-cause clusters and recommendations without automatic code changes.
- Human-review pack and private evidence key.

## Acceptance

The redesigned runner is ready when:

- The catalog remains exhaustive and fails closed on uncovered enabled systems.
- The coverage selector maps every semantic requirement to fresh live evidence.
- Deterministic action and pairwise matrices no longer create one provider call per generated row.
- The exact 500-call ledger is durable, atomic, correlation-safe, and enforced before dispatch.
- Stage A uses only missing compatible proof and cannot exceed 40 calls.
- Stage B cannot begin until Stage A passes.
- `LNG-001`, `LNG-002`, and all twenty final scenes retain their defined live behavior.
- Provider retries remain bounded to three attempts and consume the same budget.
- The runner completes remaining cases after failures and stops for user-guided review.

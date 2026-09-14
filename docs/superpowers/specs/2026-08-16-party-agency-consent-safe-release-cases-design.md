# Party Agency Consent-Safe Release Cases

Date: 2026-08-16

## Context

The autonomous Temporary Noble Party Guest certification suite currently expects several positive natural-language cases to create an active guest agreement after a short invitation. Native testing on the guarded `BaseTest` disposable copy showed that three eligible nobles independently treated the first invitation as underspecified. Each asked what danger was involved before deciding, and no guest action was emitted. This is correct production behavior because negotiation, curiosity, or “I am not saying no” is not explicit consent.

The release suite must prove that ordinary player language can create guest agreements while preserving the rule that only an NPC's explicit agreement authorizes the action.

## Decision

Positive invitation cases will use complete, natural player invitations. Each invitation will state:

- the narrative purpose;
- the known facts and likely danger;
- why that NPC's help is being requested;
- the exact fixed term or five-day open-ended review arrangement;
- that the NPC is being asked rather than ordered; and
- a direct request for explicit agreement.

The dialogue remains production dialogue. The harness will not invoke a guest action directly, supply a hidden consent flag, instruct the NPC to accept, or reinterpret conditional language as consent.

Cases that intentionally test hypotheticals, ambiguity, negation, correction, withdrawal, refusal, renewal, departure, and hostility warnings retain their distinct language purpose. Their expected outcomes will not be weakened merely to make the suite pass.

The case scope is explicit:

- `PA-LANG-001` through `PA-LANG-008` and `PA-LANG-013` receive complete invitation language;
- `PA-LANG-009` through `PA-LANG-012` remain the false-positive consent controls; and
- `PA-LANG-014` through `PA-LANG-018` remain unchanged in this redesign because their targeted review, renewal, departure, and hostility contexts already establish the situation.

## Case Behavior

For a positive invitation case, success requires all of the following evidence:

1. The exact natural player utterance is recorded.
2. The complete NPC response is recorded.
3. The NPC response explicitly agrees to the proposed temporary arrangement.
4. The provider produces the corresponding validated Party Agency action through the production dialogue pipeline.
5. Native post-state contains the expected guest record and lifecycle phase.
6. Clan, kingdom, family, rulership, companion, and ownership identity remains unchanged.
7. The report proves that no direct harness action made the guest decision.

A refusal or request for clarification remains a safe non-consent outcome. It must not create a guest record. For a case intended to prove successful joining, the autonomous controller may retry the same logical case with a different eligible NPC, but every attempt remains durable evidence and the passing attempt must contain explicit consent. Failed or refused attempts are never rewritten as passes.

## Target Selection

The controller will select real, state-eligible nobles from the live target and Party Agency candidate evidence. It will favor candidates whose current circumstances make the requested trip plausible, while still covering the manifest-owned partyless, party-member, party-leader, army, clan-leader, and ruler variants elsewhere in the native matrix.

Each positive invitation uses a target who is not already an active guest. Review, renewal, departure, and hostility cases use the guest record prepared by their preceding natural-language lifecycle setup. The harness must not silently reuse an incompatible target or bypass the required setup.

## Catalog and Documentation Contract

Because changing case utterances changes test behavior, the same change will update:

- `ReignBetaServer/ReignLiveTest/scenarios/party-agency-manifest.json`;
- `reign.testing.json`, advancing its catalog version from `2026.08.15.10` to `2026.08.16.1`;
- `docs/agent/TESTING_TOOL_GUIDE.md`;
- the Party Agency help text in `ReignBetaServer/ReignLiveTest/Program.cs`; and
- catalog and harness contract tests.

The prepared certification run is immutable. After validation and deployment, testing will start a new Party Agency certification run bound to the new source, client, server, provider, catalog, and Bannerlord fingerprints. Earlier failed reports remain diagnostic evidence but cannot satisfy the new fingerprint's release gate.

## Failure Handling

- Conditional, ambiguous, or negotiating replies produce no action and no guest record.
- Provider failure remains neither consent nor refusal.
- A positive case that receives a safe refusal may be retried with another eligible NPC, but the release gate requires a final explicit-consent pass.
- A native mismatch after explicit consent is a product defect, not a reason to relax the expected result.
- Every defect is repaired regression-first, validated through the manifest-selected Reign profile, deployed from validated artifacts, and rerun on a fresh immutable certification fingerprint.
- The original `BaseTest` save remains protected. All mutations occur only on campaign-test-owned disposable saves.

## Verification

The change is complete only when:

- catalog contract tests prove positive cases contain purpose, practical context, term/review language, and an explicit agreement request;
- negative and refusal cases still prove zero false-positive consent;
- manifest-selected validation and repository hygiene pass;
- the installed manifest and client/controller artifacts match the validated outputs;
- production dialogue reports show explicit NPC consent before each successful action;
- all 60 Party Agency case variants pass on one final fingerprint; and
- `reign_evaluate_party_agency_release_readiness` returns a green release verdict with durable evidence paths.

## Non-Goals

This design does not change NPC personality, relationship logic, native traits, reputation, rumors, moral departures, imprisonment behavior, or the production definition of consent. It changes only the autonomous release dialogue and its test contract so the suite behaves like a reasonably informative player.

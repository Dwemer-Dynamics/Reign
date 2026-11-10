# View scene clothing override — consultation and implementation proposal

## Per-character coverage repair — 2026-11-01

The user approved fixing generic clothing coverage for every depicted character, building/validating it, and stopping **ready to deploy**. This is a new deployment hold; the previously installed wardrobe-v1 build remains unchanged. No installed server/game startup, image regeneration or live provider test is authorized in this pass.

New jobs freeze `chat-scene-clothing-coverage-v1` and source-verified nomination clues from the four-exchange window plus admitted older context. The selector must return exactly one `wardrobeCoverage` decision per depicted actor: validated changes, explained portrait default, or uncertainty. Every applicable clue must be accounted for by an overlapping admitted change, compatible later replacement or exact contextual dismissal. A bare default cannot silently drop a known clue. Unknown/missing/duplicate actors, contradictions and uncertainty stop before image generation. Quotes that span the selected instant retain their earlier prefix without allowing later clothing to leak backward. The final prompt consumer revalidates the plan and emits controlled wardrobe directions for all mapped actors; reasons/proofs remain internal, with immutable face/apparent-age/hair/physique authority unchanged.

The clue list is deterministic and bounded to256 items within the existing180000-character input limit. No new schema, ordinary dialogue-prefix growth or extra provider call is introduced. New revision/evidence changes job identity, while saved images and old frozen inputs/jobs retain their original contract. Structural/source checks do not prove semantic interpretation or image obedience; missing/legacy nomination and fresh-session history limitations remain. Tests use synthetic nonsexual training-clothes scenes.

Coverage repair validation is complete: changed/Release Tier3 `20261101-052451-90769c70` passed all8operations in959.095s, with2470MCPtests,75portrait-tooltests,117/117regressiongroups and clean enforced paired hygiene. Focused135/135View checks ran on the byte-identical final validation DLL. Ordinary-dialogue prefixes remain37678/44935characters within the existing45000cap. Linux component `20261101-054231-f66dc08a` passed in66.474s and is retained without activation. Exact profiles, fingerprints, report/hygiene paths and hash verification are recorded in `.codex-build/chat-view-coverage-01a18ad4/RESULT.md`. **Ready for deployment; do not deploy until the user authorizes it. No installed server or game was started.**


2026-10-30. The user reports View is working very well and requests a clothing override: when the chat establishes different clothing for the depicted scene, send those instructions to the image model. Their example is two characters agreeing to change into rougher clothes for sparring; the resulting scene must not keep their formal portrait outfits. Preserve portrait apparent age and identity. The initial implementation/build approval included a deployment hold. The user's explicit **Deploy** instruction on 2026-10-31 lifted that hold; the validated server-only artifact is now installed, with the server, worker, game and launcher left stopped.

## Deployment — 2026-10-31

Retained Linux build `20261031-021317-6bce66f7` is installed as runtime `20261031150209-24825`. All 1,375 runtime files and ten reviewed source files match their validated hashes; prior source/runtime versions are retained. Settings, Windows installation identity, client DLL, shared portrait inventory and bridge are unchanged. No rebuild, paid provider request or game/server startup was performed.

The canonical entry point initially selected content from the main checkout, where an unrelated worker edit differs from the validated worktree. Its content guard stopped before activation. The same canonical content guard passed with the correct worktree source root (1,141 content files, 1,375 artifact files and 221 classified embedded prompts); the already installed, verified managed `reignctl` then activated that exact artifact with `--leave-stopped`. The unrelated main-checkout edit was preserved and excluded. This is documented recovery, not a successful end-to-end run of the PowerShell entry point. Exact hashes, fingerprints, backup paths and verification receipts are in `.codex-build/chat-view-wardrobe-01a18ad4/DEPLOYMENT.md`.

Health, worker warm-up, stop/start acceptance, actual model interpretation, generated-photo fidelity and native UI acceptance remain deferred. History/session and nomination coverage limits below still apply.

## Implementation and validation

The implemented `chat-scene-wardrobe-v1` uses one `wardrobeOverrides` entry per depicted character with a bounded `changes` array. Each change has scope, operation, original garment phrase, establishment basis and exact source proofs. The server derives UTF-16 offsets from unique quotations or an explicit occurrence rather than asking the model to count characters. Only controlled clothing data reaches the final image prompt; proofs remain in the receipt. Portrait face, apparent age, hair, skin and physique always retain priority.

The ordinary dialogue writer nominates source-linked `viewWardrobeEvidence` within its existing call. `chat_view_wardrobe_evidence` commits alongside accepted sources and records exact quote/hash checks or explicit rejection reasons without changing gameplay. Earlier same-session clothing context preserves original player text and the actual paired NPC answer, including refusal, and follows timeline, scene and actor-continuity guards. It does not use the mutable hourly wardrobe cache. Four complete exchanges remain the only highlight candidates. Bounds fail before providers rather than silently truncate. Valid nominations are provenance, not semantic proof; a model can still omit or misinterpret clothing.

Final changed/Release Tier 3 `20261031-015715-8122497b` passed all eight operations in 873.678 seconds: 2,470 MCP tests, 75 portrait-tool tests and 117/117 isolated regression groups, including 101/101 View checks, with clean enforced paired hygiene. The focused 2,304/2,304 dialogue/cache checks ran on a DLL byte-identical to the final validation artifact. Initial validation caught a 45,491-character noble prefix exceeding the existing 45,000 limit; shortening the added writer guidance brought it to 44,935 without raising the cap. Canonical Linux component `20261031-021317-6bce66f7` passed in 60.242 seconds; 1,375 files are retained without installation. Exact profiles, fingerprints, artifact hashes, hygiene paths, source baselines and coverage limits are in `.codex-build/chat-view-wardrobe-01a18ad4/RESULT.md`. Read-only installed hash checks confirm the prior server/client remain unchanged. **Do not deploy until explicit user authorization.**

Limits remain explicit: histories predating nominations only have the latest four exchanges plus available retained evidence; a fresh Party Continue server transport is a separate session and does not import historical wardrobe facts. Source fixtures cannot establish real-model interpretation, generated-photo fidelity or native interaction. No paid provider test or runtime action is part of this pass. **Do not deploy until the user explicitly authorizes it.**

## Verified starting behavior

`ReignServer/src/Modules/Portraits/ChatViewAppearance.cs` currently forbids redressing, makes the entire portrait wardrobe authoritative, and tells the selector not to describe outfits. `ChatView.cs` appends that lock after the scene description. A plain clothing sentence therefore cannot reliably override it. The correct chat portrait resolver and age protection should remain.

The selected scene comes from four complete exchanges. Its source validator checks exact quotes, selected-exchange membership and cast presence. Exact quotes establish provenance, not semantic truth. Source turn IDs are opaque strings, so temporal comparisons must use retained order, never lexical ID comparisons. Multiple physical moments can occur in one NPC response; a turn boundary alone cannot prevent a later costume change from leaking backward.

Existing `ConversationSceneState.cs` tracks clothing and accepted updates, but `conversation_scene_state` is mutable per hero and the clothing override expires by world hour. `ConversationContinuity.cs` also drops expired clothing. Reading that latest row at image time is not a historical outfit snapshot. It must not silently replace earlier clothing, and ordinary native/equipment/formal-outfit fallbacks remain unsuitable for View.

## Recommended behavior

1. **Separate immutable identity from default wardrobe.** Portrait face, apparent age, hair, skin and physique always win. Portrait clothing is the default. A validated clothing-only change overrides only the affected character and garments at the selected moment. Never infer a costume from rank, age, native equipment or activity alone.
2. **Use the selected dialogue LLM's existing View call.** Return the selected physical instant plus optional structured `wardrobeOverrides`. Include character ID, whole-outfit versus named-garment scope, replace/remove operation, the original clothing phrase, establishment basis, and exact source spans. Keep scene action prose separate from clothing. No second model call by default and no keyword list as the semantic authority.
3. **Support the user's accepted transition.** Explicit current wearing or completed dressing is sufficient. An accepted plan can also establish the new clothes when the named participants agree and the same planned activity is actually underway afterward, with no intervening refusal or contradiction. Preserve both agreement and enacted-activity evidence. An unanswered, rejected, future, remembered, hypothetical or merely unrejected suggestion is insufficient. Do not require a redundant dressing sentence when the accepted transition is otherwise clear.
4. **Resolve participants precisely.** Expand “we/both” only to the established participants in that exchange, using speaker and character IDs. Do not restyle a bystander, confuse same-name characters, or treat one speaker's proposal as another character's agreement.
5. **Apply the smallest established change.** Removing a cloak leaves the remaining outfit. Replacing a shirt preserves other garments where compatible. A whole change into “rougher clothes” replaces the formal ensemble rather than adding training clothes over it. Keep the source wording; avoid invented colors, decorations or materials. Unspecified visual details should remain compatible with the portrait where possible rather than becoming purported chat facts.
6. **Bind everything to the chosen instant.** Use frozen source order and validated character spans for the highlight and clothing evidence. Reject future spans even within the same NPC response. Later established corrections before that instant supersede earlier clothes; changes afterward do not. Resolve ambiguous/repeated quotations with exact verified offsets or reject the ambiguous candidate.
7. **Carry established clothing beyond the four-exchange highlight window.** Retain versioned, source-linked wardrobe facts through the accepted scene-state flow and freeze them into View's historical frames. The four exchanges remain the only highlight candidates; a compact earlier wardrobe state supplies context without resending the whole chat or adding a generative call. Scope facts to campaign/timeline/conversation, actor and accepted source boundary. Invalidate on rollback, actual clothing replacement/removal or scene scope change. Do not reuse the mutable latest outfit row or copy its hourly-expiry behavior into historical image interpretation. Legacy histories without these facts need an explicit evidence-limited fallback; do not promise coverage by silently truncating older chat.
8. **Compose a controlled clothing section.** The image prompt receives the unchanged full-body portraits and map, scene action, and `VALIDATED CLOTHING AT THE SELECTED MOMENT`. The fixed priority is: portrait identity/apparent age; grounded clothing changes for the named character/garments; remaining portrait wardrobe. Remove the conflicting “never redress/same outfits” rule when introducing the new contract. Do not send free-form generated instructions or dialogue quotes as executable image directions; keep source proofs in the manifest. Reference portraits themselves remain unchanged.

Suggested selector shape, subject to the existing JSON conventions:

```json
{
  "selectedTurnId": "npc-turn-id",
  "selectedMoment": { "start": 0, "length": 20, "quote": "exact enacted moment" },
  "characterIds": ["character-id"],
  "description": "action and setting",
  "wardrobeOverrides": [
    {
      "characterId": "character-id",
      "scope": "outfit",
      "operation": "replace",
      "garmentText": "rougher clothes",
      "basis": "accepted_plan_then_activity",
      "evidence": [
        { "turnId": "source-turn-id", "start": 10, "length": 15, "quote": "exact source span" }
      ]
    }
  ]
}
```

This is a schema sketch, not a valid fixture. An accepted-plan candidate must provide the distinct proposal, participant acceptance and enacted-activity support; the sketch omits those full strings for brevity. Bound collection/string sizes, reject duplicate/unknown IDs and unsupported fields, verify every span/hash/visibility boundary, and record rejected candidates with reasons. Exact-span validation is not a semantic certificate. Ambiguous clothing retains the portrait default; an invalid selector response must fail before an image request rather than trigger automatic provider retries.

## Integration and verification

Likely owning paths are `ChatViewAppearance.cs`, `ChatView.cs`, `ChatViewSources.cs` and a small wardrobe helper/test file under Portraits, with a proportionate accepted-scene-state integration for durable source-linked facts. Do not redesign the general NPC writer or alter unrelated memory admission. Preserve the selected image model, native maximum 3:2 size, chat UI, cache writer and Memories Book behavior.

Version the clothing contract, freeze the accepted wardrobe/proofs and contract revision in job identity/receipts, and migrate only exact shipped prompt defaults. Preserve custom styles, always enforcing the immutable apparent-age rule. Old jobs/results remain historical; do not reinterpret frozen pending jobs or automatically regenerate images. Update the testing catalog, owning dialogue/provider procedure and applicable help/security contracts when implementing the new schema/evidence.

Obtain a fresh exact-path manifest plan before code edits and run its lowest selected tier, including affected persistence/consumer boundaries. Reuse unaffected UI evidence; no UI edit is proposed. Required provider-free cases must exercise source → selector result → admission → final image prompt, including:

- No clothing mention: complete portrait wardrobe and apparent age remain.
- Mutual rough-clothes agreement followed by sparring: only those two participants change.
- Unanswered/refused proposal, conditional/future/recalled changes: no change.
- Named garment replacement/removal versus full outfit: preserve the rest; do not layer removed/replaced formal clothes underneath.
- Correction before the highlight versus after it, including two moments inside one response.
- Party pronouns, third-person references, bystanders and same-name people.
- Duplicate/unknown actor IDs, fabricated quote/offset/hash, invalid fields and injected style/age instructions.
- Clothing established more than four exchanges earlier; rollback, timeline/session and scene-scope isolation.
- Job deduplication/revision, exact-default migration, custom styles and unchanged historical book entries.

Model interpretation and generated-image fidelity require separate source-linked acceptance. Offline contracts cannot prove that an LLM correctly inferred an agreement or that the image model obeyed it. No paid image request, native launch, runtime change or deployment occurred for this consultation.

## Consultation assessment

All three completed selected-text, read-only reviews. They had no project access and ran no project tests. Inputs were 3,236 UTF-16 units; Grok's full prompt was 3,873 and Gemini's 4,012, both within their preflight limits. Grok used the required medium-v3 bridge and medium reasoning. Exact inputs, budgets and responses are retained at `.codex-build/chat-view-wardrobe-01a18ad4/consultations.json`.

- **Claude:** recommended separate per-character wardrobe data, cast/evidence/time checks, immutable identity, partial changes and an accepted-plan-plus-enacted-activity rule. Adopt this rule for the user's example. Its literal garment-token check alone is too brittle to establish semantic correctness; retain original phrases and source proofs instead.
- **Grok:** recommended strict per-character, source-bound changes and immutable identity, but would reject accepted plans followed by sparring without a separate dressing sentence. That would miss the user's stated use case; retain its proposal/recollection safeguards without imposing that restriction.
- **Gemini:** recommended the same identity/wardrobe split and partial-change handling, but would allow an unrejected proposal followed by the activity. Require affirmative agreement or explicit established wearing rather than treating silence as acceptance.

Additional source-review findings are the mutable/hourly scene-state limitation and within-response timing. These are implementation requirements, not claims that the consultants verified code or a live image.

# Court audience scene generation — combined fix plan

Date: 2026-09-09. Status: combined implementation built and validated, ready for coordinated deployment when requested. No deployment has been performed by this task. Native/provider artwork acceptance remains pending. The original investigation and requirements below are retained as history.

## User requirements

- Generate the court scene when multiple courtiers are involved, as ordinary individual petitions already do.
- Include the previously prepared culture fix in the same eventual update: the hall must follow the town hosting the audience, including an Imperial hall when Battanian nobles visit an Imperial town.
- **Send exactly one combined reference image containing ALL courtiers involved as the only image input to ONE scene-generation call.** Do not attach individual portrait images, submit separate images per courtier, or generate separate court scenes per person. The purpose is to avoid unnecessary image costs.
- Include the approved host-town hall reference inside that same composite image. Participant names/IDs and slot descriptions may be ordinary metadata/text; they are not additional image inputs.
- A four-card interface or active-speaker limit must not silently truncate the full physically present, involved cast in the composite. Keep off-scene available attendees out of the image; the ruler remains outside the image under the existing seated first-person viewpoint contract.

The authoritative completion checkbox is the corresponding Inbox entry in [REIGN_ROADMAP.md](../../REIGN_ROADMAP.md). Keep it open through implementation, verification, and native acceptance.

## Confirmed cause

The implementation split is by audience type, not a provider rule rejecting multiple people.

| Audience path | Current behavior | Source evidence |
| --- | --- | --- |
| Ordinary resource/relief petition | Starts opening dialogue and `PrepareSceneAsync(petitioner)` independently, loads/persists generated scene artwork | `ReignBeta/src/Modules/Court/UI/ViewModels/ReignCourtPetitionScreenVM.cs`, constructor lines 60–111 and `PrepareSceneAsync` |
| Existing petition image client | Accepts exactly one `Hero`, composes one portrait with a hall reference, submits one `contactSheetBase64` image and one participant ID; prompt explicitly says only one visible principal | `ReignBeta/src/Modules/Court/Integration/ReignRulerPetitionSceneClient.cs`, lines 28–98 |
| Noble docket, including disputes with several nobles | Loads only `BuildCourtPetitionReferenceImageId`; no image-generator invocation. Its readiness reports completion immediately and regards the reference image as ready | `ReignBeta/src/Modules/Court/UI/ViewModels/ReignCourtNobleMatterScreenVM.cs`, lines 68–69 and 199–201 |
| Court Life: international, family, patronage and visitor audiences | Loads the reference hall; `EventImageId` is get-only; no generated-art preparation call | `ReignBeta/src/Modules/Court/UI/ViewModels/ReignCourtLifeScreenVM.cs`, lines 41–48 and 65 |
| International `EnsureInternationalSceneAsync` | Obtains a structured written account from `/court/life/scene` and persists `sharedScene`; it does not generate pixels | Same Court Life view model, lines 268–301 |

The earlier [AI artwork/dialogue audit](AI_ART_DIALOGUE_FLOW_AUDIT_2026-09-06.md) independently records noble and Court Life audiences as reference-art-only. Both `ReignCourtLifeMatter` and `ReignNobleDocketMatter` already have `SceneAssetPath` storage; having that field is not evidence that those screens generate or restore artwork.

Current MCP observation on 2026-09-09 at 22:49 UTC found the visible Reign server healthy on port 5101 and no active/queued/failed background work. No new provider call or normal-campaign replay was needed to establish this source-routing gap. This is a source diagnosis, not a new native/model acceptance result.

## Preserve the earlier culture correction

Keep these existing source changes when implementing this bundle:

- `ReignCourtLifeScreenVM` resolves `Settlement.Find(matter.SettlementId ?? string.Empty)` and uses that settlement's culture, with `generic` when unresolved. Do not revert to `hero.Culture`.
- Keep the existing `CourtLifeHallArtUsesHostSettlementAndNeverVisitorCulture` regression in `ReignMcp/tests/Reign.Mcp.Tests/Features/Court/RulerDocketInterfaceContractTests.cs`.

Prior evidence for that source change: validation `20260909-054320-7f3469c1`, `changed`, Tier 3, Release, 397.143 seconds; source fingerprint `5b3c7bf353f6b0dad1f4eed5cd32644e4e49866ccdef0b78e618bb289e4bf0a8`. Compilation, 431 MCP tests, Court/persistence boundary tests, and enforce-mode repository hygiene passed. Complete preview matrix: 142/142. Full evidence: `.codex-build/petition-host-culture/REPORT.md`.

That historical validation covers the culture change only. It cannot certify the future group-generation implementation or authorize redeploying old whole-client artifacts over newer work. The current source still contains the correction; whether later coordinated deployments already included it must be re-observed before installation.

## Implementation approach

1. **One audience snapshot and one image request.** Introduce or extend a shared court-scene preparation seam usable by ordinary petitions, noble cases, and Court Life matters. Snapshot campaign/timeline/matter identity, actual host settlement/culture, visible public scene context, and the complete authoritative involved cast on the game thread. Deduplicate stable identities without silently dropping distinct people. Respect physically absent/deceased people and the ruler-outside-frame rule. Do not use `FirstOrDefault()` or `Take(4)` as the image-cast policy.

2. **One composite containing every reference.** Reuse the existing contact-sheet composition approach from `ReignCastleSceneClient`, but include all involved courtiers and the approved culture-specific throne-room reference in one raster. Use deterministic labeled slots and text instructions mapping each slot to one identity. Preserve each person's appearance and clothing while the hall panel alone controls architecture. Portrait inputs remain untouched; create a separate temporary composite. Use existing approved/cached identity references and the established child-safe reference route when needed. Assembling this request must not spawn one image-generation call per courtier. If a required reference is unavailable, follow a bounded pending/failure policy; never quietly submit a partial cast.

3. **Keep the single-image server route.** Continue through `PostCastleSceneAsync` with one `contactSheetBase64` value and the full `orderedParticipants` metadata. Do not add multiple reference-image fields/arrays, one request per portrait, a new provider, or a separate paid image-composition call. Generalize the single-principal prompt to the exact full cast while preserving elevated seated ruler viewpoint, no visible ruler/throne, unobstructed approach, and current visual quality constraints. Reuse the existing Castle Chat image-generation setting and provider configuration.

4. **Use host culture consistently.** Apply the preserved host-town correction to both generated art and fallback art across the three screen paths. Pass actual audience host identity explicitly; do not derive hall culture from the first courtier, their home, their kingdom, or the petition's beneficiary town. The ordinary generator currently uses `ParentTownId` and petitioner-culture fallbacks, so review that path during consolidation. Include resolved host/culture and approved-reference hash in image cache identity.

5. **Restore and publish generated art safely.** Reuse `SceneAssetPath` and existing presentation/history hooks. Persist the completed image and sufficient cast/reference/render-version identity to validate reuse. Display an existing compatible image on reopen/reload; share one pending generation job for the same audience and composite fingerprint. Reopening, each speaker turn, or adding another courtier must not independently fan out paid requests. Invalidate only the affected scene when its actual cast/host/reference changes; preserve prior history and conversations. Update `EventImageId` with change notification on the main thread only for the still-owned, active view. Late completion must not reopen a closed screen or write to a changed campaign.

6. **Dialogue and art stay independent.** Reuse the shared `ReignScenePreparationCoordinator` or the same proven lifecycle. Dialogue and decisions remain responsive while artwork is pending or fails. Expose distinct image states such as disabled, pending, ready, and failed; a fixed reference hall or a successful written `sharedScene` response must not masquerade as generated-image success. Preserve existing dialogue readiness/acceptance semantics separately. A retry may repeat one failed composite request, never one request per person.

7. **Maintain shared test coverage.** Extend existing Court/scene-preparation and ruler-docket harnesses rather than adding an ad hoc image-generation script. Any changed harness action, readiness field/schema, scenario, or evidence meaning must update `reign.testing.json`, `docs/agent/TESTING_TOOL_GUIDE.md`, applicable help/security documentation and catalog contracts in the same implementation change. Obtain a fresh manifest-selected validation plan for the exact edited code paths before coding.

## Required verification for the combined implementation

| Requirement or failure mode | Proof |
| --- | --- |
| All audience types request artwork | Deterministic generator seam invoked for ordinary, noble, international, family, patronage and visitor cases; both one-person and multi-person cases |
| One input image, all courtiers | Fake provider records exactly one scene request and one image-bearing input; inspect/decode the composite and stable slot map to prove every involved identity appears once, including a roster larger than the four-card viewport |
| Cost control | No per-person scene calls, no separate composition provider call, no individual portrait attachments, no new call per dialogue turn, and a pending/cache hit on reopen |
| Missing/duplicate/changed participants | Bounded missing-reference failure without partial-cast submission; identity deduplication; appropriate one-scene invalidation on real cast change; safe empty cast and no ruler/deceased-person inclusion |
| Correct environment | Battanian visitors in an Imperial town and Imperial visitors in a Battanian town; every supported culture reference; unresolved/custom-culture fallback; beneficiary town differs from audience host |
| Continuity | Same image/cast/host after close/reopen and save/reload, historical image retained, no late write to a new campaign or reopened finalized view |
| Independent lifecycle | Art-first, dialogue-first, disabled generation, timeout/failure/retry and close-during-generation; fixed background is not reported as generated success |
| Visual contract | Current provider-free binding/preview, complete rendered matrix, applicable aperture/portrait/layout/typography and approved-reference fidelity audits; no fixed shell redesign |
| Native and provider acceptance | On an explicitly authorized disposable campaign with documented provider gates, generate one image from one full-cast composite; verify all likenesses, host hall, visible update, responsive dialogue, cache reuse and reload. Capture provider input-count/request-count receipts and native screenshots |

Run fresh `reign_validate` using manifest-selected scope, inspect Court/scene harness results through MCP, and retain source fingerprints, reports, image-input counts and remaining coverage gaps. Do not mark the Inbox item complete merely because the plan, source code, compilation or static previews exist.

## Concurrency and delivery

Implementation checkpoint (2026-09-09): resource, noble and Court Life screens now use `ReignCourtAudienceScene` through the shared client/presentation adapters. The compositor submits one complete cast-plus-hall raster. Whole involved rosters survive normalization; the four-person active speaker limit is independent. Cached portraits or bounded free native appearance renders supply identities without paid portrait preparation. Snapshot/reference hashes, persisted image receipts, pending-request reuse and guarded main-thread publication support reopening and history. Small fractional age changes do not invalidate a scene. Native thumbnail maturity restrictions remain: unsupported infant/toddler appearances without a cached reference fail explicitly before any partial-cast scene request. Native/provider acceptance of child/guest capture and final likeness/composition remains pending after deployment.

Validation checkpoint (2026-09-09): combined `changed` Tier 3 Release run `20260909-233342-6d628d7b` passed all 10 operations in 347.996 seconds: 510 MCP tests including 28 executable court-scene cases, 72 native-renderer tests, 162 court checks, 62 persistence checks, and enforce-mode hygiene. The supplemental Verification Lab run exposed a legacy assertion requiring paid individual petition portraits. That assertion now requires the shared factory/refresh for all three court screens and prohibits individual portrait requests.

Final shared `changed` Tier 3 Release run `20260909-235021-36c7fdf0` compiled that correction and passed all eight operations in 275.429 seconds: 511 MCP tests, 72 native-renderer tests, all 103 quick Verification Lab checks (including the corrected entrypoint audit, 162 court checks, 16 portrait-derivative checks and 117 image-profile checks), and enforce-mode hygiene. Fingerprint: `f7e94c3a9668e25b7fec6c2c6efd9485df7176b53626918b5673cda6550f2b9a`. The client DLL is byte-identical to the preceding combined build. This successful concurrent report supplies final server artifacts without repeating the shared build.

Provider-free preview contracts, 142/142 rendered cases and both full-catalog approved-reference audits (20/20 each at 1920x1080 and 3440x1440) passed. Court typography and portrait/scene apertures passed. Unrelated global audit findings remain: seven Party Chat typography divergences and one Training Yard shell-alpha failure. The fidelity catalog has no dedicated Court Petition/Court Life approved-reference target; real generated likeness/composition and native publication/reload remain unproven. Full reports, binary hashes and acceptance gaps: `.codex-build/court-audience-scenes/READINESS.md`.

Other tasks may be editing this workspace. Preserve their tracked/staged/untracked changes. Keep this implementation and the host-town culture correction together. Use exact successful report artifacts through the supported visible lifecycle when deployment is requested; re-observe any newer shared deployment first. Do not restart the user's game/server or generate paid sample artwork merely to save this plan.

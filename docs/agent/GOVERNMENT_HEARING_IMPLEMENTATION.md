# Government hearings integration

Implementation checkpoint, 2026-09-08. Task `01a07f00-c1ec-7d21-a5e1-53ab39cb25b7`.

## Authority and deployment boundary

The user authorized implementation and validation in parallel with active tasks, then explicitly requested a pause and alert before deployment. Do not install these artifacts or restart the installed game/server for this task without the user's subsequent approval. Native acceptance also needs an exact protected baseline enrolled through the campaign-test harness and a verified disposable copy. The current source is not native acceptance evidence.

Shared Platform, server Program, catalogs, and modern UI manifest/preview changes are coordinated by task `01a081a0-f21c-7962-86c1-87376efb4f0c` (T - Add Clan Agreement Perk System). Wanderer task `01a08371-4d78-7370-a135-ee096e16ecbf` also implements in this workspace. Builds and verification windows are serialized. Existing staged/unstaged changes are preserved; no commit or push was requested.

## Government ownership

- Reign owns policy proposals/repeals, war/peace, fief allocation/reassignment, expulsion, alliances, trade agreements, and calls to war. Native ruler succession remains the explicit first-version exception.
- Capture native decisions before the ordinary election path, save their exact terms, suppress the corresponding native decision prompts/actions and influence spending, and apply native world effects through the authorized Reign result. Information browsing remains available.
- Seasonal demands and queued Reign government-sensitive actions enter the same business model. A world action retains its exact queue identity, parties, targets and terms; voting authorizes that action, and the actual execution receipt determines completion.
- Diplomatic agreements must retain both governments' consent requirements when replacing native prompts. Suppressing a counterpart prompt must never become automatic counterpart acceptance.
- Event hearings are independent of the 21-day seasonal schedule. They open at a safe campaign-map opportunity, with campaign time paused; no hearing interrupts a battle, mission, conversation or unrelated modal.

## People, votes and responsibility

An actual proposing clan leader remains the petitioner. A seated proposer can introduce their own petition. An outside voluntary petitioner needs a willing eligible seated sponsor or explicit ruler adoption. Sponsor selection first requires support for the specific requested outcome; petitioner relations rank willing candidates without manufacturing support. Mandatory state business invents no petitioner or sponsor. Unsponsored petitions expire after 21 campaign days without treating the expiry as a ruler refusal.

Level 1 is advisory; level 2 adds pressure; level 3 permits reconsideration before an opposed override; level 4 requires acceptance or an explicit consequential override. Urgent level-3 business receives an immediate reconsideration opportunity. At level 5 the ruler recommends, every eligible member votes individually, and the government decision is binding with no final ruler veto. NPC kingdoms use the same authority model. Binary ties preserve the status quo; an essential allocation with no status quo uses a stable tie break among the tied eligible candidates.

Votes consider the matter, representative interests, personality, party alignment, ruler recommendation and relationships, and scoped private commitments. Internal scores are simulation inputs only. Recorded ballots and actual public declarations can be displayed; secret score predictions cannot.

Named petitioners, sponsors, affected lords and claimants have outcome roles. Consequences consolidate overlapping roles and distinguish the ruler's recommendation from a binding institutional result. Consequence receipts prevent duplicate effects. Government Trust is retired from gameplay, prompts and UI; legacy save fields remain readable for migration.

## Recess, attendance and private conversations

Each nonurgent hearing permits one seven-day recess. Eligible available members attend the safe designated capital immediately. No replacement player capital is invented if none is designated or it is unsafe. Military, captivity, governor, quest and conflicting court duties take priority over relocation; unavailable members still vote absentee. A peaceful party leader or officeholder already physically at the meeting settlement may talk without moving their party or changing their duties. Army, battle, siege, captivity, travel and active conflicting assignments still prevent participation. Previously accepted commitments remain scoped to their original terms and expiry.

Attendance saves original/home location and shares residence across overlapping hearings. Members remain until decision, then return to a safe location or retain a new external assignment. Failed native returns remain pending for recovery. Death, captivity, capital loss, ruler changes and duties are rechecked. Existing keep room schedules admit notable representatives and add newly arrived members without rerolling other room sessions or bath history.

There are no session Persuade, Bribe or Deal controls. Political promises originate only in real individual in-person conversations with a physically available member. A typed receipt binds the exact turn, ruler, member, case, option, method, quoted mutual acceptance, payment and verifiable obligation. A reserved conversation alone cannot execute a promise. Replays cannot change terms or charge twice. A bribe needs the same explicit numeric amount in both quoted sides of the exchange and sufficient funds. A deal only affects support after its named existing Government obligation reaches actual completed state. An ordinary promise affects the member's consideration and does not guarantee the vote.

Public hearing speech records attributed dialogue only. It cannot enact a proposal, spend gold, change votes, create private commitments, or apply relationship/reputation actions. Absent members are not fabricated as present conversation participants. With no present respondent, the ruler's statement can enter the record without a provider call.

The server reserves government payment ownership around the ordinary action planner and rejects a competing payment after aliases and payment direction resolve, before anything enters the world-action queue. Invalid or stale typed agreements cannot fall back to a generic bribe. Other recipients and reversed payments are unaffected. An ambiguous payment of the same amount between the same parties needs separate quoted acceptance and purpose evidence to proceed as an independent purchase, ransom or gift.

## Interface and visual authority

The approved concept is retained at `ReignBeta/artwork/ui-modern-style-kit/generated-sources/government-hearing-v2/approved-concept.png`. Generated complete artwork, deterministic palette/aperture materialization, source hashes, actual alpha proofs, composition anchors and preview fixtures live beside it.

The interface uses Business, Hearing, Members & Parties, and Decisions & Obligations views, plus a dedicated constitutional authority view. The composition is approximately 20/54/26: business and context on the left, the current hearing and public transcript centrally, people and attendance on the right. Controls describe the available action, such as recommending an outcome, adopting a petition, calling the vote, taking the one recess, reconvening, or responding under the current constitution.

Portraits remain unchanged beneath transparent apertures in complete owning card art. Frames are integrated into that art; there are no separately positioned portrait rings. Scroll contents and full cards move together above the fixed shell. Superseded Government shell artwork is not referenced by the new prefab. Shared modern-style manifests and full provider-free render/fidelity checks remain authoritative.

All live relationship numbers and numeric deltas are hidden. No current-value sorting, stance score, trust meter or predicted vote percentage substitutes for the hidden value. Existing explicitly uncovered spymaster reports remain dated historical snapshots through their authorized intelligence view. Private commitment records are visible only to the participating ruler, with agreed terms and dates and no hidden vote-shift values.

Additional decrees and policies (such as relief, reconstruction, emergency levies and constitutional measures) remain later content work; this integration does not invent an unreviewed expansion of the existing 104-resolution catalog.

## Verification and current evidence

Before edits, the Reign MCP `changed` validation plan selected Tier 3 for Government client/server/contracts/UI integration. The final plan must include the three Court hooks and all shared manifest/catalog paths owned by the coordinating task. Use the exact successful Release artifact for the non-listening `reign_run_offline_verification` Government suite, then retain its run ID, fingerprint, duration and report. Do not substitute installed older-server verification results.

Deterministic tests exercise production hearing stages, one-recess rules, sponsorship, option scoring, ties, responsibility, private eligibility, exact receipt binding, payment evidence, public response filtering and injected-effect suppression. Native cases must exercise actual captures/executions and save/load, not source-text checks credited as gameplay.

The existing private lobbying certification cases use a two-pass protocol: first capture a run-owned exact business/member/gold/receipt baseline, perform the ordinary individual conversation, then rerun the same case to inspect the new scoped receipt and actual payment or completed obligation. A preexisting commitment, forced RNG success, direct lobby button call, or synthetic fulfilled deal cannot pass this proof.

The source baseline, progress checkpoint and later validation evidence are under `.codex-build/government-integration/20260908/`. Combined Tier 3 report `.codex-build/reign-mcp/validation/20260909-005651-66166e3d/validation-report.json` passed client/server/verification/live-test compilation; MCP tests reported 423 passes and four shared catalog/document expectation failures. The immutable server artifact from successful report `20260909-005420-fc7a9951` passed the isolated Government quick suite, 32/32 cases, run `verify-1788915723529-090501be`, in 3.923 seconds. Its source fingerprint is `ac4634d601a3aac1092b812c05eb7f0885f4712ea6c2f34fdb0f7a1538b0e2db`.

Subsequent payment, native-control, policy-initiation, bilateral, attendance and interface repairs compiled together in report `20260909-015231-fc17662b`; repository hygiene passed. Shared catalog inventory expectations were corrected, and focused MCP validation `20260909-020722-75cf8425` passed all 427 tests. The final combined artifact and its fresh 39-assertion Government quick result are recorded in `.codex-build/government-integration/20260908/deployment-readiness.json`; consult its status and evidence instead of treating an earlier compilation as the final deployment candidate.

The complete provider-free rendered matrix passed 142/142 cases in `.codex-build/ui-preview/combined-final-20260909/rendered-preview-audit.json`. All 20 registered approved-reference comparisons passed at both resolutions in `.codex-build/ui-preview/combined-fidelity-1920x1080/approved-reference-fidelity-audit.json` and `.codex-build/ui-preview/combined-fidelity-3440x1440/approved-reference-fidelity-audit.json`. All six Government compound aperture owners passed `.codex-build/clan-accords/government-compound-v4-audit.json`. Production `portrait_chest.png` derivatives were copied unchanged from the installed cache for representative preview fixtures; their source metadata and hashes are recorded in `.codex-build/clan-accords-art/production-portrait-receipts.json`. The installed cache was not modified.

Business now clears the displayed case and shows the pending queue overview; selecting a card enters Hearing, and the Hearing tab restores the remembered case. Tab changes do not dismiss business or change campaign pause. The supplemental provider-free projection and native navigation expectations are in `.codex-build/government-ui/evidence/business-overview-preview-override.json`.

### 2026-09-09 native hit-test repair

Court Life acceptance found that full-canvas view and composer wrappers accepted events over earlier sibling controls. On native instance `game-20260909141332-896fc28d`, a trade-card click reached both global and Government-layer input with focus owned and all input flags enabled, but did not select the case; the later `View all members` control worked immediately. Source and installed prefab hashes matched. Seven structural wrappers now use `DoNotAcceptEvents="true"`, preserving child controls while allowing underlying tabs and cards to receive input. This changes no geometry or artwork. Input-permission changes alone had failed the earlier native retest. Evidence is under `.codex-build/court-life-acceptance/20260909-resume/government-input-*.json`; post-repair validation, deployment, and native control proof remain pending until recorded there.

The XML repair subsequently passed `changed` Tier 2 validation `20260909-142431-3b94d0bb` with enforce-mode repository hygiene in 68.919 seconds (fingerprint `d4e1182f48bf55465da4bfd98cd69c11573fb54b702554773430a47be601b18f`). The guarded single-prefab installer deployed SHA-256 `113026ceaccce31922636c14dbf40912091a6db0980b1a35d6a22ceb76ac4bb5`; the installed DLL stayed on the separately validated narrative-marker-zero artifact. After closing and reopening the native screen, trade selection, Members, Decisions, and Close all worked. `government-hit-test-native-observation.json` records the exact selected trade business and delivered input. The `government-hit-test-render-matrix` passed 142/142; `government-hit-test-fidelity-1920x1080` and `government-hit-test-fidelity-3440x1440` each passed all 20 references. These evidence folders are under `.codex-build/court-life-acceptance/20260909-resume/`.

The next acceptance repair clears a pending selection when entering an empty decision archive and retains that separation after refresh. A guarded `hearing_compose` profile supplies at most 1200 characters to the already-open exact interactive hearing; it changes only unsent text. Native Speak remains the submission path. This addresses a desktop-control route where Unicode bulk typing did not reach the game while an ordinary key did. The profile cannot create/select business, call a provider, vote, or execute effects. Native negative-scope and successful submission checks remain required after this batch's validation and deployment.

Historical Government-task handoff: native effects, bilateral diplomacy, saved native peace-offer callbacks, persistence, attendance cleanup, actual private provider dialogue, safe automatic opening, tab navigation and odd candidate counts required native acceptance after deployment approval and protected baseline enrollment. The current Court Life task has that user authorization and is recording the remaining acceptance independently; no completion or roadmap change is implied by these individual repairs.

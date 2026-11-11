# Editorial coverage review — November 6, 2026

This is the maintenance ledger for the plain-language player manual. It records coverage, not a full-mod release certificate. The scope is the reviewed working source, including explicitly identified implemented previews. Current sources and their exact hashes are in `coverage.json`.

## Coverage basis

Reviewed the existing seventeen-chapter technical guide and its subtle-feature locator, current module/dependency and testing authorities, registered native campaign behaviors and patches, conversation action catalogs, MCM controls, relevant authored data, current interface references, and focused implementation documents. The roadmap was used to discover newer work, not as proof that an unchecked feature is complete. Active same-project summaries and dirty state were inspected; unrelated tracked changes were preserved.

| Area | Manual coverage and small changes retained |
|---|---|
| Installation/platform | Current managed WSL server and launcher-facing address; cloud dialogue and optional cloud images; Harmony, ButterLib, UIExtenderEx and MCM; preparation Retry; grace period; single bundled Arena module. |
| Characters | Persistent identities, traits/virtues/MBTI, private concerns, interests and newer narrative profiles, visible status, known-face distinction, added court nobles, wanderers, resident/notable recruitment, authored new-campaign ages and younger generation. |
| Relationships | Directional affinity, passive drift versus explicit conduct, real favors, romance, rivalry, marital conflict, secret/favor arcs, NPC matchmaking, player courtship, marriage/separation and reproductive choices. |
| Dialogue/actions | Individual Approach and silent continuation, group selection and continuation, narrow audiences, action builder, offers/counters/revisions, conditional/ongoing terms, inventory/ownership/authority checks, native result distinction and guide-follow behavior. |
| Communication | Correspondence delivery and return routes, unsolicited introductions, revised pacing options, banter window/takeover, party-only announcements, source-linked NPC communication, actual summons travel/stays and privacy. |
| Personal life | Invitations, story locations, brief/outings/major experiences, leave versus delay, native duel/hideout objectives, pets/lasting facts, attention/neglect, keep/patrol invitations and rescue dialogue. |
| Residents/venues | First explicit conversation promotion, actual encountered appearance, persistent home/work, private Homes audience, Visit in person, voluntary recruitment/duty release, Madam/staff confirmation, optional scenes, Whoremonger and worker replacement. |
| Family | Roleplay-only player-parented conception, individual-chat boundary, Proceed/Pull Out, eligibility, legal/biological parentage and concealed exposure, pregnant NPC duty withdrawal/recovery, children/Family Chambers and developmental memory. This is one topic family among the entire mod, not the sole focus of completeness. |
| Reputation | Gossip versus rumor versus established reputation, Public Standing, Charm mitigation without formulas, major title catalog, favoritism, ruler presence, correcting/mitigating signals versus reversing actual events. |
| Court | Capital versus Local Docket, relief petitions and real expeditions, pending unaffordable needs, Chancellor activation/pay/emergency service, retired Regent, offices/advisory council, noble/international/visitor/patronage previews, room placement and Training Yard time/stop behavior. |
| Government | Institutions, founding constitution, ruler limits/overrides, civic/noble admission, constituencies, incumbency/recall, sponsorship versus party adoption, frozen matter/caucus rolls, hearings versus private bargains, edicts/policies, deadlines and actual enactment. |
| Diplomacy | Autonomous NPC rulers, directional political pressure, all principal agreement families, recorded obligations versus native transfers, breaches/retired actions, ambassador mandates/travel/recall, Clan Accord types/capacity/lifecycle and authority limits. |
| Spymaster | Office/capacity, intelligence/sabotage, time/cost/detection, social manipulation, assassination, capture/ransom/breakout and foreign agents. |
| Military | Campaign Command objective families, NPC-only command, reports/clarification, fixed-scale War Council/panning, detection/intelligence, map orders, direct-order precedence, standing, funded mobilization, drawdown and quartering. |
| Party guests | Temporary status versus recruitment, protected actual waiting camp, reviews/departure/return, equipment lock, own-faction risk and banishment/succession. |
| Justice/rulers/rebellion | Practice/honor/lethal duels, separate damage and learning paths, two-step arrest, jurisdiction/cause, release versus rescind, protected custody, ruler capital/capture/judgment, actual death versus sentence, exile/submission, unauthorized executions, future pledges and civil-war-specific outcomes. |
| Economy | Qualitative troop/upgrade/wage premium, real payer and reserves, native ransom pricing, recruitment population/strain and exceptions, civilian crew exemption, funded retinues, real patrol manpower, recovery/production/security, siege aftermath, food budgets/real stocks/relief/donor protection/blocked cargo, garrison discounts, native player ship pricing and acquisition-aware resale, port funding/commissions, targeted tariff-loop correction. |
| World/time/news | Longer calendar/age anchoring, knowledge versus objective history, startup quiet period, every catastrophe/boon archetype, public report categories, scrolling/acknowledgement/pause, news preferences and real team tournament results. |
| Arena/XP | Included Arena Overhaul, ordinary/expansive/parry/team practice, hero/loadout/viewer options, team tournaments and rewards, player-party roster handling preview, separate Training Yard/private duel routes, participation XP and independent category controls. |
| Art/UI | Shared/campaign portraits, Look at them, scene View, before/after outfits, optional image settings, Memory Book, main-menu presentation, italic narration, hidden relations/visible skills, Reign popup keys, native safety repairs, equalized dungeon security and separate governor/owner culture rules. |
| Persistence/settings | Protected save pairing/identity, unprotected warning, finite unique-state capacity and native deletion, rollback timelines, matching export/import pairs, keys excluded from backups, update/move/uninstall limitations, MCM versus server settings, editor revisions, diagnostics/privacy and non-player lab boundaries. |

## Drift corrections made for the new manual

- Old standalone Windows/5101 startup descriptions yield to the current launcher-managed dedicated WSL/8089 contract.
- ButterLib is included because the current `SubModule.xml` requires it.
- Old Government hearing Persuade/Bribe/Deal buttons and simple majority claims do not describe the newer participation/hearing flow. The manual follows current individual-conversation commitments, the constitution, actual matter rolls and recorded decisions.
- Old rebellion instant-resolution summaries yield to the expanded ruler sentence/independence/reunification rules where applicable.
- Exact economy values are deliberately not copied out of the concurrently updated technical guide. Current source families and outstanding balance acceptance are recorded instead.
- New-campaign-only profiles, ages and child seeding are distinguished from existing-save migrations.
- Follow-up: verified Alt+A in supported conversation hosts, Ctrl+M on suitable map screens, backslash for Party Chat, Enter/Escape and editing shortcuts from current source. Added them beside relevant instructions. Removed the unsupported claim that the current bindings have a rebinding control.
- Follow-up: withdrew the unsupported 32 GB planning recommendation after the user's correction. The player manual displays only published base-game RAM figures and states that Reign's additional ordinary/peak use has not yet been benchmarked.
- Added review-gated on-demand monitoring of committed and uncommitted client/server inputs. Exact baseline source bytes and predecessor records are preserved and hash-verified on E:, so later source comparisons are not limited to whatever was committed at the time.

## Editorial consultations and disposition

- Claude: adopted a beginner route, practical first exercise, settings/recovery help, picture provenance and coverage checks. Adapted the proposed broad chapter grouping into shorter chapters for lookup. A blanket claim that every image is a mockup would be inaccurate; original diagrams are labeled separately.
- Grok: adopted audience/channel clarity, exact control names, everyday aliases, small-feature index entries, family mechanism separation, and failure explanations beside actions. Its review is editorial and does not establish source truth.
- Gemini: adopted dependency-free local search, A–Z navigation, keyboard access, full alt text, print styles and explicit layout-reference labels. Actual screen-reader, mobile-browser and full rendering acceptance have not been claimed.

## Coverage boundaries

Descriptions cover feature families and consequential behavior, not every authored story script, court-dispute title, developer test command, private formula, internal API or source line. Numeric tuning is intentionally omitted at the user's request. Necessary hardware planning figures, dates, exact keys and native labels remain.

Review of current source is distinct from installed-version proof. Preview notices preserve known native/provider/long-run acceptance gaps rather than claiming the mod is release-certified. The historical memory snapshot is retained as evidence of what was observed, not as normal-use or hardware-capacity evidence. No representative typical range or peak benchmark is claimed. The inherited picture references are byte-identical approved designs, not current in-game screenshots. At the earlier hotkey update the user deferred replacement; the native-capture progress below records the later supplied pictures. Browser inspection was stopped by the user and is deferred.


## Native captures received — November 6

Integrated the user’s original-resolution field-banter, Individual Chat and Correspondence pictures. Replaced two layout references and added one previously missing banter example; seventeen references remain temporary. Added reputation-led house introductions alongside the illustrated letter. `captures.json` binds byte-identical files to independently hash-verified originals on E:, original dimensions and limited visible evidence. Capture date and build are unknown. A narrated action is not execution evidence, and a paused map does not establish that banter pauses time. No game/runtime operations were used.

Source drift reviewed for this picture update: the Court native-scale adapter now uses actual Gauntlet scale, with bounded read-only geometry diagnostics and tests. Added a qualitative display-fit note; native readability acceptance remains open. The economy test-host null-stack guard changes fixture verification, not player economy behavior. Roadmap proposals and calibration/catalog documentation do not establish newly shipped features.

Additional supplied captures integrated: wilderness roleplay, International Court audience and Courtyard Dance. The audience is an additional example and does not replace the still-needed main Court overview. Six native pictures now replace four references and add two previously unillustrated screens; fifteen layout references remain temporary. All original bytes and hashes are preserved in the capture receipt.

Family Chambers selection capture received and integrated: seven supplied captures now replace five layout references and add two examples. Fourteen references remain temporary. The Family Chambers picture shows selection before entry, not an active household scene; the supplied capture is preserved as received at 1672 by 941.

Spymaster and War Council captures received and integrated on November 6. Nine supplied captures now replace seven references and add two examples; twelve temporary references remain. Captions explain visible categories, assessments, commitment and report controls, and map/standing/roster panels without reproducing example tuning values. The Spymaster picture shows no recorded operations; the War Council picture does not prove a command executed. Originals and receipts are preserved on E:.

Ambassadors directory capture received and integrated: ten supplied captures now replace eight references and add two examples, leaving eleven temporary references. The caption distinguishes the Foreign Advisor from foreign envoys and notes travel status and separate controls. No agreement completion or installed-version certification is inferred.

Economic Report capture integrated: eleven supplied pictures replace nine references and add two examples; ten temporary references remain. The caption identifies settlement, transfer, kingdom, logistics and advisor panels, while keeping example campaign figures and report dates distinct from universal rules. Exact original preserved on E:.

Government Agenda capture integrated: twelve supplied pictures replace ten references and add two examples; nine temporary references remain. The caption distinguishes declared positions in the lobbying interval from the later decision. Proposal terms visible in this campaign are examples, not fixed balance rules or enactment evidence. A new-proposal drafting capture remains optional supporting material.

Homes directory and Visit the Madam opening captures integrated: fourteen supplied pictures replace eleven references and add three examples; eight temporary references remain. Homes is an additional picture. The Madam screen explicitly shows scene images OFF and a pending reply, so neither completed negotiation nor generated scene is claimed. Preserved original dimensions and independently verified bytes on E:.


## November 9 edition

The user approved the 16 proposed updates after reviewing the source and task audit. The marriage changes are in Relationships and Correspondence. Nord households are in Getting to know people. Military standing, wages, recruiting, raid food, fishing, retirement, clan trade, and stipends are in their relevant war and economy topics. Sabotage, campaign cleanup, portrait storage, and the options controls are in their existing chapters.

The Action sentence system chapter accounts for all 183 definitions in the reviewed conversation catalog in 173 rows. Shared objectives and older aliases share a row where appropriate. The builder, special marriage controls, event offers, and follow-up paths are distinguished from developer console setup. The preserved source archive and definition-to-topic mapping are in action-reference.json. The completeness check compares exact registry IDs, rendered labels, topic placement, special controls, and the archive hash.

Claude reviewed both halves of the action chapter and every new feature passage. Review refinements included the event-only offers, who can request a separation, insert versus send, the limits of supervised commands, and the different marriage and petition routes. Source review retained the complete guarded Aserai playthrough evidence and confirmed the offered control names. Raw review responses and the source comparison are under E:/CodexStorage/Projects/Reign/UserGuide/updates/2026-11-09-evidence.

The canonical baseline review covers 123 exact changed inputs. Separate hash inventories cover the economy integration and wedding worktrees. Their deployments support the additional implemented topics. The NPC Clan Trade candidate passed Options browser checks. Its deployment remains pending release validation at this review, so the manual describes the installed pilot and marks the replacement as a preview. Recheck the release receipt on the next guide update.

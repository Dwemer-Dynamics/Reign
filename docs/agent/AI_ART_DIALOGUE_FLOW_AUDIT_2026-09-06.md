# AI artwork and dialogue preparation audit — 2026-09-06

## Court audience extension — 2026-09-09

The historical inventory below predates group court artwork. Resource, noble and Court Life audiences now share the full-cast image pipeline described in `COURT_AUDIENCE_SCENE_PLAN.md` and `reign.testing.json` (`courtAudienceArtwork`). Dialogue still starts independently. Exactly one local composite contains all involved identities and the host-town hall; one scene submission uses that image. Cached adult portraits and free native reference renders avoid paid per-person preparation. Generated-art readiness is distinct from fixed reference art and the international written `sharedScene`. New offline engine tests and existing native snapshot profiles cover this extension; native/provider acceptance remains a separate deployment follow-up.

User requested the Baths freeze repair and concurrent text/image preparation across every AI-art system.

## Native failure

Live ClrMD captures of Bannerlord PID 34504 found NullReferenceException in TextMeshGenerator.AddCharacterToMesh, RichText.FillPartsWithTokens and RichTextWidget.OnLateUpdate. The Font object named ReignSerifActionItalic had a null FontSprite. Its descriptor named CormorantGaramond-MediumItalic.png while SpriteData registered ReignSerifActionItalic. Correct the descriptor page to the registered sprite; retain the approved italic font and artwork. Diagnostic evidence: `.codex-build/baths-freeze-20260906/FINDINGS.md`, `render-roots.json`, and `stacks-*.json`.

The first reply returned at 16:04:56 Central and was added to the view model before rendering failed. Image preparation took 147.635 seconds; the window opened about ten seconds after image readiness; the first reply took 39.964 seconds. This was both a serial-loading delay and a native rendering failure.

## Complete flow inventory

| Flow | Existing dependency | Result |
| --- | --- | --- |
| Castle/keep locations, including Baths | PrepareAsync completed before OpenCastle | Open dialogue immediately after prompt/context resolution; artwork runs independently and refreshes the active view |
| Family Chambers and family scene route | PrepareFamilyAsync completed before OpenCastle | Same concurrent preparation using the existing family safety/context payload; retained-image reuse prevents unnecessary regeneration |
| Resource/relief ruler petitions | Constructor starts RequestReactionAsync and PrepareSceneAsync independently | Already concurrent; contract coverage preserves both independent starts |
| Noble/international matters and Court Life audiences | Existing reference artwork; opening reaction starts independently | No generated-art wait to remove |
| Individual and ordinary party chat | Portrait bridge/queue work is independent of dialogue | No generated-art wait to remove |
| Social events and generated wilderness scenarios | Existing event-phase/terrain art selected by RefreshScene; scenario text determines participants before conversation | No image-provider prerequisite; retain necessary scenario/context dependency |
| Memories Book / conversation memory painting | MemoryService launches image generation in Task.Run using existing description and portrait references | Already independent; description/reference prerequisites are real image inputs, not dialogue blocked on art |
| Portrait generation and Character Studio | Image-only operations | No paired dialogue operation to parallelize |
| Remaining council, government, diplomacy, correspondence, tavern and map views | Existing shell/scene/portrait assets or independent portrait requests | No live scene-image generation ahead of speech |

Server image entry points were audited through PortraitGenerate callers: CastleSceneGenerate, FamilyChambersSceneGenerate, portrait API and shared portrait generation. None produces dialogue that must follow image pixels. Opening NPC turns remain ordered to preserve conversational context; concurrency applies between artwork and the dialogue stream, not between dependent speakers.

## Lifecycle and proof

ReignScenePreparationCoordinator starts both operations without awaiting either first and shares pending art per session. Completion never opens a screen. The active party-chat view refreshes generated art from the established session on the main thread; finalized views are ignored. Castle prompt resolution checks that the originating campaign is still current before opening.

ScenePreparationTests executes the production coordinator with controlled tasks to prove overlap, pending-art reuse, failure isolation and retry. Its font contract checks every custom descriptor page against real registered SpriteParts and source textures, covering the native naming failure that screenshots/compilation missed. Integration contracts preserve both castle routes, petition independence and active-view refresh.

Native acceptance remains necessary: render an actual mixed speech/action reply; confirm responsive close/send controls; exercise art-first, dialogue-first, close/reopen and failed-art cases. Provider-free structural tests cannot establish native renderer or provider latency acceptance. Exact validation and preview results are recorded in the task handoff.

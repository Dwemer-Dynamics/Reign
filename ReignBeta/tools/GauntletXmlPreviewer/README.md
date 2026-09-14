# Bannerlord Reign Gauntlet XML Previewer

A focused, local inspection tool for Reign Gauntlet prefabs. It approximates Gauntlet layout and styling closely enough to review the black-and-gold Reign interfaces, exercise real-looking data, identify exact XML objects, and catch common layout failures before starting Bannerlord. Reign sprite parts are checked against the same generated sheets loaded by Bannerlord.

Calibration edits stay in browser memory by default. Workspace source changes require an explicit apply action, an unchanged-source SHA-256 match, and a timestamped backup retained under `.codex-build/ui-preview/calibration-backups`; saved patch JSON is likewise generated evidence under `.codex-build/ui-preview/calibration-patches`, never managed source. Installing one reviewed prefab into the live Reign module is a separate explicit action with its own hash check, installed-file backup, Windows-safe atomic replacement with a non-null rollback file, and post-copy verification. Installation is supported while Bannerlord is running; close and reopen the affected interface afterward so Gauntlet loads the new movie XML.

## Start

Run `Preview Gauntlet XML.cmd` from the `ReignBeta` folder, or run:

```powershell
tools\GauntletXmlPreviewer\serve-preview.ps1
```

Then open:

```text
http://127.0.0.1:5177/tools/GauntletXmlPreviewer/index.html
```

The server exposes local files inside `ReignBeta` so the preview can load current prefab XML, Reign event art, portrait-cache images, and sprite-part images without copying live-module assets. It also owns a loopback-only Codex App Server plus a browser-safe local WebSocket bridge for the bottom editing dock. If Codex or Node is unavailable, previewing and manual calibration remain usable and the dock reports the missing dependency.

Append `?interface=social-event` (or another stable catalog interface id) to open an exact standalone interface state directly. This is the authoritative route when several states share one XML file, as Social Event and Wilderness Event do. The legacy `?prefab=court` filename/label lookup remains available for unique prefabs. Use `?augmentation=native-conversation` (or another Native UI additions id) to compose Reign's current C# patches into the exact installed Bannerlord base prefab. Repeatable checks may also set `viewport=1920x1080`, `uiScale=1`, or `runtimeScale=native` (using any value present in the corresponding selector), so evidence does not depend on prior browser-local preferences. Automated provider-free captures use `codex=off`; ordinary sessions leave that parameter absent and keep the editing dock connected.

## Included workflows

- Load every cataloged standalone Reign prefab and every source-discovered Reign addition to a native Bannerlord prefab.
- Compose all 30 current `PrefabExtension` applications across 15 native Bannerlord prefab targets against the exact allowlisted installed base XML. Modified nodes retain patch class, authoritative C# source, and XPath provenance; installed brush/sprite dimensions resolve native constants used by layout.
- Keep a loaded workspace prefab synchronized with source changes. Clean documents auto-reload; documents with staged calibration edits show a blocking stale-source warning until **Reload current source** rebases the edits onto the latest XML.
- Treat Bannerlord as the rendering authority. For a workspace prefab, the previewer automatically discovers the newest installed `reign-ui-runtime-snapshot-v1` evidence for that movie, uses its exact physical resolution and `UIContext.CustomScale`, and attaches the newest native capture for an optional pixel overlay. Captured text, visibility, and repeated-list shape are applied only when the explicitly labeled captured-state mode is enabled.
- Use the bundled SIL Open Font License Fira Sans Extra Condensed web fonts for `PreAlpha.Text`, matching Bannerlord's authored font family and weights instead of substituting browser Arial metrics.
- Choose, drag, or paste an arbitrary Gauntlet XML prefab or widget tree.
- Exercise realistic active/available NPCs, long names, long chat lines, Court states, full portrait art, and event art.
- Test 1080p, 1440p, 2560×1600, 3440×1440 and 3840×1600 ultrawide surfaces, smaller viewports, and 80–140% UI scales.
- The viewer opens on the current 2560×1600 live-game comparison surface; the remaining presets stay available for regression testing.
- Social Event art uses the same viewport-derived square-size contract as the live screen: up to 876×876 while reserving the header, phase banner, and a usable chat region.
- Preview physical resolution through Bannerlord's exact native 1920×1080 Gauntlet reference formula, including its narrow-aspect tolerance for 16:10 displays.
- Switch between standard native sizing, explicit player-scale locking, and fixed-reference sizing. `Auto by prefab` reads `DoNotUseCustomScaleAndChildren="true"` from the current XML screen root or its primary canvas instead of relying on a hardcoded filename list. Large fixed Reign canvases therefore ignore the player's custom UI scale in both Bannerlord and the previewer, while intentionally responsive screens and compact popups retain normal scaling; Social Event keeps its separate fixed-reference layer contract.
- Toggle node outlines and XML labels.
- Click any rendered object to pin its exact XML identity. Repeated clicks at one point cycle through overlapping objects.
- Copy a `reignxml://select?...` token containing file, source line, XML path, element type, Id, and repeated-data instance.
- Ask Codex to change the selected widget from the persistent bottom dock. The dedicated editing task receives the exact selection token, attributes, bindings, rendered geometry, diagnostics, pending patch, and installed-file status; it may edit only the shared workspace and cannot install, deploy, launch Bannerlord, or mutate a campaign.
- Review resolved bindings, all raw attributes, rendered dimensions, policies, margins, alignment, brush, sprite, runtime-asset synchronization, overflow, and clipping.
- Enable **Drag and resize** to move, resize, or enter exact geometry for the selected XML widget. A stretch-to-parent label or event-transparent child moves its owning button or nearest bounded parent by default, preventing the child from being clipped out of existence; hold Alt only when intentionally moving the exact child. Snap increments, aspect locking, keyboard nudging, undo, and redo are available.
- Press **Delete** with any rendered object selected to stage deletion of that exact XML node. The node disappears immediately in the preview and can be restored with **Ctrl+Z** or Undo. Deleting an item-template node removes every rendered instance because those cards share one source node; the patch review calls this out explicitly.
- Overlay a game screenshot at adjustable opacity or use difference blending for fast visual comparison.
- Save/download a non-destructive calibration patch, or explicitly apply the reviewed attributes to a current Reign prefab.
- Use the **Workspace → installed game** card to compare exact source/installed hashes, install the current clean source, or apply and install one reviewed patch. A combined source-success/install-failure response is reported honestly and preserves the successful workspace edit. Live installation is allowed while Bannerlord runs; the current screen remains unchanged until it is closed and reopened.
- Import a headless Bannerlord snapshot to compare engine bounds with browser bounds. Geometry changes are authored and reviewed in the browser before they enter a pending XML patch.

## Validated whole-UI deployment

`deploy-ui-transaction.ps1` is the fail-closed route for installing the complete reviewed UI/runtime authority after a green Release product validation. It defaults to `Plan`, recomputes the validation source fingerprint, binds every validated build output and installed before-state into a reviewed plan fingerprint, and writes evidence beneath `.codex-build/ui-deployment`. The authority includes the frozen core catalog/atlas/registry/client files, the live font definition and approved Tavern overlay, the portrait-mask and modern-style contracts, the hash-pinned fallback plus six canonical-culture ruler-petition throne-viewpoint composition references, and the separately protected 17-file War Council parchment set. Protected map bytes must already match and are never copied.

```powershell
$validationRoot = '.codex-build\reign-mcp\validation\<run-id>'
$sourceFingerprint = '<validation-report sourceFingerprintSha256>'
& tools\GauntletXmlPreviewer\deploy-ui-transaction.ps1 `
  -InstalledRoot 'D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\ReignBeta' `
  -ValidationRunRoot $validationRoot `
  -ExpectedSourceFingerprint $sourceFingerprint
```

Review the resulting `deployment-evidence.json`, including its exact copy set and `planFingerprintSha256`. Apply requires that exact fingerprint and refuses any source, build-output, destination, lifecycle, count, authority-hash, containment, or reparse-point drift:

```powershell
& tools\GauntletXmlPreviewer\deploy-ui-transaction.ps1 `
  -InstalledRoot 'D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\ReignBeta' `
  -ValidationRunRoot $validationRoot `
  -ExpectedSourceFingerprint $sourceFingerprint `
  -Mode Apply `
  -ExpectedPlanFingerprint '<reviewed planFingerprintSha256>'
```

Apply is allowed only while Bannerlord, the launcher, Reign server, live-test controller, vector worker, and port 5101 are all stopped. It serializes each installed root, writes a durable pre-mutation journal, hash-verifies backups and atomic replacements, and rolls back caught failures in reverse order. It does not launch or restart the visible Reign lifetime group.

## Native Bannerlord snapshots

The game build registers a nonvisual snapshot observer for each of the 18 catalog-owned native runtime states, including legacy-prefixed surfaces such as `AIPortraitsMemoriesBook`. It never loads `ReignUiCalibrationOverlay` as a second Bannerlord movie. The overlay XML, launcher, panel, movable selection geometry, and input canvas remain provider-free browser-preview support only; the additional pregnancy-warning browser state is exercised natively through the existing Individual Chat movie and its calibration-only modal fixture.

The warning is a required nested state of `parentTargetId=individual-chat`, not another movie or native surface. Its acceptance schema is `reign-ui-native-runtime-state-acceptance-v1`, its per-case receipt schema is `reign-ui-native-runtime-state-receipt-v1`, its `stateId` is `individual-chat-pregnancy-warning`, and its matrix mode is `inherit-parent`. For each exact Individual Chat parent case, the capture route opens `individual-chat`, invokes `ui-action --target individual-chat --text show-pregnancy-warning`, requires the snapshot to report `warningVisible=true`, `inputEnabled=false`, `individualChatModalFocusOwned=true`, and `individualChatActiveStateBlocked=true`, and then stages a separate exact foreground screenshot plus a `reviewed=false` receipt before closing without advancing or saving. The aggregate manifest is `<evidence-root>/individual-chat/states/individual-chat-pregnancy-warning/acceptance.json`; each inherited matrix case records its state receipt at `<evidence-root>/individual-chat/states/individual-chat-pregnancy-warning/<matrix-case>/state-receipt.json`. The receipt inherits the parent case's loaded-prefab/installed hashes, runtime snapshot, verified-window capture, and visual-review criteria.

1. Open the target through `ReignLiveTest.exe ui-open --target <target>` on the guarded disposable campaign.
2. Export its headless widget tree with `ReignLiveTest.exe ui-snapshot --target <target>`.
3. Import that JSON in the browser previewer. The magenta engine bounds can be compared to the cyan browser bounds, and any geometry change is made through the browser calibration editor.

Native snapshot export does not add visible widgets, intercept pointer or keyboard input, mutate the open interface, edit XML, or deploy files. A runtime snapshot containing a calibration-overlay movie, widget, action, or `supportUi` payload is invalid.

## Native authority mode

The **Bannerlord native authority** card uses exact engine scale only when the current workspace prefab hash and the selected resolution/configured-UI-scale matrix case both match retained native evidence. The previewer reloads that exact case when either selector changes and never substitutes a newer snapshot from another scale. XML canvases marked `DoNotUseCustomScaleAndChildren="true"` use Bannerlord's base resolution scale and deliberately ignore the captured player-scale multiplier. Runtime text and list shape are a separate, explicit **Use captured game-state text and list shape** mode. Keeping that mode off renders one coherent interface fixture and prevents an older capture (for example, vacant Royal Council seats) from being silently combined with current fixture portraits. **Overlay latest game capture** displays the corresponding exact-window screenshot over the editable XML render. **Refresh native evidence** reloads a newly captured snapshot without restarting the preview server.

New engine snapshots retain the prefab SHA-256 captured when Gauntlet loaded the movie, record the currently installed SHA-256 separately, and include rendered visibility, `TextWidget` text, brush, font size, position offsets, and runtime list shape. The previewer reports whether that snapshot came from the current workspace XML and refuses captured-state substitution when a known hash differs. This also prevents a still-open old movie from being mislabeled after a live install. Older snapshots remain usable as explicitly labeled legacy geometry evidence, but they cannot prove source parity because they predate the XML hash field.

## Evidence workflow

`calibration_workflow.py` inventories every declared Reign UI and its bindings, sprites, and stable IDs; creates aligned overlays/difference images from native captures; checks runtime-snapshot bounds; records one explicitly reviewed matrix case at a time; and generates the acceptance dashboard. `capture-window.ps1` captures the exact Bannerlord client area and emits resolution, DPI, timestamp, and SHA-256 metadata without launching another game or server.

Run the provider-free preview contract audit whenever UI XML, fixtures, catalog data, ViewModels, or previewer behavior changes:

```powershell
node tools\GauntletXmlPreviewer\audit-preview-contract.mjs --output ..\.codex-build\ui-preview\preview-contract-audit.json
```

The audit discovers every XML under `GUI/Prefabs` and every client `LoadMovie` call; a filename prefix cannot silently omit an interface. It requires exact catalog and fixture coverage, resolves all nested `DataSource` scopes without unrelated root fallback, verifies XML bindings and `Command.*` targets against the production client ViewModel graph, and records an explicit rationale for every intentional empty-list state. Every runtime `TextWidget` and `EditableTextWidget` must also declare an approved native serif brush, a positive explicit font size, and one exact frozen modern-style text-color token. The audit fails on any missing interface, unresolved runtime movie, stale fixture contract, missing runtime property/command, off-contract typography, or Royal Council diplomacy-fixture contamination.

With the preview server running, capture the complete provider-free visual matrix:

```powershell
node tools\GauntletXmlPreviewer\audit-rendered-preview.mjs --url http://127.0.0.1:5177/tools/GauntletXmlPreviewer/index.html --output ..\.codex-build\ui-preview\render-matrix
```

This renders all 68 cataloged standalone interface states (22 unique XML prefabs, including the Court Petition screen, distinct Social Event/Wilderness Event, and normal/pregnancy-warning Individual Chat fixtures, and all 12 tavern-house states) and all 15 composed native-augmentation targets at 1920×1080 and 3440×1440 with Codex connectivity disabled. The retained 166-case report records every PNG and SHA-256 and fails on parse errors, diagnostics, incomplete catalog/install-summary coverage, missing patch provenance, cross-state fixture contamination, or contaminated Royal Council fixture content. The same run performs a real `reign-ui-direct-manipulation-audit-v1` interaction on the nested Royal Council SEND label and a `reign-ui-custom-scale-lock-audit-v1` check proving that Auto mode reads the XML scale lock, ignores a 120% player scale for Royal Council, preserves normal 120% scaling for a compact popup, and honors an explicit developer override.

Compare the standalone renders against the user-approved modern references with the shared fixed-region contract:

```powershell
node tools\GauntletXmlPreviewer\audit-approved-reference-fidelity.mjs --render-report ..\.codex-build\ui-preview\render-matrix\rendered-preview-audit.json --contract tools\GauntletXmlPreviewer\calibration\full-catalog-approved-reference-fidelity-contract.json --output ..\.codex-build\ui-preview\approved-reference-fidelity
```

The rendered audit records the browser-observed bounds of the largest visible fixed 16:9 Gauntlet surface for every standalone case. The fidelity audit registers the approved reference to those authoritative DOM bounds by translation and uniform scale only and rejects freeform warping; legacy reports may use the older antique-gold projection fallback. Fixed-region metrics run at the observed render resolution, with the approved reference and masks conservatively downsampled to the same scale, edge search scaled with that render, and saturation weighted by visible luminance so near-black hue noise cannot impersonate a palette regression. Only explicitly declared portraits, scenes, protected map content, runtime lists, and variable text are excluded from fixed-shell comparison. The Calibration Overlay additionally reports and masks only its movable cyan selection geometry. Individual Chat is rendered from its current XML and sprite assets, without a preview-only legacy shell override. The separate typography contract requires fixed titles, section labels, ornament-integrated captions, and fixed control ornaments to exist only as exact approved shell pixels or registered approved-reference sprites. Every remaining live TextWidget/EditableTextWidget explicitly declares `Brush.Font="ReignSerifDynamic"`, the packaged Cormorant Garamond Medium SDF face, and freezes face/weight/style, size hierarchy, tracking, alignment/wrapping geometry, and exact text-color roles. The audit verifies the complete FNT, atlas, provenance, license, SpriteData, runtime-manifest, packed-sheet, and decoded literal-glyph chain; silent fallback, unsupported glyphs, wrong explicit faces, and active synthetic outline, glow, blur, or shadow effects fail closed. Only runtime-bound wording may vary; reviewed native captures remain the authority for resolved glyph shape, weight, baseline, hinting, and rasterization.

Audit native-parity readiness across every cataloged surface without pretending that provider-free browser evidence is native proof:

```powershell
node tools\GauntletXmlPreviewer\audit-native-parity.mjs --render-report ..\.codex-build\ui-preview\render-matrix\rendered-preview-audit.json --installed-root 'D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\ReignBeta' --evidence-root ..\.codex-build\ui-calibration --output ..\.codex-build\ui-preview\native-parity-readiness.json
```

The readiness report evaluates all 37 native surfaces: 22 runtime interface states and 15 native augmentations. Each surface remains pending until a reviewed `reign-ui-native-parity-acceptance-v1` manifest covers every catalog resolution/UI-scale case with exact current prefab or patch-source hashes, exact installed/base-prefab hashes, zero diagnostics, and verified screenshots. Runtime interfaces also require a non-empty hash-matched headless snapshot. The browser-only Calibration Overlay and the pregnancy-warning Individual Chat state remain part of the 68 browser states and 166-case provider-free render matrix; the overlay is excluded from native readiness, while the warning is a required nested `reign-ui-native-runtime-state-acceptance-v1` child of the existing Individual Chat surface. It inherits every parent matrix case and therefore does not create an additional native surface. Native Bannerlord augmentations use the reviewed in-game screenshot as rendering authority and may include an additional runtime snapshot when their host movie exposes one. Add `--require-pass` only for the native acceptance gate; it exits nonzero until every surface and required nested state has current accepted evidence. The default render-report path is the interface-complete matrix; pass `--render-report` only when using another retained report.

For production-interface screenshots, live-test `ui-snapshot` records only the requested runtime interface through the headless observer. The standalone batch fails closed if it sees a calibration-overlay movie, widget, action, or `supportUi` payload; there is no native overlay target and no native expand/collapse action. At 3440x1440, native snapshots expose a 2580x1080 logical root at `UIContext.CustomScale=1.33333325`; fixed 16:9 workspaces remain uniformly scaled and centered with expected ultrawide side gutters rather than being stretched.

After visually reviewing one exact native case, record it through the guarded evidence assembler rather than hand-writing an acceptance manifest:

```powershell
& $python tools\GauntletXmlPreviewer\calibration_workflow.py accept-native-case --target royal-council --resolution 2560x1600 --ui-scale 1 --screenshot .codex-build\ui-calibration\royal-council\2560x1600-ui100\live.png --snapshot .codex-build\ui-calibration\royal-council\2560x1600-ui100\runtime-snapshot.json --evidence-root .codex-build\ui-calibration\native-parity --installed-root 'D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\ReignBeta' --render-report .codex-build\ui-preview\render-matrix-interface-complete\rendered-preview-audit.json --diagnostic-count 0 --reviewed --review-confirmation 'I confirm this native capture matches the approved Reign reference for all fixed visuals and typography'
```

The recorder requires the screenshot's adjacent `reign-ui-window-capture-v1` receipt from `capture-window.ps1` by default and verifies its Bannerlord process identity, foreground-client capture mode, dimensions, path, and hash. Use `--capture-metadata` only when that receipt was retained elsewhere. It also rejects unknown targets and matrix cases, missing evidence, stale installed/source hashes, stale snapshot hashes, wrong movies, empty widget trees, and runtime snapshots containing calibration-overlay or `supportUi` evidence. Omitting `--reviewed` deliberately records a pending case. For a native augmentation, omit `--snapshot` and `--installed-root`; its acceptance remains bound to the exact current patch-source aggregate and installed native base-prefab hash from the rendered audit.

Capture every standalone runtime state for one required resolution/UI-scale case in a single provider-free session:

```powershell
& tools\GauntletXmlPreviewer\capture-native-matrix.ps1 `
  -LiveTestPath .codex-build\reign-mcp\validation\<run>\live-test\out\ReignLiveTest.exe `
  -SaveName test2 -Resolution 2560x1600 -UiScale 1 `
  -InstalledRoot 'D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\ReignBeta' `
  -RenderReport .codex-build\ui-preview\render-matrix\rendered-preview-audit.json `
  -OutputRoot .codex-build\ui-calibration\native-parity
```

The batch refuses to attach to an already-running game, opens only the catalog's provider-free `ui-open` fixtures, exports each runtime widget tree, takes a verified foreground client-area screenshot, and records every result with `reviewed=false`. Immediately after each successful campaign start and before the first `ui-open`, it requests a fresh 240-minute live-test bridge arm and fails closed unless that receipt reports both `ok=true` and `armed=true`; stale or pre-existing arm state is never relied on. The batch report records the arm receipt ID and expiry. Arming is capture-controller authorization only: it neither advances campaign time nor saves the campaign. The batch verifies the requested catalog `-UiScale` against Bannerlord's configured `UIScale` value. The snapshot's positive `UIContext.CustomScale` is a separate runtime-derived value that includes Gauntlet's physical/reference scaling; both values are retained instead of being incorrectly compared. Royal Council calibration uses a non-persistent four-domain transcript fixture, reports its fixture identity, and must reach a ready, non-empty transcript with zero provider calls before capture; ordinary player entry still requests production advice. The batch emits a `reign-ui-native-capture-batch-v1` report plus a `reign-ui-native-contact-sheet-v1` review sheet when at least one case is staged, closes each live interaction run, stops Bannerlord without saving, and never advances campaign time. A zero-stage failure still emits the complete per-case report and exits nonzero without hiding the original errors behind contact-sheet generation. Review the contact sheet and full-resolution images, then rerun `accept-native-case ... --reviewed --review-confirmation 'I confirm this native capture matches the approved Reign reference for all fixed visuals and typography'` only for cases that genuinely pass all nine fixed-art and typography criteria. Missing, false, unknown, or legacy attestations remain pending. Use `-Targets royal-council,economic-report` for a bounded repair check.

The shared screenshot helper handles focus changes without weakening evidence: it makes the capture thread per-monitor-DPI-aware before reading physical client pixels, uses bounded attached-input-thread retries to acquire Bannerlord, verifies that Bannerlord actually owns the foreground before copying pixels, and restores the user's previous foreground window and DPI context after capture. If the already-verified engine snapshot is wider than the physical desktop and Windows reports clamped client bounds, the helper triggers Steam's foreground DirectX backbuffer screenshot, requires the exact requested pixel dimensions, converts it into the evidence PNG, and copies the exact JPEG into the case evidence directory with its hash. Only after the copy hash matches does it remove that exact run-created Steam screenshot and matching thumbnail; it never sweeps the Steam library. Metadata retains both the original location and the durable evidence location plus the distinct `verified-foreground-steam-backbuffer` mode. `PrintWindow` full-content capture remains available only when Windows exposes an exact off-screen client; empty output is rejected and any OS client discrepancy remains visible in metadata. On-screen cases retain the ordinary foreground pixel-copy route. If ownership or exact engine dimensions cannot be proven, the case fails and retains its already-completed readiness and snapshot paths. Batch acceptance manifests are written directly beneath `-OutputRoot`, which is the same evidence root passed to `audit-native-parity.mjs`; there is no hidden staging subfolder.

To stage several or all required display cases without manually editing Bannerlord settings between runs, use the guarded display-matrix orchestrator:

```powershell
& tools\GauntletXmlPreviewer\capture-native-display-matrix.ps1 `
  -LiveTestPath .codex-build\reign-mcp\validation\<run>\live-test\out\ReignLiveTest.exe `
  -SaveName test2 `
  -InstalledRoot 'D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\ReignBeta' `
  -RenderReport .codex-build\ui-preview\render-matrix\rendered-preview-audit.json `
  -OutputRoot .codex-build\ui-calibration\native-parity `
  -Confirmation 'temporarily configure Bannerlord display matrix and restore it'
```

`-CaseKeys '1920x1080@1.00','2560x1600@1.20'` limits a run; omitting it selects the catalog's complete matrix. The supported release matrix covers 1920x1080, 2560x1440, 2560x1600, 3440x1440, 3840x1600, and 3840x2160; heights below 1080 pixels are intentionally unsupported and are not offered by the previewer. The orchestrator refuses a running game, stores byte-for-byte copies of `BannerlordConfig.txt` and `engine_config.txt` beneath the evidence root, changes only `UIScale`, `display_width`, `display_height`, and the validated `CaptureDisplayMode` (default `0`) while Bannerlord is stopped, invokes the existing provider-free batch once per case, and stops Bannerlord after each case. Its `finally` block restores both original files and verifies their SHA-256 hashes even when a child capture fails. The durable `reign-ui-native-display-matrix-v1` report records the backup, capture mode, restoration, game-stop, child batch, contact-sheet, and failure evidence. It does not approve captures: every case remains `reviewed=false` until its contact sheet and full-resolution images are explicitly reviewed.

Native augmentations are patches to native screens rather than independently loadable Reign movies. The preferred route is the provider-free native-augmentation batch: it opens the real production screens through the shared live controller, captures the pre-campaign InitialScreen through `game start-menu`, uses one no-save `test2` campaign session for the other 14 targets, verifies exact ready-target evidence, closes each screen, and stops Bannerlord without advancing or saving:

`game start-menu` does not accept a running process or an arbitrary delay as InitialScreen readiness. It tails at most 512 KiB of the current Bannerlord process log with shared read/delete access, requires the exact `GauntletInitialScreen::HandleActivate` marker to remain observable for two continuous seconds, and then returns `main_menu_ready` with `readinessMarker`, `readinessLogPath`, and `stableInitialScreenSeconds=2`. A missing or disappearing marker resets readiness or fails closed at the bounded timeout, so the batch cannot take the InitialScreen screenshot early.

```powershell
& tools\GauntletXmlPreviewer\capture-native-augmentation-batch.ps1 `
  -LiveTestPath .codex-build\reign-mcp\validation\<run>\live-test\out\ReignLiveTest.exe `
  -SaveName test2 -Resolution 2560x1600 -UiScale 1 `
  -InstalledRoot 'D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\ReignBeta' `
  -RenderReport .codex-build\ui-preview\render-matrix\rendered-preview-audit.json `
  -OutputRoot .codex-build\ui-calibration\native-parity
```

Use `-Targets native-initial-screen,native-quests` for a bounded repair pass. For campaign-backed targets, the batch requests and verifies its own fresh 240-minute live-test bridge arm immediately after the campaign starts and before any `ui-open`, records the arm receipt ID and expiry, and fails closed instead of trusting a stale or pre-existing arm. This capture-only arm does not advance time or save. Every result remains `reviewed=false`; inspect the contact sheet and full-resolution images before acceptance. Native production screens may create temporary in-memory UI or conversation state, but the harness closes them and never advances or saves the campaign.

For a screen that is already open manually, use the guarded current-screen helper. It verifies the running game and exact foreground-client dimensions, records the current patch/base-prefab fingerprints, and still stages the case as unreviewed:

```powershell
& tools\GauntletXmlPreviewer\capture-native-augmentation.ps1 `
  -Target native-quests -Resolution 2560x1600 -UiScale 1 `
  -LiveTestPath .codex-build\reign-mcp\validation\<run>\live-test\out\ReignLiveTest.exe `
  -InstalledRoot 'D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\ReignBeta' `
  -RenderReport .codex-build\ui-preview\render-matrix\rendered-preview-audit.json `
  -OutputRoot .codex-build\ui-calibration\native-parity `
  -AllowOffscreenClientResize
```

The same guarded display-matrix orchestrator covers native augmentations by adding `-NativeAugmentations`; it then delegates every selected resolution/UI-scale case to `capture-native-augmentation-batch.ps1` while retaining the byte-for-byte configuration restore and final game-stop guarantees.

The **Every-interface parity gate** card in the previewer recomputes this audit on demand and shows provider-preview coverage, exact installed standalone hashes, and reviewed native acceptance as separate counts. Its detail line identifies source prefabs that still require installation and exposes every pending surface/reason in the tooltip. The same bounded state is included in the selected-widget Codex context. Automated provider-free browser captures disable this refresh so the 166-case matrix does not recursively launch 166 native-readiness audits.

Prove the ordinary bottom chat transport separately with one no-op Codex turn:

```powershell
node tools\GauntletXmlPreviewer\audit-codex-chat-e2e.mjs --url http://127.0.0.1:5177/tools/GauntletXmlPreviewer/index.html --output ..\.codex-build\ui-preview\codex-chat-e2e
```

The audit selects the real Royal Council title, captures the exact outbound `turn/start` payload, requires a unique response receipt for the file, XML token/path, bindings, geometry, diagnostics, pending changes, install state, and deployment guard, and proves guarded Reign source state is unchanged by the no-op turn.

The official live controller can open the Court family without mouse coordinates and export the engine widget tree. `ui-open` waits for target-specific native readiness where a screen builds asynchronously; in particular, War Council does not report success until its strategic-map input surface has completed two layout frames. The Notable Generation calibration route pushes its explicit provider-free fixture into a newly constructed popup instead of inheriting the campaign-preparation defaults:

```powershell
ReignLiveTest.exe ui-open --target court
ReignLiveTest.exe ui-open --target castle-layout
ReignLiveTest.exe ui-snapshot --target castle-layout
ReignLiveTest.exe ui-status
ReignLiveTest.exe ui-back
ReignLiveTest.exe ui-close
```

```powershell
$python = 'C:\Users\speed\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
& $python tools\GauntletXmlPreviewer\calibration_workflow.py inventory --output .codex-build\ui-calibration\inventory.json --installed-root 'D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\ReignBeta'
& tools\GauntletXmlPreviewer\capture-window.ps1 -OutputPath .codex-build\ui-calibration\castle-layout\live.png
& $python tools\GauntletXmlPreviewer\calibration_workflow.py compare --reference reference.png --live .codex-build\ui-calibration\castle-layout\live.png --snapshot runtime-snapshot.json --output-dir .codex-build\ui-calibration\castle-layout
& $python tools\GauntletXmlPreviewer\calibration_workflow.py dashboard --inventory .codex-build\ui-calibration\inventory.json --evidence-root .codex-build\ui-calibration --output .codex-build\ui-calibration\DASHBOARD.md
```

The installed-root audit records missing and hash-mismatched prefabs before Bannerlord is launched. Comparison reports retain whole-window metrics for evidence while adding content-only metrics for the aligned reference surface, so the campaign map outside a centered UI does not distort fit scoring.

Pure positioning uses bounds and difference evidence. Image-to-image generation is reserved for reviewed raster-art repairs and never substitutes for native fit evidence.

### Safe source application

- Saved patches use schema `reign-ui-calibration-patch-v1` and are stored under `.codex-build/ui-preview/calibration-patches`; timestamped source backups are stored beside them under `.codex-build/ui-preview/calibration-backups` so generated evidence never dirties managed source.
- Apply is limited to exact prefab filenames owned by `calibration/ui-catalog.json` and present in `GUI/Prefabs`.
- The server recomputes the exact source SHA-256 immediately before applying. Any concurrent edit aborts the operation with no write.
- A successful source apply immediately reloads the written XML and runtime sprite contract, so the next calibration starts from the newly written source instead of stale browser text.
- Only the targeted opening-tag attributes or explicitly deleted XML element ranges are changed. Overlapping parent/child operations are rejected. The replacement is atomic and the prior XML is copied to `.codex-build/ui-preview/calibration-backups`.
- The installed-game action is limited to the same exact prefab filename, refuses an outdated browser hash, backs up the installed XML under `UiCalibration/previewer-backups`, and verifies the copied hash before reporting success. Installation is supported while Bannerlord is running; the response explicitly reports `screenReloadRequired=true`, and the affected interface must be closed and reopened because an already-open Gauntlet movie retains its loaded tree and loaded-prefab hash.
- The game-sync card reports both the current prefab hash and an all-prefab summary, including the exact stale or missing Reign UI filenames in its tooltip.
- Runtime snapshots use schema `reign-ui-runtime-snapshot-v1`; XML Ids are the preferred cross-renderer identity.

## Runtime sprite contract

After adding or changing a `ui_reignbeta_*` sprite part or `GUI/ReignBetaSpriteData.xml`, rebuild the game-readable sheets:

```powershell
tools\GauntletXmlPreviewer\build-runtime-sprite-sheets.ps1
```

The generator validates every declared source PNG, dimension, sheet position, and boundary. It writes `GUI/RuntimeSpriteSheets/manifest.json` plus one PNG atlas per category. Bannerlord validates that manifest and loads those atlases at runtime; the previewer validates the same SpriteData, source-image, and atlas hashes. An `ASSET` diagnostic means the browser and game are not guaranteed to match and must be corrected before the prefab is treated as game-ready.

The previewer also parses `GenericSprite` versus `NineRegionSprite` definitions from the same SpriteData. Stretchable ornamental frames must use nine-region borders so Bannerlord and the browser preserve their corners and edges identically at different widget sizes.

## Architecture

- `index.html` contains only the application structure.
- `styles.css` contains previewer chrome and approximate Gauntlet visuals.
- `js/sample-data.js` owns reusable fixture data and local asset mappings.
- `js/xml-source.js` owns XML paths, source-line indexing, constants, and binding extraction.
- `js/renderer.js` owns Gauntlet XML interpretation and rendered-object metadata.
- `js/sprite-runtime.js` validates and resolves the shared game/previewer sprite manifest.
- `js/diagnostics.js` owns missing-binding, runtime-asset, overflow, clipping, and framed-content fit-contract analysis.
- `js/calibration-editor.js` owns browser dragging/resizing, screenshot and runtime overlays, patch review, undo/redo, and guarded source-apply requests.
- `js/codex-chat.js` owns selected-context chat with a dedicated previewer Codex editing task.
- `js/native-augmentations.js` discovers current C# `PrefabExtension` patches, applies their XPaths to exact installed native base XML, and retains source provenance on modified nodes.
- `js/app.js` owns file loading, Bannerlord-native resolution normalization, per-prefab runtime scale contracts, persistent viewport/UI-scale controls, persistent selection, inspector rendering, and reports.
- `codex-preview-websocket-proxy.mjs` is a loopback-only browser transport bridge to the previewer-owned Codex App Server; it accepts only loopback HTTP origins.
- `audit-preview-contract.mjs` owns complete prefab/catalog/fixture discovery and source ViewModel binding/command parity checks.
- `audit-rendered-preview.mjs` owns the provider-free every-prefab, dual-resolution browser render and screenshot-hash evidence matrix, authoritative standalone reference-surface DOM bounds, and bounded runtime dynamic-exclusion geometry.
- `audit-approved-reference-fidelity.mjs` owns fail-closed DOM-bound registration, scale-symmetric approved-reference masks and metrics, geometry/material/palette comparisons, overlays, heatmaps, and isolated self-tests for every modern interface state.
- `audit-native-parity.mjs` owns fail-closed installed-hash and native-acceptance readiness for every standalone interface state and composed native augmentation.
- `audit-codex-chat-e2e.mjs` owns the selected-widget-to-Codex transport receipt and no-op source-integrity proof.
- `assets/attendee-*.svg` contains the previewer-only ornate card and portrait frames used to reproduce the supplied Social Event card reference without changing live prefab XML.
- `assets/event-attendees-*.svg` contains the previewer-only full attendee-column surround and corner filigree.
- `assets/event-chat-*.svg` contains the previewer-only event-log frame and Send-button treatment used to reproduce the supplied chat reference.
- `assets/event-phase-*.svg` contains the previewer-only phase-banner frame used to reproduce the supplied Browsing and Bargaining reference.
- `assets/event-header-*.svg` contains the previewer-only title-header frame; capture overlays are intentionally excluded.
- `assets/event-*-button.svg` contains the supplied Social Event button reference artwork. Matching raster sprite parts are validated against the game atlas; baked-label XML children remain hidden so Bannerlord does not draw a second label over the artwork.
- `assets/event-interface-background.png` is the supplied full-resolution Social Event surface texture, applied to the selected root overlay without changing its XML identity.

New Reign interfaces should normally require fixture additions, a declared sprite part, or a small renderer capability instead of screen-specific preview code. Any new `ui_reignbeta_*` category is included by the generator automatically.

Frame apertures and layer ordering belong in `GUI/UiCalibration/frame-fit-contracts.json`. The diplomacy wreath contract records its measured transparent opening and requires the reusable order `clipped content viewport -> content texture -> decorative overlay`; browser-only decorations must not be used to conceal a missing runtime clipping layer.

## Accuracy boundary

This is not TaleWorlds Gauntlet. It does not execute engine brushes, commands, or production ViewModel code. Browser layout remains an approximation, but XML binding names and commands are statically checked against the production ViewModel graph, and headless runtime snapshots supply engine-native bounds without adding calibration controls to Bannerlord. Resolution and UI scaling reproduce Bannerlord's native formula plus declared Reign layer-scale contracts, and custom Reign sprite identity and image bytes are no longer approximate: both renderers consume the same generated-asset contract, and the previewer reports a blocking asset mismatch when that contract is stale or incomplete. Native augmentation views use the exact installed base prefab plus current source patches and installed brush/sprite metrics, but their non-Reign native ViewModel content remains fixture-limited; the Reign-modified subtree is the diagnostic authority. Final parity still requires equal workspace/installed hashes plus native screenshots and runtime snapshots for the 18-state runtime target matrix, and a native screenshot of each patched native screen.

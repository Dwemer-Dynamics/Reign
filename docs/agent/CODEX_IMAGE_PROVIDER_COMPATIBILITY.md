# Codex image provider compatibility

2026-09-09. The user confirmed the Codex SDK/subscription route and explicitly accepted Image 2 until Image 2.5 is available there. The implemented `Codex` image provider serves Normal Portraits and Normal Scenery & Events. Adult profiles and adult clothing-edit passes are rejected before runtime startup. Provider-backed compatibility and human visual acceptance are separate from offline implementation verification.

## Configuration and behavior

Select **Codex SDK (ChatGPT)** in either normal image profile. The model field displays **Image 2 (Codex managed)**; this is the documented built-in image model, not a model ID sent to the text-agent turn. Sign in using the existing Codex connection in API Settings. Image generation works independently of the selected dialogue provider and does not require an image API key. Shared portrait generation uses the same provider readiness path. Existing NanoGPT and AtlasCloud choices remain available.

Each image uses an isolated official app-server helper and the enabled system `imagegen` skill with a `localImage` reference. The helper shares the official runtime's login configuration, never reads tokens, and is separate from the dialogue helper. It discovers image capability before a turn. The ephemeral image thread disables shell, inherited MCP servers, extra agents and shell network access, and limits writes to its unique image-job folder. One generation runs at a time. The default timeout is 300 seconds, bounded to 30–600 seconds; queue timeout uses the existing provider queue setting. Cancellation or timeout interrupts the turn, stops only its owned helper, then removes its confined files. If helper shutdown cannot be confirmed, the image queue stays closed rather than overlapping uncertain work.

Only successful native image-generation items can produce output. PNG data is limited to 32 MiB, 8192 pixels per axis and 33,554,432 total pixels. Base64 is preferred; the file fallback refuses remote paths, traversal, sibling prefixes, non-PNG extensions and symlinks/junctions. Existing portrait normalization, source provenance, caching and clothing-pass fallback remain downstream. No silent switch to another paid provider occurs on Codex failure.

## Verified interfaces

- Reign uses the official Codex app-server transport in `ReignBetaServer/src/Modules/Platform/CodexAppServerProvider.cs`. Existing text turns intentionally prohibit tool use and use a read-only sandbox; image turns need a separate request contract.
- Image routing converges on `CallImageProvider` in `ReignBetaServer/src/Modules/Portraits/AdultPortraitGeneration.cs`. `ResolveImageGenerationProfile` maps portraits to `portrait`, ordinary scene/event purposes to `scenery`, and explicit adult purposes to their separate profiles. `PortraitGenerateCore` retains native references, prompt composition, product validation and optional separate clothing edits. The Codex implementation is in `CodexImageProvider.cs`.
- `ImageProfileControlCenter.cs` builds four independent profile selectors. Codex is present only in the two normal selectors, with server-side adult rejection even for manually supplied settings or provider overrides.
- Installed `codex-cli 0.153.4` reports the `image_generation` feature enabled. Its generated experimental JSON schema has an `imageGeneration` thread item with `result`, `savedPath`, `status`, `failure` and `revisedPrompt`. The model-provider capability response exposes an image-generation boolean, not a selectable image model list.
- No dedicated image-generation model selector was found in the generated `ThreadStartParams`, `TurnStartParams`, `ConfigReadResponse` or `ConfigRequirementsReadResponse`. This does not establish that no future or account-specific route exists. The `model` used for a Codex agent turn must not be assumed to select the image tool's model.

## Official documentation checked

- [Codex image generation](https://learn.chatgpt.com/docs/image-generation) currently names `gpt-image-2` for built-in generation and directs programmatic image generation to the API. It describes included Codex usage and separate API-key billing for larger batches.
- [GPT Image 2.5 Sunburst](https://developers.openai.com/api/docs/models/gpt-image-2.5-sunburst) documents the explicit API model `gpt-image-2.5-sunburst`.
- [GPT Image 2.5 Flare](https://developers.openai.com/api/docs/models/gpt-image-2.5-flare) documents `gpt-image-2.5-flare`.
- [Codex app-server](https://learn.chatgpt.com/docs/app-server) is the transport reference. The installed generated schema is more specific evidence for its current image result item.

Do not label the built-in subscription image tool as Image 2.5 merely because a prompt requests that name. Do not silently switch the user to API billing or use a text model ID as an image model ID. The user's confirmed choice is the subscription route with Image 2 for now. Future 2.5 support requires new runtime/model evidence before changing the display or reported model.

## Evidence and remaining work

Local generated schema: `.codex-build/codex-image25-schema/v2/ItemCompletedNotification.json`, `ModelProviderCapabilitiesReadResponse.json`, `ThreadStartParams.json`, `TurnStartParams.json`, `ConfigReadResponse.json` and `ConfigRequirementsReadResponse.json`. Schema generation and feature listing made no provider requests. These generated artifacts are intentionally excluded from Git.

Reign MCP workspace, source, live status, testing catalog and validation-plan queries were available. MCP source reading rejected the `.mjs` browser contract, so that source file was read locally. Project Memory was retrieved through its documented CLI fallback because memory MCP tools were absent. No Reign or game restart was needed.

The catalog's `reign-codex-image-compatibility-v1` contract adds explicit Verification Lab suite `codex_images`: provider-free checks in quick/offline, and a separately usage-authorized live-llm compatibility run of up to three synthetic-reference images. Existing aggregate image contracts include the deterministic cases. The nine-case image-profile browser matrix covers NanoGPT, AtlasCloud and Codex at 1672, 1024 and 600 pixels, saved settings and adult option exclusion. These web controls change no native artwork, portrait aperture or Bannerlord interface. Provider PNG success alone does not prove native likeness or human scene quality. See the testing guide for the existing MCP safety gates and evidence paths.

The supported visible Reign server was healthy at the initial status probe. Implementation and offline validation do not change installed files, lifecycle, settings, saves or image caches. The shared working tree already contained extensive unrelated staged and unstaged changes; preserve them.

# Military standing and quartering

2026-11-04 implementation. Values are starting game balance parameters. Native campaign acceptance is pending. The user will launch the server/game and advance a verified disposable campaign; this change does not resume unattended time advancement.

## Historical basis and limits

John Haldon's [Organisation and Support of an Expeditionary Force](https://deremilitari.org/2014/05/the-organisation-and-support-of-an-expeditionary-force-manpower-and-logistics-in-the-middle-byzantine-period/) discusses middle Byzantine recruitment, provincial supply obligations, transport, and the fiscal demands of expeditions. Recruitment included professionals, provincial soldiers, levies and mercenaries. This supports distinguishing permanent defence from costly field mobilisation; it does not establish a universal medieval force percentage.

Petr Stefanovich's [Grand Retinue study](https://publications.hse.ru/en/articles/434232240) (2020, DOI 10.1163/15700577-12341385) describes permanent professional retinues funded through tribute in tenth/eleventh-century northern and eastern Europe. Retain paid household forces rather than treating every soldier as a seasonal peasant levy.

Lower garrison food requirements represent reduced spoilage, handling and transport loss, as requested by the user. They do not claim a soldier needs fewer calories. Historical mechanisms guide the implementation; the percentages below require matched game observations.

## Standing policy

- Automatic peace field recruitment target: 45% of native capacity, with the funded 10–15 household retinue counted inside the target. Existing population, food, native space/budget and seven-day funding restrictions still apply.
- Directed hostile Political Pressure increases the target linearly from pressure 10 to 100: up to 75% near that specific foreign kingdom's frontier; up to 60% in the interior. Frontier proximity is 90 map units between the current friendly fortification (otherwise nearest clan fief) and that foreign kingdom's fiefs. Never add unrelated pairs or absolute pressure magnitudes.
- Political Pressure stores hostility as **positive**, although the political UI calls hostility negative pressure. Stored negative values represent peaceful pressure. Government people-side pressure/domestic unrest remains separate.
- Current native war against another kingdom automatically permits 100% field targets only for NPC-ruled kingdoms. A sovereign's explicit War Standing order also permits 100% before a declaration. It creates no soldiers and pays no costs itself.
- Court > Royal Docket > Kingdom Military Standing reuses existing cards and action buttons. Only the current sovereign can change the saved kingdom order. NPC sovereigns update standing daily from actual war status; hostile pressure adjusts their peace targets. The player sovereign may retain Peace Standing and the chosen percentage during war; only NPC sovereigns automatically switch to War Standing during actual war.
- Main-party/manual player recruitment retains ordinary actual-resource rules. Explicit player-issued recruitment objectives retain their requested force and existing resource-waiting behavior. Garrisons and militia have no new size ceiling. Native party-size models remain unchanged.

Oversized field parties release up to 5% of native capacity per day in peace, from 1 to 10 soldiers, stopping at their current target. Each tier receives a share of the batch based on its healthy regulars. Fractional shares carry across batches for that party generation and kingdom. Within each tier, lower mean XP and stable troop ID determine release order. Heroes, prisoners and wounded stay with the party. The existing safety checks defer releases during battle, army service, sea travel and protected player force orders. Proportional stored XP leaves with released troops. Main parties, garrisons and militia are excluded. War Standing stops drawdown, and actual war automatically stops it in NPC-ruled kingdoms.

Drawdown receipts with allocation version 2 report the observed healthy counts by tier before and after the release. The guarded readiness probe also records both native field wage quotes and verifies that a duplicate callback leaves the result unchanged. Legacy receipts remain readable and have no version 2 evidence.

Each actually discharged regular returns at most0.25hearth to the current kingdom: one conserved budget split among functioning villages in stable ID order. Releasing100soldiers returns at most25hearth total, irrespective of village count. Towns benefit indirectly; no direct prosperity/gold or food is created. Mobilisation strain and agricultural recovery remain unchanged. No eligible villages or float rounding leaves an explicit unallocated amount; it is not multiplied or banked for later free grants. A recruit/discharge cycle costs0.5hearth and returns at most0.25. This is an abstract resettlement allowance, not an assertion of a known soldier's home or a historically measured ratio.

Persist party-generation/day receipts before mutation and retain actual troop removals and village credits. Duplicate daily callbacks and reload cannot pay again; reused string IDs have distinct native generations. Interrupted operations stay uncertain and are not replayed. Operational history is bounded (128recent,8192generation/day keys,128pending); detailed evidence belongs in external reports. Native acceptance must still prove recovery and long-run balance.

## Pressure evidence and persistence

The existing political notice response carries a bounded current-state snapshot (1024 directed rows maximum), including zero after pressure is consumed by diplomacy. The client imports it on the campaign thread only for the same campaign/timeline, rejecting malformed, duplicate, incomplete, future or delayed evidence. A snapshot expires after one native day; missing/stale data gives the 45% peace baseline and explicit incomplete diagnostics. Cached evidence and kingdom orders are additive in the existing economy save payload. No provider is invoked for readiness, no new diplomatic roll is added, and local war/order/production rules operate without server availability. Existing notice fetching/acknowledgement semantics are unchanged.

## Garrison costs

- Regular soldiers in the actual stationary garrison: nominal wage ×0.75, using native per-unit integer rounding before native aggregate modifiers, Clan Accords and Billeting. The exact native wage method's three quote sites are rewritten; heroes return the original wage. Visiting field parties, patrols, caravans and agreed hero/guest salaries receive no new reduction.
- Native garrison food denominator: 25 men per food rather than 20 (20% lower baseline supply use). Native siege, governor, building and issue calculations remain in their original order. Militia and civilian consumption remain native.
- The reductions apply in peace and war. A real transfer out immediately restores field costs; there is no blanket peace discount, refund, withdrawal fee or newly invented stored troop inventory.
- Existing authored reductions remain intact, including Billeting's 15% wage reduction and its loyalty/production costs. No new combined minimum overrides legitimate native/accord zero-cost outcomes. Native rounding means a small unit wage may be unchanged.
- Read-only snapshot counterfactuals quote wages and town food change with quartering disabled through a nested exception-safe context. These are model quotes, not extra payments or stocks. They retain all other loaded modifiers.

## Verification required

Deterministic checks cover hostile sign, thresholds, interior/frontier range, freshness/rewind, actual-war and explicit-order precedence. The licensed compiled-client probe binds the exact native wage rewrite, unchanged hero/field branches, food baseline, exception-safe quotation context, zero production in non-functioning villages and passive cash selection. Server contracts verify current scoped directed pressure including consumed zeros.

Native acceptance remains separate: order authority and save/reload; incoming versus outgoing pressure and resolved/foreign-timeline controls; actual war/civil war/member changes; normal and besieged garrisons with native/Accord/Billeting benefits; real garrison-to-field transfer and daily wages/food; exact manual/player-order behavior. Use the guarded one-village raid setup and existing bounded ruler party/garrison fixtures to manufacture comparisons. Restore the exact retained Current after incompatible branches.

Run matched 32-day peace/war observations plus 64 recovery days and the provider-free 126-day stability case before claiming balance. Observe cash per actual payer, separately attributed native payouts, troop burden, actual/potential production, market food, settlement reserves and donor depletion. No passed compilation or hook count substitutes for this acceptance.

Readiness probe adds bounded synthetic local incoming-pressure0/-100/+100/stale model quotations with exact prior-cache restoration in finally; server political state and consent are untouched. A positive drawdown case requires `drawdownExercised=true`, independent of a valid at-target no-op.

## War Council controls and news

Peace Standing sits left of the crowned R and War Standing sits right. Selected standing uses the gold marker; the peace dropdown has Automatic plus40–90% in5point steps. Explicit percentages replace the pressure curve. A player sovereign may keep Peace Standing during war. Losing sovereign/kingdom access closes the popup; a ruler change invalidates the previous ruler’s order. Non-sovereigns can read the current state. The popup shields the map from clicks and panning, closes outside/on Escape, and never changes map scale or issues party orders.

Native automation on the already guarded War Council uses `standing-peace`, `standing-war`, `standing-dropdown`, and `standing-percent:automatic|40|45|50|55|60|65|70|75|80|85|90`. These invoke the same sovereign and Government authorization path as clicks; they do not bypass campaign-test or provider isolation gates. Status exposes selected standing, percentage, authority and popup state. Prove min/max, Automatic, repeated clicks, wartime player choice, succession, no map click-through, news preferences/deduplication and save/reload on the disposable Current. NPC changes publish ordinary Diplomacy news after an actual mode transition; initialisation and unchanged daily orders publish nothing.

2026-11-04 visual revision2: user rejected the first rectangular control treatment. Built-in image generation produced paired wing-shaped black-marble/gold plates and one complete dropdown panel, consumed without modifying generated pixels. Runtime labels and a two-column Automatic/40–90% selector remain live. The protected map and existing shell/portraits are unchanged. Garrison wages now discount and round the regular stack product, preventing per-unit rounding from erasing low-tier savings; both native hero salary branches remain intact. Scoped render/fidelity evidence is in `.codex-build/war-economy/fiscal-20261104/ui/standing-v2-*`; full verification and native/human acceptance remain pending.

Independent-clan War Council is an existing supported entry: without a captured kingdom, both standing selectors are neutral and unavailable, and the percentage shows--. Do not interpret absence of a realm as Peace Standing. The separate no-realm preview preserves the non-sovereign current-kingdom state. The catalogue now has161browser states and336default render cases.

Review follow-up: Peace tab and percentage selector use adjacent, non-overlapping click targets. While the popup is open, the modal outside-click shield closes it on a header click; that click does not activate an underlying standing control. Strict bounded pressure parsing rejects malformed snapshots before cache replacement. Native pointer routing remains pending.

2026-11-04 final provider-free UI evidence: v5 full matrix passes336/336 and provider readiness33/33; the complete26-screen approved-reference fidelity audit passes using full-catalog-approved-reference-fidelity-contract.json. War Council typography snapshot was refreshed only for this screen after the authored widget hierarchy changed; every other frozen screen is identical. War Council has zero divergences;123 preexisting Government divergences remain explicitly unresolved. Candidate capture is presentation-only; native/human visual and interaction acceptance remain pending. Deployment is held for incomplete requested all-provider code review, including busy/failed Grok calls.

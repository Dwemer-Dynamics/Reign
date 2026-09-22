# DRAFT — Reign Player Guide maintenance rules

> Status: **inactive proposal**  
> Prepared: 2026-08-15  
> Deployment condition: add the proposed block to the applicable project `AGENTS.md` only after the user explicitly gives the go-ahead.

This file prepares the standing maintenance rule requested alongside the Reign player guide. Merely storing this draft does not activate it. The canonical instructions remain unchanged until the proposed block below is deliberately added to `AGENTS.md`.

## Proposed `AGENTS.md` block

```markdown
# Reign Player Guide maintenance

- The canonical living player and feature guide is `C:\Users\speed\Documents\Bannerlord Events\docs\REIGN_PLAYER_GUIDE.md`.
- This rule applies to every Reign task that adds, changes, removes, retires, renames, exposes, or hides a feature; changes a player-visible default, formula, probability, limit, duration, setting, hotkey, action, prompt-facing capability, UI route, safety restriction, or persistence behavior; or changes how a player can discover or understand existing behavior.
- Read the guide before making an affected feature change. Treat guide impact as part of the implementation and validation plan, not as optional follow-up documentation.
- In the same task, update every applicable part of the guide: the chapter summary, detailed feature section, contents or feature locator, exact-value/formula tables, MCM settings reference, executable-action reference, glossary, and cross-references.
- Preserve the intended distinction between generated narrative and executable native behavior. Document authority checks, safety gates, failure conditions, background behavior, and subtle effects that a player could not reliably infer by observation.
- Preserve useful history. When behavior is removed or superseded, label it retired or replaced and explain the current route instead of silently erasing the old behavior, unless the user explicitly requests historical removal.
- Do not claim the feature task complete until implementation, appropriate harness/Verification Lab evidence, manifest-selected `reign_validate` coverage, and the corresponding guide update are all complete. A partial implementation must not be documented as available production behavior.
- If a code or data change genuinely has no player-facing or player-relevant guide impact, state that conclusion and its reason in the task handoff. Do not omit the guide silently.
- When implementation and guide disagree, implementation is the temporary behavioral authority, but the mismatch is documentation drift that must be corrected in the same task whenever possible.
- Any agent or subagent delegated affected work must receive this maintenance rule and return the guide sections, exact values, validation evidence, and remaining documentation gaps relevant to its scope.
- Validate guide-only edits through the Reign MCP using the manifest-selected documentation plan. Do not run a build merely to validate Markdown.
```

## Deployment checklist

After the user authorizes deployment:

1. Re-read the current root `AGENTS.md` so concurrent instruction changes are preserved.
2. Add the proposed block once, without duplicating or weakening existing MCP, harness, validation, safety, lifecycle, roadmap, or testing-catalog rules.
3. Confirm the canonical guide path and section anchors still resolve.
4. Run the Reign MCP validation plan selected for the changed instruction and documentation paths.
5. Report the exact files changed and validation evidence.

Until that authorization is given, do not copy this block into `AGENTS.md` and do not describe it as an active project rule.

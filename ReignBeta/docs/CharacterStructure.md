# Bannerlord Reign Character Structure

ReignBeta keeps the module id during beta, but the character system is the Bannerlord Reign AI world foundation. Every character belongs to one campaign save, because memories, secrets, portraits, court pressure, diplomacy, and intrigue must not leak between playthroughs.

## Folder

```text
data/campaigns/{campaignId}/characters/{heroStringId}/
```

## Required Files

```text
schema_manifest.json
profile.json
template.json
characteristics.json
wealth.json
inventory.json
appearance.json
traits.json
background.json
voice.json
motivations.json
save_quirks.json
hidden_history.json
secrets.json
pressure.json
state.json
constructed.json
memory/summary.json
memory/relationships.json
memory/memories.jsonl
history/dialogue.jsonl
history/events.jsonl
history/decisions.jsonl
portraits/
```

## Public vs Private

Player-readable:

- `profile.json` game facts that are already knowable in Bannerlord
- `appearance.json` visible first-view presentation, worn gear, and apparent status
- `wealth.json` only when money is knowable through gameplay, barter, or server diagnostics
- `background.json` field `encyclopediaText`
- `portraits/custom.png` and `portraits/portrait.png`

Server-private:

- `hidden_history.json`
- `secrets.json`
- `save_quirks.json`
- `pressure.json`
- `memory/*`
- `history/decisions.jsonl`

The private files may shape behavior, leaks, lies, hints, refusal, bargaining, jealousy, rebellion, court pressure, or intrigue. They should not be directly exposed in the encyclopedia or normal chat UI.

## Construction Rule

The first LLM call involving a character must ensure that character is constructed before continuing. This applies to individual chat, party chat, court, tournaments, social events, intrigue, and future world simulation calls.

If construction fails twice, the server stops the response and returns a diagnostic instead of inventing an NPC answer.

## Traits

Visible Bannerlord traits remain fixed. Reign adds hidden traits on the same `-2..2` scale. The prompt uses sentence descriptors, not raw numbers, so models receive behavior guidance instead of abstract score tables.

Attractiveness is stored in `traits.json` and only affects responses through `appearanceSensitivity`.

## Prompt Stack

Always loaded:

- identity and position from `profile.json`
- visible first impression and status presentation from `appearance.json`
- compact money/material position from `wealth.json`
- compact carried resources from `inventory.json`
- trait descriptors from `traits.json`
- voice from `voice.json`
- motivations from `motivations.json`
- current state from `state.json`

Selected only when relevant:

- public backstory from `background.json`
- hidden history from `hidden_history.json`
- secrets and save quirks
- pressure meters
- relationship memory
- rolling memory summary
- selected memories
- recent dialogue/event transcript

This keeps responses fast and token-conscious without requiring every answer to carry the full character archive.

## Appearance and Status

The character stack separates true status from apparent status.

- `trueStatusLabel` is based on actual campaign role, clan, kingdom, captivity, and wealth.
- `visibleStatusLabel` is based on worn civilian equipment, armor/clothing value, weapons, and visible presentation.
- `statusMismatch` lets the prompt reason about disguise, understatement, and overstatement.

This means a lord in poor clothes can be underestimated, and a low-status character in expensive clothing can seem more important than they are. NPCs should not automatically know the truth. Worldly, suspicious, proud, or politically experienced NPCs may notice inconsistencies; naive or appearance-sensitive NPCs may be fooled.

The portrait source capture still gives the AI image system the visual reference, but `appearance.json` gives dialogue, court, intrigue, and diplomacy the social interpretation of what the character appears to be wearing.

## Personal Wealth vs Clan Backing

`wealth.json` separates personal money from clan reputation.

- `gold` is the character's personal money.
- `personalWealthTier` describes the character's immediate purse and carried resources.
- `clanGold` is the clan leader/clan treasury signal Bannerlord exposes.
- `clanWealthTier` describes the clan's financial reputation.
- `clanTier`, `clanRenown`, and `clanFiefCount` help measure social weight beyond cash.
- `clanSocialCredit` summarizes whether the person has little, minor, credible, strong, or great-house backing.
- `canLeanOnClanReputation` tells prompts that even a personally poor lord may posture, borrow credit, threaten consequences, request deference, or expect favors because of their house.

This matters for diplomacy, marriage, ransom, loans, court influence, insults, disguises, and bluffing. A character can lack coin on hand but still be socially expensive to offend.

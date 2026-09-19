# Reign Rebellion And Civil War

Reign owns this system completely. Bannerlord campaign state and native relationship values are authoritative; the local server interprets explicit dialogue and correspondence commitments but does not roll or decide NPC outbreaks.

## NPC Outbreaks

Once per seven campaign days, each living, free, non-mercenary NPC vassal clan leader whose native relation with the current ruler is strictly below `-20` receives an independent saved `d100` roll. A result of `1-5` starts a rebellion. There is no strength, fief, coalition, or minimum-support gate.

Every roll is stored in the native save. Only one rebellion can start in a kingdom at a time. If several eligible leaders succeed in the same week, the leader with the lowest ruler relation wins the tie, followed by stable clan StringId ordering. A resolved realm has a 63-day cooldown.

At outbreak, all other eligible NPC vassal leaders in the parent kingdom are polled once:

- join rebels when relation with the rebel leader is strictly higher than relation with the ruler;
- otherwise remain loyalist;
- NPC sides do not change during the war.

The player remains loyalist by staying in the parent kingdom and may later join the active NPC rebel leader through an explicitly accepted Reign conversation or letter.

## Player Declaration And Recruitment

A living, free player who leads a non-ruling vassal clan may challenge the current ruler at any relationship level:

- in direct Reign conversation; or
- by correspondence stating an explicit challenge and declaration.

The declaration is unilateral. The ruler cannot refuse it. A mailed declaration executes when dispatched, while the physical letter and narrative reply still use normal travel time.

The player starts alone in a real `reign_rebels_*` kingdom. After declaration, the player may ask any living, free lord from any realm to join through direct conversation or mail. The server queues recruitment only after the NPC's LLM response unambiguously accepts. Because Bannerlord kingdom membership is clan-based, the accepting lord's entire clan transfers to the player rebel kingdom and remains committed until resolution, then returns to its recorded origin when possible.

## Leadership-Bound Resolution

The sole objective is the throne. There are no grievance, conspiracy, coalition, ultimatum, viability, territorial-score, strategic-advantage, succession, or automatic-stalemate stages.

Loyalists win when the named rebel leader:

- is captured by the loyalist civil-war kingdom;
- is killed; or
- explicitly surrenders.

Rebels win when the challenged ruler:

- is captured by the rebel civil-war kingdom;
- is killed;
- is deposed; or
- explicitly surrenders.

Capture by a foreign faction does not decide the rebellion. Generic peace is immediately superseded by the continuing civil war and cannot bypass the leadership outcome. There is no rebel succession.

On rebel victory, the rebel clan becomes the reunited parent kingdom's ruling clan. On loyalist victory, the original rule is restored. Committed clans are returned according to their saved pre-rebellion origins before judgment.

## Judgment

The winning leader judges every defeated current clan leader separately.

An NPC winner receives a persisted roll:

```text
score = d100 + (15 × native Mercy level) + round(native relation with defeated lord / 5)
```

- `67+`: freedom
- `34-66`: imprisonment
- `33 or less`: execution

Freedom applies `+30` native relation with the winner. Imprisonment applies `+10`. The bonus is applied after that lord's fate.

When the player wins, a saved sequential inquiry requires a personal choice of freedom, imprisonment, or execution for every defeated clan leader. Pending judgments resume after save/load. A losing player is rolled like any NPC lord and may be freed, imprisoned, or executed through Bannerlord's native player-death flow when death is enabled.

## Actions And Verification

The validated action mappings are:

- `start_ruling_clan_rebellion`
- `recruit_lord_to_rebellion`
- `join_rebellion`
- `surrender_rebellion`

Focused offline verification:

```powershell
ReignServer.exe --run-rebellion-tests
ReignServer.exe --run-tests --suite actions --case start_ruling_clan_rebellion --canned --json
ReignServer.exe --run-tests --suite actions --case recruit_lord_to_rebellion --canned --json
ReignServer.exe --run-tests --suite actions --case join_rebellion --canned --json
ReignServer.exe --run-tests --suite actions --case surrender_rebellion --canned --json
```

The roadmap remains incomplete until a disposable live campaign proves outbreak timing, declaration, recruitment, save/reload, both leadership outcomes, all three judgments including player defeat, and rebel-realm cleanup.

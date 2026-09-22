# Bundled Arena Overhaul

This source tree begins with the author's published Arena Overhaul source at commit
`6cdf70f05ed879b12e7eb454a152e56a0814794b`. The user reports permission
from the author to include Arena Overhaul permanently in Reign. The upstream
license is retained in `LICENSE` and copied into the shipped module as
`ModuleData/ArenaOverhaul-LICENSE.txt`.

Reign builds this as `ArenaOverhaul.1.4.6.dll`, a separate submodule inside the
single `ReignBeta` launcher module. Its MCM identity remains `ArenaOverhaul_v1`
and its settings folder remains `Arena Overhaul`, allowing existing settings to
load. Resource paths point to Reign's module folder. Bannerlord 1.4.8 API
adaptations and the installed prize-variety/debug-menu behavior are maintained
here. The local decompiled rebuild was used only as a compatibility reference;
it is not included as managed source.

`TeamTournamentOutcome` captures the winning team immediately before Arena
Overhaul emits Bannerlord's single-winner completion callback. Reign uses the
snapshot to attribute teammates in world history, reputation and celebration
dialogue. Native play must still verify the launcher, MCM, team tournament,
practice modes and save/load behavior after deployment.

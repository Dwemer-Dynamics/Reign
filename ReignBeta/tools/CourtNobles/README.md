# Staged court nobles

## Nord extension (2026-11-06)

The active new-campaign roster has **133 households and 798 nobles**. War Sails
adds four towns and nine castles; `nord-households-authored.json` supplies their
13 distinct families and 78 individual life seeds. All new clans start at **tier
1**, can gain tiers normally, remain landless, and use the existing rank-based
equipment policy. Their homes grant no ownership, income, troops or money.

`author_nord_households.py --game <licensed-game-root> --evidence <task-evidence>`
appends only this extension using installed Nord face ranges and DLC noble
skill/equipment templates. It checks native settlement coverage and uniqueness.
Conflicting existing definitions fail; repeated identical runs add nothing.
The user requires a **completely new campaign** for the combined economy/Nord
revision. No old-save population backfill is supported or promised. Completed
legacy migration versions are not increased or replayed.

The scripts below retain the original 120-house staging/rename workflow. Their
120/720 checks describe that legacy input. Never overwrite expanded active XML
with their legacy output.

Final normal portraits use Codex built-in **text-only** image generation from
inspected native faces, the Scandinavian Nord facial layer, tier-1 landowner
clothing, exact native age/physique and complete normal portrait modifiers.
`Finalize-NordPortraits.ps1 -ServerAssembly <canonical-artifact/ReignServer.dll>
-PortraitDirectory <owned-folder>` calls the exact compiled production detector,
approved model and focused crop/commit helpers to create the four derivatives.
It never invokes server startup or a database and leaves the master unchanged.
For this reviewed extension, `-ReviewedHeadroomFaceHeights 0.55` retains complete
hair in compact portraits. The authoring-only override records the measured crop
top and keeps production dimensions, face detection, encoding and commit checks;
it does not change runtime crop defaults or earlier artwork.
`-DetectOnly` checks without writes. Keep build/hash receipts, inspect masters
and derivatives, package them in `_shared` and update the tracked shared inventory
before final validation. `Package-NordPortraits.py --evidence-root <task-evidence>`
checks all 78 accepted masters, derivatives, native metadata and provenance,
preserves the existing library, and publishes its inventory only when complete.
The combined economy task owns the separately authorized deployment.

`Generate-CourtNobles.ps1` reads the installed Sandbox `settlements.xml`, `heroes.xml`, and `lords.xml` without changing them. It generates a separate staging package at `staging/court_nobles`.

The package contains 120 households: one for every town and castle. Each household has six adult `Occupation.Lord` heroes assigned to the initial holding clan:

- father, age 44
- mother, age 42
- children, ages 25, 23, 20, and 19

The children have explicit father and mother links and the parents have reciprocal spouse links. Each generated `NPCCharacter` inherits the skills, traits, equipment sets, voice, and combat group of a matching current noble from the holding clan where possible, or otherwise from the holding culture. Names and house names are authored from culture-specific syllable sets and checked against official game XML. Fixed face data is removed: Bannerlord generates culture-appropriate parent faces, the campaign behavior constrains every editable face slider to the equivalent of -25 through 24, and children blend their two corrected parents with a stable per-character seed. Every court noble is assigned a culture-valid hairstyle with the bald index excluded; female beards are cleared.

Run from the module root:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\CourtNobles\Generate-CourtNobles.ps1
powershell -ExecutionPolicy Bypass -File .\tools\CourtNobles\Test-CourtNobles.ps1
```

The staging package is the generator's reviewable output. The active copies are installed under the module's `ModuleData` folder and registered in `SubModule.xml`.

The already-compiled campaign behavior recognizes the `reign_court_` roster IDs but remains inert until the staged XML is activated. Once active for a new campaign, it places each unpartied household in its original holding, allows normal settlement movement for tournaments, and makes a court noble ineligible to lead a party while any regular clan lord is available. Vanilla's existing clan party limit still caps the total number of parties. Marriage, pregnancy, and all Reign character, portrait, relationship, and social systems apply to this roster exactly as they do to other nobles.

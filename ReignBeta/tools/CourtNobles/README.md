# Staged court nobles

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

# Ambassador Screen Prototype

This is an isolated, previewer-only Ambassador UI prototype. It does not modify
or deploy to the live Reign module.

## Runtime integration contract

- The rail displays five cards and scrolls horizontally for additional ambassadors.
- Each `.ambassador-card__portrait` exposes a `data-portrait-binding` value such
  as `Ambassadors[0].FullBodyPortrait` and a `data-portrait-file="portrait.png"`
  contract. The prototype uses real cached Reign NPC `portrait.png` examples.
- Portraits preserve their source ratio and use proportional `cover` cropping in
  a masked recess. `assets/ambassador-card-overlay-refined.png` is a true-alpha,
  image-to-image-derived overlay rendered above the portrait, so portraits cannot
  cover any gold rail, corner ornament, or the black-marble name plaque.
- The leading-clan banner is a separate runtime layer beneath
  `assets/ambassador-banner-overlay.png`, another true-alpha overlay. Runtime
  banner art therefore remains inside the pennant recess and cannot clip across
  the frame. The banner, kingdom, character name, and clan/house name are never
  baked into the background plate.
- Card selection and scrolling work in the prototype. The obsolete hookup/status
  strip has been removed.
- Conversation, Establish Relations, Remove Ambassador, and Court navigation are
  explicitly reserved for their later feature passes.

When the preview server is running, open:

`/tools/GauntletXmlPreviewer/prototypes/AmbassadorScreen/index.html`

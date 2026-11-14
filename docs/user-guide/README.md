# Reign player manual

Open [index.html](index.html) in a current desktop browser. Keep the `assets` folder beside it. The manual's contents, A–Z index, pictures and keyword search work without a web server or internet connection. Its single external reference is a deliberately clicked link to the base game's official Steam requirements.

The guide covers ordinary play and rulership in plain language. Hardware figures identify published base-game requirements; Reign has no benchmarked RAM minimum or typical-use figure yet. The former 32 GB planning recommendation was withdrawn. The guide explicitly requires a cloud LLM for AI dialogue and a separately configured cloud image provider for optional new AI images. Offline *reading* does not imply offline AI gameplay.

Use the search field or Ctrl+K, the chapter contents, A–Z index, or browser Find. Search accepts everyday terms such as “wife,” “troops expensive,” “NPC won't leave,” and “become senator.” Enter opens the first result; Escape clears a search or closes the small-screen contents menu. Print uses the complete guide and index, including while a search is open.

## Files and maintenance

- `manual.md` is the prose source.
- `coverage-config.json` assigns sources, preview notices, search aliases and approved reference pictures.
- `coverage.json` records exact source/illustration hashes and chapter coverage.
- `captures.json` records user-supplied in-game pictures, preserved originals, dimensions, hashes and the visible evidence each provides.
- `coverage-audit.md` records the editorial coverage review and its boundaries.
- `action-reference.json` maps the conversation action registry to the guide's reference tables. It identifies alternate labels and retained follow-up entries. The original registry and builder sources used for this edition are hash-verified on E:.
- `requirements-evidence.json` separates observed hardware data from recommendations.
- `template.html`, `guide.css`, and `guide.js` supply the offline reader.
- `build.py` builds the HTML, index data and original explanatory SVG diagrams using Python's standard library. Approved screen references and player captures are copied byte for byte. Their separate manifest entries prevent native replacements from being overwritten on rebuild. Image dimensions retain the original aspect ratio.
- `publish.py` creates the single-file Desktop reader with all pictures embedded and a compressed ZIP containing that same HTML file. Once configured on this machine, every successful rebuild verifies and refreshes both `Reign User Guide.html` and `Reign User Guide.zip` automatically. Machine-specific publication settings live in `%USERPROFILE%/.reign/user-guide-publication.json`, outside source control.
- `verify.py` checks links, image provenance, source hashes, coverage, offline dependencies and the absence of gameplay tuning formulas.
- `verify-search.cjs` checks real player-worded queries against the actual manual content with Node.js.
- `update-guide.py` and `update-baseline.json` detect source drift against an exact reviewed snapshot. `verify-updates.py` exercises their review gates without changing the product.

When returning to this chat, **“Update the Reign guide”** is enough to request the following workflow. This is an on-demand review, not a background watcher or scheduled automation.

1. Run `python docs/user-guide/update-guide.py status`. This hashes both committed and uncommitted source, including new and removed files, across the client and paired server. It reports affected chapters and unknown new modules.
2. If changes exist, save a review report on E:, for example `python docs/user-guide/update-guide.py status --report E:/CodexStorage/Projects/Reign/UserGuide/updates/review.json`. Read that report and inspect each actual change. `python docs/user-guide/update-guide.py diff --path <exact-source-path>` compares current contents with preserved baseline bytes, including a baseline that was uncommitted. It does not substitute the old Git commit for the actual reviewed source.
3. Review player behavior, defaults, hotkeys, menus, authority, persistence, images and acceptance status. Update affected topics and search aliases, or explain why an individual change has no player impact. Each report entry needs a `review` object with `outcome` (`updated` or `no-player-impact`), a specific `reason`, and actual revised `topics` for `updated`. Deferred work blocks baseline advancement. New unclassified modules must first be mapped through `monitorMappings` in `coverage-config.json`.
4. Accept only that exact fully reviewed report: `python docs/user-guide/update-guide.py accept --review <report-path> --note <specific-editorial-review-note>`. Stale reports, omitted changes, unreviewed entries, deferred entries, unknown topics and unsupported “updated” claims are rejected. The prior baseline and accepted review are preserved on E:. Exact source snapshots are compressed there and independently hash-verified, keeping future comparisons possible even for uncommitted work.
5. Rebuild, run document/search/update checks, obtain the exact-path Reign documentation plan/status, validate with this task's UUID, and refresh the distribution ZIP. The configured Desktop HTML and compressed ZIP are automatically refreshed by `build.py`. Do not advance the baseline merely to make a build pass.

After the source review is current, rebuild and check from the Reign checkout:

```text
python docs/user-guide/build.py
python docs/user-guide/verify.py
node docs/user-guide/verify-search.cjs
python docs/user-guide/verify-updates.py
```

These are document operations, not game builds or campaign tests. Use Reign MCP for the manifest-selected documentation validation. The document builder checks the source baseline before and after generation and refuses unreviewed source drift. Rebuilding alone cannot mark changed gameplay reviewed. If E: or paired source is unavailable, report that gap; do not create an unverified replacement baseline or silently use another archive drive.

## Desktop copy for sharing

The user requested an ordinary HTML file on the Desktop, plus a ZIP for easier sending. `Reign User Guide.html` contains the guide, search, styling and all pictures; recipients can open that single file in a browser without an assets folder or internet. `Reign User Guide.zip` contains exactly that HTML file: extract it, then open the HTML. Picture links still open full-size images. Existing older Desktop guides are preserved.

Publication is enabled explicitly with `python docs/user-guide/publish.py --configure-desktop <actual-desktop-directory>`. After configuration, `build.py` refreshes both files automatically, and `python docs/user-guide/publish.py` can republish the current verified edition. The publisher verifies the document, every embedded picture and the ZIP's extracted contents, then replaces each file atomically. It does not create a background watcher. Changes must be made in the canonical guide sources and rebuilt; treat the Desktop files as shareable outputs.

Do not overwrite the older `docs/REIGN_PLAYER_GUIDE.md` technical reference with this manual. It contains retained history and concurrent feature updates. The new manual deliberately omits numeric balance tables and formulas while retaining small consequential changes.

## Review and evidence

Claude, Grok and Gemini reviewed the editorial plan using selected task-authorized text. Their suggestions informed the beginner route, audience/privacy explanations, everyday aliases, picture labels, preview notices and offline/print checks. They did not inspect the repository or certify the gameplay claims.

Full consultation results and delivery/evidence packages belong in `E:/CodexStorage/Projects/Reign/UserGuide/2026-11-06-01a1b1c8`. Source-controlled working documentation remains in this folder. Browser inspection was interrupted by the user's physical Escape key and remains deferred; no browser-rendering pass or native-game acceptance is claimed.

The November 9 review also checked the deployed wedding and economy worktrees and recent Reign task records. Their approval list, Claude review, source inventories, and action registry evidence are in `E:/CodexStorage/Projects/Reign/UserGuide/updates/2026-11-09-evidence`. For updates from a separate worktree, compare its actual deployment evidence alongside the canonical source monitor. Recount action IDs and menu aliases when the registry changes.

## In-game picture replacement progress

On November 6 the user supplied original-resolution captures of field banter, Individual Chat, Correspondence, wilderness roleplay, a Court audience, a Social Event, Family Chambers, Spymaster, War Council, Ambassadors, the Economic Report, Government Agenda, Homes directory and Visit the Madam. These are integrated without cropping, repainting or resizing their pixels. Correspondence also illustrates reputation-led house invitations. Original bytes and receipts are preserved on E:; capture/build date and installed version are unknown. These pictures show the visible screen, not complete feature acceptance.

Eight remaining layout references still need actual captures: Party Chat, Castle Layout, Training Yard, Court, Royal Council, Clan Accords, Diplomacy/news announcement and Memory Book. Further examples requested include the action builder (opening and review), an attached chat action, new-proposal drafting, military standing and Reign settings.

For each new capture, preserve and hash the exact original on E:, add its provenance to `captures.json`, remove a replaced basename from `referenceImages`, and update its caption and explanation. Rebuild and verify the bundle. Do not launch/control Bannerlord or interrupt another task to obtain captures; the user is supplying them. Never infer gameplay completion from dialogue text or invent a screenshot’s capture date/version.

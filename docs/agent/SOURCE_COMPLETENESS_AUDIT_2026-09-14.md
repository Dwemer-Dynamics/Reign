# Source completeness audit — 2026-09-14

## Result

The paired Reign repositories now own every identified first-party file required to reproduce the current local build. The complete approved shared portrait library is private source, not a machine-local packaging dependency.

## First-party source accounted for

- `ReignContent/PortraitCache/_shared`: 12,278 files in 1,228 character directories, totaling 5,034,854,753 bytes. This includes all 221 accepted portrait sets from the physique rebuild effort.
- `ReignContent/shared-portrait-inventory.json`: repository-relative size and SHA-256 manifest for every shared portrait file.
- `ReignBeta/artwork/cinematic-title`: the separately validated final title-card image and its generation prompt are tracked with the client source.
- Client module, MCP/integration tooling and private documentation remain owned by the private Reign repository.
- Server, shared contracts/helpers, native portrait pipeline and release tooling remain owned by the public ReignServer repository.

No other untracked first-party build or content output was found in the active D: source checkouts during this audit. Campaigns, credentials, logs, caches, generated binaries and player state remain intentionally excluded because they are runtime/user state rather than reproducible source.

## Enforcement

- Approved shared portrait PNGs use Git LFS, while their metadata and prompts use normal Git storage.
- The canonical release test verifies the complete inventory, exact file set, file sizes, SHA-256 hashes, Git-index ownership and LFS rule.
- The package builder accepts only the fixed tracked inventory and source root. It rejects absolute or machine-local inventory roots, duplicate entries, missing or extra files, untracked files and hash/size mismatches.
- Any accepted portrait edit must update the tracked source and regenerate the inventory in the same task before validation, deployment or publication.
- Repository instructions now define a build as incomplete when a required first-party file exists only in a runtime, staging, output or machine-local directory.

## External inputs

The remaining non-Git inputs are third-party dependencies rather than first-party source: PostgreSQL, Codex, Bannerlord dependency modules, the Visual C++ runtime, the embedding model, vector runtime components, NuGet packages and the Inno Setup compiler. Their versions or payload proofs are maintained by the existing dependency, model and package locks. These inputs are not substitutes for first-party source and are not copied into the public repository.

## Verification contract

Before publication, run the inventory updater in `--check` mode, compare the installed shared library to the tracked inventory, run both repository hygiene checks, and complete the canonical paired-repository validation. Publication must use the validated commits; deployment and packaging must report the same source fingerprint.

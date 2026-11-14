# Reign Linux deployment and client artifacts

## Unstable tester distribution

User requirement, 2026-11-14: every publication of the completed latest full build targeting `unstable` includes one large ZIP uploaded to the user's Google Drive, suitable for sharing with testers. Include both the matching server and its installation system, the Reign mod itself, and the latest instructions HTML. This applies to the draft-PR publication route as well as an explicitly authorized branch promotion; it does not authorize merging a draft or publishing to `dev`/`reign`.

Use the exact successful manifest-selected Release artifacts and canonical Linux component, without rebuilding unchanged qualified source. Bind both source revisions and artifact/report fingerprints in a package manifest. Include the complete distributable ReignBeta module and tracked shared-portrait inventory, its required Windows portrait helper, Linux server payload, supported dedicated-WSL installation/lifecycle scripts and declared prerequisites, and current HTML instructions with every required local stylesheet, script and image. Do not substitute a source archive, developer deployment script requiring absent checkouts, historical installer or incomplete payload for an installable build.

Keep saves, campaign portraits, private settings/credentials, runtime databases, logs, caches, licensed game assemblies and unredistributable dependencies out of the ZIP. Preserve third-party licensing and document externally installed prerequisites. Validate package contents and hashes against the qualified artifacts, check the extracted instructions' links/assets, and exercise the supported installer in an isolated environment where available. Record native/clean-install acceptance gaps separately; never label an untested or incomplete installer as verified.

Retain the package and durable inventory/hash/upload receipts under `E:\CodexStorage\Projects\Reign` per the storage policy. Discover and reuse an appropriate existing Drive destination; otherwise upload to My Drive without reorganizing existing files. Verify uploaded name, size/checksum when available, file ID and download/view link through Drive metadata readback. Preserve existing sharing settings unless the user authorizes changes. Publication is incomplete if packaging or upload is blocked; report the exact missing capability or prerequisite and preserve the recoverable checkpoint.

ReignServer targets `linux-x64` only and runs in its own separately registered `ReignServer` WSL distro. Windows remains the Bannerlord client and native portrait-renderer platform. There is no standalone Windows server installer or packaged Windows vector worker.

Use the canonical validator for product validation and `ReignMcp/scripts/reign-validate.ps1 -LinuxServer -Restore -RequestingTaskId <task UUID>` for the self-contained Linux artifact. The Linux report records its source fingerprint and file inventory. Private verification source is excluded from published server artifacts.

Deploy validated artifacts with `ReignServer/scripts/Deploy-LocalWsl.ps1`. The Windows portrait helper is built with the client. Preserve `ReignServer/data` and retained `ReignServer/runtime` builds, campaigns, credentials and the PostgreSQL database. A deployment does not authorize a release or establish native/in-game acceptance.

Server verification runs in WSL when Codex runs on Windows as the `reign` user. The canonical process runner requires `REIGN_VALIDATION_MODE=1` and `REIGN_DB_NAME=ReignValidation`, maps run-owned paths, and bounds the Linux child process group. The selected distro is `REIGN_WSL_DISTRO` (default `ReignServer`). Never point validation at the production database or data directory.

Approved first-party shared portraits belong in the private Reign module at ReignBeta/PortraitCache/_shared, with the tracked ReignBeta/PortraitCache/shared-portrait-inventory.json. Deploy and package the verified module inventory; do not rely on a separate or machine-local first-party portrait payload.

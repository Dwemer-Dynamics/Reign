# Reign Linux deployment and client artifacts

ReignServer targets `linux-x64` only and runs under DwemerDistro. Windows remains the Bannerlord client and native portrait-renderer platform. There is no standalone Windows server installer or packaged Windows vector worker.

Use the canonical validator for product validation and `ReignMcp/scripts/reign-validate.ps1 -LinuxServer -Restore -RequestingTaskId <task UUID>` for the self-contained Linux artifact. The Linux report records its source fingerprint and file inventory. Private verification source is excluded from published server artifacts.

Deploy validated artifacts with `ReignRelease/Deploy-LocalWsl.ps1`. The Windows portrait helper is built with the client. Preserve `ReignServer/data` and retained `ReignServer/runtime` builds, campaigns, credentials and the PostgreSQL database. A deployment does not authorize a release or establish native/in-game acceptance.

Server verification runs in WSL when Codex runs on Windows. The canonical process runner requires `REIGN_VALIDATION_MODE=1` and `REIGN_DB_NAME=ReignValidation`, maps run-owned paths, and bounds the Linux child process group. The selected distro is `REIGN_WSL_DISTRO` (default `DwemerAI4Skyrim3`). Never point validation at the production database or data directory.

Approved first-party shared portraits belong in the private Reign module at ReignBeta/PortraitCache/_shared, with the tracked ReignBeta/PortraitCache/shared-portrait-inventory.json. Deploy and package the verified module inventory; do not rely on a separate or machine-local first-party portrait payload.

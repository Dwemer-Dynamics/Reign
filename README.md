# Reign

Reign is the Bannerlord game module. Its local companion server and shared contracts are maintained in [ReignServer](https://github.com/Dwemer-Dynamics/ReignServer). Internal `ReignBeta` module and assembly names remain for save compatibility.

Both source repositories are public. The latest development channel is **unstable**; **dev** is the beta channel and **reign** is the production/default branch. See the [illustrated player guide](docs/user-guide/README.md) for gameplay documentation.

For development, use sibling local checkouts named **Reign** and **ReignServer**, then run `../ReignServer/scripts/Connect-Repositories.ps1`. Server directories are linked into this workspace for integration tools; edit and commit them in ReignServer. Client code, fixed interface assets and integration tools belong here. All work stays local; the two Dwemer-Dynamics repositories are the publishing destinations.

Read [repository recovery](docs/agent/REPOSITORY_RECOVERY.md), [launch implementation](docs/agent/REIGN_LAUNCH_IMPLEMENTATION.md), and [release packaging proof](docs/agent/testing/RELEASE_PACKAGING.md) before building or distributing. The complete Windows preview package has passed an isolated local reinstall, save migration and portrait-discovery repair. On 2026-09-14 the user confirmed the installed build was working and authorized publication of both source repositories.

Installer binaries and independently locked third-party runtime/model downloads are distributed separately from Git. Every first-party game asset, including the complete approved shared portrait seed library under `ReignBeta/PortraitCache/_shared`, is inside the directly distributable `ReignBeta` module folder. Portrait PNGs use Git LFS and `ReignBeta/PortraitCache/shared-portrait-inventory.json` locks every shipped file. Campaign portraits and player state remain outside Git. Execution on a second computer remains separate acceptance work; the available acceptance evidence is from the local reinstall and user testing.

## Get the latest development source

Install Git LFS, then clone both repositories on the same channel:

```powershell
git lfs install
git clone --branch unstable https://github.com/Dwemer-Dynamics/Reign.git
git clone --branch unstable https://github.com/Dwemer-Dynamics/ReignServer.git
git -C Reign lfs pull
& .\ReignServer\scripts\Connect-Repositories.ps1
```

A source checkout needs the licensed Bannerlord installation and the prerequisites in the recovery/build guides. The complete approved first-party portrait libraries are tracked through Git LFS; campaign data, credentials, game-owned assemblies and separately distributed installer binaries are excluded. Native new-game and cross-machine acceptance remain separate from automated source validation.

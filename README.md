# Reign

Reign is the Bannerlord game module. Its local companion server and shared contracts are maintained in [ReignServer](https://github.com/Dwemer-Dynamics/ReignServer). Internal `ReignBeta` module and assembly names remain for save compatibility.

For development, use sibling local checkouts named **Reign** and **ReignServer**, then run `../ReignServer/ReignRelease/Connect-Repositories.ps1`. Server directories are linked into this workspace for integration tools; edit and commit them in ReignServer. Client code, fixed interface assets and integration tools belong here. All work stays local; the two Dwemer-Dynamics repositories are the publishing destinations.

Read [repository recovery](docs/agent/REPOSITORY_RECOVERY.md), [launch implementation](docs/agent/REIGN_LAUNCH_IMPLEMENTATION.md), and [release packaging proof](docs/agent/testing/RELEASE_PACKAGING.md) before building or distributing. The complete Windows preview package has passed an isolated local reinstall, save migration and portrait-discovery repair. On 2026-09-14 the user confirmed the installed build was working and authorized publication of both source repositories.

Installer binaries, runtime/model downloads and bulk portrait content are distributed separately from Git. Private download hosting and execution on a second computer remain separate follow-up work; the available acceptance evidence is from the local reinstall and user testing.

# Reign

Reign is the Bannerlord game module. Its local companion server and shared contracts are maintained in [ReignServer](https://github.com/Dwemer-Dynamics/ReignServer). Internal `ReignBeta` module and assembly names remain for save compatibility.

For development, use sibling local checkouts named **Reign** and **ReignServer**, then run `../ReignServer/ReignRelease/Connect-Repositories.ps1`. Server directories are linked into this workspace for integration tools; edit and commit them in ReignServer. Client code, fixed interface assets and integration tools belong here. All work stays local; the two Dwemer-Dynamics repositories are the publishing destinations.

Read [repository recovery](docs/agent/REPOSITORY_RECOVERY.md), [launch implementation](docs/agent/REIGN_LAUNCH_IMPLEMENTATION.md), and [release packaging proof](docs/agent/testing/RELEASE_PACKAGING.md) before building or distributing. Launch packaging and clean-computer acceptance are in progress. No launch-ready installer is claimed by this source import.

using TaleWorlds.MountAndBlade;

namespace Bannerlord.EditorBridge;

public sealed class SubModule : MBSubModuleBase
{
    // The editor-facing work is owned by EditorBridgeController, which must be
    // attached to one entity in a disposable scene. Keeping this class empty
    // avoids a global bridge that could act on an unintended scene.
}

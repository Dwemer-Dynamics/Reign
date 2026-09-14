using Bannerlord.EditorMcp.Protocol;

namespace Bannerlord.EditorMcp.Server;

public sealed class BridgeServerOptions
{
    public string PipeName { get; init; } = ProtocolConstants.DefaultPipeName;
    public string GameDirectory { get; init; } = string.Empty;
    public string ProjectDirectory { get; init; } = string.Empty;
    public string ModDirectory { get; init; } = string.Empty;

    public static BridgeServerOptions FromEnvironment()
    {
        return new BridgeServerOptions
        {
            PipeName = Read("BANNERLORD_EDITOR_PIPE", ProtocolConstants.DefaultPipeName),
            GameDirectory = Read("BANNERLORD_GAME_DIR", string.Empty),
            ProjectDirectory = Read("BANNERLORD_PROJECT_DIR", string.Empty),
            ModDirectory = Read("BANNERLORD_MOD_DIR", string.Empty)
        };
    }

    private static string Read(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}

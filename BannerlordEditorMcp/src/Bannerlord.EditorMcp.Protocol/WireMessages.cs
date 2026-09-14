using System;
using System.Collections.Generic;

namespace Bannerlord.EditorMcp.Protocol;

public sealed class BridgeHello
{
    public string Kind { get; set; } = "hello";
    public string ProtocolVersion { get; set; } = ProtocolConstants.Version;
    public string BridgeInstanceId { get; set; } = string.Empty;
    public string GameVersion { get; set; } = string.Empty;
    public string TargetModuleId { get; set; } = string.Empty;
    public string SceneName { get; set; } = string.Empty;
    public bool EditorMode { get; set; }
    public DateTimeOffset ConnectedAtUtc { get; set; }
}

public sealed class BridgeRequest
{
    public string Kind { get; set; } = "request";
    public string ProtocolVersion { get; set; } = ProtocolConstants.Version;
    public string RequestId { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string TargetModule { get; set; } = string.Empty;
    public string Scene { get; set; } = string.Empty;
    public string ExpectedSceneRevision { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public bool DryRun { get; set; }
    public string ArgumentsJson { get; set; } = "{}";
}

public sealed class BridgeResponse
{
    public string Kind { get; set; } = "response";
    public string ProtocolVersion { get; set; } = ProtocolConstants.Version;
    public string RequestId { get; set; } = string.Empty;
    public bool Ok { get; set; }
    public bool Applied { get; set; }
    public bool RequiresSave { get; set; }
    public string SceneRevision { get; set; } = string.Empty;
    public string ResultJson { get; set; } = "{}";
    public string ErrorCode { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public List<BridgeChange> Changes { get; set; } = new List<BridgeChange>();
    public List<string> Warnings { get; set; } = new List<string>();

    public static BridgeResponse Rejected(BridgeRequest request, string code, string error, string revision)
    {
        return new BridgeResponse
        {
            RequestId = request.RequestId,
            Ok = false,
            Applied = false,
            SceneRevision = revision,
            ErrorCode = code,
            Error = error
        };
    }
}

public sealed class BridgeChange
{
    public string Operation { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

public sealed class EditorCapabilities
{
    public bool InspectEntities { get; set; }
    public bool CreateEmptyEntity { get; set; }
    public bool InstantiatePrefab { get; set; }
    public bool SetTransform { get; set; }
    public bool SetTags { get; set; }
    public bool CreatePath { get; set; }
    public bool ReadTerrain { get; set; }
    public bool ImportHeightmap { get; set; }
    public bool ModifyTerrain { get; set; }
    public bool InspectNavmesh { get; set; }
    public bool GenerateNavmesh { get; set; }
    public bool SaveScene { get; set; }
}

public sealed class EditorStatus
{
    public bool Connected { get; set; }
    public bool EditorMode { get; set; }
    public bool BridgeEnabled { get; set; }
    public bool ReadOnly { get; set; }
    public bool SafeWritesArmed { get; set; }
    public string BridgeInstanceId { get; set; } = string.Empty;
    public string GameVersion { get; set; } = string.Empty;
    public string TargetModuleId { get; set; } = string.Empty;
    public string SceneName { get; set; } = string.Empty;
    public string SceneModulePath { get; set; } = string.Empty;
    public string SceneRevision { get; set; } = string.Empty;
    public string LastCommand { get; set; } = string.Empty;
    public string LastError { get; set; } = string.Empty;
}

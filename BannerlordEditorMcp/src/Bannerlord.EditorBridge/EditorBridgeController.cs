using System;
using Bannerlord.EditorMcp.Protocol;
using TaleWorlds.DotNet;
using TaleWorlds.Engine;

namespace Bannerlord.EditorBridge;

public sealed class EditorBridgeController : ScriptComponentBehavior
{
    [EditableScriptComponentVariable(true, "")]
    public bool BridgeEnabled = true;

    [EditableScriptComponentVariable(true, "")]
    public bool ReadOnly = true;

    [EditableScriptComponentVariable(true, "")]
    public bool SafeWritesArmed = false;

    [EditableScriptComponentVariable(true, "")]
    public string TargetModuleId = string.Empty;

    [EditableScriptComponentVariable(true, "")]
    public string DisposableSceneName = string.Empty;

    [EditableScriptComponentVariable(true, "")]
    public string PipeName = ProtocolConstants.DefaultPipeName;

    [EditorVisibleScriptComponentVariable(true)]
    public string ConnectionStatus = "Disconnected";

    [EditorVisibleScriptComponentVariable(true)]
    public string LastCommand = string.Empty;

    [EditorVisibleScriptComponentVariable(true)]
    public string LastError = string.Empty;

    private readonly string _bridgeInstanceId = Guid.NewGuid().ToString("D");
    private PipeBridgeClient? _client;
    private BridgeCommandDispatcher? _dispatcher;

    public override TickRequirement GetTickRequirement() => TickRequirement.Tick;

    protected override void OnEditorInit()
    {
        base.OnEditorInit();
        BridgeEnabled = true;
        ReadOnly = true;
        SafeWritesArmed = false;
        _dispatcher = new BridgeCommandDispatcher(this, _bridgeInstanceId);
        ApplyEnabledState();
    }

    protected override void OnEditorTick(float dt)
    {
        base.OnEditorTick(dt);
        ApplyEnabledState();
        ConnectionStatus = _client?.Connected == true ? "Connected" : "Disconnected";

        if (_client is null || _dispatcher is null)
        {
            return;
        }

        for (var i = 0; i < 8 && _client.TryDequeue(out var pending); i++)
        {
            if (pending is null)
            {
                continue;
            }

            LastCommand = pending.Request.Command;
            try
            {
                var response = _dispatcher.Handle(pending.Request);
                LastError = response.Ok ? string.Empty : response.Error;
                pending.Complete(response);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                pending.Complete(BridgeResponse.Rejected(
                    pending.Request,
                    "bridge_exception",
                    ex.Message,
                    _dispatcher.CurrentRevision));
            }
        }
    }

    protected override void OnEditorVariableChanged(string variableName)
    {
        base.OnEditorVariableChanged(variableName);
        if (ReadOnly)
        {
            SafeWritesArmed = false;
        }

        ApplyEnabledState();
    }

    protected override void OnSceneSave(string saveFolder)
    {
        SafeWritesArmed = false;
        base.OnSceneSave(saveFolder);
    }

    protected override void OnRemoved(int removeReason)
    {
        StopClient();
        base.OnRemoved(removeReason);
    }

    internal EditorStatus CreateStatus(string revision)
    {
        return new EditorStatus
        {
            Connected = _client?.Connected == true,
            EditorMode = Scene?.IsEditorScene() == true,
            BridgeEnabled = BridgeEnabled,
            ReadOnly = ReadOnly,
            SafeWritesArmed = SafeWritesArmed,
            BridgeInstanceId = _bridgeInstanceId,
            GameVersion = "PC@v1.3.4",
            TargetModuleId = TargetModuleId ?? string.Empty,
            SceneName = Scene?.GetName() ?? string.Empty,
            SceneModulePath = Scene?.GetModulePath() ?? string.Empty,
            SceneRevision = revision,
            LastCommand = LastCommand ?? string.Empty,
            LastError = LastError ?? string.Empty
        };
    }

    private BridgeHello CreateHello()
    {
        return new BridgeHello
        {
            BridgeInstanceId = _bridgeInstanceId,
            GameVersion = "PC@v1.3.4",
            TargetModuleId = TargetModuleId ?? string.Empty,
            SceneName = Scene?.GetName() ?? string.Empty,
            EditorMode = Scene?.IsEditorScene() == true,
            ConnectedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private void ApplyEnabledState()
    {
        if (!BridgeEnabled)
        {
            SafeWritesArmed = false;
            StopClient();
            return;
        }

        if (_client is null)
        {
            _client = new PipeBridgeClient(
                string.IsNullOrWhiteSpace(PipeName) ? ProtocolConstants.DefaultPipeName : PipeName.Trim(),
                CreateHello);
            _client.Start();
        }
    }

    private void StopClient()
    {
        _client?.Dispose();
        _client = null;
        ConnectionStatus = "Disconnected";
    }
}

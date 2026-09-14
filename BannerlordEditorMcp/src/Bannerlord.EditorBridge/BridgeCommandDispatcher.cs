using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Bannerlord.EditorMcp.Protocol;
using Newtonsoft.Json.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace Bannerlord.EditorBridge;

internal sealed class BridgeCommandDispatcher
{
    private static readonly Regex TestEntityNamePattern = new Regex(
        "^mcp_test_[A-Za-z0-9_.-]{1,80}$",
        RegexOptions.CultureInvariant);

    private readonly EditorBridgeController _controller;
    private readonly string _sessionId;
    private readonly Dictionary<string, IdempotencyEntry> _idempotency =
        new Dictionary<string, IdempotencyEntry>(StringComparer.Ordinal);
    private readonly IdempotencyRegistry _idempotencyKeys = new IdempotencyRegistry();
    private long _changeCounter;

    internal BridgeCommandDispatcher(EditorBridgeController controller, string sessionId)
    {
        _controller = controller;
        _sessionId = sessionId;
    }

    internal string CurrentRevision => string.Format(
        CultureInfo.InvariantCulture,
        "{0}:{1}",
        _sessionId,
        _changeCounter);

    internal BridgeResponse Handle(BridgeRequest request)
    {
        if (!string.Equals(request.ProtocolVersion, ProtocolConstants.Version, StringComparison.Ordinal))
        {
            return Reject(request, "unsupported_protocol", "The bridge protocol version is not supported.");
        }

        if (string.IsNullOrWhiteSpace(request.RequestId) || string.IsNullOrWhiteSpace(request.Command))
        {
            return Reject(request, "invalid_request", "requestId and command are required.");
        }

        switch (request.Command)
        {
            case BridgeCommands.EditorGetStatus:
                return Result(request, _controller.CreateStatus(CurrentRevision));
            case BridgeCommands.EditorGetCapabilities:
                return Result(request, CreateCapabilities());
            case BridgeCommands.SceneInspect:
                return InspectScene(request);
            case BridgeCommands.SceneInspectTerrain:
                return InspectTerrain(request);
            case BridgeCommands.SceneFindEntities:
                return FindEntities(request);
            case BridgeCommands.SceneCreateTestEntity:
            case BridgeCommands.SceneSetTestEntityTransform:
            case BridgeCommands.SceneRemoveTestEntity:
                return HandleWrite(request);
            default:
                return Reject(request, "unsupported_command", "The requested editor command is not exposed by this bridge.");
        }
    }

    private BridgeResponse HandleWrite(BridgeRequest request)
    {
        var guardFailure = ValidateWriteGuard(request);
        if (guardFailure is not null)
        {
            return guardFailure;
        }

        var fingerprint = CreateIdempotencyFingerprint(request);
        var idempotencyCheck = _idempotencyKeys.Check(request.IdempotencyKey, fingerprint);
        if (idempotencyCheck == IdempotencyCheck.Conflict)
        {
            return Reject(
                request,
                "idempotency_conflict",
                "The idempotency key was already used for a different request.");
        }

        if (idempotencyCheck == IdempotencyCheck.Replay &&
            _idempotency.TryGetValue(request.IdempotencyKey, out var previous))
        {
            var replay = CloneResponse(previous.Response);
            replay.RequestId = request.RequestId;
            replay.Warnings.Add("This is an idempotent replay; the command was not executed again.");
            return replay;
        }

        BridgeResponse response;
        switch (request.Command)
        {
            case BridgeCommands.SceneCreateTestEntity:
                response = CreateTestEntity(request);
                break;
            case BridgeCommands.SceneSetTestEntityTransform:
                response = SetTestEntityTransform(request);
                break;
            case BridgeCommands.SceneRemoveTestEntity:
                response = RemoveTestEntity(request);
                break;
            default:
                response = Reject(request, "unsupported_command", "The requested write command is not exposed.");
                break;
        }

        if (response.Ok)
        {
            _idempotencyKeys.Commit(request.IdempotencyKey, fingerprint);
            _idempotency[request.IdempotencyKey] = new IdempotencyEntry(fingerprint, CloneResponse(response));
        }

        return response;
    }

    private BridgeResponse? ValidateWriteGuard(BridgeRequest request)
    {
        var rejection = WriteSafetyValidator.Validate(
            request,
            new WriteSafetyContext
            {
                BridgeEnabled = _controller.BridgeEnabled,
                ReadOnly = _controller.ReadOnly,
                SafeWritesArmed = _controller.SafeWritesArmed,
                ConfiguredTargetModule = _controller.TargetModuleId ?? string.Empty,
                ConfiguredDisposableScene = _controller.DisposableSceneName ?? string.Empty,
                CurrentScene = _controller.Scene?.GetName() ?? string.Empty,
                CurrentRevision = CurrentRevision
            });
        return rejection is null ? null : Reject(request, rejection.Code, rejection.Message);
    }

    private BridgeResponse InspectScene(BridgeRequest request)
    {
        var args = ParseArguments(request);
        var maxEntities = Math.Max(1, Math.Min(500, args.Value<int?>("maxEntities") ?? 100));
        var entities = GetEntities()
            .Take(maxEntities)
            .Select(EntitySummary.From)
            .ToList();

        return Result(request, new
        {
            sceneName = _controller.Scene?.GetName() ?? string.Empty,
            modulePath = _controller.Scene?.GetModulePath() ?? string.Empty,
            editorMode = _controller.Scene?.IsEditorScene() == true,
            revision = CurrentRevision,
            totalEntities = GetEntities().Count,
            returnedEntities = entities.Count,
            entities
        });
    }

    private BridgeResponse FindEntities(BridgeRequest request)
    {
        var args = ParseArguments(request);
        var nameContains = (args.Value<string>("nameContains") ?? string.Empty).Trim();
        var tag = (args.Value<string>("tag") ?? string.Empty).Trim();
        var maxResults = Math.Max(1, Math.Min(500, args.Value<int?>("maxResults") ?? 100));

        IEnumerable<GameEntity> query = GetEntities();
        if (!string.IsNullOrWhiteSpace(nameContains))
        {
            query = query.Where(entity =>
                (entity.Name ?? string.Empty).IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            query = query.Where(entity => entity.HasTag(tag));
        }

        var matches = query.Take(maxResults).Select(EntitySummary.From).ToList();
        return Result(request, new { revision = CurrentRevision, count = matches.Count, entities = matches });
    }

    private BridgeResponse InspectTerrain(BridgeRequest request)
    {
        var scene = _controller.Scene;
        if (scene is null)
        {
            return Reject(request, "scene_unavailable", "The controller is not attached to a scene.");
        }

        var args = ParseArguments(request);
        var sampleColumns = args.Value<int?>("sampleColumns") ?? 0;
        var sampleRows = args.Value<int?>("sampleRows") ?? 0;
        var sampleColumnOffset = args.Value<int?>("sampleColumnOffset") ?? 0;
        var sampleRowOffset = args.Value<int?>("sampleRowOffset") ?? 0;
        var totalSampleColumns = args.Value<int?>("totalSampleColumns") ?? 0;
        var totalSampleRows = args.Value<int?>("totalSampleRows") ?? 0;

        if (sampleColumns < 0 || sampleColumns > 257 || sampleRows < 0 || sampleRows > 257)
        {
            return Reject(request, "invalid_sample_grid", "sampleColumns and sampleRows must be between 0 and 257.");
        }

        if ((sampleColumns == 0) != (sampleRows == 0))
        {
            return Reject(request, "invalid_sample_grid", "sampleColumns and sampleRows must both be zero or both be positive.");
        }

        if (sampleColumns == 0)
        {
            if (sampleColumnOffset != 0 || sampleRowOffset != 0 || totalSampleColumns != 0 || totalSampleRows != 0)
            {
                return Reject(request, "invalid_sample_window", "Sample-grid offsets and totals require positive sampleColumns and sampleRows.");
            }
        }
        else
        {
            totalSampleColumns = totalSampleColumns == 0 ? sampleColumns : totalSampleColumns;
            totalSampleRows = totalSampleRows == 0 ? sampleRows : totalSampleRows;

            if (totalSampleColumns < 1 || totalSampleColumns > 4097 || totalSampleRows < 1 || totalSampleRows > 4097)
            {
                return Reject(request, "invalid_sample_window", "Logical sample-grid dimensions must be between 1 and 4097.");
            }

            if (sampleColumnOffset < 0 || sampleRowOffset < 0 ||
                sampleColumnOffset + sampleColumns > totalSampleColumns ||
                sampleRowOffset + sampleRows > totalSampleRows)
            {
                return Reject(request, "invalid_sample_window", "The requested sample tile must fit inside the complete logical sample grid.");
            }
        }

        Vec3 boundsMin;
        Vec3 boundsMax;
        scene.GetBoundingBox(out boundsMin, out boundsMax);

        var nodeDimension = new Vec2i();
        float nodeSize;
        int layerCount;
        int layerVersion;
        scene.GetTerrainData(out nodeDimension, out nodeSize, out layerCount, out layerVersion);

        float minHeight;
        float maxHeight;
        var hasMinMaxHeight = scene.GetTerrainMinMaxHeight(out minHeight, out maxHeight);
        var nodes = new List<object>();
        for (var y = 0; y < nodeDimension.Y; y++)
        {
            for (var x = 0; x < nodeDimension.X; x++)
            {
                int vertexCountAlongAxis;
                float quadLength;
                float nodeMinHeight;
                float nodeMaxHeight;
                scene.GetTerrainNodeData(
                    x,
                    y,
                    out vertexCountAlongAxis,
                    out quadLength,
                    out nodeMinHeight,
                    out nodeMaxHeight);
                nodes.Add(new
                {
                    x,
                    y,
                    vertexCountAlongAxis,
                    quadLength,
                    minHeight = nodeMinHeight,
                    maxHeight = nodeMaxHeight
                });
            }
        }

        var samples = new List<object>();
        if (sampleColumns > 0 && sampleRows > 0)
        {
            var terrainWidth = nodeDimension.X * nodeSize;
            var terrainHeight = nodeDimension.Y * nodeSize;
            for (var row = 0; row < sampleRows; row++)
            {
                var globalRow = sampleRowOffset + row;
                var y = totalSampleRows == 1 ? 0f : terrainHeight * globalRow / (totalSampleRows - 1f);
                for (var column = 0; column < sampleColumns; column++)
                {
                    var globalColumn = sampleColumnOffset + column;
                    var x = totalSampleColumns == 1 ? 0f : terrainWidth * globalColumn / (totalSampleColumns - 1f);
                    samples.Add(new
                    {
                        column,
                        row,
                        globalColumn,
                        globalRow,
                        x,
                        y,
                        height = scene.GetTerrainHeight(new Vec2(x, y), true)
                    });
                }
            }
        }

        return Result(request, new
        {
            sceneName = scene.GetName() ?? string.Empty,
            revision = CurrentRevision,
            containsTerrain = scene.ContainsTerrain,
            hasTerrainHeightmap = scene.HasTerrainHeightmap,
            nodeDimension = new { x = nodeDimension.X, y = nodeDimension.Y },
            nodeSize,
            terrainSize = new
            {
                width = nodeDimension.X * nodeSize,
                height = nodeDimension.Y * nodeSize
            },
            layerCount,
            layerVersion,
            hasMinMaxHeight,
            minHeight,
            maxHeight,
            boundingBox = new
            {
                min = PositionSummary.From(boundsMin),
                max = PositionSummary.From(boundsMax)
            },
            sampleColumns,
            sampleRows,
            sampleColumnOffset,
            sampleRowOffset,
            totalSampleColumns,
            totalSampleRows,
            nodes,
            samples
        });
    }

    private BridgeResponse CreateTestEntity(BridgeRequest request)
    {
        var args = ParseArguments(request);
        var entityName = (args.Value<string>("entityName") ?? string.Empty).Trim();
        if (!TestEntityNamePattern.IsMatch(entityName))
        {
            return Reject(request, "invalid_test_entity_name", "Test entity names must begin with mcp_test_ and contain only safe identifier characters.");
        }

        if (_controller.Scene?.FindEntityWithName(entityName) is not null)
        {
            return Reject(request, "entity_exists", "An entity with that name already exists.");
        }

        if (!TryReadPosition(args, out var position, out var positionError))
        {
            return Reject(request, "invalid_transform", positionError);
        }

        var response = PlannedChange(request, "create_test_entity", entityName, FormatPosition(position));
        if (request.DryRun)
        {
            return response;
        }

        var scene = _controller.Scene;
        if (scene is null)
        {
            return Reject(request, "scene_unavailable", "The controller is not attached to a scene.");
        }

        var entity = GameEntity.CreateEmpty(scene, true, true, true);
        entity.Name = entityName;
        entity.AddTag(ProtocolConstants.TestEntityTag);
        entity.SetLocalPosition(position);
        IncrementRevision(response);
        return response;
    }

    private BridgeResponse SetTestEntityTransform(BridgeRequest request)
    {
        var args = ParseArguments(request);
        var entityName = (args.Value<string>("entityName") ?? string.Empty).Trim();
        var entity = FindOwnedTestEntity(entityName);
        if (entity is null)
        {
            return Reject(request, "test_entity_not_found", "No bridge-owned test entity with that name exists.");
        }

        if (!TryReadPosition(args, out var position, out var positionError))
        {
            return Reject(request, "invalid_transform", positionError);
        }

        var response = PlannedChange(request, "move_test_entity", entityName, FormatPosition(position));
        if (request.DryRun)
        {
            return response;
        }

        entity.SetLocalPosition(position);
        IncrementRevision(response);
        return response;
    }

    private BridgeResponse RemoveTestEntity(BridgeRequest request)
    {
        var args = ParseArguments(request);
        var entityName = (args.Value<string>("entityName") ?? string.Empty).Trim();
        var entity = FindOwnedTestEntity(entityName);
        if (entity is null)
        {
            return Reject(request, "test_entity_not_found", "No bridge-owned test entity with that name exists.");
        }

        var response = PlannedChange(request, "remove_test_entity", entityName, string.Empty);
        if (request.DryRun)
        {
            return response;
        }

        _controller.Scene?.RemoveEntity(entity, 0);
        IncrementRevision(response);
        return response;
    }

    private GameEntity? FindOwnedTestEntity(string entityName)
    {
        if (!TestEntityNamePattern.IsMatch(entityName))
        {
            return null;
        }

        var entity = _controller.Scene?.FindEntityWithName(entityName);
        return entity?.HasTag(ProtocolConstants.TestEntityTag) == true ? entity : null;
    }

    private List<GameEntity> GetEntities()
    {
        var entities = new List<GameEntity>();
        _controller.Scene?.GetEntities(ref entities);
        return entities;
    }

    private static EditorCapabilities CreateCapabilities()
    {
        return new EditorCapabilities
        {
            InspectEntities = true,
            CreateEmptyEntity = true,
            InstantiatePrefab = true,
            SetTransform = true,
            SetTags = true,
            CreatePath = true,
            ReadTerrain = true,
            ImportHeightmap = false,
            ModifyTerrain = false,
            InspectNavmesh = true,
            GenerateNavmesh = false,
            SaveScene = false
        };
    }

    private static JObject ParseArguments(BridgeRequest request)
    {
        try
        {
            return JObject.Parse(string.IsNullOrWhiteSpace(request.ArgumentsJson) ? "{}" : request.ArgumentsJson);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("argumentsJson is invalid JSON: " + ex.Message, ex);
        }
    }

    private static bool TryReadPosition(JObject args, out Vec3 position, out string error)
    {
        var x = args.Value<float?>("x") ?? 0f;
        var y = args.Value<float?>("y") ?? 0f;
        var z = args.Value<float?>("z") ?? 0f;
        if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z))
        {
            position = Vec3.Zero;
            error = "Transform coordinates must be finite numbers.";
            return false;
        }

        position = new Vec3(x, y, z);
        error = string.Empty;
        return true;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static string FormatPosition(Vec3 position)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "({0:R}, {1:R}, {2:R})",
            position.x,
            position.y,
            position.z);
    }

    private BridgeResponse Result(BridgeRequest request, object result)
    {
        return new BridgeResponse
        {
            RequestId = request.RequestId,
            Ok = true,
            Applied = false,
            RequiresSave = false,
            SceneRevision = CurrentRevision,
            ResultJson = BridgeJson.Serialize(result)
        };
    }

    private BridgeResponse PlannedChange(
        BridgeRequest request,
        string operation,
        string entity,
        string detail)
    {
        return new BridgeResponse
        {
            RequestId = request.RequestId,
            Ok = true,
            Applied = false,
            RequiresSave = !request.DryRun,
            SceneRevision = CurrentRevision,
            ResultJson = BridgeJson.Serialize(new { dryRun = request.DryRun }),
            Changes = new List<BridgeChange>
            {
                new BridgeChange { Operation = operation, Entity = entity, Detail = detail }
            }
        };
    }

    private void IncrementRevision(BridgeResponse response)
    {
        _changeCounter++;
        response.Applied = true;
        response.RequiresSave = true;
        response.SceneRevision = CurrentRevision;
    }

    private BridgeResponse Reject(BridgeRequest request, string code, string message)
    {
        return BridgeResponse.Rejected(request, code, message, CurrentRevision);
    }

    private static string CreateIdempotencyFingerprint(BridgeRequest request)
    {
        return string.Join("|", new[]
        {
            request.Command,
            request.TargetModule,
            request.Scene,
            request.ExpectedSceneRevision,
            request.DryRun.ToString(CultureInfo.InvariantCulture),
            request.ArgumentsJson
        });
    }

    private static BridgeResponse CloneResponse(BridgeResponse response)
    {
        return BridgeJson.Deserialize<BridgeResponse>(BridgeJson.Serialize(response))
            ?? throw new InvalidOperationException("Unable to clone bridge response.");
    }

    private sealed class IdempotencyEntry
    {
        internal IdempotencyEntry(string fingerprint, BridgeResponse response)
        {
            Fingerprint = fingerprint;
            Response = response;
        }

        internal string Fingerprint { get; }
        internal BridgeResponse Response { get; }
    }

    private sealed class EntitySummary
    {
        public string Name { get; set; } = string.Empty;
        public string Guid { get; set; } = string.Empty;
        public string PrefabName { get; set; } = string.Empty;
        public string[] Tags { get; set; } = Array.Empty<string>();
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        internal static EntitySummary From(GameEntity entity)
        {
            var position = entity.GlobalPosition;
            return new EntitySummary
            {
                Name = entity.Name ?? string.Empty,
                Guid = entity.GetGuid() ?? string.Empty,
                PrefabName = entity.GetPrefabName() ?? string.Empty,
                Tags = entity.Tags ?? Array.Empty<string>(),
                X = position.x,
                Y = position.y,
                Z = position.z
            };
        }
    }

    private sealed class PositionSummary
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        internal static PositionSummary From(Vec3 position)
        {
            return new PositionSummary { X = position.x, Y = position.y, Z = position.z };
        }
    }
}

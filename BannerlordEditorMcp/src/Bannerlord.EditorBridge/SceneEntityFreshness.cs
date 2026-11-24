using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TaleWorlds.DotNet;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using BinaryWriter = System.IO.BinaryWriter;

namespace Bannerlord.EditorBridge;

internal sealed class SceneEntityFreshness
{
    private const int MaxEntities = 4096;
    private const int MaxTagsPerEntity = 64;
    private const int MaxFieldLength = 512;
    private NativeObjectArray? _rootEntities;

    internal string? Capture(Scene scene)
    {
        var scenePointer = scene.Pointer;
        int expectedRoots = scene.RootEntityCount;
        if (scenePointer == UIntPtr.Zero || expectedRoots < 0 || expectedRoots > MaxEntities)
        {
            return null;
        }

        var roots = _rootEntities ??= NativeObjectArray.Create();
        var pending = new List<(GameEntity Entity, ulong ParentPointer)>(expectedRoots);
        try
        {
            roots.Clear();
            scene.GetRootEntities(roots);
            if (roots.Count != expectedRoots)
            {
                return null;
            }

            for (int i = 0; i < expectedRoots; i++)
            {
                if (roots.GetElementAt(i) is not GameEntity root)
                {
                    return null;
                }

                pending.Add((root, 0));
            }
        }
        finally
        {
            roots.Clear();
        }

        var encountered = new HashSet<ulong>();
        using var digest = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, digest, CryptoStreamMode.Write);
        using var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), leaveOpen: true);
        writer.Write(scenePointer.ToUInt64());
        WriteField(writer, scene.GetName());
        WriteField(writer, scene.GetModulePath());
        writer.Write(expectedRoots);

        // Preserve native root and child order, which can affect name lookup in
        // scenes with duplicate names. Each link is part of the observed state.
        for (int i = 0; i < pending.Count; i++)
        {
            var (entity, parentPointer) = pending[i];
            ulong pointer = entity.Pointer.ToUInt64();
            if (pointer == 0 || !encountered.Add(pointer) || entity.GetScenePointer() != scenePointer)
            {
                return null;
            }

            writer.Write(pointer);
            writer.Write(parentPointer);
            WriteField(writer, entity.GetGuid());
            WriteField(writer, entity.Name);
            WriteField(writer, entity.GetPrefabName());
            WriteFrame(writer, entity.GetLocalFrame());
            WriteFrame(writer, entity.GetGlobalFrame());

            var tags = entity.Tags ?? Array.Empty<string>();
            if (tags.Length > MaxTagsPerEntity)
            {
                return null;
            }

            writer.Write(tags.Length);
            foreach (var tag in tags.OrderBy(value => value, StringComparer.Ordinal))
            {
                WriteField(writer, tag);
            }

            int children = entity.ChildCount;
            if (children < 0 || children > MaxEntities - pending.Count)
            {
                return null;
            }

            writer.Write(children);
            for (int j = 0; j < children; j++)
            {
                var child = entity.GetChild(j);
                if (child is null)
                {
                    return null;
                }

                pending.Add((child, pointer));
            }
        }

        if (scene.Pointer != scenePointer || scene.RootEntityCount != expectedRoots)
        {
            return null;
        }

        writer.Flush();
        stream.FlushFinalBlock();
        return Convert.ToBase64String(digest.Hash!);
    }

    private static void WriteField(BinaryWriter writer, string? value)
    {
        if (value is not null && value.Length > MaxFieldLength)
        {
            throw new InvalidOperationException("A scene entity field exceeded the freshness bound.");
        }

        writer.Write(value ?? string.Empty);
    }

    private static void WriteFrame(BinaryWriter writer, MatrixFrame frame)
    {
        WriteVector(writer, frame.origin);
        WriteVector(writer, frame.rotation.s);
        WriteVector(writer, frame.rotation.f);
        WriteVector(writer, frame.rotation.u);
    }

    private static void WriteVector(BinaryWriter writer, Vec3 vector)
    {
        writer.Write(vector.x);
        writer.Write(vector.y);
        writer.Write(vector.z);
    }
}

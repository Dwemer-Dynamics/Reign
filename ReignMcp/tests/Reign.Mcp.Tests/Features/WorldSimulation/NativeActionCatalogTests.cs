using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Reign.Core.Contracts.Actions;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class NativeActionCatalogTests
{
    [Fact] public void PrimitiveRegistryIsExplicitUniqueAndContainsNoChatActions()
    {
        Assert.Equal(193, ReignNativeActionCatalog.All.Count);
        Assert.Equal(193, ReignNativeActionCatalog.All.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(ReignNativeActionCatalog.All, d =>
        {
            Assert.StartsWith("native.", d.Id);
            Assert.StartsWith("TaleWorlds.CampaignSystem.Actions.", d.Type);
            Assert.DoesNotContain(ReignActionCatalog.All, a => a.Id == d.Id);
            Assert.Equal(d.Parameters.Count, d.Parameters.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count());
        });
    }

    [InstalledNativeMetadataFact]
    public void EveryInstalledPublicNativeActionHasAnExactClassifiedSignature()
    {
        string game = Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH")!;
        using var stream = File.OpenRead(Path.Combine(game, "bin", "Win64_Shipping_Client", "TaleWorlds.CampaignSystem.dll"));
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        var provider = new Names();
        var actual = new List<string>();
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(handle);
            if (reader.GetString(type.Namespace) != "TaleWorlds.CampaignSystem.Actions") continue;
            string fullType = reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public || !method.Attributes.HasFlag(MethodAttributes.Static)) continue;
                var signature = method.DecodeSignature(provider, (object?)null);
                var parameterMetadata = method.GetParameters().Select(reader.GetParameter).Where(p => p.SequenceNumber > 0).OrderBy(p => p.SequenceNumber).ToArray();
                actual.Add(fullType + "." + reader.GetString(method.Name) + ":" + signature.ReturnType + "("
                    + string.Join(";", signature.ParameterTypes.Select((name, i) => name + " " + reader.GetString(parameterMetadata[i].Name)
                        + (parameterMetadata[i].Attributes.HasFlag(ParameterAttributes.Out) ? "!out" : "")
                        + (parameterMetadata[i].Attributes.HasFlag(ParameterAttributes.HasDefault) ? "=optional" : ""))) + ")");
            }
        }
        var expected = ReignNativeActionCatalog.All.Select(d => d.Type + "." + d.Method + ":" + d.ReturnType + "("
            + string.Join(";", d.Parameters.Select(p => p.Type + (p.Direction.Length > 0 ? "&" : "") + " " + p.Name
                + (p.Direction == "out" ? "!out" : "") + (p.Default.Length > 0 ? "=optional" : ""))) + ")").ToArray();
        var missing = expected.Except(actual, StringComparer.Ordinal).ToArray();
        var unclassified = actual.Except(expected, StringComparer.Ordinal).ToArray();
        Assert.True(missing.Length == 0 && unclassified.Length == 0,
            "Missing or changed registered signatures:\n" + string.Join("\n", missing)
            + "\nUnclassified installed signatures:\n" + string.Join("\n", unclassified));
        Assert.Equal(expected.Length, actual.Count);
    }

    // Metadata-only inventory checks need the licensed game, not a loaded campaign.
    // Canonical client/product validation supplies this path; server-only plans may skip.
    private sealed class InstalledNativeMetadataFactAttribute : FactAttribute
    {
        public InstalledNativeMetadataFactAttribute()
        {
            string? game = Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH");
            if (string.IsNullOrEmpty(game) || !File.Exists(Path.Combine(game, "bin", "Win64_Shipping_Client", "TaleWorlds.CampaignSystem.dll")))
                Skip = "Requires the licensed REIGN_BANNERLORD_PATH; run client or product validation.";
        }
    }

    private sealed class Names : ISignatureTypeProvider<string, object?>
    {
        public string GetArrayType(string type, ArrayShape shape) => type + "[]";
        public string GetByReferenceType(string type) => type + "&";
        public string GetFunctionPointerType(MethodSignature<string> signature) => "function-pointer";
        public string GetGenericInstantiation(string type, ImmutableArray<string> args) => type.Split('`')[0] + "<" + string.Join(", ", args) + ">";
        public string GetGenericMethodParameter(object? context, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? context, int index) => "!" + index;
        public string GetModifiedType(string modifier, string type, bool required) => type;
        public string GetPinnedType(string type) => type;
        public string GetPointerType(string type) => type + "*";
        public string GetPrimitiveType(PrimitiveTypeCode code) => code switch { PrimitiveTypeCode.Void => "void", PrimitiveTypeCode.Int32 => "int", PrimitiveTypeCode.Boolean => "bool", PrimitiveTypeCode.Single => "float", _ => code.ToString() };
        public string GetSZArrayType(string type) => type + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte kind)
        {
            var type = reader.GetTypeDefinition(handle); var parent = type.GetDeclaringType();
            return (parent.IsNil ? "" : GetTypeFromDefinition(reader, parent, kind) + ".") + reader.GetString(type.Name);
        }
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte kind)
        {
            var type = reader.GetTypeReference(handle);
            return (type.ResolutionScope.Kind == HandleKind.TypeReference ? GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, kind) + "." : "") + reader.GetString(type.Name);
        }
        public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte kind)
            => reader.GetTypeSpecification(handle).DecodeSignature(this, context);
    }
}

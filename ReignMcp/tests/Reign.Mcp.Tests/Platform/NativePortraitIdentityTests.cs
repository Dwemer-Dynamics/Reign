using AIPortraits;

namespace Reign.Mcp.Tests;

public sealed class NativePortraitIdentityTests
{
    private const string Campaign = "portrait-campaign";
    private const string Itaria = "Itaria (lord_SE8_c)";
    private const string Abalytos = "Abalytos (lord_1_72)";
    // The real shared native key that exposed the bug. Identity must also survive fully equal codes.
    private const string SharedCode = "@---@equipment@---@<BodyProperties key=\"0011B40B402410088733440E80468F1FFF4F9274C862B448941B5637963E5D3E000E08830B7247A6000000000000000000000000000000000000000000000003101\" />@---@1@---@1@---@0@---@0@---@0@---@0@---@";

    [Fact]
    public void Identical_appearance_codes_keep_separate_hero_identities()
    {
        string itariaArgs = Bind(new object(), Itaria);
        string abalytosArgs = Bind(new object(), Abalytos);
        Assert.NotEqual(itariaArgs, abalytosArgs);
        Assert.True(NativePortraitIdentity.TryResolve(SharedCode, itariaArgs, Campaign, out string itaria));
        Assert.True(NativePortraitIdentity.TryResolve(SharedCode, abalytosArgs, Campaign, out string abalytos));
        Assert.Equal(Itaria, itaria);
        Assert.Equal(Abalytos, abalytos);
    }

    [Fact]
    public void Matching_code_text_without_the_originating_object_does_not_borrow_identity()
    {
        Bind(new object(), Itaria);
        Assert.Equal("customSizeX=448", NativePortraitIdentity.CreateArguments(new object(), SharedCode,
            Campaign, "customSizeX=448"));
        Assert.False(NativePortraitIdentity.TryResolve(SharedCode, "customSizeX=448", Campaign, out _));
    }

    [Fact]
    public void Native_size_arguments_are_preserved_and_identity_binding_is_idempotent()
    {
        var code = new object();
        string args = Bind(code, Itaria, "customSizeX=448;customSizeY=320;thirdParty=keep");
        Assert.StartsWith("customSizeX=448;customSizeY=320;thirdParty=keep;", args);
        Assert.Equal(args, NativePortraitIdentity.CreateArguments(code, SharedCode, Campaign, args));
        Assert.True(NativePortraitIdentity.TryResolve(SharedCode, args, Campaign, out string key));
        Assert.Equal(Itaria, key);
    }

    [Theory]
    [InlineData("different-image", Campaign)]
    [InlineData(SharedCode, "another-campaign")]
    [InlineData(SharedCode, "unknown")]
    [InlineData("", Campaign)]
    public void Stale_widget_identity_is_claimed_but_cannot_resolve_or_use_appearance_fallback(string image, string campaign)
    {
        string args = Bind(new object(), Itaria);
        Assert.True(NativePortraitIdentity.TryResolve(image, args, campaign, out string key));
        Assert.Null(key);
    }

    [Theory]
    [InlineData("reignPortraitV1")]
    [InlineData("reignPortraitV1=")]
    [InlineData("reignPortraitV1=garbage")]
    public void Malformed_identity_fails_closed(string args)
    {
        Assert.True(NativePortraitIdentity.TryResolve(SharedCode, args, Campaign, out string key));
        Assert.Null(key);
    }

    [Fact]
    public void Duplicate_identity_arguments_fail_closed()
    {
        string args = Bind(new object(), Itaria) + ";" + Bind(new object(), Abalytos);
        Assert.True(NativePortraitIdentity.TryResolve(SharedCode, args, Campaign, out string key));
        Assert.Null(key);
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("Li4vYW5vdGhlci1oZXJv")]
    [InlineData("/w==")]
    public void Invalid_encoded_keys_and_path_traversal_fail_closed(string encodedKey)
    {
        string[] fields = Bind(new object(), Itaria).Split('.');
        fields[1] = encodedKey;
        Assert.True(NativePortraitIdentity.TryResolve(SharedCode, string.Join('.', fields), Campaign, out string key));
        Assert.Null(key);
    }

    [Theory]
    [InlineData("", Campaign, Itaria)]
    [InlineData(SharedCode, "unknown", Itaria)]
    [InlineData(SharedCode, Campaign, "../wrong")]
    [InlineData(SharedCode, Campaign, "")]
    public void Empty_hidden_or_invalid_identities_are_not_bound(string image, string campaign, string key)
    {
        var code = new object();
        NativePortraitIdentity.Remember(code, image, campaign, key);
        Assert.Equal("", NativePortraitIdentity.CreateArguments(code, image, campaign, ""));
    }

    [Fact]
    public void Captured_identity_cannot_be_attached_to_a_changed_code_or_campaign()
    {
        var code = new object();
        Bind(code, Itaria);
        Assert.Equal("", NativePortraitIdentity.CreateArguments(code, "changed", Campaign, ""));
        Assert.Equal("", NativePortraitIdentity.CreateArguments(code, SharedCode, "changed", ""));
    }

    [Fact]
    public void Missing_exact_portrait_does_not_borrow_another_hero_portrait()
    {
        string args = Bind(new object(), Itaria);
        string selected = NativePortraitIdentity.TryResolve(SharedCode, args, Campaign, out string key) ? key : Abalytos;
        var available = new HashSet<string> { Abalytos };
        Assert.Equal(Itaria, selected);
        Assert.DoesNotContain(selected, available); // Caller keeps the native texture, not Abalytos.
    }

    [Fact]
    public void Unicode_and_argument_delimiters_in_names_round_trip_without_corrupting_native_options()
    {
        const string key = "Éléna;=漢字 (hero_42)";
        string args = Bind(new object(), key, "customSizeX=448");
        Assert.Equal(2, args.Split(';').Length);
        Assert.True(NativePortraitIdentity.TryResolve(SharedCode, args, Campaign, out string resolved));
        Assert.Equal(key, resolved);
    }

    [Fact]
    public void Production_registers_both_native_code_factories_and_binds_vm_metadata_before_texture_lookup()
    {
        string root = TestOptions.FindWorkspace();
        string patch = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Portraits/AIPortraits/NativePortraitIdentityPatch.cs"));
        string bootstrap = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Platform/SubModule.cs"));
        string consumer = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Portraits/AIPortraits/PortraitPatch.cs"));
        Assert.Contains("nameof(CampaignUIHelper.GetCharacterCode)", patch);
        Assert.Contains("typeof(BasicCharacterObject), typeof(Equipment)", patch);
        Assert.Contains("typeof(CharacterImageIdentifierVM), MethodType.Constructor", patch);
        Assert.Contains("character?.HeroObject == null || code == null || code.IsEmpty", patch);
        Assert.Contains("__instance.AdditionalArgs = NativePortraitIdentity.CreateArguments", patch);
        foreach (string hook in new[] { "CampaignCodePatch", "CharacterCodePatch", "ImageIdentifierPatch" })
            Assert.Contains("PatchPortraitHarmony(typeof(NativePortraitIdentityPatch." + hook + "), true)", bootstrap);
        Assert.Contains("ResolveCacheKey(__instance, value2, text)", consumer);
        Assert.Contains("Property(\"AdditionalArgs\")", consumer);
        Assert.Contains("out string cacheKey)) return cacheKey", consumer);
        Assert.Contains("return PortraitIndex.Resolve(appearanceKey)", consumer);
        Assert.Contains("if (!TextureFactory.Has(text2))", consumer);
        Assert.Contains("state.ImageId != imageId || state.AdditionalArgs != additionalArgs", consumer);
    }

    private static string Bind(object code, string key, string args = "")
    {
        NativePortraitIdentity.Remember(code, SharedCode, Campaign, key);
        return NativePortraitIdentity.CreateArguments(code, SharedCode, Campaign, args);
    }
}

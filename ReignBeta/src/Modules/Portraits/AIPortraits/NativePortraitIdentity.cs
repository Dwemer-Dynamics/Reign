#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace AIPortraits;

// CharacterCode contains appearance, not identity. Keep the originating object distinct even
// when two heroes have identical serialized codes, then carry identity in native image args.
internal static class NativePortraitIdentity
{
    internal const string ArgumentName = "reignPortraitV1";
    private const string ArgumentPrefix = ArgumentName + "=";
    private static readonly ConditionalWeakTable<object, Identity> Identities = new ConditionalWeakTable<object, Identity>();
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

    private sealed class Identity
    {
        internal string ImageId;
        internal string CampaignId;
        internal string CacheKey;
    }

    internal static void Remember(object characterCode, string imageId, string campaignId, string cacheKey)
    {
        if (characterCode == null || string.IsNullOrEmpty(imageId)
            || !ValidCampaign(campaignId) || !ValidCacheKey(cacheKey)) return;

        lock (Identities)
        {
            Identities.Remove(characterCode);
            Identities.Add(characterCode, new Identity { ImageId = imageId, CampaignId = campaignId, CacheKey = cacheKey });
        }
    }

    internal static string CreateArguments(object characterCode, string imageId, string campaignId, string additionalArgs)
    {
        if (characterCode == null || !Identities.TryGetValue(characterCode, out Identity identity)
            || !string.Equals(identity.ImageId, imageId, StringComparison.Ordinal)
            || !string.Equals(identity.CampaignId, campaignId, StringComparison.Ordinal)) return additionalArgs;

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(additionalArgs))
        {
            foreach (string part in additionalArgs.Split(';'))
                if (!IsIdentityArgument(part)) parts.Add(part);
        }
        // Native CharacterImageTextureProvider ignores unknown arguments; preserve its size options.
        parts.Add(ArgumentPrefix + Encode(identity.CampaignId) + "." + Encode(identity.CacheKey) + "." + ImageHash(imageId));
        return string.Join(";", parts);
    }

    // True means explicit identity owns this lookup, including a rejected/stale identity (null key).
    // Never fall through to an appearance match after a claimed identity or missing exact portrait.
    internal static bool TryResolve(string imageId, string additionalArgs, string campaignId, out string cacheKey)
    {
        cacheKey = null;
        if (string.IsNullOrEmpty(additionalArgs)) return false;
        string argument = null;
        foreach (string part in additionalArgs.Split(';'))
        {
            if (!IsIdentityArgument(part)) continue;
            if (argument != null) return true; // Conflicting/duplicate identities fail closed.
            argument = part;
        }
        if (argument == null) return false;
        if (!argument.StartsWith(ArgumentPrefix, StringComparison.Ordinal) || argument.Length > 8192
            || string.IsNullOrEmpty(imageId) || !ValidCampaign(campaignId)) return true;

        string[] fields = argument.Substring(ArgumentPrefix.Length).Split('.');
        if (fields.Length != 3 || !string.Equals(fields[2], ImageHash(imageId), StringComparison.Ordinal)) return true;
        try
        {
            string boundCampaign = Decode(fields[0]);
            string boundKey = Decode(fields[1]);
            if (string.Equals(boundCampaign, campaignId, StringComparison.Ordinal) && ValidCacheKey(boundKey))
                cacheKey = boundKey;
        }
        catch (FormatException) { }
        catch (DecoderFallbackException) { }
        return true;
    }

    private static bool IsIdentityArgument(string part) =>
        part == ArgumentName || part.StartsWith(ArgumentPrefix, StringComparison.Ordinal);

    private static bool ValidCampaign(string value) => !string.IsNullOrWhiteSpace(value) && value != "unknown";

    private static bool ValidCacheKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "." || value == ".." || value.Length > 1024) return false;
        foreach (char c in value)
            if (char.IsControl(c) || "<>:\"/\\|?*".IndexOf(c) >= 0) return false;
        return true;
    }

    private static string Encode(string value) => Convert.ToBase64String(Utf8.GetBytes(value));
    private static string Decode(string value) => Utf8.GetString(Convert.FromBase64String(value));

    private static string ImageHash(string imageId)
    {
        using (SHA256 hash = SHA256.Create())
            return Convert.ToBase64String(hash.ComputeHash(Utf8.GetBytes(imageId)));
    }
}

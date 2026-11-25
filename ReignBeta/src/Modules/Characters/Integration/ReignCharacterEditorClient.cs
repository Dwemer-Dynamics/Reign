using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using static ReignBeta.Platform.Integration.ReignServerClient;
using ReignBeta.Platform.Integration;

namespace ReignBeta.Characters.Integration
{
    internal static class ReignCharacterEditorClient
    {
        internal static async Task<List<ReignCharacterEditorCommand>> PollCharacterEditorCommandsAsync(
            string campaignId, string timelineId, string restoreGeneration, string nativeEditorSession, Func<bool> ownsPoll)
        {
            List<ReignCharacterEditorCommand> result = new List<ReignCharacterEditorCommand>();
            try
            {
                var request = new JObject { ["campaignId"] = campaignId, ["timelineId"] = timelineId,
                    ["sharedActivityGeneration"] = restoreGeneration };
                if (nativeEditorSession.Length > 0) request["editorNativeLoadSessionId"] = nativeEditorSession;
                JObject response = await PostOwnedJsonAsync("/character-editor/native/poll",
                    request, ownsPoll).ConfigureAwait(false);
                foreach (JObject row in (response["commands"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    var lineage = row["manualOriginLineageId"];
                    var material = row["manualOriginMaterialSha256"];
                    var session = row["editorNativeLoadSessionId"];
                    var receiptOnly = row["nativeReceiptOnly"];
                    var automaticId = row["automaticOriginId"];
                    var automaticMaterial = row["automaticOriginMaterialSha256"];
                    bool ownedManual = lineage != null || material != null || session != null;
                    bool ownedAutomatic = automaticId != null || automaticMaterial != null;
                    if (ownedManual && ownedAutomatic || !ownedManual && !ownedAutomatic && receiptOnly != null) continue;
                    if (ownedAutomatic && (automaticId?.Type != JTokenType.String || automaticMaterial?.Type != JTokenType.String
                        || receiptOnly?.Type != JTokenType.Boolean || !IsNativeOwnerNonce(automaticId.Value<string>())
                        || !IsNativeOriginDigest(automaticMaterial.Value<string>()) || row["claimId"]?.Type != JTokenType.String
                        || !IsNativeOwnerNonce(row.Value<string>("claimId"))))
                        continue;
                    if (ownedManual && (lineage?.Type != JTokenType.String || material?.Type != JTokenType.String
                        || session?.Type != JTokenType.String || receiptOnly?.Type != JTokenType.Boolean
                        || !IsNativeOwnerNonce(lineage.Value<string>()) || !IsNativeOriginDigest(material.Value<string>())
                        || !IsNativeOwnerNonce(session.Value<string>()) || session.Value<string>() != nativeEditorSession
                        || row["claimId"]?.Type != JTokenType.String || !IsNativeOwnerNonce(row.Value<string>("claimId"))))
                        continue;
                    result.Add(new ReignCharacterEditorCommand
                    {
                        ManualOriginLineageId = ownedManual ? lineage.Value<string>() : "",
                        ManualOriginMaterialSha256 = ownedManual ? material.Value<string>() : "",
                        ManualClaimSessionId = ownedManual ? session.Value<string>() : "",
                        AutomaticOriginId = ownedAutomatic ? automaticId.Value<string>() : "",
                        AutomaticOriginMaterialSha256 = ownedAutomatic ? automaticMaterial.Value<string>() : "",
                        ReceiptOnly = (ownedManual || ownedAutomatic) && receiptOnly.Value<bool>(),
                        CommandId = row.Value<string>("command_id") ?? string.Empty,
                        ClaimId = row.Value<string>("claimId") ?? string.Empty,
                        HeroId = row.Value<string>("hero_id") ?? string.Empty,
                        Dangerous = row.Value<int?>("dangerous") == 1,
                        Confirmation = row.Value<string>("confirmation") ?? string.Empty,
                        Changes = row["changes"] as JObject ?? JObject.Parse(row.Value<string>("changes_json") ?? "{}")
                    });
                }
            }
            catch (ReignRequestOwnerAbandonedException) { throw; }
            catch (Exception ex) { ReignLog.Warn("Character editor command poll failed: " + ex.Message); }
            return result;
        }

        internal static async Task ReportCharacterEditorCommandAsync(string campaignId, string timelineId,
            string restoreGeneration, ReignCharacterEditorCommand command, string status,
            IList<string> applied, IList<string> errors, Func<bool> ownsPoll)
        {
            try
            {
                var report = new JObject
                {
                    ["campaignId"] = campaignId, ["commandId"] = command.CommandId, ["status"] = status ?? "failed",
                    ["applied"] = new JArray(applied ?? new List<string>()), ["errors"] = new JArray(errors ?? new List<string>())
                };
                if (!string.IsNullOrEmpty(command.ClaimId))
                {
                    report["timelineId"] = timelineId;
                    report["sharedActivityGeneration"] = restoreGeneration;
                    report["claimId"] = command.ClaimId;
                }
                if (command.IsManualNative)
                {
                    report["manualOriginLineageId"] = command.ManualOriginLineageId;
                    report["manualOriginMaterialSha256"] = command.ManualOriginMaterialSha256;
                    report["editorNativeLoadSessionId"] = command.ManualClaimSessionId;
                    report["nativeReceiptOnly"] = command.ReceiptOnly;
                }
                if (command.IsAutomaticNative)
                {
                    report["automaticOriginId"] = command.AutomaticOriginId;
                    report["automaticOriginMaterialSha256"] = command.AutomaticOriginMaterialSha256;
                    report["nativeReceiptOnly"] = command.ReceiptOnly;
                }
                await PostOwnedJsonAsync("/character-editor/native/report", report, ownsPoll).ConfigureAwait(false);
            }
            catch (ReignRequestOwnerAbandonedException) { throw; }
            catch (Exception ex) { ReignLog.Warn("Character editor command report failed: " + ex.Message); }
        }

        internal static bool IsNativeOwnerNonce(string value) =>
            Guid.TryParseExact(value, "N", out Guid nonce) && nonce.ToString("N") == value;
        internal static bool IsNativeOriginDigest(string value) =>
            value?.Length == 64 && value.All(Uri.IsHexDigit);

        public static Task<JObject> InitializeNotableMbtiProfilesAsync(IList<Hero> notableHeroes,
            string startupCampaign = null, JObject startupOwner = null, Func<bool> ownsPreparation = null)
        {
            JArray heroes = new JArray();
            foreach (Hero hero in notableHeroes ?? new List<Hero>())
            {
                if (hero == null || !hero.IsAlive || !hero.IsNotable) continue;
                heroes.Add(new JObject
                {
                    ["heroStringId"] = hero.StringId ?? string.Empty,
                    ["name"] = hero.Name?.ToString() ?? string.Empty,
                    ["isNotable"] = true,
                    ["isAlive"] = true,
                    ["age"] = hero.Age,
                    ["isFemale"] = hero.IsFemale,
                    ["cultureId"] = hero.Culture?.StringId ?? string.Empty,
                    ["occupation"] = hero.Occupation.ToString(),
                    ["currentSettlementId"] = hero.CurrentSettlement?.StringId ?? string.Empty,
                    ["traits"] = NativePersonalityTraits(hero)
                });
            }
            var payload = new JObject
            {
                ["campaignId"] = startupCampaign ?? GetCampaignId(),
                ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                ["heroes"] = heroes
            };
            return PostNotableStartupAsync("/notables/mbti/initialize", payload, startupOwner, ownsPreparation);
        }

        public static Task<JObject> ConfirmNotableMbtiProfilesAsync(JArray observations,
            string startupCampaign = null, JObject startupOwner = null, Func<bool> ownsPreparation = null)
        {
            var payload = new JObject
            {
                ["campaignId"] = startupCampaign ?? GetCampaignId(),
                ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                ["observations"] = observations ?? new JArray()
            };
            return PostNotableStartupAsync("/notables/mbti/confirm-batch", payload, startupOwner, ownsPreparation);
        }

        private static Task<JObject> PostNotableStartupAsync(string route, JObject payload, JObject owner,
            Func<bool> ownsPreparation)
        {
            if (owner == null) return PostJsonAsync(route, payload);
            payload["typedNotableStartupVersion"] = 1;
            payload["startupNativeOwner"] = owner.DeepClone();
            return PostOwnedJsonAsync(route, payload, ownsPreparation);
        }

        public static JObject NativePersonalityTraits(Hero hero)
        {
            return new JObject
            {
                ["valor"] = hero?.GetTraitLevel(DefaultTraits.Valor) ?? 0,
                ["generosity"] = hero?.GetTraitLevel(DefaultTraits.Generosity) ?? 0,
                ["honor"] = hero?.GetTraitLevel(DefaultTraits.Honor) ?? 0,
                ["mercy"] = hero?.GetTraitLevel(DefaultTraits.Mercy) ?? 0,
                ["calculating"] = hero?.GetTraitLevel(DefaultTraits.Calculating) ?? 0
            };
        }
    }

    public sealed class ReignCharacterEditorCommand
    {
        public string CommandId { get; set; } = string.Empty;
        internal string ClaimId { get; set; } = string.Empty;
        internal string ManualOriginLineageId { get; set; } = string.Empty;
        internal string ManualOriginMaterialSha256 { get; set; } = string.Empty;
        internal string ManualClaimSessionId { get; set; } = string.Empty;
        internal bool ReceiptOnly { get; set; }
        internal bool IsManualNative => ManualOriginLineageId.Length > 0;
        internal string AutomaticOriginId { get; set; } = string.Empty;
        internal string AutomaticOriginMaterialSha256 { get; set; } = string.Empty;
        internal bool IsAutomaticNative => AutomaticOriginId.Length > 0;
        public string HeroId { get; set; } = string.Empty;
        public bool Dangerous { get; set; }
        public string Confirmation { get; set; } = string.Empty;
        public JObject Changes { get; set; } = new JObject();
    }
}

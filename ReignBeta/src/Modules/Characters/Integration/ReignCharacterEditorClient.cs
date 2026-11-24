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
            string campaignId, Func<bool> ownsPoll)
        {
            List<ReignCharacterEditorCommand> result = new List<ReignCharacterEditorCommand>();
            try
            {
                JObject response = await PostOwnedJsonAsync("/character-editor/native/poll",
                    new JObject { ["campaignId"] = campaignId }, ownsPoll).ConfigureAwait(false);
                foreach (JObject row in (response["commands"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    result.Add(new ReignCharacterEditorCommand
                    {
                        CommandId = row.Value<string>("command_id") ?? string.Empty,
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

        internal static async Task ReportCharacterEditorCommandAsync(string campaignId, string commandId,
            string status, IList<string> applied, IList<string> errors, Func<bool> ownsPoll)
        {
            try
            {
                await PostOwnedJsonAsync("/character-editor/native/report", new JObject
                {
                    ["campaignId"] = campaignId, ["commandId"] = commandId ?? string.Empty, ["status"] = status ?? "failed",
                    ["applied"] = new JArray(applied ?? new List<string>()), ["errors"] = new JArray(errors ?? new List<string>())
                }, ownsPoll).ConfigureAwait(false);
            }
            catch (ReignRequestOwnerAbandonedException) { throw; }
            catch (Exception ex) { ReignLog.Warn("Character editor command report failed: " + ex.Message); }
        }

        public static Task<JObject> InitializeNotableMbtiProfilesAsync(IList<Hero> notableHeroes)
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
            return PostJsonAsync("/notables/mbti/initialize", new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                ["heroes"] = heroes
            });
        }

        public static Task<JObject> ConfirmNotableMbtiProfilesAsync(JArray observations)
        {
            return PostJsonAsync("/notables/mbti/confirm-batch", new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                ["observations"] = observations ?? new JArray()
            });
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
        public string HeroId { get; set; } = string.Empty;
        public bool Dangerous { get; set; }
        public string Confirmation { get; set; } = string.Empty;
        public JObject Changes { get; set; } = new JObject();
    }
}

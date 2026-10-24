using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Shared.Characters;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        internal static NpcLivesOptions NpcLivesOptions { get; private set; } = new NpcLivesOptions(_ => false, _ => null);
        internal static bool NpcLivesOptionsConfirmed { get; private set; }
        internal static void SetNpcLivesOptions(JObject values)
        {
            NpcLivesOptions = new NpcLivesOptions(k => values?[k]?.Type != JTokenType.Boolean || values.Value<bool>(k),
                k => values?[k]?.Type == JTokenType.Integer || values?[k]?.Type == JTokenType.Float ? values.Value<double?>(k) : null);
            NpcLivesOptionsConfirmed = values != null;
        }
        internal static JObject CaptureNpcLivesContext(string generation, bool roster = false)
        {
            var main = MobileParty.MainParty;
            var settlement = Settlement.CurrentSettlement ?? main?.CurrentSettlement;
            var p = new JObject {
                ["campaignId"] = GetCampaignId(), ["timelineId"] = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                ["generation"] = generation ?? "", ["worldDay"] = CampaignTime.Now.ToDays,
                ["playerId"] = Hero.MainHero?.StringId ?? "", ["playerName"] = Hero.MainHero?.Name?.ToString() ?? "Player",
                ["partyId"] = main?.StringId ?? "", ["locationId"] = settlement?.StringId ?? "",
                ["location"] = settlement?.Name?.ToString() ?? "on the road"
            };
            if (roster)
            {
                // Freeze the native member roster. Presence, clan, army and settlement
                // lists are deliberately not an authority for who heard an announcement.
                p["members"] = new JArray((main?.MemberRoster.GetTroopRoster().Where(r => r.Number > 0 && r.Character.IsHero)
                    .Select(r => r.Character.HeroObject) ?? Enumerable.Empty<Hero>()).Where(h => h != null && h != Hero.MainHero)
                    .Distinct().Select(h => {
                        var row = BuildApproachCandidate(h);
                        row["inMainParty"] = main.MemberRoster.Contains(h.CharacterObject) && h.PartyBelongedTo == main && !h.IsPrisoner;
                        return row;
                    }));
            }
            return p;
        }
        internal static JArray CaptureNpcStoryLocations() => new JArray(Settlement.All
            .Where(s => s.IsTown || s.IsVillage || s.IsCastle || s.IsHideout).Select(s => new JObject {
                ["id"] = s.StringId, ["name"] = s.Name?.ToString() ?? s.StringId,
                ["kind"] = s.IsHideout ? "hideout" : s.IsTown ? "town" : s.IsCastle ? "castle" : "village",
                ["coastal"] = s.HasPort,
                ["available"] = !s.IsUnderSiege && (s.IsHideout ? s.Parties.Any(p => p.IsBandit) : !FactionManager.IsAtWarAgainstFaction(Hero.MainHero.MapFaction, s.MapFaction)),
                ["travelDays"] = MobileParty.MainParty.Position.Distance(s.GatePosition) / (4d * 24d)
            }));
        internal static Task<JObject> NpcLivesRequestAsync(string operation, JObject payload) =>
            PostJsonAsync("/npc-lives/" + operation, payload);

        internal static async Task<JObject> OpenNpcLifeApproachAsync(Hero hero, string session, JObject context, Func<bool> continueWaiting = null)
        {
            JObject p = await ReignMainThread.InvokeAsync(() => {
                ReignHeroKnowledge.MarkMet(hero);
                var value = BuildDialogueRequestPayload(hero, "", session, "", false, context);
                foreach (var field in CaptureNpcLivesContext("").Properties()) value[field.Name] = field.Value.DeepClone();
                value["heroId"] = hero.StringId; value["heroName"] = hero.Name?.ToString() ?? hero.StringId;
                value["profile"] = value["hero"].DeepClone();
                value["locationId"] = ResolveDialogueLocationId(hero);
                value["npcLifeContext"] = context?.Value<string>("npcLifeContext") ?? "in_person";
                foreach (string key in new[] { "npcStoryId", "npcLifeRequestId", "npcLivesRevision", "npcLifeContext", "npcLifeAudienceIds" })
                    if (context?[key] != null) value[key] = context[key].DeepClone();
                var audienceIds = (value["npcLifeAudienceIds"] as JArray ?? new JArray(hero.StringId)).Values<string>().ToList();
                var audience = Hero.AllAliveHeroes.Where(h => h != Hero.MainHero && audienceIds.Contains(h.StringId)).ToList();
                if (!audience.Contains(hero)) audience.Add(hero);
                value["sceneParticipants"] = BuildSceneParticipants(audience);
                value["localPresence"] = BuildLocalPresenceSnapshot(audience);
                if (!string.IsNullOrWhiteSpace(context?.Value<string>("npcLifeSceneContext")))
                    value["sceneContext"] = context.Value<string>("npcLifeSceneContext");
                AttachReadOnlyAgreementPaymentHistory(value, hero);
                return value;
            }).ConfigureAwait(false);
            var active = await ReignMainThread.InvokeAsync(() => {
                var ids = (p["npcLifeAudienceIds"] as JArray ?? new JArray(hero.StringId)).Values<string>().ToList();
                return Hero.AllAliveHeroes.Where(h => h != Hero.MainHero && ids.Contains(h.StringId)).Concat(new[] { hero }).Distinct().ToList();
            }).ConfigureAwait(false);
            await AttachContextBundlesAsync(p, p.Value<string>("npcLifeContext") == "in_person" ? "dialogue" : "party_chat", hero, active).ConfigureAwait(false);
            JObject response = await NpcLivesRequestAsync("open", p).ConfigureAwait(false);
            if (response.Value<bool?>("ok") != true) return response;
            p["operationId"] = response.Value<string>("operationId") ?? "";
            p["generation"] = response.Value<string>("generation") ?? "";
            // Follow the one saved opening for as long as this screen is open.
            // Closing stops UI polling, not the provider or its durable result.
            // Reopening recovers the same operation without a second model call.
            while (response.Value<string>("status") == "pending" && (continueWaiting == null || continueWaiting()))
            {
                await Task.Delay(500).ConfigureAwait(false);
                if (continueWaiting != null && !continueWaiting()) break;
                response = await NpcLivesRequestAsync("opening", p).ConfigureAwait(false);
            }
            return response;
        }
    }
}

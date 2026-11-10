using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using ReignBeta.Shared;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        private static readonly System.Net.Http.HttpClient ApproachOptionsClient = CreateLocalServerHttpClient(2, 5);
        private static PlayerApproachOptions _approachOptions = new PlayerApproachOptions();
        private static bool _approachOptionsConfirmed, _approachOptionsRefreshing;
        private static float _approachOptionsDelay;
        internal static bool ApproachKindEnabled(string kind) => _approachOptionsConfirmed && _approachOptions.Allows(kind);

        // Application ticks continue while an invitation holds native campaign time.
        // Never start new invitations before the first successful settings read.
        internal static void PlayerApproachOptionsTick(float dt)
        {
            ReignBeta.Economy.ReignWarEconomyOptions.EnsureLoaded();
            if (Settings.ReignBetaSettings.Instance?.UseLocalServer == false)
            { _approachOptionsConfirmed = false; return; }
            _approachOptionsDelay = Math.Max(0, _approachOptionsDelay - dt);
            if (!_approachOptionsRefreshing && _approachOptionsDelay <= 0)
            { _approachOptionsRefreshing = true; _approachOptionsDelay = 5; _ = RefreshPlayerApproachOptionsAsync(); }
            if (_approachOptionsConfirmed)
            {
                ReignBeta.Campaign.ReignPlayerApproachCampaignBehavior.Instance?.ApplyApproachOptions();
                ReignBeta.Campaign.ReignRescueConversationBehavior.Instance?.ApplyApproachOptions();
            }
        }

        private static async Task RefreshPlayerApproachOptionsAsync()
        {
            try
            {
                JObject response = await GetJsonAsync("/api/gameplay-options", ApproachOptionsClient).ConfigureAwait(false);
                if (response.Value<bool?>("ok") != true) return;
                var values = response["playerApproaches"] as JObject;
                // Older servers have no approach preferences and retain enabled defaults.
                var options = new PlayerApproachOptions(key => values?[key]?.Type != JTokenType.Boolean || values.Value<bool>(key));
                await ReignMainThread.InvokeAsync(() => { _approachOptions = options; _approachOptionsConfirmed = true;
                    SetNpcLivesOptions(response["npcLives"] as JObject); SetRulerOptions(response["rulers"] as JObject);
                    SetNewsOptions(response["announcements"] as JObject);
                    ReignBeta.Economy.ReignWarEconomyOptions.Apply(response["warEconomy"] as JObject); }).ConfigureAwait(false);
            }
            catch (Exception ex) { ReignLog.Warn("Approach options refresh failed; retaining confirmed preferences: " + ex.Message); }
            finally { await ReignMainThread.InvokeAsync(() => _approachOptionsRefreshing = false).ConfigureAwait(false); }
        }

        internal static JArray BuildPlayerAttentionPresence(Hero speaker = null)
        {
            var playerScopes = AttentionScopes(Hero.MainHero);
            MobileParty other = speaker?.PartyBelongedTo;
            bool contact = other != null && other != MobileParty.MainParty && MobileParty.MainParty != null
                && other.Position.Distance(MobileParty.MainParty.Position) <= PlayerApproachPolicy.PatrolContactRadius;
            bool nativeContact = speaker != null && CharacterObject.OneToOneConversationCharacter?.HeroObject == speaker
                && TaleWorlds.CampaignSystem.Campaign.Current?.ConversationManager?.IsConversationInProgress == true;
            string contactScope = contact ? "encounter:" + MobileParty.MainParty.StringId + ":" + other.StringId
                : nativeContact ? "native-conversation:" + Hero.MainHero.StringId + ":" + speaker.StringId : "";
            return new JArray(Hero.AllAliveHeroes.Where(h => h != null && h.IsAlive
                && (h == Hero.MainHero || AttentionScopes(h).Intersect(playerScopes).Any() || (contact && h.PartyBelongedTo == other)
                    || (nativeContact && h == speaker)))
                .OrderByDescending(h => h == Hero.MainHero || h == speaker).Take(512).Select(h => new JObject
                { ["heroId"] = h.StringId, ["name"] = h.Name?.ToString() ?? h.StringId, ["alive"] = h.IsAlive,
                    ["scopes"] = new JArray(AttentionScopes(h).Concat(contactScope.Length > 0 ? new[] { contactScope } : Array.Empty<string>())) }));
        }

        private static List<string> AttentionScopes(Hero hero)
        {
            var scopes = new List<string>();
            if (hero == null) return scopes;
            MobileParty party = hero.PartyBelongedTo ?? (hero == Hero.MainHero ? MobileParty.MainParty : null);
            Settlement settlement = hero.CurrentSettlement ?? party?.CurrentSettlement;
            if (hero == Hero.MainHero) settlement = settlement ?? Settlement.CurrentSettlement;
            if (party != null) scopes.Add("party:" + party.StringId);
            if (party?.Army?.LeaderParty != null) scopes.Add("army:" + party.Army.LeaderParty.StringId);
            if (settlement != null) scopes.Add("settlement:" + settlement.StringId);
            return scopes;
        }

        internal static JObject BuildApproachCandidate(Hero hero, bool coPresent = true)
        {
            return new JObject
            {
                ["heroId"] = hero.StringId, ["name"] = hero.Name?.ToString() ?? hero.StringId,
                ["alive"] = hero.IsAlive, ["adult"] = !hero.IsChild && hero.Age >= 18,
                ["free"] = hero.IsActive && !hero.IsPrisoner, ["coPresent"] = coPresent,
                ["known"] = hero.HasMet || hero.IsKnownToPlayer, ["isLord"] = hero.IsLord,
                ["profile"] = BuildHeroProfile(hero), ["nativeRelation"] = hero.GetRelation(Hero.MainHero),
                ["location"] = hero.CurrentSettlement?.Name?.ToString() ?? "traveling with the party",
                ["partyId"] = hero.PartyBelongedTo?.StringId ?? ""
            };
        }

        internal static Task<JObject> EvaluateApproachesAsync(string kind, JArray candidates, int healthyMembers = 0)
        {
            return PostJsonAsync("/approaches/evaluate", new JObject
            {
                ["campaignId"] = GetCampaignId(), ["timelineId"] = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                ["playerId"] = Hero.MainHero?.StringId ?? "", ["worldDay"] = CampaignTime.Now.ToDays,
                ["playerRenown"] = Clan.PlayerClan?.Renown ?? 0, ["healthyPartyMembers"] = healthyMembers,
                ["kind"] = kind, ["candidates"] = candidates
            });
        }

        internal static Task<JObject> DecideApproachAsync(string requestId, string choice) => PostJsonAsync("/approaches/decision", new JObject
        {
            ["campaignId"] = GetCampaignId(), ["timelineId"] = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
            ["playerId"] = Hero.MainHero?.StringId ?? "", ["worldDay"] = CampaignTime.Now.ToDays,
            ["requestId"] = requestId, ["choice"] = choice
        });

        internal static Task<JObject> AcknowledgeApproachAsync(string requestId, JArray observations) => PostJsonAsync("/approaches/decision", new JObject
        {
            ["campaignId"] = GetCampaignId(), ["timelineId"] = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
            ["playerId"] = Hero.MainHero?.StringId ?? "", ["worldDay"] = CampaignTime.Now.ToDays,
            ["requestId"] = requestId, ["choice"] = "acknowledge", ["nativeObservations"] = observations
        });

        internal static JArray ApplyApproachReceipts(JObject response)
        {
            var observations = new JArray();
            foreach (JObject receipt in (response?["request"]?["receipts"] as JArray ?? new JArray()).OfType<JObject>())
            {
                Hero hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == receipt.Value<string>("heroId"));
                var owner = ReignBeta.Campaign.ReignRelationshipCampaignBehavior.Instance;
                if (hero == null || owner == null) throw new InvalidOperationException("The approach relationship participants are unavailable.");
                JObject observation = owner.ApplyConversationRelationOnce(
                    new[] { receipt.Value<string>("receiptId") }, hero, Hero.MainHero, receipt.Value<int>("nativeRelationDelta"), true);
                observation["receiptId"] = receipt.Value<string>("receiptId"); observations.Add(observation);
            }
            return observations;
        }
    }
}

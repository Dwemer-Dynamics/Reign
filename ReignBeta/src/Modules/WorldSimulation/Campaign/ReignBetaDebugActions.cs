using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIPortraits;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.UI;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;
using ReignBeta.Dialogue.UI;
using ReignBeta.Diplomacy.Campaign;
using ReignBeta.Diplomacy.Integration;
using ReignBeta.KingdomEvents.Campaign;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.Portraits.Integration;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.Campaign
{
    public static partial class ReignBetaDebugActions
    {
        public static void OpenForceKingdomEventMenu()
        {
            if (!RequireTestMode())
            {
                return;
            }

            ReignKingdomEventsCampaignBehavior behavior = ReignServices.KingdomEvents;
            if (behavior == null || TaleWorlds.CampaignSystem.Campaign.Current == null)
            {
                Show("Load a campaign before forcing a kingdom event.");
                return;
            }

            List<InquiryElement> choices = ReignKingdomEventsCampaignBehavior.Catalog
                .Select(definition => new InquiryElement(
                    definition.Id,
                    definition.Title + " [" + (definition.Beneficial ? "Boon" : "Catastrophe")
                        + "] — eligible kingdoms: " + behavior.EligibleCount(definition.Id)
                        + "\n" + definition.Description,
                    null))
                .ToList();
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Force Kingdom Catastrophe or Boon",
                "Choose one production archetype. The event selects a valid kingdom only after the archetype and does not consume or alter the normal daily roll.",
                choices, true, 1, 1, "Apply Event", "Cancel",
                selected =>
                {
                    string archetypeId = selected?.FirstOrDefault()?.Identifier as string;
                    if (string.IsNullOrWhiteSpace(archetypeId))
                    {
                        return;
                    }
                    bool ok = behavior.ForceEvent(archetypeId, string.Empty, out string result);
                    Show((ok ? "Forced event: " : "Could not force event: ") + result);
                },
                null, string.Empty, false), true, false);
        }

        public static void DiagnoseCurrentConversationIdentity()
        {
            if (!RequireTestMode())
            {
                return;
            }

            Hero observer = CharacterObject.OneToOneConversationCharacter?.HeroObject;
            if (observer == null || Hero.MainHero == null)
            {
                Show("Start a one-to-one hero conversation before diagnosing identity knowledge.");
                return;
            }
            _ = DiagnoseIdentityAsync(observer);
        }

        public static void ResetCurrentConversationIdentity()
        {
            if (!RequireTestMode())
            {
                return;
            }

            Hero observer = CharacterObject.OneToOneConversationCharacter?.HeroObject;
            if (observer == null || Hero.MainHero == null)
            {
                Show("Start a one-to-one hero conversation before resetting identity knowledge.");
                return;
            }
            _ = ResetIdentityAsync(observer);
        }

        private static async Task DiagnoseIdentityAsync(Hero observer)
        {
            JObject response = await ReignServerClient.QueryIdentityAsync(observer, Hero.MainHero);
            JObject row = (response["acquaintances"] as JArray)?.OfType<JObject>().FirstOrDefault();
            if (response.Value<bool?>("ok") != true)
            {
                Show("Identity diagnosis failed: " + (response.Value<string>("error") ?? "unknown server error"));
                return;
            }
            if (row == null)
            {
                Show(observer.Name + " has no identity record for the player and has not met them through Reign.");
                return;
            }
            Show(observer.Name + " identity state=" + (row.Value<string>("identity_state") ?? "unknown")
                + ", claimed=" + (row.Value<string>("claimed_name") ?? "none")
                + ", source=" + (row.Value<string>("verification_source") ?? "none")
                + ", encounters=" + (row.Value<int?>("encounter_count") ?? 0) + ".");
        }

        private static async Task ResetIdentityAsync(Hero observer)
        {
            JObject response = await ReignServerClient.ResetIdentityAsync(observer, Hero.MainHero);
            Show(response.Value<bool?>("ok") == true
                ? "Reset " + observer.Name + "'s identity knowledge of the player."
                : "Identity reset failed: " + (response.Value<string>("error") ?? "unknown server error"));
        }

        public static void QueuePlayerKingdomPeaceOrWar()
        {
            ReignAICampaignBehavior behavior = ReignServices.AI;
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            if (behavior == null || playerKingdom == null)
            {
                Show("No active player kingdom or Bannerlord Reign behavior.");
                return;
            }

            Kingdom warTarget = FindEnemyKingdom(playerKingdom);
            if (warTarget != null)
            {
                behavior.QueueMakePeace(playerKingdom, warTarget, "Debug peace action from Bannerlord Reign.");
                return;
            }

            Kingdom target = FindPeacefulKingdom(playerKingdom);
            if (target == null)
            {
                Show("No valid kingdom target found.");
                return;
            }

            behavior.QueueDeclareWar(playerKingdom, target, "Debug war action from Bannerlord Reign.");
        }

        public static void QueueFirstLordCapturePlan()
        {
            if (!TryGetPlayerKingdom(out Kingdom playerKingdom))
            {
                return;
            }

            Hero actor = FindTestLord(playerKingdom);
            Settlement target = FindHostileFortification(playerKingdom, actor);
            if (actor == null || target == null)
            {
                Show("Need a non-player lord and a hostile fortification for the capture test.");
                return;
            }

            ReignServices.AI.QueueCaptureSettlement(actor, target, "Debug capture plan from Bannerlord Reign.");
        }

        public static void ShowLedgerSummary()
        {
            ReignAICampaignBehavior behavior = ReignServices.AI;
            if (behavior == null)
            {
                Show("Bannerlord Reign behavior is not active.");
                return;
            }

            int total = behavior.Actions.Count;
            int active = behavior.Actions.Count(x => x != null && !x.IsTerminal);
            int proposed = behavior.Actions.Count(x => x != null && x.Status == ReignWorldActionStatus.Proposed);
            int accepted = behavior.Actions.Count(x => x != null && x.Status == ReignWorldActionStatus.Accepted);
            int completed = behavior.Actions.Count(x => x != null && x.Status == ReignWorldActionStatus.Completed);
            int failed = behavior.Actions.Count(x => x != null && x.Status == ReignWorldActionStatus.Failed);
            Show("Ledger total=" + total + " active=" + active + " proposed=" + proposed + " accepted=" + accepted + " completed=" + completed + " failed=" + failed + ".");
        }

        public static void ShowTestTargetSummary()
        {
            if (!TryGetPlayerKingdom(out Kingdom kingdom))
            {
                return;
            }

            Hero lord = FindTestLord(kingdom);
            Kingdom enemy = FindEnemyKingdom(kingdom);
            Kingdom peaceful = FindPeacefulKingdom(kingdom);
            Settlement hostile = FindHostileFortification(kingdom, lord);
            Settlement anyTarget = hostile ?? FindAnyUsefulSettlement(kingdom);
            Clan claimant = FindClaimantClan(kingdom);
            IEnumerable<Clan> supporters = FindSupporterClans(kingdom, claimant);

            Show("Test targets: kingdom=" + kingdom.InformalName
                + ", enemy=" + NameOrNone(enemy)
                + ", peaceful=" + NameOrNone(peaceful)
                + ", lord=" + NameOrNone(lord)
                + ", settlement=" + NameOrNone(anyTarget)
                + ", claimant=" + NameOrNone(claimant)
                + ", supporters=" + string.Join(",", supporters.Select(x => x.Name.ToString()).DefaultIfEmpty("none")) + ".");
        }

        public static void ConfirmSetupPlayerKingdomTestScenario()
        {
            if (!RequireTestMode())
            {
                return;
            }

            if (TaleWorlds.CampaignSystem.Campaign.Current == null || Hero.MainHero == null || Clan.PlayerClan == null || MobileParty.MainParty == null)
            {
                Show("Start or load a campaign before preparing the royal-court test realm.");
                return;
            }

            if (Hero.MainHero.IsPrisoner)
            {
                Show("The royal-court test realm cannot be prepared while the player is a prisoner.");
                return;
            }

            InformationManager.ShowInquiry(new InquiryData(
                "Prepare Royal Court Test Realm",
                "Use a dedicated test save. This permanently changes the current campaign.\n\n"
                + "The player will become the kingdom ruler, receive at least two personal fortifications, and gain four non-mercenary vassal clans. The setup raises the player to 5,000,000 denars, 5,000 influence, clan tier 6, 200 elite troops, 1,000 grain, and 100 pack horses. It also gives the test vassals working influence and relations, and enables the Court system.\n\n"
                + "If the player serves another ruler, the player clan will leave that kingdom. Landless clans are recruited first; if too few exist, recruited clans return their old holdings to their former ruler so the new test realm stays compact. Existing player-ruled kingdoms are expanded but never dismantled.",
                true,
                true,
                "Prepare Realm",
                "Cancel",
                SetupPlayerKingdomTestScenario,
                null),
                true);
        }

        public static void SetupPlayerKingdomTestScenario()
        {
            try
            {
                SetupPlayerKingdomTestScenarioCore();
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Royal-court test realm setup failed: " + ex);
                Show("Royal-court test setup stopped after a native campaign error. The save may contain partial test changes; reload the dedicated test save before trying again. " + ex.Message);
            }
        }

        private static void SetupPlayerKingdomTestScenarioCore()
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current == null || Hero.MainHero == null || Clan.PlayerClan == null || MobileParty.MainParty == null)
            {
                Show("Need an active campaign, main hero, player clan, and main party before setup.");
                return;
            }

            Hero player = Hero.MainHero;
            Clan playerClan = Clan.PlayerClan;
            List<string> notes = new List<string>();
            List<Settlement> landPlan = FindRoyalCourtTestLandPlan(playerClan);
            if (landPlan.Count < 2)
            {
                Show("Could not find two safe fortifications for the royal-court test realm. No campaign changes were applied.");
                return;
            }

            const int targetGold = 5000000;
            if (player.Gold < targetGold)
            {
                GiveGoldAction.ApplyBetweenCharacters(null, player, targetGold - player.Gold, true);
                notes.Add("gold=5,000,000");
            }
            else
            {
                notes.Add("gold already 5,000,000+");
            }

            CharacterObject cataphract = MBObjectManager.Instance.GetObject<CharacterObject>("imperial_elite_cataphract");
            int troopShortfall = Math.Max(0, 200 - MobileParty.MainParty.MemberRoster.TotalManCount);
            if (cataphract != null && troopShortfall > 0)
            {
                MobileParty.MainParty.MemberRoster.AddToCounts(cataphract, troopShortfall);
                notes.Add("party raised to 200 troops");
            }
            else if (cataphract == null)
            {
                notes.Add("elite cataphract troop not found");
            }

            if (DefaultItems.Grain != null)
            {
                int grainShortfall = Math.Max(0, 1000 - MobileParty.MainParty.ItemRoster.GetItemNumber(DefaultItems.Grain));
                if (grainShortfall > 0) MobileParty.MainParty.ItemRoster.AddToCounts(DefaultItems.Grain, grainShortfall);
                notes.Add("1,000 grain ensured");
            }
            else
            {
                notes.Add("grain item not found");
            }

            ItemObject packHorse = MBObjectManager.Instance.GetObject<ItemObject>("sumpter_horse");
            if (packHorse != null)
            {
                int horseShortfall = Math.Max(0, 100 - MobileParty.MainParty.ItemRoster.GetItemNumber(packHorse));
                if (horseShortfall > 0) MobileParty.MainParty.ItemRoster.AddToCounts(packHorse, horseShortfall);
                notes.Add("100 pack horses ensured");
            }

            int requiredRenown = TaleWorlds.CampaignSystem.Campaign.Current.Models.ClanTierModel.GetRequiredRenownForTier(6);
            if (playerClan.Renown < requiredRenown)
            {
                GainRenownAction.Apply(player, requiredRenown - playerClan.Renown + 5f, true);
                notes.Add("clan tier 6 renown");
            }
            else
            {
                notes.Add("renown already tier 6");
            }

            if (playerClan.Influence < 5000f)
            {
                ChangeClanInfluenceAction.Apply(playerClan, 5000f - playerClan.Influence);
                notes.Add("influence=5,000");
            }
            else
            {
                notes.Add("influence already 5,000+");
            }

            if (playerClan.Leader != player)
            {
                playerClan.SetLeader(player);
                notes.Add("player set as clan leader");
            }

            if (playerClan.Kingdom != null && playerClan.Kingdom.RulingClan != playerClan)
            {
                Kingdom formerKingdom = playerClan.Kingdom;
                if (playerClan.IsUnderMercenaryService)
                {
                    ChangeKingdomAction.ApplyByLeaveKingdomAsMercenary(playerClan, true);
                }
                else
                {
                    ChangeKingdomAction.ApplyByLeaveKingdom(playerClan, true);
                }
                notes.Add("left " + formerKingdom.InformalName);
            }

            foreach (Settlement settlement in landPlan)
            {
                if (settlement.OwnerClan != playerClan)
                {
                    ChangeOwnerOfSettlementAction.ApplyByGift(settlement, player);
                    notes.Add(settlement.Name + " granted");
                }
            }

            if (playerClan.Kingdom == null)
            {
                TaleWorlds.CampaignSystem.Campaign.Current.KingdomManager.CreateKingdom(playerClan.Name, playerClan.InformalName, playerClan.Culture, playerClan);
                notes.Add("kingdom created");
            }

            if (playerClan.Kingdom != null && playerClan.Kingdom.RulingClan != playerClan)
            {
                ChangeRulingClanAction.Apply(playerClan.Kingdom, playerClan);
                notes.Add("player clan made ruling clan");
            }

            Kingdom kingdom = playerClan.Kingdom;
            EnsureRoyalCourtTestVassals(kingdom, player, notes);

#if !REIGN_EXCLUDE_COURT
            if (ReignBetaSettings.Instance != null && !ReignBetaSettings.Instance.CourtSystemEnabled)
            {
                ReignBetaSettings.Instance.CourtSystemEnabled = true;
                notes.Add("Court system enabled");
            }
#endif

            Settlement testSeat = landPlan.FirstOrDefault(x => x?.IsTown == true) ?? landPlan.FirstOrDefault();
            MovePlayerIntoTestSeat(testSeat, notes);

            List<string> playerLands = playerClan.Fiefs
                .Where(x => x?.Settlement?.IsFortification == true)
                .Select(x => x.Settlement.Name?.ToString() ?? x.Settlement.StringId)
                .OrderBy(x => x)
                .ToList();
            int vassalCount = kingdom?.Clans.Count(x => IsUsableTestVassal(x, kingdom)) ?? 0;
            string summary = "Royal-court test realm ready: kingdom=" + (kingdom?.InformalName?.ToString() ?? "unknown")
                + ", ruler=" + player.Name
                + ", player lands=" + string.Join(" / ", playerLands)
                + ", vassal clans=" + vassalCount
                + ", kingdom fortifications=" + (kingdom?.Fiefs.Count ?? 0)
                + ", gold=" + player.Gold.ToString("N0")
                + ", influence=" + playerClan.Influence.ToString("N0") + ".";
            ReignLog.Info(summary + " Changes: " + string.Join(", ", notes) + ".");
            Show(summary + " The player has been moved inside " + (testSeat?.Name?.ToString() ?? "the granted fortification") + " for immediate Rule Mode testing.");
        }

        public static void GenerateAllHeroFolders()
        {
            if (!RequireTestMode())
            {
                return;
            }

            Show("Generating fixed noble folders on the Bannerlord Reign server...");
            _ = GenerateAllHeroFoldersAsync();
        }

        public static void GenerateFullLivingCharacters()
        {
            if (!RequireTestMode())
            {
                return;
            }

            Show("Constructing fixed lords and ladies on the Bannerlord Reign server. Notables and wanderers remain first-contact builds.");
            _ = GenerateFullLivingCharactersAsync();
        }

        public static void ForceEventInCurrentTown()
        {
            if (!RequireTestMode())
            {
                return;
            }

            ReignSocialEventsCampaignBehavior behavior = ReignServices.SocialEvents;
            if (behavior == null)
            {
                Show("Bannerlord Reign social event behavior is not active.");
                return;
            }

            behavior.ForceEventInCurrentTown();
        }

        public static void OpenPartyChat()
        {
            if (ReignBetaSettings.Instance != null && !ReignBetaSettings.Instance.PartyChatEnabled)
            {
                Show("Party Chat is disabled in Bannerlord Reign settings.");
                return;
            }

            if (TaleWorlds.CampaignSystem.Campaign.Current == null || Hero.MainHero == null || MobileParty.MainParty == null)
            {
                Show("Party Chat is only available inside an active campaign.");
                return;
            }

            ReignPartyChatScreenManager.Open();
        }

        public static void OpenCorrespondence()
        {
            if (ReignBetaSettings.Instance != null && !ReignBetaSettings.Instance.CorrespondenceEnabled)
            {
                Show("Correspondence is disabled in Bannerlord Reign settings.");
                return;
            }

            if (TaleWorlds.CampaignSystem.Campaign.Current == null || Hero.MainHero == null || MobileParty.MainParty == null)
            {
                Show("Correspondence is only available inside an active campaign.");
                return;
            }

            ReignCorrespondenceScreenManager.Open();
        }

        public static void ForceGeneratedWildernessEvent()
        {
            if (!RequireTestMode())
            {
                return;
            }

            ReignSocialEventsCampaignBehavior behavior = ReignServices.SocialEvents;
            if (behavior == null)
            {
                Show("Bannerlord Reign social event behavior is not active.");
                return;
            }

            behavior.ForceGeneratedWildernessEvent();
        }

        public static void RequestConversationPortrait()
        {
            if (!RequireTestMode())
            {
                return;
            }

            Hero hero = CharacterObject.OneToOneConversationCharacter?.HeroObject;
            if (hero == null)
            {
                Show("Start a one-to-one hero conversation before requesting a conversation portrait.");
                return;
            }

            ReignPortraitBridge.RequestPortrait(hero);
        }

        public static void CreatePlayerPortrait()
        {
            if (RequireTestMode()) DiagnosticsRunner.RequestPlayerBuild();
        }

        public static void ClearPlayerPortrait()
        {
            if (RequireTestMode()) DiagnosticsRunner.RequestPlayerClear();
        }

        public static void ReloadPortraitsFromDisk()
        {
            if (RequireTestMode()) DiagnosticsRunner.ReloadPortraitsFromDisk();
        }

        public static void PrepareSharedPortraitCache()
        {
            if (RequireTestMode()) DiagnosticsRunner.PrepareSharedPortraitCache();
        }

		public static void StartLordEncyclopediaSourceScan()
		{
			if (RequireTestMode()) LordSourceExportService.Start();
		}

        public static void DiagnoseCurrentConversationPortrait()
        {
            if (RequireTestMode()) DiagnosticsRunner.DiagnoseCurrentConversationPortrait();
        }

        public static void MakePlayerKnowEveryone()
        {
            if (!RequireTestMode())
            {
                return;
            }

            if (TaleWorlds.CampaignSystem.Campaign.Current == null || Hero.MainHero == null)
            {
                Show("Start or load a campaign before marking the player as knowing everyone.");
                return;
            }

            List<Hero> livingHeroes = Hero.AllAliveHeroes
                .Where(hero => hero != null
                    && hero != Hero.MainHero
                    && !string.IsNullOrWhiteSpace(hero.StringId))
                .GroupBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            int nativeKnown = 0;
            foreach (Hero hero in livingHeroes)
            {
                if (!hero.IsKnownToPlayer)
                {
                    hero.IsKnownToPlayer = true;
                }
                if (hero.IsKnownToPlayer)
                {
                    nativeKnown++;
                }
            }

            Show("Marking every living hero as known in Bannerlord and Reign...");
            _ = MakePlayerKnowEveryoneAsync(livingHeroes.Count, nativeKnown);
        }

        private static async Task MakePlayerKnowEveryoneAsync(int expected, int nativeKnown)
        {
            JObject response = await ReignServerClient.MakePlayerKnowEveryoneAsync().ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() =>
            {
                int reignKnown = response.Value<int?>("known") ?? 0;
                bool reignComplete = response.Value<bool?>("ok") == true
                    && response.Value<bool?>("complete") == true
                    && reignKnown == (response.Value<int?>("requested") ?? expected);
                if (reignComplete && nativeKnown == expected)
                {
                    Show("The player now knows all " + expected + " living heroes in Bannerlord and Reign."
                        + " Created=" + (response.Value<int?>("created") ?? 0)
                        + ", upgraded=" + (response.Value<int?>("upgraded") ?? 0) + ".");
                    return;
                }

                string error = response.Value<string>("error") ?? "Reign identity verification was incomplete";
                Show("Know Everyone was incomplete: Bannerlord=" + nativeKnown + "/" + expected
                    + ", Reign=" + reignKnown + "/" + expected + ". " + error);
            }).ConfigureAwait(false);
        }

        public static void RetryCurrentFailedCharacterGeneration()
        {
            if (!RequireTestMode())
            {
                return;
            }

            Hero hero = CharacterObject.OneToOneConversationCharacter?.HeroObject;
            if (hero == null)
            {
                Show("Start a one-to-one hero conversation before retrying the current failed character.");
                return;
            }

            Show("Retrying failed character generation for " + hero.Name + "...");
            _ = RetryFailedCharacterGenerationAsync(hero);
        }

        public static void RetryAllFailedCharacterGeneration()
        {
            if (!RequireTestMode())
            {
                return;
            }

            Show("Retrying all failed character generations for this campaign...");
            _ = RetryFailedCharacterGenerationAsync(null);
        }

        private static async Task GenerateAllHeroFoldersAsync()
        {
            ReignHeroGenerationResult result = await ReignServerClient.UpsertAllHeroesAsync();
            if (result.Ok)
            {
                Show("Generated noble folders. Created=" + result.Created + ", updated=" + result.Updated + ", failed=" + result.Failed + ", total=" + result.Total + ".");
            }
            else
            {
                Show("Generate all hero folders finished with issues. Created=" + result.Created + ", updated=" + result.Updated + ", failed=" + result.Failed + ". " + result.Error);
            }
        }

        private static async Task GenerateFullLivingCharactersAsync()
        {
            ReignCharacterConstructionResult result = await ReignServerClient.ConstructAllHeroesAsync();
            if (result.Ok)
            {
                Show("Constructed fixed nobles. Built=" + result.Constructed + ", LLM=" + result.LlmUsed + ", failed=" + result.Failed + ", total=" + result.Total + ".");
            }
            else
            {
                Show("Construct living characters finished with issues. Built=" + result.Constructed + ", failed=" + result.Failed + ". " + result.Error);
            }
        }

        private static async Task RetryFailedCharacterGenerationAsync(Hero hero)
        {
            JObject response = await ReignServerClient.RetryFailedCharactersAsync(hero);
            bool ok = response.Value<bool?>("ok") == true;
            int retried = response.Value<int?>("retried") ?? 0;
            int recovered = response.Value<int?>("recovered") ?? 0;
            int failed = response.Value<int?>("failed") ?? 0;
            string error = response.Value<string>("error") ?? string.Empty;
            string target = hero == null ? "failed characters" : hero.Name.ToString();
            if (ok)
            {
                Show("Retry completed for " + target + ". Retried=" + retried + ", recovered=" + recovered + ", failed=" + failed + ".");
            }
            else
            {
                Show("Retry finished with issues for " + target + ". Retried=" + retried + ", recovered=" + recovered + ", failed=" + failed + ". " + error);
            }
        }
    }
}

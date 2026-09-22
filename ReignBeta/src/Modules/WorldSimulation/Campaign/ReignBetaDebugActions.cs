using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIPortraits;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Settings;
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

namespace ReignBeta.Campaign
{
    public static class ReignBetaDebugActions
    {
        public static void OpenForceKingdomEventMenu()
        {
            if (!RequireTestMode())
            {
                return;
            }

            ReignKingdomEventsCampaignBehavior behavior = ReignKingdomEventsCampaignBehavior.Instance;
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
            ReignAICampaignBehavior behavior = ReignAICampaignBehavior.Instance;
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

            ReignAICampaignBehavior.Instance.QueueCaptureSettlement(actor, target, "Debug capture plan from Bannerlord Reign.");
        }

        public static void ShowLedgerSummary()
        {
            ReignAICampaignBehavior behavior = ReignAICampaignBehavior.Instance;
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

            ReignSocialEventsCampaignBehavior behavior = ReignSocialEventsCampaignBehavior.Instance;
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

            ReignSocialEventsCampaignBehavior behavior = ReignSocialEventsCampaignBehavior.Instance;
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

        public static void QueueTestDeclareWar()
        {
            if (!RequireTestMode() || !TryGetPlayerKingdom(out Kingdom actor))
            {
                return;
            }

            Kingdom target = FindPeacefulKingdom(actor);
            if (target == null)
            {
                Show("No peaceful kingdom available for declare war test.");
                return;
            }

            QueueAction(new ReignWorldActionRecord
            {
                Type = ReignWorldActionType.DiplomacyDeclareWar,
                Source = "mcm_test",
                ActorKingdomStringId = actor.StringId,
                TargetKingdomStringId = target.StringId,
                Reason = "MCM test: declare war."
            }, "declare war");
        }

        public static void TriggerRandomDiplomacyPopupTest()
        {
            if (!RequireTestMode())
            {
                return;
            }

            ReignBetaSettings settings = ReignBetaSettings.Instance;
            if (settings != null && (!settings.Enabled || !settings.UseLocalServer || !settings.ExecuteDiplomacyActions || !settings.AllowAutonomousWorldTicks))
            {
                Show("Enable Bannerlord Reign, Local Server, Execute Diplomacy Actions, and Autonomous World Ticks before running the diplomacy popup test.");
                return;
            }
            if (TaleWorlds.CampaignSystem.Campaign.Current == null || ReignAICampaignBehavior.Instance == null)
            {
                Show("Load a campaign before running the diplomacy popup test.");
                return;
            }
            int npcKingdoms = Kingdom.All.Count(x => x != null && !x.IsEliminated && x.Leader != null && x != Clan.PlayerClan?.Kingdom);
            if (npcKingdoms < 2)
            {
                Show("At least two living non-player kingdoms are required for the diplomacy popup test.");
                return;
            }

            Show("Selecting one eligible NPC ruler and making the normal diplomacy initiative rolls...");
            _ = RunRandomDiplomacyPopupTestAsync();
        }

        private static async Task RunRandomDiplomacyPopupTestAsync()
        {
            ReignRandomDiplomacyTestResult test = await ReignServerClient.QueueRandomDiplomacyPopupTestAsync().ConfigureAwait(false);
            if (!test.Ok)
            {
                await ReignMainThread.InvokeAsync(() => Show("Diplomacy initiative test could not run: " + (string.IsNullOrWhiteSpace(test.Error) ? "unknown server error" : test.Error))).ConfigureAwait(false);
                return;
            }

            if (string.Equals(test.Status, "no_initiative", StringComparison.OrdinalIgnoreCase)
                || string.Equals(test.Status, "no_ruler_eligible", StringComparison.OrdinalIgnoreCase)
                || string.Equals(test.Status, "ruler_chose_no_action", StringComparison.OrdinalIgnoreCase))
            {
                string ruler = string.IsNullOrWhiteSpace(test.RulerName) ? "The selected ruler" : test.RulerName;
                string detail = string.IsNullOrWhiteSpace(test.InitiativeSummary) ? string.Empty : " Rolls: " + test.InitiativeSummary + ".";
                string outcome = string.Equals(test.Status, "ruler_chose_no_action", StringComparison.OrdinalIgnoreCase)
                    ? ruler + " passed an initiative roll but chose no valid diplomatic action."
                    : ruler + " did not pass a normal diplomacy initiative roll.";
                await ReignMainThread.InvokeAsync(() => Show(outcome + detail)).ConfigureAwait(false);
                return;
            }

            if (!test.Idempotent && test.Action != null)
            {
                Tuple<bool, string> validation = await ReignMainThread.InvokeAsync(() =>
                {
                    bool valid = ReignActionValidator.Validate(test.Action, out string failure);
                    return Tuple.Create(valid, failure ?? string.Empty);
                }).ConfigureAwait(false);
                if (!validation.Item1)
                {
                    ReignActionResult rejected = ReignActionResult.ValidationFailed(validation.Item2);
                    await ReignServerClient.ReportActionAsync(test.Action, "failed", validation.Item2, rejected).ConfigureAwait(false);
                    await ReignMainThread.InvokeAsync(() => Show("Random diplomacy test was rejected safely: " + validation.Item2)).ConfigureAwait(false);
                    return;
                }

                ReignActionResult execution = await ReignMainThread.InvokeAsync(() => ReignAICampaignBehavior.Instance?.ExecuteActionForTest(test.Action)).ConfigureAwait(false);
                if (execution == null || !execution.Success || !execution.Completed)
                {
                    string failure = execution?.Message ?? "The Bannerlord action behavior was unavailable.";
                    await ReignMainThread.InvokeAsync(() => Show("Random diplomacy test did not complete: " + failure)).ConfigureAwait(false);
                    return;
                }
            }

            ReignWorldDiplomacyCampaignBehavior diplomacy = await ReignMainThread.InvokeAsync(() =>
                TaleWorlds.CampaignSystem.Campaign.Current?.GetCampaignBehavior<ReignWorldDiplomacyCampaignBehavior>()).ConfigureAwait(false);
            bool popupQueued = !string.IsNullOrWhiteSpace(test.EventId)
                && diplomacy != null
                && await diplomacy.WaitForAnnouncementAndShowForTestAsync(test.EventId).ConfigureAwait(false);
            string description = test.ActorKingdomName + " -> " + test.TargetKingdomName + ": " + test.ActionLabel;
            await ReignMainThread.InvokeAsync(() => Show(popupQueued
                ? "Diplomacy initiative passed and completed (" + description + "). The real event popup is queued; close MCM to return to the campaign map if it is not already visible."
                : "Diplomacy initiative passed (" + description + "), but its popup receipt is still pending. Normal diplomacy polling will continue trying after MCM closes.")).ConfigureAwait(false);
        }

        public static void QueueTestMakePeace()
        {
            QueuePeaceAction(ReignWorldActionType.DiplomacyMakePeace, "MCM test: make peace.", null, "make peace");
        }

        public static void QueueTestTributePeace()
        {
            JObject terms = new JObject
            {
                ["dailyTribute"] = 150,
                ["durationDays"] = 30
            };
            QueuePeaceAction(ReignWorldActionType.DiplomacyOfferTributePeace, "MCM test: offer tribute peace.", terms, "tribute peace");
        }

        public static void QueueTestRecordPromise()
        {
            if (!RequireTestMode() || !TryGetPlayerKingdom(out Kingdom actor))
            {
                return;
            }

            Kingdom target = FindEnemyKingdom(actor) ?? FindPeacefulKingdom(actor);
            if (target == null)
            {
                Show("No kingdom target available for promise test.");
                return;
            }

            QueueAction(new ReignWorldActionRecord
            {
                Type = ReignWorldActionType.DiplomacyRecordPromise,
                Source = "mcm_test",
                ActorKingdomStringId = actor.StringId,
                TargetKingdomStringId = target.StringId,
                Reason = "MCM test: diplomatic promise recorded for future memory."
            }, "record promise");
        }

        public static void QueueTestReparationsPeace()
        {
            JObject terms = new JObject
            {
                ["reparationsGold"] = 15000,
                ["dailyTribute"] = 200,
                ["durationDays"] = 30
            };
            QueuePeaceAction(ReignWorldActionType.DiplomacyDemandReparationsPeace, "MCM test: demand reparations for peace.", terms, "reparations peace");
        }

        public static void QueueTestSettlementSurrenderPeace()
        {
            QueueSurrenderAction(ReignWorldActionType.DiplomacyDemandSettlementPeace, "MCM test: demand settlement surrender for peace.", false);
        }

        public static void QueueTestFullSurrenderPeace()
        {
            QueueSurrenderAction(ReignWorldActionType.DiplomacyDemandSurrenderPeace, "MCM test: demand full surrender terms.", true);
        }

        public static void QueueTestRecruitAndRecover()
        {
            QueueStrategyAction(ReignWorldActionType.StrategyRecruitAndRecover, "MCM test: recruit and recover.", "recruit and recover", false);
        }

        public static void QueueTestFormArmy()
        {
            QueueStrategyAction(ReignWorldActionType.StrategyFormArmy, "MCM test: form army.", "form army", false);
        }

        public static void QueueTestAttackSettlement()
        {
            QueueStrategyAction(ReignWorldActionType.StrategyAttackSettlement, "MCM test: attack settlement.", "attack settlement", true);
        }

        public static void QueueTestCapturePlan()
        {
            QueueStrategyAction(ReignWorldActionType.StrategyCaptureSettlement, "MCM test: capture settlement plan.", "capture settlement plan", true);
        }

        public static void QueueTestRulingClanRebellion()
        {
            if (!RequireTestMode())
            {
                return;
            }
            Show("NPC rebellion starts now come only from the saved weekly native-relationship roll. To test a player rebellion, serve as a non-ruling vassal and explicitly declare rebellion to the current ruler in direct conversation or by letter.");
        }

        public static void QueueTestInstallRulingClan()
        {
            if (!RequireTestMode() || !TryGetPlayerKingdom(out Kingdom kingdom))
            {
                return;
            }

            Clan claimant = FindClaimantClan(kingdom);
            if (claimant == null)
            {
                Show("No non-ruling clan available for install ruling clan test.");
                return;
            }

            QueueAction(new ReignWorldActionRecord
            {
                Type = ReignWorldActionType.PoliticsInstallRulingClan,
                Source = "mcm_test",
                ActorKingdomStringId = kingdom.StringId,
                ActorClanStringId = claimant.StringId,
                Reason = "MCM test: install new ruling clan."
            }, "install ruling clan");
        }

        private static void QueuePeaceAction(ReignWorldActionType type, string reason, JObject terms, string label)
        {
            if (!RequireTestMode() || !TryGetPlayerKingdom(out Kingdom actor))
            {
                return;
            }

            Kingdom target = FindEnemyKingdom(actor);
            if (target == null)
            {
                Show("No enemy kingdom available. Use Declare War test first.");
                return;
            }

            QueueAction(new ReignWorldActionRecord
            {
                Type = type,
                Source = "mcm_test",
                ActorKingdomStringId = actor.StringId,
                TargetKingdomStringId = target.StringId,
                TermsJson = terms == null ? string.Empty : terms.ToString(Formatting.None),
                Reason = reason
            }, label);
        }

        private static void QueueSurrenderAction(ReignWorldActionType type, string reason, bool includeReparations)
        {
            if (!RequireTestMode() || !TryGetPlayerKingdom(out Kingdom actor))
            {
                return;
            }

            Kingdom target = FindEnemyKingdom(actor);
            if (target == null)
            {
                Show("No enemy kingdom available. Use Declare War test first.");
                return;
            }

            Settlement settlement = FindEnemyFortificationOwnedBy(target);
            if (settlement == null)
            {
                Show("Enemy kingdom has no town/castle available for surrender test.");
                return;
            }

            JObject terms = new JObject
            {
                ["settlementIds"] = new JArray(settlement.StringId)
            };

            if (includeReparations)
            {
                terms["reparationsGold"] = 25000;
                terms["dailyTribute"] = 300;
                terms["durationDays"] = 45;
            }

            QueueAction(new ReignWorldActionRecord
            {
                Type = type,
                Source = "mcm_test",
                ActorKingdomStringId = actor.StringId,
                TargetKingdomStringId = target.StringId,
                TargetSettlementStringId = settlement.StringId,
                TermsJson = terms.ToString(Formatting.None),
                Reason = reason
            }, type == ReignWorldActionType.DiplomacyDemandSurrenderPeace ? "full surrender peace" : "settlement surrender peace");
        }

        private static void QueueStrategyAction(ReignWorldActionType type, string reason, string label, bool requireHostile)
        {
            if (!RequireTestMode() || !TryGetPlayerKingdom(out Kingdom kingdom))
            {
                return;
            }

            Hero actor = FindTestLord(kingdom);
            if (actor == null)
            {
                Show("No non-player lord with an active party found.");
                return;
            }

            Settlement target = requireHostile ? FindHostileFortification(kingdom, actor) : FindHostileFortification(kingdom, actor) ?? FindAnyUsefulSettlement(kingdom);
            if (target == null)
            {
                Show("No valid settlement target found for " + label + ".");
                return;
            }

            QueueAction(new ReignWorldActionRecord
            {
                Type = type,
                Source = "mcm_test",
                ActorHeroStringId = actor.StringId,
                ActorKingdomStringId = kingdom.StringId,
                TargetSettlementStringId = target.StringId,
                Reason = reason,
                MinimumTroops = 40,
                DesiredStrength = 350,
                MaxAttempts = type == ReignWorldActionType.StrategyCaptureSettlement ? 24 : 3
            }, label);
        }

        private static void QueueAction(ReignWorldActionRecord action, string label)
        {
            ReignAICampaignBehavior behavior = ReignAICampaignBehavior.Instance;
            if (behavior == null)
            {
                Show("Bannerlord Reign behavior is not active.");
                return;
            }

            if (!ReignActionValidator.Validate(action, out string failure))
            {
                Show("Cannot queue " + label + ": " + failure);
                return;
            }

            behavior.EnqueueAction(action);
            Show("MCM test queued " + label + ".");
        }

        private static bool RequireTestMode()
        {
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            if (settings == null || settings.McmTestModeEnabled)
            {
                return true;
            }

            Show("Enable Bannerlord Reign > Debug > Enable Debug Controls before using test actions.");
            return false;
        }

        private static bool TryGetPlayerKingdom(out Kingdom kingdom)
        {
            kingdom = Clan.PlayerClan?.Kingdom;
            if (ReignAICampaignBehavior.Instance == null || kingdom == null)
            {
                Show("No active player kingdom or Bannerlord Reign behavior.");
                return false;
            }

            return true;
        }

        private static Kingdom FindEnemyKingdom(Kingdom actor)
        {
            return Kingdom.All.FirstOrDefault(x => x != null && x != actor && !x.IsEliminated && actor.IsAtWarWith(x));
        }

        private static Kingdom FindPeacefulKingdom(Kingdom actor)
        {
            return Kingdom.All.FirstOrDefault(x => x != null && x != actor && !x.IsEliminated && !actor.IsAtWarWith(x));
        }

        private static Hero FindTestLord(Kingdom kingdom)
        {
            return kingdom?.AliveLords
                .Where(x => x != null && x != Hero.MainHero && x.PartyBelongedTo != null && !x.PartyBelongedTo.IsMainParty)
                .OrderByDescending(x => x.PartyBelongedTo.Party?.EstimatedStrength ?? 0f)
                .FirstOrDefault();
        }

        private static Settlement FindHostileFortification(Kingdom kingdom, Hero actor)
        {
            if (kingdom == null)
            {
                return null;
            }

            Vec2 origin = actor?.PartyBelongedTo == null ? Vec2.Zero : actor.PartyBelongedTo.GetPosition2D;
            return Settlement.All
                .Where(x => x != null
                    && x.IsFortification
                    && x.MapFaction != null
                    && kingdom.IsAtWarWith(x.MapFaction)
                    && !x.IsUnderSiege)
                .OrderBy(x => origin == Vec2.Zero ? 0f : origin.DistanceSquared(x.GetPosition2D))
                .FirstOrDefault();
        }

        private static Settlement FindEnemyFortificationOwnedBy(Kingdom target)
        {
            return Settlement.All
                .Where(x => x != null && x.IsFortification && x.MapFaction == target && !x.IsUnderSiege)
                .OrderBy(x => x.StringId)
                .FirstOrDefault();
        }

        private static Settlement FindAnyUsefulSettlement(Kingdom kingdom)
        {
            return kingdom?.Settlements.FirstOrDefault(x => x != null && !x.IsHideout)
                ?? Settlement.All.FirstOrDefault(x => x != null && !x.IsHideout);
        }

        private static List<Settlement> FindRoyalCourtTestLandPlan(Clan playerClan)
        {
            List<Settlement> plan = playerClan?.Fiefs
                .Select(x => x?.Settlement)
                .Where(x => x?.IsFortification == true && !x.IsUnderSiege)
                .OrderByDescending(x => x.IsTown)
                .ThenBy(x => x.StringId)
                .Take(2)
                .ToList() ?? new List<Settlement>();
            if (plan.Count >= 2) return plan;

            Settlement anchor = FindZeonica();
            IFaction anchorFaction = anchor?.MapFaction;
            IEnumerable<Settlement> candidates = Settlement.All
                .Where(x => x != null
                    && x.IsFortification
                    && !x.IsUnderSiege
                    && x.OwnerClan != null
                    && x.OwnerClan != playerClan
                    && !plan.Contains(x))
                .OrderBy(x => x == anchor ? 0 : anchorFaction != null && x.MapFaction == anchorFaction ? 1 : 2)
                .ThenBy(x => x == anchor ? 0 : x.IsCastle ? 0 : 1)
                .ThenBy(x => playerClan != null && x.Culture == playerClan.Culture ? 0 : 1)
                .ThenBy(x => x.StringId);
            foreach (Settlement candidate in candidates)
            {
                plan.Add(candidate);
                if (plan.Count >= 2) break;
            }
            return plan;
        }

        private static void EnsureRoyalCourtTestVassals(Kingdom kingdom, Hero player, List<string> notes)
        {
            if (kingdom == null || player == null) return;
            const int targetVassalClans = 4;

            List<Clan> invalidTestVassals = kingdom.Clans
                .Where(x => x != kingdom.RulingClan && x != Clan.PlayerClan && IsInvalidTestVassal(x))
                .ToList();
            foreach (Clan invalid in invalidTestVassals)
            {
                if (invalid.IsUnderMercenaryService)
                {
                    ChangeKingdomAction.ApplyByLeaveKingdomAsMercenary(invalid, true);
                }
                else
                {
                    ChangeKingdomAction.ApplyByLeaveKingdom(invalid, true);
                }
                notes?.Add(invalid.Name + " removed from the test realm because it is not a regular noble clan");
            }

            List<Clan> current = kingdom.Clans.Where(x => IsUsableTestVassal(x, kingdom)).ToList();
            int needed = Math.Max(0, targetVassalClans - current.Count);
            if (needed > 0)
            {
                List<Clan> candidates = Clan.All
                    .Where(x => x != null
                        && x != Clan.PlayerClan
                        && x.Kingdom != kingdom
                        && !x.IsEliminated
                        && !x.IsMinorFaction
                        && !x.IsBanditFaction
                        && !x.IsClanTypeMercenary
                        && !x.IsUnderMercenaryService
                        && x.Leader != null
                        && x.Leader.IsAlive
                        && x.Leader.IsActive
                        && !x.Leader.IsPrisoner
                        && (x.Kingdom == null || x.Kingdom.RulingClan != x))
                    .OrderBy(x => x.Fiefs.Count == 0 ? 0 : 1)
                    .ThenBy(x => x.Culture == Clan.PlayerClan?.Culture ? 0 : 1)
                    .ThenBy(x => x.Fiefs.Count)
                    .ThenByDescending(x => x.Tier)
                    .ThenBy(x => x.StringId)
                    .ToList();

                foreach (Clan candidate in candidates)
                {
                    if (needed <= 0) break;
                    Kingdom formerKingdom = candidate.Kingdom;
                    List<Settlement> formerHoldings = candidate.Fiefs
                        .Select(x => x?.Settlement)
                        .Where(x => x != null)
                        .ToList();
                    Hero formerRuler = formerKingdom?.RulingClan?.Leader;
                    if (formerHoldings.Count > 0 && formerRuler == null) continue;

                    foreach (Settlement holding in formerHoldings)
                    {
                        ChangeOwnerOfSettlementAction.ApplyByGift(holding, formerRuler);
                    }

                    ChangeKingdomAction.ApplyByJoinToKingdom(candidate, kingdom, CampaignTime.Zero, true);
                    if (candidate.IsUnderMercenaryService)
                    {
                        EndMercenaryServiceAction.EndByBecomingVassal(candidate);
                    }

                    if (IsUsableTestVassal(candidate, kingdom))
                    {
                        current.Add(candidate);
                        needed--;
                        notes?.Add(candidate.Name + " joined as a regular test vassal");
                    }
                    else
                    {
                        notes?.Add(candidate.Name + " could not be confirmed as a regular test vassal");
                    }
                }
            }

            foreach (Clan clan in kingdom.Clans.Where(x => IsUsableTestVassal(x, kingdom)).Take(targetVassalClans))
            {
                if (clan.Influence < 500f) ChangeClanInfluenceAction.Apply(clan, 500f - clan.Influence);
                int relation = player.GetRelation(clan.Leader);
                if (relation < 25) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(player, clan.Leader, 25 - relation, false);
            }

            int finalCount = kingdom.Clans.Count(x => IsUsableTestVassal(x, kingdom));
            notes?.Add(finalCount >= targetVassalClans
                ? "four usable vassal clans ensured"
                : "only " + finalCount + " usable vassal clans were available");
        }

        private static void MovePlayerIntoTestSeat(Settlement settlement, List<string> notes)
        {
            MobileParty party = MobileParty.MainParty;
            if (party == null || settlement == null)
            {
                notes?.Add("test-seat travel unavailable");
                return;
            }

            if (party.MapEvent != null)
            {
                notes?.Add("test-seat travel deferred because the player is in a map event");
                return;
            }

            if (party.CurrentSettlement == settlement
                && PlayerEncounter.EncounterSettlement == settlement
                && PlayerEncounter.LocationEncounter != null)
            {
                party.SetMoveModeHold();
                notes?.Add("already inside " + settlement.Name);
                return;
            }

            if (PlayerEncounter.Current != null)
            {
                PlayerEncounter.Finish();
            }
            else if (party.CurrentSettlement != null)
            {
                LeaveSettlementAction.ApplyForParty(party);
            }

            party.SetPositionAfterMapChange(settlement.GatePosition);
            party.SetMoveModeHold();
            EncounterManager.StartSettlementEncounter(party, settlement);
            if (PlayerEncounter.EncounterSettlement != settlement
                || PlayerEncounter.LocationEncounter == null
                || party.CurrentSettlement != settlement)
            {
                throw new InvalidOperationException("Native settlement encounter did not initialize for " + settlement.Name + ".");
            }
            notes?.Add("player moved inside " + settlement.Name);
        }

        private static bool IsUsableTestVassal(Clan clan, Kingdom kingdom)
        {
            return clan != null
                && clan.Kingdom == kingdom
                && clan != kingdom?.RulingClan
                && clan != Clan.PlayerClan
                && !clan.IsEliminated
                && !clan.IsMinorFaction
                && !clan.IsBanditFaction
                && !clan.IsClanTypeMercenary
                && !clan.IsUnderMercenaryService
                && clan.Leader != null
                && clan.Leader.IsAlive
                && clan.Leader.IsActive
                && !clan.Leader.IsPrisoner;
        }

        private static bool IsInvalidTestVassal(Clan clan)
        {
            return clan == null
                || clan.IsEliminated
                || clan.IsMinorFaction
                || clan.IsBanditFaction
                || clan.IsClanTypeMercenary
                || clan.IsUnderMercenaryService;
        }

        private static Settlement FindZeonica()
        {
            return Settlement.All.FirstOrDefault(x => x != null
                    && x.IsTown
                    && string.Equals(x.StringId, "town_EW2", System.StringComparison.OrdinalIgnoreCase))
                ?? Settlement.All.FirstOrDefault(x => x != null
                    && x.IsTown
                    && x.Name != null
                    && x.Name.ToString().IndexOf("Zeonica", System.StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static Clan FindClaimantClan(Kingdom kingdom)
        {
            return kingdom?.Clans
                .Where(x => x != null && x != kingdom.RulingClan && !x.IsEliminated && !x.IsClanTypeMercenary)
                .OrderByDescending(x => x.Fiefs.Count)
                .FirstOrDefault();
        }

        private static IEnumerable<Clan> FindSupporterClans(Kingdom kingdom, Clan claimant)
        {
            return kingdom?.Clans
                .Where(x => x != null && x != claimant && x != kingdom.RulingClan && !x.IsEliminated && !x.IsClanTypeMercenary)
                .OrderByDescending(x => x.Fiefs.Count)
                .Take(2)
                ?? Enumerable.Empty<Clan>();
        }

        private static string NameOrNone(Kingdom kingdom)
        {
            return kingdom == null ? "none" : kingdom.InformalName.ToString();
        }

        private static string NameOrNone(Hero hero)
        {
            return hero == null ? "none" : hero.Name.ToString();
        }

        private static string NameOrNone(Settlement settlement)
        {
            return settlement == null ? "none" : settlement.Name.ToString();
        }

        private static string NameOrNone(Clan clan)
        {
            return clan == null ? "none" : clan.Name.ToString();
        }

        private static void Show(string message)
        {
            InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + message, Color.FromUint(0xFF66CCFF)));
        }
    }
}

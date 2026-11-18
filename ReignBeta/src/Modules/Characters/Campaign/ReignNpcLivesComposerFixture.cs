using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Actions;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;
using static ReignBeta.Characters.Campaign.ReignNpcLivesBehavior;
using ReignBeta.Characters.Campaign;
using ReignBeta.Characters.UI;
using ReignBeta.Dialogue.UI;
using ReignBeta.Persistence.Campaign;
using ReignBeta.WorldSimulation.Actions;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.Campaign
{
    // Harness-owned party composer fixture.
    internal static partial class ReignNpcLivesTestControls
    {
        // Only the native test dispatcher calls this, after its exact enrolled Current,
        // campaign/timeline/process, paused clock and retained recovery checkpoint gates.
        internal static JObject PrepareNativePartyComposerCase(ReignNpcLivesBehavior lives, JObject test)
        {
            string[] fields = { "caseId", "phase", "heroId", "otherHeroId", "operationId" };
            if (test.Count != fields.Length || test.Properties().Any(p => !fields.Contains(p.Name)
                || p.Value.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)p.Value)
                || ((string)p.Value).Length > 128 || ((string)p.Value).Any(ch => !char.IsLetterOrDigit(ch) && ch != '_' && ch != '-' && ch != ':'))
                || test.Value<string>("phase") != "composer" || test.Value<string>("heroId") == test.Value<string>("otherHeroId"))
                throw new InvalidOperationException("Supply only the bounded case, operation and two distinct actual party NPC identities.");
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            var party = MobileParty.MainParty;
            if (!ReignLiveInteractionTestHost.OwnsNpcLivesSession || !lives._initialized || string.IsNullOrEmpty(lives.Generation)
                || !lives.Current(campaign, lives._epoch) || lives._busy || lives._outbox.Count != 0 || lives._failedOperations.Count != 0
                || campaign.TimeControlMode != CampaignTimeControlMode.Stop || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress
                || party == null || !party.IsActive || party.IsDisbanding || party.LeaderHero != Hero.MainHero
                || Hero.MainHero == null || Hero.MainHero.IsDead || Hero.MainHero.IsChild || Hero.MainHero.IsPrisoner
                || party.Army != null || party.MapEvent != null || party.SiegeEvent != null
                || party.IsCurrentlyAtSea || party.IsTransitionInProgress || Mission.Current != null
                || campaign.ConversationManager?.IsConversationInProgress == true)
                throw new InvalidOperationException("An idle exact paused NPC Lives session and free independent land player are required.");
            string operationId = "npc_party_composer:" + test.Value<string>("operationId");
            string requestHash = PartyComposerDigest(new { campaign = lives._campaign, timeline = lives._timeline, generation = lives.Generation,
                arguments = new JObject(test.Properties().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new JProperty(p.Name, p.Value.DeepClone()))) });
            var journal = ReignServices.Agreement ?? throw new InvalidOperationException("Native fixture journal is unavailable.");
            var retained = journal.NativeOperation(operationId);
            if (retained != null)
            {
                if (retained.Value<string>("requestHash") != requestHash)
                    throw new InvalidOperationException("This operation identity already belongs to another exact composer request.");
                retained["replayed"] = true;
                retained["currentComposer"] = ObserveNativePartyComposer();
                retained["currentConservation"] = ObservePartyComposerConservation(lives, test.Value<string>("heroId"), test.Value<string>("otherHeroId"));
                return retained; // Including started/uncertain: never reopen a partial operation.
            }
            Hero RequireMember(string id)
            {
                var npc = ReignActionService.HeroById(id);
                if (npc == null || npc == Hero.MainHero || !npc.IsAlive || !npc.IsActive || npc.IsChild || npc.IsPrisoner
                    || npc.PartyBelongedTo != party || party.MemberRoster.GetTroopCount(npc.CharacterObject) < 1)
                    throw new InvalidOperationException("Both identities must be actual living free adult members of the main party; town bystanders are not eligible.");
                return npc;
            }
            var first = RequireMember(test.Value<string>("heroId"));
            var second = RequireMember(test.Value<string>("otherHeroId"));
            if (!QuietMap() || ReignActionBuilder.IsOpen || ReignTavernHouseScreenManager.IsOpen)
                throw new InvalidOperationException("Close all conversations, inquiries and builders before preparing this unsent party composer.");
            var before = ObservePartyComposerConservation(lives, first.StringId, second.StringId);
            var receipt = new JObject { ["schema"] = "reign-npc-lives-party-composer-v1", ["origin"] = "operator_unsent_ui_fixture",
                ["caseId"] = test["caseId"], ["operationId"] = operationId, ["requestHash"] = requestHash,
                ["status"] = "started", ["invocations"] = 1, ["replayed"] = false, ["consentGranted"] = false,
                ["before"] = before, ["cleanup"] = "Cancel the unsent builder and leave party chat; restore only the enrolled Current if recovery is needed." };
            journal.SaveNativeOperation(operationId, receipt);
            bool opened = false;
            try
            {
                ReignPartyChatScreenManager.Open();
                opened = ReignPartyChatScreenManager.IsOpenOrPending;
                var vm = ReignPartyChatScreenManager.ActiveViewModel;
                if (!ReignPartyChatScreenManager.IsOpen || vm == null || vm.ActionBusy || !vm.InputEnabled
                    || vm.ActionDraft != null || !string.IsNullOrEmpty(vm.InputText)
                    || !vm.SelectAutomationHeroes(new[] { first, second })
                    || !ReignActionBuilder.TryOpenForHost(ScreenManager.TopScreen, vm)
                    || !ReignActionBuilder.IsOpen || ReignActionBuilder.IsCalibrationFixture)
                    throw new InvalidOperationException("The real unsent party action builder did not open for exactly the requested members.");
                var after = ObservePartyComposerConservation(lives, first.StringId, second.StringId);
                receipt["after"] = after;
                receipt["conserved"] = JToken.DeepEquals(before, after);
                if (!JToken.DeepEquals(before, after)) throw new InvalidOperationException("Party composer preparation changed conserved campaign state.");
                receipt["status"] = "prepared";
            }
            catch (Exception ex)
            {
                receipt["status"] = "uncertain"; receipt["error"] = ex.Message;
                if (opened) { ReignActionBuilder.Close(); ReignPartyChatScreenManager.Close(); }
                receipt["after"] = ObservePartyComposerConservation(lives, first.StringId, second.StringId);
            }
            receipt["currentComposer"] = ObserveNativePartyComposer();
            journal.SaveNativeOperation(operationId, receipt);
            return receipt;
        }

        private static JObject ObserveNativePartyComposer()
        {
            var vm = ReignPartyChatScreenManager.ActiveViewModel;
            var draft = vm?.ActionDraft;
            return new JObject { ["partyChatOpen"] = ReignPartyChatScreenManager.IsOpen,
                ["builderOpen"] = ReignActionBuilder.IsOpen, ["calibration"] = ReignActionBuilder.IsCalibrationFixture,
                ["participantIds"] = new JArray(vm?.ActionParticipants.Select(h => h.StringId) ?? Enumerable.Empty<string>()),
                ["hasActionDraft"] = draft != null, ["draftClauseCount"] = draft?.Clauses.Count ?? 0,
                ["draftDigest"] = PartyComposerDigest(draft), ["textLength"] = vm?.InputText?.Length ?? 0,
                ["textDigest"] = PartyComposerDigest(vm?.InputText ?? ""), ["canAnnounce"] = vm?.CanAnnounce ?? false };
        }

        private static JObject ObservePartyComposerConservation(ReignNpcLivesBehavior lives, string first, string second)
        {
            var journal = ReignServices.Agreement;
            return new JObject { ["campaignId"] = lives._campaign, ["timelineId"] = lives._timeline, ["generation"] = lives.Generation,
                ["worldDay"] = CampaignTime.Now.ToDays,
                ["timeMode"] = TaleWorlds.CampaignSystem.Campaign.Current.TimeControlMode.ToString(),
                ["npcLivesDigest"] = PartyComposerDigest(lives.State), ["outboxDigest"] = PartyComposerDigest(lives._outbox),
                ["outboxCount"] = lives._outbox.Count, ["nativeEffectsDigest"] = PartyComposerDigest(lives._nativeEffects.OrderBy(x => x, StringComparer.Ordinal)),
                ["offerCount"] = journal.Offers.Count, ["agreementCount"] = journal.Agreements.Count,
                ["mainPartyContentsDigest"] = JObject.FromObject(ObserveLifePartyContents(MobileParty.MainParty))["Digest"],
                ["participants"] = new JArray(new[] { Hero.MainHero, ReignActionService.HeroById(first), ReignActionService.HeroById(second) }
                    .Select(h => new JObject { ["heroId"] = h.StringId, ["gold"] = h.Gold,
                        ["nativeRelation"] = h == Hero.MainHero ? 0 : h.GetRelation(Hero.MainHero),
                        ["skills"] = new JObject(typeof(DefaultSkills).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                            .Where(p => p.PropertyType == typeof(SkillObject)).Select(p => (SkillObject)p.GetValue(null))
                            .Where(skill => skill != null).Select(skill => new JProperty(skill.StringId, h.GetSkillValue(skill)))) })) };
        }

        private static string PartyComposerDigest(object value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value))))
                    .Replace("-", "").ToLowerInvariant();
        }
    }
}

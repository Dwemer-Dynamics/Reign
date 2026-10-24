using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Shared.Characters;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace ReignBeta.Campaign
{
    public sealed partial class ReignNpcLivesBehavior
    {
        private Dictionary<string, JObject> _storyBindings = new Dictionary<string, JObject>(StringComparer.Ordinal);
        private void ReconcileStoryBindings()
        {
            foreach (var story in State.Stories.Where(s => s.Status == "active" && s.PhaseIndex >= s.Definition.SupportingRevealPhase
                && s.Definition.SupportingRole.Length > 0))
            {
                // Accepted identities stay fixed. Newly accepted stories can bind
                // while time is paused without recapturing an acknowledged binding.
                if (_acceptedOperations.Contains(story.Id + ":binding") || _failedOperations.ContainsKey(story.Id + ":binding")) continue;
                try
                {
                if (!_storyBindings.TryGetValue(story.Id, out var record))
                {
                    // Reuse a recovered server binding; never allocate a replacement identity.
                    record = JObject.FromObject(story.Bindings); _storyBindings.Add(story.Id, record);
                }
                if (record.Value<string>("heroId") == null)
                {
                    Hero owner = ReignObjectResolver.FindHero(story.HeroId);
                    Settlement home = Settlement.Find(story.LocationId);
                    if (home == null || home.IsHideout) home = owner?.HomeSettlement ?? Settlement.All.FirstOrDefault(s => s.IsTown && s.Culture == owner?.Culture);
                    if (owner?.Culture?.Townsman == null || home == null) continue;
                    if (record.Value<string>("heroId") == null)
                    {
                        string name = WandererNames.Choose(owner.Culture.StringId, false, story.Id,
                            Hero.AllAliveHeroes.Select(h => h.FirstName?.ToString() ?? h.Name.ToString()));
                        // Same native allocation/name path as encountered residents; Special
                        // keeps the personal contact out of random wanderer maintenance.
                        Hero contact = HeroCreator.CreateSpecialHero(owner.Culture.Townsman, home, null, null,
                            Math.Max(20, (int)owner.Age));
                        record["heroId"] = contact.StringId; // Reserve immediately, including partial initialization failures.
                        record["heroName"] = name; record["role"] = story.Definition.SupportingRole;
                        contact.Clan = null; contact.Father = contact.Mother = contact.Spouse = null;
                        contact.Culture = owner.Culture; contact.SetNewOccupation(Occupation.Special);
                        contact.SetName(new TextObject("{=!}" + name), new TextObject("{=!}" + name));
                        contact.EncyclopediaText = new TextObject("{=!}" + name + " is " + owner.Name + "'s " + story.Definition.SupportingRole + ".");
                        if (story.Definition.Phases.Any(p => p.Objectives.Any(o => o.RequiredOutcome == "rescued"))) contact.ChangeState(Hero.CharacterStates.Traveling);
                        else { contact.ChangeState(Hero.CharacterStates.Active); EnterSettlementAction.ApplyForCharacterOnly(contact, home); }
                    }
                }
                    if (story.Definition.Phases.Any(p => p.Objectives.Any(o => o.RequiredOutcome == "recovered")) && record.Value<string>("itemId") == null)
                    {
                        var item = MBObjectManager.Instance.GetObjectTypeList<ItemObject>()
                            .Where(i => i.ItemType == ItemObject.ItemTypeEnum.OneHandedWeapon && i.Value > 0 && i.Value < 3000)
                            .OrderBy(i => NpcLivesEngine.Draw(story.Id + "|" + i.StringId)).FirstOrDefault();
                        if (item == null) continue;
                        record["itemId"] = item.StringId; record["itemName"] = item.Name.ToString();
                    }
                if (record.Value<string>("heroId") == null) continue;
                var p = ReignServerClient.CaptureNpcLivesContext(Generation);
                p["id"] = story.Id; p["revision"] = story.Revision; p["heroId"] = story.HeroId;
                p["kind"] = "story_binding"; p["bindings"] = record.DeepClone();
                string receipt = story.Id + ":binding"; p["nativeReceipt"] = receipt;
                if (!_acceptedOperations.Contains(receipt) && !_failedOperations.ContainsKey(receipt)) QueueLifeOperation("native", p, receipt);
                }
                catch (Exception ex)
                {
                    // Preserve any reserved native identity; never create a second
                    // contact after a partial initialization failure.
                    _failedOperations[story.Id + ":binding"] = "Story identity needs review: " + ex.Message;
                }
            }
        }
        private void PrepareStoryNativeBinding(string encounter, Settlement hideout)
        {
            var row = _storyEncounters[encounter];
            var story = State.Stories.Single(s => s.Id == row.Value<string>("storyId"));
            if (row.Value<string>("requiredOutcome") != "rescued" && row.Value<string>("requiredOutcome") != "recovered") return;
            if (!_acceptedOperations.Contains(story.Id + ":binding") || !_storyBindings.TryGetValue(story.Id, out var bindings))
                throw new InvalidOperationException("The story's supporting identities are still being established.");
            if (row.Value<string>("requiredOutcome") == "rescued")
            {
                Hero contact = ReignObjectResolver.FindHero(bindings.Value<string>("heroId"));
                var holder = hideout.Parties.FirstOrDefault(p => p.IsBandit)?.Party;
                if (contact == null || !contact.IsAlive || holder == null) throw new InvalidOperationException("The bound rescue target or hideout party is unavailable.");
                if (contact.IsPrisoner && contact.PartyBelongedToAsPrisoner != holder)
                    throw new InvalidOperationException("The friend is held elsewhere; this hideout cannot complete the rescue.");
                if (!contact.IsPrisoner) TakePrisonerAction.Apply(holder, contact);
                if (contact.PartyBelongedToAsPrisoner != holder) throw new InvalidOperationException("The native rescue target was not placed with the captors.");
                row["rescueHeroId"] = contact.StringId;
            }
            else row["itemId"] = bindings["itemId"]?.DeepClone() ?? throw new InvalidOperationException("The stolen item is unbound.");
        }
        private JObject CompleteStoryNativeBinding(JObject row)
        {
            var proof = new JObject();
            if (row.Value<string>("requiredOutcome") == "rescued")
            {
                Hero friend = ReignObjectResolver.FindHero(row.Value<string>("rescueHeroId"));
                if (friend == null || !friend.IsAlive) throw new InvalidOperationException("The bound rescue target is no longer alive.");
                if (friend.IsPrisoner) EndCaptivityAction.ApplyByReleasedByChoice(friend, Hero.MainHero);
                if (friend.IsPrisoner) throw new InvalidOperationException("The native captive has not been released.");
                proof["heroId"] = friend.StringId; proof["aliveAndFree"] = true;
            }
            else if (row.Value<string>("requiredOutcome") == "recovered")
            {
                var item = MBObjectManager.Instance.GetObject<ItemObject>(row.Value<string>("itemId"));
                if (item == null) throw new InvalidOperationException("The bound heirloom item is unavailable.");
                int before = MobileParty.MainParty.ItemRoster.GetItemNumber(item);
                MobileParty.MainParty.ItemRoster.AddToCounts(item, 1);
                int after = MobileParty.MainParty.ItemRoster.GetItemNumber(item);
                if (after != before + 1) throw new InvalidOperationException("The recovered item was not placed in party inventory.");
                proof["itemId"] = item.StringId; proof["beforeCount"] = before; proof["afterCount"] = after;
            }
            return proof;
        }
    }
}

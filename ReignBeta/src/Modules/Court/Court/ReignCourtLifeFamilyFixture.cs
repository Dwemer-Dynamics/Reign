using System;
using System.Linq;
using Helpers;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;

namespace ReignBeta.Court
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        private JObject CourtLifeFamilyFixtureInventory() => new JObject
        {
            ["player"] = Hero.MainHero == null ? null : FamilyVisitProfile(Hero.MainHero),
            ["family"] = new JArray(RulerFamily().Select(FamilyVisitProfile)),
            ["attention"] = FamilyMirror()
        };

        private JObject PrepareCourtLifeFamilyFixture(string runId, JObject options, string save, string instance)
        {
            const string phase = "court_life_family_fixture";
            if (options.Value<string>("confirmation") != RulerDocketFixtureConfirmation || !HasRoyalCommandAccess)
                return RulerDocketTestFailure(runId, phase, "Exact armed disposable fixture confirmation and capital Court are required.", save, save);
            Hero father = Hero.MainHero;
            bool ageRulerTo34 = options.Value<bool?>("courtLifeFixtureAgeRulerTo34") == true;
            if (father == null || father.IsFemale || !father.IsAlive || father.IsChild || father.Age < 18
                || (father.Age < 32 && !ageRulerTo34) || (ageRulerTo34 && father.Age > 34.02f) || CurrentCapital == null)
            {
                JObject failure = RulerDocketTestFailure(runId, phase, "This bounded fixture requires a living male ruler aged at least 32, or explicitly authorized age-to-34 setup for an adult ruler no older than 34. Adult parentage for children aged four and fourteen is required. Inspect familyInventory before choosing another fixture.", save, save);
                failure["familyInventory"] = CourtLifeFamilyFixtureInventory();
                return failure;
            }
            JObject marker = FindRulerDocketMarker(runId);
            if (marker != null && marker.Value<string>("familyFixturePlayerId") != father.StringId)
                return RulerDocketTestFailure(runId, phase, "Fixture identity already belongs to another purpose or player.", save, save);
            if (marker != null && (marker.Value<bool?>("familyFixtureAgeRulerTo34") == true) != ageRulerTo34)
                return RulerDocketTestFailure(runId, phase, "Retry must retain the original ruler-age setup option.", save, save);
            if (marker == null)
            {
                if (RulerFamily().Count != 0)
                    return RulerDocketTestFailure(runId, phase, "Family setup requires an empty household; existing spouses and children are never replaced.", save, save);
                marker = new JObject { ["runId"] = runId, ["familyFixturePlayerId"] = father.StringId,
                    ["preparedDay"] = CurrentCourtLifeDay(), ["preparedGameInstance"] = instance,
                    ["familyFixtureAgeRulerTo34"] = ageRulerTo34, ["familyFixturePlayerAgeBefore"] = father.Age,
                    ["familyFixturePlayerAgeAdjusted"] = false,
                    ["familyFixtureChildren"] = new JArray(), ["familyFixtureComplete"] = false };
                StoreRulerDocketMarker(marker);
            }
            try
            {
                if (ageRulerTo34 && marker.Value<bool?>("familyFixturePlayerAgeAdjusted") != true)
                {
                    father.SetBirthDay(CampaignTime.YearsFromNow(-34));
                    marker["familyFixturePlayerAgeAdjusted"] = true;
                    marker["familyFixturePlayerAgeAfter"] = father.Age;
                    StoreRulerDocketMarker(marker);
                }
                if (father.Age < 32)
                    throw new InvalidOperationException("Adult historical parentage failed after ruler-age preparation.");
                Hero wife = FindHero(marker.Value<string>("familyFixtureWifeId"));
                if (wife == null)
                {
                    if (!string.IsNullOrEmpty(marker.Value<string>("familyFixtureWifeId")))
                        throw new InvalidOperationException("Recorded wife is missing; restore the preparation checkpoint.");
                    // Native LordTemplates may contain only male mercenary leaders. CreateSpecialHero
                    // clones its CharacterObject, so an adult noble can safely supply a female template.
                    CharacterObject template = father.Culture.LordTemplates.Concat(Hero.AllAliveHeroes
                        .Where(h => h.IsFemale && !h.IsChild && h.Age >= 18 && h.Occupation == Occupation.Lord)
                        .Select(h => h.CharacterObject)).Where(c => c.IsFemale
                        && c.Occupation == Occupation.Lord && c.Culture == father.Culture
                        && c.Race == father.CharacterObject.Race).OrderBy(c => c.StringId, StringComparer.Ordinal).FirstOrDefault();
                    Clan origin = Clan.All.Where(c => c != Clan.PlayerClan && !c.IsEliminated && c.Kingdom != null
                        && !father.Clan.Kingdom.IsAtWarWith(c.Kingdom) && c.Culture == father.Culture)
                        .OrderBy(c => c.Kingdom == father.Clan.Kingdom ? 0 : 1)
                        .ThenBy(c => c.StringId, StringComparer.Ordinal).FirstOrDefault();
                    marker["familyFixtureTemplateId"] = template?.StringId ?? "";
                    marker["familyFixtureOriginClanId"] = origin?.StringId ?? "";
                    marker["familyFixturePlayerCultureId"] = father.Culture.StringId;
                    StoreRulerDocketMarker(marker);
                    if (template == null)
                        throw new InvalidOperationException("No matching female noble template or adult noble character in the ruler culture.");
                    if (origin == null)
                        throw new InvalidOperationException("No same-culture noble origin clan in the ruler kingdom or a kingdom at peace with it.");
                    wife = HeroCreator.CreateSpecialHero(template, CurrentCapital, origin, null, 34);
                    if (wife == null) throw new InvalidOperationException("Native wife creation returned null.");
                    marker["familyFixtureWifeId"] = wife.StringId;
                    StoreRulerDocketMarker(marker);
                    if (ReferenceEquals(wife.CharacterObject, template))
                        throw new InvalidOperationException("Native wife creation did not clone its source character.");
                }
                if (father.Spouse == null && wife.Spouse == null)
                {
                    wife.ChangeState(Hero.CharacterStates.Active);
                    if (!global::TaleWorlds.CampaignSystem.Campaign.Current.Models.MarriageModel.IsCoupleSuitableForMarriage(father, wife))
                        throw new InvalidOperationException("Native marriage model rejects this couple; restore the preparation checkpoint.");
                    MarriageAction.Apply(father, wife, true);
                }
                if (father.Spouse != wife || wife.Spouse != father || wife.Clan != father.Clan || !wife.IsAlive || wife.Age < 32)
                    throw new InvalidOperationException("Native marriage postconditions failed.");
                if (wife.CurrentSettlement != CurrentCapital) EnterSettlementAction.ApplyForCharacterOnly(wife, CurrentCapital);
                var children = (JArray)marker["familyFixtureChildren"];
                foreach (int age in new[] { 4, 14 })
                {
                    JObject row = children.OfType<JObject>().FirstOrDefault(x => x.Value<int>("age") == age);
                    Hero child = row == null ? null : FindHero(row.Value<string>("heroId"));
                    if (row != null && child == null) throw new InvalidOperationException("Recorded child is missing; no replacement was created.");
                    if (child == null)
                    {
                        var template = global::TaleWorlds.CampaignSystem.Campaign.Current.Models.HeroCreationModel.GetCharacterTemplateForOffspring(wife, father, age == 4);
                        if (template == null) throw new InvalidOperationException("Native offspring template is unavailable.");
                        child = HeroCreator.CreateChild(template, CurrentCapital, father.Clan, age);
                        if (child == null) throw new InvalidOperationException("Native child creation returned null.");
                        row = new JObject { ["age"] = age, ["heroId"] = child.StringId, ["initialized"] = false };
                        children.Add(row);
                        StoreRulerDocketMarker(marker);
                    }
                    if (row.Value<bool?>("initialized") != true)
                    {
                        if ((child.Father != null && child.Father != father) || (child.Mother != null && child.Mother != wife))
                            throw new InvalidOperationException("Created child has conflicting parentage.");
                        if (child.Father == null) child.Father = father;
                        if (child.Mother == null) child.Mother = wife;
                        child.SetBirthDay(CampaignTime.YearsFromNow(-age));
                        child.Culture = age == 4 ? wife.Culture : father.Culture;
                        var model = global::TaleWorlds.CampaignSystem.Campaign.Current.Models.HeroCreationModel;
                        child.StaticBodyProperties = model.GetStaticBodyProperties(child, true);
                        var names = model.GenerateFirstAndFullName(child);
                        child.SetName(names.Item2, names.Item1);
                        child.ClearTraits();
                        EquipmentHelper.AssignHeroEquipmentFromEquipment(child, model.GetCivilianEquipment(child));
                        EquipmentHelper.AssignHeroEquipmentFromEquipment(child, model.GetBattleEquipment(child));
                        // Native child state is retained; only physical household residence is established.
                        EnterSettlementAction.ApplyForCharacterOnly(child, CurrentCapital);
                        row["initialized"] = true;
                        StoreRulerDocketMarker(marker);
                    }
                    if (!child.IsAlive || !child.IsChild || !child.IsNotSpawned || child.Age >= 18
                        || Math.Abs(child.Age - age) > 0.02 || child.Clan != father.Clan
                        || child.Father != father || child.Mother != wife || child.PartyBelongedTo != null
                        || child.Spouse != null || child.CurrentSettlement != CurrentCapital
                        || father.Children.Count(h => h == child) != 1 || wife.Children.Count(h => h == child) != 1)
                        throw new InvalidOperationException("Native child postconditions failed.");
                }
                marker["familyFixtureComplete"] = true;
                StoreRulerDocketMarker(marker);
                _lastFamilyVisitSyncDay = -1;
                ProcessFamilyVisits(CurrentCourtLifeDay());
                StateChanged?.Invoke();
                return new JObject { ["ok"] = true, ["schema"] = "reign-court-life-family-fixture-v1",
                    ["phase"] = phase, ["runId"] = runId, ["saveName"] = save, ["fixture"] = marker,
                    ["familyInventory"] = CourtLifeFamilyFixtureInventory(), ["savedByHarness"] = false,
                    ["nativeTimeAdvancedByHarness"] = false, ["requiresQuiescentCheckpoint"] = true };
            }
            catch (Exception ex)
            {
                JObject failure = RulerDocketTestFailure(runId, phase, ex.Message, save, save);
                failure["fixture"] = marker; failure["familyInventory"] = CourtLifeFamilyFixtureInventory();
                failure["requiresInspectionBeforeRetry"] = true;
                return failure;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace ReignBeta.Economy
{
    // Reachable only through the exact enrolled, paused disposable-save fixture dispatcher.
    internal static class ReignRaidFoodTestHost
    {
        [ThreadStatic] private static RaidEventComponent _raid;
        [ThreadStatic] private static bool _catchProbe;
        [ThreadStatic] private static Village _creatingVillage;
        [ThreadStatic] private static MobileParty _creatingAttacker;
        [ThreadStatic] private static int _defenderHits,_damageHits;
        [ThreadStatic] private static int _roundHits;
        [ThreadStatic] private static Village _fishingVillage;
        [ThreadStatic] private static float _roundInput;
        private static bool NoExtraRaidDamage(MapEventSide __0, ref ExplainedNumber __result)
        { if (_raid == null || !ReferenceEquals(__0,_raid.MapEvent.AttackerSide)) return true;_damageHits++; __result=new ExplainedNumber(0); return false; }
        private static void CaptureCreatedRaid(RaidEventComponent __instance)
        {
            if(_creatingVillage!=null && __instance.MapEvent.MapEventSettlement?.Village==_creatingVillage
                && __instance.MapEvent.AttackerSide.LeaderParty?.MobileParty==_creatingAttacker)_raid=__instance;
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int ScopedDefenderCount(MapEventSide side)
        {if(_raid!=null&&ReferenceEquals(side,_raid.MapEvent.DefenderSide)){_defenderHits++;return 0;}return side.TroopCount;}
        private static IEnumerable<CodeInstruction> UnopposedUpdate(IEnumerable<CodeInstruction> instructions)
        {
            var code=instructions.ToList();var getter=AccessTools.PropertyGetter(typeof(MapEventSide),"TroopCount");
            var defender=AccessTools.PropertyGetter(typeof(RaidEventComponent),"DefenderSide");
            var sites=code.Where((x,i)=>i>0&&x.Calls(getter)&&code[i-1].Calls(defender)).ToArray();
            if(sites.Length!=1)throw new InvalidOperationException("Native raid defender branch changed; expected one exact troop count.");
            sites[0].opcode=OpCodes.Call;sites[0].operand=AccessTools.Method(typeof(ReignRaidFoodTestHost),nameof(ScopedDefenderCount));return code;
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int PlannedOneFish(float input)
        {if(!_catchProbe)return MBRandom.RoundRandomized(input);_roundHits++;_roundInput=input;return 1;}
        private static IEnumerable<CodeInstruction> DeterministicCatchRound(IEnumerable<CodeInstruction> instructions)
        {
            var code=instructions.ToList();var round=AccessTools.Method(typeof(MBRandom),"RoundRandomized",new[]{typeof(float)});
            var sites=code.Where(x=>x.Calls(round)).ToArray();
            if(sites.Length!=1)throw new InvalidOperationException("Native fishing rounding shape changed.");
            sites[0].opcode=OpCodes.Call;sites[0].operand=AccessTools.Method(typeof(ReignRaidFoodTestHost),nameof(PlannedOneFish));return code;
        }
private static object NativeBehavior(Type type) => typeof(TaleWorlds.CampaignSystem.Campaign).GetMethod("GetCampaignBehavior")
            .MakeGenericMethod(type).Invoke(TaleWorlds.CampaignSystem.Campaign.Current,null);
        internal static JObject Run(JObject command)
        {
            if(ReignWarEconomyTransactions.State.RaidFood.FixtureTainted)throw new InvalidOperationException("Food fixture is tainted; restore and verify clean Current first.");
            var settlement=Settlement.Find(command.Value<string>("settlementId")); var village=settlement?.Village;
            if (village?.VillageState!=Village.VillageStates.Normal || village.Bound?.Town==null
                || settlement.OwnerClan==Clan.PlayerClan || settlement.Party.MapEvent!=null || village.Bound.IsUnderSiege
                || village.Hearth<10 || village.Hearth>2000)
                throw new InvalidOperationException("Food fixtures require a safe functioning NPC village, 10-2000 hearths, with a bound town/castle.");
            JObject result=null;
            string randomBefore=ReignVillageProductionCalculatorModel.RandomFingerprint();
            try
            {
                ReignVillageProductionCalculatorModel.ObserveWithoutRandomAdvance(()=> {
                    result=command.Value<string>("profile")=="raid_partial_setup"?Partial(command,village):Probe(command,village);return 0;
                });
                result["rngRestored"]=randomBefore==ReignVillageProductionCalculatorModel.RandomFingerprint();
                result["ok"]=result.Value<bool>("ok")&&result.Value<bool>("rngRestored");
                if(!result.Value<bool>("ok"))ReignWarEconomyTransactions.State.RaidFood.FixtureTainted=true;
            }
            catch {ReignWarEconomyTransactions.State.RaidFood.FixtureTainted=true;throw;}
            return result;
        }
        private static JObject Partial(JObject command,Village village)
        {
            int percent=command.Value<int?>("damagePercent")??10;
            if (!new[]{0,4,6,10,24,25,40}.Contains(percent)) throw new ArgumentOutOfRangeException("damagePercent");
            var party=MobileParty.All.SingleOrDefault(x=>x.StringId==command.Value<string>("partyId"));
            var settlement=village.Settlement;
            if (settlement.SettlementHitPoints<.99f || party?.IsActive!=true || !party.IsLordParty || party==MobileParty.MainParty
                || party.ActualClan==Clan.PlayerClan || party.MapEvent!=null || party.Army!=null || party.IsCurrentlyAtSea
                || party.CurrentSettlement!=null
                || party.SiegeEvent!=null || party.IsCurrentlyUsedByAQuest || party.MemberRoster.TotalManCount>1000
                || party.MapFaction==null || !party.MapFaction.IsAtWarWith(settlement.MapFaction)
                || settlement.ItemRoster.Count>2048 || settlement.ItemRoster.Sum(x=>x.Amount)>10000)
                throw new InvalidOperationException("Partial raid setup requires undamaged normalized village HP and a free existing enemy NPC field party; bounded inventories and force size apply.");
            var before=ReignWarEconomyTestHost.VillageObservation(village);
            long unsupportedBefore=ReignWarEconomyTransactions.State.RaidFood.Unsupported;
            double hp=settlement.SettlementHitPoints,day=CampaignTime.Now.ToDays;
            double hpBeforeUpdate=hp,hpAfterUpdate=hp;
            int members=party.MemberRoster.TotalManCount; float militia=settlement.Militia;
            int goldBefore=party.LeaderHero?.Gold??0,marketUnitsBefore=settlement.ItemRoster.Sum(x=>x.Amount),cargoBefore=party.ItemRoster.Sum(x=>x.Amount);
            var scope=new Harmony("com.bannerlordreign.reignbeta.raidfood.fixture");
            RaidEventComponent raid=null;
            double bufferAfter=double.NaN;bool nativeFinish=false,finalized=false;
            try
            {
                if(_raid!=null||_creatingVillage!=null)throw new InvalidOperationException("A raid fixture is already active.");
                _defenderHits=0;_damageHits=0;_creatingVillage=village;_creatingAttacker=party;
                scope.Patch(AccessTools.Method(typeof(RaidEventComponent),"OnInitialize"),prefix:new HarmonyMethod(typeof(ReignRaidFoodTestHost),nameof(CaptureCreatedRaid)));
                raid=RaidEventComponent.CreateRaidEvent(party.Party,settlement.Party); _raid=raid;
                _creatingVillage=null;_creatingAttacker=null;
                hpBeforeUpdate=settlement.SettlementHitPoints;
                var update=AccessTools.Method(typeof(RaidEventComponent),"Update",new[]{typeof(bool).MakeByRefType()});
                scope.Patch(update,transpiler:new HarmonyMethod(typeof(ReignRaidFoodTestHost),nameof(UnopposedUpdate)));
                scope.Patch(AccessTools.Method(TaleWorlds.CampaignSystem.Campaign.Current.Models.RaidModel.GetType(),"CalculateHitDamage"),
                    prefix:new HarmonyMethod(typeof(ReignRaidFoodTestHost),nameof(NoExtraRaidDamage)));
                AccessTools.Field(typeof(RaidEventComponent),"_nextSettlementDamage").SetValue(raid,percent/100f);
                var arguments=new object[]{false};update.Invoke(raid,arguments);nativeFinish=(bool)arguments[0];
                hpAfterUpdate=settlement.SettlementHitPoints;
                bufferAfter=(float)AccessTools.Field(typeof(RaidEventComponent),"_nextSettlementDamage").GetValue(raid);
            }
            finally
            {
                var created=raid??_raid;
                _creatingVillage=null;_creatingAttacker=null;_raid=null;
                try {created?.MapEvent.FinalizeEvent();finalized=created?.MapEvent.IsFinalized==true;}
                finally {scope.UnpatchAll(scope.Id);}
                raid=created;
            }
            double applied=hpBeforeUpdate-hpAfterUpdate;
            double expected=percent<=5?0:percent/100d;
            double expectedCapacity=ReignRaidFoodPolicy.Damage(before["raidFood"].Value<double>("capacity"),applied,
                ReignWarEconomyPolicy.Options.Number("economyRaidShutdownPercent")/100);
            bool capacityCharged=Math.Abs(ReignRaidFoodDisruption.Capacity(village)-expectedCapacity)<1e-5;
            bool receiptClosed=ReignWarEconomyTransactions.State.RaidFood.Villages.TryGetValue(village.StringId,out var villageRecord)
                && villageRecord.Active==null && villageRecord.LastClosedNativeKey==settlement.StringId+":"+raid.MapEvent.StringId+":"+raid.MapEvent.BattleStartTime.ToHours.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            bool hooks=ReignRaidFoodDisruption.Diagnostics().Value<bool>("hooksHealthy");
            bool nativePath=_defenderHits==1&&_damageHits==1&&!nativeFinish&&Math.Abs(bufferAfter-(percent<=5?percent/100d:0))<1e-5;
            bool cleaned=finalized&&party.MapEvent==null&&settlement.Party.MapEvent==null&&village.VillageState==Village.VillageStates.Normal;
            return new JObject { ["ok"]=Math.Abs(applied-expected)<1e-5 && day==CampaignTime.Now.ToDays
                    && members==party.MemberRoster.TotalManCount && militia==settlement.Militia && capacityCharged && receiptClosed && hooks
                    && ReignWarEconomyTransactions.State.RaidFood.Unsupported==unsupportedBefore && nativePath && cleaned,
                ["profile"]="raid_partial_setup",["villageId"]=settlement.StringId,["raiderPartyId"]=party.StringId,
                ["damagePercent"]=percent,["actualAppliedDamage"]=applied,["before"]=before,
                ["hitPointsBeforeUpdate"]=hpBeforeUpdate,["hitPointsAfterUpdate"]=hpAfterUpdate,["hitPointsAfterFinalize"]=settlement.SettlementHitPoints,
                ["expectedFoodCapacity"]=expectedCapacity,["capacityChargedOnce"]=capacityCharged,["receiptClosed"]=receiptClosed,["hooksHealthy"]=hooks,
                ["after"]=ReignWarEconomyTestHost.VillageObservation(village),["nativeRaidUpdate"]=nativePath,
                ["nativeDefenderBranchHits"]=_defenderHits,["nativeDamageModelHits"]=_damageHits,["pendingBufferAfterUpdate"]=bufferAfter,["nativeFinish"]=nativeFinish,
                ["nativeEventCleaned"]=cleaned,["nativeHearthDelta"]=village.Hearth-before.Value<double>("hearth"),
                ["nativeLeaderGoldDelta"]=(party.LeaderHero?.Gold??0)-goldBefore,["nativeMarketUnitDelta"]=settlement.ItemRoster.Sum(x=>x.Amount)-marketUnitsBefore,["nativeRaiderCargoDelta"]=party.ItemRoster.Sum(x=>x.Amount)-cargoBefore,
                ["syntheticUnopposedRaid"]=true,["timeAdvanced"]=day!=CampaignTime.Now.ToDays,
                ["nativeMapEventLeftActive"]=false,["damageScenarioRetained"]=true,["fullCurrentRestoreAfterBranchRequired"]=true,
                ["nativeCombatAcceptance"]=false,["providerCallsInThisAdapter"]=0,["providerFreeProof"]="independent provider guard required" };
        }
        private static JObject Probe(JObject command,Village village)
        {
            if (!ReignWarEconomyPolicy.Options.Enabled("economyRaidFoodEnabled")) throw new InvalidOperationException("Enable Raid food disruption before its controlled probe.");
            var economy=ReignWarEconomyTransactions.State;
            string saved=JsonConvert.SerializeObject(economy.RaidFood);
            string savedRecovery=JsonConvert.SerializeObject(economy.Recovery);
            var recoveryValues=new Dictionary<string,float>(economy.Recovery,StringComparer.Ordinal);
            var before=ReignWarEconomyTestHost.VillageObservation(village);
            var rows=new JArray(); JObject fishing=null;
            try
            {
                foreach(var pair in new[]{new[]{1d,1d},new[]{.5,.5},new[]{.4,.6},new[]{1d,0d}})
                {
                    economy.Recovery[village.StringId]=(float)pair[0];
                    economy.RaidFood.Villages[village.StringId]=new RaidFoodVillage {Capacity=pair[1],LastHours=CampaignTime.Now.ToHours,Normal=true};
                    double actual=ReignVillageProductionCalculatorModel.TotalProduction(village,false,true);
                    double potential=ReignVillageProductionCalculatorModel.TotalProduction(village,true,true);
                    double factor=Math.Min(pair[0],pair[1]);
                    rows.Add(new JObject { ["legacy"]=pair[0],["capacity"]=pair[1],["actualFoodGoods"]=actual,
                        ["potentialFoodGoods"]=potential,["expectedFactor"]=factor,
                        ["foodRecipeAvailable"]=potential>0,["passed"]=potential<=0? (JToken)JValue.CreateNull():new JValue(Math.Abs(actual-potential*factor)<.001),
                        ["boundFoodModel"]=TaleWorlds.CampaignSystem.Campaign.Current.Models.SettlementFoodModel.CalculateTownFoodStocksChange(village.Bound.Town,true,true).ResultNumber,
                        ["supplementaryFood"]=TaleWorlds.CampaignSystem.Campaign.Current.Models.VillageProductionCalculatorModel.CalculateDailyFoodProductionAmount(village) });
                }
                economy.Recovery[village.StringId]=1;
                economy.RaidFood.Villages[village.StringId]=new RaidFoodVillage{Capacity=1,LastHours=CampaignTime.Now.ToHours,Normal=true};
                fishing=Fishing(village);
            }
            finally
            {
                economy.RaidFood=JsonConvert.DeserializeObject<RaidFoodState>(saved);
                economy.Recovery=new Dictionary<string,float>(JsonConvert.DeserializeObject<Dictionary<string,float>>(savedRecovery),StringComparer.Ordinal);
            }
            var after=ReignWarEconomyTestHost.VillageObservation(village);
            bool restored=JToken.DeepEquals(before,after)&&RestoredJson(saved,economy.RaidFood)
                && RestoredJson(savedRecovery,economy.Recovery)&&RecoveryValuesMatch(recoveryValues,economy.Recovery);
            double healthy=rows[0].Value<double>("boundFoodModel"),zeroFood=rows[3].Value<double>("boundFoodModel");
            bool boundSourceObserved=healthy-zeroFood>.0001;
            foreach(var row in rows.OfType<JObject>())
            {
                double factor=row.Value<double>("expectedFactor");
                row["boundFoodOnce"]=Math.Abs(row.Value<double>("boundFoodModel")-(zeroFood+(healthy-zeroFood)*factor))<.01;
                row["supplementaryFoodOnce"]=Math.Abs(row.Value<double>("supplementaryFood")-rows[0].Value<double>("supplementaryFood")*factor)<.001;
            }
            bool hooks=ReignRaidFoodDisruption.Diagnostics().Value<bool>("hooksHealthy")
                && ReignEconomyPatches.BuildDiagnostics().Value<bool>("allModelsHealthy");
            return new JObject { ["ok"]=restored && rows.All(x=>x.Value<bool?>("passed")!=false)
                    && boundSourceObserved && rows.All(x=>x.Value<bool>("boundFoodOnce")&&x.Value<bool>("supplementaryFoodOnce")) && hooks
                    && (fishing.Value<bool?>("available")!=true || fishing.Value<bool>("ok")),
                ["profile"]="raid_food_probe",["villageId"]=village.Settlement.StringId,["cases"]=rows,["fishing"]=fishing,
                ["fishingExercised"]=fishing.Value<bool?>("available")==true,["foodRecipesExercised"]=rows[0].Value<double>("potentialFoodGoods")>0,
                ["boundFoodSourceObserved"]=boundSourceObserved,["hooksAndModelsHealthy"]=hooks,
                ["resourcesRestored"]=restored,["before"]=before,["after"]=after,
                ["nativeGameplayAcceptanceComplete"]=false,["fullCurrentRestoreAfterBranchRequired"]=true,["providerCallsInThisAdapter"]=0,["providerFreeProof"]="independent provider guard required" };
        }
        private static bool RestoredJson(string saved,object restored) =>
            JToken.DeepEquals(JToken.Parse(saved),JToken.Parse(JsonConvert.SerializeObject(restored)));
        private static bool RecoveryValuesMatch(Dictionary<string,float> before,Dictionary<string,float> after)
        {
            if(before==null || after==null || before.Count!=after.Count)return false;
            var ordinal=new Dictionary<string,float>(after,StringComparer.Ordinal);
            return before.All(pair=>ordinal.TryGetValue(pair.Key,out var value)&&pair.Value.Equals(value));
        }
        private static Type FishingComponentType(Type behaviorType)
        {
            var matches=behaviorType.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                .Where(m=>m.Name=="CatchFish").ToArray();
            if(matches.Length!=1 || matches[0].GetParameters().Length!=1 || matches[0].ReturnType!=typeof(void))
                throw new InvalidOperationException("Native fishing catch signature changed.");
            var catchMethod=matches[0];
            var component=catchMethod.GetParameters()[0].ParameterType;
            var factory=AccessTools.Method(component,"CreateFishingParty",new[]{typeof(string),typeof(Village)});
            if(component.FullName!="NavalDLC.FishingPartyComponent" || component.Assembly!=behaviorType.Assembly
                || !typeof(VillagerPartyComponent).IsAssignableFrom(component)
                || factory==null || !factory.IsStatic || factory.DeclaringType!=component || factory.ReturnType!=typeof(MobileParty))
                throw new InvalidOperationException("Native fishing component/factory identity changed.");
            return component;
        }
        private static JObject Fishing(Village village)
        {
            var type=ReignEconomyPatches.NativeFishingType;
            if(type==null || village.VillageType!=DefaultVillageTypes.Fisherman || !village.Settlement.HasPort || village.Settlement.Culture?.FishingPartyTemplate==null)
                return new JObject{["available"]=false,["reason"]="Choose a safe native fisherman village with a port; not a fishing acceptance pass."};
            var componentType=FishingComponentType(type);
            var native=NativeBehavior(type)??throw new InvalidOperationException("Native fishing behavior missing.");
            var catchMethod=AccessTools.Method(type,"CatchFish",new[]{componentType});
            var hourly=AccessTools.Method(type,"OnHourlyTickParty",new[]{typeof(MobileParty)});
            var dropOff=AccessTools.Method(type,"OnDropOff",new[]{componentType});
            var roaming=AccessTools.Method(type,"StartRoaming",new[]{componentType});
            var factory=AccessTools.Method(componentType,"CreateFishingParty",new[]{typeof(string),typeof(Village)});
            if(new[]{catchMethod,hourly,dropOff,roaming}.Any(m=>m==null || m.IsStatic || m.ReturnType!=typeof(void)))
                throw new InvalidOperationException("Native fishing fixture callback signature changed.");
            var scope=new Harmony("com.bannerlordreign.reignbeta.raidfood.catchfixture");
            string fixtureId="reign_food_probe_"+Guid.NewGuid().ToString("N");
            var partiesBefore=new HashSet<MobileParty>(MobileParty.All,ReignWarEconomyReferenceComparer<MobileParty>.Instance);
            MobileParty party=null;
            JObject report=null;
            var warehouse=village.Settlement.ItemRoster.ToList();
            var town=village.Bound.Town;
            float prosperity=town.Prosperity,loyalty=town.Loyalty,hearth=village.Hearth;
            var state=ReignWarEconomyTransactions.State.RaidFood;
            var record=state.Villages[village.StringId];
            try
            {
                party=(MobileParty)factory.Invoke(null,new object[]{fixtureId,village});
                if(party==null || party.PartyComponent.GetType()!=componentType || partiesBefore.Contains(party))
                    throw new InvalidOperationException("Native fishing fixture factory identity changed.");
                var fish=TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<ItemObject>("fish") ?? throw new InvalidOperationException("Native fish item missing.");
                if(party.InventoryCapacity-party.TotalWeightCarried<3*(fish.Weight+.1f))
                    throw new InvalidOperationException("Fixture fishing hold has insufficient real cargo capacity.");
                _roundHits=0;
                scope.Patch(catchMethod,transpiler:new HarmonyMethod(typeof(ReignRaidFoodTestHost),nameof(DeterministicCatchRound)));
                Action catchOnce=()=> {
                    _catchProbe=true;
                    try {catchMethod.Invoke(native,new[]{party.PartyComponent});}
                    finally {_catchProbe=false;}
                };
                record.Capacity=.5;record.CatchCarry=0;
                long plannedBefore=state.CatchPlanned;
                int before=party.ItemRoster.GetItemNumber(fish);
                catchOnce();catchOnce();
                int half=party.ItemRoster.GetItemNumber(fish)-before;
                record.Capacity=0;catchOnce();
                int zero=party.ItemRoster.GetItemNumber(fish)-before-half;
                long unloadBefore=state.Unloads;
                int partyFish=party.ItemRoster.GetItemNumber(fish),destinationFish=village.Settlement.ItemRoster.GetItemNumber(fish);
                int cargo=party.ItemRoster.Sum(x=>x.Amount),destinationUnits=village.Settlement.ItemRoster.Sum(x=>x.Amount);
                // Match the verified native arrival branch's hold -> unload -> productive departure.
                // The factory randomizes a one-unit spawn radius. Dock only this temporary actor.
                party.Position=village.Settlement.PortPosition;
                party.SetMoveModeHold();
                if(party.Position.DistanceSquared(village.Settlement.PortPosition)>=.01f)
                    throw new InvalidOperationException("Temporary fishing fixture failed to reach its exact home port.");
                dropOff.Invoke(native,new[]{party.PartyComponent});
                long unloadAfter=state.Unloads;
                bool cargoConserved=party.ItemRoster.Count==0
                    && village.Settlement.ItemRoster.GetItemNumber(fish)-destinationFish==partyFish
                    && village.Settlement.ItemRoster.Sum(x=>x.Amount)-destinationUnits==cargo;
                float prosperityAfter=town.Prosperity,loyaltyAfter=town.Loyalty;
                string key=party.StringId+":"+party.Id;
                bool noHoldInsideUnload=state.Boats.TryGetValue(key,out var unloaded)&&!unloaded.Hold;
                roaming.Invoke(native,new[]{party.PartyComponent});
                bool held=state.Boats.TryGetValue(key,out var heldBoat)&&heldBoat.Hold;
                hourly.Invoke(native,new object[]{party});
                bool noRepeatUnload=unloadAfter==unloadBefore+1&&state.Unloads==unloadAfter
                    && town.Prosperity==prosperityAfter&&town.Loyalty==loyaltyAfter&&party.ItemRoster.Count==0;
                record.Capacity=.1;
                long resumed=state.ResumedDepartures;
                hourly.Invoke(native,new object[]{party});
                bool resumedOnce=state.ResumedDepartures==resumed+1
                    && state.Boats.TryGetValue(key,out var resumedBoat)&&!resumedBoat.Hold&&!resumedBoat.Unloaded
                    && state.Unloads==unloadAfter&&town.Prosperity==prosperityAfter&&town.Loyalty==loyaltyAfter;
                report=new JObject {["available"]=true,
                    ["ok"]=half==1&&zero==0&&_roundHits==3&&state.CatchPlanned-plannedBefore==3
                        &&held&&noHoldInsideUnload&&cargoConserved&&noRepeatUnload&&resumedOnce&&village.Hearth==hearth,
                    ["halfCapacityFishAfterTwoNativeCatches"]=half,["zeroCapacityNewFish"]=zero,
                    ["nativeRoundingOverride"]=true,["nativeRoundingSiteHits"]=_roundHits,["lastNativeRoundingInput"]=_roundInput,
                    ["nativePlannedFish"]=state.CatchPlanned-plannedBefore,["nativeProbabilisticCatchAcceptance"]=false,
                    ["cargoConserved"]=cargoConserved,["fishDelivered"]=partyFish,["cargoUnitsDelivered"]=cargo,
                    ["nativeProsperityBonus"]=prosperityAfter-prosperity,["nativeLoyaltyBonus"]=loyaltyAfter-loyalty,
                    ["postUnloadHold"]=held,["noHoldInsideUnload"]=noHoldInsideUnload,["unloadedOnce"]=noRepeatUnload,
                    ["resumedOnce"]=resumedOnce,["syntheticFishingParties"]=1,
                    ["crewHearthUnchanged"]=village.Hearth==hearth};
                return report;
            }
            finally
            {
                _catchProbe=false;
                try {scope.UnpatchAll(scope.Id);}
                finally
                {
                    if(party==null)
                    {
                        var owned=MobileParty.All.Where(p=>!partiesBefore.Contains(p)&&p.StringId==fixtureId).ToList();
                        if(owned.Count==1)party=owned[0];
                    }
                    try {if(party?.IsActive==true)TaleWorlds.CampaignSystem.Actions.DestroyPartyAction.Apply(null,party);}
                    finally
                    {
                        village.Settlement.ItemRoster.Clear();village.Settlement.ItemRoster.Add(warehouse);
                        town.Prosperity=prosperity;town.Loyalty=loyalty;
                        bool removed=party==null||!MobileParty.All.Any(p=>ReferenceEquals(p,party));
                        int unexpected=MobileParty.All.Count(p=>!partiesBefore.Contains(p)&&!ReferenceEquals(p,party));
                        if(report!=null)
                        {
                            report["temporaryPartyRemoved"]=removed;report["unexpectedCreatedParties"]=unexpected;
                            report["ok"]=report.Value<bool>("ok")&&removed&&unexpected==0;
                        }
                        else if(!removed||unexpected!=0)ReignWarEconomyTransactions.State.RaidFood.FixtureTainted=true;
                    }
                }
            }
        }
    }
}

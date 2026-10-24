using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.Save;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        private List<GovernmentEdictState> _edicts = new List<GovernmentEdictState>();
        private List<string> _edictChunks = new List<string>();
        private static Settlement FindSettlement(string id) => ReignObjectResolver.FindSettlement(id);
        private static Clan FindClan(string id) => ReignObjectResolver.FindClan(id);
        private void RegisterEdictEvents() => CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, TickEdicts);
        private void SyncEdicts(IDataStore store)
        {
            if (store.IsSaving)
            {
                var retained = _edicts.Where(x => x.Status == "active" || x.BudgetRemaining > 0 || x.GrainStock > 0).ToList();
                retained.AddRange(_edicts.Except(retained).OrderByDescending(x => x.ExpiresDay).Take(256));
                _edicts = retained;
                _edictChunks = ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(_edicts));
            }
            store.SyncData("_reignGovernment_edicts_v1", ref _edictChunks);
            if (!store.IsLoading) return;
            _edicts = _edictChunks == null || _edictChunks.Count == 0 ? new List<GovernmentEdictState>()
                : JsonConvert.DeserializeObject<List<GovernmentEdictState>>(ReignSavePayloadCodec.Decode(_edictChunks)) ?? new List<GovernmentEdictState>();
            if (_edicts.Any(x => !GovernmentEdictRules.ValidState(x)) || _edicts.GroupBy(x => x.Id).Any(x => x.Count() > 1)
                || _edicts.GroupBy(x => x.BusinessId).Any(x => x.Count() > 1)
                || _edicts.Where(x => x.Status == "active").GroupBy(x => x.SettlementId + "|" + x.EdictId).Any(x => x.Count() > 1))
                throw new InvalidOperationException("Government edict save records failed conservation/identity validation; no effects were replayed.");
        }

        public JArray GetEdictProposalChoices(string kingdomId)
        {
            var kingdom = FindKingdom(kingdomId);
            var result = new JArray();
            if (kingdom == null || GetGovernment(kingdom) == null) return result;
            foreach (var settlement in kingdom.Settlements.Where(x => x.IsTown || x.IsCastle).OrderBy(x => x.StringId, StringComparer.Ordinal))
            foreach (var definition in GovernmentEdictRules.All)
            {
                var active = _edicts.FirstOrDefault(x => x.SettlementId == settlement.StringId && x.EdictId == definition.Id && x.Status == "active");
                string proposal = active == null ? "edict:" + definition.Id + ":" + settlement.StringId : "edict_repeal:" + active.Id;
                bool conflict = GovernmentEdictRules.HasConflict(definition.Id, kingdom.ActivePolicies.Select(x => x.StringId));
                bool project = definition.Id != "public_works" || EdictProjectAvailable(settlement.Town);
                result.Add(new JObject { ["proposalId"] = proposal, ["category"] = "edict",
                    ["payload"] = EdictPetitionPayload(definition.Id, settlement, active),
                    ["label"] = (active == null ? "Enact " : "Repeal ") + definition.Name + " — " + settlement.Name,
                    ["description"] = definition.Terms + "\nSettlement: " + settlement.Name + ". Beneficiary and payer: " + settlement.OwnerClan?.Name
                        + ". Regular government session; no immediate execution."
                        + (definition.Id == "public_works" ? " Named construction: " + settlement.Town?.CurrentBuilding?.BuildingType?.Name + ", current level " + (settlement.Town?.CurrentBuilding?.CurrentLevel ?? 0) + "." : "")
                        + (conflict && active == null ? " Conflicting native taxation/privilege must be repealed first." : "")
                        + (!project && active == null ? " A finite construction project must be selected first." : ""),
                    ["available"] = (active != null || !conflict && project)
                        && !_business.Any(x => x.Kind == "edict" && x.TargetId == definition.Id + ":" + settlement.StringId && !GovernmentBusinessRules.IsClosed(x.Status)) });
            }
            return result;
        }
        private static JObject EdictPetitionPayload(string edictId, Settlement settlement, GovernmentEdictState existing)
        {
            var definition = GovernmentEdictRules.Find(edictId);
            return new JObject { ["edictId"] = edictId, ["settlementId"] = settlement.StringId,
                ["beneficiaryClanId"] = settlement.OwnerClan?.StringId ?? "", ["funderHeroId"] = settlement.OwnerClan?.Leader?.StringId ?? "",
                ["repealId"] = existing?.Id ?? "", ["projectId"] = edictId == "public_works" ? settlement.Town?.CurrentBuilding?.BuildingType?.StringId ?? "" : "",
                ["projectLevel"] = edictId == "public_works" ? settlement.Town?.CurrentBuilding?.CurrentLevel ?? 0 : 0, ["budget"] = definition.Budget };
        }

        public JArray GetActiveEdicts(string kingdomId) => new JArray(_edicts.Where(x => x.KingdomId == kingdomId && (x.Status == "active" || GovernmentEdictRules.HasPendingReturn(x)))
            .Select(x => new JObject { ["id"] = x.Id, ["edictId"] = x.EdictId, ["title"] = GovernmentEdictRules.Find(x.EdictId)?.Name,
                ["settlementId"] = x.SettlementId, ["settlement"] = FindSettlement(x.SettlementId)?.Name?.ToString() ?? x.SettlementId,
                ["beneficiaryClanId"] = x.BeneficiaryClanId, ["expiresDay"] = x.ExpiresDay, ["budgetRemaining"] = x.BudgetRemaining,
                ["grainStock"] = x.GrainStock, ["serviceRecruitsIssued"] = x.ServiceRecruitsIssued,
                ["status"] = x.Status, ["pendingReturn"] = GovernmentEdictRules.HasPendingReturn(x),
                ["returnDetail"] = GovernmentEdictRules.HasPendingReturn(x) ? "Awaiting return to " + (FindClan(x.BeneficiaryClanId)?.Name?.ToString() ?? x.BeneficiaryClanId)
                    + ": " + x.BudgetRemaining + " gold and " + x.GrainStock + " grain. Effects have ended." : "",
                ["terms"] = GovernmentEdictRules.Find(x.EdictId)?.Terms }));

        // A petition's saved "active" status records enactment, not a perpetual effect.
        // Project the exact originating law without rewriting its historical decision.
        public string PetitionEdictOutcomeText(GovernmentPetition petition)
        {
            if (petition == null || petition.Kind != "edict" || petition.Status != "active") return string.Empty;
            var state = string.IsNullOrEmpty(petition.BusinessId) ? null : _edicts.FirstOrDefault(x =>
                x.BusinessId == petition.BusinessId && x.KingdomId == petition.KingdomId);
            if (state == null) return "Enacted · effect record unavailable";
            string status = state.Status == "active" && CurrentDay() >= state.ExpiresDay ? "expired" : state.Status;
            switch (status)
            {
                case "active": return "Enacted · effects active";
                case "expired": return "Enacted · effects expired";
                case "repealed": return "Enacted · later repealed";
                case "ownership_changed": return "Enacted · ended after ownership changed";
                case "project_ended": return "Enacted · project ended";
                case "superseded_by_native_policy": return "Enacted · superseded by kingdom policy";
                default: return "Enacted · effect record unavailable";
            }
        }

        // Reconstruct solely from the allowlisted definition and named campaign entities.
        // These are public settlement mechanics, never freeform character relationship data.
        internal string PublicEdictDescription(ReignGovernmentBusinessRecord record)
        {
            if (record?.Kind != "edict") return string.Empty;
            var terms = JObject.Parse(record.ActionPayloadJson);
            var definition = GovernmentEdictRules.Find(terms.Value<string>("edictId"));
            if (definition == null) return "The original edict definition is unavailable.";
            var settlement = FindSettlement(terms.Value<string>("settlementId"));
            var beneficiary = FindClan(terms.Value<string>("beneficiaryClanId"));
            return (record.IsRepeal ? "Repeal ends the following measures and returns unused reserves. " : "")
                + definition.Terms + "\nSettlement: " + settlement?.Name + ". Beneficiary and payer: " + beneficiary?.Name + ".";
        }

        public bool CreateEdictBusiness(string kingdomId, string proposalId, Hero actor, out string businessId, out string result)
        {
            businessId = string.Empty;
            var kingdom = FindKingdom(kingdomId);
            if (!CanFileGovernmentPetition(kingdomId, actor) || IsCharterPending(kingdomId) || EnsureGovernment(kingdom) == null)
            { result = "Only the current ruler may introduce this edict recommendation."; return false; }
            var choice = GetEdictProposalChoices(kingdomId).OfType<JObject>().FirstOrDefault(x => x.Value<string>("proposalId") == proposalId);
            if (choice?.Value<bool>("available") != true) { result = "This edict is unavailable or already has a pending proposal."; return false; }
            bool repeal = proposalId.StartsWith("edict_repeal:", StringComparison.Ordinal);
            var existing = repeal ? _edicts.FirstOrDefault(x => x.Id == proposalId.Substring(13)) : null;
            var parts = proposalId.Split(':');
            string edictId = repeal ? existing?.EdictId : parts[1];
            string settlementId = repeal ? existing?.SettlementId : parts[2];
            var settlement = FindSettlement(settlementId);
            var definition = GovernmentEdictRules.Find(edictId);
            if (definition == null || settlement?.OwnerClan?.Leader == null) { result = "The settlement or its owner is unavailable."; return false; }
            var payload = EdictPetitionPayload(edictId, settlement, existing);
            var record = new ReignGovernmentBusinessRecord
            {
                KingdomStringId = kingdomId, Kind = "edict", Title = choice.Value<string>("label"), Summary = choice.Value<string>("description"),
                TargetId = edictId + ":" + settlementId, PetitionerHeroStringId = actor.StringId, ActionKindValue = (int)ReignGovernmentActionKind.Policy,
                CreatedDay = CurrentDay(), Status = "queued", IsUrgent = false, IsRepeal = repeal, ActionPayloadJson = payload.ToString(Formatting.None),
                RequestedOptionId = "accept", StatusQuoOptionId = "reject",
                OptionsJson = new JArray(new JObject { ["id"] = "accept", ["label"] = (repeal ? "Repeal " : "Approve ") + definition.Name,
                    ["description"] = choice.Value<string>("description"), ["affirmative"] = true },
                    new JObject { ["id"] = "reject", ["label"] = "Keep the current law", ["affirmative"] = false }).ToString(Formatting.None),
                ParticipantsJson = new JArray(new JObject { ["heroId"] = actor.StringId, ["role"] = "petitioner" },
                    new JObject { ["heroId"] = settlement.OwnerClan.Leader.StringId, ["role"] = "beneficiary" }).ToString(Formatting.None)
            };
            _business.Add(record);
            ChangedBusiness(record);
            businessId = record.BusinessId;
            result = "The edict is queued for the regular government session. Its named scope, costs and duration will be voted on.";
            return true;
        }

        private bool ValidateEdictBusiness(ReignGovernmentBusinessRecord record, out string result)
        {
            result = string.Empty;
            if (!ReignGovernmentPatches.VerifyReady()) { result = "Government effect hooks are unavailable; the recorded vote is held safely."; return false; }
            var terms = JObject.Parse(record.ActionPayloadJson);
            var definition = GovernmentEdictRules.Find(terms.Value<string>("edictId"));
            var settlement = FindSettlement(terms.Value<string>("settlementId"));
            var kingdom = FindKingdom(record.KingdomStringId);
            if (definition == null || settlement?.Town == null || kingdom == null || settlement.OwnerClan?.Kingdom != kingdom
                || settlement.OwnerClan.StringId != terms.Value<string>("beneficiaryClanId"))
            { CloseInvalidBusiness(record, result = "The named settlement, beneficiary or kingdom changed; a new proposal is needed."); return false; }
            if (record.IsRepeal)
            {
                if (!_edicts.Any(x => x.Id == terms.Value<string>("repealId") && x.Status == "active"))
                { CloseInvalidBusiness(record, result = "The edict has already expired or ended."); return false; }
                return true;
            }
            if (_edicts.Any(x => x.Status == "active" && x.SettlementId == settlement.StringId && x.EdictId == definition.Id))
            { CloseInvalidBusiness(record, result = "An equivalent edict already governs this settlement."); return false; }
            if (GovernmentEdictRules.HasConflict(definition.Id, kingdom.ActivePolicies.Select(x => x.StringId)))
            { CloseInvalidBusiness(record, result = "A conflicting native policy now applies. Repeal it by vote and introduce a fresh edict proposal."); return false; }
            if (definition.Id == "public_works" && (!EdictProjectAvailable(settlement.Town)
                || settlement.Town.CurrentBuilding.BuildingType.StringId != terms.Value<string>("projectId")
                || settlement.Town.CurrentBuilding.CurrentLevel != terms.Value<int>("projectLevel")))
            { CloseInvalidBusiness(record, result = "The named construction project changed or completed before the vote."); return false; }
            return true;
        }

        private bool ExecuteEdictBusiness(ReignGovernmentBusinessRecord record, string optionId, bool overridden, out string result)
        {
            if (!ValidateEdictBusiness(record, out result)) return false;
            if (optionId == "reject") { FinishBusiness(record, optionId, overridden); result = record.Outcome; return true; }
            var terms = JObject.Parse(record.ActionPayloadJson);
            if (record.IsRepeal) EndEdict(_edicts.First(x => x.Id == terms.Value<string>("repealId")), "repealed");
            else
            {
                if (_edicts.Any(x => x.BusinessId == record.BusinessId)) { result = "This edict has already been funded and enacted."; return false; }
                var definition = GovernmentEdictRules.Find(terms.Value<string>("edictId"));
                var funder = FindHero(terms.Value<string>("funderHeroId"));
                double enactedDay = CampaignTime.Now.ToDays;
                double expiry = enactedDay + definition.DurationDays;
                if (definition.Id == "extraordinary_subsidy")
                {
                    float next = NextGovernmentSessionDay(CurrentDay());
                    expiry = next < 0 ? -1 : NextGovernmentSessionDay(next);
                    if (expiry <= enactedDay)
                    {
                        CloseInvalidBusiness(record, result = "The recorded vote was not implemented because the regular session calendar is unavailable. No edict was enacted. The motion is closed; a fresh proposal may be introduced when the calendar is available.");
                        return false;
                    }
                }
                if (definition.Budget > 0 && (funder == null || !funder.IsAlive || funder.Gold < definition.Budget))
                {
                    CloseInvalidBusiness(record, result = "The recorded vote was not implemented because the named payer could not fund the granary budget. No payment, law or benefits were created. The motion is closed; a fresh proposal may be introduced at a regular session.");
                    return false;
                }
                var state = new GovernmentEdictState { Id = "edict_" + record.BusinessId, BusinessId = record.BusinessId, EdictId = definition.Id,
                    KingdomId = record.KingdomStringId, SettlementId = terms.Value<string>("settlementId"), BeneficiaryClanId = terms.Value<string>("beneficiaryClanId"),
                    FunderHeroId = terms.Value<string>("funderHeroId"), ProjectId = terms.Value<string>("projectId"), ProjectLevel = terms.Value<int>("projectLevel"),
                    EnactedDay = enactedDay, ExpiresDay = expiry,
                    LastRecruitDay = (int)enactedDay, BudgetRemaining = definition.Budget };
                if (definition.Budget > 0) GiveGoldAction.ApplyBetweenCharacters(funder, null, definition.Budget, true);
                _edicts.Add(state);
            }
            FinishBusiness(record, optionId, overridden); result = record.Outcome; return true;
        }

        private int EdictOptionInterest(ReignGovernmentBusinessRecord record, Hero member)
        {
            var terms = JObject.Parse(record.ActionPayloadJson);
            return member?.Clan?.StringId == terms.Value<string>("beneficiaryClanId") ? 20 : 0;
        }

        internal GovernmentEdictEffects EdictEffects(Settlement settlement)
        {
            if (settlement?.IsVillage == true) settlement = settlement.Village.Bound;
            if (settlement == null) return new GovernmentEdictEffects();
            double day = CampaignTime.Now.ToDays;
            var kingdom = settlement.OwnerClan?.Kingdom;
            return GovernmentEdictRules.Effects(_edicts.Where(x => x.SettlementId == settlement.StringId && x.KingdomId == kingdom?.StringId
                && x.BeneficiaryClanId == settlement.OwnerClan?.StringId
                && !GovernmentEdictRules.HasConflict(x.EdictId, kingdom?.ActivePolicies.Select(p => p.StringId))
                && (x.EdictId != "public_works" || EdictProjectMatches(x, settlement.Town))), day, settlement.Culture != settlement.OwnerClan?.Culture);
        }

        private static bool EdictProjectAvailable(Town town) => town?.CurrentBuilding?.BuildingType != null && !town.CurrentBuilding.BuildingType.IsDailyProject;
        private static bool EdictProjectMatches(GovernmentEdictState state, Town town) => EdictProjectAvailable(town)
            && town.CurrentBuilding.BuildingType.StringId == state.ProjectId && town.CurrentBuilding.CurrentLevel == state.ProjectLevel;
        private void TickEdicts()
        {
            if (!ReignGovernmentPatches.VerifyReady()) return;
            double now = CampaignTime.Now.ToDays;
            int day = (int)now;
            foreach (var closed in _edicts.Where(GovernmentEdictRules.HasPendingReturn).ToList()) ReturnEdictResources(closed);
            foreach (var state in _edicts.Where(x => x.Status == "active").ToList())
            {
                var settlement = FindSettlement(state.SettlementId);
                if (!GovernmentEdictRules.Active(state, now)) { EndEdict(state, "expired"); continue; }
                if (settlement?.Town == null || settlement.OwnerClan?.Kingdom?.StringId != state.KingdomId || settlement.OwnerClan.StringId != state.BeneficiaryClanId)
                { EndEdict(state, "ownership_changed"); continue; }
                if (state.EdictId == "public_works" && !EdictProjectMatches(state, settlement.Town)) { EndEdict(state, "project_ended"); continue; }
                if (GovernmentEdictRules.HasConflict(state.EdictId, settlement.OwnerClan.Kingdom.ActivePolicies.Select(x => x.StringId)))
                { EndEdict(state, "superseded_by_native_policy"); continue; }
                if (!GovernmentEdictRules.CanTick(state, now)) continue;
                state.LastTickDay = day; // Persisted receipt: never replay a paid daily transfer after reload.
                if (state.EdictId == "public_granary") TickEdictGranary(state, settlement.Town, day);
                if (state.EdictId == "castle_guard") TickEdictService(state, settlement.Town, now);
            }
        }

        private void EndEdict(GovernmentEdictState state, string reason)
        {
            if (state.Status != "active") return;
            state.Status = reason;
            ReturnEdictResources(state);
            // Recruitment was fully paid and used real volunteers. Repeal ends further
            // enlistment, not the service of paid troops or unrelated matching troop stacks.
            state.ServiceTroops.Clear();
        }

        private void ReturnEdictResources(GovernmentEdictState state)
        {
            if (!GovernmentEdictRules.HasPendingReturn(state)) return;
            var originalClan = FindClan(state.BeneficiaryClanId);
            var funder = FindHero(state.FunderHeroId);
            var recipient = funder?.IsAlive == true && funder.Clan == originalClan ? funder : originalClan?.Leader;
            int refund = GovernmentEdictRules.RefundAmount(state, recipient?.IsAlive == true && originalClan != null);
            if (refund > 0)
            {
                GiveGoldAction.ApplyBetweenCharacters(null, recipient, refund, true);
                state.BudgetRemaining -= refund; state.GoldRefunded += refund;
            }
            var grain = TaleWorlds.CampaignSystem.Campaign.Current?.ObjectManager?.GetObject<ItemObject>("grain");
            if (originalClan == null || grain == null || state.GrainStock <= 0) return;
            // Captured settlement inventories belong to the new owner. Prefer a retained
            // settlement, then an available party of the original funding clan.
            var destination = originalClan.Settlements.Where(x => x.OwnerClan == originalClan && x.Town != null && !x.Town.IsUnderSiege)
                .OrderBy(x => x.StringId == state.SettlementId ? 0 : 1).ThenBy(x => x.StringId, StringComparer.Ordinal).FirstOrDefault();
            var inventory = destination?.ItemRoster ?? MobileParty.All.Where(x => x.IsActive && x.ActualClan == originalClan
                    && x.MapEvent == null && x.SiegeEvent == null).OrderBy(x => x.StringId, StringComparer.Ordinal).FirstOrDefault()?.ItemRoster;
            int returned = GovernmentEdictRules.ReturnGrainAmount(state, inventory != null);
            if (returned > 0)
            {
                inventory.AddToCounts(grain, returned);
                state.GrainStock -= returned; state.GrainReturned += returned;
            }
        }

        private void TickEdictGranary(GovernmentEdictState state, Town town, int day)
        {
            if (town.IsUnderSiege) return;
            var grain = TaleWorlds.CampaignSystem.Campaign.Current?.ObjectManager?.GetObject<ItemObject>("grain");
            if (grain == null) return;
            int price = Math.Max(1, town.GetItemPrice(grain));
            int buy = GovernmentEdictRules.GrainPurchase(state.BudgetRemaining, price, town.Settlement.ItemRoster.GetItemNumber(grain), state.GrainStock);
            if (buy > 0)
            {
                town.Settlement.ItemRoster.AddToCounts(grain, -buy);
                town.ChangeGold(buy * price);
                state.BudgetRemaining -= buy * price; state.GoldSpent += buy * price;
                state.GrainStock += buy; state.GrainPurchased += buy;
            }
            int release = GovernmentEdictRules.GrainDistribution(state.GrainStock, (int)Math.Max(0, town.FoodStocksUpperLimit() - town.FoodStocks),
                town.FoodStocks < town.FoodStocksUpperLimit() * .25f && town.FoodChange < 0f);
            if (release <= 0) return;
            state.GrainStock -= release; state.GrainReleased += release;
            town.FoodStocks += release; state.LastDistributionDay = day;
        }

        private void TickEdictService(GovernmentEdictState state, Town town, double now)
        {
            int day = (int)now;
            var garrison = town.GarrisonParty;
            var payer = town.OwnerClan?.Leader;
            if (garrison == null || payer == null || town.IsUnderSiege) return;
            foreach (var id in state.ServiceTroops.Keys.ToList())
            {
                var troop = CharacterObject.All.FirstOrDefault(x => x.StringId == id);
                state.ServiceTroops[id] = troop == null ? 0 : Math.Min(state.ServiceTroops[id], garrison.MemberRoster.GetTroopCount(troop));
            }
            bool exempt = _edicts.Any(x => x.SettlementId == state.SettlementId && x.EdictId == "harvest_exemption" && GovernmentEdictRules.Active(x, now));
            bool recruited = false;
            foreach (var village in town.Settlement.BoundVillages.OrderBy(x => x.Settlement.StringId, StringComparer.Ordinal))
            {
                int room = (int)TaleWorlds.CampaignSystem.Campaign.Current.Models.PartySizeLimitModel.CalculateGarrisonPartySizeLimit(town.Settlement).ResultNumber - garrison.MemberRoster.TotalManCount;
                if (!GovernmentEdictRules.CanRecruit(state, now, village.VillageState == Village.VillageStates.Normal, exempt,
                    town.FoodStocks > 0 && town.FoodChange > 0, garrison.GetAvailableWageBudget() > 0, room)) continue;
                // Native castle villages recruit the base of their noble line; town
                // villages recruit the regular base. Never invent a replacement volunteer.
                var basic = village.Bound?.IsCastle == true ? village.Settlement.Culture?.EliteBasicTroop : village.Settlement.Culture?.BasicTroop;
                if (basic == null) continue;
                int wage = TaleWorlds.CampaignSystem.Campaign.Current.Models.PartyWageModel.GetCharacterWage(basic);
                int cost = Math.Max(1, TaleWorlds.CampaignSystem.Campaign.Current.Models.PartyWageModel.GetTroopRecruitmentCost(basic, payer).RoundedResultNumber);
                if (garrison.GetAvailableWageBudget() < wage || payer.Gold < cost + wage * 3) continue;
                Hero source = village.Settlement.Notables.FirstOrDefault(x => x.VolunteerTypes != null && x.VolunteerTypes.Any(t => t == basic));
                if (source == null) continue;
                int index = Array.IndexOf(source.VolunteerTypes, basic);
                source.VolunteerTypes[index] = null;
                GiveGoldAction.ApplyBetweenCharacters(payer, source, cost, true);
                garrison.MemberRoster.AddToCounts(basic, 1);
                state.ServiceRecruitsIssued++; recruited = true;
                state.ServiceTroops[basic.StringId] = (state.ServiceTroops.TryGetValue(basic.StringId, out int old) ? old : 0) + 1;
            }
            if (recruited) state.LastRecruitDay = day;
        }

    }
}

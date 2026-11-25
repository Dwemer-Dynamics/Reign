using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.Campaign;
using ReignBeta.UI;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.Library;
using ReignBeta.Characters.Integration;
using ReignBeta.Characters.World;
using ReignBeta.Court.FamilyChambers;
using ReignBeta.Court.Integration;
using ReignBeta.Court.UI;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Persistence.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Persistence.Save;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.Rebellion.Campaign;
using ReignBeta.Relationships.Campaign;
using ReignBeta.Relationships.Integration;
using ReignBeta.Spymaster.Campaign;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Court.Campaign
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        private const string CourtOwnedActionUnavailable = "This court action belongs to a different loaded court or Save Sync point.";

        private async Task<NativeCourtExecution> ExecuteNativeActionsAsync(JArray actions, JObject weddingReceipt = null,
            Func<bool> requestOwner = null, Action onSuccess = null, bool ownedPaidSupply = false)
        {
            return await ReignMainThread.InvokeAsync(() =>
            {
                JArray receipts = new JArray();
                try
                {
                    if (requestOwner != null && !requestOwner()) throw new InvalidOperationException(CourtOwnedActionUnavailable);
                    var commands = (actions ?? new JArray()).OfType<JObject>().ToList();
                    if (commands.Count > 1 && commands.Any(a => a.Value<string>("type") == "marriage_response"
                        || ReignMarriageService.HasMarriageIntent(ReignCourtDialogueClient.ParseCourtWorldAction(a["action"] as JObject ?? a["worldAction"] as JObject))))
                        throw new InvalidOperationException("A marriage decision must bind its consideration inside one signed wedding package.");
                    bool IsPaidSupply(JObject command) => string.Equals(command.Value<string>("type"), "transfer_item",
                        StringComparison.OrdinalIgnoreCase) && command.Value<int?>("goldAmount") > 0;
                    if (commands.Any(IsPaidSupply) && (!ownedPaidSupply || requestOwner == null || commands.Count != 1))
                        throw new InvalidOperationException("Paid supply requires one exact owned and recoverable transfer.");
                    if (commands.Any(command => string.Equals(command.Value<string>("type"), "transfer_item", StringComparison.OrdinalIgnoreCase)
                        && !IsPaidSupply(command) && _paidSupplyJournal.HasCommand(command.Value<string>("commandId") ?? "")))
                        throw new InvalidOperationException("This command ID is owned by a saved paid supply attempt.");
                    foreach (JObject action in commands)
                    {
                        string type = action.Value<string>("type") ?? string.Empty;
                        string nativeCommandId = action.Value<string>("commandId") ?? string.Empty;
                        JObject receipt = new JObject { ["type"] = type };
                        if (IsPaidSupply(action))
                        {
                            if (!requestOwner() || string.IsNullOrWhiteSpace(nativeCommandId)
                                || _appliedNativeCommandIds.Contains(nativeCommandId)
                                || !JToken.DeepEquals(action["paidSupplyRecoveryVersion"], 1))
                                throw new InvalidOperationException("Paid supply requires one exact owned and recoverable transfer.");
                            ReignSupplyTransferResult payment = ReignCourtSupplyService.ApplyOwnedPaidTransfer(
                                ReadSupplyTransfer(action, nativeCommandId), _session.Authority, _paidSupplyJournal,
                                ReignCampaignIdentity.CurrentCampaignId(), ReignServices.WorldHistory?.TimelineId ?? "main",
                                requestOwner);
                            if (!payment.Success) throw new InvalidOperationException(payment.Error);

                            receipt["ok"] = true;
                            receipt["commandId"] = nativeCommandId;
                            receipt["receiptId"] = payment.ReceiptId;
                            receipt["paidSupply"] = JObject.FromObject(payment.PaidReceipt);
                            receipt["idempotentReplay"] = payment.Replayed;
                            receipts.Add(receipt);
                            if (!payment.Replayed) onSuccess?.Invoke();
                            return new NativeCourtExecution { Success = true, Receipt = new JObject { ["ok"] = true, ["actions"] = receipts } };
                        }
                        if (!string.IsNullOrWhiteSpace(nativeCommandId) && _appliedNativeCommandIds.Contains(nativeCommandId))
                        {
                            receipt["ok"] = true;
                            receipt["idempotentReplay"] = true;
                            receipt["commandId"] = nativeCommandId;
                            receipts.Add(receipt);
                            continue;
                        }
                        switch (type)
                        {
                            case "world_action":
                            {
                                JObject encoded = action["action"] as JObject ?? action["worldAction"] as JObject;
                                ReignWorldActionRecord worldAction = ReignCourtDialogueClient.ParseCourtWorldAction(encoded);
                                if (worldAction == null) throw new InvalidOperationException("The validated world action could not be decoded.");
                                worldAction.Source = "court_decision";
                                worldAction.RequiresAcceptance = false;
                                if (!ReignActionValidator.Validate(worldAction, out string validationError)) throw new InvalidOperationException(validationError);
                                bool weddingAction = ReignMarriageService.HasMarriageIntent(worldAction);
                                if (weddingAction && weddingReceipt == null)
                                    throw new InvalidOperationException("The marriage must have an exact court receipt before scheduling.");
                                if (ReignServices.AI?.EnqueueAndExecuteServerActions(new[] { worldAction }) != 1) throw new InvalidOperationException("The authoritative world action queue rejected the plan.");
                                ReignActionResult result = ReignServices.AI.GetLastActionResult(worldAction.ActionId);
                                if (result != null && !result.Success) throw new InvalidOperationException(result.Message);
                                if (weddingAction && result?.Completed != true)
                                {
                                    weddingReceipt["worldActionId"] = worldAction.ActionId;
                                    weddingReceipt["weddingId"] = ReignMarriageService.WeddingId(worldAction);
                                    _courtWeddingReceipts.Add(weddingReceipt.ToString(Formatting.None));
                                    return new NativeCourtExecution { PendingWedding = true };
                                }
                                receipt["actionId"] = worldAction.ActionId;
                                receipt["result"] = result?.ResultCode ?? "queued";
                                break;
                            }
                            case "spend_player_gold":
                            {
                                int amount = Math.Max(0, action.Value<int?>("amount") ?? 0);
                                if ((Hero.MainHero?.Gold ?? 0) < amount) throw new InvalidOperationException("The player can no longer afford the validated cost.");
                                if (amount > 0) GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, amount, true);
                                receipt["amount"] = amount;
                                break;
                            }
                            case "spend_clan_influence":
                            {
                                float amount = Math.Max(0f, action.Value<float?>("amount") ?? 0f);
                                if ((Clan.PlayerClan?.Influence ?? 0f) < amount) throw new InvalidOperationException("The player clan no longer has the validated influence.");
                                if (amount > 0f) ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -amount);
                                receipt["amount"] = amount;
                                break;
                            }
                            case "transfer_gold":
                            {
                                Hero from = FindHero(action.Value<string>("fromHeroStringId"));
                                Hero to = FindHero(action.Value<string>("toHeroStringId"));
                                int amount = Math.Max(0, action.Value<int?>("amount") ?? 0);
                                if (from == null || to == null || from == to || amount <= 0) throw new InvalidOperationException("A gift requires two distinct current heroes and a positive amount.");
                                if (from != Hero.MainHero) throw new InvalidOperationException("Court gifts may spend only player-clan spendable gold.");
                                if (from.Gold < amount) throw new InvalidOperationException("The player can no longer afford the validated gift.");
                                GiveGoldAction.ApplyBetweenCharacters(from, to, amount, true);
                                receipt["fromHeroStringId"] = from.StringId;
                                receipt["toHeroStringId"] = to.StringId;
                                receipt["amount"] = amount;
                                break;
                            }
                            case "transfer_item":
                            {
                                ReignSupplyTransferResult transfer = ReignCourtSupplyService.ApplyTransfer(
                                    ReadSupplyTransfer(action, action.Value<string>("commandId") ?? "court_transfer_" + Guid.NewGuid().ToString("N")),
                                    _session.Authority);
                                if (!transfer.Success) throw new InvalidOperationException(transfer.Error);
                                receipt["receiptId"] = transfer.ReceiptId;
                                break;
                            }
                            case "appoint_governor":
                            {
                                Town town = Settlement.Find(action.Value<string>("settlementStringId") ?? string.Empty)?.Town;
                                Hero hero = FindHero(action.Value<string>("heroStringId"));
                                if (town == null || hero == null || town.OwnerClan != Clan.PlayerClan || hero.Clan != Clan.PlayerClan || hero.IsPrisoner)
                                    throw new InvalidOperationException("The governor appointment no longer satisfies native eligibility.");
                                ChangeGovernorAction.Apply(town, hero);
                                receipt["settlementStringId"] = town.StringId;
                                receipt["heroStringId"] = hero.StringId;
                                break;
                            }
                            case "fund_construction":
                            {
                                Town town = Settlement.Find(action.Value<string>("settlementStringId") ?? string.Empty)?.Town;
                                int amount = Math.Max(0, action.Value<int?>("goldAmount") ?? 0);
                                if (town == null || town.OwnerClan != Clan.PlayerClan || amount <= 0) throw new InvalidOperationException("Construction funding requires a player-clan town or castle and a positive amount.");
                                if ((Hero.MainHero?.Gold ?? 0) < amount) throw new InvalidOperationException("The player can no longer afford the validated construction funding.");
                                int oldBoost = town.BoostBuildingProcess;
                                int oldGold = Hero.MainHero.Gold;
                                try
                                {
                                    GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, amount, true);
                                    town.BoostBuildingProcess += amount;
                                }
                                catch
                                {
                                    town.BoostBuildingProcess = oldBoost;
                                    int restore = oldGold - Hero.MainHero.Gold;
                                    if (restore > 0) GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, restore, false);
                                    throw;
                                }
                                receipt["settlementStringId"] = town.StringId;
                                receipt["goldAmount"] = amount;
                                receipt["boostBuildingProcess"] = town.BoostBuildingProcess;
                                break;
                            }
                            case "change_relation":
                            {
                                Hero from = FindHero(action.Value<string>("fromHeroStringId")) ?? Hero.MainHero;
                                Kingdom target = Kingdom.All.FirstOrDefault(x => x.StringId == (action.Value<string>("toKingdomStringId") ?? string.Empty));
                                Hero to = target?.Leader;
                                int amount = action.Value<int?>("amount") ?? 0;
                                if (from != Hero.MainHero || to == null || amount < 1 || amount > 2) throw new InvalidOperationException("The ambassador relation change no longer satisfies its bounded native terms.");
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(from, to, amount, true);
                                receipt["fromHeroStringId"] = from.StringId;
                                receipt["toHeroStringId"] = to.StringId;
                                receipt["amount"] = amount;
                                break;
                            }
                            case "rebellion_response":
                            {
                                string result = string.Empty;
                                if (ReignServices.Rebellion?.TryResolvePlayerUltimatum(action.Value<string>("movementId"), action.Value<string>("response"), out result) != true)
                                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(result) ? "The rebellion response was rejected." : result);
                                receipt["result"] = result;
                                break;
                            }
                            case "marriage_response":
                            {
                                if (weddingReceipt == null)
                                    throw new InvalidOperationException("The marriage must have an exact court receipt before scheduling.");
                                string weddingId = "court_marriage_" + action.Value<string>("evaluationId");
                                var execution=ReignServices.Relationship?.ApplyArrangedMarriage(new ReignDirectorAction {
                                    DirectorActionId=weddingId, ActionType="marriage",
                                    ActorId=action.Value<string>("heroAId"),TargetId=action.Value<string>("heroBId"),
                                    Payload=new JObject { ["route"]="arranged",["timelineId"]=action.Value<string>("timelineId"),["marriageTerms"]=action["marriageTerms"]?.DeepClone() }
                                },true);
                                if (execution?.Value<string>("status") == "wedding_pending")
                                {
                                    weddingReceipt["weddingId"] = weddingId;
                                    _courtWeddingReceipts.Add(weddingReceipt.ToString(Formatting.None));
                                    return new NativeCourtExecution { PendingWedding = true };
                                }
                                if(execution?.Value<string>("status")!="completed")throw new InvalidOperationException(execution?.Value<string>("error")??"Marriage executor unavailable.");
                                receipt["marriageVerified"]=true;receipt["paymentVerified"]=true;receipt["marriageReceipt"]=execution;
                                break;
                            }
                            default:
                                throw new InvalidOperationException("No authoritative client executor exists for court native action '" + type + "'.");
                        }
                        if (!string.IsNullOrWhiteSpace(nativeCommandId) && !_appliedNativeCommandIds.Contains(nativeCommandId))
                        {
                            _appliedNativeCommandIds.Add(nativeCommandId);
                            if (_appliedNativeCommandIds.Count > 500) _appliedNativeCommandIds.RemoveRange(0, _appliedNativeCommandIds.Count - 500);
                        }
                        receipt["ok"] = true;
                        receipts.Add(receipt);
                    }
                    onSuccess?.Invoke();
                    return new NativeCourtExecution { Success = true, Receipt = new JObject { ["ok"] = true, ["actions"] = receipts } };
                }
                catch (Exception ex)
                {
                    return new NativeCourtExecution { Success = false, Error = ex.Message, Receipt = new JObject { ["ok"] = false, ["error"] = ex.Message, ["actions"] = receipts } };
                }
            }).ConfigureAwait(false);
        }

        internal void PumpCourtWeddingReports()
        {
            if (_courtWeddingReportInFlight || ReignSaveSyncCoordinator.IsAlignmentPending
                || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress) return;
            string timeline = ReignServices.WorldHistory?.TimelineId ?? "main";
            string campaign = ReignCampaignIdentity.CurrentCampaignId();
            foreach (string encoded in _courtWeddingReceipts)
            {
                var link = JObject.Parse(encoded);
                if (link.Value<string>("campaignId") != campaign || link.Value<string>("timelineId") != timeline) continue;
                var wedding = ReignServices.Relationship?.Wedding(link.Value<string>("weddingId"));
                var action = ReignServices.AI?.Actions.FirstOrDefault(a => a.ActionId == link.Value<string>("worldActionId"));
                if (action != null && !action.IsTerminal || action == null && wedding?.Terminal != true) continue;
                bool success = wedding?.State == WeddingState.Completed
                    && (action == null || action.Status == ReignWorldActionStatus.Completed);
                string status = success ? "completed" : wedding?.State == WeddingState.Cancelled ? "wedding_cancelled" : "failed";
                var receipt = new JObject
                {
                    ["ok"] = success, ["error"] = wedding?.Reason ?? action?.FailureReason ?? "",
                    ["actions"] = new JArray(new JObject
                    {
                        ["type"] = action == null ? "marriage_response" : "world_action",
                        ["weddingId"] = wedding?.Id ?? "", ["actionId"] = action?.ActionId ?? "",
                        ["marriageVerified"] = success, ["paymentVerified"] = success
                    })
                };
                _courtWeddingReportInFlight = true;
                _ = ReportCourtWeddingAsync(encoded, link, status, receipt);
                break;
            }
        }

        private async Task ReportCourtWeddingAsync(string encoded, JObject link, string status, JObject receipt)
        {
            ReignCourtServerResponse response = null;
            try
            {
                response = await ReignCourtServerClient.ResolveMatterAsync(link.Value<string>("matterId"), link.Value<string>("optionId"),
                    link.Value<string>("termsHash"), link.Value<long>("revision"), link.Value<string>("commandId"), status, receipt)
                    .ConfigureAwait(false);
            }
            finally
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    _courtWeddingReportInFlight = false;
                    if (response?.Ok != true || !ReferenceEquals(Instance, this)) return;
                    _courtWeddingReceipts.Remove(encoded);
                    string matterId = link.Value<string>("matterId");
                    _serverMatterRevisions[matterId] = response.Revision;
                    var matter = _matters.FirstOrDefault(m => m.MatterId == matterId);
                    if (matter == null) return;
                    matter.State = status == "completed" ? ReignCourtMatterState.Resolved
                        : status == "wedding_cancelled" ? ReignCourtMatterState.Invalidated : ReignCourtMatterState.AwaitingDecision;
                    matter.ResolutionReceiptJson = receipt.ToString(Formatting.None);
                    StateChanged?.Invoke();
                }).ConfigureAwait(false);
            }
        }

        private JObject NativeHeroEligibility(Hero hero)
        {
            bool inScope = hero != null && (hero.Clan == Clan.PlayerClan || hero.CompanionOf == Clan.PlayerClan || ReignCourtNobleCampaignBehavior.IsCourtNoble(hero)
                || _session.Authority == ReignCourtAuthority.Royal && hero.Clan?.Kingdom == Clan.PlayerClan?.Kingdom);
            return new JObject
            {
                ["isAdult"] = hero != null && hero.Age >= TaleWorlds.CampaignSystem.Campaign.Current.Models.AgeModel.HeroComesOfAge,
                ["isFree"] = hero != null && hero.IsAlive && hero.IsActive && !hero.IsPrisoner,
                ["inCourtScope"] = inScope,
                ["isAbsent"] = hero?.CurrentSettlement?.StringId != _session.HostSettlementStringId,
                ["hasConflictingNativeDuty"] = hero?.GovernorOf != null || hero?.PartyBelongedTo?.Army != null
            };
        }

        private string MutationUnavailable()
        {
            return "The court server is unavailable. Native reports and time controls remain available, but mutations stay queued.";
        }

        internal static Hero FindHero(string id)
        {
            return string.IsNullOrWhiteSpace(id) ? null : Hero.AllAliveHeroes.FirstOrDefault(x => string.Equals(x.StringId, id, StringComparison.OrdinalIgnoreCase));
        }

        private static ReignSupplyTransferRequest ReadSupplyTransfer(JObject action, string commandId)
        {
            return new ReignSupplyTransferRequest
            {
                CommandId = commandId,
                SourceSettlementStringId = action.Value<string>("sourceSettlementStringId") ?? string.Empty,
                TargetSettlementStringId = action.Value<string>("targetSettlementStringId") ?? string.Empty,
                ItemStringId = action.Value<string>("itemStringId") ?? string.Empty,
                ItemAmount = action.Value<int?>("amount") ?? 0,
                GoldAmount = action.Value<int?>("goldAmount") ?? 0,
                VassalConsentGranted = action.Value<bool?>("vassalConsentGranted") == true,
                GoldPayerKind = action.Value<string>("goldPayerKind") ?? string.Empty,
                GoldPayerStringId = action.Value<string>("goldPayerStringId") ?? string.Empty,
                GoldRecipientKind = action.Value<string>("goldRecipientKind") ?? string.Empty,
                GoldRecipientStringId = action.Value<string>("goldRecipientStringId") ?? string.Empty
            };
        }

        private sealed class NativeCourtExecution
        {
            public bool PendingWedding;
            public bool Success;
            public string Error = string.Empty;
            public JObject Receipt = new JObject();
        }
    }
}

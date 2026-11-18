using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using Reign.Core.Contracts.Court;
using ReignBeta.Actions;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Court
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        private readonly Dictionary<string, Action> _courtAgreementFences = new Dictionary<string, Action>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ReignActionOption>> _courtAgreementSocialChoices = new Dictionary<string, List<ReignActionOption>>(StringComparer.Ordinal);
        private Task _courtAgreementTail = Task.CompletedTask;
        private readonly HashSet<string> _courtAgreementSpyEffects = new HashSet<string>(StringComparer.Ordinal);

        private Action CourtActionContextFence()
        {
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            string campaignId = ReignCampaignIdentity.CurrentCampaignId();
            string timeline = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
            return () =>
            {
                if (!ReferenceEquals(campaign, TaleWorlds.CampaignSystem.Campaign.Current) || !ReferenceEquals(Instance, this)
                    || campaignId != ReignCampaignIdentity.CurrentCampaignId()
                    || timeline != (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main"))
                    throw new InvalidOperationException("The campaign or timeline changed during this court operation.");
            };
        }

        // Existing UI callers omit the operation ID. Agreement callers bind the
        // asynchronous service's native continuation to this exact saved journal.
        private Action CourtAgreementContinuation(string operationId)
        {
            if (string.IsNullOrEmpty(operationId)) return () => { };
            if (!_courtAgreementFences.TryGetValue(operationId, out var fence))
                throw new InvalidOperationException("This court agreement operation has no active continuation.");
            return fence;
        }

        private CourtOfficeAssignment AgreementOffice(ActionClause c) => _offices.FirstOrDefault(x => x.IsActive && x.HeroStringId == c.ActorId);
        private AmbassadorPosting AgreementAmbassador(ActionClause c) => _ambassadors.FirstOrDefault(x => x.HeroStringId == c.ActorId && x.Status != "recalled" && x.Status != "replaced");
        private static string SpyType(ActionClause c) => c.ActionId.Substring("spymaster.".Length);
        private static bool SpyLand(ActionClause c) => c.Arguments.ContainsKey("targetSettlementId");
        private static string SpyTarget(ActionClause c) => ReignActionService.Arg(c, SpyLand(c) ? "targetSettlementId" : "targetHeroId");
        private static string SocialKey(ActionClause c) => SpyTarget(c) + ":" + (c.ActionId.Contains("reputation") ? "reputations" : "rumors");

        internal IReadOnlyList<ReignActionOption> AgreementSocialChoices(ActionClause c) =>
            _courtAgreementSocialChoices.TryGetValue(SocialKey(c), out var choices) ? choices : new List<ReignActionOption>();

        internal async Task LoadAgreementSocialChoicesAsync(ActionClause c)
        {
            var fence = CourtActionContextFence();
            string key = SocialKey(c), target = SpyTarget(c), kind = c.ActionId.Contains("reputation") ? "reputations" : "rumors";
            var response = await ReignSpymasterServerClient.GetSocialStatusAsync(target).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() =>
            {
                fence();
                if (response == null || response["error"] != null) throw new InvalidOperationException("The rumor and reputation list could not be loaded.");
                _courtAgreementSocialChoices[key] = (response[kind] as JArray ?? new JArray()).OfType<JObject>()
                    .Select(row => new ReignActionOption { Id = row.Value<string>("tag_id") ?? row.Value<string>("occurrence_id") ?? "",
                        Label = row.Value<string>("description") ?? row.Value<string>("tag_id") ?? "" })
                    .Where(row => row.Id.Length > 0).GroupBy(row => row.Id).Select(group => group.First()).ToList();
            }).ConfigureAwait(false);
        }

        internal List<string> CheckCourtAgreement(ActionClause c)
        {
            var errors = new List<string>();
            var actor = ReignActionService.HeroById(c.ActorId);
            bool chancellorAction = c.ActionId.Contains("chancellor");
            if (!(chancellorAction ? IsPlayerKingdomRuler() : IsRuleModeActive) || c.RecipientId != Hero.MainHero?.StringId || actor == null || actor == Hero.MainHero
                || !actor.IsActive || actor.IsPrisoner || actor.IsChild)
            { errors.Add("This service requires a free adult NPC and the reigning player in an active royal court session."); return errors; }
            if (chancellorAction)
            {
                if (c.ActionId == "court.serve_chancellor")
                {
                    if (!Chancellor.IsVacant) errors.Add("Dismiss the current Chancellor before appointing another.");
                    if (!IsNormalChancellorCandidate(actor, out string reason)) errors.Add(reason);
                    if (actor.PartyBelongedTo?.LeaderHero == actor && (actor.PartyBelongedTo.MapEvent != null || actor.PartyBelongedTo.Army != null))
                        errors.Add("The candidate cannot safely relinquish an active battle or army command.");
                }
                else if (Chancellor.IsVacant || Chancellor.HeroId != c.ActorId) errors.Add("The consenting NPC must be the current Chancellor.");
                if (c.ActionId == "court.set_chancellor_active")
                {
                    if (Chancellor.Emergency || Chancellor.State == ReignChancellorOfficeState.CaptiveContinuity || Chancellor.State == ReignChancellorOfficeState.Handoff)
                        errors.Add("Emergency and handoff coverage must finish before changing duties.");
                    if (ReignActionService.Arg(c, "active") == "true" && Hero.MainHero.Gold < Chancellor.Salary)
                        errors.Add("Activation requires one full active-day salary in the treasury.");
                }
                return errors;
            }
            if (c.ActionId.StartsWith("spymaster.", StringComparison.Ordinal))
            {
                if (ActiveSpymaster != actor) errors.Add("The consenting actor must be your currently appointed Spymaster.");
                if (ActiveSpymasterMissionCount >= SpymasterMissionCapacity) errors.Add("The intelligence service is at capacity.");
                bool land = SpyLand(c); string type = SpyType(c), target = SpyTarget(c);
                var hero = land ? FindSettlement(target)?.Town?.Governor : FindHero(target);
                if (land ? FindSettlement(target) == null : hero == null) errors.Add("The exact intelligence target is unavailable.");
                if (type.StartsWith("mitigate_own", StringComparison.Ordinal) && target != Hero.MainHero.StringId) errors.Add("This operation concerns the ruler's own social record.");
                if (type.StartsWith("assassinate", StringComparison.Ordinal) && (hero == null || hero == Hero.MainHero || !hero.IsAlive || hero.IsChild || hero.IsNotable && hero.Issue != null))
                    errors.Add("This assassination target is ineligible.");
                var quote = GetSpymasterQuote(type, land ? "settlement" : "person", target);
                if (quote.GoldCost > int.Parse(ReignActionService.Arg(c, "maxCost", "0"))) errors.Add("This operation costs " + quote.GoldCost + " denars, above the agreed maximum.");
                if (Hero.MainHero.Gold < quote.GoldCost) errors.Add("The ruler's treasury cannot cover the operation cost.");
                if (type.StartsWith("mitigate_", StringComparison.Ordinal) && ReignActionService.Arg(c, "socialItemId").Length == 0) errors.Add("Select an exact rumor or reputation.");
                if (type.StartsWith("fabricate_", StringComparison.Ordinal) && ReignActionService.Arg(c, "fabricatedText").Length == 0) errors.Add("State the exact words to circulate.");
                return errors;
            }
            if (!_serverAvailable || !_serverSessionOpened) errors.Add(MutationUnavailable());
            if (c.ActionId == "court.serve_office")
            {
                var office = (ReignCourtOffice)Enum.Parse(typeof(ReignCourtOffice), ReignActionService.Arg(c, "office"));
                var active = _offices.FirstOrDefault(x => x.IsActive && x.Office == office);
                if (!ReignCourtOfficeService.IsEligible(actor, _session.Authority, _offices.Where(x => x != active), out var reason)) errors.Add(reason);
                if (office == ReignCourtOffice.Spymaster && active != null && active.HeroStringId != actor.StringId && !CanChangeSpymasterOffice(out reason)) errors.Add(reason);
            }
            if (c.ActionId == "court.leave_office")
            {
                var office = AgreementOffice(c);
                if (office == null) errors.Add("This person has no active court office.");
                else if (office.Office == ReignCourtOffice.Spymaster && !CanChangeSpymasterOffice(out var reason)) errors.Add(reason);
            }
            if (c.ActionId == "court.serve_regent" || c.ActionId == "court.serve_ambassador")
            {
                var eligible = NativeHeroEligibility(actor);
                if (eligible.Value<bool?>("inCourtScope") != true) errors.Add("This person is outside your royal court's appointment scope.");
                if (IsNobleVisitorReserved(actor)) errors.Add("This person is committed to a court visit.");
            }
            if (c.ActionId == "court.leave_regent" && ActiveRegent?.HeroStringId != actor.StringId) errors.Add("This person is not your current regent.");
            if (c.ActionId == "court.serve_ambassador")
            {
                if (AgreementAmbassador(c) != null || _ambassadors.Count(x => x.Status != "recalled" && x.Status != "replaced") >= 3) errors.Add("An ambassador posting is already held or all three postings are occupied.");
                var target = Kingdom.All.FirstOrDefault(k => k.StringId == ReignActionService.Arg(c, "targetKingdomId") && !k.IsEliminated);
                if (target == null || target == Clan.PlayerClan?.Kingdom) errors.Add("Choose a different active kingdom.");
            }
            if ((c.ActionId == "court.recall_ambassador" || c.ActionId == "court.set_ambassador_mission") && AgreementAmbassador(c) == null)
                errors.Add("This person has no active ambassador posting.");
            return errors;
        }

        internal IEnumerable<string> CheckCourtAgreementPackage(ActionAgreement agreement)
        {
            var missions = agreement.Clauses.Where(c => c.ActionId.StartsWith("spymaster.", StringComparison.Ordinal)).ToList();
            if (missions.Count + ActiveSpymasterMissionCount > SpymasterMissionCapacity)
                yield return "This complete bargain exceeds the intelligence service's available mission capacity.";
            long operationCost = missions.Sum(c => (long)GetSpymasterQuote(SpyType(c), SpyLand(c) ? "settlement" : "person", SpyTarget(c)).GoldCost);
            long promisedGold = agreement.Clauses.Where(c => c.ActionId == "give.gold" && c.ActorId == Hero.MainHero?.StringId)
                .Sum(c => (long)int.Parse(ReignActionService.Arg(c, "quantity")) * c.Installments);
            if (operationCost + promisedGold > (Hero.MainHero?.Gold ?? 0))
                yield return "The treasury cannot cover all intelligence costs and promised gold in this complete bargain.";
            if (agreement.Clauses.Count(c => c.ActionId == "court.serve_ambassador") + _ambassadors.Count(x => x.Status != "recalled" && x.Status != "replaced") > 3)
                yield return "This complete bargain exceeds the three ambassador postings.";
            if (agreement.Clauses.Count(c => c.ActionId.Contains("chancellor")) > 1)
                yield return "Review one Chancellor office change at a time.";
        }

        internal JObject CaptureCourtAgreement(ActionClause c)
        {
            var office = c.ActionId == "court.serve_office" ? _offices.FirstOrDefault(x => x.IsActive && x.Office.ToString() == ReignActionService.Arg(c, "office"))
                : c.ActionId == "court.leave_office" ? AgreementOffice(c) : null;
            return new JObject { ["rulerId"] = Clan.PlayerClan?.Kingdom?.Leader?.StringId ?? "",
                ["officeId"] = office?.AssignmentId ?? "",
                ["regentId"] = c.ActionId.EndsWith("regent", StringComparison.Ordinal) ? ActiveRegent?.AssignmentId ?? "" : "",
                ["postingId"] = c.ActionId.Contains("ambassador") ? AgreementAmbassador(c)?.PostingId ?? "" : "",
                ["chancellorTermId"] = c.ActionId.Contains("chancellor") ? Chancellor.TermId ?? "" : "", ["status"] = "prepared" };
        }

        private void ValidateCourtAssignmentSnapshot(ActionClause c, JObject before)
        {
            var current = CaptureCourtAgreement(c);
            foreach (string key in new[] { "rulerId", "officeId", "regentId", "postingId", "chancellorTermId" })
                if (before.Value<string>(key) != current.Value<string>(key)) throw new InvalidOperationException("The relevant court assignment changed; review the agreement again.");
        }

        internal void ApplyCourtAgreement(ActionAgreement a, ActionClause c, AgreementStep step)
        {
            var errors = CheckCourtAgreement(c);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
            var before = JObject.Parse(step.Snapshot);
            ValidateCourtAssignmentSnapshot(c, before);
            string operation = "sa_" + ReignCourtTerms.Hash(a.Id + ":" + c.Id);
            before["operationId"] = operation; before["status"] = "queued"; step.Snapshot = before.ToString(Formatting.None);
            Action context = CourtActionContextFence();
            _courtAgreementFences[operation] = () =>
            {
                context();
                if (!IsPlayerKingdomRuler() || ReignAgreementBehavior.Instance?.IsActiveAgreement(a.Id, a.TermsHash()) != true)
                    throw new InvalidOperationException("The exact accepted court agreement is no longer active.");
            };
            _courtAgreementTail = RunCourtAgreementAsync(_courtAgreementTail, context, operation, a, c, step);
        }

        private async Task RunCourtAgreementAsync(Task prior, Action context, string operation, ActionAgreement a, ActionClause c, AgreementStep step)
        {
            bool invoked = false;
            try
            {
                // Complete Apply's durable step registration before a synchronously
                // completed local response can re-enter the agreement engine.
                await Task.Yield();
                await prior.ConfigureAwait(false);
                if (c.ActionId.StartsWith("spymaster.mitigate_", StringComparison.Ordinal))
                {
                    Task refresh = await ReignMainThread.InvokeAsync(() => { _courtAgreementFences[operation](); return LoadAgreementSocialChoicesAsync(c); }).ConfigureAwait(false);
                    await refresh.ConfigureAwait(false);
                }
                Task<string> pending = await ReignMainThread.InvokeAsync(() =>
                {
                    _courtAgreementFences[operation]();
                    var errors = CheckCourtAgreement(c);
                    if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
                    var receipt = JObject.Parse(step.Snapshot); receipt["status"] = "running"; step.Snapshot = receipt.ToString(Formatting.None);
                    ValidateCourtAssignmentSnapshot(c, receipt);
                    invoked = true;
                    var actor = ReignActionService.HeroById(c.ActorId);
                    if (c.ActionId.Contains("chancellor"))
                    {
                        string result;
                        bool applied = c.ActionId == "court.serve_chancellor"
                            ? TryAppointChancellor(actor, int.Parse(ReignActionService.Arg(c, "activeDailySalary")), true, out result)
                            : c.ActionId == "court.leave_chancellor" ? TryDismissChancellor(true, out result)
                            : TryScheduleChancellorActive(bool.Parse(ReignActionService.Arg(c, "active")), out result);
                        receipt["chancellorTermIdAfter"] = Chancellor.TermId ?? "";
                        step.Snapshot = receipt.ToString(Formatting.None);
                        return Task.FromResult(applied ? "" : result);
                    }
                    if (c.ActionId.StartsWith("spymaster.", StringComparison.Ordinal))
                    {
                        string target = SpyTarget(c), type = SpyType(c);
                        if (type.StartsWith("mitigate_", StringComparison.Ordinal) && !AgreementSocialChoices(c).Any(x => x.Id == ReignActionService.Arg(c, "socialItemId")))
                            throw new InvalidOperationException("The exact selected rumor or reputation is no longer present.");
                        receipt["missionId"] = operation; step.Snapshot = receipt.ToString(Formatting.None);
                        return Task.FromResult(StartSpymasterMission(type, SpyLand(c) ? "settlement" : "person", target,
                            SpyLand(c) ? FindSettlement(target)?.Name?.ToString() : FindHero(target)?.Name?.ToString(),
                            ReignActionService.Arg(c, "socialItemId"), ReignActionService.Arg(c, "fabricatedText"), operation));
                    }
                    switch (c.ActionId)
                    {
                        case "court.serve_office": return AssignOfficeAsync((ReignCourtOffice)Enum.Parse(typeof(ReignCourtOffice), ReignActionService.Arg(c, "office")), actor, operation);
                        case "court.leave_office": return DismissOfficeAsync(AgreementOffice(c), operation);
                        case "court.serve_regent": return AssignRegentAsync(actor, operation);
                        case "court.leave_regent": return DismissRegentAsync(operation);
                        case "court.serve_ambassador": return AssignAmbassadorAsync(actor, Kingdom.All.First(k => k.StringId == ReignActionService.Arg(c, "targetKingdomId")), ReignActionService.Arg(c, "missionType"), operation);
                        case "court.recall_ambassador": return RecallAmbassadorAsync(AgreementAmbassador(c), operation);
                        case "court.set_ambassador_mission": return SetAmbassadorMissionAsync(AgreementAmbassador(c), ReignActionService.Arg(c, "missionType"), new JObject(), operation);
                        default: throw new InvalidOperationException("Unknown court agreement adapter.");
                    }
                }).ConfigureAwait(false);
                string error = await pending.ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(() =>
                {
                    context();
                    var receipt = JObject.Parse(step.Snapshot); receipt["status"] = error.Length == 0 ? "applied" : "uncertain"; receipt["message"] = error;
                    step.Snapshot = receipt.ToString(Formatting.None);
                    _courtAgreementFences.Remove(operation);
                    ReignAgreementBehavior.Instance.RecheckAgreement(a.Id);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    try { context(); } catch { return; }
                    var receipt = JObject.Parse(step.Snapshot); receipt["status"] = invoked ? "uncertain" : "failed"; receipt["message"] = ex.Message;
                    step.Snapshot = receipt.ToString(Formatting.None); _courtAgreementFences.Remove(operation);
                    ReignAgreementBehavior.Instance.RecheckAgreement(a.Id);
                }).ConfigureAwait(false);
            }
        }

        internal bool ObserveCourtAgreement(ActionClause c, AgreementStep step)
        {
            var receipt = JObject.Parse(step.Snapshot);
            string status = receipt.Value<string>("status"), operation = receipt.Value<string>("operationId");
            if (status == "queued" || status == "running")
            {
                if (_courtAgreementFences.ContainsKey(operation)) return false;
                throw new AgreementRecoveryException("The court request was interrupted. Reconcile its saved operation before retrying.", step.Snapshot, null);
            }
            if (status == "failed") throw new InvalidOperationException(receipt.Value<string>("message"));
            if (status != "applied") throw new AgreementRecoveryException("The court service did not confirm its exact result.", step.Snapshot, null);
            if (c.ActionId.StartsWith("spymaster.", StringComparison.Ordinal))
            {
                var mission = EnsureSpymasterState().Missions.FirstOrDefault(m => m.MissionId == receipt.Value<string>("missionId"));
                if (mission == null) throw new AgreementRecoveryException("The dispatched intelligence operation is missing.", step.Snapshot, null);
                if (mission.State == ReignSpymasterMissionState.Active) return false;
                if (mission.State != ReignSpymasterMissionState.Succeeded) throw new InvalidOperationException("The promised intelligence mission did not succeed: " + mission.ResultSummary);
                if (mission.AgreementEffectState == "uncertain") throw new AgreementRecoveryException("The intelligence effect needs reconciliation.", mission.ReportJson, null);
                if (mission.AgreementEffectState == "pending" && !_courtAgreementSpyEffects.Contains(mission.MissionId))
                    throw new AgreementRecoveryException("The intelligence effect was interrupted before confirmation.", mission.ReportJson, null);
                return mission.AgreementEffectState == "completed";
            }
            bool matches;
            switch (c.ActionId)
            {
                case "court.serve_chancellor": matches = !Chancellor.IsVacant && Chancellor.HeroId == c.ActorId
                    && Chancellor.TermId == receipt.Value<string>("chancellorTermIdAfter")
                    && Chancellor.Salary == int.Parse(ReignActionService.Arg(c, "activeDailySalary")); break;
                case "court.leave_chancellor": matches = Chancellor.IsVacant; break;
                case "court.set_chancellor_active": matches = Chancellor.HeroId == c.ActorId
                    && Chancellor.TermId == receipt.Value<string>("chancellorTermIdAfter")
                    && Chancellor.DesiredActive == bool.Parse(ReignActionService.Arg(c, "active")); break;
                case "court.serve_office": matches = _offices.Any(x => x.IsActive && x.HeroStringId == c.ActorId && x.LastCommandId == operation); break;
                case "court.leave_office": matches = _offices.Any(x => !x.IsActive && x.HeroStringId == c.ActorId && x.LastCommandId == operation); break;
                case "court.serve_regent": matches = ActiveRegent?.HeroStringId == c.ActorId && ActiveRegent.LastCommandId == operation; break;
                case "court.leave_regent": matches = _regents.Any(x => !x.IsActive && x.HeroStringId == c.ActorId && x.LastCommandId == operation); break;
                case "court.serve_ambassador":
                    var posting = _ambassadors.FirstOrDefault(x => x.HeroStringId == c.ActorId && x.LastCommandId == operation);
                    if (posting?.Status == "traveling") return false;
                    matches = posting?.Status == "posted"; break;
                case "court.recall_ambassador": matches = _ambassadors.Any(x => x.HeroStringId == c.ActorId && x.Status == "recalled" && x.LastCommandId == operation); break;
                case "court.set_ambassador_mission": matches = _ambassadors.Any(x => x.HeroStringId == c.ActorId && x.MissionType == ReignActionService.Arg(c, "missionType") && x.LastCommandId == operation); break;
                default: matches = false; break;
            }
            if (!matches) throw new AgreementRecoveryException("The actual court record differs from this agreement's operation receipt.", step.Snapshot, null);
            return true;
        }
    }
}

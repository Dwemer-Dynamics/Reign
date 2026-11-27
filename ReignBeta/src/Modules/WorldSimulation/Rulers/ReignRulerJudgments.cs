using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using ReignBeta.Shared.Rulers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using NativeCampaign = TaleWorlds.CampaignSystem.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Rebellion.Campaign;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.News;
using ReignBeta.Platform.Runtime;
using ReignBeta.Persistence.Campaign;

namespace ReignBeta.WorldSimulation.Rulers
{
    internal sealed class RulerAppealTarget
    {
        internal RulerAppealTarget(RulerJudgment judgment, string subjectId, string clanId = "")
        {
            Judgment = judgment;
            SubjectId = subjectId;
            ClanId = clanId;
        }

        internal RulerJudgment Judgment { get; }
        internal string SubjectId { get; }
        internal string ClanId { get; }
        internal bool IsClanAppeal => ClanId.Length != 0;
        internal string Id => IsClanAppeal ? ReignRulerPolicy.ClanAppealId(Judgment.Id, ClanId, SubjectId) : Judgment.Id;
    }

    public sealed partial class ReignRulerCampaignBehavior
    {
        private static bool HeldForJudgment(RulerJudgment j)
        {
            var captive = HeroById(j.PrisonerId);
            var realm = Realm(j.VictorKingdomId);
            return captive?.IsAlive == true && captive.IsPrisoner
                && (j.DefeatedLord ? captive.PartyBelongedToAsPrisoner?.MapFaction == realm
                    : IsSovereignCustody(captive.PartyBelongedToAsPrisoner, realm));
        }

        private void PrepareDecision(RulerJudgment j, RulerVerdict? requested)
        {
            if (!ReignRulerPolicy.DecisionDue(j, Day) || !HeldForJudgment(j)
                || Realm(j.VictorKingdomId)?.Leader?.IsAlive != true
                || Realm(j.VictorKingdomId)?.Leader?.StringId != j.VictorId) return;
            _networkBusy = true; int epoch = _epoch; var owner = NativeCampaign.Current;
            string decidingRuler = j.VictorId;
            _ = Resolve();
            async Task Resolve()
            {
                try
                {
                    // Capture native context on the calling main thread, inside
                    // the try/finally so preparation errors cannot strand busy.
                    var p = ReignRulerClient.RulerRequestPayload(); p["victorId"] = j.VictorId; p["prisonerId"] = j.PrisonerId;
                    p["victorProfile"] = ReignRulerClient.RulerPersonalitySnapshot(HeroById(j.VictorId));
                    var defeated = Realm(j.DefeatedKingdomId);
                    p["clans"] = new JArray((defeated?.Clans ?? Enumerable.Empty<Clan>()).Where(c => !c.IsEliminated && !c.IsUnderMercenaryService && c.Leader?.IsAlive == true)
                        .Select(c => new JObject { ["clanId"] = c.StringId, ["leaderId"] = c.Leader.StringId }));
                    var r = await ReignRulerClient.RulerRequestAsync("context", p).ConfigureAwait(false);
                    await ReignMainThread.InvokeAsync(() => {
                        if (Instance != this || epoch != _epoch || owner != NativeCampaign.Current
                            || !ReignRulerPolicy.DecisionDue(j, Day) || !HeldForJudgment(j)) return;
                        if (Realm(j.VictorKingdomId)?.Leader?.StringId != decidingRuler) return;
                        if (r.Value<bool?>("ok") != true) throw new InvalidOperationException(r.Value<string>("error") ?? "Ruler judgment context unavailable.");
                        j.Traits = r["traits"]?.ToObject<Dictionary<string, double>>() ?? new Dictionary<string, double>();
                        j.TraitSource = r.Value<string>("traitSource") ?? "";
                        j.VictorAffinity = r.Value<int>("victorAffinity");
                        j.Clans = (r["clans"]?.ToObject<List<RulerClanSnapshot>>() ?? new List<RulerClanSnapshot>()).Select(c => {
                            c.Fiefs = ClanById(c.ClanId)?.Settlements.Where(s => s.IsTown || s.IsCastle).Select(s => s.StringId).ToList() ?? new List<string>(); return c; }).ToList();
                        j.Failure = "";
                        var intended = requested ?? j.PendingVerdict;
                        if (!intended.HasValue && ReignRulerPolicy.InfluencedVerdict(j) is RulerVerdict influenced
                            && JudgmentRealmsAtWar(j) && Eligible(j, influenced))
                        {
                            Sentence(j, influenced);
                            return;
                        }
                        if (j.Draw < 0 && TryConsumeJudgmentDraw(j, out double controlledDraw)) j.Draw = controlledDraw;
                        if (intended.HasValue && Eligible(j, intended.Value)) { Sentence(j, intended.Value); return; }
                        // A queued choice can become unavailable during captivity.
                        // Release it so the normal eligible decision can proceed.
                        j.PendingVerdict = null;
                        if (HeroById(j.VictorId) == Hero.MainHero) { PresentDecision(j); return; }
                        if (j.CivilWar && !j.RebelVictory)
                        {
                            if (j.Draw < 0) j.Draw = (MBRandom.RandomInt(1, 101)-1)/100d;
                            var sovereign = HeroById(j.VictorId); var captive = HeroById(j.PrisonerId);
                            int score = (int)Math.Floor(j.Draw*100)+1 + 15*sovereign.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Mercy)
                                + (int)Math.Round(sovereign.GetRelation(captive)/5d, MidpointRounding.AwayFromZero);
                            var choice = score >= 67 ? RulerVerdict.Freedom : score >= 34 || CampaignOptions.IsLifeDeathCycleDisabled ? RulerVerdict.Imprisonment : RulerVerdict.CleanExecution;
                            Sentence(j, choice); return;
                        }
                        var scores = ReignRulerPolicy.Scores(j.Traits, j.VictorAffinity.Value, j.CivilWar && j.RebelVictory);
                        if (j.DefeatedLord || (j.CivilWar && !j.RebelVictory)) { scores.Clear(); scores[RulerVerdict.Freedom] = j.Traits["mercy"]; scores[RulerVerdict.Imprisonment] = 50; scores[RulerVerdict.CleanExecution] = 100-j.Traits["mercy"]; }
                        foreach (var key in scores.Keys.Where(v => !Eligible(j, v)).ToList()) scores.Remove(key);
                        if (j.Draw < 0) j.Draw = MBRandom.RandomFloat;
                        Sentence(j, ReignRulerPolicy.Choose(scores, j.Draw));
                    }).ConfigureAwait(false);
                }
                catch (Exception ex) { await ReignMainThread.InvokeAsync(() => { if (epoch == _epoch && Instance == this && j.Stage == RulerJudgmentStage.AwaitingDecision) j.Failure = ex.Message; }).ConfigureAwait(false); ReignLog.Warn("Ruler judgment awaiting retry: " + ex.Message); }
                finally { await ReignMainThread.InvokeAsync(() => { if (epoch == _epoch && Instance == this) _networkBusy = false; }).ConfigureAwait(false); }
            }
        }
        internal IEnumerable<RulerAppealTarget> InfluenceTargets(Hero ruler)
        {
            Hero player = Hero.MainHero;
            if (!Enabled(RulerOptions.Judgments) || ApplyingNative || ReignCampaignInitializationGate.IsPending
                || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress
                || player?.IsAlive != true || player.IsPrisoner || !Sovereign(ruler) || ruler == player)
                yield break;
            foreach (var judgment in State.Judgments.Where(j => j.VictorId == ruler.StringId && ruler.Clan.Kingdom.StringId == j.VictorKingdomId
                && j.PrisonerId != player.StringId && (!j.CivilWar || Enabled(RulerOptions.CivilWars))
                && ReignRulerPolicy.AppealWindowOpen(j, Day) && HeldForJudgment(j) && JudgmentRealmsAtWar(j)))
            {
                if (ReignRulerPolicy.InfluenceOpen(judgment, Day))
                    yield return new RulerAppealTarget(judgment, judgment.PrisonerId);

                var rebellion = ReignServices.Rebellion;
                if (rebellion == null) continue;
                foreach (var (clanId, leaderId) in rebellion.RebelClanAppealTargets(judgment))
                    if (leaderId != player.StringId && !judgment.ClanInfluences.ContainsKey(clanId))
                        yield return new RulerAppealTarget(judgment, leaderId, clanId);
            }
        }

        private static bool JudgmentRealmsAtWar(RulerJudgment judgment)
        {
            Kingdom victor = Realm(judgment.VictorKingdomId), defeated = Realm(judgment.DefeatedKingdomId);
            return victor != null && defeated != null && victor != defeated && !victor.IsEliminated
                && !defeated.IsEliminated && victor.IsAtWarWith(defeated);
        }

        internal IEnumerable<RulerVerdict> InfluenceVerdicts(RulerAppealTarget target)
            => Enum.GetValues(typeof(RulerVerdict)).Cast<RulerVerdict>().Where(v => EligibleAppealVerdict(target, v));

        internal RulerAppealTarget FindInfluenceTarget(string judgmentId, string rulerId)
            => InfluenceTargets(HeroById(rulerId)).FirstOrDefault(target => target.Id == judgmentId);

        internal bool CanRequestInfluence(RulerAppealTarget target, string value, out RulerVerdict verdict)
            => Enum.TryParse(value, out verdict) && verdict.ToString() == value && target != null
                && EligibleAppealVerdict(target, verdict);

        private bool EligibleAppealVerdict(RulerAppealTarget target, RulerVerdict verdict)
            => target.IsClanAppeal
                ? ReignRulerPolicy.ClanAppealVerdictEligible(verdict, !CampaignOptions.IsLifeDeathCycleDisabled)
                : EligibleVerdict(target.Judgment, verdict, beforeContext: true);

        internal void AttemptInfluence(string judgmentId, string rulerId, string value, string operationId)
        {
            if (InfluenceReceipt(judgmentId, operationId) != null) return;
            var target = FindInfluenceTarget(judgmentId, rulerId);
            if (!CanRequestInfluence(target, value, out var verdict))
                throw new InvalidOperationException("This ruler's judgment or requested verdict is no longer available.");
            Hero ruler = HeroById(rulerId);
            int relation = ruler.GetRelation(Hero.MainHero);
            int roll = MBRandom.RandomInt(1, 101);
            var attempt = new RulerJudgmentInfluence
            {
                OperationId = operationId,
                RulerId = rulerId,
                SubjectId = target.SubjectId,
                Verdict = verdict,
                Chance = ReignRulerPolicy.InfluenceChance(relation),
                Roll = roll,
                Succeeded = ReignRulerPolicy.InfluenceSucceeds(relation, roll)
            };
            if (target.IsClanAppeal) target.Judgment.ClanInfluences.Add(target.ClanId, attempt);
            else target.Judgment.Influence = attempt;
        }

        internal RulerJudgmentInfluence InfluenceReceipt(string judgmentId, string operationId)
        {
            foreach (var judgment in State.Judgments)
            {
                if (judgment.Id == judgmentId && judgment.Influence?.OperationId == operationId) return judgment.Influence;
                foreach (var appeal in judgment.ClanInfluences)
                    if (appeal.Value.OperationId == operationId
                        && ReignRulerPolicy.ClanAppealId(judgment.Id, appeal.Key, appeal.Value.SubjectId) == judgmentId)
                        return appeal.Value;
            }
            return null;
        }

        internal static string InfluenceVerdictLabel(RulerVerdict verdict) => Label(verdict);

        internal bool Eligible(RulerJudgment j, RulerVerdict v) => EligibleVerdict(j, v, beforeContext: false);

        private bool EligibleVerdict(RulerJudgment j, RulerVerdict v, bool beforeContext)
        {
            if (ReignRulerPolicy.Execution(v) && CampaignOptions.IsLifeDeathCycleDisabled) return false;
            if (j.DefeatedLord) return v == RulerVerdict.Freedom || v == RulerVerdict.Imprisonment || v == RulerVerdict.CleanExecution;
            if (j.CivilWar) return j.RebelVictory ? v == RulerVerdict.Independence || v == RulerVerdict.RebelExecution || v == RulerVerdict.Reunification : v == RulerVerdict.Freedom || v == RulerVerdict.Imprisonment || v == RulerVerdict.CleanExecution;
            if (v == RulerVerdict.Exile) return CessionCandidates(j).Any() && (beforeContext
                ? Realm(j.DefeatedKingdomId)?.Clans.Any(c => !c.IsEliminated && !c.IsUnderMercenaryService && CanSucceed(j, c.Leader)) == true
                : Successor(j) != null);
            return v == RulerVerdict.TortureExecution || v == RulerVerdict.CleanExecution || v == RulerVerdict.Exile || v == RulerVerdict.Submission;
        }
        private void PresentDecision(RulerJudgment j)
        {
            if (!ReignRulerPolicy.DecisionDue(j, Day) || !HeldForJudgment(j)) return;
            if (_presenting || Mission.Current != null || NativeCampaign.Current.ConversationManager.IsConversationInProgress || MobileParty.MainParty?.MapEvent != null
                || ReignBeta.Diplomacy.UI.ReignDiplomacyAnnouncementScreenManager.IsOpen || InformationManager.IsAnyInquiryActive()) return;
            var rows = Enum.GetValues(typeof(RulerVerdict)).Cast<RulerVerdict>().Where(v => Eligible(j, v)).Select(v => new InquiryElement(v, Label(v), null)).ToList();
            _presenting = true;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData("Captured ruler", "Decide the fate of " + (HeroById(j.PrisonerId)?.Name?.ToString() ?? "the captive") + ". Executions allow eight days for escape or rescue; ransom is forbidden after sentencing.",
                rows, true, 1, 1, "Choose", "Decide later", chosen => {
                    _presenting = false;
                    if (j.Stage != RulerJudgmentStage.AwaitingDecision || chosen.Count != 1 || Realm(j.VictorKingdomId)?.Leader != Hero.MainHero) return;
                    var verdict = (RulerVerdict)chosen[0].Identifier;
                    if (verdict == RulerVerdict.Exile) PresentCession(j); else PrepareDecision(j, verdict);
                }, _ => { _presenting = false; _tick = -25; }), true);
        }
        private static string Label(RulerVerdict v)
        {
            switch (v) {
                case RulerVerdict.TortureExecution: return "Torture and humiliation, then execution in eight days";
                case RulerVerdict.CleanExecution: return "Clean execution in eight days";
                case RulerVerdict.Exile: return "Exile, reparations, a ceded holding and immediate peace";
                case RulerVerdict.Submission: return "Submission: unite their kingdom with ours";
                case RulerVerdict.Independence: return "Recognize rebel independence and release the ruler";
                case RulerVerdict.RebelExecution: return "Keep independence and execute the old ruler in eight days";
                case RulerVerdict.Reunification: return "Reunify under the rebel leader and spare the old ruler";
                case RulerVerdict.Freedom: return "Grant freedom";
                default: return "Imprison the defeated leader";
            }
        }
        private static string CompletedSummary(RulerVerdict verdict)
        {
            switch (verdict) {
                case RulerVerdict.TortureExecution: return "The ruler was executed after torture and humiliation";
                case RulerVerdict.CleanExecution: return "The defeated leader's clean execution was carried out";
                case RulerVerdict.Exile: return "The ruler was exiled; reparations, a ceded holding and peace were imposed";
                case RulerVerdict.Submission: return "The defeated kingdom joined the victor, and its ruler survived as a clan leader";
                case RulerVerdict.Independence: return "Rebel independence was recognized, the ruler was released and peace was made";
                case RulerVerdict.RebelExecution: return "The old ruler was executed; the rebels retain independence and the war continues";
                case RulerVerdict.Reunification: return "The kingdom was reunited under the rebel leader, and the old ruler was spared";
                case RulerVerdict.Freedom: return "The defeated leader was granted freedom";
                default: return "The defeated leader remains imprisoned";
            }
        }
        private IEnumerable<Settlement> CessionCandidates(RulerJudgment j)
        {
            var all = Settlement.All.Where(s => s.OwnerClan?.Kingdom?.StringId == j.DefeatedKingdomId && s.IsFortification).ToList();
            return all.Any(s => s.IsTown) ? all.Where(s => s.IsTown) : all.Where(s => s.IsCastle);
        }
        private void PresentCession(RulerJudgment j)
        {
            _presenting = true;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData("Peace concession", "Choose the holding to be ceded. Reparations take one quarter of available realm wealth, without debt.",
                CessionCandidates(j).Select(s => new InquiryElement(s.StringId, s.Name.ToString(), null)).ToList(), true, 1, 1, "Choose", "Cancel",
                chosen => { _presenting = false; if (chosen.Count == 1) { j.CededSettlementId = (string)chosen[0].Identifier; PrepareDecision(j, RulerVerdict.Exile); } },
                _ => { _presenting = false; _tick = -25; }), true);
        }
        private static bool CanSucceed(RulerJudgment j, Hero hero) => hero?.IsAlive == true && hero.StringId != j.PrisonerId
            && !hero.IsChild && !hero.IsPrisoner && hero.Clan?.Kingdom?.StringId == j.DefeatedKingdomId;
        private Hero Successor(RulerJudgment j) => j.Clans.OrderByDescending(c => c.AffinityToVictor)
            .ThenByDescending(c => ClanById(c.ClanId)?.Tier ?? 0).ThenBy(c => c.ClanId, StringComparer.Ordinal).Select(c => HeroById(c.LeaderId))
            .FirstOrDefault(h => CanSucceed(j, h));
        internal void Sentence(RulerJudgment j, RulerVerdict verdict)
        {
            if (!ReignRulerPolicy.DecisionDue(j, Day) || !Eligible(j, verdict) || !HeldForJudgment(j)
                || Realm(j.VictorKingdomId)?.Leader?.StringId != j.VictorId) return;
            j.PendingVerdict = null;
            j.Verdict = verdict; j.SentencedDay = Day; j.Deadline = Day + ReignRulerPolicy.ExecutionDays;
            if (verdict == RulerVerdict.Exile)
            {
                j.SuccessorId = Successor(j)?.StringId ?? "";
                if (j.CededSettlementId.Length == 0)
                {
                    var homeland = Holdings(Realm(j.VictorKingdomId)).Select(Holding).Where(s => s != null).ToList();
                    j.CededSettlementId = CessionCandidates(j).OrderBy(s => homeland.Count == 0 ? 0 : homeland.Min(h => h.GatePosition.Distance(s.GatePosition)))
                        .ThenByDescending(s => s.Town?.Prosperity ?? 0).ThenBy(s => s.StringId, StringComparer.Ordinal).First().StringId;
                }
                var defeated = Realm(j.DefeatedKingdomId);
                j.Payments["treasury"] = (int)ReignRulerPolicy.Reparations(defeated.KingdomBudgetWallet);
                foreach (var clan in defeated.Clans.Where(c => !c.IsEliminated && !c.IsUnderMercenaryService && c.Leader?.IsAlive == true))
                    j.Payments[clan.Leader.StringId] = (int)ReignRulerPolicy.Reparations(clan.Leader.Gold);
            }
            j.Stage = ReignRulerPolicy.Execution(verdict) ? RulerJudgmentStage.Sentenced : RulerJudgmentStage.Applying;
            History(j, "sentenced", Label(verdict));
            if (j.Stage == RulerJudgmentStage.Applying) ApplyJudgment(j);
        }
        private void ApplyJudgment(RulerJudgment j)
        {
            if (j.Failure.StartsWith("Native consequence uncertain:", StringComparison.Ordinal)) return;
            Hero prisoner = HeroById(j.PrisonerId), victor = HeroById(j.VictorId);
            Kingdom defeated = Realm(j.DefeatedKingdomId), winning = Realm(j.VictorKingdomId);
            if (winning?.Leader?.IsAlive == true) victor = winning.Leader;
            if (prisoner == null || victor?.IsAlive != true || winning == null || defeated == null) { j.Failure = "Judgment principals unavailable."; return; }
            if (!j.NativeDeathObserved && !j.NativeExecutionRequested && (!prisoner.IsPrisoner || prisoner.PartyBelongedToAsPrisoner?.MapFaction != winning)) { CancelForLostCustody(j, prisoner.IsAlive, "cancelled"); return; }
            j.Stage = RulerJudgmentStage.Applying; ApplyingNative = true;
            try
            {
                if (ReignRulerPolicy.Execution(j.Verdict))
                {
                    if (!j.Receipts.Contains("execution"))
                    {
                        if (!prisoner.IsDead && !j.NativeExecutionRequested)
                            KillCharacterAction.ApplyByExecution(prisoner, victor, true, prisoner != Hero.MainHero);
                        // Native player death opens heir selection, releases death custody,
                        // then completes through ApplyByDeathMarkForced. Preserve that flow.
                        if (!ReignRulerPolicy.ObserveNativeExecution(j, prisoner.IsDead, NativeExecutionMarked(prisoner))) return;
                        if (j.NativeDeathDay < 0) j.NativeDeathDay = Day;
                    }
                    if (!j.CivilWar)
                        foreach (var row in j.Clans.Where(c => ReignRulerPolicy.DefectsFromRealm(j.Verdict, c.Affinity, ClanById(c.ClanId)?.Kingdom?.StringId, defeated.StringId)))
                            Once(j, "defect:" + row.ClanId, () => {
                                var clan = ClanById(row.ClanId);
                                row.Fiefs = clan.Settlements.Where(s => s.IsFortification).Select(s => s.StringId).ToList();
                                TransferClan(clan, winning);
                            });
                }
                else if (j.Verdict == RulerVerdict.Exile)
                {
                    if ((!j.Receipts.Contains("replacement_ruler") && HeroById(j.SuccessorId)?.Clan?.Kingdom != defeated)
                        || (!j.Receipts.Contains("cession") && Holding(j.CededSettlementId)?.OwnerClan?.Kingdom != defeated)) throw new InvalidOperationException("Exile terms became unavailable.");
                    long outstanding = j.Payments.Where(p => !j.Receipts.Contains("payment:" + p.Key)).Sum(p => (long)p.Value);
                    if (outstanding + winning.KingdomBudgetWallet > int.MaxValue) throw new InvalidOperationException("Victorious treasury cannot receive all reparations.");
                    foreach (var payment in j.Payments.Where(p => !j.Receipts.Contains("payment:" + p.Key)))
                        if (payment.Key == "treasury" ? defeated.KingdomBudgetWallet < payment.Value : HeroById(payment.Key)?.Gold < payment.Value)
                            throw new InvalidOperationException("Exile reparations are no longer affordable.");
                    foreach (var payment in j.Payments) Once(j, "payment:" + payment.Key, () => {
                        int value = payment.Value; if (value == 0) return;
                        if (winning.KingdomBudgetWallet > int.MaxValue - value) throw new InvalidOperationException("Victorious treasury cannot receive reparations.");
                        if (payment.Key == "treasury") { if (defeated.KingdomBudgetWallet < value) throw new InvalidOperationException("Defeated treasury cannot pay reparations."); defeated.KingdomBudgetWallet -= value; }
                        else { var payer = HeroById(payment.Key); if (payer == null || payer.Gold < value) throw new InvalidOperationException("A clan cannot pay its agreed reparations."); payer.ChangeHeroGold(-value); }
                        winning.KingdomBudgetWallet += value;
                    });
                    Once(j, "cession", () => ChangeOwnerOfSettlementAction.ApplyByGift(Holding(j.CededSettlementId), victor));
                    Once(j, "replacement_ruler", () => ChangeRulingClanAction.Apply(defeated, HeroById(j.SuccessorId).Clan));
                    Once(j, "household_exile", () => CreateExile(j, true, false));
                    Once(j, "peace", () => MakePeaceAction.Apply(winning, defeated));
                }
                else if (j.Verdict == RulerVerdict.Submission)
                {
                    var annexed = Holdings(defeated);
                    Once(j, "submission_release", () => { if (prisoner.IsPrisoner) EndCaptivityAction.ApplyByReleasedAfterBattle(prisoner); });
                    foreach (var row in j.Clans) Once(j, "submit:" + row.ClanId, () => TransferClan(ClanById(row.ClanId), winning));
                    Once(j, "submission_relation", () => QueueRelations(j.Id + ":submission", new JArray(new JObject { ["subjectId"] = prisoner.StringId, ["targetId"] = victor.StringId, ["delta"] = 75, ["reason"] = "submission" })));
                    Once(j, "end_realm", () => {
                        foreach (var mercenary in defeated.Clans.Where(c => c.IsUnderMercenaryService).ToList()) ChangeKingdomAction.ApplyByLeaveKingdomAsMercenary(mercenary);
                        if (!defeated.IsEliminated && defeated.Clans.Count == 0) DestroyKingdomAction.Apply(defeated);
                        if (!defeated.IsEliminated) throw new InvalidOperationException("Defeated realm still contains an untransferred noble clan.");
                    });
                    AddEffects(j, annexed);
                }
                else if (j.Verdict == RulerVerdict.Freedom) Once(j, "freedom", () => { if (prisoner.IsPrisoner) EndCaptivityAction.ApplyByReleasedAfterBattle(prisoner); });
                if (j.CivilWar && !j.DefeatedLord) Once(j, "civil_war_resolution", () => ReignServices.Rebellion.CompleteRulerJudgment(j));
                if (j.Verdict == RulerVerdict.Imprisonment && (!prisoner.IsAlive || !prisoner.IsPrisoner
                    || prisoner.PartyBelongedToAsPrisoner?.MapFaction != winning))
                    throw new InvalidOperationException("Imprisonment did not retain the living leader in the victorious kingdom's custody.");
                if (!j.CivilWar && j.Verdict != RulerVerdict.Submission) AddEffects(j, null);
                j.Stage = RulerJudgmentStage.Completed; j.ResolvedDay = Day; j.Failure = ""; History(j, "completed", CompletedSummary(j.Verdict));
            }
            catch (Exception ex) { j.Failure = "Native consequence uncertain: " + ex.Message; ReignLog.Warn("Ruler judgment " + j.Id + " halted: " + ex); }
            finally { ApplyingNative = false; }
        }
        private static void Once(RulerJudgment j, string receipt, Action action)
        { if (j.Receipts.Contains(receipt)) return; action(); j.Receipts.Add(receipt); }
        private static void TransferClan(Clan clan, Kingdom destination)
        {
            if (clan == null || clan.IsEliminated || clan.Kingdom == destination) return;
            var fiefs = clan.Settlements.Where(s => s.IsFortification).ToList(); var prior = clan.Kingdom;
            ChangeKingdomAction.ApplyByJoinToKingdomByDefection(clan, prior, destination);
            if (clan.Kingdom != destination) throw new InvalidOperationException("Native clan transfer did not reach the intended kingdom.");
            foreach (var s in fiefs) if (s.OwnerClan != clan) ChangeOwnerOfSettlementAction.ApplyByGift(s, clan.Leader);
            if (fiefs.Any(s => s.OwnerClan != clan)) throw new InvalidOperationException("Native clan transfer did not preserve its fiefs.");
        }
        private void AddEffects(RulerJudgment j, List<string> conquered)
        {
            if (j.CivilWar || ReignRulerPolicy.Duration(j.Verdict) == 0 || State.Effects.Any(e => e.JudgmentId == j.Id)) return;
            var war = State.Wars.FirstOrDefault(w => w.Id == j.WarId);
            var lands = conquered ?? (war == null ? new List<string>() : (war.KingdomA == j.VictorKingdomId ? war.CapturesA : war.CapturesB).ToList());
            foreach (var c in j.Clans.Where(c => j.Receipts.Contains("defect:" + c.ClanId))) lands.AddRange(c.Fiefs);
            State.Effects.Add(new RulerEffect { JudgmentId = j.Id, Verdict = j.Verdict,
                StartDay = j.NativeDeathObserved && j.NativeDeathDay >= 0 ? j.NativeDeathDay : Day, VictorKingdomId = j.VictorKingdomId,
                DefeatedKingdomId = j.DefeatedKingdomId, WarId = j.WarId, Conquered = lands.Distinct().ToList(),
                Homeland = war == null ? new List<string>() : (war.KingdomA == j.VictorKingdomId ? war.HomelandA : war.HomelandB).ToList() });
        }
        private void CreateExile(RulerJudgment j, bool household, bool fugitive)
        {
            Hero hero = HeroById(j.PrisonerId); if (hero == null || !hero.IsAlive) return;
            var original = hero.Clan;
            var moving = new List<Hero> { hero };
            if (household) { if (hero.Spouse?.IsAlive == true && hero.Spouse.Clan == original) moving.Add(hero.Spouse); moving.AddRange(hero.Children.Where(h => h.IsAlive && h.IsChild && h.Clan == original)); }
            Hero heir = original.AliveLords.Where(h => !h.IsChild && !moving.Contains(h)).OrderByDescending(h => h.Age).ThenBy(h => h.StringId, StringComparer.Ordinal).FirstOrDefault();
            if (original.Leader == hero && heir != null) ChangeClanLeaderAction.ApplyWithSelectedNewLeader(original, heir);
            if (j.ExileClanId.Length == 0) j.ExileClanId = "reign_exile_" + j.Id;
            Clan exile = ClanById(j.ExileClanId);
            if (exile == null)
            {
                exile = Clan.CreateClan(j.ExileClanId); j.ExileClanId = exile.StringId;
                exile.ChangeClanName(new TextObject("{=!}" + hero.Name + "'s household"), new TextObject("{=!}" + hero.Name + "'s household"));
                exile.Culture = original.Culture; exile.Banner = original.Banner; exile.Renown = original.Renown;
            }
            if (hero.IsPrisoner) EndCaptivityAction.ApplyByReleasedAfterBattle(hero);
            if (original.Leader == hero)
            {
                foreach (var s in original.Settlements.Where(s => s.IsFortification).ToList()) ChangeOwnerOfSettlementAction.ApplyByKingDecision(Realm(j.DefeatedKingdomId)?.Leader ?? HeroById(j.VictorId), s);
            }
            foreach (var member in moving) { if (member.GovernorOf != null) ChangeGovernorAction.RemoveGovernorOf(member); member.Clan = exile; }
            exile.SetLeader(hero);
            if (moving.Contains(Hero.MainHero)) AccessTools.Property(typeof(NativeCampaign), "PlayerDefaultFaction").SetValue(NativeCampaign.Current, exile, null);
            if (heir == null && original.AliveLords.Count == 0) DestroyClanAction.Apply(original);
            var safe = Settlement.All.Where(s => s.IsTown && !s.IsUnderSiege && s.OwnerClan?.Kingdom != null && s.OwnerClan.Kingdom.StringId != j.DefeatedKingdomId && s.OwnerClan.Kingdom.StringId != j.VictorKingdomId)
                .OrderBy(s => s.StringId, StringComparer.Ordinal).FirstOrDefault();
            if (safe != null) exile.SetInitialHomeSettlement(safe);
            foreach (var member in moving.Where(h => h != Hero.MainHero))
            {
                if (member.PartyBelongedTo != null) member.PartyBelongedTo.MemberRoster.RemoveTroop(member.CharacterObject);
                if (member.CurrentSettlement != null) LeaveSettlementAction.ApplyForCharacterOnly(member);
                if (safe == null) MakeHeroFugitiveAction.Apply(member); else EnterSettlementAction.ApplyForCharacterOnly(member, safe);
            }
            if (!State.Exiles.Any(e => e.Id == j.Id)) State.Exiles.Add(new RulerExile { Id = j.Id, HeroId = hero.StringId, ClanId = exile.StringId, Fugitive = fugitive, UntilDay = Day + (fugitive ? ReignRulerPolicy.FugitiveDays : ReignRulerPolicy.ExileDays) });
        }
        internal static bool NativeExecutionMarked(Hero hero) => hero?.DeathMark == KillCharacterAction.KillCharacterActionDetail.Executed
            || hero?.DeathMark == KillCharacterAction.KillCharacterActionDetail.ExecutionAfterMapEvent;
        internal bool CompletesDeferredExecution(Hero hero) => hero != null && NativeExecutionMarked(hero)
            && State.Judgments.Any(j => j.PrisonerId == hero.StringId && j.Stage == RulerJudgmentStage.Applying && j.NativeExecutionRequested);
        internal static bool Restricted(Clan clan, Kingdom destination = null)
        {
            if (!Enabled(RulerOptions.Judgments) || Instance == null || clan == null) return false;
            return Instance.State.Exiles.Any(e => e.ClanId == clan.StringId && Day < e.UntilDay && !(e.Fugitive && destination?.Leader == Hero.MainHero));
        }
        private bool CanRecruitFugitive(Hero hero, bool companion)
        {
            if (hero?.IsAlive != true || hero.IsPrisoner || hero.IsChild || hero.Clan?.Leader != hero) return false;
            if (!State.Exiles.Any(e => e.HeroId == hero.StringId && e.Fugitive)) return false;
            return companion ? Clan.PlayerClan.Companions.Count < NativeCampaign.Current.Models.ClanTierModel.GetCompanionLimit(Clan.PlayerClan)
                : Clan.PlayerClan.Kingdom?.Leader == Hero.MainHero;
        }
        internal bool CanRecruitFugitiveMember(Hero hero, Clan target) => target == Clan.PlayerClan && CanRecruitFugitive(hero, true);
        internal void RecruitFugitiveMember(Hero hero, Clan target)
        {
            if (!CanRecruitFugitiveMember(hero, target)) throw new InvalidOperationException("Fugitive recruitment is no longer eligible.");
            var old = hero.Clan;
            AddCompanionAction.Apply(target, hero); AddHeroToPartyAction.Apply(hero, MobileParty.MainParty);
            if (old.AliveLords.Count == 0) DestroyClanAction.Apply(old);
        }
        private void QueueRelations(string id, JArray deltas)
        {
            if (deltas.Count == 0 || _relationOutbox.Any(p => p.Value<string>("operationId") == id)) return;
            var p = ReignRulerClient.RulerRequestPayload(); p["operationId"] = id; p["deltas"] = deltas; _relationOutbox.Add(p);
        }
        private void SendRelations(JObject pending)
        {
            _networkBusy = true; int epoch = _epoch; _ = Send();
            async Task Send()
            {
                try {
                    var probe = ReignRulerClient.RulerRequestPayload(); probe["victorId"] = Hero.MainHero.StringId; probe["prisonerId"] = Hero.MainHero.StringId;
                    var context = await ReignRulerClient.RulerRequestAsync("context", probe).ConfigureAwait(false);
                    var p = (JObject)pending.DeepClone(); p["generation"] = context["generation"]?.DeepClone();
                    var result = await ReignRulerClient.RulerRequestAsync("relations", p).ConfigureAwait(false);
                    if (result.Value<bool?>("ok") != true || result.Value<string>("operationId") != p.Value<string>("operationId")) throw new InvalidOperationException(result.Value<string>("error") ?? "Ruler relationship receipt missing.");
                    await ReignMainThread.InvokeAsync(() => { if (epoch == _epoch && Instance == this) _relationOutbox.Remove(pending); }).ConfigureAwait(false);
                }
                catch (Exception ex) { ReignLog.Warn("Ruler relationship consequence awaiting retry: " + ex.Message); }
                finally { await ReignMainThread.InvokeAsync(() => { if (epoch == _epoch && Instance == this) _networkBusy = false; }).ConfigureAwait(false); }
            }
        }
        private void History(RulerJudgment j, string phase, string summary)
        {
            ReignServices.WorldHistory?.RecordReignSystemEvent("ruler_judgment", phase, "politics", j.Id,
                summary, "major_world", j.VictorId, j.DefeatedKingdomId, JObject.FromObject(new { judgmentId = j.Id,
                    verdict = ReignRulerPolicy.HasIssuedSentence(j) ? j.Verdict.ToString() : "",
                    j.CapturedDay, j.SentencedDay, j.Deadline, j.CancellationReason, j.ResolvedDay }), new JArray(j.PrisonerId));
            ReignServices.News?.Judgment(j, phase);
        }
        internal void RequestPlayerSentence(Hero prisoner)
        {
            if (prisoner == null) return;
            var j = State.Judgments.LastOrDefault(x => x.PrisonerId == prisoner.StringId && x.Stage == RulerJudgmentStage.AwaitingDecision);
            if (j == null && prisoner.PartyBelongedToAsPrisoner != null) { PrisonerTaken(prisoner.PartyBelongedToAsPrisoner, prisoner); j = State.Judgments.LastOrDefault(x => x.PrisonerId == prisoner.StringId && x.Stage == RulerJudgmentStage.AwaitingDecision); }
            if (j == null) return;
            if (HeroById(j.VictorId) != Hero.MainHero || Realm(j.VictorKingdomId)?.Leader != Hero.MainHero)
            {
                InformationManager.DisplayMessage(new InformationMessage("Only the ruling sovereign can decide this captive ruler's fate."));
                return;
            }
            if (!ReignRulerPolicy.DecisionDue(j, Day))
            {
                InformationManager.DisplayMessage(new InformationMessage("A captured ruler's fate can be decided after seven days in captivity."));
                return;
            }
            j.PendingVerdict = RulerVerdict.CleanExecution;
            if (!_networkBusy) PrepareDecision(j, null);
        }
        internal void ScheduleDefeatedLord(Hero defeated, Hero victor, string movement)
        {
            var kingdom = victor?.Clan?.Kingdom;
            var sovereign = kingdom?.Leader;
            if (!Enabled(RulerOptions.CivilWars) || CampaignOptions.IsLifeDeathCycleDisabled || defeated == null || victor == null
                || sovereign?.IsAlive != true
                || State.Judgments.Any(j => j.PrisonerId == defeated.StringId && j.Stage != RulerJudgmentStage.Completed && j.Stage != RulerJudgmentStage.Cancelled)) return;
            var j = new RulerJudgment { Id = Guid.NewGuid().ToString("N"), PrisonerId = defeated.StringId, VictorId = sovereign.StringId,
                VictorKingdomId = kingdom.StringId, DefeatedKingdomId = defeated.Clan?.Kingdom?.StringId ?? kingdom.StringId,
                MovementId = movement, CivilWar = true, DefeatedLord = true, CapturedDay = Day, DecisionCustodyConfirmed = true,
                PendingVerdict = RulerVerdict.CleanExecution, Stage = RulerJudgmentStage.AwaitingDecision };
            State.Judgments.Add(j);
        }
    }
}

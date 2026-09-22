using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.CastleChat;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace ReignBeta.Court
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        public ReignCourtLifeMatter TryCreatePatronageMatterForSlot(string campaignId, string timelineId, int day, int slot, string requestedTemplateId = null)
        {
            Settlement host = FindCurrentCapital();
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            Hero ruler = Hero.MainHero;
            if (host == null || kingdom == null || ruler == null || kingdom.Leader != ruler) return null;
            ReignRulerDocketState state = EnsureRulerDocketState();
            NormalizePatronageState(state);
            string seed = campaignId + "|" + timelineId + "|patronage|" + day + "|" + slot;
            List<ReignPatronageTemplate> eligible = ReignPatronageCatalog.Templates.Where(x =>
                (!x.RequiresSpouse || ruler.Spouse?.IsAlive == true)
                && (!x.RequiresChild || ruler.Children.Any(c => c != null && c.IsAlive))
                && (x.Id != "patronage-coming-of-age" || PatronageComingOfAgeChildren(ruler).Any())
                && ((x.Id != "patronage-returning-artist" && x.Id != "patronage-popular-performer")
                    || state.PatronageCreators.Any(c => c.Role == x.CreatorRole && c.HomeSettlementId == host.StringId
                        && state.PatronageWorks.Any(w => w.CreatorId == c.ActorId)))
                && (!x.RequiresHistoricalSubject || PatronageSubjectHistory(x).Any())
                && !state.CourtLifeMatters.Any(m => m.IsPending && m.TemplateId == x.Id)).ToList();
            if (!string.IsNullOrWhiteSpace(requestedTemplateId)) eligible = eligible.Where(x => x.Id == requestedTemplateId).ToList();
            if (eligible.Count == 0) return null;
            ReignPatronageTemplate template = eligible[ReignCourtLifeRules.StableRoll(seed, eligible.Count)];
            ReignPatronageCreator creator = state.PatronageCreators.FirstOrDefault(x => x.Role == template.CreatorRole
                && x.HomeSettlementId == host.StringId);
            if (creator == null)
            {
                string[] names = { "Aren", "Mira", "Darin", "Selia", "Oren", "Talia", "Varen", "Lina", "Corin", "Nera", "Belen", "Sora" };
                creator = new ReignPatronageCreator { ActorId = "reign_artist_" + ReignCourtTerms.Hash(campaignId + "|" + host.StringId + "|" + template.CreatorRole),
                    Role = template.CreatorRole, HomeSettlementId = host.StringId,
                    Name = names[ReignCourtLifeRules.StableRoll(seed + "|creator", names.Length)] + ", " + template.CreatorRole + " of " + host.Name };
                state.PatronageCreators.Add(creator);
            }
            var matter = new ReignCourtLifeMatter { MatterId = "patronage_" + ReignCourtTerms.Hash(seed), CampaignId = campaignId,
                TimelineId = timelineId, ReignId = CurrentReignId(), Source = ReignDocketSource.Patronage,
                TemplateId = template.Id, Title = template.Title, Summary = ReignPatronageRules.PublicSummary(template), ReceivedDay = day,
                AvailableDay = CurrentCourtLifeDay(), SettlementId = host.StringId, KingdomId = kingdom.StringId };
            matter.Participants.Add(new ReignCourtLifeParticipant { ActorId = creator.ActorId, Name = creator.Name,
                Role = template.CreatorRole, Age = 30,
                PrivateContext = BuildPatronagePrivateContext(template, state, creator.ActorId, ruler) });
            matter.PayloadJson = new JObject { ["creatorId"] = creator.ActorId, ["creatorRole"] = creator.Role,
                ["isNativeHero"] = false, ["historicalSubjects"] = new JArray(PatronageSubjectHistory(template).Select(x => x.Summary).Take(5)),
                ["deliveryIsReliable"] = true, ["benefitsRequirePaidCommission"] = true }.ToString(Formatting.None);
            RefreshPatronageOptions(matter);
            return matter;
        }

        private static IEnumerable<Hero> PatronageComingOfAgeChildren(Hero ruler)
        {
            int adulthood = Math.Max(18, TaleWorlds.CampaignSystem.Campaign.Current?.Models.AgeModel.HeroComesOfAge ?? 18);
            return ruler.Children.Where(c => c != null && c.IsAlive && c.Age >= adulthood - 1 && c.Age < adulthood + 2);
        }

        private static string BuildPatronagePrivateContext(ReignPatronageTemplate template, ReignRulerDocketState state,
            string creatorId, Hero ruler)
        {
            string householdSubjects = template.RequiresSpouse && ruler?.Spouse?.IsAlive == true
                ? " The ruler's spouse is " + ruler.Spouse.Name + ". Ask for any private dedication details."
                : template.RequiresChild && ruler != null
                    ? " Eligible family subjects: " + string.Join("; ", (template.Id == "patronage-coming-of-age"
                        ? PatronageComingOfAgeChildren(ruler) : ruler.Children.Where(c => c != null && c.IsAlive))
                        .Select(c => c.Name + " (age " + (int)Math.Floor(c.Age) + ")"))
                        + ". Do not invent their interests or consent; ask the ruler."
                    : string.Empty;
            return "You are a persistent court artist, not a campaign noble. Offer a sample and negotiate before payment. "
                + "This audience concerns only the current commission, \"" + template.Title + "\". Do not introduce a subject, passage, attribution question, proposed line, objective, or dilemma from another patronage commission unless the ruler explicitly raises it. Current commission premise: "
                + template.Premise + " Never invent a military victory, a private affair, or a completed native game action. "
                + "When the ruler requests an invented sample, fable, allegory, hypothetical, or other fiction that is not grounded in the supplied history, explicitly label it as fiction before narrating it. Never state or imply that invented events actually happened in the ruler's reign. Past actual works: "
                + string.Join("; ", state.PatronageWorks.Where(w => w.CreatorId == creatorId).Select(w => w.Title).Take(8))
                + householdSubjects;
        }

        private IEnumerable<ReignDocketHistoryRecord> PatronageSubjectHistory(ReignPatronageTemplate template)
        {
            ReignRulerDocketState state = EnsureRulerDocketState();
            string scope = ReignBeta.Integration.ReignServerClient.GetCampaignId() + "|"
                + (ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main") + "|" + (Clan.PlayerClan?.Kingdom?.StringId ?? "");
            if (_patronagePublicHistoryScope == scope && _patronagePublicHistoryDay <= CurrentCourtLifeDay())
                foreach (JObject subject in _patronagePublicSubjects.OfType<JObject>().Where(x =>
                    template.RequiredContext == "history" || x.Value<string>("context") == template.RequiredContext))
                    yield return new ReignDocketHistoryRecord { RecordId = subject.Value<string>("eventId") ?? "",
                        Summary = subject.Value<string>("summary") ?? "" };
            // Court History includes private family attention and testimony. Its presence is not public knowledge.
            foreach (ReignDocketHistoryRecord record in state.History.Where(x => x != null).OrderByDescending(x => x.Day))
            {
                if (record.Type == "royal_proclamation" && record.Outcome == "proclaimed" && template.RequiredContext == "history")
                    yield return new ReignDocketHistoryRecord { Summary = "The ruler publicly proclaimed: " + record.Summary
                        + " (The proclamation is verified; any claim within it is the ruler's stated account.)" };
                else if (record.Type == "patronage_completion" && template.RequiredContext == "history")
                {
                    ReignPatronageCommission commission = state.PatronageCommissions.FirstOrDefault(x => x.Completed
                        && x.CommissionId + "_complete" == record.RecordId && x.Objective != ReignPatronageObjective.PersonalWork);
                    if (commission != null && commission.DeliveryReceipts.Count > 0)
                        yield return new ReignDocketHistoryRecord { Summary = commission.CreatorName + " publicly delivered the commissioned work " + commission.Title + "." };
                }
                else if (record.Type == "petition" && record.Outcome == "granted"
                    && (template.RequiredContext == "history" || template.RequiredContext == "aid"))
                    yield return new ReignDocketHistoryRecord { Summary = "The crown granted a public " + record.PetitionKind
                        + " petition for " + record.SettlementName + ". The grant does not prove a later recovery or victory." };
            }
            // Specific historical contexts above require typed native-event subjects, never keywords in testimony.
        }

        public void RefreshPatronageOptions(ReignCourtLifeMatter matter, IEnumerable<string> regionalSettlementIds = null)
        {
            if (matter == null || matter.Source != ReignDocketSource.Patronage || matter.EffectsCommitted) return;
            ReignRulerDocketState state = EnsureRulerDocketState();
            NormalizePatronageState(state);
            string rulerId = Hero.MainHero?.StringId ?? string.Empty;
            var activeObjectives = new HashSet<ReignPatronageObjective>(state.PatronageCommissions
                .Where(x => x.Paid && !x.Completed && x.RulerHeroId == rulerId)
                .Select(x => x.Objective));
            ReignPatronageTemplate template = ReignPatronageCatalog.Find(matter.TemplateId);
            Settlement host = Settlement.Find(matter.SettlementId);
            List<Settlement> settlements = EligiblePatronageSettlements(matter.KingdomId, host);
            List<string> regional = regionalSettlementIds?.Distinct(StringComparer.Ordinal).ToList();
            if (regional == null) regional = settlements.Take(3).Select(x => x.StringId).ToList();
            if (regional.Count != 3 || regional.Any(id => !settlements.Any(s => s.StringId == id))) regional = settlements.Take(3).Select(x => x.StringId).ToList();
            matter.Options.Clear();
            foreach (ReignPatronageObjective objective in Enum.GetValues(typeof(ReignPatronageObjective)))
            foreach (ReignPatronageReach reach in Enum.GetValues(typeof(ReignPatronageReach)))
            {
                if (activeObjectives.Contains(objective)) continue;
                if (!ReignPatronageRules.Supports(template, objective, reach, settlements.Count)) continue;
                List<string> ids = reach == ReignPatronageReach.Regional ? regional : reach == ReignPatronageReach.Kingdom
                    ? settlements.Select(x => x.StringId).ToList() : new List<string> { matter.SettlementId };
                int praiseAudienceCount = objective == ReignPatronageObjective.Praise
                    ? PatronageAudience(matter.KingdomId, reach, ids).Count() : 0;
                if (objective == ReignPatronageObjective.Praise
                    && !ReignPatronageRules.HasEligiblePraiseAudience(praiseAudienceCount)) continue;
                string scope = reach == ReignPatronageReach.Court ? "Court" : reach == ReignPatronageReach.Kingdom ? "Kingdom-wide"
                    : string.Join(", ", ids.Select(id => Settlement.Find(id)?.Name?.ToString() ?? id));
                string benefit = objective == ReignPatronageObjective.Praise ? "+3 noble relation toward the ruler per noble reached"
                    : objective == ReignPatronageObjective.Loyalty ? "+5 settlement loyalty once per settlement reached" : "A personal cultural work; no automatic relationship reward";
                string audience = objective == ReignPatronageObjective.Praise ? " Current eligible audience: " + praiseAudienceCount + " nobles."
                    : objective == ReignPatronageObjective.Loyalty ? " Destination count: " + ids.Count + "." : string.Empty;
                matter.Options.Add(new ReignCourtLifeOption { OptionId = objective.ToString().ToLowerInvariant() + "_" + reach.ToString().ToLowerInvariant(),
                    Label = scope + ": " + objective + " (" + ReignPatronageRules.Cost(reach) + " gold)",
                    Description = benefit + ". Delivery: " + ReignPatronageRules.DeliveryDays(reach, objective) + " day(s)." + audience,
                    TermsJson = new JObject { ["objective"] = objective.ToString(), ["reach"] = reach.ToString(),
                        ["gold"] = ReignPatronageRules.Cost(reach), ["settlementIds"] = new JArray(ids),
                        ["deliveryDays"] = ReignPatronageRules.DeliveryDays(reach, objective) }.ToString(Formatting.None) });
            }
            matter.Options.Add(new ReignCourtLifeOption { OptionId = "decline", Label = "Decline the commission", Description = "No gold is paid." });
            if (!matter.Options.Any(x => x.OptionId == matter.SelectedOptionId))
                matter.SelectedOptionId = string.Empty;
        }

        public bool ApplyPatronageDecision(ReignCourtLifeMatter matter, JObject decision, out string summary)
        {
            summary = string.Empty;
            if (matter == null || matter.Source != ReignDocketSource.Patronage || decision == null) return false;
            if (matter.EffectsCommitted) { summary = matter.DecisionSummary; return true; }
            if (!HasRoyalCommandAccess || Hero.MainHero == null || Clan.PlayerClan?.Kingdom?.StringId != matter.KingdomId)
            { summary = "The ruler must hold court in the kingdom before commissioning this work."; return false; }
            string optionId = decision.Value<string>("optionId") ?? string.Empty;
            ReignCourtLifeOption option = matter.Options.FirstOrDefault(x => x.OptionId == optionId);
            if (option == null) { summary = "Choose a currently quoted commission."; return false; }
            if (optionId == "decline")
            {
                summary = "The proposed commission was declined without payment.";
                matter.EffectsCommitted = true; matter.DecisionSummary = summary;
                matter.State = ReignCourtLifeMatterState.Resolved; return true;
            }
            JObject terms = ParseObject(option.TermsJson);
            if (!Enum.TryParse(terms.Value<string>("objective"), out ReignPatronageObjective objective)
                || !Enum.TryParse(terms.Value<string>("reach"), out ReignPatronageReach reach)) return false;
            List<string> destinations = (terms["settlementIds"] as JArray ?? new JArray()).Values<string>().Distinct().ToList();
            List<Settlement> eligible = EligiblePatronageSettlements(matter.KingdomId, Settlement.Find(matter.SettlementId));
            if (!ReignPatronageRules.Supports(ReignPatronageCatalog.Find(matter.TemplateId), objective, reach, eligible.Count)
                || destinations.Any(id => !eligible.Any(s => s.StringId == id))
                || (reach == ReignPatronageReach.Regional && destinations.Count != 3))
            { summary = "The quoted audience is no longer available. Review the updated destinations before funding."; RefreshPatronageOptions(matter); return false; }
            List<string> praiseRecipients = objective == ReignPatronageObjective.Praise
                ? PatronageAudience(matter.KingdomId, reach, destinations).Select(x => x.StringId).ToList()
                : new List<string>();
            if (objective == ReignPatronageObjective.Praise
                && !ReignPatronageRules.HasEligiblePraiseAudience(praiseRecipients.Count))
            { summary = "No eligible nobles remain in the quoted audience. Review the updated reaches before funding."; RefreshPatronageOptions(matter); return false; }
            ReignRulerDocketState state = EnsureRulerDocketState(); NormalizePatronageState(state);
            if (state.PatronageCommissions.Any(x => x.Paid && !x.Completed && x.Objective == objective && x.RulerHeroId == Hero.MainHero.StringId))
            {
                summary = "An active " + objective.ToString().ToLowerInvariant() + " commission must finish before another is funded.";
                RefreshPatronageOptions(matter);
                return false;
            }
            int cost = ReignPatronageRules.Cost(reach);
            if (Hero.MainHero.Gold < cost) { summary = "The agreed commission requires " + cost + " gold."; return false; }
            string id = matter.MatterId + "_commission";
            if (state.PatronageCommissions.Any(x => x.CommissionId == id)) { summary = "This commission has already been recorded."; return false; }
            var commission = new ReignPatronageCommission { CommissionId = id, MatterId = matter.MatterId,
                CampaignId = matter.CampaignId, TimelineId = matter.TimelineId, ReignId = matter.ReignId,
                KingdomId = matter.KingdomId, RulerHeroId = Hero.MainHero.StringId, CreatorId = matter.Participants[0].ActorId,
                CreatorName = matter.Participants[0].Name, TemplateId = matter.TemplateId, Title = matter.Title,
                Description = PatronageAgreedDescription(matter), Objective = objective, Reach = reach,
                CreatedDay = CurrentCourtLifeDay(), DueDay = CurrentCourtLifeDay() + ReignPatronageRules.DeliveryDays(reach, objective),
                GoldPaid = cost, SettlementIds = destinations };
            if (objective == ReignPatronageObjective.Praise)
                commission.HeroIds = praiseRecipients;
            // Native debit and receipt are synchronous on the campaign thread; save callbacks cannot interleave.
            GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, cost, true);
            commission.Paid = true; state.PatronageCommissions.Add(commission);
            matter.EffectsCommitted = true; matter.State = ReignCourtLifeMatterState.Resolved;
            matter.SelectedOptionId = optionId; matter.ResolutionReceiptId = id + "_payment";
            summary = cost + " gold paid for " + commission.Title + ". " + option.Description;
            matter.DecisionSummary = summary;
            ProcessPatronageCommissions(CurrentCourtLifeDay()); StateChanged?.Invoke(); return true;
        }

        private static List<Settlement> EligiblePatronageSettlements(string kingdomId, Settlement host)
            => Settlement.All.Where(x => x != null && (x.IsTown || x.IsCastle) && x.Town != null
                && x.OwnerClan?.Kingdom?.StringId == kingdomId)
                .OrderBy(x => host == null ? 0f : x.GetPosition2D.DistanceSquared(host.GetPosition2D)).ThenBy(x => x.StringId, StringComparer.Ordinal).ToList();

        private IEnumerable<Hero> PatronageAudience(string kingdomId, ReignPatronageReach reach, List<string> destinations)
        {
            HashSet<string> courtAttendees = new HashSet<string>(StringComparer.Ordinal);
            if (reach == ReignPatronageReach.Court)
            {
                CastleRoomSessionRecord room = GetCastleRoomSession(CastleRoom.ThroneRoom);
                foreach (string id in (room?.OccupantHeroIdsCsv ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) courtAttendees.Add(id);
            }
            return Hero.AllAliveHeroes.Where(x => x != null && x.IsLord && x.IsActive && !x.IsPrisoner && x != Hero.MainHero
                && x.Clan?.Kingdom?.StringId == kingdomId && x.Age >= (TaleWorlds.CampaignSystem.Campaign.Current?.Models.AgeModel.HeroComesOfAge ?? 18))
                .Where(x => reach == ReignPatronageReach.Kingdom
                    || (reach == ReignPatronageReach.Court ? courtAttendees.Contains(x.StringId) && destinations.Contains(x.CurrentSettlement?.StringId ?? string.Empty)
                    : destinations.Contains(x.CurrentSettlement?.StringId ?? string.Empty) || destinations.Contains(x.HomeSettlement?.StringId ?? string.Empty)))
                .GroupBy(x => x.StringId).Select(x => x.First()).OrderBy(x => x.StringId, StringComparer.Ordinal);
        }

        public void ProcessPatronageCommissions(double now)
        {
            if (!_patronagePublicHistoryPending && (_patronagePublicHistoryDay > now || Math.Floor(_patronagePublicHistoryDay) != Math.Floor(now)))
                _ = PreparePatronagePublicHistoryAsync();
            ReignRulerDocketState state = EnsureRulerDocketState(); NormalizePatronageState(state);
            foreach (ReignPatronageCommission commission in state.PatronageCommissions.Where(x => x.Paid && !x.Completed && x.DueDay <= now).ToList())
            {
                if (commission.Objective == ReignPatronageObjective.Loyalty)
                {
                    foreach (string id in commission.SettlementIds)
                    {
                        string receipt = "settlement:" + id;
                        if (commission.DeliveryReceipts.Contains(receipt) || commission.ExcludedRecipientIds.Contains(receipt)) continue;
                        Settlement settlement = Settlement.Find(id);
                        if (settlement?.Town == null || settlement.OwnerClan?.Kingdom?.StringId != commission.KingdomId)
                        { commission.ExcludedRecipientIds.Add(receipt); continue; }
                        float before = settlement.Town.Loyalty;
                        settlement.Town.Loyalty = ReignPatronageRules.ApplyLoyalty(before);
                        commission.NativeDeliveryEffects.Add(new ReignPatronageDeliveryEffect { ReceiptId = commission.CommissionId + "_" + receipt,
                            RecipientId = id, Stat = "settlement_loyalty", Before = before, After = settlement.Town.Loyalty, WorldDay = now });
                        commission.DeliveryReceipts.Add(receipt);
                    }
                }
                else if (commission.Objective == ReignPatronageObjective.Praise)
                {
                    foreach (string id in commission.HeroIds)
                    {
                        string receipt = "hero:" + id;
                        if (commission.DeliveryReceipts.Contains(receipt) || commission.ExcludedRecipientIds.Contains(receipt)) continue;
                        Hero hero = FindHero(id);
                        if (hero == null || !hero.IsAlive || hero.Clan?.Kingdom?.StringId != commission.KingdomId)
                        { commission.ExcludedRecipientIds.Add(receipt); continue; }
                        string adjustmentId = commission.CommissionId + "_praise_" + id;
                        ReignDirectionalRelationAdjustment adjustment = state.PendingRelationAdjustments.FirstOrDefault(x => x.AdjustmentId == adjustmentId);
                        if (adjustment == null)
                        {
                            adjustment = new ReignDirectionalRelationAdjustment { AdjustmentId = adjustmentId, ObserverHeroId = id,
                                SubjectHeroId = commission.RulerHeroId, Delta = ReignPatronageRules.PraiseRelation,
                                Reason = "Heard the funded work: " + commission.Title };
                            state.PendingRelationAdjustments.Add(adjustment);
                        }
                        if (adjustment.Skipped) commission.ExcludedRecipientIds.Add(receipt);
                        else if (adjustment.Applied) commission.DeliveryReceipts.Add(receipt);
                    }
                    if (commission.DeliveryReceipts.Count + commission.ExcludedRecipientIds.Count < commission.HeroIds.Count) continue;
                }
                string workId = commission.CommissionId + "_work";
                if (!state.PatronageWorks.Any(x => x.WorkId == workId)) state.PatronageWorks.Add(new ReignPatronageWork {
                    WorkId = workId, CommissionId = commission.CommissionId, CreatorId = commission.CreatorId,
                    PatronHeroId = commission.RulerHeroId, Title = commission.Title, Description = commission.Description, CompletedDay = now });
                commission.Completed = true;
                commission.CompletionReport = commission.CreatorName + " completed " + commission.Title + ". "
                    + (commission.Objective == ReignPatronageObjective.PersonalWork ? "The commissioned work is preserved in Court History."
                    : commission.DeliveryReceipts.Count + (commission.Objective == ReignPatronageObjective.Loyalty ? " settlements received +5 loyalty once." : " nobles received +3 relation toward the ruler."))
                    + (commission.ExcludedRecipientIds.Count > 0 ? " " + commission.ExcludedRecipientIds.Count + " original recipients were no longer eligible." : string.Empty);
                state.History.Add(new ReignDocketHistoryRecord { RecordId = commission.CommissionId + "_complete", ReignId = commission.ReignId,
                    Type = "patronage_completion", Outcome = "completed", PetitionId = commission.MatterId, Day = (int)Math.Floor(now),
                    PetitionerName = commission.CreatorName, Summary = commission.CompletionReport, Detail = JsonConvert.SerializeObject(commission) });
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + commission.CompletionReport));
                StateChanged?.Invoke();
            }
        }

        private static string PatronageAgreedDescription(ReignCourtLifeMatter matter)
        {
            var player = matter.ConversationLines.LastOrDefault(x => x.Role == "user");
            var creator = player == null ? null : matter.ConversationLines
                .SkipWhile(x => !ReferenceEquals(x, player)).LastOrDefault(x => x.Role == "assistant");
            return ReignPatronageRules.AgreedWorkDescription(matter.Title, player?.Text, creator?.Text);
        }

        private static void NormalizePatronageState(ReignRulerDocketState state)
        {
            state.PatronageCommissions = (state.PatronageCommissions ?? new List<ReignPatronageCommission>()).Where(x => x != null).ToList();
            state.PatronageWorks = (state.PatronageWorks ?? new List<ReignPatronageWork>()).Where(x => x != null).ToList();
            state.PatronageCreators = (state.PatronageCreators ?? new List<ReignPatronageCreator>()).Where(x => x != null).ToList();
            foreach (var matter in state.CourtLifeMatters.Where(x => x != null && x.Source == ReignDocketSource.Patronage))
            {
                var template = ReignPatronageCatalog.Find(matter.TemplateId);
                if (template == null) continue;
                if (matter.Summary == template.Premise)
                {
                    matter.Summary = ReignPatronageRules.PublicSummary(template);
                    // Only replace the old generated clerk introduction, never player or creator testimony.
                    foreach (var line in matter.ConversationLines.Where(x => x.Role == "system" && x.Text == template.Premise))
                        line.Text = matter.Summary;
                }
                if (!matter.IsPending) continue;
                foreach (var participant in matter.Participants.Where(x => x != null && x.Role == template.CreatorRole))
                    participant.PrivateContext = BuildPatronagePrivateContext(template, state, participant.ActorId, Hero.MainHero);
            }
            foreach (ReignPatronageCommission c in state.PatronageCommissions)
            {
                var template = ReignPatronageCatalog.Find(c.TemplateId);
                if (template != null && c.Description == template.Premise)
                {
                    var matter = state.CourtLifeMatters.FirstOrDefault(x => x.MatterId == c.MatterId);
                    string restored = matter == null
                        ? ReignPatronageRules.AgreedWorkDescription(c.Title, null, null)
                        : PatronageAgreedDescription(matter);
                    c.Description = restored;
                    foreach (var work in state.PatronageWorks.Where(x => x.CommissionId == c.CommissionId && x.Description == template.Premise))
                        work.Description = restored;
                }
                c.SettlementIds = (c.SettlementIds ?? new List<string>()).Distinct().ToList();
                c.HeroIds = (c.HeroIds ?? new List<string>()).Distinct().ToList();
                c.DeliveryReceipts = (c.DeliveryReceipts ?? new List<string>()).Distinct().ToList();
                c.ExcludedRecipientIds = (c.ExcludedRecipientIds ?? new List<string>()).Distinct().ToList();
                c.NativeDeliveryEffects = (c.NativeDeliveryEffects ?? new List<ReignPatronageDeliveryEffect>()).Where(x => x != null).ToList();
            }
        }
    }
}

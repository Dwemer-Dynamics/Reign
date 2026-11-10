using System;
using System.Globalization;
using System.Linq;
using Reign.Core.Contracts.Actions;

namespace ReignBeta.Actions
{
    public static class ReignActionWording
    {
        // Player-built terms and model counteroffers share this renderer. Automatic
        // native revision bindings are signed but do not clutter the player's sentence.
        public static void Render(ActionAgreement a)
        {
            // A completed receipt keeps the exact presentation that was signed.
            // Live entity labels can change after fulfillment (for example a
            // civil-war movement becomes resolved); re-rendering would alter
            // consent even though the saved executable terms are unchanged.
            var completed = ReignAgreementBehavior.Instance?.Agreements
                .FirstOrDefault(j => j.Agreement.Id == a.Id);
            if (AgreementRules.IsExactCompletedReplay(a, completed)) return;
            ReignActionService.BindGuestReviews(a);
            ReignSpouseSeparationAction.Bind(a);
            ReignBeta.Government.ReignGovernmentCampaignBehavior.Instance?.BindPetitionRevisions(a);
            ReignBeta.Campaign.ReignNpcLivesBehavior.Instance?.BindMeetingArrival(a);
            for (int i = 0; i < a.Clauses.Count; i++)
            {
                var c = a.Clauses[i]; var d = ReignActionCatalog.Find(c.ActionId)
                    ?? throw new InvalidOperationException("Unknown action: " + c.ActionId);
                bool player = c.ActorId == a.PlayerId;
                string subject = ReignActionLanguage.Subject(player, i);
                string political = ReignBeta.Government.ReignGovernmentCampaignBehavior.Instance?.GovernmentActionSentence(c, subject) ?? "";
                if (c.ActionId == "marriage.propose")
                    c.DisplayText = "I propose that " + ReignActionService.HeroById(ReignActionService.Arg(c, "playerMatchId"))?.Name
                        + " marry " + ReignActionService.HeroById(ReignActionService.Arg(c, "prospectId"))?.Name;
                else if (c.ActionId == "marriage.appeal")
                    c.DisplayText = "I ask " + ReignActionService.HeroById(c.RecipientId)?.Name + " to call off the wedding because " + ReignActionService.Arg(c, "reason");
                else if (c.ActionId == "marriage.release_prisoner")
                    c.DisplayText = subject + "release " + ReignActionService.HeroById(ReignActionService.Arg(c, "assetId"))?.Name;
                else if (political.Length > 0) c.DisplayText = political;
                else if (c.ActionId.StartsWith("npc_life.", StringComparison.Ordinal))
                    c.DisplayText = ReignBeta.Campaign.ReignNpcLivesBehavior.Instance.LifeActionSentence(a, c);
                else if (c.ActionId == ReignSpouseSeparation.ActionId)
                    c.DisplayText = subject + "write to " + ReignActionService.HeroById(ReignActionService.Arg(c, "spouseId"))?.Name
                        + " that your marriage is over and end it";
                else if (c.ActionId == "expose_concealed_pregnancy")
                    c.DisplayText = ReignBeta.Campaign.ReignFamilyCampaignBehavior.Instance.PregnancyExposureSentence(
                        ReignActionService.Arg(c, "pregnancyId"), ReignActionService.Arg(c, "disclosure") == "name_father");
                else if (c.ActionId == "start_ruling_clan_rebellion")
                    c.DisplayText = "I declare rebellion against your rule of " + Label(d.Parameters.First(p => p.Key == "actorKingdomId"), c)
                        + ", as leader of " + Label(d.Parameters.First(p => p.Key == "actorClanId"), c);
                else if (c.ActionId == "pledge_rebellion_support")
                    c.DisplayText = subject + "support me when I declare rebellion against "
                        + Label(d.Parameters.First(p => p.Key == "terms.challengedRulerId"), c) + ", ruler of "
                        + Label(d.Parameters.First(p => p.Key == "actorKingdomId"), c);
                else if (c.ActionId.StartsWith("give.", StringComparison.Ordinal))
                {
                    string quantity = ReignActionService.Arg(c, "quantity", "1");
                    string asset = c.ActionId == "give.gold" ? "denars" : Label(d.Parameters.First(p => p.Key == "assetId"), c);
                    c.DisplayText = subject + "give " + (player ? "you " : "me ") + quantity + " " + asset;
                }
                else
                {
                    var details = d.Parameters.Where(p => !p.Automatic && c.Arguments.ContainsKey(p.Key))
                        .Select(p => p.Label + ": " + Label(p, c));
                    c.DisplayText = subject + ReignActionLanguage.Verb(d, player)
                        + (details.Any() ? " (" + string.Join("; ", details) + ")" : "");
                }
                if (c.DueDay > 0) c.DisplayText += " on or after campaign day " + Number(c.DueDay);
                if (c.DurationDays > 0) c.DisplayText += " for " + Number(c.DurationDays) + " days";
                if (c.Installments > 1) c.DisplayText += ", " + c.Installments + " installments every " + Number(c.IntervalDays) + " days";
                if (c.ConditionKind.Length > 0) c.DisplayText += ", after " + c.ConditionKind + (c.ConditionTargetId.Length > 0 ? " of " + ConditionLabel(c) : "");
            }
            if (a.AdvancePayment && a.Clauses.Count > 0) a.Clauses[0].DisplayText += " (payment in advance)";
        }

        private static string Label(ActionParameter p, ActionClause c)
        {
            string value = ReignActionService.Arg(c, p.Key);
            if (p.Kind == "quantity" || p.Kind == "text" || p.Choices.Length > 0) return value;
            var row = ReignActionEntities.Find(p.Kind, c.ActorId, "", "", c).FirstOrDefault(r => r.Id == value
                && (p.Kind != "item" || new[] { "variantId", "equipmentSet", "equipmentSlot" }.All(k =>
                    (r.Arguments.TryGetValue(k, out var actual) ? actual : "") == ReignActionService.Arg(c, k))));
            return row?.Label ?? value;
        }
        private static string ConditionLabel(ActionClause c)
        {
            string kind = c.ConditionKind == "release" ? "hero" : c.ConditionKind == "war" || c.ConditionKind == "peace" ? "kingdom" : "settlement";
            return ReignActionEntities.Find(kind, c.ActorId).FirstOrDefault(x => x.Id == c.ConditionTargetId)?.Label ?? c.ConditionTargetId;
        }
        private static string Number(double n) => n.ToString("0.##", CultureInfo.InvariantCulture);
    }
}

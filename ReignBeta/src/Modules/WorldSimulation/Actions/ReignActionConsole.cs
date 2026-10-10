using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Reign.Core.Contracts.Actions;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace ReignBeta.Actions
{
    public static class ReignActionConsole
    {
        private static readonly Dictionary<string, ActionAgreement> Drafts = new Dictionary<string, ActionAgreement>(StringComparer.Ordinal);
        private static TaleWorlds.CampaignSystem.Campaign _draftCampaign;
        private static string Guard(Func<string> run)
        {
            try { return run(); } catch (Exception ex) { return "Reign action: " + ex.Message; }
        }
        private static void RequireCampaign()
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current == null || ReignAgreementBehavior.Instance == null) throw new InvalidOperationException("Load a campaign first.");
            if (_draftCampaign != TaleWorlds.CampaignSystem.Campaign.Current) { Drafts.Clear(); _draftCampaign = TaleWorlds.CampaignSystem.Campaign.Current; }
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("list", "reign.action")]
        public static string List(List<string> args) => Guard(() =>
        {
            string filter = string.Join(" ", args ?? new List<string>());
            return string.Join("\n", ReignActionCatalog.All.Where(d => filter.Length == 0 || (d.Id + " " + d.Family + " " + d.Label).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(d => d.Id, StringComparer.Ordinal).Select(d => d.Id + " — " + d.Description)
                .Concat(ReignNativeActionCatalog.All.Where(d => filter.Length == 0 || d.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(d => d.Id + " — native operator " + (d.ReturnType == "void" ? "operation" : "query"))));
        });

        [CommandLineFunctionality.CommandLineArgumentFunction("help", "reign.action")]
        public static string Help(List<string> args) => Guard(() =>
        {
            if (args == null || args.Count == 0) return "reign.action.list [filter]\nreign.action.help <actionId>\nreign.entity.find <kind> [--owner=<heroId>] [--query=<text>]\nreign.action.preview|run <actionId> --actor=<exactHeroId> --recipient=<exactHeroId> [--parameter=value]\nreign.action.status|cancel <operationId>\nreign.agreement.create --actor=<heroId> --recipient=<heroId>\nreign.agreement.add <draftId> <actionId> --side=actor|recipient [--parameter=value]\nreign.agreement.remove <draftId> <clauseId>\nreign.agreement.preview|run <draftId>";
            var native = ReignNativeActionCatalog.Find(args[0]); if (native != null) return ReignNativeActionConsole.Help(native);
            var d = ReignActionCatalog.Find(args[0]) ?? throw new InvalidOperationException("Unknown action ID.");
            return d.Id + " — " + d.Description + "\n--actor=<exactHeroId> --recipient=<exactHeroId>\n"
                + string.Join("\n", d.Parameters.Select(p => "--" + p.Key + "=<" + p.Kind + ">" + (p.Required ? " required" : " optional")))
                + "\nConsole execution is an explicit operator action. It does not record NPC consent. Preview does not mutate campaign state.";
        });

        [CommandLineFunctionality.CommandLineArgumentFunction("find", "reign.entity")]
        public static string Find(List<string> args) => Guard(() =>
        {
            RequireCampaign(); if (args == null || args.Count == 0) return "Supply an entity kind: hero, clan, kingdom, party, settlement, item, troop, prisoner, workshop, ship, politicalParty, governmentBusiness.";
            var p = Parse(args.Skip(1));
            return string.Join("\n", ReignActionEntities.Find(args[0], Get(p, "owner", Hero.MainHero.StringId), Get(p, "query"), Get(p, "category"))
                .Select(r => r.Id + " — " + r.Label + (r.Quantity > 0 ? " x" + r.Quantity : "") + (r.Arguments.Count > 0 ? " " + JsonConvert.SerializeObject(r.Arguments) : "") + (r.Available ? "" : " [unavailable: " + r.Reason + "]")));
        });

        [CommandLineFunctionality.CommandLineArgumentFunction("preview", "reign.action")]
        public static string Preview(List<string> args) => Guard(() => Native(args, false) ?? RenderPreview(Single(args)));
        [CommandLineFunctionality.CommandLineArgumentFunction("run", "reign.action")]
        public static string Run(List<string> args) => Guard(() => Native(args, true) ?? JsonConvert.SerializeObject(ReignActionService.RunOperator(Single(args)), Formatting.Indented));
        [CommandLineFunctionality.CommandLineArgumentFunction("status", "reign.action")]
        public static string Status(List<string> args) => Guard(() => { RequireCampaign(); string id = Only(args); return JsonConvert.SerializeObject((object)ReignAgreementBehavior.Instance.NativeOperation(id) ?? ReignAgreementBehavior.Instance.Status(id), Formatting.Indented); });
        [CommandLineFunctionality.CommandLineArgumentFunction("cancel", "reign.action")]
        public static string Cancel(List<string> args) => Guard(() => { RequireCampaign(); return JsonConvert.SerializeObject(ReignAgreementBehavior.Instance.Cancel(Only(args)), Formatting.Indented); });

        [CommandLineFunctionality.CommandLineArgumentFunction("create", "reign.agreement")]
        public static string Create(List<string> args) => Guard(() =>
        {
            RequireCampaign(); var p = Parse(args); var a = New(p); Drafts.Add(a.Id, a); return "Draft " + a.Id + ". Add one or more terms, then preview or run it.";
        });
        [CommandLineFunctionality.CommandLineArgumentFunction("add", "reign.agreement")]
        public static string Add(List<string> args) => Guard(() =>
        {
            RequireCampaign(); if (args.Count < 2) throw new InvalidOperationException("Supply a draft ID and action ID.");
            var a = Draft(args[0]); var p = Parse(args.Skip(2)); string side = Get(p, "side", "actor");
            if (side != "actor" && side != "recipient") throw new InvalidOperationException("--side must be actor or recipient.");
            var c = Clause(args[1], p, side == "actor" ? a.PlayerId : a.NpcId, side == "actor" ? a.NpcId : a.PlayerId);
            if (a.Clauses.Count >= AgreementRules.MaximumClauses) throw new InvalidOperationException("The agreement already has 64 terms.");
            a.Clauses.Add(c); a.Revise(); return "Added " + c.Id + " to " + a.Id + ".";
        });
        [CommandLineFunctionality.CommandLineArgumentFunction("remove", "reign.agreement")]
        public static string Remove(List<string> args) => Guard(() =>
        {
            RequireCampaign(); if (args.Count != 2) throw new InvalidOperationException("Supply the draft ID and exact clause ID.");
            var a = Draft(args[0]); var c = a.Clauses.SingleOrDefault(x => x.Id == args[1]);
            if (c == null) throw new InvalidOperationException("Unknown clause ID."); a.Clauses.Remove(c); a.Revise(); return "Removed " + c.Id + ".";
        });
        [CommandLineFunctionality.CommandLineArgumentFunction("preview", "reign.agreement")]
        public static string PreviewDraft(List<string> args) => Guard(() => RenderPreview(Draft(Only(args))));
        [CommandLineFunctionality.CommandLineArgumentFunction("run", "reign.agreement")]
        public static string RunDraft(List<string> args) => Guard(() =>
        {
            RequireCampaign(); var a = Draft(Only(args)); var receipt = ReignActionService.RunOperator(a); Drafts.Remove(a.Id); return JsonConvert.SerializeObject(receipt, Formatting.Indented);
        });

        private static string RenderPreview(ActionAgreement a)
        {
            RequireCampaign(); var errors = ReignActionService.Preview(a);
            return JsonConvert.SerializeObject(new { valid = errors.Count == 0, errors, agreement = a, termsHash = a.TermsHash() }, Formatting.Indented);
        }
        private static string Native(List<string> args, bool run)
        {
            if (args == null || args.Count == 0 || !args[0].StartsWith("native.", StringComparison.Ordinal)) return null;
            RequireCampaign(); return ReignNativeActionConsole.Execute(args[0], Parse(args.Skip(1)), run).ToString(Formatting.Indented);
        }
        private static ActionAgreement Single(List<string> args)
        {
            RequireCampaign(); if (args == null || args.Count == 0) throw new InvalidOperationException("Supply an action ID; use reign.action.help.");
            var p = Parse(args.Skip(1)); var a = New(p); a.Clauses.Add(Clause(args[0], p, a.PlayerId, a.NpcId)); return a;
        }
        private static ActionAgreement New(Dictionary<string, string> p)
        {
            string actor = Get(p, "actor"), recipient = Get(p, "recipient");
            if (ReignActionService.HeroById(actor) == null || ReignActionService.HeroById(recipient) == null || actor == recipient) throw new InvalidOperationException("Supply --actor and --recipient as distinct, exact living hero IDs. Use reign.entity.find hero to look them up.");
            return new ActionAgreement { PlayerId = actor, NpcId = recipient, ProposerId = actor, CreatedDay = ReignActionService.Now, Origin = ActionOrigin.Console };
        }
        private static ActionClause Clause(string actionId, Dictionary<string, string> p, string actor, string recipient)
        {
            var d = ReignActionCatalog.Find(actionId) ?? throw new InvalidOperationException("Unknown action ID: " + actionId);
            var c = new ActionClause { ActionId = actionId, ActorId = actor, RecipientId = recipient, DisplayText = d.Label };
            foreach (var kv in p)
            {
                if (new[] { "actor", "recipient", "side" }.Contains(kv.Key)) continue;
                if (!d.Parameters.Any(x => x.Key == kv.Key)) throw new InvalidOperationException("Unknown parameter --" + kv.Key + "; use reign.action.help " + actionId + ".");
                c.Arguments.Add(kv.Key, kv.Value);
            }
            return c;
        }
        private static ActionAgreement Draft(string id) { RequireCampaign(); return Drafts.TryGetValue(id, out var a) ? a : throw new InvalidOperationException("Unknown draft ID."); }
        private static string Only(List<string> args) => args != null && args.Count == 1 ? args[0] : throw new InvalidOperationException("Supply exactly one operation or draft ID.");
        private static string Get(Dictionary<string, string> p, string key, string fallback = "") => p.TryGetValue(key, out var value) ? value : fallback;

        internal static Dictionary<string, string> Parse(IEnumerable<string> words)
        {
            // Native console tokenization splits on spaces. Reassemble explicitly quoted values
            // without interpreting escapes, expressions, paths, scripts or console commands.
            var tokens = new List<string>(); var current = new StringBuilder(); char quote = '\0';
            foreach (char ch in string.Join(" ", words ?? Enumerable.Empty<string>()))
            {
                if (quote != '\0') { if (ch == quote) quote = '\0'; else current.Append(ch); }
                else if (ch == '\'' || ch == '"') quote = ch;
                else if (char.IsWhiteSpace(ch)) { if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); } }
                else current.Append(ch);
            }
            if (quote != '\0') throw new InvalidOperationException("Unclosed quoted argument.");
            if (current.Length > 0) tokens.Add(current.ToString());
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string token in tokens)
            {
                int equals = token.IndexOf('=');
                if (!token.StartsWith("--", StringComparison.Ordinal) || equals <= 2) throw new InvalidOperationException("Use named arguments: --name=value.");
                string key = token.Substring(2, equals - 2);
                if (result.ContainsKey(key)) throw new InvalidOperationException("Duplicate parameter: " + key);
                result.Add(key, token.Substring(equals + 1));
            }
            return result;
        }
    }
}

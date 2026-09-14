using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace ReignBeta.Government
{
    public sealed class ReignGovernmentCommitmentRecord
    {
        public string ReceiptId { get; set; } = "";
        public string SessionId { get; set; } = "";
        public string PlayerConsentQuote { get; set; } = "";
        public string MemberConsentQuote { get; set; } = "";
        public string BusinessId { get; set; } = "";
        public string MemberHeroStringId { get; set; } = "";
        public string RulerHeroStringId { get; set; } = "";
        public string OptionId { get; set; } = "";
        public string Method { get; set; } = "";
        public string ObligationId { get; set; } = "";
        public string Terms { get; set; } = "";
        public string Status { get; set; } = "active";
        public int GoldPaid { get; set; }
        public float AcceptedDay { get; set; }
        public float ExpiresDay { get; set; }
    }

    public sealed partial class ReignGovernmentCampaignBehavior
    {
        private List<ReignGovernmentCommitmentRecord> _governmentCommitments = new List<ReignGovernmentCommitmentRecord>();
        // A receipt can only originate in a currently reserved, actual individual conversation turn.
        private readonly Dictionary<string, JObject> _governmentConversationTurns = new Dictionary<string, JObject>(StringComparer.Ordinal);
        private readonly HashSet<string> _governmentPublicTurns = new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyList<ReignGovernmentCommitmentRecord> GetGovernmentCommitments(string kingdomId)
            => _governmentCommitments.Where(x => _business.Any(b => Same(b.BusinessId, x.BusinessId) && Same(b.KingdomStringId, kingdomId))).ToList();

        public JObject BuildGovernmentPrivateConversationContext(Hero member, string sessionId, string playerText)
        {
            var cases = member == null ? new List<ReignGovernmentBusinessRecord>() : _business
                .Where(x => CanDiscussGovernmentBusinessPrivately(x.BusinessId, member.StringId, out _)).Take(12).ToList();
            if (cases.Count == 0 || string.IsNullOrWhiteSpace(playerText)) return new JObject { ["enabled"] = false };
            string receiptId = "government_private_" + Guid.NewGuid().ToString("N");
            var rows = new JArray(cases.Select(b => new JObject
            {
                ["businessId"] = b.BusinessId, ["title"] = b.Title, ["summary"] = b.Summary,
                ["options"] = GetPublicBusinessOptions(b),
                ["rulerRecommendation"] = b.RecommendedOptionId, ["stage"] = b.Status,
                ["expiresDay"] = b.Postponed ? b.RecessUntilDay : CurrentDay() + 7f
            }));
            var obligations = new JArray(_resolutions.Where(x => cases.Any(b => Same(b.KingdomStringId, x.KingdomStringId))
                && (x.Status == "active" || x.Status == "completed")).Take(24).Select(x => new JObject
                { ["obligationId"] = x.ResolutionId, ["title"] = ReignGovernmentResolutionCatalog.Find(x.TemplateId)?.Title ?? x.TemplateId, ["status"] = x.Status }));
            var context = new JObject { ["enabled"] = true, ["receiptId"] = receiptId, ["sessionId"] = sessionId ?? "",
                ["memberHeroId"] = member.StringId, ["rulerHeroId"] = Hero.MainHero.StringId,
                ["playerText"] = playerText.Length > 4000 ? playerText.Substring(0, 4000) : playerText,
                ["business"] = rows, ["verifiableObligations"] = obligations,
                ["instructions"] = "This is an actual private conversation, not a hearing. Decide freely whether to agree; a request is not consent. "
                    + "A scoped accepted promise may concern one listed option in one listed matter. Persuasion costs no gold. A bribe requires an exact mutually accepted gold amount. "
                    + "A deal must name a listed verifiable obligation and gives no support before that obligation is fulfilled. Never invent a completed action or relationship value." };
            _governmentConversationTurns[receiptId] = (JObject)context.DeepClone();
            while (_governmentConversationTurns.Count > 32) _governmentConversationTurns.Remove(_governmentConversationTurns.Keys.First());
            return context;
        }

        public JObject ApplyGovernmentPrivateConversationResult(JObject context, JObject response)
        {
            string receiptId = context?.Value<string>("receiptId") ?? "";
            if (!_governmentConversationTurns.TryGetValue(receiptId, out var reserved)) return new JObject { ["ok"] = false, ["error"] = "No active individual conversation receipt." };
            // Consume even rejected turns; retries cannot re-use consent or charge gold twice.
            _governmentConversationTurns.Remove(receiptId);
            if (response?.Value<bool?>("ok") != true) return new JObject { ["ok"] = false, ["error"] = "The conversation did not complete." };
            JObject accepted = response["governmentCommitment"] as JObject;
            if (accepted == null || accepted["accepted"]?.Type != JTokenType.Boolean || accepted.Value<bool>("accepted") != true)
                return new JObject { ["ok"] = true, ["recorded"] = false };
            foreach (string key in new[] { "receiptId", "sessionId", "memberHeroId", "rulerHeroId", "businessId", "optionId",
                "method", "obligationId", "playerConsentQuote", "memberConsentQuote" })
                if (accepted[key] != null && accepted[key].Type != JTokenType.Null
                    && (accepted[key].Type != JTokenType.String || accepted[key].ToString().Length > 4000))
                    return new JObject { ["ok"] = false, ["error"] = "The agreement contains malformed or oversized terms." };
            string playerQuote = accepted.Value<string>("playerConsentQuote") ?? "";
            string memberQuote = accepted.Value<string>("memberConsentQuote") ?? "";
            string playerText = reserved.Value<string>("playerText") ?? "";
            string reply = response.Value<string>("reply") ?? "";
            if (accepted.Value<string>("receiptId") != receiptId || playerQuote.Length < 5 || memberQuote.Length < 5
                || playerText.IndexOf(playerQuote, StringComparison.Ordinal) < 0 || reply.IndexOf(memberQuote, StringComparison.Ordinal) < 0)
                return new JObject { ["ok"] = false, ["error"] = "Both parties' explicit agreement must be evidenced in this private exchange." };
            string businessId = accepted.Value<string>("businessId") ?? "";
            string optionId = accepted.Value<string>("optionId") ?? "";
            string method = accepted.Value<string>("method") ?? "";
            var goldToken = accepted["gold"];
            int gold = 0;
            if (goldToken != null && goldToken.Type != JTokenType.Null
                && (goldToken.Type != JTokenType.Integer || !int.TryParse(goldToken.ToString(), out gold)))
                return new JObject { ["ok"] = false, ["error"] = "The agreed payment must be an exact whole gold amount within the supported range." };
            string obligationId = accepted.Value<string>("obligationId") ?? "";
            var offered = (reserved["business"] as JArray)?.OfType<JObject>().FirstOrDefault(x => x.Value<string>("businessId") == businessId);
            if (offered == null || (offered["options"] as JArray)?.OfType<JObject>().Any(x => x.Value<string>("id") == optionId) != true)
                return new JObject { ["ok"] = false, ["error"] = "The agreement does not match a discussed matter and option." };
            var proof = new GovernmentPrivateReceiptProof
            {
                Accepted = accepted.Value<bool?>("accepted") == true,
                ReceiptId = accepted.Value<string>("receiptId"), ExpectedReceiptId = receiptId,
                SessionId = accepted.Value<string>("sessionId"), ExpectedSessionId = reserved.Value<string>("sessionId"),
                MemberHeroId = accepted.Value<string>("memberHeroId"), ExpectedMemberHeroId = reserved.Value<string>("memberHeroId"),
                RulerHeroId = accepted.Value<string>("rulerHeroId"), ExpectedRulerHeroId = reserved.Value<string>("rulerHeroId"),
                BusinessId = businessId, ExpectedBusinessId = offered.Value<string>("businessId"),
                PlayerText = playerText, MemberReply = reply, PlayerConsentQuote = playerQuote, MemberConsentQuote = memberQuote,
                OptionId = optionId, AllowedOptionIds = ((JArray)offered["options"]).OfType<JObject>().Select(x => x.Value<string>("id")),
                Method = method, Gold = gold, ObligationId = obligationId,
                VerifiableObligationIds = ((JArray)reserved["verifiableObligations"]).OfType<JObject>().Select(x => x.Value<string>("obligationId"))
            };
            if (!GovernmentPrivateConversationEligibility.ValidateReceipt(proof, out string proofError))
                return new JObject { ["ok"] = false, ["error"] = proofError };
            if (method == "deal" && !(reserved["verifiableObligations"] as JArray).OfType<JObject>().Any(x => x.Value<string>("obligationId") == obligationId))
                return new JObject { ["ok"] = false, ["error"] = "That obligation cannot be verified." };
            // The typed receipt is transient and owned here; arbitrary world actions cannot mint it.
            reserved["validatedCommitment"] = accepted.DeepClone();
            _governmentConversationTurns[receiptId] = reserved;
            bool ok;
            string result;
            try { ok = RecordGovernmentCommitment(FindHero(reserved.Value<string>("memberHeroId")), FindHero(reserved.Value<string>("rulerHeroId")),
                businessId, optionId, method, receiptId, gold, obligationId, out result); }
            finally { _governmentConversationTurns.Remove(receiptId); }
            if (ok)
            {
                var record = _governmentCommitments.First(x => x.ReceiptId == receiptId);
                record.Terms = playerQuote + " / " + memberQuote;
                record.PlayerConsentQuote = playerQuote;
                record.MemberConsentQuote = memberQuote;
            }
            return new JObject { ["ok"] = ok, ["recorded"] = ok, ["summary"] = result };
        }

        public bool RecordGovernmentCommitment(Hero member, Hero ruler, string businessId, string optionId, string method,
            string consentReceiptId, int gold, string obligationId, out string result)
        {
            result = "This commitment requires a current, mutually accepted individual conversation.";
            var existing = _governmentCommitments.FirstOrDefault(x => x.ReceiptId == consentReceiptId);
            if (existing != null)
            {
                bool same = existing.MemberHeroStringId == member?.StringId && existing.RulerHeroStringId == ruler?.StringId
                    && existing.BusinessId == businessId && existing.OptionId == optionId && existing.Method == method
                    && existing.GoldPaid == gold && existing.ObligationId == (obligationId ?? "");
                result = same ? "This agreement has already been recorded." : "This receipt belongs to different agreed terms.";
                return same;
            }
            if (!_governmentConversationTurns.TryGetValue(consentReceiptId ?? "", out var receipt)
                || member == null || ruler != Hero.MainHero || receipt.Value<string>("memberHeroId") != member.StringId
                || receipt.Value<string>("rulerHeroId") != ruler.StringId || !CanDiscussGovernmentBusinessPrivately(businessId, member.StringId, out result)) return false;
            var accepted = receipt["validatedCommitment"] as JObject;
            if (accepted == null || accepted.Value<string>("businessId") != businessId || accepted.Value<string>("optionId") != optionId
                || accepted.Value<string>("method") != method || (accepted.Value<int?>("gold") ?? 0) != gold
                || (accepted.Value<string>("obligationId") ?? "") != (obligationId ?? "")) return false;
            var business = _business.FirstOrDefault(x => Same(x.BusinessId, businessId));
            if (!BusinessOptions(business).Any(x => x.Value<string>("id") == optionId)) return false;
            if (method != "persuasion" && method != "bribe" && method != "deal") { result = "Unknown agreement terms."; return false; }
            if (method == "bribe" && (gold <= 0 || ruler.Gold < gold)) { result = "The agreed payment is unavailable."; return false; }
            if (method != "bribe" && gold != 0) { result = "Only an explicitly agreed bribe may transfer gold."; return false; }
            if (method == "deal" && !_resolutions.Any(x => Same(x.ResolutionId, obligationId) && Same(x.KingdomStringId, business.KingdomStringId)
                && (x.Status == "active" || x.Status == "completed"))) { result = "The agreed obligation is unavailable."; return false; }
            var commitment = new ReignGovernmentCommitmentRecord { ReceiptId = consentReceiptId, SessionId = receipt.Value<string>("sessionId") ?? "", BusinessId = businessId,
                MemberHeroStringId = member.StringId, RulerHeroStringId = ruler.StringId, OptionId = optionId, Method = method,
                ObligationId = method == "deal" ? obligationId : "", AcceptedDay = CurrentDay(),
                ExpiresDay = business.Postponed ? business.RecessUntilDay : CurrentDay() + 7f };
            if (method == "bribe") { GiveGoldAction.ApplyBetweenCharacters(ruler, member, gold, false); commitment.GoldPaid = gold; }
            foreach (var old in _governmentCommitments.Where(x => x.Status == "active" && Same(x.BusinessId, businessId) && Same(x.MemberHeroStringId, member.StringId))) old.Status = "superseded";
            _governmentCommitments.Add(commitment);
            result = method == "deal" ? "The conditional promise is recorded; support depends on the obligation being fulfilled before the vote."
                : "The member's promise is recorded for this matter. The vote remains their own.";
            ChangedBusiness(business);
            return true;
        }

        public int GetGovernmentCommitmentShift(string businessId, string memberId, string optionId)
        {
            var record = _governmentCommitments.LastOrDefault(x => x.Status == "active" && Same(x.BusinessId, businessId)
                && Same(x.MemberHeroStringId, memberId) && Same(x.OptionId, optionId) && CurrentDay() <= x.ExpiresDay);
            if (record == null || FindKingdom(_business.FirstOrDefault(x => Same(x.BusinessId, businessId))?.KingdomStringId)?.Leader?.StringId != record.RulerHeroStringId) return 0;
            if (record.Method == "deal" && !_resolutions.Any(x => Same(x.ResolutionId, record.ObligationId) && x.Status == "completed")) return 0;
            return record.Method == "bribe" ? 25 : record.Method == "deal" ? 20 : 15;
        }

        public async Task<JObject> SubmitHearingMessageAsync(string businessId, string message)
        {
            JObject payload = await ReignMainThread.InvokeAsync(() => ReservePublicHearingTurn(businessId, message)).ConfigureAwait(false);
            if (payload == null) return new JObject { ["ok"] = false, ["error"] = "This hearing cannot receive a statement now." };
            JObject response;
            try { response = (payload["speakers"] as JArray)?.Count == 0
                    ? new JObject { ["ok"] = true, ["statements"] = new JArray(), ["message"] = "Your statement was entered in the public record. No participants are present to respond.", ["providerCallCount"] = 0 }
                    : await ReignServerClient.RequestGovernmentHearingDiscussionAsync(payload).ConfigureAwait(false); }
            catch (Exception ex) { response = new JObject { ["ok"] = false, ["error"] = ex.Message }; }
            return await ReignMainThread.InvokeAsync(() => ApplyPublicHearingTurn(businessId, payload, response)).ConfigureAwait(false);
        }

        private JObject ReservePublicHearingTurn(string businessId, string message)
        {
            var business = AuthorizedBusiness(businessId, Hero.MainHero, out _);
            if (business == null || business.Status != "hearing" || string.IsNullOrWhiteSpace(message) || !_governmentPublicTurns.Add(businessId)) return null;
            string boundedMessage = message.Trim();
            if (boundedMessage.Length > 2000) boundedMessage = boundedMessage.Substring(0, 2000);
            var transcript = JArray.Parse(string.IsNullOrEmpty(business.TranscriptJson) ? "[]" : business.TranscriptJson);
            var speakerIds = new[] { business.SponsorHeroStringId, business.PetitionerHeroStringId }
                .Concat(GetSeats(business.KingdomStringId).Select(x => x.HeroStringId)).Where(x => !string.IsNullOrEmpty(x))
                .Distinct(StringComparer.OrdinalIgnoreCase).Where(x => FindHero(x)?.IsAlive == true && x != Hero.MainHero.StringId
                    && GovernmentAttendanceBlock(FindHero(x), false).Length == 0 && FindHero(x).CurrentSettlement != null
                    && FindHero(x).CurrentSettlement == Hero.MainHero.CurrentSettlement).Take(6);
            return new JObject { ["businessId"] = businessId, ["revision"] = business.Revision,
                ["institutionName"] = GetGovernment(business.KingdomStringId)?.InstitutionName ?? "Government", ["authorityLevel"] = GetGovernment(business.KingdomStringId)?.Level ?? 1,
                ["title"] = business.Title, ["summary"] = business.Summary, ["options"] = GetPublicBusinessOptions(business),
                ["recommendation"] = business.RecommendedOptionId, ["playerMessage"] = boundedMessage,
                ["transcript"] = new JArray(transcript.Skip(Math.Max(0, transcript.Count - 12))),
                ["speakers"] = new JArray(speakerIds.Select(id => new JObject { ["heroId"] = id, ["name"] = FindHero(id).Name.ToString(),
                    ["role"] = Same(id, business.SponsorHeroStringId) ? "sponsor" : Same(id, business.PetitionerHeroStringId) ? "petitioner" : "member" })) };
        }

        private JObject ApplyPublicHearingTurn(string businessId, JObject payload, JObject response)
        {
            _governmentPublicTurns.Remove(businessId);
            var business = _business.FirstOrDefault(x => Same(x.BusinessId, businessId));
            if (business == null || business.Status != "hearing" || business.Revision != payload.Value<long>("revision"))
                return new JObject { ["ok"] = false, ["error"] = "The hearing changed while the response was being prepared. No statement was applied." };
            if (response?.Value<bool?>("ok") != true) return response ?? new JObject { ["ok"] = false };
            var transcript = JArray.Parse(string.IsNullOrEmpty(business.TranscriptJson) ? "[]" : business.TranscriptJson);
            transcript.Add(new JObject { ["speakerHeroId"] = Hero.MainHero.StringId, ["speakerName"] = Hero.MainHero.Name.ToString(), ["text"] = payload.Value<string>("playerMessage"), ["day"] = CurrentDay() });
            var speakers = ((JArray)payload["speakers"]).OfType<JObject>().ToDictionary(x => x.Value<string>("heroId"), x => x.Value<string>("name"));
            foreach (var row in (response["statements"] as JArray ?? new JArray()).OfType<JObject>().Take(3))
            {
                string id = row.Value<string>("speakerHeroId") ?? "";
                string text = row.Value<string>("text") ?? "";
                if (!speakers.ContainsKey(id) || string.IsNullOrWhiteSpace(text)) continue;
                transcript.Add(new JObject { ["speakerHeroId"] = id, ["speakerName"] = speakers[id], ["text"] = text.Length > 1200 ? text.Substring(0, 1200) : text, ["day"] = CurrentDay() });
            }
            business.TranscriptJson = new JArray(transcript.Skip(Math.Max(0, transcript.Count - 120))).ToString(Formatting.None);
            ChangedBusiness(business);
            // Public language never applies actions, votes, money, commitments or relationship effects.
            return new JObject { ["ok"] = true, ["message"] = response.Value<string>("message") ?? "The public record has been updated.",
                ["nativeActions"] = new JArray(), ["governmentCommitments"] = new JArray() };
        }
    }
}

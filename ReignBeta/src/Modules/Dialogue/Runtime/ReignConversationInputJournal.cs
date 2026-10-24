#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ReignBeta.Runtime
{
    // Originals remain local even if the server never receives the request. No automatic
    // replay across saves: resending can execute game actions and needs the original scope.
    internal static class ReignConversationInputJournal
    {
        private static readonly object Gate = new object();
        private static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Reign", "ConversationInputs");
        internal static string Hash(string text)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""))).Replace("-", "").ToLowerInvariant();
        }
        internal static string Save(string campaign, string timeline, string player, string id, string text,
            string hero = "", string session = "")
        {
            if (string.IsNullOrWhiteSpace(text)) return id;
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("The message needs a stable contribution ID before it can be sent.");
            string directory = Path.Combine(Root, Hash(campaign + "|" + timeline + "|" + player));
            string file = Path.Combine(directory, Hash(id) + ".json");
            lock (Gate)
            {
                Directory.CreateDirectory(directory);
                if (File.Exists(file))
                {
                    var saved = JObject.Parse(File.ReadAllText(file, Encoding.UTF8));
                    if (saved.Value<string>("text") != text || saved.Value<string>("sourceHash") != Hash(text))
                        throw new InvalidOperationException("The saved contribution has different text. Your existing original was retained.");
                    var listeners = saved["associatedHeroIds"] as JArray ?? new JArray();
                    if (!string.IsNullOrWhiteSpace(hero) && !listeners.Values<string>().Contains(hero)) listeners.Add(hero);
                    saved["associatedHeroIds"] = listeners;
                    if (string.IsNullOrEmpty(saved.Value<string>("sessionId")) && !string.IsNullOrEmpty(session)) saved["sessionId"] = session;
                    AtomicWrite(file, saved);
                    return id;
                }
                var row = new JObject { ["schema"] = "reign-client-input-v1", ["campaignId"] = campaign, ["timelineId"] = timeline,
                    ["playerId"] = player, ["contributionId"] = id, ["heroId"] = hero, ["sessionId"] = session,
                    ["text"] = text, ["sourceHash"] = Hash(text), ["savedUtc"] = DateTime.UtcNow.ToString("O") };
                AtomicWrite(file, row);
            }
            return id;
        }
        private static void AtomicWrite(string file, JObject row)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(row.ToString(Formatting.None));
            Reign.Core.Contracts.Dialogue.ConversationInputJournalStore.WriteAtomic(file, bytes);
        }
        internal static void Prepare(JObject payload)
        {
            string text = payload?.Value<string>("playerText");
            if (string.IsNullOrWhiteSpace(text)) return;
            string id = payload.Value<string>("contributionId");
            if (string.IsNullOrWhiteSpace(id)) id = payload.Value<string>("sceneTurnId") ?? payload.Value<string>("turnId") ?? payload.Value<string>("correlationId");
            payload["contributionId"] = Save(payload.Value<string>("campaignId") ?? "", payload.Value<string>("timelineId") ?? "main",
                payload.Value<string>("playerHeroStringId") ?? payload.Value<string>("mainHeroStringId") ?? "", id, text,
                payload.Value<string>("heroStringId") ?? payload.Value<string>("speakerHeroStringId") ?? payload.Value<string>("petitionerHeroId")
                    ?? payload["speaker"]?.Value<string>("characterId") ?? "", payload.Value<string>("conversationSessionId") ?? "");
        }
        // A local acknowledgment-write failure must not turn an accepted server reply
        // into a failed send and offer another execution of its actions.
        internal static string Acknowledge(JObject payload, JObject response)
        {
            var receipt = response?["inputReceipt"] as JObject;
            if (receipt?["persisted"]?.Type != JTokenType.Boolean || !(bool)receipt["persisted"]) return "";
            string id = ExactString(payload, "contributionId");
            if (string.IsNullOrWhiteSpace(id) || ExactString(receipt, "contributionId") != id) return "The delivery receipt has a different contribution ID.";
            string scope = Hash((payload.Value<string>("campaignId") ?? "") + "|" + (payload.Value<string>("timelineId") ?? "main") + "|"
                + (payload.Value<string>("playerHeroStringId") ?? payload.Value<string>("mainHeroStringId") ?? ""));
            return AcknowledgeDirectory(Path.Combine(Root, scope), id, receipt);
        }

        internal static string AcknowledgeDirectory(string directory, string id, JObject receipt)
        {
            string file = Path.Combine(directory, Hash(id) + ".json");
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(file)) return "The saved original is unavailable for receipt reconciliation.";
                    var row = JObject.Parse(File.ReadAllText(file, Encoding.UTF8));
                    if (ExactString(row, "contributionId") != id || ExactString(receipt, "contributionId") != id
                        || ExactString(row, "sourceHash") != Hash(ExactString(row, "text"))
                        || ExactString(row, "sourceHash") != ExactString(receipt, "sourceHash"))
                        return "The delivery receipt did not match the saved original; the original was retained.";
                    row["receipt"] = receipt.DeepClone();
                    AtomicWrite(file, row);
                    return "";
                }
                catch (IOException) { return "The delivery receipt could not be saved locally."; }
                catch (UnauthorizedAccessException) { return "The delivery receipt could not be saved locally."; }
                catch (JsonException) { return "The saved original is damaged; it was retained without a receipt."; }
            }
        }
        internal static void AppendPending(JArray lines, string campaign, string timeline, string player, string hero = "", string session = "")
        {
            // A missing session is never a wildcard. In particular, an old failed
            // greeting must not become a player turn beneath a fresh NPC approach.
            if (string.IsNullOrWhiteSpace(session)) return;
            AppendPendingRows(lines, ReadRecoverableDirectory(Path.Combine(Root, Hash(campaign + "|" + timeline + "|" + player)),
                campaign, timeline, player, hero, session), session);
        }

        internal static void AppendPendingRows(JArray lines, JArray recoverable, string session)
        {
            if (string.IsNullOrWhiteSpace(session)) return;
            foreach (JObject row in recoverable.OfType<JObject>())
            {
                if (ExactString(row, "sessionId") != session) continue;
                string id = row.Value<string>("contributionId");
                if (lines.OfType<JObject>().Any(line => line.Value<string>("contributionId") == id)) continue;
                lines.Add(new JObject { ["speaker"] = "Saved draft — delivery unconfirmed", ["role"] = "system",
                    ["text"] = "This saved input has no confirmed delivery in this conversation.\n\n" + row.Value<string>("text"),
                    ["contributionId"] = id, ["deliveryStatus"] = "awaiting_server_receipt" });
            }
        }

        internal static JArray ReadRecoverable(string campaign, string timeline, string player, string hero)
            => ReadRecoverableDirectory(Path.Combine(Root, Hash(campaign + "|" + timeline + "|" + player)), campaign, timeline, player, hero);

        // Directory injection is limited to this IO boundary so the production
        // journal can be exercised against an isolated temporary directory.
        internal static JArray ReadRecoverableDirectory(string directory, string campaign, string timeline, string player, string hero, string requiredSession = "")
        {
            lock (Gate)
            {
                var rows = ReadScopedRows(directory, campaign, timeline, player, hero, requiredSession);
                return new JArray(rows.Values.Where(row => (requiredSession.Length == 0 || ExactString(row, "sessionId") == requiredSession) && !Confirmed(row)
                    && !(rows.TryGetValue(ExactString(row, "reintroducedAs"), out var accepted)
                        && Confirmed(accepted) && ExactString(row, "reviewedSourceHash") == accepted.Value<string>("sourceHash")
                        && ExactString(row, "reviewedOriginalHash") == row.Value<string>("sourceHash")))
                    .OrderByDescending(row => ExactString(row, "savedUtc"), StringComparer.Ordinal)
                    .ThenBy(row => row.Value<string>("contributionId"), StringComparer.Ordinal).Select(row => row.DeepClone()));
            }
        }

        private static Dictionary<string, JObject> ReadScopedRows(string directory, string campaign, string timeline, string player, string hero, string requiredSession = "")
        {
            var rows = new Dictionary<string, JObject>(StringComparer.Ordinal);
            if ((string.IsNullOrWhiteSpace(hero) && string.IsNullOrWhiteSpace(requiredSession)) || !Directory.Exists(directory)) return rows;
            foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
            {
                try
                {
                    var row = JObject.Parse(File.ReadAllText(file, Encoding.UTF8));
                    string id = row.Value<string>("contributionId");
                    if (row.Value<string>("schema") != "reign-client-input-v1" || string.IsNullOrWhiteSpace(id) || Path.GetFileName(file) != Hash(id) + ".json"
                        || row.Value<string>("campaignId") != campaign || row.Value<string>("timelineId") != timeline
                        || row.Value<string>("playerId") != player || string.IsNullOrWhiteSpace(row.Value<string>("text"))
                        || Hash(row.Value<string>("text")) != row.Value<string>("sourceHash")
                        || (row["sessionId"] != null && row["sessionId"].Type != JTokenType.String)
                        || (hero.Length > 0 && row.Value<string>("heroId") != hero && !(row["associatedHeroIds"] as JArray ?? new JArray()).Values<string>().Contains(hero))) continue;
                    rows[id] = row;
                }
                catch (IOException) { } // Retain damaged/unreadable originals; other drafts still load.
                catch (UnauthorizedAccessException) { }
                catch (JsonException) { }
                catch (FormatException) { }
                catch (InvalidCastException) { }
            }
            return rows;
        }

        private static bool Confirmed(JObject row)
        {
            var receipt = row["receipt"] as JObject;
            if (receipt?["persisted"]?.Type == JTokenType.Boolean && (bool)receipt["persisted"]
                && ExactString(receipt, "contributionId") == row.Value<string>("contributionId")
                && ExactString(receipt, "sourceHash") == row.Value<string>("sourceHash")) return true;
            var history = row["acceptedHistory"] as JObject;
            return ExactString(history, "sourceHash") == row.Value<string>("sourceHash")
                && !string.IsNullOrWhiteSpace(ExactString(history, "turnId"))
                && !string.IsNullOrWhiteSpace(ExactString(row, "sessionId"))
                && ExactString(history, "sessionId") == ExactString(row, "sessionId");
        }

        private static string ExactString(JObject row, string key) => row?[key]?.Type == JTokenType.String ? (string)row[key] : "";

        internal static void ReconcileAcceptedHistory(JArray lines, string campaign, string timeline, string player, string hero)
        {
            string directory = Path.Combine(Root, Hash(campaign + "|" + timeline + "|" + player));
            ReconcileAcceptedHistoryDirectory(directory, lines, campaign, timeline, player, hero);
        }

        internal static void ReconcileAcceptedHistoryDirectory(string directory, JArray lines, string campaign, string timeline, string player, string hero)
        {
            lock (Gate)
            {
                var rows = ReadScopedRows(directory, campaign, timeline, player, hero);
                foreach (JObject line in lines.OfType<JObject>())
                {
                    if (line.Value<string>("role") != "player" || line.Value<string>("deliveryStatus") == "submitted"
                        || line.Value<string>("sourceIntegrity") != "original_verified" || string.IsNullOrWhiteSpace(line.Value<string>("turnId"))
                        || !rows.TryGetValue(line.Value<string>("contributionId") ?? "", out var row)
                        || Confirmed(row)
                        || string.IsNullOrWhiteSpace(row.Value<string>("sessionId")) || row.Value<string>("sessionId") != line.Value<string>("sessionId")
                        || row.Value<string>("sourceHash") != Hash(line.Value<string>("text"))) continue;
                    // This is a history reference, not an invented original-message receipt.
                    row["acceptedHistory"] = new JObject { ["turnId"] = line["turnId"].DeepClone(),
                        ["sessionId"] = line["sessionId"].DeepClone(), ["sourceHash"] = row["sourceHash"].DeepClone() };
                    AtomicWrite(Path.Combine(directory, Hash(row.Value<string>("contributionId")) + ".json"), row);
                }
            }
        }

        internal static void ResolveReintroducedDraft(JObject original, string newContributionId, string hero = "")
        {
            if (original == null || string.IsNullOrWhiteSpace(newContributionId)) return;
            string campaign = original.Value<string>("campaignId"), timeline = original.Value<string>("timelineId"), player = original.Value<string>("playerId");
            string directory = Path.Combine(Root, Hash(campaign + "|" + timeline + "|" + player));
            ResolveReintroducedDraftDirectory(directory, original, newContributionId, hero);
        }

        internal static void ResolveReintroducedDraftDirectory(string directory, JObject original, string newContributionId, string hero = "")
        {
            lock (Gate)
            {
                var rows = ReadScopedRows(directory, original.Value<string>("campaignId"), original.Value<string>("timelineId"),
                    original.Value<string>("playerId"), string.IsNullOrWhiteSpace(hero) ? original.Value<string>("heroId") : hero);
                string id = original.Value<string>("contributionId");
                if (id == newContributionId || !rows.TryGetValue(id ?? "", out var saved) || !rows.TryGetValue(newContributionId ?? "", out var accepted)
                    || !Confirmed(accepted) || saved.Value<string>("sourceHash") != original.Value<string>("sourceHash")) return;
                saved["reintroducedAs"] = newContributionId;
                saved["reviewedOriginalHash"] = saved["sourceHash"].DeepClone();
                saved["reviewedSourceHash"] = accepted["sourceHash"].DeepClone();
                AtomicWrite(Path.Combine(directory, Hash(id) + ".json"), saved);
            }
        }
    }

    internal sealed class ReignRecoveredDraftReview
    {
        private DateTime _submitAfterUtc;
        internal JObject SelectedDraft { get; private set; }
        internal void Clear() { SelectedDraft = null; }
        internal void InputChanged(string text) { if (string.IsNullOrWhiteSpace(text)) Clear(); }
        internal static bool HasPendingInSession(JArray drafts, string session) => !string.IsNullOrWhiteSpace(session)
            && drafts.OfType<JObject>().Any(row => row["sessionId"]?.Type == JTokenType.String && (string)row["sessionId"] == session);
        internal bool CanSubmit(DateTime nowUtc) => nowUtc >= _submitAfterUtc;
        internal string Restore(JObject draft, bool composerEmpty, DateTime nowUtc)
        {
            if (!composerEmpty || draft == null || ReignConversationInputJournal.Hash(draft.Value<string>("text")) != draft.Value<string>("sourceHash")) return null;
            _submitAfterUtc = nowUtc.AddSeconds(1); // A double-click must not turn recovery into a send.
            SelectedDraft = (JObject)draft.DeepClone();
            return draft.Value<string>("text");
        }
    }
}

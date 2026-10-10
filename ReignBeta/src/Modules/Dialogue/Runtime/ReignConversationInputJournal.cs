using System;
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
        internal static void Acknowledge(JObject payload, JObject response)
        {
            var receipt = response?["inputReceipt"] as JObject;
            if (receipt?.Value<bool?>("persisted") != true) return;
            string id = payload.Value<string>("contributionId");
            if (string.IsNullOrWhiteSpace(id) || receipt.Value<string>("contributionId") != id) return;
            string scope = Hash((payload.Value<string>("campaignId") ?? "") + "|" + (payload.Value<string>("timelineId") ?? "main") + "|"
                + (payload.Value<string>("playerHeroStringId") ?? payload.Value<string>("mainHeroStringId") ?? ""));
            string file = Path.Combine(Root, scope, Hash(id) + ".json");
            lock (Gate)
            {
                if (!File.Exists(file)) return;
                var row = JObject.Parse(File.ReadAllText(file, Encoding.UTF8));
                if (row.Value<string>("sourceHash") != receipt.Value<string>("sourceHash"))
                    throw new InvalidOperationException("The server's original-message receipt did not match the saved text.");
                row["receipt"] = receipt.DeepClone();
                AtomicWrite(file, row);
            }
        }
        internal static void AppendPending(JArray lines, string campaign, string timeline, string player, string hero = "", string session = "")
        {
            string directory = Path.Combine(Root, Hash(campaign + "|" + timeline + "|" + player));
            lock (Gate)
            {
                if (!Directory.Exists(directory)) return;
                foreach (string file in Directory.EnumerateFiles(directory, "*.json").OrderBy(f => f, StringComparer.Ordinal))
                {
                    var row = JObject.Parse(File.ReadAllText(file, Encoding.UTF8));
                    if (row["receipt"] != null || (hero.Length > 0 && row.Value<string>("heroId") != hero
                            && !(row["associatedHeroIds"] as JArray ?? new JArray()).Values<string>().Contains(hero))
                        || (session.Length > 0 && row.Value<string>("sessionId") != session)) continue;
                    string id = row.Value<string>("contributionId");
                    if (lines.OfType<JObject>().Any(line => line.Value<string>("contributionId") == id)) continue;
                    if (Hash(row.Value<string>("text")) != row.Value<string>("sourceHash"))
                        throw new InvalidOperationException("A locally saved message failed its integrity check; the original file was retained.");
                    lines.Add(new JObject { ["speaker"] = "Player (saved locally)", ["role"] = "player", ["text"] = row["text"].DeepClone(),
                        ["contributionId"] = id, ["deliveryStatus"] = "awaiting_server_receipt" });
                }
            }
        }
    }
}

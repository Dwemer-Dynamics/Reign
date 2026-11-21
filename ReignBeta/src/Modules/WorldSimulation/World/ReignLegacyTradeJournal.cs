using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ReignBeta.WorldSimulation.World
{
    internal sealed class LegacyTradeBalance
    {
        internal LegacyTradeBalance(string id, Func<int> read, Action<int> restore)
        {
            Id = id;
            Read = read;
            Restore = restore;
        }

        internal string Id { get; }
        internal Func<int> Read { get; }
        internal Action<int> Restore { get; }
    }

    internal sealed class LegacyTradeLeg
    {
        internal LegacyTradeLeg(LegacyTradeBalance from, LegacyTradeBalance to, int amount, Action apply)
        {
            From = from;
            To = to;
            Amount = amount;
            Apply = apply;
        }

        internal LegacyTradeBalance From { get; }
        internal LegacyTradeBalance To { get; }
        internal int Amount { get; }
        internal Action Apply { get; }
    }

    internal sealed class LegacyTradeReceipt
    {
        public string Binding { get; set; }
        public string State { get; set; }
        public string Message { get; set; }
        public JObject Result { get; set; }
        public JArray Legs { get; set; }
        public JArray Unresolved { get; set; }
        public int AttemptedLegs { get; set; }
    }

    // Stored in the same native save as the traded assets, independent of the pruned action queue.
    internal sealed class LegacyTradeJournal
    {
        public const string Applying = "applying";
        public const string Applied = "applied";
        public const string Restored = "restored";
        public const string RecoveryRequired = "recovery_required";

        private string _applyingActionId;

        internal bool IsApplying(string actionId) => _applyingActionId == actionId;

        [JsonProperty(Required = Required.Always)]
        public int Version { get; set; } = 1;
        [JsonProperty(Required = Required.Always)]
        public Dictionary<string, LegacyTradeReceipt> Receipts { get; set; } =
            new Dictionary<string, LegacyTradeReceipt>(StringComparer.Ordinal);

        internal LegacyTradeReceipt Find(string actionId, string binding)
        {
            if (string.IsNullOrWhiteSpace(actionId))
                throw new InvalidOperationException("A trade requires a durable action ID.");
            if (!Receipts.TryGetValue(actionId, out LegacyTradeReceipt receipt)) return null;
            if (!string.Equals(receipt.Binding, binding, StringComparison.Ordinal))
                throw new InvalidOperationException("The trade action ID was already used for different terms.");
            return receipt;
        }

        internal LegacyTradeReceipt Execute(string actionId, string binding, IReadOnlyList<LegacyTradeLeg> legs,
            Func<JObject> finalize)
        {
            var prior = Find(actionId, binding);
            if (prior != null) return prior;
            if (_applyingActionId != null) throw new InvalidOperationException("Another trade is still applying in this campaign.");
            if (legs.Count == 0) throw new InvalidOperationException("The trade has no supported asset transfers.");

            // Plan the whole batch before any write, including repeated uses of an exact stack.
            var opening = new Dictionary<string, int>(StringComparer.Ordinal);
            var projected = new Dictionary<string, int>(StringComparer.Ordinal);
            var balances = new Dictionary<string, LegacyTradeBalance>(StringComparer.Ordinal);
            var planned = new JArray();
            foreach (var leg in legs)
            {
                if (leg.Amount <= 0 || leg.From.Id == leg.To.Id)
                    throw new InvalidOperationException("A trade leg requires a positive amount and two distinct holdings.");
                foreach (var balance in new[] { leg.From, leg.To })
                {
                    if (opening.ContainsKey(balance.Id)) continue;
                    balances.Add(balance.Id, balance);
                    opening.Add(balance.Id, balance.Read());
                    projected.Add(balance.Id, opening[balance.Id]);
                }

                int from = projected[leg.From.Id];
                int to = projected[leg.To.Id];
                if (from < leg.Amount || to < 0 || (long)to + leg.Amount > int.MaxValue)
                    throw new InvalidOperationException("The whole trade cannot be funded or received at the exact holdings.");
                projected[leg.From.Id] = from - leg.Amount;
                projected[leg.To.Id] = to + leg.Amount;
                planned.Add(new JObject
                {
                    ["from"] = State(leg.From.Id, from, projected[leg.From.Id]),
                    ["to"] = State(leg.To.Id, to, projected[leg.To.Id])
                });
            }

            var receipt = new LegacyTradeReceipt { Binding = binding, State = Applying, Legs = planned };
            Receipts.Add(actionId, receipt);
            _applyingActionId = actionId;
            try
            {
                for (int i = 0; i < legs.Count; i++)
                {
                    var leg = legs[i];
                    Verify(leg.From, planned[i]["from"].Value<int>("before"));
                    Verify(leg.To, planned[i]["to"].Value<int>("before"));
                    receipt.AttemptedLegs = i + 1;
                    leg.Apply();
                    Verify(leg.From, planned[i]["from"].Value<int>("expected"));
                    Verify(leg.To, planned[i]["to"].Value<int>("expected"));
                }

                foreach (var holding in balances.Values) Verify(holding, projected[holding.Id]);
                receipt.Result = finalize() ?? throw new InvalidOperationException("The trade has no verified receipt.");
                receipt.State = Applied;
            }
            catch (Exception ex)
            {
                // Reverse each attempted holding once using its latest attempted boundary. An
                // unexpected count may be a callback side effect, so leave it for reconciliation.
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var errors = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (JToken leg in planned.Take(receipt.AttemptedLegs).Reverse())
                {
                    foreach (JToken side in new[] { leg["to"], leg["from"] })
                    {
                        string id = side.Value<string>("holding");
                        if (!seen.Add(id)) continue;
                        try
                        {
                            int current = balances[id].Read();
                            if (current == opening[id]) continue;
                            if (current == side.Value<int>("before") || current == side.Value<int>("expected"))
                                balances[id].Restore(opening[id]);
                        }
                        catch (Exception rollback)
                        {
                            errors[id] = rollback.Message;
                        }
                    }
                }

                var unresolved = new JArray();
                foreach (var holding in balances.Values)
                {
                    var row = State(holding.Id, opening[holding.Id], projected[holding.Id]);
                    try
                    {
                        int observed = holding.Read();
                        if (observed == opening[holding.Id]) continue;
                        row["observed"] = observed;
                    }
                    catch (Exception read)
                    {
                        row["readError"] = read.Message;
                    }
                    if (errors.TryGetValue(holding.Id, out string rollbackError)) row["rollbackError"] = rollbackError;
                    unresolved.Add(row);
                }

                receipt.Unresolved = unresolved;
                receipt.State = unresolved.Count == 0 ? Restored : RecoveryRequired;
                receipt.Message = ex.Message;
            }
            finally
            {
                _applyingActionId = null;
            }
            return receipt;
        }

        private static JObject State(string id, int before, int expected)
            => new JObject { ["holding"] = id, ["before"] = before, ["expected"] = expected };

        private static void Verify(LegacyTradeBalance holding, int expected)
        {
            if (holding.Read() != expected)
                throw new InvalidOperationException("The native trade holding did not match its expected count: " + holding.Id);
        }
    }
}

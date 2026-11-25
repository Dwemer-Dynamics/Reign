#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ReignBeta.Court.Campaign
{
    internal enum CourtPaidSupplyPhase
    {
        RemovingStock = 1,
        AddingStock = 2,
        Paying = 3,
        Verified = 4,
        Held = 5
    }

    internal sealed class CourtPaidSupplyBalances
    {
        internal CourtPaidSupplyBalances(int source, int target, int payer, int recipient)
        {
            Source = source;
            Target = target;
            Payer = payer;
            Recipient = recipient;
        }

        [JsonProperty(Required = Required.Always)]
        public int Source { get; set; }
        [JsonProperty(Required = Required.Always)]
        public int Target { get; set; }
        [JsonProperty(Required = Required.Always)]
        public int Payer { get; set; }
        [JsonProperty(Required = Required.Always)]
        public int Recipient { get; set; }

        [JsonConstructor]
        private CourtPaidSupplyBalances() { }

        internal bool CanFund(int stock, int gold) => stock > 0 && gold > 0 && Source >= stock
            && Target >= 0 && (long)Target + stock <= int.MaxValue
            && Payer >= gold && Recipient >= 0 && (long)Recipient + gold <= int.MaxValue;

        internal bool Matches(int source, int target, int payer, int recipient)
            => Source == source && Target == target && Payer == payer && Recipient == recipient;
    }

    internal sealed class CourtPaidSupplyReceipt
    {
        [JsonProperty(Required = Required.Always)]
        public string Binding { get; set; } = "";
        [JsonProperty(Required = Required.Always)]
        public CourtPaidSupplyPhase Phase { get; set; }
        [JsonProperty(Required = Required.Always)]
        public CourtPaidSupplyBalances Before { get; set; } = null!;
        public CourtPaidSupplyBalances? AfterRemoval { get; set; }
        public CourtPaidSupplyBalances? AfterDelivery { get; set; }
        public CourtPaidSupplyBalances? AfterPayment { get; set; }

        internal bool ProvesCompletion(int stock, int gold) => Phase == CourtPaidSupplyPhase.Verified && Before.CanFund(stock, gold)
            && AfterRemoval?.Matches(Before.Source - stock, Before.Target, Before.Payer, Before.Recipient) == true
            && AfterDelivery?.Matches(Before.Source - stock, Before.Target + stock, Before.Payer, Before.Recipient) == true
            && AfterPayment?.Matches(Before.Source - stock, Before.Target + stock, Before.Payer - gold, Before.Recipient + gold) == true;
    }

    // Saved with the native rosters and wallets. An in-flight or held receipt can never authorize another native leg.
    internal sealed class ReignCourtSupplyJournal
    {
        private bool _applying;

        [JsonProperty(Required = Required.Always)]
        public int Version { get; set; } = 1;
        [JsonProperty(Required = Required.Always)]
        public Dictionary<string, CourtPaidSupplyReceipt> Receipts { get; set; } =
            new Dictionary<string, CourtPaidSupplyReceipt>(StringComparer.Ordinal);

        internal void EnsureReadable()
        {
            if (Version != 1 || Receipts == null)
                throw new InvalidOperationException("Unsupported paid Court supply save data.");
            foreach (var entry in Receipts)
            {
                if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value == null || string.IsNullOrWhiteSpace(entry.Value.Binding)
                    || entry.Value.Before == null || !Enum.IsDefined(typeof(CourtPaidSupplyPhase), entry.Value.Phase))
                    throw new InvalidOperationException("Paid Court supply save data contains an invalid receipt.");
            }
        }

        internal bool HasCommand(string commandId) => Receipts.ContainsKey(commandId);

        internal CourtPaidSupplyReceipt? Find(string commandId, string binding)
        {
            if (!Receipts.TryGetValue(commandId, out var receipt)) return null;
            if (!string.Equals(receipt.Binding, binding, StringComparison.Ordinal))
                throw new InvalidOperationException("The paid supply command belongs to different saved terms or scope.");
            return receipt;
        }

        internal CourtPaidSupplyReceipt? Apply(string commandId, string binding, int stock, int gold,
            Func<bool> owns, Func<CourtPaidSupplyBalances> read, Action removeStock, Action deliverStock, Action pay)
        {
            if (string.IsNullOrWhiteSpace(commandId) || string.IsNullOrWhiteSpace(binding))
                throw new InvalidOperationException("A paid supply transfer requires exact saved command terms.");
            var previous = Find(commandId, binding);
            if (previous != null) return previous;
            if (_applying || !owns()) return null;

            var before = read();
            if (!before.CanFund(stock, gold)) return null;
            var receipt = new CourtPaidSupplyReceipt { Binding = binding, Before = before, Phase = CourtPaidSupplyPhase.RemovingStock };
            Receipts.Add(commandId, receipt);
            _applying = true;
            try
            {
                try { removeStock(); }
                finally { receipt.AfterRemoval = Observe(read); }
                if (receipt.AfterRemoval?.Matches(before.Source - stock, before.Target, before.Payer, before.Recipient) != true
                    || !owns()) return Hold(receipt);

                receipt.Phase = CourtPaidSupplyPhase.AddingStock;
                try { deliverStock(); }
                finally { receipt.AfterDelivery = Observe(read); }
                if (receipt.AfterDelivery?.Matches(before.Source - stock, before.Target + stock, before.Payer, before.Recipient) != true
                    || !owns()) return Hold(receipt);

                receipt.Phase = CourtPaidSupplyPhase.Paying;
                try { pay(); }
                finally { receipt.AfterPayment = Observe(read); }
                if (receipt.AfterPayment?.Matches(before.Source - stock, before.Target + stock, before.Payer - gold, before.Recipient + gold) != true
                    || !owns()) return Hold(receipt);

                receipt.Phase = CourtPaidSupplyPhase.Verified;
                return receipt;
            }
            catch (Exception)
            {
                // A native callback may throw after one or all expected writes. Do not infer success or overwrite its effects.
                return Hold(receipt);
            }
            finally { _applying = false; }
        }

        private static CourtPaidSupplyBalances? Observe(Func<CourtPaidSupplyBalances> read)
        {
            try { return read(); }
            catch (Exception) { return null; }
        }

        private static CourtPaidSupplyReceipt Hold(CourtPaidSupplyReceipt receipt)
        {
            receipt.Phase = CourtPaidSupplyPhase.Held;
            return receipt;
        }
    }
}

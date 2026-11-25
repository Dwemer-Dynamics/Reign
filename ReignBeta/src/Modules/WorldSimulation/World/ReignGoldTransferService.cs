using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Shared.Dialogue;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using ReignBeta.Characters.Campaign;
using ReignBeta.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.World
{
    public sealed class ReignGoldTransferService : CampaignBehaviorBase
    {
        private GoldPaymentLedger _ledger = new GoldPaymentLedger();
        private string _serialized = "";
        private string _tradePackagesSerialized = "";
        private string _ransomJournalSerialized = "";
        internal LegacyTradeJournal TradePackages { get; private set; } = new LegacyTradeJournal();
        internal NativeRansomJournal Ransoms { get; private set; } = new NativeRansomJournal();
        public static ReignGoldTransferService Instance => ReignServices.GoldTransferService;
        public override void RegisterEvents() { ReignServices.GoldTransferService = this; }
        public override void SyncData(IDataStore store)
        {
            if (store.IsSaving)
            {
                _serialized = JsonConvert.SerializeObject(_ledger);
                if (TradePackages != null)
                    _tradePackagesSerialized = JsonConvert.SerializeObject(TradePackages);
                _ransomJournalSerialized = JsonConvert.SerializeObject(Ransoms);
            }
            store.SyncData("_reign_gold_receipts_v1", ref _serialized);
            if (store.IsLoading)
            {
                _tradePackagesSerialized = "";
                _ransomJournalSerialized = "";
                TradePackages = null;
            }
            store.SyncData("_reign_legacy_trade_packages_v1", ref _tradePackagesSerialized);
            store.SyncData("_reign_native_ransom_journal_v1", ref _ransomJournalSerialized);
            if (store.IsLoading)
            {
                _ledger = string.IsNullOrEmpty(_serialized) ? new GoldPaymentLedger() : JsonConvert.DeserializeObject<GoldPaymentLedger>(_serialized);
                if (_ledger == null || _ledger.Version != 1 || _ledger.Receipts == null)
                    throw new InvalidOperationException("Unsupported Reign gold receipt save data.");
                var tradePackages = string.IsNullOrEmpty(_tradePackagesSerialized) ? new LegacyTradeJournal()
                    : JsonConvert.DeserializeObject<LegacyTradeJournal>(_tradePackagesSerialized);
                if (tradePackages == null || tradePackages.Version != 1 || tradePackages.Receipts == null)
                    throw new InvalidOperationException("Unsupported Reign trade journal save data.");
                TradePackages = tradePackages;
                var ransoms = string.IsNullOrEmpty(_ransomJournalSerialized) ? new NativeRansomJournal()
                    : JsonConvert.DeserializeObject<NativeRansomJournal>(_ransomJournalSerialized);
                if (ransoms == null || ransoms.Version != 1 || ransoms.Attempts == null)
                    throw new InvalidOperationException("Unsupported Reign ransom journal save data.");
                Ransoms = ransoms;
            }
        }
        public JArray Snapshot(Hero hero) => JArray.FromObject(_ledger.Receipts
            .Where(r => hero != null && (r.From == hero.StringId || r.To == hero.StringId))
            .Reverse().Take(12).Select(r => new { actionId = r.Id, fromHeroStringId = r.From, toHeroStringId = r.To,
                amount = r.Amount, status = r.Status, purpose = r.Purpose, agreementId = r.Agreement,
                credited = r.Credited, refunded = r.Refunded, refundOfReceiptId = r.RefundOf, message = r.Message }));

        public int Credit(Hero recipient, string agreement) => _ledger.AvailableCredit(Hero.MainHero.StringId, recipient.StringId, agreement);
        public int UsedCredit(string transaction) => _ledger.CreditUsed(transaction);
        public int ConsumeCredit(Hero recipient, string agreement, string transaction, int total)
            => _ledger.ConsumeCredit(Hero.MainHero.StringId, recipient.StringId, agreement, transaction, total);
        public void ReleaseCredit(string transaction) => _ledger.ReleaseCredit(transaction);

        public bool HasReceipt(string id) => _ledger.Receipts.Any(r => r.Id == id);
        public string ReceiptStatus(string id) => _ledger.Receipts.FirstOrDefault(r => r.Id == id)?.Status ?? "pending";
        internal const string InternationalCourtPaymentPurpose = "international_court";

        internal GoldPaymentReceipt ReadInternationalCourtReceipt(string id)
        {
            var candidates = _ledger.Receipts.Where(receipt => receipt.Id == id).Take(2).ToArray();
            return candidates.Length == 1 && candidates[0].Purpose == InternationalCourtPaymentPurpose
                ? candidates[0] : null;
        }

        internal GoldPaymentReceipt PayInternationalCourt(string id, Hero payer, Hero recipient, int amount,
            string materialHash, out bool? nativeChangedBalance)
        {
            bool? changed = null;
            Hero ResolveParty(string key) => key == payer.StringId ? payer : recipient;
            var receipt = _ledger.Transfer(id, payer.StringId, recipient.StringId, amount, InternationalCourtPaymentPurpose, materialHash,
                key => ResolveParty(key).Gold,
                (_, _, gold) =>
                {
                    int payerBefore = payer.Gold, recipientBefore = recipient.Gold;
                    try { GiveGoldAction.ApplyBetweenCharacters(payer, recipient, gold, true); }
                    finally { changed = payer.Gold != payerBefore || recipient.Gold != recipientBefore; }
                },
                (key, balance) =>
                {
                    Hero hero = ResolveParty(key);
                    if (hero.Gold != balance) hero.ChangeHeroGold(balance - hero.Gold);
                });
            nativeChangedBalance = changed;
            return receipt;
        }

        public bool Validate(ReignWorldActionRecord action, Hero from, Hero to, int amount, out string reason)
        {
            reason = "";
            if (from == null || to == null || from == to || !from.IsAlive || !to.IsAlive || amount <= 0)
            { reason = "Gold transfer requires two living heroes and an exact positive amount."; return false; }
            var prior = _ledger.Receipts.FirstOrDefault(r => r.Id == action.ActionId);
            if (prior != null) return true; // Transfer checks immutable terms and returns the saved outcome.
            if (from.Gold < amount || (long)to.Gold + amount > int.MaxValue)
            { reason = "The exact payment cannot be afforded or received."; return false; }
            return true;
        }
        public ReignActionResult Transfer(ReignWorldActionRecord action, Hero from, Hero to, int amount)
        {
            JObject terms = JObject.Parse(action.TermsJson ?? "{}");
            string purpose = terms.Value<string>("paymentPurpose") ?? "gift";
            string agreement = terms.Value<string>("paymentAgreementId") ?? "";
            if (purpose == "recruitment_advance"
                && ReignServices.EncounteredResidents?.AgreementId(to) != agreement)
                return ReignActionResult.ValidationFailed("The recruitment advance no longer matches this resident's agreement.");
            return Pay(action.ActionId, from, to, amount, purpose, agreement, terms.Value<string>("refundOfReceiptId") ?? "");
        }
        public ReignActionResult Pay(string id, Hero from, Hero to, int amount, string purpose, string agreement, string refundOf = "")
        {
            try
            {
                var receipt = _ledger.Transfer(id, from.StringId, to.StringId, amount, purpose, agreement,
                    key => Hero.Find(key).Gold,
                    (payer, payee, gold) => GiveGoldAction.ApplyBetweenCharacters(Hero.Find(payer), Hero.Find(payee), gold, false),
                    (key, gold) => { var hero = Hero.Find(key); hero.ChangeHeroGold(gold - hero.Gold); }, refundOf);
                if (receipt.Status != "applied") return ReignActionResult.FailTerminal(receipt.Message,
                    receipt.Status == "unknown" ? "payment_unknown" : "payment_failed", "native_payment");
                return ReignActionResult.Done(from.Name + " transferred " + amount + " denars to " + to.Name + ".")
                    .WithEffect("gold_transfer", "hero", to.StringId, to.Name.ToString(), "from=" + from.StringId + ";amount=" + amount + ";receipt=" + id)
                    .WithChangedEntity("hero", from.StringId, from.Name.ToString(), "gold_changed")
                    .WithChangedEntity("hero", to.StringId, to.Name.ToString(), "gold_changed");
            }
            catch (Exception ex) { return ReignActionResult.ValidationFailed(ex.Message); }
        }
    }
}

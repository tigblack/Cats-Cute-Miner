using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CatMiners.Core.Outpost
{
    [Serializable]
    public sealed class PurchaseIntent
    {
        public string UserId, OrderId, Sku;
        public string Price, Currency;
        public long PriceMicros, StartedUtcMs;
    }
    [Serializable]
    public sealed class PaymentDelivery
    {
        public string TokenHash, UserId, OrderId, Sku, PaymentId, RewardId, Environment;
        public long Gems;
        public bool Granted, Claimed, Confirmed;
        public string RevenueStatus;
        public long PriceMicros;
        public string Currency;
        public bool NewRevenueEligible;
        public string AppMetricaRevenue, MetaRevenue, AdjustRevenue;
    }
    public enum PaymentRevenueRecipient { AppMetrica, Meta, Adjust }
    public interface IPaymentRevenueSink
    {
        // null means ready. Other statuses are retryable, and must never reserve a dispatch.
        string Unavailable(PaymentRevenueRecipient recipient);
        void Report(PaymentRevenueRecipient recipient, PaymentDelivery delivery);
    }
    // At-most-once SDK invocation per durable ledger/recipient, not remote exactly-once.
    // Crash between dispatching and call can lose revenue; neither return nor handed_off is a server acknowledgement.
    public sealed class PaymentRevenue
    {
        readonly IPaymentRevenueProgress progress;
        bool busy;
        public PaymentRevenue(IPaymentRevenueProgress progress) { this.progress = progress; }
        public static long PriceMicros(string price, string currency)
        {
            if (!ValidCurrency(currency) || !decimal.TryParse(price,
                System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var amount) || amount <= 0) return 0;
            try {
                var micros = checked(amount * 1000000m);
                return micros == decimal.Truncate(micros) && micros <= long.MaxValue ? (long)micros : 0;
            } catch (OverflowException) { return 0; }
        }
        public static bool ValidCurrency(string currency) => currency != null && currency.Length == 3 && currency.All(c => c >= 'A' && c <= 'Z');
        public static bool Eligible(PaymentDelivery d) => d != null && d.Granted && d.NewRevenueEligible &&
            d.Environment == "production" && d.PriceMicros > 0 && ValidCurrency(d.Currency) &&
            PaymentRules.Products.ContainsKey(d.Sku ?? "") && !string.IsNullOrEmpty(d.PaymentId) && !string.IsNullOrEmpty(d.OrderId);
        public static string Status(PaymentDelivery d, PaymentRevenueRecipient r) => r == PaymentRevenueRecipient.AppMetrica ? d.AppMetricaRevenue :
            r == PaymentRevenueRecipient.Meta ? d.MetaRevenue : d.AdjustRevenue;
        static void Set(PaymentDelivery d, PaymentRevenueRecipient r, string status)
        {
            if (r == PaymentRevenueRecipient.AppMetrica) d.AppMetricaRevenue = status;
            else if (r == PaymentRevenueRecipient.Meta) d.MetaRevenue = status;
            else d.AdjustRevenue = status;
        }
        async Task Mark(string paymentId, PaymentRevenueRecipient r, string status)
        {
            if (!progress.Current) throw new PaymentException("stale_context");
            await progress.SaveRevenue(p => Set(p.Payments.Single(d => d.PaymentId == paymentId), r, status));
        }
        public async Task Dispatch(IPaymentRevenueSink sink)
        {
            if (busy || !progress.Current || sink == null) return;
            busy = true;
            try {
                foreach (var paymentId in progress.Profile.Payments.Where(Eligible).Select(d => d.PaymentId).ToArray())
                    foreach (PaymentRevenueRecipient r in Enum.GetValues(typeof(PaymentRevenueRecipient)))
                    {
                        // Isolate readiness, persistence and SDK exceptions for each recipient.
                        try {
                            if (!progress.Current) return;
                            var d = progress.Profile.Payments.Single(x => x.PaymentId == paymentId);
                            var status = Status(d, r);
                            if (status == "dispatching") { await Mark(paymentId, r, "ambiguous_not_resent"); continue; }
                            if (status == "handed_off" || status == "ambiguous_not_resent") continue;
                            var unavailable = sink.Unavailable(r);
                            if (unavailable != null) {
                                if (status != unavailable) await Mark(paymentId, r, unavailable);
                                continue;
                            }
                            // No SDK call until this write succeeds. Any later failure is ambiguous.
                            await Mark(paymentId, r, "dispatching");
                            if (!progress.Current) return;
                            d = progress.Profile.Payments.Single(x => x.PaymentId == paymentId);
                            // Consent may change during disk IO. A skipped invocation can safely become retryable.
                            unavailable = sink.Unavailable(r);
                            if (unavailable != null) { await Mark(paymentId, r, unavailable); continue; }
                            try { sink.Report(r, OutpostJson.DeepCopy(d)); }
                            catch { await Mark(paymentId, r, "ambiguous_not_resent"); continue; }
                            await Mark(paymentId, r, "handed_off");
                        } catch { /* Failed pre-call saves retry; durable dispatching never invokes again. */ }
                    }
            } finally { busy = false; }
        }
    }
    public class PaymentException : Exception
    {
        public PaymentException(string status) : base(status) { }
    }
    public sealed class GooglePurchaseProof
    {
        // Memory only. Neither string is serialized to ordinary progress.
        public string OriginalJson { get; private set; }
        public string TokenHash { get; private set; }
        public string Sku { get; private set; }
        public long PurchasedUtcMs { get; private set; }
        public static GooglePurchaseProof Parse(string wrapper, string transactionId, string sku, string accountHash)
        {
            try
            {
                var settings = new Newtonsoft.Json.Linq.JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error };
                var root = JObject.Parse(wrapper, settings);
                var payload = JObject.Parse(PaymentRules.String(root, "Payload"), settings);
                var original = PaymentRules.String(payload, "json");
                var receipt = JObject.Parse(original, settings);
                var token = PaymentRules.String(receipt, "purchaseToken");
                if (string.IsNullOrWhiteSpace(token) || token.Length > 16384 || string.IsNullOrWhiteSpace(transactionId) || PaymentRules.String(root, "Store") != "GooglePlay" ||
                    PaymentRules.String(root, "TransactionID") != transactionId || token != transactionId ||
                    PaymentRules.String(receipt, "packageName") != PaymentRules.Package ||
                    PaymentRules.String(receipt, "productId") != sku || !PaymentRules.Products.ContainsKey(sku) ||
                    receipt["purchaseState"]?.Type != JTokenType.Integer || (int)receipt["purchaseState"] != 0 ||
                    receipt["quantity"] != null && (receipt["quantity"].Type != JTokenType.Integer || (int)receipt["quantity"] != 1) ||
                    receipt["obfuscatedAccountId"] != null && PaymentRules.String(receipt, "obfuscatedAccountId") != accountHash ||
                    string.IsNullOrEmpty(PaymentRules.String(payload, "signature")))
                    throw new PaymentException("receipt_mismatch");
                return new GooglePurchaseProof { OriginalJson = original, Sku = sku, TokenHash = OutpostJson.Sha256Hex(token),
                    PurchasedUtcMs = receipt["purchaseTime"]?.Type == JTokenType.Integer ? (long)receipt["purchaseTime"] : 0 };
            }
            catch (PaymentException) { throw; }
            catch { throw new PaymentException("receipt_malformed"); }
        }
    }
    public static class PaymentRules
    {
        public const string Package = "com.multicastgames.catscuteminer";
        public const string GameId = "gm_cats_cute_miner";
        public const string BaseUrl = "https://backend.api-multicastgames.org";
        public static readonly IReadOnlyDictionary<string, long> Products = new Dictionary<string, long>(StringComparer.Ordinal) {
            { "gems_handful", 160 }, { "gems_sack", 500 }, { "gems_bag", 1200 },
            { "gems_crate", 2500 }, { "gems_cart", 6500 }, { "gems_pile", 14000 }
        };
        public static string String(JToken root, string key) => root is JObject obj && obj[key]?.Type == JTokenType.String ? (string)obj[key] : null;
        public static string Id(JToken root, string key) => Identifier(String(root, key));
        static string Identifier(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || id.Any(char.IsWhiteSpace) || id.Any(char.IsControl)) throw new PaymentException("malformed_id");
            return id;
        }
        public static void Items(JToken items, string sku)
        {
            if (!Products.TryGetValue(sku, out long gems) || !(items is JArray a) || a.Count != 1 || !(a[0] is JObject) ||
                String(a[0], "sku") != "gems" || a[0]["quantity"]?.Type != JTokenType.Integer ||
                (long)a[0]["quantity"] != gems || a[0]["nestedItems"] != null && a[0]["nestedItems"].Type != JTokenType.Null)
                throw new PaymentException("reward_mismatch");
        }
        public static void Catalog(JObject json)
        {
            if (!(json["products"] is JArray products)) throw new PaymentException("catalog_malformed");
            foreach (var pair in Products)
            {
                var matches = products.Where(p => String(p, "productSku") == pair.Key).ToList();
                if (matches.Count != 1 || String(matches[0], "googlePlaySku") != pair.Key || String(matches[0], "productType") != "Consumable")
                    throw new PaymentException("catalog_mismatch");
                Items(matches[0]["rewards"], pair.Key);
            }
        }
        public static void LocalCatalog(StoreCatalog catalog)
        {
            foreach (var pair in Products)
                if (catalog == null || !catalog.TryGet(pair.Key, out var d) || d.Currency != StoreCurrency.Real ||
                    d.Kind != StoreItemKind.Gems || d.Amount != pair.Value || d.BonusAmount != 0 || d.BonusPercent != 0)
                    throw new PaymentException("local_catalog_mismatch");
            if (catalog.Items.Count(d => d.Currency == StoreCurrency.Real) != Products.Count) throw new PaymentException("local_catalog_mismatch");
        }
        public static void Ledger(OutpostProfile profile)
        {
            if (profile.Payments == null) throw new PaymentException("ledger_missing");
            var tokens = new HashSet<string>(); var payments = new HashSet<string>(); var rewards = new HashSet<string>();
            foreach (var d in profile.Payments)
            {
                if (d == null || !d.Granted || string.IsNullOrEmpty(d.TokenHash) || d.TokenHash.Length != 64 ||
                    d.TokenHash.Any(c => !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) ||
                    !Products.TryGetValue(d.Sku ?? "", out var quantity) || d.Gems != quantity ||
                    d.Environment != "production" && d.Environment != "sandbox" || !tokens.Add(d.TokenHash) ||
                    !payments.Add(d.PaymentId ?? "") || !rewards.Add(d.RewardId ?? "")) throw new PaymentException("ledger_malformed");
                Identifier(d.UserId); Identifier(d.OrderId); Identifier(d.PaymentId); Identifier(d.RewardId);
            }
            if (profile.PendingPurchase != null)
            {
                var intent = profile.PendingPurchase;
                if (!Products.ContainsKey(intent.Sku ?? "")) throw new PaymentException("intent_malformed");
                Identifier(intent.UserId); Identifier(intent.OrderId);
            }
        }
        public static void PreserveAcrossReset(OutpostProfile previous, OutpostProfile next)
        {
            if (previous == null) return;
            Ledger(previous);
            if (Pending(previous)) throw new PaymentException("purchase_pending");
            next.Payments = OutpostJson.DeepCopy(previous.Payments);
        }
        public static bool Pending(OutpostProfile p) => p?.PendingPurchase != null || p?.Payments?.Any(x => !x.Claimed || !x.Confirmed) == true;
    }
    public interface IPaymentBackend
    {
        string UserId { get; }
        Task<JObject> Request(string path, JObject body = null);
    }
    public interface IPaymentProgress
    {
        bool Current { get; }
        OutpostProfile Profile { get; }
        // Must clone latest state, credit and ledger in the same durable transaction. No confirmation on failure.
        Task Save(Action<OutpostProfile> change);
    }
    public interface IPaymentRevenueProgress : IPaymentProgress
    {
        // Best-effort atomic write; failures must not enter gameplay's blocking RetrySave flow.
        Task SaveRevenue(Action<OutpostProfile> change);
    }
    public sealed class PaymentPipeline
    {
        readonly IPaymentBackend backend;
        readonly IPaymentProgress progress;
        public PaymentPipeline(IPaymentBackend backend, IPaymentProgress progress) { this.backend = backend; this.progress = progress; }
        void Current() { if (!progress.Current) throw new PaymentException("stale_context"); PaymentRules.Ledger(progress.Profile); }
        async Task<JObject> Request(string path, JObject body = null)
        {
            Current(); var user = backend.UserId;
            if (string.IsNullOrWhiteSpace(user)) throw new PaymentException("authentication_required");
            var result = await backend.Request(path, body); Current();
            if (backend.UserId != user) throw new PaymentException("account_mismatch");
            return result;
        }
        public async Task Prepare(string sku, string price, string currency)
        {
            Current();
            var startedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var priceMicros = PaymentRevenue.PriceMicros(price, currency);
            if (PaymentRules.Pending(progress.Profile) || !PaymentRules.Products.ContainsKey(sku)) throw new PaymentException("purchase_pending");
            PaymentRules.Catalog(await Request("/api/products/get"));
            var order = await Request("/api/orders/create", new JObject { ["productSku"] = sku });
            var id = PaymentRules.Id(order, "orderId");
            PaymentRules.Items(order["rewardItems"], sku);
            await progress.Save(p => p.PendingPurchase = new PurchaseIntent { UserId = backend.UserId, OrderId = id, Sku = sku, Price = price, Currency = currency,
                PriceMicros = priceMicros, StartedUtcMs = startedUtcMs });
            Current();
        }
        public async Task<PaymentDelivery> ValidateAndDeliver(GooglePurchaseProof proof, bool protectedPlayer, string freshOrderId = null)
        {
            Current();
            var existing = progress.Profile.Payments.SingleOrDefault(x => x.TokenHash == proof.TokenHash);
            if (existing != null && (existing.UserId != backend.UserId || existing.Sku != proof.Sku)) throw new PaymentException("account_mismatch");
            var intent = progress.Profile.PendingPurchase;
            if (existing == null && (intent == null || intent.UserId != backend.UserId || intent.Sku != proof.Sku)) throw new PaymentException("unknown_purchase");
            var payment = await Request("/api/payment/google-play", new JObject { ["receipt"] = proof.OriginalJson });
            var paymentId = PaymentRules.Id(payment, "paymentId"); var rewardId = PaymentRules.Id(payment, "rewardId");
            var environment = PaymentRules.String(payment, "environment");
            if (environment != "production" && environment != "sandbox") throw new PaymentException("unknown_environment");
            if (existing != null)
            {
                if (!existing.Granted || existing.PaymentId != paymentId || existing.RewardId != rewardId || existing.Environment != environment || existing.Gems != PaymentRules.Products[proof.Sku])
                    throw new PaymentException("replay_mismatch");
                return existing;
            }
            if (progress.Profile.Payments.Any(x => x.PaymentId == paymentId || x.RewardId == rewardId)) throw new PaymentException("replay_mismatch");
            var inbox = await Request("/api/rewards/get-all-unclaimed", new JObject());
            if (!(inbox["rewards"] is JArray rewards)) throw new PaymentException("inbox_malformed");
            var matching = rewards.Where(r => PaymentRules.String(r, "rewardId") == rewardId).ToList();
            if (matching.Count != 1) throw new PaymentException("unknown_claimed_reward");
            PaymentRules.Items(matching[0]["items"], proof.Sku);
            // Freshness is session-local: fetched/restored orders cannot authorize monetary analytics.
            bool fresh = freshOrderId != null && freshOrderId == intent.OrderId && intent.StartedUtcMs > 0 &&
                proof.PurchasedUtcMs >= intent.StartedUtcMs && proof.PurchasedUtcMs <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() &&
                intent.PriceMicros > 0 && PaymentRevenue.PriceMicros(intent.Price, intent.Currency) == intent.PriceMicros;
            var delivery = new PaymentDelivery { TokenHash = proof.TokenHash, UserId = backend.UserId, OrderId = intent.OrderId,
                Sku = proof.Sku, PaymentId = paymentId, RewardId = rewardId, Environment = environment,
                Gems = PaymentRules.Products[proof.Sku], Granted = true,
                PriceMicros = fresh ? intent.PriceMicros : 0, Currency = fresh ? intent.Currency : null,
                NewRevenueEligible = fresh && environment == "production" && !protectedPlayer,
                RevenueStatus = environment == "sandbox" ? "excluded_sandbox" : protectedPlayer ? "excluded_privacy" :
                    fresh ? "pending_recipients" : "excluded_recovery_or_unknown_price" };
            await progress.Save(p => {
                if (p.Payments.Any(x => x.TokenHash == delivery.TokenHash || x.PaymentId == paymentId || x.RewardId == rewardId)) throw new PaymentException("replay_mismatch");
                p.Gems = checked(p.Gems + delivery.Gems); p.Payments.Add(delivery); p.PendingPurchase = null;
            });
            Current(); return progress.Profile.Payments.Single(x => x.TokenHash == proof.TokenHash);
        }
        public async Task Claim(PaymentDelivery delivery)
        {
            Current(); Check(delivery);
            if (delivery.Claimed) return;
            await Request("/api/rewards/set-claimed", new JObject { ["rewardId"] = delivery.RewardId });
            await progress.Save(p => p.Payments.Single(x => x.TokenHash == delivery.TokenHash).Claimed = true); Current();
        }
        public async Task Confirmed(string hash)
        {
            Current(); var delivery = progress.Profile.Payments.Single(x => x.TokenHash == hash); Check(delivery);
            await progress.Save(p => p.Payments.Single(x => x.TokenHash == hash).Confirmed = true); Current();
        }
        void Check(PaymentDelivery d)
        {
            if (d == null || !d.Granted || d.UserId != backend.UserId ||
                !progress.Profile.Payments.Any(x => x.TokenHash == d.TokenHash && x.PaymentId == d.PaymentId && x.RewardId == d.RewardId && x.Granted))
                throw new PaymentException("unknown_delivery");
        }
    }
}

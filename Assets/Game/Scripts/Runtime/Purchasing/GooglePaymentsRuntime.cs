using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CatMiners.Core.Outpost;
using CatMiners.Runtime.Integrations;
using CatMiners.Runtime.Outpost;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Purchasing;
#endif

namespace CatMiners.Runtime.Purchasing
{
    // Purposeful singleton. Billing does not depend on ad/analytics provider readiness.
    public sealed class GooglePaymentsRuntime : MonoBehaviour
    {
        public static GooglePaymentsRuntime Instance { get; private set; }
        public string Status { get; private set; } = "Loading store";
        public bool Busy { get; private set; }
        public bool Ready { get; private set; }
        public bool Pending => Busy || Processing || PaymentRules.Pending(owner?.State?.Profile);
        bool Processing {
            get {
#if UNITY_ANDROID && !UNITY_EDITOR
                return processing || billing || fetchWaiting;
#else
                return false;
#endif
            }
        }
        OutpostController owner;
        IPaymentProgress progress;
        PrivateGuestSession backend;
        PaymentPipeline pipeline;
        PaymentRevenue revenue;
        float revenueRetryAt;
        int generation;
        readonly ConcurrentQueue<Action> callbacks = new ConcurrentQueue<Action>();
        readonly Dictionary<string, string> prices = new Dictionary<string, string>();
        public string Price(string sku) => prices.TryGetValue(sku, out var value) ? value : null;
        public static GooglePaymentsRuntime Attach(OutpostController controller, IPaymentProgress context)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Instance == null) { Instance = new GameObject("Google payments").AddComponent<GooglePaymentsRuntime>(); DontDestroyOnLoad(Instance.gameObject); }
            if (Instance.owner == controller && Instance.progress == context) return Instance;
            Instance.revenue = context is IPaymentRevenueProgress rp ? new PaymentRevenue(rp) : null;
            Instance.generation++; Instance.owner = controller; Instance.progress = context; Instance.Ready = false;
            Instance.StartStore();
            return Instance;
#else
            return null;
#endif
        }
        public void Detach(OutpostController controller)
        {
            if (owner != controller) return;
            generation++; owner = null; progress = null; pipeline = null; revenue = null; Ready = false;
        }
        bool Current(int g) => g == generation && owner != null && progress?.Current == true;
        void Message(string text) { Status = text; owner?.Hud?.RefreshNow(); }
        public void OpenStore() { Telemetry(GameEvent.StoreOpened); if (!Ready && !Busy) Enable(); }
        static void Telemetry(GameEvent kind) => GameSdkRuntime.Instance?.Track(kind);
        public void Enable()
        {
            if (Busy || owner == null) return;
            int g = generation;
            ParentPurchaseGate.Show(owner.gameObject, () => { if (Current(g)) EnableCatalog(); });
        }
        async void EnableCatalog()
        {
            int g = generation;
            if (!Current(g) || Busy) return;
            Busy = true; Message("Checking store catalog");
            try
            {
                EnsureBackend(); await backend.Authenticate(); if (!Current(g)) return;
                PaymentRules.Catalog(await backend.Request("/api/products/get")); if (!Current(g)) return;
#if UNITY_ANDROID && !UNITY_EDITOR
                catalogReady = true; RefreshReady();
#endif
                Message(Ready ? "Store ready" : "Waiting for store prices");
            }
            catch (Exception e) { if (Current(g)) { Ready = false; Telemetry(GameEvent.PurchaseFailed); Message(SafeError(e)); } }
            finally { if (g == generation) Busy = false; }
        }
        void EnsureBackend()
        {
            if (backend == null) backend = new PrivateGuestSession(progress.Profile.PendingPurchase != null || progress.Profile.Payments.Count > 0);
            pipeline = new PaymentPipeline(backend, progress);
        }
        public bool Buy(string sku)
        {
            if (!Ready || Busy || Pending || owner == null || !owner.CanUseMoneyStore || !prices.ContainsKey(sku)) return false;
            int g = generation;
            ParentPurchaseGate.Show(owner.gameObject, () => { if (Current(g)) Purchase(sku); }); return true;
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        async void Purchase(string sku)
        {
            int g = generation;
            if (!Current(g) || !Ready || Busy || Pending || !owner.CanUseMoneyStore || !products.TryGetValue(sku, out var product)) return;
            Busy = true; Telemetry(GameEvent.PurchaseStarted); Message("Preparing purchase");
            try
            {
                EnsureBackend(); await backend.Authenticate(); if (!Current(g)) return;
                await pipeline.Prepare(sku, product.metadata.localizedPrice.ToString(System.Globalization.CultureInfo.InvariantCulture), product.metadata.isoCurrencyCode);
                if (!Current(g)) return;
                freshOrderId = progress.Profile.PendingPurchase?.OrderId;
                // Guest is already durable; SHA256 is rejection-only account binding, never authentication.
                store.GooglePlayStoreExtendedService.SetObfuscatedAccountId(OutpostJson.Sha256Hex(backend.UserId));
                saleGeneration = g; billing = true; waitingSince = Time.unscaledTime; Message("Waiting for Google Play"); store.PurchaseProduct(product);
            }
            catch (Exception e) { if (Current(g)) { Telemetry(GameEvent.PurchaseFailed); Message(SafeError(e)); } }
            finally { if (g == generation) Busy = false; }
        }
#else
        void Purchase(string sku) { }
#endif
        public void Recover(bool retryPrices = true)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (owner == null || Busy || processing || billing) return;
            if (!connected) { StartStore(); return; }
            if (store == null || fetchWaiting) return;
            if (retryPrices && prices.Count != PaymentRules.Products.Count) { FetchProducts(); return; }
            Message("Checking unfinished purchases (consumed gems are not restored)");
            try { if (!fence.BeginQuery(generation)) return; fetchWaiting = true; waitingSince = Time.unscaledTime; Telemetry(GameEvent.PurchaseRestore); store.FetchPurchases(); }
            catch { fence.EndQuery(generation); fetchWaiting = false; Telemetry(GameEvent.PurchaseFailed); Message("Store unavailable — try recovery again"); }
            retryAt = 0;
#endif
        }
        async void DispatchRevenue()
        {
            try { if (revenue != null) await revenue.Dispatch(GameSdkRuntime.Instance); }
            catch { /* Analytics never controls billing readiness or recovery. */ }
        }
        static string SafeError(Exception error) => error is PaymentException p ? p.Message + " — purchase remains recoverable" : "Purchase unavailable — try recovery again";
        void Update()
        {
            for (int n = 0; n < 32 && callbacks.TryDequeue(out var callback); n++)
                try { callback(); } catch { Message("Purchase callback failed — try recovery"); }
#if UNITY_ANDROID && !UNITY_EDITOR
            if (owner == null || progress?.Current != true) return;
            // Run independently of grant/claim/consume, including fully finished purchases waiting for config/consent.
            if (Time.unscaledTime >= revenueRetryAt && !Busy && !processing && !billing && !fetchWaiting)
            { revenueRetryAt = Time.unscaledTime + 10; DispatchRevenue(); }
            if ((billing || fetchWaiting || connecting || pricesWaiting) && Time.unscaledTime - waitingSince > 45)
            {
                // A query without an operation ID cannot be overlapped safely with another query/new sale.
                // Keep its barrier until a callback or relaunch; a late empty inventory must never clear a new sale.
                billing = connecting = pricesWaiting = false; Ready = false;
                Message(fetchWaiting ? "Recovery response delayed — restart the game to retry safely" : "Store response delayed — use recovery");
                waitingSince = Time.unscaledTime;
            }
            if (!Busy && !processing && !fetchWaiting && Time.unscaledTime >= retryAt) ProcessNext();
#endif
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        StoreController store;
        bool connected, connecting, pricesWaiting, catalogReady, billing, fetchWaiting, processing;
        float waitingSince, retryAt;
        int failures, saleGeneration = -1;
        string freshOrderId;
        readonly Dictionary<string, string> freshOrders = new Dictionary<string, string>();
        readonly PaymentCallbackFence fence = new PaymentCallbackFence();
        readonly Dictionary<string, Product> products = new Dictionary<string, Product>();
        readonly Dictionary<string, PendingOrder> pending = new Dictionary<string, PendingOrder>();
        readonly HashSet<string> confirmed = new HashSet<string>();
        void Queue(Action callback)
        {
            int g = generation; callbacks.Enqueue(() => { if (Current(g)) callback(); });
        }
        void QueueInventory(Action callback)
        {
            int g = generation;
            callbacks.Enqueue(() => {
                bool matches = fence.EndQuery(g); fetchWaiting = false;
                if (!Current(g)) return;
                if (!matches) { Recover(false); return; }
                callback();
            });
        }
        async void StartStore()
        {
            int g = generation;
            Busy = false; processing = false; pending.Clear(); confirmed.Clear(); freshOrders.Clear(); freshOrderId = null; catalogReady = false;
            try
            {
                PaymentRules.LocalCatalog(owner.Configs.Store);
                if (Application.identifier != PaymentRules.Package) throw new PaymentException("package_mismatch");
                if (store == null)
                {
                    store = UnityIAPServices.StoreController();
                    store.ProcessPendingOrdersOnPurchasesFetched(false);
                    store.OnStoreConnected += Connected;
                    store.OnStoreDisconnected += Disconnected;
                    store.OnProductsFetched += ProductsFetched;
                    store.OnProductsFetchFailed += ProductsFailed;
                    store.OnPurchasePending += PurchasePending;
                    store.OnPurchaseDeferred += Deferred;
                    store.OnPurchaseFailed += Failed;
                    store.OnPurchaseConfirmed += Confirmed;
                    store.OnPurchasesFetched += PurchasesFetched;
                    store.OnPurchasesFetchFailed += PurchasesFailed;
                }
                if (connected) { FetchProducts(); return; }
                connecting = true; waitingSince = Time.unscaledTime;
                await store.Connect(); if (!Current(g)) return;
            }
            catch (Exception e) { if (Current(g)) { connecting = false; Telemetry(GameEvent.PurchaseFailed); Message(SafeError(e)); } }
        }
        void Connected() => Queue(() => { connected = true; connecting = false; FetchProducts(); });
        void Disconnected(StoreConnectionFailureDescription d) => Queue(() => { connected = Ready = false; connecting = false; Telemetry(GameEvent.PurchaseFailed); Message("Store disconnected — use recovery"); });
        void FetchProducts()
        {
            pricesWaiting = true; waitingSince = Time.unscaledTime;
            store.FetchProducts(PaymentRules.Products.Keys.Select(s => new ProductDefinition(s, s, ProductType.Consumable)).ToList());
        }
        void ProductsFetched(List<Product> fetched) => Queue(() => {
            pricesWaiting = false; prices.Clear(); products.Clear();
            foreach (var sku in PaymentRules.Products.Keys)
            {
                var matching = fetched.Where(p => p.definition.id == sku).ToList();
                if (matching.Count != 1) continue;
                var p = matching[0];
                if (p.definition.type != ProductType.Consumable || p.definition.storeSpecificId != sku || !p.availableToPurchase ||
                    p.metadata == null || p.metadata.localizedPrice <= 0 || string.IsNullOrWhiteSpace(p.metadata.localizedPriceString) ||
                    p.metadata.isoCurrencyCode == null || p.metadata.isoCurrencyCode.Length != 3 || !p.metadata.isoCurrencyCode.All(c => c >= 'A' && c <= 'Z')) continue;
                products.Add(sku, p); prices.Add(sku, p.metadata.localizedPriceString);
            }
            RefreshReady(); Message(Ready ? "Store ready" : "Store prices or server catalog unavailable");
            if (!billing && !processing) Recover(false);
        });
        void RefreshReady() { Ready = connected && catalogReady && prices.Count == PaymentRules.Products.Count; }
        void ProductsFailed(ProductFetchFailed d) => Queue(() => { pricesWaiting = false; Ready = false; Telemetry(GameEvent.PurchaseFailed); Message("Store prices unavailable — retry recovery"); });
        void PurchasePending(PendingOrder order) => Queue(() => {
            if (saleGeneration != generation || !billing) { Recover(false); return; }
            billing = false;
            Telemetry(GameEvent.PurchasePending);
            if (order?.Info != null && !string.IsNullOrWhiteSpace(order.Info.TransactionID) && freshOrderId != null)
                freshOrders[OutpostJson.Sha256Hex(order.Info.TransactionID)] = freshOrderId;
            freshOrderId = null; AddPending(order);
        });
        void AddPending(PendingOrder order)
        {
            if (order?.Info == null || string.IsNullOrWhiteSpace(order.Info.TransactionID)) { Message("Malformed store order — no gems delivered"); return; }
            pending[OutpostJson.Sha256Hex(order.Info.TransactionID)] = order; retryAt = 0; Message("Validating purchase");
        }
        void Deferred(DeferredOrder order) => Queue(() => { if (saleGeneration != generation) return; billing = false; Telemetry(GameEvent.PurchasePending); Message("Payment pending approval — no gems delivered"); });
        void Failed(FailedOrder order) => Queue(() => { if (saleGeneration != generation) return; billing = false; Telemetry(order != null && order.FailureReason == PurchaseFailureReason.UserCancelled ? GameEvent.PurchaseCancelled : GameEvent.PurchaseFailed); Message("Purchase cancelled or failed — use recovery to check"); });
        void Confirmed(Order order)
        {
            int g = generation;
            callbacks.Enqueue(() => {
                if (order?.Info == null || string.IsNullOrWhiteSpace(order.Info.TransactionID)) { if (Current(g)) Message("Consumption result unknown — use recovery"); return; }
                var hash = OutpostJson.Sha256Hex(order.Info.TransactionID);
                var result = fence.EndConsume(hash, g, backend?.UserId, order is FailedOrder);
                if (!Current(g) || result == PaymentConfirmationResult.Ignored) return;
                if (result == PaymentConfirmationResult.Failed) { Telemetry(GameEvent.PurchaseFailed); Message("Consumption failed — will retry"); retryAt = Time.unscaledTime + Mathf.Min(120, Mathf.Pow(2, Mathf.Min(++failures, 7))); return; }
                if (progress.Profile.Payments.Any(x => x.TokenHash == hash && x.Granted && x.UserId == backend.UserId)) { confirmed.Add(hash); retryAt = 0; }
            });
        }
        void PurchasesFetched(Orders orders) => QueueInventory(() => {
            fetchWaiting = billing = false;
            foreach (var order in orders.PendingOrders) AddPending(order);
            var live = new HashSet<string>(orders.PendingOrders.Select(o => OutpostJson.Sha256Hex(o.Info.TransactionID)));
            foreach (var hash in pending.Keys.Where(h => !live.Contains(h)).ToList()) pending.Remove(hash);
            if (orders.DeferredOrders.Count > 0) Message("Payment pending approval — no gems delivered");
            else if (orders.PendingOrders.Count == 0) {
                Message(progress.Profile.Payments.Any(d => d.Granted && !d.Confirmed) ? "Gems saved; consumption result unavailable for this store account" : "No unfinished Google purchases; consumed gems are not restored");
                ClearAbandonedIntent();
            }
        });
        async void ClearAbandonedIntent()
        {
            int g = generation;
            if (processing || Busy || progress.Profile.PendingPurchase == null) return;
            Busy = true;
            try { await progress.Save(p => p.PendingPurchase = null); }
            catch { if (Current(g)) Message("Could not save recovery result"); }
            finally { if (g == generation) Busy = false; }
        }
        void PurchasesFailed(PurchasesFetchFailureDescription d) => QueueInventory(() => { fetchWaiting = false; Telemetry(GameEvent.PurchaseFailed); Message("Recovery unavailable — try again"); });
        async void ProcessNext()
        {
            if (pending.Count == 0 && confirmed.Count == 0 && !progress.Profile.Payments.Any(x => x.Granted && !x.Claimed)) return;
            int g = generation; processing = true;
            try
            {
                EnsureBackend(); await backend.Authenticate(); if (!Current(g)) return;
                if (confirmed.Count > 0)
                {
                    var hash = confirmed.First();
                    var d = progress.Profile.Payments.SingleOrDefault(x => x.TokenHash == hash);
                    if (d != null && d.UserId == backend.UserId) { bool first = !d.Confirmed; await pipeline.Confirmed(hash); if (!Current(g)) return; if (first) Telemetry(GameEvent.PurchaseConfirmed); confirmed.Remove(hash); pending.Remove(hash); }
                    else throw new PaymentException("account_mismatch");
                }
                else if (pending.Count > 0)
                {
                    var pair = pending.First(); var order = pair.Value; var cart = order.CartOrdered.Items();
                    if (cart == null || cart.Count != 1 || cart[0] == null || cart[0].Quantity != 1 || cart[0].Product?.definition == null || cart[0].Product.definition.type != ProductType.Consumable || cart[0].Product.definition.id != cart[0].Product.definition.storeSpecificId) throw new PaymentException("cart_mismatch");
                    var proof = GooglePurchaseProof.Parse(order.Info.Receipt, order.Info.TransactionID, cart[0].Product.definition.id, OutpostJson.Sha256Hex(backend.UserId));
                    freshOrders.TryGetValue(pair.Key, out var freshIntent);
                    bool alreadyGranted = progress.Profile.Payments.Any(x => x.TokenHash == pair.Key && x.Granted);
                    Telemetry(GameEvent.PurchaseValidationStarted);
                    var d = await pipeline.ValidateAndDeliver(proof, GameSdkRuntime.Instance?.Privacy?.Protected != false, freshIntent); if (!Current(g)) return;
                    Telemetry(GameEvent.PurchaseValidated);
                    if (!alreadyGranted && d.Granted) Telemetry(GameEvent.PurchaseGranted);
                    // Claim and consumption retry independently. Claim failure never cancels a durable grant.
                    try { await pipeline.Claim(d); } catch (Exception e) { if (Current(g)) { Telemetry(GameEvent.PurchaseFailed); Message(SafeError(e)); } }
                    if (!Current(g)) return;
                    if (!d.Confirmed)
                    {
                        if (fence.BeginConsume(pair.Key, g, backend.UserId))
                            try { store.ConfirmPurchase(order); } catch { fence.AbortConsume(pair.Key); throw; }
                        Message("Gems saved — finishing purchase");
                    }
                    else pending.Remove(pair.Key);
                }
                else
                {
                    var d = progress.Profile.Payments.First(x => x.Granted && !x.Claimed);
                    await pipeline.Claim(d); if (!Current(g)) return;
                }
                failures = 0; retryAt = Time.unscaledTime + 5;
            }
            catch (Exception e) { if (Current(g)) { Telemetry(GameEvent.PurchaseFailed); Message(SafeError(e)); retryAt = Time.unscaledTime + Mathf.Min(120, Mathf.Pow(2, Mathf.Min(++failures, 7))); } }
            finally { if (g == generation) processing = false; }
        }
#endif
        void OnDestroy()
        {
            if (Instance != this) return; generation++; Instance = null;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (store == null) return;
            store.OnStoreConnected -= Connected; store.OnStoreDisconnected -= Disconnected;
            store.OnProductsFetched -= ProductsFetched; store.OnProductsFetchFailed -= ProductsFailed;
            store.OnPurchasePending -= PurchasePending; store.OnPurchaseDeferred -= Deferred; store.OnPurchaseFailed -= Failed;
            store.OnPurchaseConfirmed -= Confirmed; store.OnPurchasesFetched -= PurchasesFetched; store.OnPurchasesFetchFailed -= PurchasesFailed;
#endif
        }
    }
}

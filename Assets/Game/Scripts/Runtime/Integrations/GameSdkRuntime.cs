using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CatMiners.Core.Outpost;
using Newtonsoft.Json;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using Io.AppMetrica;
using AdjustSdk;
using Facebook.Unity;
#endif

namespace CatMiners.Runtime.Integrations
{
    // One installation-lifetime owner. SDK callbacks enter gameplay only from Update on Unity's main thread.
    public sealed partial class GameSdkRuntime : MonoBehaviour, IPaymentRevenueSink
    {
        public static GameSdkRuntime Instance { get; private set; }
        public ProviderState AppMetricaState { get; private set; }
        public ProviderState AdjustState { get; private set; }
        public ProviderState MetaState { get; private set; }
        public ProviderState MaxState { get; private set; }
        public bool AdjustSessionReceived { get; private set; }
        public bool AdjustAttributionReceived { get; private set; }
        public string RewardStatus { get; private set; } = "unavailable";
        public IntegrationPrivacy Privacy { get; private set; }
        public GameSdkConfiguration Config { get; private set; }
        readonly ConcurrentQueue<Action> callbacks = new ConcurrentQueue<Action>();
        public void Post(Action action) => callbacks.Enqueue(action);
        public static GameSdkRuntime Begin(IntegrationPrivacy privacy)
        {
            if (Instance != null) return Instance;
            Instance = new GameObject("Game SDK services").AddComponent<GameSdkRuntime>();
            DontDestroyOnLoad(Instance.gameObject);
            Instance.Privacy = privacy;
            if (!GameSdkConfiguration.AdsEnabled && privacy != null) privacy.Ads = false;
            Instance.Config = GameSdkConfiguration.Load();
            Instance.RefreshConsent();
            McLog.Write(McLogCode.SessionStart);
            Instance.Initialize();
            Instance.Track(GameEvent.ConsentChosen);
            Instance.Track(GameEvent.SessionStarted);
            return Instance;
        }
        void Update()
        {
            for (int n = 0; n < 64 && callbacks.TryDequeue(out var action); n++)
                try { action(); } catch (Exception) { McLog.Write(McLogCode.CallbackFailed); }
            RefreshConsent();
            TickGameplayClock();
            FlushTelemetry();
            ObserveProviders();
#if UNITY_ANDROID && !UNITY_EDITOR && CATMINERS_MAX
            if (MaxState == ProviderState.Initialized && Privacy.MayInitialize(Privacy.Ads) && activePlacement == null && Time.unscaledTime >= nextLoad && !MaxSdk.IsRewardedAdReady(Config.rewardedAdUnit))
            { nextLoad = float.PositiveInfinity; Track(GameEvent.AdRequested);
                try { MaxSdk.LoadRewardedAd(Config.rewardedAdUnit); }
                catch { Track(GameEvent.AdLoadFailed); nextLoad = Time.unscaledTime + 30; } }
#endif
        }
        public static bool ManualRevenueSafe
        {
            get {
#if APPMETRICA_FEATURES_ADREVENUE_APPLOVIN_V8
                return false;
#else
                return true;
#endif
            }
        }
        bool appMetricaStarted, adjustStarted, metaStarted, maxStarted;
        public void ApplyPrivacy(IntegrationPrivacy privacy)
        {
            TickGameplayClock();
            Privacy = privacy;
            if (!GameSdkConfiguration.AdsEnabled && privacy != null) privacy.Ads = false;
            RefreshConsent();
            Config = GameSdkConfiguration.Load();
#if UNITY_ANDROID && !UNITY_EDITOR
            if (appMetricaStarted) try
            { AppMetrica.SetDataSendingEnabled(Privacy != null && Privacy.MayInitialize(Privacy.AppMetrica));
              if (Privacy != null && Privacy.MayInitialize(Privacy.AppMetrica)) AppMetrica.ResumeSession(); else AppMetrica.PauseSession(); }
            catch { McLog.Write(McLogCode.CallbackFailed, McProvider.AppMetrica); }
            if (adjustStarted) try { if (Privacy != null && Privacy.MayInitialize(Privacy.Adjust)) Adjust.Enable(); else Adjust.Disable(); }
            catch { McLog.Write(McLogCode.CallbackFailed, McProvider.Adjust); }
#endif
            Initialize();
            Track(GameEvent.ConsentChosen);
        }
        void Initialize()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Application.identifier != GameSdkConfiguration.Package || Privacy == null || Privacy.Protected) return;
            if (Privacy.MayInitialize(Privacy.AppMetrica) && !appMetricaStarted)
            {
                AppMetricaState = ProviderState.WaitingForConfiguration;
                if (Config.AppMetricaValid && Config.LegalValid && ManualRevenueSafe) try
                {
                    StartProvider(McProvider.AppMetrica);
                    appMetricaStarted = true;
                    AppMetrica.Activate(new AppMetricaConfig(Config.appMetricaKey) {
                        LocationTracking = false, Location = null, DataSendingEnabled = true,
                        RevenueAutoTrackingEnabled = false, SessionsAutoTrackingEnabled = true,
                        CrashReporting = false, NativeCrashReporting = false, Logs = false });
                    AppMetricaState = ProviderState.Initialized;
                } catch { appMetricaStarted = false; AppMetricaState = ProviderState.Failed; }
            }
            if (Privacy.MayInitialize(Privacy.Adjust) && !adjustStarted)
            {
                AdjustState = ProviderState.WaitingForConfiguration;
                if (Config.AdjustValid && Config.LegalValid) try
                {
                    var c = new AdjustConfig(Config.adjustToken, AdjustEnvironment.Production) {
                        IsSendingInBackgroundEnabled = false, IsDeferredDeeplinkOpeningEnabled = false,
                        IsFbIdReadingEnabled = false, IsGoogleAdIdReadingEnabled = false,
                        IsAndroidIdReadingEnabled = false, IsFireAdIdReadingEnabled = false,
                        IsDeviceIdsFromPluginsReadingEnabled = false,
                        AttributionChangedDelegate = a => Post(() => AdjustAttributionReceived = a != null),
                        SessionSuccessDelegate = s => Post(() => { AdjustSessionReceived = s != null; AdjustState = ProviderState.Initialized; }),
                        SessionFailureDelegate = s => Post(() => AdjustState = ProviderState.Failed)
                    };
                    AdjustState = ProviderState.Starting;
                    StartProvider(McProvider.Adjust);
                    adjustStarted = true;
                    Adjust.InitSdk(c);
                } catch { adjustStarted = false; AdjustState = ProviderState.Failed; }
            }
            if (Privacy.MayInitialize(Privacy.Meta) && !metaStarted)
            {
                MetaState = ProviderState.WaitingForConfiguration;
                if (Config.MetaValid && Config.LegalValid && NativePrivacyConfiguration.MetaAutoCollectionDisabled()) try
                {
                    MetaState = ProviderState.Starting;
                    StartProvider(McProvider.Meta);
                    metaStarted = true;
                    FB.Init(Config.metaAppId, Config.metaClientToken, logging: false, onInitComplete: () => Post(() => {
                        if (!FB.IsInitialized) { metaStarted = false; MetaState = ProviderState.Failed; return; }
                        FB.Mobile.SetAutoLogAppEventsEnabled(false);
                        FB.Mobile.SetAdvertiserIDCollectionEnabled(false);
                        if (Privacy.MayInitialize(Privacy.Meta)) FB.ActivateApp();
                        MetaState = ProviderState.Initialized;
                    }));
                } catch { metaStarted = false; MetaState = ProviderState.Failed; }
            }
#if CATMINERS_MAX
            if (Privacy.MayInitialize(Privacy.Ads) && !maxStarted)
            {
                MaxState = ProviderState.WaitingForConfiguration;
                if (Config.MaxValid && Config.LegalValid && NativePrivacyConfiguration.MaxMatches(Config)) try
                {
                    StartProvider(McProvider.Max);
                    maxStarted = true;
                    SubscribeMax();
                    MaxState = ProviderState.Starting;
                    MaxSdk.SetSdkKey(Config.maxSdkKey);
                    // Never infer ad consent from an analytics toggle. MAX's configured native CMP collects it.
                    MaxSdk.InitializeSdk();
                } catch { maxStarted = false; MaxState = ProviderState.Failed; }
            }
#endif
#endif
        }
        public string Unavailable(PaymentRevenueRecipient recipient)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Application.identifier != GameSdkConfiguration.Package || Privacy == null || Privacy.Protected) return "waiting_for_consent";
            if (Config == null || !Config.LegalValid) return "not_configured";
            switch (recipient)
            {
                case PaymentRevenueRecipient.AppMetrica:
                    if (!Privacy.MayInitialize(Privacy.AppMetrica)) return "waiting_for_consent";
                    if (!Config.AppMetricaValid || !ManualRevenueSafe) return "not_configured";
                    return AppMetricaState == ProviderState.Initialized ? null : "waiting_for_initialization";
                case PaymentRevenueRecipient.Meta:
                    if (!Privacy.MayInitialize(Privacy.Meta)) return "waiting_for_consent";
                    if (!Config.MetaValid) return "not_configured";
                    return MetaState == ProviderState.Initialized && FB.IsInitialized ? null : "waiting_for_initialization";
                default:
                    if (!Privacy.MayInitialize(Privacy.Adjust)) return "waiting_for_consent";
                    if (!Config.AdjustPurchaseValid) return "not_configured";
                    return AdjustState == ProviderState.Initialized ? null : "waiting_for_initialization";
            }
#else
            return "not_configured";
#endif
        }
        public void Report(PaymentRevenueRecipient recipient, PaymentDelivery d)
        {
            if (!PaymentRevenue.Eligible(d) || Unavailable(recipient) != null) throw new InvalidOperationException("Revenue recipient unavailable");
#if UNITY_ANDROID && !UNITY_EDITOR
            // Stable backend IDs only. No receipt, signature, purchase token, account/device ID or token hash.
            var data = new Dictionary<string, object> { { "payment_id", d.PaymentId }, { "order_id", d.OrderId } };
            switch (recipient)
            {
                case PaymentRevenueRecipient.AppMetrica:
                    AppMetrica.ReportRevenue(new Io.AppMetrica.Revenue(d.PriceMicros, d.Currency) {
                        ProductID = d.Sku, Quantity = 1, Payload = JsonConvert.SerializeObject(data) });
                    break;
                case PaymentRevenueRecipient.Meta:
                    data["fb_content_id"] = d.Sku; data["fb_num_items"] = 1;
                    FB.LogPurchase(d.PriceMicros / 1000000m, d.Currency, data);
                    break;
                case PaymentRevenueRecipient.Adjust:
                    // v5.8.1 TransactionId is iOS-specific; Android uses DeduplicationId.
                    // Do not set PurchaseToken or use receipt validation APIs for analytics.
                    var e = new AdjustEvent(Config.adjustPurchaseEventToken) { ProductId = d.Sku, DeduplicationId = d.PaymentId };
                    e.SetRevenue(d.PriceMicros / 1000000d, d.Currency);
                    e.AddCallbackParameter("payment_id", d.PaymentId); e.AddCallbackParameter("order_id", d.OrderId);
                    Adjust.TrackEvent(e);
                    break;
            }
            McLog.Write(McLogCode.Revenue, recipient == PaymentRevenueRecipient.Adjust ? McProvider.Adjust : recipient == PaymentRevenueRecipient.Meta ? McProvider.Meta : McProvider.AppMetrica);
#endif
        }
        public static string EventName(GameEvent kind)
        {
            switch (kind)
            {
                case GameEvent.GameLoaded: return "game_loaded";
                case GameEvent.ConsentChosen: return "consent_chosen";
                case GameEvent.MineStarted: return "mine_started";
                case GameEvent.MineCompleted: return "mine_completed";
                case GameEvent.SectorOpened: return "sector_opened";
                case GameEvent.UpgradePurchased: return "upgrade_purchased";
                case GameEvent.CardUpgraded: return "card_upgraded";
                case GameEvent.ChestOpened: return "chest_opened";
                case GameEvent.StorePurchased: return "store_purchased";
                case GameEvent.RewardOffered: return "rewarded_offered";
                case GameEvent.RewardEarned: return "rewarded_earned";
                case GameEvent.RewardDelivered: return "rewarded_delivered";
                case GameEvent.RewardClosed: return "rewarded_closed";
                case GameEvent.RewardFailed: return "rewarded_failed";
                case GameEvent.TimeSpent: return "time_spent";
                case GameEvent.SessionStarted: return "session_started";
                case GameEvent.SessionEnded: return "session_ended";
                case GameEvent.AdRequested: return "ad_requested";
                case GameEvent.AdLoaded: return "ad_loaded";
                case GameEvent.AdLoadFailed: return "ad_load_failed";
                case GameEvent.AdShow: return "ad_show";
                case GameEvent.AdDisplayed: return "ad_displayed";
                case GameEvent.AdDisplayFailed: return "ad_display_failed";
                case GameEvent.AdShowFailed: return "ad_show_failed";
                case GameEvent.AdClicked: return "ad_clicked";
                case GameEvent.AdHidden: return "ad_hidden";
                case GameEvent.AdCompleted: return "ad_completed";
                case GameEvent.AdIncomplete: return "ad_incomplete";
                case GameEvent.AdRewardSaved: return "ad_reward_saved";
                case GameEvent.StoreOpened: return "store_opened";
                case GameEvent.PurchaseStarted: return "purchase_started";
                case GameEvent.PurchasePending: return "purchase_pending";
                case GameEvent.PurchaseValidationStarted: return "purchase_validation_started";
                case GameEvent.PurchaseValidated: return "purchase_validated";
                case GameEvent.PurchaseGranted: return "purchase_granted";
                case GameEvent.PurchaseCancelled: return "purchase_cancelled";
                case GameEvent.PurchaseFailed: return "purchase_failed";
                case GameEvent.PurchaseRestore: return "purchase_restore";
                case GameEvent.PurchaseConfirmed: return "purchase_confirmed";
                case GameEvent.WorkerHired: return "worker_hired";
                case GameEvent.EquipmentChanged: return "equipment_changed";
                case GameEvent.ItemUpgraded: return "item_upgraded";
                case GameEvent.FactoryUnlocked: return "factory_unlocked";
                case GameEvent.FactoryAutomated: return "factory_automated";
                case GameEvent.GoodsSold: return "goods_sold";
                case GameEvent.BonusGranted: return "bonus_granted";
                case GameEvent.DynamiteLaunched: return "dynamite_launched";
                case GameEvent.DynamiteExploded: return "dynamite_exploded";
                case GameEvent.ElixirFound: return "elixir_found";
                case GameEvent.CurrencyEarned: return "currency_earned";
                case GameEvent.CurrencySpent: return "currency_spent";
                case GameEvent.AdRevenueUnavailable: return "ad_revenue_unavailable";
                default: return null;
            }
        }
        public void GameAction(OutpostEvent e, int zone)
        {
            // Observe values already emitted by the simulation; analytics never changes the economy.
            string currency = null;
            bool spent = false;
            switch (e.Type)
            {
                case OutpostEventType.TeamUpgradePurchased: currency = "gold"; spent = true; break;
                case OutpostEventType.CardUpgraded: currency = "elixir"; spent = true; break;
                case OutpostEventType.ItemUpgraded: currency = "parts"; spent = true; break;
                case OutpostEventType.CargoDeposited:
                case OutpostEventType.GoodsSold:
                case OutpostEventType.BonusGold: currency = "gold"; break;
                case OutpostEventType.ElixirFound: currency = "elixir"; break;
                case OutpostEventType.BonusGranted: currency = e.ItemId; break;
            }
            if (currency != null && e.Value > 0)
                Track(spent ? GameEvent.CurrencySpent : GameEvent.CurrencyEarned, zone, currency, e.Value.ToString());
            GameEvent kind;
            switch (e.Type)
            {
                case OutpostEventType.StageCompleted: kind = GameEvent.MineCompleted; break;
                case OutpostEventType.SectorOpened: kind = GameEvent.SectorOpened; break;
                case OutpostEventType.TeamUpgradePurchased: kind = GameEvent.UpgradePurchased; break;
                case OutpostEventType.CardUpgraded: kind = GameEvent.CardUpgraded; break;
                case OutpostEventType.ChestOpened: kind = GameEvent.ChestOpened; break;
                case OutpostEventType.StorePurchased: kind = GameEvent.StorePurchased; break;
                case OutpostEventType.WorkerHired: kind = GameEvent.WorkerHired; break;
                case OutpostEventType.EquipmentChanged: kind = GameEvent.EquipmentChanged; break;
                case OutpostEventType.ItemUpgraded: kind = GameEvent.ItemUpgraded; break;
                case OutpostEventType.FactoryUnlocked: kind = GameEvent.FactoryUnlocked; break;
                case OutpostEventType.FactoryAutomated: kind = GameEvent.FactoryAutomated; break;
                case OutpostEventType.GoodsSold: kind = GameEvent.GoodsSold; break;
                case OutpostEventType.BonusGranted: kind = GameEvent.BonusGranted; break;
                case OutpostEventType.DynamiteLaunched: kind = GameEvent.DynamiteLaunched; break;
                case OutpostEventType.DynamiteExploded: kind = GameEvent.DynamiteExploded; break;
                case OutpostEventType.ElixirFound: kind = GameEvent.ElixirFound; break;
                default: return;
            }
            Track(kind, zone, e.ItemId, e.Value.ToString());
        }
        readonly HashSet<string> earnedOffers = new HashSet<string>();
        public bool HasEarned(string id) => id != null && earnedOffers.Contains(id);
        public void AcknowledgeReward(string id) => earnedOffers.Remove(id);
        public bool IsRewardShowing
        {
            get {
#if UNITY_ANDROID && !UNITY_EDITOR && CATMINERS_MAX
                return activePlacement != null;
#else
                return false;
#endif
            }
        }
        public bool NeedsRewardRetry => RewardStatus == "waiting_for_ad_result";
        public void RetryUncorrelatedShow()
        {
#if UNITY_ANDROID && !UNITY_EDITOR && CATMINERS_MAX
            // Explicit player retry abandons the unresolved UI request, not an inferred callback association.
            if (NeedsRewardRetry) EndShow(false);
#endif
        }
        public bool RewardReady
        {
            get {
#if UNITY_ANDROID && !UNITY_EDITOR && CATMINERS_MAX
                return MaxState == ProviderState.Initialized && Privacy.MayInitialize(Privacy.Ads) && activePlacement == null && MaxSdk.IsRewardedAdReady(Config.rewardedAdUnit);
#else
                return false;
#endif
            }
        }
        public bool ShowReward(string offerId, Action earned, Action closed)
        {
#if UNITY_ANDROID && !UNITY_EDITOR && CATMINERS_MAX
            if (!RewardReady) { Track(GameEvent.AdShowFailed); RewardStatus = "unavailable"; return false; }
            TickGameplayClock();
            Track(GameEvent.AdShow);
            activePlacement = offerId;
            onEarned = earned;
            onClosed = closed;
            rewardSeen = false;
            displaySeen = false;
            impressions[offerId] = new Impression { Earn = earned };
            RewardStatus = "showing";
            try { MaxSdk.ShowRewardedAd(Config.rewardedAdUnit, offerId, offerId); return true; }
            catch { Track(GameEvent.AdShowFailed); EndShow(false); return false; }
#else
            return false;
#endif
        }
#if UNITY_ANDROID && !UNITY_EDITOR && CATMINERS_MAX
        string activePlacement;
        Action onEarned, onClosed;
        bool rewardSeen, displaySeen, maxCallbacksSubscribed;
        sealed class Impression { public int Recipients; public bool RewardSeen; public Action Earn; }
        readonly Dictionary<string, Impression> impressions = new Dictionary<string, Impression>();
        float nextLoad = float.PositiveInfinity;
        int loadFailures;
        bool Match(string unit, MaxSdkBase.AdInfo info) => activePlacement != null && unit == Config.rewardedAdUnit && info != null && info.Placement == activePlacement;
        void SubscribeMax()
        {
            if (maxCallbacksSubscribed) return;
            maxCallbacksSubscribed = true;
            MaxSdkCallbacks.OnSdkInitializedEvent += MaxInitialized;
            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent += AdLoaded;
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent += LoadFailed;
            MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent += Displayed;
            MaxSdkCallbacks.Rewarded.OnAdClickedEvent += Clicked;
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += Earned;
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent += Hidden;
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += DisplayFailed;
            MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent += Revenue;
        }
        void MaxInitialized(MaxSdkBase.SdkConfiguration c) => Post(() => {
            // A missing CMP or unset privacy data never becomes an invented consent grant.
            if (!MaxSdk.CmpService.HasSupportedCmp || !MaxSdk.IsUserConsentSet()) { MaxState = ProviderState.Failed; return; }
            MaxState = ProviderState.Initialized;
            nextLoad = Time.unscaledTime;
        });
        void AdLoaded(string unit, MaxSdkBase.AdInfo info) => Post(() => { if (unit == Config.rewardedAdUnit) { loadFailures = 0; RewardStatus = "ready"; Track(GameEvent.AdLoaded); } });
        void LoadFailed(string unit, MaxSdkBase.ErrorInfo error) => Post(() => {
            if (unit != Config.rewardedAdUnit) return;
            McLog.Write(McLogCode.AdError, McProvider.Max, error == null ? 0 : (int)error.Code);
            Track(GameEvent.AdLoadFailed, value: error == null ? null : error.Code.ToString());
            RewardStatus = "unavailable";
            nextLoad = Time.unscaledTime + Mathf.Min(60, Mathf.Pow(2, ++loadFailures));
        });
        void Displayed(string unit, MaxSdkBase.AdInfo info) => Post(() => { if (Match(unit, info)) { displaySeen = true; Track(GameEvent.AdDisplayed); } });
        void Clicked(string unit, MaxSdkBase.AdInfo info) => Post(() => { if (Match(unit, info)) Track(GameEvent.AdClicked); });
        void Earned(string unit, MaxSdkBase.Reward reward, MaxSdkBase.AdInfo info) => Post(() => {
            if (unit != Config.rewardedAdUnit || info == null || string.IsNullOrEmpty(info.Placement) ||
                !impressions.TryGetValue(info.Placement, out var impression) || impression.RewardSeen) return;
            impression.RewardSeen = true;
            earnedOffers.Add(info.Placement);
            if (Match(unit, info)) rewardSeen = true;
            RewardStatus = "saving_reward";
            Track(GameEvent.AdCompleted);
            Track(GameEvent.RewardEarned);
            impression.Earn?.Invoke();
            impression.Earn = null;
        });
        void Hidden(string unit, MaxSdkBase.AdInfo info) => Post(() => { if (Match(unit, info)) { Track(GameEvent.AdHidden); if (!rewardSeen) Track(GameEvent.AdIncomplete); EndShow(true); } });
        void DisplayFailed(string unit, MaxSdkBase.ErrorInfo error, MaxSdkBase.AdInfo info) => Post(() => {
            if (unit == Config.rewardedAdUnit) { McLog.Write(McLogCode.AdError, McProvider.Max, error == null ? 0 : (int)error.Code); Track(GameEvent.AdDisplayFailed, value: error == null ? null : error.Code.ToString()); }
            if (Match(unit, info)) { Track(GameEvent.RewardFailed); EndShow(false); }
            else if (unit == Config.rewardedAdUnit && activePlacement != null && !displaySeen)
                RewardStatus = "waiting_for_ad_result"; // No placement: cannot safely bind a late failure to a new show.
        });
        void EndShow(bool hidden)
        {
            Track(GameEvent.RewardClosed, value: rewardSeen ? "earned" : "no_reward");
            var close = onClosed;
            activePlacement = null;
            onClosed = null;
            onEarned = null;
            RewardStatus = "loading";
            nextLoad = Time.unscaledTime + 1;
            close?.Invoke();
        }
        void Revenue(string unit, MaxSdkBase.AdInfo info) => Post(() => {
            if (unit != Config.rewardedAdUnit || info == null || string.IsNullOrEmpty(info.Placement) ||
                !impressions.TryGetValue(info.Placement, out var impression)) return;
            if (double.IsNaN(info.Revenue) || double.IsInfinity(info.Revenue) || info.Revenue < 0)
            { Track(GameEvent.AdRevenueUnavailable); return; }
            McLog.Write(McLogCode.Revenue, McProvider.Max);
            // One impression per offered show; creative IDs are not impression IDs. Each recipient is independent.
            if ((impression.Recipients & 1) == 0 && Privacy.MayInitialize(Privacy.Adjust) && (AdjustState == ProviderState.Starting || AdjustState == ProviderState.Initialized))
            {
                impression.Recipients |= 1;
                try { var r = new AdjustAdRevenue("applovin_max_sdk") { AdRevenueNetwork = info.NetworkName,
                    AdRevenueUnit = unit, AdRevenuePlacement = info.Placement, AdImpressionsCount = 1 };
                    r.SetRevenue(info.Revenue, "USD"); Adjust.TrackAdRevenue(r); } catch { }
            }
            if ((impression.Recipients & 2) == 0 && Privacy.MayInitialize(Privacy.AppMetrica) && AppMetricaState == ProviderState.Initialized)
            {
                impression.Recipients |= 2;
                try { AppMetrica.ReportAdRevenue(new AdRevenue(info.Revenue, "USD") { AdNetwork = info.NetworkName,
                    AdUnitId = unit, AdPlacementName = info.Placement, AdPlacementId = info.NetworkPlacement,
                    AdType = AdType.Rewarded, Precision = info.RevenuePrecision }); } catch { }
            }
        });
#endif
        void OnApplicationPause(bool paused)
        {
            TickGameplayClock();
            telemetryPaused = paused;
            McLog.Write(paused ? McLogCode.Background : McLogCode.Foreground);
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!paused && MetaState == ProviderState.Initialized && Privacy.MayInitialize(Privacy.Meta)) FB.ActivateApp();
#endif
        }
        void OnDestroy()
        {
            if (Instance != this) return;
            TickGameplayClock();
            EndSession();
            Instance = null;
#if UNITY_ANDROID && !UNITY_EDITOR && CATMINERS_MAX
            MaxSdkCallbacks.OnSdkInitializedEvent -= MaxInitialized;
            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent -= AdLoaded;
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent -= LoadFailed;
            MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent -= Displayed;
            MaxSdkCallbacks.Rewarded.OnAdClickedEvent -= Clicked;
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent -= Earned;
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent -= Hidden;
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent -= DisplayFailed;
            MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent -= Revenue;
#endif
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using CatMiners.Core.Outpost;
using Newtonsoft.Json;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using AdjustSdk;
using Io.AppMetrica;
using Facebook.Unity;
#endif

namespace CatMiners.Runtime.Integrations
{
    public sealed partial class GameSdkRuntime
    {
        // The controller supplies actual gameplay eligibility; no scene/time-since-launch inference.
        public Func<bool> GameplayActive;
        sealed class PendingEvent
        {
            public GameEvent Kind;
            public Dictionary<string, object> Data;
            public int Recipients, Generation;
        }
        readonly Queue<PendingEvent> telemetry = new Queue<PendingEvent>();
        int consentGeneration, allowedRecipients, consentSnapshot = -1;
        bool telemetryPaused, telemetryUnfocused, sessionEnded;
        readonly ForegroundMinuteClock gameplayClock = new ForegroundMinuteClock();
        readonly double[] initStartedAt = new double[5];
        readonly ProviderState[] observedStates = new ProviderState[5];
        int ConsentMask => Privacy == null ? 0 :
            (Privacy.AppMetricaEnabled ? 1 : 0) |
            (Privacy.MayInitialize(Privacy.Adjust) ? 2 : 0) |
            (Privacy.MayInitialize(Privacy.Meta) ? 4 : 0);
        void RefreshConsent()
        {
            int mask = ConsentMask;
            int snapshot = mask | (GameSdkConfiguration.AdsEnabled && Privacy != null && Privacy.MayInitialize(Privacy.Ads) ? 8 : 0) |
                (Privacy == null || Privacy.Protected ? 16 : 0);
            if (snapshot == consentSnapshot) return;
            // Snapshot bits survive privacy objects mutated in place. Grants never backfill to a new
            // recipient: every queued event retains its original recipient mask. Revocation discards it.
            // Protected age alone does not revoke automatic basic AppMetrica analytics.
            if ((allowedRecipients & ~mask) != 0)
            {
                consentGeneration++;
                telemetry.Clear();
                gameplayClock.Revoke();
            }
            consentSnapshot = snapshot;
            allowedRecipients = mask;
            AppMetricaDiagnostics.Consent((mask & 1) != 0);
            McLog.Write(McLogCode.Consent, detail: snapshot);
            McLog.Write(McLogCode.Consent, McProvider.AppMetrica, (mask & 1) != 0 ? 1 : 0);
            McLog.Write(McLogCode.Consent, McProvider.Adjust, (mask & 2) != 0 ? 1 : 0);
            McLog.Write(McLogCode.Consent, McProvider.Meta, (mask & 4) != 0 ? 1 : 0);
            McLog.Write(McLogCode.Consent, McProvider.Max, (snapshot & 8) != 0 ? 1 : 0);
        }
        public void Track(GameEvent kind, int zone = 0, string item = null, string value = null)
            => Track(kind, zone, item, value, null);
        void Track(GameEvent kind, int zone, string item, string value, Dictionary<string, object> measurements, int recipientMask = 7)
        {
            McLog.Write(McLogCode.Event, detail: (int)kind);
            RefreshConsent();
            int recipients = allowedRecipients & recipientMask;
            if (recipients == 0 || EventName(kind) == null) return;
            var data = new Dictionary<string, object> { { "zone", zone } };
            // Correlation/offer IDs never enter custom events. Existing callers remain source compatible.
            if (kind != GameEvent.RewardEarned && kind != GameEvent.RewardClosed && kind != GameEvent.RewardFailed && item != null)
                data["item"] = item.Length <= 128 ? item : item.Substring(0, 128);
            if (value != null) data["value"] = value.Length <= 128 ? value : value.Substring(0, 128);
            if (kind == GameEvent.PlaytimeMinutes)
            {
                // One occurrence is one completed minute, including in the Events report's count column.
                data["minutes"] = 1;
                data["seconds"] = 60;
            }
            if (measurements != null)
                foreach (var pair in measurements) data.Add(pair.Key, pair.Value);
            if (telemetry.Count == 256) { telemetry.Dequeue(); McLog.Write(McLogCode.QueueFull); }
            telemetry.Enqueue(new PendingEvent { Kind = kind, Data = data, Recipients = recipients, Generation = consentGeneration });
            FlushTelemetry();
        }
        void FlushTelemetry()
        {
            RefreshConsent();
#if UNITY_ANDROID && !UNITY_EDITOR
            int count = telemetry.Count;
            while (count-- > 0)
            {
                var e = telemetry.Dequeue();
                if (e.Generation != consentGeneration) continue;
                e.Recipients &= allowedRecipients;
                string name = EventName(e.Kind);
                if ((e.Recipients & 1) != 0 && AppMetricaState == ProviderState.Initialized)
                {
                    e.Recipients &= ~1; // At most one SDK submission attempt, independent of other recipients.
                    AppMetricaDiagnostics.Record(AppMetricaDiagnostics.Stage.EventAttempt, e.Kind);
                    try {
                        AppMetrica.ReportEvent(name, JsonConvert.SerializeObject(e.Data));
                        AppMetricaDiagnostics.Record(AppMetricaDiagnostics.Stage.EventSubmitted, e.Kind);
                        McLog.Write(McLogCode.Sent, McProvider.AppMetrica, (int)e.Kind);
                    }
                    catch (Exception ex) {
                        AppMetricaDiagnostics.Record(AppMetricaDiagnostics.Stage.EventError, e.Kind, ex);
                        McLog.Write(McLogCode.CallbackFailed, McProvider.AppMetrica);
                    }
                }
                if ((e.Recipients & 2) != 0 && (AdjustState == ProviderState.Starting || AdjustState == ProviderState.Initialized))
                {
                    e.Recipients &= ~2;
                    var token = AdjustEventToken(e.Kind);
                    if (token != null) try
                    {
                        var a = new AdjustEvent(token);
                        int n = 0;
                        foreach (var pair in e.Data)
                        {
                            if (n++ == 24) break;
                            a.AddCallbackParameter(pair.Key, Convert.ToString(pair.Value, CultureInfo.InvariantCulture));
                        }
                        Adjust.TrackEvent(a);
                        McLog.Write(McLogCode.Sent, McProvider.Adjust, (int)e.Kind);
                    }
                    catch { McLog.Write(McLogCode.CallbackFailed, McProvider.Adjust); }
                }
                if ((e.Recipients & 4) != 0 && (e.Kind != GameEvent.MineCompleted || MetaState == ProviderState.Initialized))
                {
                    e.Recipients &= ~4;
                    if (e.Kind == GameEvent.MineCompleted) try
                    {
                        FB.LogAppEvent("fb_mobile_level_achieved", parameters: new Dictionary<string, object> { { "fb_level", Convert.ToString(e.Data["zone"], CultureInfo.InvariantCulture) } });
                    }
                    catch { McLog.Write(McLogCode.CallbackFailed, McProvider.Meta); }
                }
                if (e.Recipients != 0) telemetry.Enqueue(e);
            }
#endif
        }
        string AdjustEventToken(GameEvent kind)
        {
            // AppMetrica separates chest sources into visible event names; reuse the registered
            // optional Adjust chest binding and pass the same source/count context to it.
            string name = kind == GameEvent.StoreChestOpened || kind == GameEvent.ArenaChestOpened
                ? "chest_opened" : EventName(kind);
            return Config.EventToken(name);
        }
        void TickGameplayClock()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            bool playing = !telemetryPaused && !telemetryUnfocused && !IsRewardShowing &&
                allowedRecipients != 0 && GameplayActive != null && GameplayActive();
            SampleGameplayClock(now, playing);
        }
        void SampleGameplayClock(double now, bool playing)
        {
            int minutes = gameplayClock.Sample(now, playing);
            for (int n = 0; n < minutes; n++)
            {
                Track(GameEvent.PlaytimeMinutes, 0, null, "1", null, 1);
                // Keep existing optional-provider bindings in seconds; AppMetrica gets only the new minute metric.
                Track(GameEvent.TimeSpent, 0, null, "60", null, 6);
            }
        }
        void SendConsentedAppMetricaBuffer()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!appMetricaStarted || AppMetricaState != ProviderState.Initialized ||
                Privacy == null || !Privacy.AppMetricaEnabled) return;
            AppMetricaDiagnostics.Record(AppMetricaDiagnostics.Stage.FlushRequest);
            try {
                AppMetrica.SendEventsBuffer();
                AppMetricaDiagnostics.Record(AppMetricaDiagnostics.Stage.FlushReturned);
            }
            catch (Exception ex) {
                AppMetricaDiagnostics.Record(AppMetricaDiagnostics.Stage.FlushError, error: ex);
                McLog.Write(McLogCode.CallbackFailed, McProvider.AppMetrica);
            }
#endif
        }
        void OnApplicationFocus(bool focused)
        {
            TickGameplayClock();
            telemetryUnfocused = !focused;
            McLog.Write(focused ? McLogCode.Focused : McLogCode.Unfocused);
        }
        void OnApplicationQuit()
        {
            TickGameplayClock();
            EndSession();
        }
        void EndSession()
        {
            if (sessionEnded) return;
            sessionEnded = true;
            Track(GameEvent.SessionEnded);
            McLog.Write(McLogCode.SessionEnd);
        }
        void StartProvider(McProvider provider)
        {
            initStartedAt[(int)provider] = Time.realtimeSinceStartupAsDouble;
            McLog.Write(McLogCode.SdkInitStart, provider);
        }
        void ObserveProviders()
        {
            ObserveProvider(McProvider.AppMetrica, AppMetricaState);
            ObserveProvider(McProvider.Adjust, AdjustState);
            ObserveProvider(McProvider.Meta, MetaState);
            ObserveProvider(McProvider.Max, MaxState);
        }
        void ObserveProvider(McProvider provider, ProviderState state)
        {
            int i = (int)provider;
            if (observedStates[i] == state) return;
            observedStates[i] = state;
            int duration = initStartedAt[i] <= 0 ? 0 : (int)Math.Min(int.MaxValue, Math.Max(0, Time.realtimeSinceStartupAsDouble - initStartedAt[i]) * 1000);
            McLog.Write(McLogCode.SdkInitResult, provider, (int)state, duration);
        }
    }
}

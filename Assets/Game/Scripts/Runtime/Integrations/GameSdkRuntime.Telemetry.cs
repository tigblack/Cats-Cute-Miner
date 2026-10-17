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
            (Privacy.MayInitialize(Privacy.AppMetrica) ? 1 : 0) |
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
            if ((allowedRecipients & ~mask) != 0 || (snapshot & 16) != 0)
            {
                consentGeneration++;
                telemetry.Clear();
                gameplayClock.Revoke();
            }
            consentSnapshot = snapshot;
            allowedRecipients = mask;
            McLog.Write(McLogCode.Consent, detail: snapshot);
            McLog.Write(McLogCode.Consent, McProvider.AppMetrica, (mask & 1) != 0 ? 1 : 0);
            McLog.Write(McLogCode.Consent, McProvider.Adjust, (mask & 2) != 0 ? 1 : 0);
            McLog.Write(McLogCode.Consent, McProvider.Meta, (mask & 4) != 0 ? 1 : 0);
            McLog.Write(McLogCode.Consent, McProvider.Max, (snapshot & 8) != 0 ? 1 : 0);
        }
        public void Track(GameEvent kind, int zone = 0, string item = null, string value = null)
        {
            McLog.Write(McLogCode.Event, detail: (int)kind);
            RefreshConsent();
            if (allowedRecipients == 0 || EventName(kind) == null) return;
            var data = new Dictionary<string, object> { { "zone", zone } };
            // Correlation/offer IDs never enter custom events. Existing callers remain source compatible.
            if (kind != GameEvent.RewardEarned && kind != GameEvent.RewardClosed && kind != GameEvent.RewardFailed && item != null)
                data["item"] = item.Length <= 128 ? item : item.Substring(0, 128);
            if (value != null) data["value"] = value.Length <= 128 ? value : value.Substring(0, 128);
            if (telemetry.Count == 256) { telemetry.Dequeue(); McLog.Write(McLogCode.QueueFull); }
            telemetry.Enqueue(new PendingEvent { Kind = kind, Data = data, Recipients = allowedRecipients, Generation = consentGeneration });
            FlushTelemetry();
        }
        void FlushTelemetry()
        {
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
                    try { AppMetrica.ReportEvent(name, JsonConvert.SerializeObject(e.Data)); McLog.Write(McLogCode.Sent, McProvider.AppMetrica, (int)e.Kind); }
                    catch { McLog.Write(McLogCode.CallbackFailed, McProvider.AppMetrica); }
                }
                if ((e.Recipients & 2) != 0 && (AdjustState == ProviderState.Starting || AdjustState == ProviderState.Initialized))
                {
                    e.Recipients &= ~2;
                    var token = Config.EventToken(name);
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
        void TickGameplayClock()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            bool playing = !telemetryPaused && !telemetryUnfocused && !IsRewardShowing &&
                allowedRecipients != 0 && GameplayActive != null && GameplayActive();
            int minutes = gameplayClock.Sample(now, playing);
            for (int n = 0; n < minutes; n++) Track(GameEvent.TimeSpent, value: "60");
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

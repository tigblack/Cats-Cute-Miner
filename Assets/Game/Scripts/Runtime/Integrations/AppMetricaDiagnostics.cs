using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace CatMiners.Runtime.Integrations
{
    // Local SDK observations only. No record here establishes server acceptance or delivery.
    public static class AppMetricaDiagnostics
    {
        public enum NativeActivation { Unknown, UnsupportedPlatform, Activated, NotActivated, CheckFailed }
        internal enum Stage { Blocked, ActivationReturned, InitError, ConsentApplied, ConsentError,
            EventAttempt, EventSubmitted, EventError, FlushRequest, FlushReturned, FlushError }
        internal enum BlockReason { UnsupportedPlatform, Package, PrivacyMissing, Protected, Consent,
            Configuration, Legal, ManualRevenueSymbol }
        const int Capacity = 500;
        static readonly object Gate = new object();
        static readonly Queue<string> Lines = new Queue<string>(Capacity);
        static NativeActivation nativeStatus;
        static string initStatus = "not_attempted", policyStatus = "unknown";
        static long sequence;
        [ThreadStatic] static bool emitting;

        public static bool VerboseSdkLogs
        {
            get
            {
#if CATMINERS_APPMETRICA_DIAGNOSTICS
                return true;
#else
                return false;
#endif
            }
        }
        public static NativeActivation NativeStatus { get { lock (Gate) return nativeStatus; } }
        public static string Summary
        {
            get
            {
                lock (Gate) return "init=" + initStatus + "; native=" + nativeStatus +
                    "; basic_gameplay=" + policyStatus + "; sdk_logs=" + (VerboseSdkLogs ? "1" : "0") +
                    "; server_delivery=unverified";
            }
        }
        public static string[] Snapshot() { lock (Gate) return Lines.ToArray(); }

        // Hash the exact supplied value (no trim/case conversion). Never retain or emit the key.
        public static string KeyFingerprint(string key)
        {
            if (string.IsNullOrEmpty(key)) return "missing";
            try
            {
                using (var sha = SHA256.Create())
                {
                    var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
                    var result = new StringBuilder(64);
                    foreach (byte b in bytes) result.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                    return result.ToString();
                }
            }
            catch { return "unavailable"; } // A diagnostic hash failure cannot block SDK initialization.
        }

        // Call on the Unity main thread. Reads the Java proxy, never the managed IsActivated cache.
        // Failure is diagnostic only and must not change provider/queue behavior.
        public static NativeActivation RefreshNativeActivation()
        {
            NativeActivation status;
            string error = null;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var proxy = new AndroidJavaClass("io.appmetrica.analytics.plugin.unity.AppMetricaProxy"))
                    status = proxy.CallStatic<bool>("isActivated") ? NativeActivation.Activated : NativeActivation.NotActivated;
            }
            catch (Exception ex) { status = NativeActivation.CheckFailed; error = ExceptionType(ex); }
#else
            status = NativeActivation.UnsupportedPlatform;
#endif
            lock (Gate) nativeStatus = status;
            Append("native_activation=" + status + (error == null ? "" : " exception=" + error) + " server_delivery=unverified");
            return status;
        }
        internal static void Blocked(BlockReason reason)
        {
            lock (Gate)
            {
                // Preserve historical activation when basic analytics is later disabled; policy is independent.
                if (initStatus != "activation_returned") initStatus = "blocked_" + reason;
            }
            Record(Stage.Blocked, reason: reason);
        }
        internal static void InitStart(string key)
        {
            lock (Gate) { initStatus = "starting"; nativeStatus = NativeActivation.Unknown; }
            Append("InitStart key_sha256=" + KeyFingerprint(key) +
                " startup=automatic advIdentifiers=0 appOpen=0 location=0 data_sending=1 revenue_auto=0 sessions_auto=1 crash=0 native_crash=0 sdk_logs=" +
                (VerboseSdkLogs ? "1" : "0"));
        }
        internal static void Consent(bool allowed)
        {
            lock (Gate) policyStatus = allowed ? "enabled" : "disabled";
            Append("basic_gameplay_enabled=" + (allowed ? "1" : "0"));
        }
        internal static void Record(Stage stage, GameEvent? kind = null, Exception error = null, BlockReason? reason = null)
        {
            lock (Gate)
            {
                if (stage == Stage.ActivationReturned) initStatus = "activation_returned";
                if (stage == Stage.InitError) initStatus = "error";
            }
            // Names can only come from the game's approved event map; never accept arbitrary text/payloads.
            string name = kind.HasValue ? GameSdkRuntime.EventName(kind.Value) : null;
            Append(stage + (reason.HasValue ? " reason=" + reason.Value : "") +
                (name == null ? "" : " event=" + name) +
                (error == null ? "" : " exception=" + ExceptionType(error)) +
                (stage == Stage.EventSubmitted || stage == Stage.ActivationReturned || stage == Stage.FlushReturned
                    ? " server_delivery=unverified" : ""));
        }
        static string ExceptionType(Exception error)
        {
            string name = error.GetType().Name;
            var safe = new StringBuilder(96);
            foreach (char c in name)
            {
                if (safe.Length == 96) break;
                if (c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_') safe.Append(c);
            }
            return safe.ToString();
        }
        static void Append(string text)
        {
            string line;
            lock (Gate)
            {
                line = (++sequence).ToString(CultureInfo.InvariantCulture) + " " + text;
                if (Lines.Count == Capacity) Lines.Dequeue();
                Lines.Enqueue(line);
            }
            // Never subscribe to Unity log callbacks or forward into telemetry/McLog.
            // Use a single sink per platform so Android logcat does not get duplicate lines.
            if (emitting) return;
            emitting = true;
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var log = new AndroidJavaClass("android.util.Log")) log.CallStatic<int>("i", "MC-AM", line);
#else
                Debug.Log("[MC-AM] " + line);
#endif
            }
            catch { /* Logging must not interfere with SDK calls or the consent queue. */ }
            finally { emitting = false; }
        }
    }
}

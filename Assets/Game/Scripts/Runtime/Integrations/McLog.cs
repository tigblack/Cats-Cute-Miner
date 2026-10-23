using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace CatMiners.Runtime.Integrations
{
    public enum McLogCode { SessionStart, SessionEnd, Foreground, Background, Focused, Unfocused,
        Consent, SdkInitStart, SdkInitResult, Event, Sent, CallbackFailed, Revenue, QueueFull, AdError, AgeGroup, AdConsent }
    public enum McProvider { Game, AppMetrica, Adjust, Meta, Max }
    // Memory only; callers cannot pass receipts, identifiers, keys or exception messages.
    public static class McLog
    {
        static readonly object Gate = new object();
        static readonly Queue<string> Lines = new Queue<string>();
        static readonly Dictionary<int, double> Last = new Dictionary<int, double>();
        public static string[] Snapshot() { lock (Gate) return Lines.ToArray(); }
        public static void Write(McLogCode code, McProvider provider = McProvider.Game, int detail = 0, int durationMs = 0)
        {
            try
            {
                var now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                string line;
                lock (Gate)
                {
                    int key = ((int)code * 8 + (int)provider) * 256 + Math.Max(0, Math.Min(255, detail));
                    if (Last.TryGetValue(key, out var last) && now - last < 1) return;
                    // All keys derive from bounded enums/numeric details.
                    if (Last.Count >= 2048) Last.Clear();
                    Last[key] = now;
                    string eventName = code == McLogCode.Event || code == McLogCode.Sent ? GameSdkRuntime.EventName((GameEvent)detail) : null;
                    line = now.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " " + code + " " + provider + " detail=" + detail + " ms=" + durationMs + (eventName == null ? "" : " event=" + eventName);
                    if (Lines.Count == 500) Lines.Dequeue();
                    Lines.Enqueue(line);
                }
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var log = new AndroidJavaClass("android.util.Log")) log.CallStatic<int>("i", "MC", line);
#endif
            }
            catch { /* Diagnostics must never affect gameplay or delivery. */ }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

namespace CatMiners.Runtime.Integrations
{
    // Only fixed event names and counters leave this parser. Raw native logs may contain
    // identifiers/payloads and must never enter the on-screen console or clipboard.
    public static class AppMetricaNativeLog
    {
        static readonly object Gate = new object();
        static readonly Queue<string> Lines = new Queue<string>();
        static readonly Regex Accepted = new Regex(@"^I/AppMetrica\s*:\s*\[com\.multicastgames\.catscuteminer\]\s*:\s*\[[^\]\r\n]{0,80}\]\s*(?:\[\d+-[^\]\r\n]{0,160}\]\s*)?Event sent: ([a-z_]+)(?: with value .*)?$", RegexOptions.CultureInvariant);
        static int acceptedCount;
        static string readerStatus = "disabled in this build";
        public static string Summary { get { lock (Gate) return "Native reader: " + readerStatus + "; accepted custom events: " + acceptedCount; } }
        public static string[] Snapshot() { lock (Gate) return Lines.ToArray(); }

        public static bool TryGetAcceptedEvent(string nativeLine, out string name)
        {
            name = null;
            if (nativeLine == null || nativeLine.Length > 16384) return false;
            var match = Accepted.Match(nativeLine);
            if (!match.Success) return false;
            string candidate = match.Groups[1].Value;
            foreach (GameEvent kind in Enum.GetValues(typeof(GameEvent)))
                if (candidate == GameSdkRuntime.EventName(kind)) { name = candidate; return true; }
            return false;
        }

        static void Consume(string line)
        {
            if (!TryGetAcceptedEvent(line, out string name)) return;
            string safe;
            lock (Gate)
            {
                acceptedCount++;
                readerStatus = "reading (accepted server response observed)";
                safe = DateTime.UtcNow.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " NativeAccepted event=" + name;
                if (Lines.Count >= 500) Lines.Dequeue();
                Lines.Enqueue(safe);
            }
#if UNITY_ANDROID && !UNITY_EDITOR && CATMINERS_APPMETRICA_DIAGNOSTICS
            using (var log = new AndroidJavaClass("android.util.Log")) log.CallStatic<int>("i", "MC-AM-NATIVE", safe);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR && CATMINERS_APPMETRICA_DIAGNOSTICS
        static AndroidJavaObject process;
        static volatile bool stopping;
        static Thread worker;
        // The SDK may use another process in the same UID. Android restricts this
        // unprivileged logcat reader to our app's UID; no READ_LOGS permission is added.
        public static void Start()
        {
            if (worker != null) return;
            stopping = false;
            lock (Gate) readerStatus = "starting";
            worker = new Thread(Read) { IsBackground = true, Name = "AppMetrica diagnostic reader" };
            worker.Start();
            Application.quitting += Stop;
        }
        static void Read()
        {
            AndroidJNI.AttachCurrentThread();
            try
            {
                using (var runtimeClass = new AndroidJavaClass("java.lang.Runtime"))
                using (var runtime = runtimeClass.CallStatic<AndroidJavaObject>("getRuntime"))
                {
                    process = runtime.Call<AndroidJavaObject>("exec", (object)new[] { "logcat", "-v", "tag", "-T", "1", "AppMetrica:I", "*:S" });
                    using (var input = process.Call<AndroidJavaObject>("getInputStream"))
                    using (var stream = new AndroidJavaObject("java.io.InputStreamReader", input))
                    using (var reader = new AndroidJavaObject("java.io.BufferedReader", stream))
                    {
                        lock (Gate) readerStatus = "reading (no server ACK observed yet)";
                        string line;
                        while (!stopping && (line = reader.Call<string>("readLine")) != null) Consume(line);
                        lock (Gate) readerStatus = stopping ? "stopped" : "reader exited; use adb logcat";
                    }
                }
            }
            catch { lock (Gate) readerStatus = "unavailable; use adb logcat"; }
            finally
            {
                try { process?.Call("destroy"); process?.Dispose(); } catch { }
                process = null;
                AndroidJNI.DetachCurrentThread();
            }
        }
        static void Stop()
        {
            stopping = true;
            try { process?.Call("destroy"); } catch { }
        }
#endif
    }
}

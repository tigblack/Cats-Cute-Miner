using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CatMiners.Core;
using CatMiners.Core.Grid;
using CatMiners.Core.Sim;
using CatMiners.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CatMiners.Tests
{
    /// <summary>
    /// Запись геймплея для визуальной приёмки (Docs/Visual/VISUAL_QA.md): около 45 с реального времени — найм из пушки,
    /// появление котиков, перенос и объединение, добыча со стадиями повреждения, сбор добычи, состояния шахты, затем
    /// кристальная пещера и медная выработка. Кадр — камера с HUD 1080×1920, уменьшенный вдвое (540×960, пиксель арта =
    /// 2 пикселя кадра), 15 кадров/с; звук — то, что слышит AudioListener (OnAudioFilterRead), 16 бит PCM.
    /// Файл — AVI (MJPEG + PCM) в build/recording/ на ПК: ffmpeg в среде нет, контейнер пишется здесь же.
    /// Не входит в обычный прогон: unity.ps1 -Mode PlayMode -TestFilter GameplayRecordingTests.
    /// </summary>
    public class GameplayRecordingTests
    {
        const int Fps = 15, OutW = 540, OutH = 960;
        static readonly string[] s_TutorialSteps = { "t1", "t1b", "t2", "t2b", "t3", "t3b", "t4", "t4b", "t5", "t5a", "t5b", "t6", "t7", "t7a", "t8", "t8a", "t9", "t10", "t11" };

        string m_SaveDir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_SaveDir = PlayModeUtil.UseTempSaveDir();
            CameraRig.OverrideAspect = 1080f / 1920f;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            CameraRig.OverrideAspect = 0f;
            PlayModeUtil.DropTempSaveDir(m_SaveDir);
            yield break;
        }

        [UnityTest, Explicit("пишет build/recording/gameplay.avi, ≈45 с")]
        public IEnumerator RecordGameplay()
        {
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            Assert.IsNotNull(gc);
            yield return PlayModeUtil.CloseModals(gc);
            gc.SwitchSlot(SaveSlots.Debug, true, 2);
            yield return PlayModeUtil.WaitReal(0.5f);
            yield return PlayModeUtil.CloseModals(gc);
            var s = gc.Session;
            foreach (var id in s_TutorialSteps)
                if (!s.Player.TutorialDone.Contains(id)) s.Player.TutorialDone.Add(id);
            s.DebugAddGold(GameNumber.FromDouble(1e6));
            gc.Hud.Toast("", 0f);
            yield return PlayModeUtil.WaitReal(0.5f);

            var cam = gc.Rig.Cam;
            var canvas = gc.Hud.GetComponent<Canvas>();
            var scaler = gc.Hud.GetComponent<CanvasScaler>();
            var rt = new RenderTexture(1080, 1920, 24) { filterMode = FilterMode.Point };
            var small = new RenderTexture(OutW, OutH, 0) { filterMode = FilterMode.Point };
            var tex = new Texture2D(OutW, OutH, TextureFormat.RGB24, false);
            cam.targetTexture = rt;
            if (scaler != null) scaler.enabled = false;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            var tap = cam.gameObject.AddComponent<AudioTap>();
            var frames = new List<byte[]>();
            var host = new GameObject("RecordingHost").AddComponent<RecordingHost>();
            bool recording = true;
            float start = Time.realtimeSinceStartup;
            int sampleRate = AudioSettings.outputSampleRate;

            // Кадр читается в начале следующего кадра (после Update): в RenderTexture — последний отрисованный кадр.
            // WaitForEndOfFrame в batchmode не наступает, поэтому обычный кадровый цикл.
            IEnumerator Grab()
            {
                double next = 0;
                while (recording)
                {
                    yield return null;
                    double now = Time.realtimeSinceStartup - start;
                    if (now < next) continue;
                    Graphics.Blit(rt, small);
                    var prev = RenderTexture.active;
                    RenderTexture.active = small;
                    tex.ReadPixels(new Rect(0, 0, OutW, OutH), 0, 0, false);
                    tex.Apply(false);
                    RenderTexture.active = prev;
                    var jpg = tex.EncodeToJPG(80);
                    // Кадры на сетке 1/Fps реального времени: если захват отстал, кадр повторяется — звук и видео совпадают.
                    while (next <= now)
                    {
                        frames.Add(jpg);
                        next += 1.0 / Fps;
                    }
                }
            }

            tap.Begin();
            host.StartCoroutine(Grab());
            try
            {
                // 1. Тёплая штольня (zone2): найм — пушка стреляет бочками.
                yield return PlayModeUtil.WaitReal(1.5f);
                for (int i = 0; i < 3; i++)
                {
                    gc.Hire();
                    yield return PlayModeUtil.WaitReal(1.1f);
                }
                yield return PlayModeUtil.WaitReal(0.6f);
                PlayModeUtil.OpenHireBarrels(gc);
                yield return PlayModeUtil.WaitReal(1.6f);

                // 2. Перенос и объединение двух котиков одного уровня.
                var pair = FindPair(s);
                if (pair.a != null)
                {
                    yield return Drag(gc, pair.a.Id, pair.b.Cell, 0.8f);
                    yield return PlayModeUtil.WaitReal(1.4f);
                }

                // 3. Котики — на камни: ходьба, добыча, стадии повреждения, разрушение, добыча на полу.
                var first = s.Mine.Cats.FirstOrDefault(c => c.TargetIndex < 0);
                int rock = FirstRock(s);
                if (first != null && rock >= 0)
                {
                    yield return Drag(gc, first.Id, s.Layout.Objects[rock].Cells[0], 0.9f);
                    yield return PlayModeUtil.WaitReal(0.6f);
                }
                PlayModeUtil.AssignIdleCats(gc);
                yield return PlayModeUtil.WaitReal(7f);
                PlayModeUtil.CollectAllLoot(gc);
                yield return PlayModeUtil.WaitReal(1.5f);

                // 4. Шахта: завал расчищен → лента «готова к активации» → активация (свет, колесо).
                int shaft = -1;
                foreach (var sh in s.Layout.Shafts)
                    if (s.IsCellOpen(sh.Cells[0]) && s.Mine.Objects[sh.Ordinal].Status == ObjectStatus.Intact) { shaft = sh.Ordinal; break; }
                if (shaft >= 0)
                {
                    gc.Rig.FocusOn(gc.Board.ObjectCenter(shaft).z);
                    yield return PlayModeUtil.WaitReal(1.2f);
                    s.DebugCompleteTarget(shaft);
                    yield return PlayModeUtil.WaitReal(1.8f);
                    gc.ActivateShaft(shaft);
                    yield return PlayModeUtil.WaitReal(1.6f);
                    yield return PlayModeUtil.CloseModals(gc);
                    yield return PlayModeUtil.WaitReal(1.8f);
                    gc.Rig.FocusOn(gc.Rig.HomeFocusZ(s));
                }

                // 5. Кристальная пещера (zone4): своя музыка, друзы.
                yield return GoTo(gc, 4, new[] { 4, 5, 5, 6, 7 });
                yield return PlayModeUtil.WaitReal(8f);

                // 6. Медная выработка (zone7): шестерни.
                yield return GoTo(gc, 7, new[] { 6, 7, 8, 8, 10 });
                yield return PlayModeUtil.WaitReal(7f);
            }
            finally
            {
                recording = false;
                tap.End();
            }
            yield return null;

            cam.targetTexture = null;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = null;
            if (scaler != null) scaler.enabled = true;
            UnityEngine.Object.Destroy(host.gameObject);

            float[] audio = tap.Samples();
            int channels = tap.Channels;
            string dir = Path.GetFullPath(Path.Combine(PlayModeUtil.ReportsDir(), "..", "..", "build", "recording"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "gameplay.avi");
            AviWriter.Write(path, frames, OutW, OutH, Fps, audio, channels, sampleRate);
            // Образцы кадров (10 %, 50 %, 90 %) рядом с записью — посмотреть без видеоплеера.
            for (int k = 1; k <= 9; k += 4)
                if (frames.Count > 0) File.WriteAllBytes(Path.Combine(dir, "frame_" + k + "0pct.jpg"), frames[frames.Count * k / 10]);
            var check = AviWriter.Check(path);
            Assert.AreEqual(frames.Count, check.Video, "кадров в AVI: " + check.Report);
            Assert.AreEqual(check.Video + check.Audio, check.Index, "индекс idx1: " + check.Report);
            double seconds = frames.Count / (double)Fps;
            double audioSeconds = channels > 0 ? audio.Length / (double)channels / sampleRate : 0;
            float peak = audio.Length > 0 ? audio.Max(Mathf.Abs) : 0f;
            File.WriteAllText(Path.Combine(dir, "gameplay.txt"),
                "Cat Miners — запись геймплея (GameplayRecordingTests)\n" +
                "видео: " + frames.Count + " кадров, " + Fps + " кадров/с, " + seconds.ToString("0.0") + " с, " + OutW + "×" + OutH + ", MJPEG\n" +
                "звук: " + (channels > 0 ? channels + " канала, " + sampleRate + " Гц, " + audioSeconds.ToString("0.0") + " с, пик " + peak.ToString("0.000") : "не записан (AudioListener не отдавал данные)") + "\n" +
                "сцены: тёплая штольня (zone2) — найм, появление, объединение, добыча, сбор, шахта; кристальная пещера (zone4); медная выработка (zone7)\n",
                new UTF8Encoding(false));
            Debug.Log("[recording] " + path + ": " + frames.Count + " кадров, звук " + audioSeconds.ToString("0.0") + " с, пик " + peak);
            Assert.Greater(seconds, 30.0, "запись не короче 30 с");
        }

        /// <summary>Перенос котика: взять, провести над полем к клетке и отпустить (как пальцем).</summary>
        static IEnumerator Drag(GameController gc, int catId, int toCell, float seconds)
        {
            var s = gc.Session;
            var cat = s.FindCat(catId);
            if (cat == null) yield break;
            var from = gc.Board.CellCenter(cat.Cell);
            var to = gc.Board.CellCenter(toCell);
            gc.BeginCatDrag(catId);
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                float u = Mathf.SmoothStep(0f, 1f, t / seconds);
                gc.UpdateCatDrag(Vector3.Lerp(from, to, u));
                yield return null;
            }
            gc.DropCat(catId, s.Layout.Col(toCell) + 0.5, s.Layout.Row(toCell) + 0.5);
        }

        static (CatState a, CatState b) FindPair(GameSession s)
        {
            var idle = s.Mine.Cats.Where(c => c.TargetIndex < 0).ToList();
            foreach (var a in idle)
                foreach (var b in idle)
                    if (a.Id != b.Id && a.Level == b.Level) return (a, b);
            return (null, null);
        }

        static int FirstRock(GameSession s)
        {
            int best = -1, bestRow = int.MinValue;
            foreach (var o in s.Layout.Objects)
            {
                if (o.Kind != ObjectKind.Rock || !s.IsCellOpen(o.Cells[0]) || s.Mine.Objects[o.Ordinal].Status != ObjectStatus.Intact) continue;
                if (o.Row > bestRow) { bestRow = o.Row; best = o.Ordinal; }
            }
            return best;
        }

        static IEnumerator GoTo(GameController gc, int zone, int[] levels)
        {
            yield return PlayModeUtil.CloseModals(gc);
            gc.ClearSelection();
            gc.Session.DebugGotoZone(zone);
            gc.RebuildBoard();
            yield return PlayModeUtil.WaitReal(0.4f);
            yield return PlayModeUtil.CloseModals(gc);
            var s = gc.Session;
            foreach (var id in s_TutorialSteps)
                if (!s.Player.TutorialDone.Contains(id)) s.Player.TutorialDone.Add(id);
            for (int i = 0; i < levels.Length - 1; i++) s.DebugSpawnCat(levels[i]);
            PlayModeUtil.AssignIdleCats(gc);
            s.DebugSpawnCat(levels[levels.Length - 1]);
            gc.Hud.Toast("", 0f);
        }

        /// <summary>Носитель корутины захвата кадров (живёт только во время записи).</summary>
        public sealed class RecordingHost : MonoBehaviour { }

        /// <summary>Перехват того, что слышит AudioListener: OnAudioFilterRead на его объекте (поток звука).</summary>
        public sealed class AudioTap : MonoBehaviour
        {
            readonly List<float> m_Buffer = new List<float>(48000 * 2 * 60);
            readonly object m_Lock = new object();
            volatile bool m_On;
            public int Channels { get; private set; }

            public void Begin()
            {
                lock (m_Lock) m_Buffer.Clear();
                m_On = true;
            }

            public void End() => m_On = false;

            public float[] Samples()
            {
                lock (m_Lock) return m_Buffer.ToArray();
            }

            void OnAudioFilterRead(float[] data, int channels)
            {
                if (!m_On) return;
                lock (m_Lock)
                {
                    Channels = channels;
                    m_Buffer.AddRange(data);
                }
            }
        }

        /// <summary>
        /// AVI 1.0 (RIFF): видео MJPEG (каждый кадр — JPEG) и звук PCM 16 бит, чередуются по кадрам, индекс idx1.
        /// Открывается VLC, «Кино и ТВ»/Медиаплеером Windows, браузерными плеерами с поддержкой MJPEG.
        /// </summary>
        public static class AviWriter
        {
            public static void Write(string path, List<byte[]> frames, int w, int h, int fps, float[] audio, int channels, int sampleRate)
            {
                bool hasAudio = channels > 0 && audio != null && audio.Length > 0;
                int blockAlign = hasAudio ? channels * 2 : 0;
                long totalAudioFrames = hasAudio ? audio.Length / channels : 0;
                int maxFrame = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite))
                using (var bw = new BinaryWriter(fs))
                {
                    long riff = BeginChunk(bw, "RIFF");
                    Four(bw, "AVI ");
                    long hdrl = BeginList(bw, "hdrl");
                    // avih
                    long avih = BeginChunk(bw, "avih");
                    bw.Write(1000000 / fps);                 // мкс на кадр
                    bw.Write(maxFrame * fps + sampleRate * blockAlign);
                    bw.Write(0);
                    bw.Write(0x10 | 0x100);                  // AVIF_HASINDEX | AVIF_ISINTERLEAVED
                    bw.Write(frames.Count);
                    bw.Write(0);
                    bw.Write(hasAudio ? 2 : 1);
                    bw.Write(maxFrame);
                    bw.Write(w);
                    bw.Write(h);
                    for (int i = 0; i < 4; i++) bw.Write(0);
                    EndChunk(bw, avih);
                    // видео
                    long strlV = BeginList(bw, "strl");
                    long strhV = BeginChunk(bw, "strh");
                    Four(bw, "vids");
                    Four(bw, "MJPG");
                    bw.Write(0);
                    bw.Write((short)0);
                    bw.Write((short)0);
                    bw.Write(0);
                    bw.Write(1);                             // scale
                    bw.Write(fps);                           // rate
                    bw.Write(0);
                    bw.Write(frames.Count);
                    bw.Write(maxFrame);
                    bw.Write(-1);
                    bw.Write(0);
                    bw.Write((short)0);
                    bw.Write((short)0);
                    bw.Write((short)w);
                    bw.Write((short)h);
                    EndChunk(bw, strhV);
                    long strfV = BeginChunk(bw, "strf");
                    bw.Write(40);
                    bw.Write(w);
                    bw.Write(h);
                    bw.Write((short)1);
                    bw.Write((short)24);
                    Four(bw, "MJPG");
                    bw.Write(w * h * 3);
                    bw.Write(0);
                    bw.Write(0);
                    bw.Write(0);
                    bw.Write(0);
                    EndChunk(bw, strfV);
                    EndChunk(bw, strlV);
                    // звук
                    if (hasAudio)
                    {
                        long strlA = BeginList(bw, "strl");
                        long strhA = BeginChunk(bw, "strh");
                        Four(bw, "auds");
                        bw.Write(0);
                        bw.Write(0);
                        bw.Write((short)0);
                        bw.Write((short)0);
                        bw.Write(0);
                        bw.Write(blockAlign);                // scale
                        bw.Write(sampleRate * blockAlign);   // rate (байт/с): rate/scale = отсчётов в секунду
                        bw.Write(0);
                        bw.Write((int)totalAudioFrames);
                        bw.Write(sampleRate * blockAlign);
                        bw.Write(-1);
                        bw.Write(blockAlign);
                        bw.Write((short)0);
                        bw.Write((short)0);
                        bw.Write((short)0);
                        bw.Write((short)0);
                        EndChunk(bw, strhA);
                        long strfA = BeginChunk(bw, "strf");
                        bw.Write((short)1);                  // PCM
                        bw.Write((short)channels);
                        bw.Write(sampleRate);
                        bw.Write(sampleRate * blockAlign);
                        bw.Write((short)blockAlign);
                        bw.Write((short)16);
                        bw.Write((short)0);                  // cbSize
                        EndChunk(bw, strfA);
                        EndChunk(bw, strlA);
                    }
                    EndChunk(bw, hdrl);

                    // movi: кадр и звук этого кадра по очереди
                    var index = new List<(string id, int offset, int size)>();
                    long movi = BeginList(bw, "movi");
                    long moviData = movi + 8;                // позиция FOURCC «movi»
                    long audioPos = 0;
                    var pcm = new byte[0];
                    for (int i = 0; i < frames.Count; i++)
                    {
                        index.Add(("00dc", (int)(fs.Position - moviData), frames[i].Length));
                        WriteData(bw, "00dc", frames[i], frames[i].Length);
                        if (!hasAudio) continue;
                        long until = (long)Math.Round((i + 1) * (double)sampleRate / fps);
                        if (i == frames.Count - 1) until = totalAudioFrames;
                        until = Math.Min(until, totalAudioFrames);
                        int count = (int)Math.Max(0, until - audioPos);
                        if (count == 0) continue;
                        int bytes = count * blockAlign;
                        if (pcm.Length < bytes) pcm = new byte[bytes];
                        for (long k = 0; k < (long)count * channels; k++)
                        {
                            float v = Mathf.Clamp(audio[audioPos * channels + k], -1f, 1f);
                            short sv = (short)Mathf.RoundToInt(v * 32767f);
                            pcm[k * 2] = (byte)(sv & 0xff);
                            pcm[k * 2 + 1] = (byte)((sv >> 8) & 0xff);
                        }
                        index.Add(("01wb", (int)(fs.Position - moviData), bytes));
                        WriteData(bw, "01wb", pcm, bytes);
                        audioPos += count;
                    }
                    EndChunk(bw, movi);
                    long idx = BeginChunk(bw, "idx1");
                    foreach (var e in index)
                    {
                        Four(bw, e.id);
                        bw.Write(0x10);                      // AVIIF_KEYFRAME
                        bw.Write(e.offset);
                        bw.Write(e.size);
                    }
                    EndChunk(bw, idx);
                    EndChunk(bw, riff);
                }
            }

            /// <summary>Разбор готового файла: RIFF/AVI, число чанков видео и звука в movi, записей idx1.</summary>
            public static (int Video, int Audio, int Index, string Report) Check(string path)
            {
                var data = File.ReadAllBytes(path);
                string Id(int at) => Encoding.ASCII.GetString(data, at, 4);
                int Size(int at) => BitConverter.ToInt32(data, at);
                if (Id(0) != "RIFF" || Id(8) != "AVI ") return (0, 0, 0, "не RIFF/AVI");
                if (Size(4) + 8 != data.Length) return (0, 0, 0, "размер RIFF " + (Size(4) + 8) + " ≠ файл " + data.Length);
                int video = 0, audio = 0, index = 0;
                int pos = 12;
                while (pos + 8 <= data.Length)
                {
                    string id = Id(pos);
                    int size = Size(pos + 4);
                    if (id == "LIST" && Id(pos + 8) == "movi")
                    {
                        int p = pos + 12, end = pos + 8 + size;
                        while (p + 8 <= end)
                        {
                            string c = Id(p);
                            int cs = Size(p + 4);
                            if (c == "00dc") video++;
                            else if (c == "01wb") audio++;
                            p += 8 + cs + (cs & 1);
                        }
                    }
                    else if (id == "idx1") index = size / 16;
                    pos += 8 + size + (size & 1);
                }
                return (video, audio, index, "видео " + video + ", звук " + audio + ", idx1 " + index + ", " + data.Length + " байт");
            }

            static void Four(BinaryWriter bw, string s) => bw.Write(Encoding.ASCII.GetBytes(s));

            static long BeginChunk(BinaryWriter bw, string id)
            {
                Four(bw, id);
                long pos = bw.BaseStream.Position;
                bw.Write(0);
                return pos - 4;
            }

            static long BeginList(BinaryWriter bw, string type)
            {
                long pos = BeginChunk(bw, "LIST");
                Four(bw, type);
                return pos;
            }

            /// <summary>Записать размер чанка (без байта выравнивания) и выровнять конец до чётного.</summary>
            static void EndChunk(BinaryWriter bw, long start)
            {
                var fs = bw.BaseStream;
                long end = fs.Position;
                fs.Position = start + 4;
                bw.Write((int)(end - start - 8));
                fs.Position = end;
                if (((end - start) & 1) == 1) bw.Write((byte)0);
            }

            static void WriteData(BinaryWriter bw, string id, byte[] data, int size)
            {
                Four(bw, id);
                bw.Write(size);
                bw.Write(data, 0, size);
                if ((size & 1) == 1) bw.Write((byte)0);
            }
        }
    }
}

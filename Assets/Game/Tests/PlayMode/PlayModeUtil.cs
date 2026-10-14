using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CatMiners.Core;
using CatMiners.Core.Grid;
using CatMiners.Core.Sim;
using CatMiners.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CatMiners.Tests
{
    /// <summary>Общие шаги PlayMode-тестов: сцена, ожидание, временный каталог сохранений, журнал ошибок, скриншоты.</summary>
    public static class PlayModeUtil
    {
        public static string ReportsDir()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-catminersReports") return args[i + 1];
            return Path.GetFullPath("Docs/Reports");
        }

        /// <summary>Сохранения тестов пишутся во временный каталог, не в persistentDataPath (ТЗ 12.2).</summary>
        public static string UseTempSaveDir()
        {
            string dir = Path.Combine(Application.temporaryCachePath, "Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            GameController.SaveDirectoryOverride = dir;
            return dir;
        }

        public static void DropTempSaveDir(string dir)
        {
            GameController.SaveDirectoryOverride = null;
            GameBootstrap.LegacyMode = false;
            try { if (dir != null) Directory.Delete(dir, true); } catch (IOException) { }
        }

        public static IEnumerator LoadCoreGame()
        {
            GameBootstrap.LegacyMode = true;
            yield return SceneManager.LoadSceneAsync("CoreGame", LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        public static IEnumerator WaitReal(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        public static Button FindButton(Component root, string name)
        {
            foreach (var b in root.GetComponentsInChildren<Button>(true))
                if (b.gameObject.name == name) return b;
            return null;
        }

        /// <summary>Активная кнопка открытого модального окна HUD с подписью label (кнопки окна называются «btn»).</summary>
        public static Button FindModalButton(Component root, string label)
        {
            foreach (var b in root.GetComponentsInChildren<Button>(false))
            {
                if (b.gameObject.name != "btn" || !b.gameObject.activeInHierarchy) continue;
                var text = b.GetComponentInChildren<TMP_Text>(true);
                if (text != null && text.text == label) return b;
            }
            return null;
        }

        /// <summary>Нажатие кнопки через систему событий UI: проверяются активность и interactable, как при касании.</summary>
        public static void Click(Button button)
        {
            var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerClickHandler);
        }

        /// <summary>Назначить свободных котиков на первые цели, которые их принимают (через команду UI).</summary>
        public static int AssignIdleCats(GameController gc)
        {
            var s = gc.Session;
            int assigned = 0;
            foreach (var cat in s.Mine.Cats.ToArray())
            {
                if (cat.TargetIndex >= 0) continue;
                for (int ord = 0; ord < s.Layout.Objects.Count; ord++)
                {
                    var o = s.Layout.Objects[ord];
                    if (!o.IsTarget || !s.AcceptsCat(ord, cat.Id)) continue;
                    gc.DropCat(cat.Id, s.Layout.Col(o.Cells[0]) + 0.5, s.Layout.Row(o.Cells[0]) + 0.5);
                    var after = s.FindCat(cat.Id);
                    if (after != null && after.TargetIndex >= 0)
                    {
                        assigned++;
                        break;
                    }
                }
            }
            return assigned;
        }

        /// <summary>Бочки найма на поле (котик внутри ждёт тапа), в порядке появления.</summary>
        public static List<ContainerState> HireBarrels(GameSession s)
        {
            var result = new List<ContainerState>();
            foreach (var c in s.Mine.Containers)
                if (c.Kind == ContainerKind.Hire) result.Add(c);
            return result;
        }

        /// <summary>
        /// Открыть все бочки найма командой тапа по бочке (OpenContainer, как PointerInput): в игре наём ставит бочку,
        /// котик появляется при её открытии. Возвращает число открытых бочек.
        /// </summary>
        public static int OpenHireBarrels(GameController gc)
        {
            int opened = 0;
            foreach (var box in HireBarrels(gc.Session))
            {
                gc.OpenContainer(box.Id);
                opened++;
            }
            return opened;
        }

        /// <summary>Собрать всю добычу на поле командой тапа по кучке (CollectLoot, как PointerInput). Возвращает число собранных.</summary>
        public static int CollectAllLoot(GameController gc)
        {
            int collected = 0;
            foreach (var loot in gc.Session.Mine.Loot.ToArray())
            {
                gc.CollectLoot(loot.Id);
                collected++;
            }
            return collected;
        }

        /// <summary>
        /// Слот Debug: разрушать инструментом доступные камни, начиная с ближних к площадке (нижние строки), до первой выпавшей
        /// добычи (камень с валютной наградой). null — добычи нет. События разбирает следующий кадр контроллера (сундуки камней
        /// открывают окна).
        /// </summary>
        public static LootState BreakRockForLoot(GameSession s)
        {
            var layout = s.Layout;
            var rocks = new List<int>();
            for (int ord = 0; ord < layout.Objects.Count; ord++)
                if (layout.Objects[ord].Kind == ObjectKind.Rock && s.IsTargetAvailable(ord)) rocks.Add(ord);
            rocks.Sort((a, b) => layout.Objects[a].Row != layout.Objects[b].Row ? layout.Objects[b].Row.CompareTo(layout.Objects[a].Row) : a.CompareTo(b));
            foreach (int ord in rocks)
            {
                s.DebugCompleteTarget(ord);
                if (s.Mine.Loot.Count > 0) return s.Mine.Loot[0];
            }
            return null;
        }

        /// <summary>Начислено по ключу журнала (например, «gold:rock»); нет записи — ноль.</summary>
        public static GameNumber Ledger(GameSession s, string key) =>
            s.Player.Ledger.TryGetValue(key, out var v) ? v : GameNumber.Zero;

        /// <summary>Закрыть окна (сундуки наград, сообщения) кнопками, как игрок.</summary>
        public static IEnumerator CloseModals(GameController gc)
        {
            yield return null;
            for (int guard = 0; guard < 50 && gc.Hud.ModalOpen; guard++)
            {
                foreach (var b in gc.Hud.GetComponentsInChildren<Button>(false))
                {
                    if (b.gameObject.name != "btn") continue;
                    Click(b);
                    break;
                }
                yield return null;
            }
        }

        /// <summary>
        /// Снимок 1080×1920 камеры с HUD для визуальной приёмки. CanvasScaler на время снимка выключен (масштаб 1): Canvas — ровно
        /// эталон 1080×1920 независимо от окна редактора. Несколько кадров ожидания — чтобы раскладка, появление окон (0,15 с)
        /// и подсказка обучения пересчитались под Canvas снимка. После снимка всё возвращается.
        /// </summary>
        public static IEnumerator CaptureUi(GameController gc, string file) => CaptureUi(gc, file, 1920, "screenshots");

        /// <summary>
        /// Снимок 1080×<paramref name="height"/> (вытянутые экраны 9:19,5 и 9:20 — 2340 и 2400) в подкаталог отчётов
        /// <paramref name="subdir"/>. Пропорции камеры (CameraRig.OverrideAspect) задаёт тест — под ту же высоту.
        /// </summary>
        public static IEnumerator CaptureUi(GameController gc, string file, int height, string subdir)
        {
            var cam = gc.Rig.Cam;
            var canvas = gc.Hud.GetComponent<Canvas>();
            var scaler = gc.Hud.GetComponent<CanvasScaler>();
            var rt = new RenderTexture(1080, height, 24);
            var prevTarget = cam.targetTexture;
            try
            {
                cam.targetTexture = rt;
                if (scaler != null) scaler.enabled = false;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 1f;
                yield return WaitReal(0.3f);
                gc.Hud.Refresh();
                Canvas.ForceUpdateCanvases();
                cam.Render();
                var prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(1080, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1080, height), 0, 0);
                tex.Apply();
                RenderTexture.active = prevActive;
                string dir = Path.Combine(ReportsDir(), subdir);
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, file), tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
            }
            finally
            {
                cam.targetTexture = prevTarget;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                if (scaler != null) scaler.enabled = true;
                rt.Release();
                UnityEngine.Object.Destroy(rt);
            }
        }

        /// <summary>Рендер камеры с HUD в 1080×1920 и сохранение PNG в каталог отчётов.</summary>
        public static void Capture(GameController gc, string file)
        {
            var cam = gc.Rig.Cam;
            var canvas = gc.Hud.GetComponent<Canvas>();
            var rt = new RenderTexture(1080, 1920, 24);
            var prevTarget = cam.targetTexture;
            cam.targetTexture = rt;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases();
            gc.Hud.Refresh();
            Canvas.ForceUpdateCanvases();
            cam.Render();
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1080, 1920, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1080, 1920), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            cam.targetTexture = prevTarget;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            string dir = Path.Combine(ReportsDir(), "screenshots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, file), tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            rt.Release();
        }
    }

    /// <summary>Собирает исключения и ошибки из журнала Unity за время теста.</summary>
    public sealed class LogWatcher : IDisposable
    {
        public readonly List<string> Errors = new List<string>();

        public LogWatcher() => Application.logMessageReceived += OnLog;

        void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            // Шум окружения ПК: лицензирование и плагин Rider, не код игры.
            if (message.Contains("Licensing") || message.Contains("Rider")) return;
            Errors.Add(type + ": " + message + "\n" + stack);
        }

        public string Report() => string.Join("\n", Errors);

        public void Dispose() => Application.logMessageReceived -= OnLog;
    }
}

using System.Globalization;
using CatMiners.Core.Sim;
using CatMiners.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CatMiners.DevTools
{
    /// <summary>
    /// Чит уровней (инструменты разработки, ТЗ 17: только Editor и Development Build). Кнопка «ЧИТ» под табличкой шахты
    /// открывает окно на UI-ките: выбор шахты (±1, ±10, первая, последняя), «Перейти» и «Пройти шахту».
    /// Клавиши: ] / [ — следующая и предыдущая шахта, с Shift — на 10, \ — пройти текущую шахту.
    /// Переход идёт в слоте Debug (как у панели DEV): основное сохранение не меняется. «Пройти шахту» разрушает ворота,
    /// расчищает и активирует шахты, разрушает выход и выполняет обычный переход — с наградой выхода и новой картой.
    /// </summary>
    public sealed class LevelCheat : MonoBehaviour
    {
        const float ButtonSize = 16 * PixelUi.Px;

        GameController m_Game;
        PixelWindow m_Window;
        TextMeshProUGUI m_Target, m_Info;
        int m_Zone = 1;
        int m_ZoneShown = int.MinValue, m_CurrentShown = int.MinValue;
        bool m_DebugShown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (GameBootstrap.Controller != null) Attach(GameBootstrap.Controller);
        }

        /// <summary>Подключить чит к контроллеру (при запуске сцены — сам; в тестах — явно). Без HUD — null.</summary>
        public static LevelCheat Attach(GameController game)
        {
            if (game == null || game.Hud == null) return null;
            var go = new GameObject("LevelCheat");
            var cheat = go.AddComponent<LevelCheat>();
            cheat.m_Game = game;
            cheat.Build();
            return cheat;
        }

        void Build()
        {
            var safe = m_Game.Hud.transform.Find("SafeArea") as RectTransform;
            if (safe == null) return;

            // Кнопка под табличкой «Шахта N» (верхняя полоса — 128 единиц от верха safe area).
            var open = PixelUi.Button(safe, "LevelCheatButton", "ЧИТ", PixelButton.Red, Toggle, PixelUi.FontBody);
            var ort = open.Rect;
            ort.anchorMin = ort.anchorMax = new Vector2(0f, 1f);
            ort.pivot = new Vector2(0f, 1f);
            ort.sizeDelta = new Vector2(30 * PixelUi.Px, ButtonSize);
            ort.anchoredPosition = new Vector2(2 * PixelUi.Px, -34 * PixelUi.Px);

            m_Window = PixelUi.Window(safe, "LevelCheat", "Чит: переход по шахтам", () => m_Window.Hide());
            m_Window.Box.Anchor(0.06f, 0.5f, 0.94f, 0.5f, new Vector2(0f, -90 * PixelUi.Px), new Vector2(0f, 90 * PixelUi.Px));
            m_Window.UpdateBodyArea();
            Ui.SetActive(m_Window.BodyText, false);
            var body = m_Window.Body;

            m_Target = PixelUi.Text(body, "target", "", PixelUi.FontTitle, PixelUi.TextAccentLight, TextAlignmentOptions.Center, true, true);
            m_Target.rectTransform.Anchor(0, 1, 1, 1, new Vector2(0f, -18 * PixelUi.Px), Vector2.zero);
            m_Info = PixelUi.Text(body, "info", "", PixelUi.FontSmall, PixelUi.TextDim, TextAlignmentOptions.Center, false, true);
            m_Info.rectTransform.Anchor(0, 1, 1, 1, new Vector2(0f, -28 * PixelUi.Px), new Vector2(0f, -18 * PixelUi.Px));

            Row(body, 32, new[] { "−10", "−1", "+1", "+10" }, new UnityEngine.Events.UnityAction[]
            {
                () => SetZone(m_Zone - 10), () => SetZone(m_Zone - 1), () => SetZone(m_Zone + 1), () => SetZone(m_Zone + 10),
            });
            Row(body, 52, new[] { "Первая", "Текущая", "Последняя" }, new UnityEngine.Events.UnityAction[]
            {
                () => SetZone(1), () => SetZone(CurrentZone), () => SetZone(ZoneCount),
            });

            var hint = PixelUi.Text(body, "hint", "Клавиши: ] [ — следующая / предыдущая, с Shift — на 10, \\ — пройти шахту.\n" +
                "Переход открывает слот Debug: основное сохранение не меняется.", PixelUi.FontSmall, PixelUi.TextDim, TextAlignmentOptions.Center);
            hint.rectTransform.Anchor(0, 1, 1, 1, new Vector2(4 * PixelUi.Px, -94 * PixelUi.Px), new Vector2(-4 * PixelUi.Px, -72 * PixelUi.Px));

            m_Window.AddButton("finish", "Пройти шахту", PixelButton.Blue, FinishCurrentZone, PixelUi.FontBody);
            m_Window.AddButton("go", "Перейти", PixelButton.Gold, () => JumpTo(m_Zone));
            m_Zone = CurrentZone;
        }

        /// <summary>Ряд одинаковых кнопок на расстоянии <paramref name="topPx"/> пикселей арта от верха тела окна.</summary>
        static void Row(RectTransform body, float topPx, string[] labels, UnityEngine.Events.UnityAction[] actions)
        {
            var row = Ui.Rect(body, "row").Anchor(0, 1, 1, 1, new Vector2(4 * PixelUi.Px, -(topPx + 16) * PixelUi.Px), new Vector2(-4 * PixelUi.Px, -topPx * PixelUi.Px));
            var layout = row.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            layout.spacing = 4 * PixelUi.Px;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            for (int i = 0; i < labels.Length; i++) PixelUi.Button(row, "cheat" + i, labels[i], PixelButton.Dark, actions[i], PixelUi.FontBody);
        }

        int ZoneCount => m_Game.Balance != null ? m_Game.Balance.ZoneCount : 1;
        int CurrentZone => m_Game.Session != null ? m_Game.Session.Mine.Zone : 1;

        void Toggle()
        {
            if (m_Window.Visible) m_Window.Hide();
            else
            {
                m_Zone = CurrentZone;
                m_Window.Show();
            }
        }

        void SetZone(int zone) => m_Zone = Mathf.Clamp(zone, 1, ZoneCount);

        bool Ready()
        {
            if (m_Game.Balance == null || m_Game.Session == null || m_Game.InTransition) return false;
            if (m_Game.Hud.ModalOpen)
            {
                m_Game.Hud.Toast("Сначала закройте окно");
                return false;
            }
            return true;
        }

        /// <summary>Перейти в шахту <paramref name="zone"/> в слоте Debug (из основного слота — новый слот Debug с этой шахты).</summary>
        public void JumpTo(int zone)
        {
            if (!Ready()) return;
            zone = Mathf.Clamp(zone, 1, ZoneCount);
            m_Zone = zone;
            if (m_Game.Session.IsDebugSlot)
            {
                m_Game.ClearSelection();
                m_Game.Session.DebugGotoZone(zone);
                m_Game.RebuildBoard();
                m_Game.SaveNow();
            }
            else m_Game.SwitchSlot(SaveSlots.Debug, true, zone);
            m_Window.Hide();
            m_Game.Hud.Toast("Чит: шахта " + zone.ToString(CultureInfo.InvariantCulture) + " (слот Debug)");
        }

        /// <summary>
        /// Пройти текущую шахту инструментами: ворота разрушаются и открываются снизу вверх, шахты расчищаются и активируются,
        /// выход разрушается; затем обычный переход 11.2 (награда выхода, новая карта). Только слот Debug.
        /// </summary>
        public void FinishCurrentZone()
        {
            if (!Ready()) return;
            if (!m_Game.Session.IsDebugSlot)
            {
                // В основном слоте инструменты запрещены (ТЗ 15.2): сначала та же шахта в слоте Debug.
                JumpTo(CurrentZone);
                if (m_Game.Session == null || !m_Game.Session.IsDebugSlot) return;
            }
            var s = m_Game.Session;
            var layout = s.Layout;
            foreach (var gate in layout.Gates)
            {
                var st = s.Mine.Objects[gate.Ordinal];
                if (st.Status == ObjectStatus.Intact) s.DebugCompleteTarget(gate.Ordinal);
                if (st.Status == ObjectStatus.Broken) m_Game.OpenGate(gate.Ordinal);
            }
            foreach (var shaft in layout.Shafts)
            {
                var st = s.Mine.Objects[shaft.Ordinal];
                if (st.Status == ObjectStatus.Intact) s.DebugCompleteTarget(shaft.Ordinal);
                if (st.Status == ObjectStatus.ReadyToActivate) m_Game.ActivateShaft(shaft.Ordinal);
            }
            if (s.Mine.Objects[layout.Exit.Ordinal].Status == ObjectStatus.Intact) s.DebugCompleteTarget(layout.Exit.Ordinal);
            m_Window.Hide();
            if (m_Game.CompleteZoneNow()) m_Zone = CurrentZone;
        }

        void Update()
        {
            if (m_Game == null || m_Window == null) return;
            var kb = Keyboard.current;
            if (kb != null)
            {
                int step = kb.shiftKey.isPressed ? 10 : 1;
                if (kb.rightBracketKey.wasPressedThisFrame) JumpTo(CurrentZone + step);
                else if (kb.leftBracketKey.wasPressedThisFrame) JumpTo(CurrentZone - step);
                else if (kb.backslashKey.wasPressedThisFrame) FinishCurrentZone();
            }
            if (!m_Window.Visible) return;
            // Тексты окна — только при смене значений (без выделений памяти в каждом кадре).
            if (m_Zone != m_ZoneShown)
            {
                m_ZoneShown = m_Zone;
                Ui.SetText(m_Target, "Шахта " + m_Zone.ToString(CultureInfo.InvariantCulture) + " из " + ZoneCount.ToString(CultureInfo.InvariantCulture));
            }
            bool debug = m_Game.Session != null && m_Game.Session.IsDebugSlot;
            if (CurrentZone != m_CurrentShown || debug != m_DebugShown)
            {
                m_CurrentShown = CurrentZone;
                m_DebugShown = debug;
                Ui.SetText(m_Info, "Сейчас: шахта " + m_CurrentShown.ToString(CultureInfo.InvariantCulture) + " · слот " + (debug ? "Debug" : "Main"));
            }
        }
    }
}

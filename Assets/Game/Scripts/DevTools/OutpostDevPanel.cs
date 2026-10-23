using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CatMiners.DevTools
{
    /// <summary>Installed only by the development assembly; every mutation is guarded in the controller too.</summary>
    public sealed class OutpostDevPanel : MonoBehaviour
    {
        OutpostController m_Game;
        RectTransform m_Panel;
        TextMeshProUGUI m_Status;
        Ui.ButtonRefs m_ResetButton;
        TMP_InputField m_LevelInput;
        TextMeshProUGUI m_LevelHint;
        readonly List<Selectable> m_LevelControls = new List<Selectable>();
        bool m_Jumping;
        int m_Zone = 1;
        float m_Next;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            Attach(GameBootstrap.OutpostController);
        }

        public static OutpostDevPanel Attach(OutpostController game)
        {
            if (!(Application.isEditor || Debug.isDebugBuild) || game == null || game.Hud == null) return null;
            var existing = game.GetComponentInChildren<OutpostDevPanel>(true);
            if (existing != null) return existing;
            var panel = new GameObject("OutpostDevTools").AddComponent<OutpostDevPanel>();
            panel.transform.SetParent(game.transform, false);
            panel.m_Game = game;
            panel.Build();
            game.SessionChanged += panel.OnSessionChanged;
            return panel;
        }
        void Build()
        {
            var safe = m_Game.Hud.transform.Find("SafeArea");
            var toggle = PixelUi.Button(safe, "OutpostDevToggle", "DEV 4", PixelButton.Blue, () =>
            {
                m_Game.Input.CancelGesture();
                if (!m_Panel.gameObject.activeSelf && !m_Jumping) SetZone(CurrentZone);
                m_Panel.gameObject.SetActive(!m_Panel.gameObject.activeSelf);
            }, 22);
            toggle.Image.rectTransform.Anchor(0, 0.8f, 0.15f, 0.84f);
            m_Panel = PixelUi.Panel(safe, "OutpostDevPanel", PixelPanel.Window).rectTransform.Anchor(0.03f, 0.12f, 0.97f, 0.79f);
            Ui.VList(m_Panel, 8, 14);
            m_Status = Ui.Text(m_Panel, "Status", "", 22, PixelUi.TextDim, TextAlignmentOptions.TopLeft);
            Ui.Layout(m_Status, 208);
            var profiles = Row();
            m_LevelControls.Add(Button(profiles, "Main", () => m_Game.SwitchProfile(false)).Button);
            m_LevelControls.Add(Button(profiles, "Debug", () => m_Game.SwitchProfile(true)).Button);
            var zones = Row();
            m_LevelControls.Add(Button(zones, "-10", () => JumpToZone(CurrentZone - 10)).Button);
            m_LevelControls.Add(Button(zones, "-1", () => JumpToZone(CurrentZone - 1)).Button);
            m_LevelControls.Add(Button(zones, "+1", () => JumpToZone(CurrentZone + 1)).Button);
            m_LevelControls.Add(Button(zones, "+10", () => JumpToZone(CurrentZone + 10)).Button);
            var level = Row();
            BuildLevelInput(level);
            m_LevelControls.Add(Button(level, "Load", LoadEnteredZone, PixelButton.Gold).Button);
            m_LevelHint = Ui.Text(m_Panel, "LevelHint", "Enter mine 1–" + ZoneCount + ". Cheats use the Debug profile.", 22, PixelUi.TextDim);
            Ui.Layout(m_LevelHint, 32);
            SetZone(CurrentZone);
            var speeds = Row();
            Button(speeds, "x1", () => m_Game.SimSpeed = 1);
            Button(speeds, "x5", () => m_Game.SimSpeed = 5);
            Button(speeds, "x20", () => m_Game.SimSpeed = 20);
            Button(speeds, "x100", () => m_Game.SimSpeed = 100);
            var tools = Row();
            Button(tools, "Test kit", m_Game.GrantTestKit);
            Button(tools, "Cards", m_Game.GrantTestCards);
            Button(tools, "Gems", m_Game.GrantTestGems);
            Button(tools, "Finish", m_Game.CompleteStageNow);
            Button(tools, "Paths", () => { if (m_Game.IsDebugProfile) OutpostDebugOverlay.Enabled = !OutpostDebugOverlay.Enabled; });
            // ТЗ 2.2 §13: заводы (только debug-профиль; проверки — в контроллере).
            var factories = Row();
            Button(factories, "Cycles", m_Game.DebugFinishFactoryCycles);
            Button(factories, "Goods x100", m_Game.DebugGrantGoods);
            Button(factories, "Unlock", m_Game.DebugUnlockFactories);
            Button(factories, "Auto all", m_Game.DebugAutomateAll);
            m_ResetButton = Button(Row(), Loc.T("outpost.dev.reset"), ShowResetConfirmation, PixelButton.Red);
            m_ResetButton.Image.gameObject.name = "OutpostResetProgress";
            m_ResetButton.SetInteractable(m_Game.CanResetProgress);
            m_Panel.gameObject.SetActive(false);
        }

        void ShowResetConfirmation()
        {
            if (!m_Game.CanResetProgress) return;
            bool debug = m_Game.IsDebugProfile;
            m_Game.Hud.Modal(Loc.T("outpost.dev.reset_title"),
                Loc.F("outpost.dev.reset_body", debug ? "Debug" : "Main"),
                new OutpostHud.ModalButton { Label = Loc.T("outpost.dev.reset_cancel"), Style = PixelButton.Blue, Cancel = true },
                new OutpostHud.ModalButton
                {
                    Label = Loc.T("outpost.dev.reset_confirm"), Style = PixelButton.Red,
                    Action = () =>
                    {
                        if (debug != m_Game.IsDebugProfile || !m_Game.CanResetProgress) return;
                        m_Game.ResetProgress();
                        SetZone(1);
                        m_Panel.gameObject.SetActive(false);
                    }
                });
        }

        int CurrentZone => m_Game.State?.Run.Zone ?? 1;
        int ZoneCount => m_Game.Balance?.ZoneCount ?? OutpostIds.LastZone;

        void BuildLevelInput(Transform parent)
        {
            var image = PixelUi.Panel(parent, "OutpostLevelInput", PixelPanel.Inset);
            var size = image.gameObject.AddComponent<LayoutElement>();
            size.flexibleWidth = 3; size.minWidth = 120;
            var viewport = Ui.Rect(image.transform, "Viewport").Anchor(0, 0, 1, 1, new Vector2(14, 6), new Vector2(-14, -6));
            viewport.gameObject.AddComponent<RectMask2D>();
            var text = PixelUi.Text(viewport, "Text", "", 26, PixelUi.TextColor, TextAlignmentOptions.MidlineLeft);
            text.rectTransform.Anchor(0, 0, 1, 1);
            text.raycastTarget = false;
            m_LevelInput = image.gameObject.AddComponent<TMP_InputField>();
            m_LevelInput.targetGraphic = image;
            m_LevelInput.textViewport = viewport;
            m_LevelInput.textComponent = text;
            m_LevelInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            m_LevelInput.characterLimit = 3;
            m_LevelInput.lineType = TMP_InputField.LineType.SingleLine;
            m_LevelInput.customCaretColor = true;
            m_LevelInput.caretColor = PixelUi.TextColor;
            m_LevelInput.onSubmit.AddListener(_ => LoadEnteredZone());
            m_LevelControls.Add(m_LevelInput);
        }

        void SetZone(int zone)
        {
            m_Zone = Mathf.Clamp(zone, 1, ZoneCount);
            m_LevelInput.SetTextWithoutNotify(m_Zone.ToString(CultureInfo.InvariantCulture));
        }

        void OnSessionChanged()
        {
            if (!m_Jumping) SetZone(CurrentZone);
        }

        void LoadEnteredZone()
        {
            if (!int.TryParse(m_LevelInput.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int zone) || zone < 1 || zone > ZoneCount)
            {
                LevelMessage("Enter a mine number from 1 to " + ZoneCount + ".", true);
                return;
            }
            JumpToZone(zone);
        }

        public void JumpToZone(int zone)
        {
            if (m_Jumping) return;
            if (m_Game.State == null || m_Game.Phase != OutpostControllerPhase.Playing || m_Game.InTransition || m_Game.SaveFailed)
            {
                LevelMessage(m_Game.SaveFailed ? "Retry the failed save before changing mines." : "Wait for the current mine to finish loading.", true);
                return;
            }
            zone = Mathf.Clamp(zone, 1, ZoneCount);
            SetZone(zone);
            m_Game.Input.CancelGesture();
            m_LevelInput.DeactivateInputField();
            m_Jumping = true;
            foreach (var control in m_LevelControls) control.interactable = false;
            StartCoroutine(Jump(zone));
        }

        IEnumerator Jump(int zone)
        {
            try
            {
                LevelMessage("Loading mine " + zone + "…");
                // A foreground hitch can make SwitchProfile start catch-up in Main first.
                // Wait for it, retry the switch, then wait for the Debug profile too.
                while (m_Game != null && !m_Game.SaveFailed)
                {
                    if (m_Game.Phase == OutpostControllerPhase.Loading || m_Game.Phase == OutpostControllerPhase.OfflineCatchUp)
                    {
                        yield return null;
                        continue;
                    }
                    if (m_Game.IsDebugProfile || m_Game.Phase != OutpostControllerPhase.Playing) break;
                    m_Game.SwitchProfile(true);
                    if (!m_Game.IsDebugProfile && m_Game.Phase == OutpostControllerPhase.Playing) break;
                }
                if (m_Game == null) yield break;
                if (!m_Game.IsDebugProfile)
                {
                    LevelMessage(m_Game.SaveFailed ? "Save failed. Retry the save, then change mines." : "Cannot switch to Debug right now. Close the active dialog and try again.", true);
                    yield break;
                }
                if (!m_Game.CanDebugLoadZone)
                {
                    LevelMessage("Cannot load a mine right now. Finish loading or retry the save.", true);
                    yield break;
                }
                m_Game.LoadZone(zone);
                if (m_Game.SaveFailed)
                {
                    LevelMessage("Save failed. Retry the save to finish loading mine " + zone + ".", true);
                    yield break;
                }
                SetZone(CurrentZone);
                LevelMessage("Mine " + CurrentZone + " loaded (Debug).");
                m_Game.Hud.Toast("Mine " + CurrentZone + " (Debug)");
            }
            finally
            {
                m_Jumping = false;
                foreach (var control in m_LevelControls) if (control != null) control.interactable = true;
            }
        }

        void LevelMessage(string text, bool error = false)
        {
            Ui.SetText(m_LevelHint, text);
            m_LevelHint.color = error ? PixelUi.TextBad : PixelUi.TextDim;
        }

        void OnDestroy()
        {
            if (m_Game != null) m_Game.SessionChanged -= OnSessionChanged;
        }
        RectTransform Row()
        {
            var row = Ui.Rect(m_Panel, "Row");
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6;
            layout.childForceExpandWidth = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            Ui.Layout(row, 55);
            return row;
        }
        static Ui.ButtonRefs Button(Transform parent, string label, UnityEngine.Events.UnityAction action, PixelButton style = PixelButton.Blue)
        {
            var b = PixelUi.Button(parent, label, label, style, action, 22);
            var layout = b.Image.gameObject.AddComponent<LayoutElement>();
            layout.flexibleWidth = 1; layout.minWidth = 30;
            return b;
        }
        void Update()
        {
            if (m_Game == null) { Destroy(gameObject); return; }
            if (m_Panel == null || !m_Panel.gameObject.activeSelf || Time.unscaledTime < m_Next) return;
            m_Next = Time.unscaledTime + 0.2f;
            m_ResetButton.SetInteractable(m_Game.CanResetProgress && !m_Jumping);
            var run = m_Game.State?.Run;
            if (run == null)
            {
                Ui.SetText(m_Status, "Profile: " + (m_Game.IsDebugProfile ? "Debug" : "Main") + "\n" + m_Game.ErrorText);
                return;
            }
            int ground = 0, carried = 0;
            foreach (var b in run.Batches) ground += b.Units.Count;
            foreach (var w in run.Workers) carried += w.Bag.Count;
            var s = m_Game.Stats;
            Ui.SetText(m_Status, "Profile: " + (m_Game.IsDebugProfile ? "DEBUG" : "MAIN (level cheats open Debug)") +
                " | Zone " + run.Zone + " | select " + m_Zone + " | x" + m_Game.SimSpeed +
                "\nCargo created/ground/carried/deposited: " + run.Counters.ReleasedUnits + "/" + ground + "/" + carried + "/" + run.Counters.DeliveredUnits +
                "\nDamage local/final: " + s.LocalDamage + "/" + s.FinalDamage + " | gear " + s.PickaxeBonus +
                "\nSpeed local/final: " + s.LocalMoveSpeed + "/" + s.FinalMoveSpeed + " | gear " + s.BootsBonus +
                "\nBag local/final: " + s.LocalCapacity + "/" + s.Capacity + " | gear +" + s.BackpackExtra +
                "\nValue local/final: " + s.LocalValue + "/" + s.FinalValue + " | gear " + s.HatBonus + " | queue " + run.Queue.Count +
                "\n" + FactoryLine(run));
        }

        /// <summary>Заводы шахты: состояния, AUTO, партии в трубах, склад (ТЗ 2.2 §13).</summary>
        static string FactoryLine(OutpostRun run)
        {
            if (run.Factories == null || run.Factories.Count == 0) return "Factories: none";
            int locked = 0, working = 0, ready = 0, stopped = 0, auto = 0;
            foreach (var f in run.Factories)
            {
                if (f.Automated) auto++;
                switch (f.Status)
                {
                    case FactoryStatus.Locked: locked++; break;
                    case FactoryStatus.Working: working++; break;
                    case FactoryStatus.Ready: ready++; break;
                    default: stopped++; break;
                }
            }
            long stock = 0, debug = 0;
            if (run.Warehouse != null) foreach (var kv in run.Warehouse) stock += kv.Value;
            if (run.DebugGoods != null) foreach (var kv in run.DebugGoods) debug += kv.Value;
            return "Factories " + run.Factories.Count + ": locked " + locked + " / work " + working + " / ready " + ready + " / stop " + stopped +
                " | auto " + auto + " | pipes " + (run.PipeBatches?.Count ?? 0) + " | stock " + stock + " (debug " + debug + ") | sold " + run.GoodsSoldGold;
        }
    }
}

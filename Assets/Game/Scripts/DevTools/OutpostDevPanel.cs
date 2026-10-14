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
        int m_Zone = 1;
        float m_Next;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!(Application.isEditor || Debug.isDebugBuild) || GameBootstrap.OutpostController == null) return;
            var panel = new GameObject("OutpostDevTools").AddComponent<OutpostDevPanel>();
            panel.m_Game = GameBootstrap.OutpostController;
            panel.Build();
        }
        void Build()
        {
            var safe = m_Game.Hud.transform.Find("SafeArea");
            var toggle = PixelUi.Button(safe, "OutpostDevToggle", "DEV 4", PixelButton.Blue, () =>
            {
                m_Game.Input.CancelGesture();
                m_Panel.gameObject.SetActive(!m_Panel.gameObject.activeSelf);
            }, 22);
            toggle.Image.rectTransform.Anchor(0, 0.8f, 0.15f, 0.84f);
            m_Panel = PixelUi.Panel(safe, "OutpostDevPanel", PixelPanel.Window).rectTransform.Anchor(0.03f, 0.35f, 0.97f, 0.79f);
            Ui.VList(m_Panel, 8, 14);
            m_Status = Ui.Text(m_Panel, "Status", "", 22, PixelUi.TextDim, TextAlignmentOptions.TopLeft);
            Ui.Layout(m_Status, 220);
            var profiles = Row();
            Button(profiles, "Main", () => m_Game.SwitchProfile(false));
            Button(profiles, "Debug", () => m_Game.SwitchProfile(true));
            var zones = Row();
            Button(zones, "-10", () => m_Zone = Mathf.Max(1, m_Zone - 10));
            Button(zones, "-1", () => m_Zone = Mathf.Max(1, m_Zone - 1));
            Button(zones, "+1", () => m_Zone = Mathf.Min(144, m_Zone + 1));
            Button(zones, "+10", () => m_Zone = Mathf.Min(144, m_Zone + 10));
            Button(zones, "Load", () => m_Game.LoadZone(m_Zone));
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
                        m_Zone = 1;
                        m_Panel.gameObject.SetActive(false);
                    }
                });
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
            m_ResetButton.SetInteractable(m_Game.CanResetProgress);
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
            Ui.SetText(m_Status, "Profile: " + (m_Game.IsDebugProfile ? "DEBUG" : "MAIN (reset available)") +
                " | Zone " + run.Zone + " | select " + m_Zone + " | x" + m_Game.SimSpeed +
                "\nCargo created/ground/carried/deposited: " + run.Counters.ReleasedUnits + "/" + ground + "/" + carried + "/" + run.Counters.DeliveredUnits +
                "\nDamage local/final: " + s.LocalDamage + "/" + s.FinalDamage + " | gear " + s.PickaxeBonus +
                "\nSpeed local/final: " + s.LocalMoveSpeed + "/" + s.FinalMoveSpeed + " | gear " + s.BootsBonus +
                "\nBag local/final: " + s.LocalCapacity + "/" + s.Capacity + " | gear +" + s.BackpackExtra +
                "\nValue local/final: " + s.LocalValue + "/" + s.FinalValue + " | gear " + s.HatBonus + " | queue " + run.Queue.Count);
        }
    }
}

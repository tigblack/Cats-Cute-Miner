using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BigInteger = System.Numerics.BigInteger;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Сведения о выбранном котике или цели (ТЗ 5, 14; RUNTIME_SPEC §5): компактная тёмная панель над нижней частью HUD,
    /// как контекстная панель старого HUD, но без кнопок назначения. Котик: текущая задача, сумка n/cap (шкала), стоимость
    /// груза. Цель: русское имя типа, HP (шкала), работники на цели, оставшаяся добыча (Total − Released порций), причина
    /// недоступности. Строки пересобираются только при смене исходных значений.
    /// </summary>
    public sealed partial class OutpostHud
    {
        const float InfoHeight = 64 * Px;
        const float PortraitSize = 38 * Px;
        const float InfoTextLeft = BarPad + PortraitSize + 4 * Px, InfoTitleHeight = 13 * Px, InfoBarHeight = 8 * Px;

        RectTransform m_Info;
        Image m_InfoPortrait;
        TextMeshProUGUI m_InfoTitle, m_InfoBody;
        PixelBar m_InfoBar;
        ValueKey m_InfoKey;
        bool m_InfoValid;

        void BuildInfo()
        {
            m_Info = PixelUi.Panel(m_Safe, "Info", PixelPanel.Dark).rectTransform;
            m_Info.Anchor(0, 0, 1, 0, new Vector2(Margin, BottomUnits + Margin), new Vector2(-Margin, BottomUnits + Margin + InfoHeight));

            var slot = PixelUi.Panel(m_Info, "portrait", PixelPanel.Slot);
            slot.raycastTarget = false;
            var srt = slot.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0f, 1f);
            srt.pivot = new Vector2(0f, 1f);
            srt.sizeDelta = new Vector2(PortraitSize, PortraitSize);
            srt.anchoredPosition = new Vector2(BarPad, -BarPad);
            m_InfoPortrait = PixelUi.Icon(srt, "icon", "icon_cat", 2);

            m_InfoTitle = PixelUi.Text(m_Info, "title", "", PixelUi.FontLarge, PixelUi.TextAccent, TextAlignmentOptions.Left, true, true);
            m_InfoTitle.rectTransform.Anchor(0, 1, 1, 1, new Vector2(InfoTextLeft, -(BarPad + InfoTitleHeight)), new Vector2(-BarPad, -BarPad));
            m_InfoTitle.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(m_InfoTitle, PixelUi.FontBody, PixelUi.FontLarge);

            m_InfoBody = PixelUi.Text(m_Info, "body", "", PixelUi.FontBody, PixelUi.TextColor, TextAlignmentOptions.TopLeft, false, true);
            m_InfoBody.rectTransform.Anchor(0, 0, 1, 1, new Vector2(InfoTextLeft, BarPad + InfoBarHeight + 2 * Px), new Vector2(-BarPad, -(BarPad + InfoTitleHeight + Px)));
            Ui.AutoSize(m_InfoBody, PixelUi.FontSmall * 0.8f, PixelUi.FontBody);

            m_InfoBar = PixelUi.Bar(m_Info, "bar", PixelBarFill.Green);
            m_InfoBar.Rect.Anchor(0, 0, 1, 0, new Vector2(InfoTextLeft, BarPad), new Vector2(-BarPad, BarPad + InfoBarHeight));
            m_Info.gameObject.SetActive(false);
        }

        void HideInfo()
        {
            if (m_Info != null) Ui.SetActive(m_Info, false);
            m_InfoValid = false;
        }

        void InvalidateInfo() => m_InfoValid = false;

        /// <summary>Верхний край панели сведений в пикселях экрана (контроллер поднимает выбранное над ней).</summary>
        public bool TryGetInfoTop(out float screenY)
        {
            if (m_Info != null && m_Info.gameObject.activeInHierarchy)
            {
                m_Info.GetWorldCorners(s_Corners);
                screenY = RectTransformUtility.WorldToScreenPoint(UiCamera, s_Corners[1]).y;
                return true;
            }
            screenY = 0f;
            return false;
        }

        /// <summary>Панель сведений показана.</summary>
        public bool InfoVisible => m_Info != null && m_Info.gameObject.activeSelf;

        void RefreshInfo(OutpostSimulation sim)
        {
            var sel = m_Game.Selection;
            if (InTransition || AnyWindowOpen || (sel.Kind != OutpostSelectionKind.Worker && sel.Kind != OutpostSelectionKind.Target))
            {
                HideInfo();
                return;
            }
            bool ok = sel.Kind == OutpostSelectionKind.Worker ? RefreshWorkerInfo(sim, sel.Id) : RefreshTargetInfo(sim, sel.Id);
            if (!ok)
            {
                HideInfo();
                return;
            }
            Ui.SetActive(m_Info, true);
        }

        bool RefreshWorkerInfo(OutpostSimulation sim, int id)
        {
            var w = sim.WorkerById(id);
            if (w == null) return false;
            int cap = sim.Stats != null ? sim.Stats.Capacity : 0, bag = w.Bag.Count;
            var key = new ValueKey
            {
                A = 1, B = id, C = (int)w.Mode, D = bag, E = cap, F = w.TargetId,
                G = bag > 0 ? w.Bag[0].Source * 1000 + w.Bag[0].Ordinal : -1,
                H = bag > 0 ? w.Bag[bag - 1].Source * 1000 + w.Bag[bag - 1].Ordinal : -1,
            };
            if (m_InfoValid && key.Equals(m_InfoKey)) return true;
            m_InfoKey = key;
            m_InfoValid = true;

            PixelUi.SetIcon(m_InfoPortrait, "icon_cat", 2);
            m_InfoPortrait.rectTransform.anchoredPosition = OutpostDialog.CenterOffset("icon_cat", 2);
            Ui.SetText(m_InfoTitle, Loc.F("outpost.info.worker", id));
            var value = BigInteger.Zero;
            for (int i = 0; i < bag; i++) value += w.Bag[i].Value;
            string task = OutpostUiText.Task(w.Mode);
            bool toTarget = (w.Mode == WorkerMode.MovingToWork || w.Mode == WorkerMode.Mining) && w.TargetId >= 0;
            if (toTarget) task = Loc.F("outpost.info.task_target", task, OutpostUiText.TargetName(sim.Map.Target(w.TargetId)));
            var sb = m_Sb.Clear();
            sb.Append(Loc.F("outpost.info.task", task)).Append('\n');
            sb.Append(Loc.F("outpost.info.bag", bag, cap)).Append('\n');
            sb.Append(Loc.F("outpost.info.cargo_value", OutpostUiText.Money(value)));
            Ui.SetText(m_InfoBody, sb.ToString());
            m_InfoBar.SetFill(bag >= cap && cap > 0 ? PixelBarFill.Gold : PixelBarFill.Green);
            m_InfoBar.SetValue(cap > 0 ? Mathf.Clamp01((float)bag / cap) : 0f);
            return true;
        }

        bool RefreshTargetInfo(OutpostSimulation sim, int id)
        {
            var mt = sim.Map.Target(id);
            var ts = sim.TargetById(id);
            if (mt == null || ts == null) return false;
            var run = sim.Run;
            int assigned = sim.AssignedTo(id);
            int remaining = Mathf.Max(0, ts.TotalUnits - ts.ReleasedUnits);
            var key = new ValueKey
            {
                A = 2, B = id, C = ts.Destroyed ? 1 : 0, D = assigned, E = remaining, F = run.GatesOpened, G = run.NavVersion,
                H = (int)run.Phase * 100000 + LivingInSector(sim, run.GatesOpened), L = ts.RemainingHpUnits,
            };
            if (m_InfoValid && key.Equals(m_InfoKey)) return true;
            m_InfoKey = key;
            m_InfoValid = true;

            string sprite = OutpostUiText.TargetPortrait(mt, out int scale);
            PixelUi.SetIcon(m_InfoPortrait, sprite, scale);
            m_InfoPortrait.rectTransform.anchoredPosition = OutpostDialog.CenterOffset(sprite, scale);
            Ui.SetText(m_InfoTitle, OutpostUiText.TargetName(mt));
            var sb = m_Sb.Clear();
            sb.Append(Loc.F("outpost.info.hp", OutpostUiText.Hp(ts.RemainingHpUnits), OutpostUiText.Hp(ts.MaxHpUnits))).Append('\n');
            sb.Append(Loc.F("outpost.info.workers", assigned)).Append('\n');
            sb.Append(Loc.Plural("outpost.info.loot", remaining));
            string reason = TargetReason(sim, mt, ts);
            if (reason != null) sb.Append('\n').Append(BadTag).Append(reason).Append(ColorClose);
            if (sim.CanStrike(id)) sb.Append('\n').Append(Loc.T("outpost.info.strike"));
            Ui.SetText(m_InfoBody, sb.ToString());
            m_InfoBar.SetFill(PixelBarFill.Red);
            m_InfoBar.SetValue(ts.MaxHpUnits > 0 ? Mathf.Clamp01((float)((double)ts.RemainingHpUnits / ts.MaxHpUnits)) : 0f);
            return true;
        }

        /// <summary>Живых r/s/x в секторе <paramref name="sector"/> (ворота сектора ждут их расчистки).</summary>
        static int LivingInSector(OutpostSimulation sim, int sector)
        {
            int n = 0;
            var targets = sim.Map.Targets;
            var states = sim.Run.Targets;
            for (int i = 0; i < targets.Length; i++)
                if (!targets[i].IsGate && targets[i].Sector == sector && !states[i].Destroyed) n++;
            return n;
        }

        /// <summary>
        /// Почему котики сейчас не берут цель (ТЗ 14): расчищено; закрытый сектор (за неоткрытыми воротами); ворота ждут
        /// расчистки своего сектора; нет пути к рабочим клеткам от базы. null — цель доступна.
        /// </summary>
        static string TargetReason(OutpostSimulation sim, MapTarget mt, TargetState ts)
        {
            if (ts.Destroyed) return Loc.T("outpost.reason.cleared");
            var run = sim.Run;
            int sector = run.GatesOpened;
            if (mt.Sector > sector) return Loc.T("outpost.reason.closed_sector");
            if (mt.IsGate && LivingInSector(sim, sector) > 0) return Loc.T("outpost.reason.wait_sector");
            if (run.Phase != RunPhase.Running) return null;
            var nav = sim.Navigation;
            var field = nav.From(sim.Map.IntakeCell);
            var cells = mt.WorkCells;
            if (cells != null)
                for (int i = 0; i < cells.Length; i++)
                    if (nav.Passable(cells[i]) && field.Dist[cells[i]] >= 0) return null;
            return Loc.T("outpost.reason.no_path");
        }
    }
}

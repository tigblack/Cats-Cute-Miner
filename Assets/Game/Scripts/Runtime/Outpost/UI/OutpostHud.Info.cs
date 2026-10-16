using System;
using CatMiners.Core;
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
    /// груза. Цель: имя типа, HP (шкала), работники на цели, оставшаяся добыча (Total − Released порций), причина
    /// недоступности. Завод (ТЗ 2.2 §8, цель s с <see cref="OutpostMap.Factory"/>): «завод · товар», состояние с циклом, выпуск
    /// и цена, оставшееся время (шкала цикла) или HP цепей и сундук, строка автодоставки с именем требуемой карточки; в Ready —
    /// кнопка «Deliver ×N». Строки пересобираются только при смене исходных значений.
    /// </summary>
    public sealed partial class OutpostHud
    {
        const float InfoHeight = 64 * Px;
        /// <summary>Сведения завода: четыре строки и перенос строки автодоставки; в Ready под ними — ряд кнопки «Deliver».</summary>
        const float FactoryInfoHeight = 76 * Px, InfoDeliverHeight = 18 * Px;
        // Раскладка панели: обычная (котик, цель), завод со шкалой, завод с кнопкой «Deliver», завод без шкалы (Stopped).
        const int InfoLayoutPlain = 0, InfoLayoutFactory = 1, InfoLayoutDeliver = 2, InfoLayoutStopped = 3;
        const float PortraitSize = 38 * Px;
        const float InfoTextLeft = BarPad + PortraitSize + 4 * Px, InfoTitleHeight = 13 * Px, InfoBarHeight = 8 * Px;

        RectTransform m_Info;
        Image m_InfoPortrait;
        TextMeshProUGUI m_InfoTitle, m_InfoBody;
        PixelBar m_InfoBar;
        ValueKey m_InfoKey;
        bool m_InfoValid;
        Image m_InfoGood, m_InfoAuto;
        Ui.ButtonRefs m_InfoDeliver;
        int m_InfoLayout = -1;
        /// <summary>Завод, для которого показана кнопка «Deliver», и причина её недоступности (тост по тапу).</summary>
        int m_InfoFactoryId = -1;
        string m_InfoDeliverReason;
        // Цена единицы товара показанного завода: постоянна в пределах шахты — кэш по run и товару.
        OutpostRun m_InfoPriceRun;
        string m_InfoPriceGood;
        BigInteger m_InfoPrice;

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

            // Завод: иконка товара в углу портрета, значок AUTO и кнопка «Deliver ×N» (готовая партия, ручной режим).
            // Pivot иконки — центр холста 16×16 (атлас): центр ставится на 9 пикселей арта внутрь от угла ячейки портрета.
            m_InfoGood = PixelUi.Icon(srt, "good", "icon_factory");
            m_InfoGood.rectTransform.anchorMin = m_InfoGood.rectTransform.anchorMax = new Vector2(1f, 0f);
            m_InfoGood.rectTransform.anchoredPosition = new Vector2(-9 * Px, 9 * Px);
            m_InfoGood.raycastTarget = false;
            m_InfoGood.gameObject.SetActive(false);
            m_InfoAuto = PixelUi.Icon(srt, "auto", "icon_auto");
            m_InfoAuto.rectTransform.anchorMin = m_InfoAuto.rectTransform.anchorMax = new Vector2(0f, 1f);
            m_InfoAuto.rectTransform.anchoredPosition = new Vector2(9 * Px, -9 * Px);
            m_InfoAuto.raycastTarget = false;
            m_InfoAuto.gameObject.SetActive(false);
            m_InfoDeliver = PixelUi.Button(m_Info, "deliver", "", PixelButton.Green, OnInfoDeliver, PixelUi.FontBody);
            m_InfoDeliver.Rect.Anchor(0, 0, 1, 0, new Vector2(InfoTextLeft, BarPad), new Vector2(-BarPad, BarPad + InfoDeliverHeight));
            m_InfoDeliver.Label.textWrappingMode = TextWrappingModes.NoWrap;
            m_InfoDeliver.SetIcon("icon_deliver");
            m_InfoDeliver.Icon.rectTransform.anchorMin = m_InfoDeliver.Icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            m_InfoDeliver.Icon.rectTransform.anchoredPosition = new Vector2(12 * Px, Px);
            m_InfoDeliver.Label.rectTransform.offsetMin = new Vector2(22 * Px, m_InfoDeliver.Label.rectTransform.offsetMin.y);
            AddDeniedTap(m_InfoDeliver, OnInfoDeliverDenied);
            m_InfoDeliver.SetActive(false);
            SetInfoLayout(InfoLayoutPlain);
            m_Info.gameObject.SetActive(false);
        }

        /// <summary>Высота панели, область текста и нижний ряд (шкала, кнопка «Deliver» или ничего) по виду сведений.</summary>
        void SetInfoLayout(int layout)
        {
            if (layout == m_InfoLayout) return;
            m_InfoLayout = layout;
            float height = layout == InfoLayoutPlain ? InfoHeight
                : layout == InfoLayoutDeliver ? FactoryInfoHeight + InfoDeliverHeight - InfoBarHeight
                : layout == InfoLayoutStopped ? FactoryInfoHeight - InfoBarHeight - 2 * Px : FactoryInfoHeight;
            m_Info.Anchor(0, 0, 1, 0, new Vector2(Margin, BottomUnits + Margin), new Vector2(-Margin, BottomUnits + Margin + height));
            float row = layout == InfoLayoutDeliver ? InfoDeliverHeight + 2 * Px : layout == InfoLayoutStopped ? 0f : InfoBarHeight + 2 * Px;
            m_InfoBody.rectTransform.Anchor(0, 0, 1, 1, new Vector2(InfoTextLeft, BarPad + row), new Vector2(-BarPad, -(BarPad + InfoTitleHeight + Px)));
            Ui.SetActive(m_InfoBar, layout == InfoLayoutPlain || layout == InfoLayoutFactory);
            m_InfoDeliver.SetActive(layout == InfoLayoutDeliver);
            bool factory = layout != InfoLayoutPlain;
            // Сведения завода: текст не мельче FontSmall (строки короче целей; перенос строки автодоставки помещается).
            Ui.AutoSize(m_InfoBody, factory ? PixelUi.FontSmall : PixelUi.FontSmall * 0.8f, PixelUi.FontBody);
            if (!factory)
            {
                Ui.SetActive(m_InfoGood, false);
                Ui.SetActive(m_InfoAuto, false);
                m_InfoFactoryId = -1;
            }
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

            SetInfoLayout(InfoLayoutPlain);
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
            var mf = sim.Map.Factory(id);
            var factory = mf != null ? m_Game.FactoryOf(id) : null;
            if (factory != null) return RefreshFactoryInfo(sim, mt, ts, mf, factory);
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

            SetInfoLayout(InfoLayoutPlain);
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

        /// <summary>
        /// Сведения завода (ТЗ 2.2 §8): заголовок «Dairy · Milk»; Locked — «Dairy — locked. Break the chains» (§2 п. 1), HP цепей
        /// (шкала) и сундук; Working — цикл, выпуск и цена, оставшееся время (шкала цикла по Sim.Now); Ready — готовая партия и
        /// кнопка «Deliver ×N»; Stopped — шахта завершена. Последняя строка — автодоставка: «on» или требуемая карточка токена s
        /// с уровнем игрока. Выпуск — Sim.FactoryUnitsPerCycle (бонус карточек ядра), в Ready — зафиксированный ReadyUnits; цена —
        /// кэш по run.
        /// </summary>
        bool RefreshFactoryInfo(OutpostSimulation sim, MapTarget mt, TargetState ts, MapFactory mf, FactoryState f)
        {
            var run = sim.Run;
            int id = mt.Id;
            long now = sim.Now;
            bool working = f.Status == FactoryStatus.Working && f.CycleEnd > f.CycleStart;
            long left = working ? Math.Max(0L, f.CycleEnd - now) : 0L;
            int leftSec = (int)Math.Min(int.MaxValue, (left + OutpostIds.TicksPerSecond - 1) / OutpostIds.TicksPerSecond);
            int units = f.Status == FactoryStatus.Ready ? f.ReadyUnits : f.Status == FactoryStatus.Locked ? 0 : sim.FactoryUnitsPerCycle(id);
            var profile = m_Game.State != null ? m_Game.State.Profile : null;
            int have = profile != null ? profile.CardLevel(mf.RequiredCardId) : 0;
            string deliver = f.Status == FactoryStatus.Ready ? (CommandsAllowed(out string blocked) ? m_Game.DeliverBlockReason(id) : blocked) : null;
            m_InfoDeliverReason = deliver;
            m_InfoFactoryId = id;

            // Шкала: цикл — плавно по Sim.Now (каждое обновление, без выделений); цепи — по HP.
            if (working) m_InfoBar.SetValue((float)((double)(now - f.CycleStart) / (f.CycleEnd - f.CycleStart)));
            else if (f.Status == FactoryStatus.Locked)
                m_InfoBar.SetValue(ts.MaxHpUnits > 0 ? Mathf.Clamp01((float)((double)ts.RemainingHpUnits / ts.MaxHpUnits)) : 0f);

            if (!ReferenceEquals(m_InfoPriceRun, run) || m_InfoPriceGood != f.GoodId)
            {
                m_InfoPriceRun = run;
                m_InfoPriceGood = f.GoodId;
                m_InfoPrice = m_Game.GoodsPrice(f.GoodId);
            }
            var key = new ValueKey
            {
                A = 3, B = id, C = (int)f.Status * 8 + (f.Automated ? 1 : 0) + (deliver == null ? 2 : 0) + (run.Phase == RunPhase.ReadyToContinue ? 4 : 0),
                D = units, E = leftSec, F = have, G = run.GatesOpened, H = run.NavVersion, L = ts.RemainingHpUnits, N1 = m_InfoPrice,
            };
            if (m_InfoValid && key.Equals(m_InfoKey)) return true;
            m_InfoKey = key;
            m_InfoValid = true;

            SetInfoLayout(f.Status == FactoryStatus.Ready ? InfoLayoutDeliver : f.Status == FactoryStatus.Stopped ? InfoLayoutStopped : InfoLayoutFactory);
            PixelUi.SetIcon(m_InfoPortrait, "icon_factory", 2);
            m_InfoPortrait.rectTransform.anchoredPosition = OutpostDialog.CenterOffset("icon_factory", 2);
            string goodIcon = FactoryText.GoodIcon(f.GoodId);
            PixelUi.SetIcon(m_InfoGood, goodIcon);
            Ui.SetActive(m_InfoGood, true);
            Ui.SetActive(m_InfoAuto, f.Automated);
            string name = FactoryText.FactoryName(f.GoodId), good = FactoryText.GoodName(f.GoodId);
            Ui.SetText(m_InfoTitle, Loc.F("outpost.factory.title", name, good));

            string price = OutpostUiText.Money(m_InfoPrice);
            string cycle = NumberFormat.FormatDuration(run.FactoryRules?.ForType(f.TypeId)?.CycleSec ?? 0);
            var sb = m_Sb.Clear();
            switch (f.Status)
            {
                case FactoryStatus.Locked:
                    // Удержание на закрытом заводе (ТЗ 2.2 §2 п. 1): «Dairy — locked. Break the chains».
                    sb.Append(Loc.F("factories.locked_hint", name)).Append('\n');
                    sb.Append(Loc.F("outpost.factory.chains", OutpostUiText.Hp(ts.RemainingHpUnits), OutpostUiText.Hp(ts.MaxHpUnits)));
                    if (mt.RewardId != null && m_Game.Configs?.Cards?.Chest(mt.RewardId) != null)
                        sb.Append('\n').Append(Loc.T("outpost.factory.chest"));
                    string reason = TargetReason(sim, mt, ts);
                    if (reason != null) sb.Append('\n').Append(BadTag).Append(reason).Append(ColorClose);
                    m_InfoBar.SetFill(PixelBarFill.Red);
                    break;
                case FactoryStatus.Ready:
                    sb.Append(Loc.F("outpost.factory.ready", cycle)).Append('\n');
                    sb.Append(Loc.F("outpost.factory.batch", units, good, price));
                    m_InfoDeliver.SetLabel(Loc.F("outpost.factory.deliver", units));
                    PixelUi.SetIcon(m_InfoDeliver.Icon, goodIcon);
                    m_InfoDeliver.SetInteractable(deliver == null);
                    break;
                case FactoryStatus.Stopped:
                    sb.Append(Loc.T("outpost.factory.stopped")).Append('\n');
                    sb.Append(Loc.F("outpost.factory.output", units, good, price));
                    break;
                default:
                    sb.Append(Loc.F("outpost.factory.working", cycle)).Append('\n');
                    sb.Append(Loc.F("outpost.factory.output", units, good, price)).Append('\n');
                    sb.Append(Loc.F("outpost.factory.next", NumberFormat.FormatDuration(leftSec)));
                    m_InfoBar.SetFill(PixelBarFill.Green);
                    break;
            }
            sb.Append('\n').Append(AutoDeliveryLine(mf, f, have));
            Ui.SetText(m_InfoBody, sb.ToString());
            return true;
        }

        /// <summary>
        /// Строка автодоставки (ТЗ 2.2 §8) по требованию токена s: «Auto delivery: on», «needs Dairy Manager Lv 3 (you have Lv 1)»
        /// или «needs Dairy Foreman Lv 2 (not collected)» при уровне 0. Включённый флаг завода не снимается — «on».
        /// </summary>
        string AutoDeliveryLine(MapFactory mf, FactoryState f, int have)
        {
            if (f.Automated || (mf.RequiredCardId != null && have >= 1 && have >= mf.RequiredCardLevel)) return Loc.T("outpost.factory.auto_on");
            string card = OutpostCardText.Name(m_Game.Configs?.Cards?.Get(mf.RequiredCardId));
            if (string.IsNullOrEmpty(card)) card = mf.RequiredCardId ?? "";
            return have < 1
                ? Loc.F("outpost.factory.auto_missing", card, mf.RequiredCardLevel)
                : Loc.F("outpost.factory.auto_needs", card, mf.RequiredCardLevel, have);
        }

        /// <summary>«Deliver» в сведениях готового завода: команда ядра DeliverFactory через контроллер (сразу запись).</summary>
        void OnInfoDeliver()
        {
            if (m_Game == null || m_InfoFactoryId < 0) return;
            if (!CommandsAllowed(out string blocked))
            {
                Denied(blocked);
                return;
            }
            var result = m_Game.DeliverFactory(m_InfoFactoryId);
            if (!result.Ok) Toast(FactoryText.Reason(result.Reason));
            InvalidateInfo();
            RefreshNow();
        }

        void OnInfoDeliverDenied()
        {
            if (!CommandsAllowed(out string blocked)) Toast(OutpostUiText.Reason(blocked));
            else if (m_InfoDeliverReason != null) Toast(FactoryText.Reason(m_InfoDeliverReason));
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

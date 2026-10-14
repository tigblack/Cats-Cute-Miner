using System;
using System.Collections.Generic;
using System.Text;
using CatMiners.Core;
using CatMiners.Core.Grid;
using CatMiners.Core.Sim;
using CatMiners.Core.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Контекстная панель выбранного объекта или котика (ТЗ 13.1, «UI при тапе по шахте»): тёмная панель над нижней частью
    /// HUD — портрет (пиксельная иконка объекта, спрайт камня или котик в каске), заголовок, строки состояния и до трёх
    /// кнопок (золотая — главное действие, синяя — второстепенное, зелёная — сбор; недоступная — серая, цена, на которую
    /// не хватает, красная). Строки и кнопки пересобираются только при смене исходных значений (ValueKey).
    /// </summary>
    public sealed partial class Hud
    {
        /// <summary>Высота панели с кнопками и без них; кнопки — 24 пикселя арта (крупнее строки списка: это действия поля).</summary>
        const float CtxHeight = 91 * Px, CtxHeightNoButtons = 64 * Px, CtxButtonHeight = 24 * Px;
        /// <summary>Ячейка портрета: внутри 26 пикселей арта — иконка ×2 почти без полей, спрайт поля ×1.</summary>
        const float PortraitSize = 38 * Px;
        const float CtxTextLeft = BarPad + PortraitSize + 4 * Px, CtxTitleHeight = 13 * Px;

        // Окрас, каска и кирка котика — как в CatView (Views.cs): окрас по id, каска по (уровень − 1) mod 8, кирка по восьмёркам уровней.

        RectTransform m_Context;
        RectTransform m_CtxButtonRow;
        TextMeshProUGUI m_CtxTitle, m_CtxBody;
        // Слои портрета: один спрайт объекта или тело, каска и кирка котика.
        readonly Image[] m_Portrait = new Image[4];   // котик: тело, (свободный слой), каска, оружие; объект — один слой
        readonly Ui.ButtonRefs[] m_CtxButtons = new Ui.ButtonRefs[3];
        readonly Action[] m_CtxActions = new Action[3];
        // Желаемое состояние трёх кнопок: применяются только отличия от показанного.
        readonly string[] m_WantLabel = new string[3];
        readonly bool[] m_WantEnabled = new bool[3];
        readonly PixelButton[] m_WantStyle = new PixelButton[3];
        readonly Action[] m_WantAction = new Action[3];
        int m_CtxOrd = -1;
        int m_CtxButtonsShown = -1;
        // Действия кнопок создаются один раз и читают ordinal панели в момент нажатия.
        Action m_ActActivate, m_ActOpenGate, m_ActNextMine, m_ActUpgrade, m_ActToThreshold, m_ActCollect;

        void BuildContext()
        {
            m_Context = PixelUi.Panel(m_Safe, "Context", PixelPanel.Dark).rectTransform;
            m_Context.Anchor(0, 0, 1, 0, new Vector2(Margin, BottomUnits + Margin), new Vector2(-Margin, BottomUnits + Margin + CtxHeight));

            var slot = PixelUi.Panel(m_Context, "portrait", PixelPanel.Slot);
            slot.raycastTarget = false;
            var srt = slot.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0f, 1f);
            srt.pivot = new Vector2(0f, 1f);
            srt.sizeDelta = new Vector2(PortraitSize, PortraitSize);
            srt.anchoredPosition = new Vector2(BarPad, -BarPad);
            for (int i = 0; i < m_Portrait.Length; i++)
            {
                m_Portrait[i] = PixelUi.Icon(srt, "layer" + i, "icon_shaft");
                if (i > 0) m_Portrait[i].gameObject.SetActive(false);
            }

            m_CtxTitle = PixelUi.Text(m_Context, "title", "", PixelUi.FontLarge, PixelUi.TextAccent, TextAlignmentOptions.Left, true, true);
            m_CtxTitle.rectTransform.Anchor(0, 1, 1, 1, new Vector2(CtxTextLeft, -(BarPad + CtxTitleHeight)), new Vector2(-BarPad, -BarPad));
            m_CtxTitle.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(m_CtxTitle, PixelUi.FontBody, PixelUi.FontLarge);
            m_CtxBody = PixelUi.Text(m_Context, "body", "", PixelUi.FontBody, PixelUi.TextColor, TextAlignmentOptions.TopLeft, false, true);
            Ui.AutoSize(m_CtxBody, PixelUi.FontSmall * 0.8f, PixelUi.FontBody);

            // Ряд кнопок: активные делят ширину поровну (неактивные раскладка не учитывает).
            m_CtxButtonRow = Ui.Rect(m_Context, "buttons").Anchor(0, 0, 1, 0, new Vector2(BarPad, BarPad), new Vector2(-BarPad, BarPad + CtxButtonHeight));
            var row = m_CtxButtonRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = SlotGap;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = true;
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                var b = PixelUi.Button(m_CtxButtonRow, "ctx" + i, "", PixelButton.Gold, () => m_CtxActions[k]?.Invoke(), PixelUi.FontBody);
                b.Label.textWrappingMode = TextWrappingModes.NoWrap;
                b.SetActive(false);
                m_CtxButtons[i] = b;
            }
            SetContextButtons(false);
            m_Context.gameObject.SetActive(false);
        }

        /// <summary>Высота панели и область текста — по наличию кнопок (без кнопок панель ниже и меньше закрывает поле).</summary>
        void SetContextButtons(bool any)
        {
            int state = any ? 1 : 0;
            if (state == m_CtxButtonsShown) return;
            m_CtxButtonsShown = state;
            float height = any ? CtxHeight : CtxHeightNoButtons;
            m_Context.offsetMax = new Vector2(m_Context.offsetMax.x, BottomUnits + Margin + height);
            float bottom = any ? BarPad + CtxButtonHeight + SlotGap : BarPad;
            m_CtxBody.rectTransform.Anchor(0, 0, 1, 1, new Vector2(CtxTextLeft, bottom), new Vector2(-BarPad, -(BarPad + CtxTitleHeight + Px)));
            Ui.SetActive(m_CtxButtonRow, any);
        }

        void HideContext()
        {
            Ui.SetActive(m_Context, false);
            m_CtxValid = false;
        }

        void Want(int i, string label, bool enabled, Action action, PixelButton style)
        {
            m_WantLabel[i] = label;
            m_WantEnabled[i] = enabled;
            m_WantAction[i] = action;
            m_WantStyle[i] = style;
        }

        void ApplyCtxButtons()
        {
            bool any = false;
            for (int i = 0; i < 3; i++)
            {
                var b = m_CtxButtons[i];
                string label = m_WantLabel[i];
                b.SetActive(label != null);
                m_CtxActions[i] = label != null ? m_WantAction[i] : null;
                if (label == null) continue;
                any = true;
                b.SetLabel(label);
                // SetStyle и SetInteractable сами ничего не делают, если значение не изменилось.
                if (b.Style != m_WantStyle[i]) b.SetStyle(m_WantStyle[i]);
                b.SetInteractable(m_WantEnabled[i]);
            }
            SetContextButtons(any);
        }

        void RefreshContext(GameSession s)
        {
            int ord = m_Game.SelectedOrdinal;
            int catId = m_Game.SelectedCatId;
            var key = new ValueKey { A = ord, B = catId, H = m_Game.ShowDebugInfo ? 1 : 0 };
            CatState cat = null;
            if (catId > 0)
            {
                cat = s.FindCat(catId);
                if (cat == null)
                {
                    HideContext();
                    return;
                }
                key.C = cat.Level;
                key.D = cat.TargetIndex;
                int target = cat.TargetIndex >= 0 ? s.OrdinalOf(cat.TargetIndex) : -1;
                key.L = target >= 0 ? CeilSeconds(s.EtaSeconds(target)) : -2;
            }
            else if (ord >= 0 && ord < s.Layout.Objects.Count)
            {
                ObjectKey(s, ord, ref key);
            }
            else
            {
                HideContext();
                return;
            }
            Ui.SetActive(m_Context, true);
            if (m_CtxValid && key.Equals(m_CtxKey)) return;
            bool sameSubject = m_CtxValid && m_CtxKey.A == key.A && m_CtxKey.B == key.B && m_CtxKey.C == key.C;
            m_CtxKey = key;
            m_CtxValid = true;
            m_CtxOrd = ord;
            for (int i = 0; i < 3; i++) m_WantLabel[i] = null;
            if (cat != null)
            {
                if (!sameSubject) SetCatPortrait(cat);
                CatContext(s, cat);
            }
            else
            {
                if (!sameSubject) SetPortrait(ObjectPortrait(s, ord));
                ObjectContext(s, ord);
            }
            ApplyCtxButtons();
        }

        // ───────────── Портрет ─────────────

        /// <summary>Спрайт портрета объекта: иконка вида (шахта, кузница, ворота, выход) или спрайт поля (камень, препятствие).</summary>
        static string ObjectPortrait(GameSession s, int ord)
        {
            var obj = s.Layout.Objects[ord];
            switch (obj.Kind)
            {
                case ObjectKind.Shaft: return "icon_shaft";
                case ObjectKind.Cart: return "icon_forge";
                case ObjectKind.Gate: return "icon_gate";
                case ObjectKind.Exit: return "icon_exit";
                case ObjectKind.Rock:
                {
                    // Вариант валуна — тот же, что у ObjectView на поле.
                    int variant = (int)(BoardView.Hash(obj.Index, s.Layout.ZoneNumber, 17) % 3u);
                    return PresentationCatalog.RockSprite(s.Balance, obj.TypeId, variant);
                }
                default: return PresentationCatalog.ObstructionSingleSprite(obj.TypeId) ?? "block_single_1";
            }
        }

        /// <summary>Иконки (16×16) — ×2, спрайты поля (32 пикселя на клетку) — ×1; видимая часть по центру ячейки.</summary>
        void SetPortrait(string sprite)
        {
            int scale = sprite.StartsWith("icon_", StringComparison.Ordinal) ? 2 : 1;
            SetLayer(0, sprite, scale, CenterOffset(sprite, scale));
            for (int i = 1; i < m_Portrait.Length; i++) Ui.SetActive(m_Portrait[i], false);
        }

        /// <summary>
        /// Котик своего окраса с оружием и каской своего уровня (CatLook): тело, каска и оружие с общей точкой ног,
        /// по центру — видимая часть тела.
        /// </summary>
        void SetCatPortrait(CatState cat)
        {
            int coat = CatLook.Coat(cat.Id);
            int level = Math.Max(1, cat.Level);
            string body = CatLook.Body(coat, CatLook.Idle);
            var offset = CenterOffset(body, 1);
            SetLayer(0, body, 1, offset);
            Ui.SetActive(m_Portrait[1], false);
            SetLayer(2, CatLook.Helmet(CatLook.HelmetColor(level), CatLook.Idle), 1, offset);
            SetLayer(3, CatLook.WeaponSprite(CatLook.Weapon(level), CatLook.Idle), 1, offset);
        }

        void SetLayer(int i, string sprite, int scale, Vector2 position)
        {
            var img = m_Portrait[i];
            PixelUi.SetIcon(img, sprite, scale);
            img.rectTransform.anchoredPosition = position;
            Ui.SetActive(img, true);
        }

        /// <summary>Сдвиг pivot спрайта, при котором центр его обрезанного прямоугольника стоит в центре ячейки.</summary>
        static Vector2 CenterOffset(string sprite, int scale) =>
            PixelArt.TryGetDef(sprite, out var d) ? new Vector2((d.PivotX - d.W * 0.5f) * Px * scale, (d.PivotY - d.H * 0.5f) * Px * scale) : Vector2.zero;

        // ───────────── Содержимое ─────────────

        /// <summary>Исходные значения панели объекта: статус, HP, работники, ETA, уровни, выплата, цены, условия перехода.</summary>
        static void ObjectKey(GameSession s, int ord, ref ValueKey key)
        {
            var obj = s.Layout.Objects[ord];
            var st = s.Mine.Objects[ord];
            key.C = (int)st.Status;
            if (obj.IsTarget && st.Status == ObjectStatus.Intact)
            {
                key.N1 = st.Hp;
                key.D = s.WorkersOn(ord);
                key.L = CeilSeconds(s.EtaSeconds(ord));
                if (s.IsTargetAvailable(ord)) key.G |= 1;
            }
            if (st.Status == ObjectStatus.Producing && st.Gen != null)
            {
                var gen = st.Gen;
                key.D = gen.Level;
                key.E = gen.ThresholdsReached;
                if (gen.Automated) key.G |= 2;
                if (gen.PayoutReady) key.G |= 4;
                key.N1 = gen.PendingPayout;
                key.N2 = s.GeneratorOutput(ord);
                if (gen.Level < s.MaxGeneratorLevel(ord))
                {
                    if (s.Mine.Gold >= s.UpgradeCost(ord, gen.Level)) key.G |= 8;
                    s.PreviewToThreshold(ord, out int levels, out GameNumber total);
                    key.F = levels;
                    key.N3 = total;
                }
                if (obj.Kind == ObjectKind.Shaft && obj.CardId != null) key.L = s.CardLevel(obj.CardId);
            }
            if (obj.Kind == ObjectKind.Exit && st.Status == ObjectStatus.Broken)
            {
                key.D = ProducingShafts(s);
                key.E = s.Mine.GatesOpened;
                if (s.Player.CampaignCompleted) key.G |= 16;
            }
        }

        void CatContext(GameSession s, CatState cat)
        {
            Ui.SetText(m_CtxTitle, Loc.F("hud.cat.title", PresentationCatalog.CatName, cat.Level));
            var sb = m_Sb.Clear();
            sb.Append(Loc.F("hud.cat.damage", NumberFormat.Format(s.CatDamage(cat.Level)), NumberFormat.Format(s.CatDps(cat.Level)))).Append('\n');
            int target = cat.TargetIndex >= 0 ? s.OrdinalOf(cat.TargetIndex) : -1;
            if (target >= 0)
            {
                // ТЗ 8.2: цель и оценка времени по всем назначенным работникам.
                string name = PresentationCatalog.ObjectName(s.Layout.Objects[target]);
                double eta = s.EtaSeconds(target);
                if (!double.IsInfinity(eta) && !double.IsNaN(eta)) sb.Append(Loc.F("hud.cat.working_eta", name, NumberFormat.FormatDuration(eta)));
                else sb.Append(Loc.F("hud.cat.working", name));
            }
            else sb.Append(Loc.T("hud.cat.idle"));
            Ui.SetText(m_CtxBody, sb.ToString());
        }

        /// <summary>Заголовок с уровнем: у производящей шахты — производственный, у целей расчистки — уровень токена (ТЗ 6.2, 9.3).</summary>
        static string ObjectTitle(ZoneObject obj, ObjectState st)
        {
            string name = PresentationCatalog.ObjectName(obj);
            if (obj.Kind == ObjectKind.Shaft && st.Status == ObjectStatus.Producing && st.Gen != null) return Loc.F("hud.object.title_level", name, st.Gen.Level);
            return obj.IsTarget ? Loc.F("hud.object.title_level", name, obj.Level) : name;
        }

        void ObjectContext(GameSession s, int ord)
        {
            var obj = s.Layout.Objects[ord];
            var st = s.Mine.Objects[ord];
            var body = m_Sb.Clear();
            Ui.SetText(m_CtxTitle, ObjectTitle(obj, st));

            if (obj.IsTarget && st.Status == ObjectStatus.Intact)
            {
                var max = s.Balance.TargetHp(obj.TypeId, obj.Level);
                body.Append(Loc.F("hud.object.hp", NumberFormat.Format(st.Hp), NumberFormat.Format(max))).Append('\n');
                int workers = s.WorkersOn(ord);
                double eta = s.EtaSeconds(ord);
                // ТЗ 8.2: при нулевом DPS — «Нет работников», а не бесконечность.
                if (workers == 0 || double.IsInfinity(eta) || double.IsNaN(eta)) body.Append(Loc.T("hud.object.no_workers"));
                else body.Append(Loc.F("hud.object.workers_eta", workers, NumberFormat.FormatDuration(eta)));
                if (!s.IsTargetAvailable(ord)) body.Append('\n').Append(Loc.T("hud.object.sector_closed"));
                if (obj.Kind == ObjectKind.Shaft) body.Append('\n').Append(Loc.T("hud.object.shaft_after_clear"));
            }

            switch (obj.Kind)
            {
                case ObjectKind.Shaft when st.Status == ObjectStatus.ReadyToActivate:
                    body.Append(Loc.T("hud.shaft.ready"));
                    Want(0, Loc.T("hud.shaft.activate"), true, m_ActActivate, PixelButton.Gold);
                    break;
                case ObjectKind.Shaft when st.Status == ObjectStatus.Producing:
                case ObjectKind.Cart:
                    if (st.Gen != null) GeneratorInfo(s, ord, body);
                    break;
                case ObjectKind.Gate when st.Status == ObjectStatus.Broken:
                    // ТЗ 11.1: бонусы подписываются как в таблице, значения — из данных CheckPoint.
                    body.Append(Loc.T("hud.gate.broken"));
                    var labels = GateBonusLabels(s);
                    for (int i = 0; i < labels.Count; i++) body.Append("\n• ").Append(labels[i]);
                    body.Append("\n• ").Append(Loc.T("hud.gate.reward_chest"));
                    Want(0, Loc.T("hud.gate.open"), true, m_ActOpenGate, PixelButton.Gold);
                    break;
                case ObjectKind.Gate when st.Status == ObjectStatus.Opened:
                    body.Append(Loc.T("hud.gate.opened"));
                    break;
                case ObjectKind.Exit when st.Status == ObjectStatus.Broken:
                {
                    bool last = s.Mine.Zone >= s.Balance.ZoneCount;
                    if (s.CanCompleteZone(out string why))
                    {
                        body.Append(last ? Loc.T("hud.exit.open_last") : Loc.T("hud.exit.open_next"));
                        Want(0, last ? Loc.T("hud.next_mine.finish") : Loc.T("hud.exit.go"), true, m_ActNextMine, PixelButton.Gold);
                    }
                    else
                    {
                        body.Append(why);
                        // ТЗ 11.2: переход камеры к ближайшей неактивированной шахте.
                        if (s.NearestInactiveShaft() >= 0) Want(0, Loc.T("hud.exit.to_shaft"), true, m_ActNextMine, PixelButton.Blue);
                    }
                    break;
                }
            }
            if (m_Game.ShowDebugInfo) body.Append("\n<size=70%>[").Append(s.ZoneId).Append(':').Append(obj.Index).Append("] ").Append(obj.Token).Append("</size>");
            Ui.SetText(m_CtxBody, body.ToString());
        }

        /// <summary>
        /// Генератор (ТЗ 9.2–9.3): производственный уровень (у шахты он в заголовке), выход и цикл, доход в секунду, ближайший
        /// порог с множителем и прогрессом внутри отрезка; у ручной шахты — причина без автоматизации (нужная карта по имени
        /// и её уровень) и отдельной строкой готовая выплата.
        /// На максимальном уровне кнопок улучшения нет. Цена «+1», на которую не хватает золота, — красная.
        /// </summary>
        void GeneratorInfo(GameSession s, int ord, StringBuilder body)
        {
            var gen = s.Mine.Objects[ord].Gen;
            var obj = s.Layout.Objects[ord];
            var def = s.DefFor(obj);
            var output = s.GeneratorOutput(ord);
            double cycle = GameSession.TicksToSeconds(s.GeneratorCycleTicks(ord));
            int max = s.MaxGeneratorLevel(ord);
            string perSecond = NumberFormat.Format(output / GameNumber.FromDouble(cycle));
            // У шахты уровень уже в заголовке (ObjectTitle) — строка выхода без него; у кузницы заголовок без уровня.
            if (obj.Kind == ObjectKind.Shaft)
                body.Append(Loc.F("hud.generator.rate", NumberFormat.Format(output), NumberFormat.FormatDuration(cycle), perSecond));
            else body.Append(Loc.F("hud.generator.output", gen.Level, NumberFormat.Format(output), NumberFormat.FormatDuration(cycle), perSecond));
            if (gen.Level >= max) body.Append('\n').Append(Loc.T("hud.generator.max_level"));
            else
            {
                int k = gen.ThresholdsReached;
                if (k >= 0 && k < def.Thresholds.Length && k < def.Objectives.CoreCurrencyMultiplier.Length)
                {
                    int next = def.Thresholds[k];
                    int prev = k > 0 ? def.Thresholds[k - 1] : 1;
                    body.Append('\n').Append(Loc.F("hud.generator.threshold", NumberFormat.Format(def.Objectives.CoreCurrencyMultiplier[k]), next,
                        gen.Level - prev, next - prev));
                }
            }
            if (obj.Kind == ObjectKind.Shaft)
            {
                if (gen.Automated) body.Append('\n').Append(Loc.T("hud.generator.automated"));
                else
                {
                    // ТЗ 9.2: неинтерактивный индикатор причины виден всегда, в том числе при готовой выплате. Карта — по имени
                    // из каталога, а не по ID: сырой текст GameSession.AutomationRequirement остаётся для DevPanel и тестов.
                    bool hasCard = !string.IsNullOrEmpty(obj.CardId);
                    int have = hasCard ? s.CardLevel(obj.CardId) : 0;
                    string card = hasCard ? PresentationCatalog.CardName(s.Balance, obj.CardId) : "?";
                    string owned = have > 0 ? Loc.F("core.automation.have_level", have) : Loc.T("core.automation.have_none");
                    body.Append('\n').Append(PixelUi.DimTag).Append(Loc.F("hud.generator.manual_need", card, obj.CardLevel, owned)).Append(ColorClose);
                    if (gen.PayoutReady) body.Append('\n').Append(Loc.F("hud.generator.payout_ready", NumberFormat.Format(gen.PendingPayout)));
                }
            }
            if (gen.Level < max)
            {
                var cost = s.UpgradeCost(ord, gen.Level);
                bool afford = s.Mine.Gold >= cost;
                string price = afford ? NumberFormat.Format(cost) : BadTag + NumberFormat.Format(cost) + ColorClose;
                Want(0, Loc.F("hud.generator.upgrade_one", price), afford, m_ActUpgrade, PixelButton.Gold);
                s.PreviewToThreshold(ord, out int levels, out GameNumber total);
                if (levels > 0) Want(1, Loc.F("hud.generator.to_threshold_cost", levels, NumberFormat.Format(total)), true, m_ActToThreshold, PixelButton.Blue);
                else Want(1, Loc.T("hud.generator.to_threshold"), false, null, PixelButton.Blue);
            }
            if (gen.PayoutReady && !gen.Automated)
                Want(2, Loc.F("hud.generator.collect", NumberFormat.Format(gen.PendingPayout)), true, m_ActCollect, PixelButton.Green);
        }

        /// <summary>
        /// Подписи бонусов ворот (ТЗ 11.1) в порядке таблицы: код 10 — множитель выхода, 2 — делитель цены найма,
        /// 7 — секунды интервала бесплатного котика, 1 — прибавка лимита. Значения — те, что GameSession применяет за одни ворота;
        /// нулевые не показываются.
        /// </summary>
        public static IReadOnlyList<string> GateBonusLabels(GameSession s)
        {
            var labels = new List<string>(4);
            if (s == null) return labels;
            // Множители применяются, только если положительны (GlobalIncomeMultiplier, HireDivisor).
            var v10 = s.CheckpointValue(10);
            if (v10.IsPositive) labels.Add(Loc.F("hud.gate_bonus.income", NumberFormat.Format(v10)));
            var v2 = s.CheckpointValue(2);
            if (v2.IsPositive) labels.Add(Loc.F("hud.gate_bonus.hire_divisor", NumberFormat.Format(v2)));
            var v7 = s.CheckpointValue(7);
            if (!v7.IsZero) labels.Add(Loc.F("hud.gate_bonus.free_cat_interval", NumberFormat.Format(v7)));
            var v1 = s.CheckpointValue(1);
            if (!v1.IsZero) labels.Add(Loc.F("hud.gate_bonus.cat_cap", NumberFormat.Format(v1)));
            return labels;
        }
    }
}

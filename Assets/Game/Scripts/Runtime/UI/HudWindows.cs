using System;
using System.Collections.Generic;
using CatMiners.Core;
using CatMiners.Core.Data;
using CatMiners.Core.Sim;
using CatMiners.Core.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Окна HUD на ките (PixelUi.Window): модальное окно с очередью (сундук, «Пока вас не было», переход, сообщения
    /// контроллера), настройки (звук, вибрация) и экран завершения кампании. Окна создаются один раз; кнопки и строки
    /// модального окна переиспользуются.
    /// </summary>
    public sealed partial class Hud
    {
        /// <summary>Строка списка в окне (награда сундука, пункт офлайна): иконка 16×16 и текст.</summary>
        const float RowHeight = 18 * Px, RowGap = 2 * Px;
        /// <summary>Поля окна кита: тело начинается на 14 пикселей арта ниже верха рамки, ряд кнопок — 11 пикселей над низом.</summary>
        const float WindowTop = 14 * Px, WindowBottom = 12 * Px, WindowButtons = 11 * Px + PixelUi.ButtonHeight + 4 * Px;
        /// <summary>Строка настроек: иконка, подпись, переключатель ×2.</summary>
        const float SettingsRowHeight = 26 * Px;

        /// <summary>
        /// Кнопка модального окна. Стиль — <see cref="Style"/>, а если он не задан — по цвету старого API: UiGreen — зелёная,
        /// UiPanelLight — тёмная, Gold/UiButton или цвет не задан — золотая, красные — красная, прочие — синяя.
        /// </summary>
        public struct ModalButton
        {
            public string Label;
            public Color Color;
            public Action Action;
            /// <summary>Стиль кнопки кита; null — по <see cref="Color"/>.</summary>
            public PixelButton? Style;
        }

        struct ModalRow
        {
            public string Icon;
            public string Text;
            public Color Color;
        }

        /// <summary>Содержимое модального окна: заголовок, крупная иконка, текст, строки с иконками, кнопки.</summary>
        sealed class ModalRequest
        {
            public string Title, Body, Icon;
            public ModalRow[] Rows;
            public ModalButton[] Buttons;
        }

        sealed class RowView
        {
            public Image Panel, Icon;
            public TextMeshProUGUI Text;
        }

        // Модальное окно. m_ModalCurrent и m_ModalLayoutSize — для повторной раскладки при смене размера safe area
        // (поворот, окно редактора, рендер в текстуру): рамка не должна остаться под прежний экран.
        ModalRequest m_ModalCurrent;
        Vector2 m_ModalLayoutSize;
        PixelWindow m_ModalWin;
        Image m_ModalIcon;
        RectTransform m_ModalRows;
        readonly List<Ui.ButtonRefs> m_ModalButtons = new List<Ui.ButtonRefs>();
        readonly List<Action> m_ModalActions = new List<Action>();
        readonly List<RowView> m_RowViews = new List<RowView>();
        readonly Queue<ModalRequest> m_ModalQueue = new Queue<ModalRequest>();
        bool m_ModalOpen;

        // Настройки.
        PixelWindow m_Settings;
        PixelToggle m_SoundToggle, m_VibroToggle, m_ReduceToggle;
        Image m_SoundIcon, m_VibroIcon;
        int m_SoundShown = -1, m_VibroShown = -1, m_ReduceShown = -1;
        // Ползунки громкости: Master, Music, Sfx, Ambience (порядок — AudioGroup).
        readonly PixelSlider[] m_Volume = new PixelSlider[4];

        // Экран завершения кампании (ТЗ 11.4). m_EndLayoutSize — размер safe area последней раскладки рамки.
        PixelWindow m_End;
        Ui.ButtonRefs m_EndChests;
        Vector2 m_EndLayoutSize;

        // ───────────── Модальное окно ─────────────

        void BuildModal()
        {
            m_ModalWin = PixelUi.Window(m_Safe, "Modal", "", null);
            m_ModalIcon = PixelUi.Icon(m_ModalWin.Body, "icon", "icon_chest_big", 2);
            m_ModalIcon.rectTransform.anchorMin = m_ModalIcon.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            m_ModalIcon.gameObject.SetActive(false);
            m_ModalRows = Ui.Rect(m_ModalWin.Body, "rows").Anchor(0, 1, 1, 1);
        }

        /// <summary>Модальное окно. Если уже открыто другое — ставится в очередь.</summary>
        public void Modal(string title, string body, params ModalButton[] buttons) =>
            Modal(new ModalRequest { Title = title, Body = body, Buttons = buttons });

        void Modal(ModalRequest request)
        {
            if (m_ModalOpen) m_ModalQueue.Enqueue(request);
            else OpenModal(request);
        }

        void OpenModal(ModalRequest r)
        {
            m_ModalOpen = true;
            m_ModalCurrent = r;
            var w = m_ModalWin;
            w.SetTitle(r.Title);
            var buttons = r.Buttons ?? Array.Empty<ModalButton>();
            for (int i = 0; i < buttons.Length; i++)
            {
                if (i == m_ModalButtons.Count)
                {
                    int k = i;
                    // Имя «btn» ищут тесты (PlayModeUtil.FindModalButton); первый текст кнопки — её подпись.
                    m_ModalButtons.Add(w.AddButton("btn", "", PixelButton.Gold, () => OnModalButton(k)));
                    m_ModalActions.Add(null);
                }
                var refs = m_ModalButtons[i];
                refs.SetLabel(buttons[i].Label);
                refs.SetStyle(ModalStyle(buttons[i]));
                refs.SetInteractable(true);
                refs.SetActive(true);
                m_ModalActions[i] = buttons[i].Action;
            }
            for (int i = buttons.Length; i < m_ModalButtons.Count; i++)
            {
                m_ModalButtons[i].SetActive(false);
                m_ModalActions[i] = null;
            }
            w.UpdateBodyArea();
            LayoutModal(r);
            w.Show();
        }

        void OnModalButton(int index)
        {
            // Действие берётся до закрытия: CloseModal может сразу открыть следующее окно очереди с другими действиями.
            var action = index < m_ModalActions.Count ? m_ModalActions[index] : null;
            CloseModal();
            action?.Invoke();
        }

        void CloseModal()
        {
            m_ModalOpen = false;
            m_ModalCurrent = null;
            m_ModalWin.Hide();
            if (m_ModalQueue.Count > 0) OpenModal(m_ModalQueue.Dequeue());
        }

        /// <summary>
        /// Высота рамки — по содержимому (иконка, текст, строки), не выше 86 % safe area; при нехватке места текст
        /// уменьшается. Элементы тела — сверху вниз.
        /// </summary>
        void LayoutModal(ModalRequest r)
        {
            bool hasButtons = r.Buttons != null && r.Buttons.Length > 0;
            m_ModalLayoutSize = m_Safe.rect.size;
            float safeW = m_Safe.rect.width, safeH = m_Safe.rect.height;
            if (safeW < 100 * Px) safeW = RefWidth;
            if (safeH < 100 * Px) safeH = RefHeight;
            float inner = safeW * 0.88f - 24 * Px;
            float chrome = WindowTop + (hasButtons ? WindowButtons : WindowBottom);

            float y = 0f;
            bool hasIcon = !string.IsNullOrEmpty(r.Icon);
            Ui.SetActive(m_ModalIcon, hasIcon);
            if (hasIcon)
            {
                PixelUi.SetIcon(m_ModalIcon, r.Icon, 2);
                float iconH = PixelUi.IconSize(r.Icon, 2).y;
                m_ModalIcon.rectTransform.anchoredPosition = CenterOffset(r.Icon, 2) + new Vector2(0f, -iconH * 0.5f);
                y = iconH + 4 * Px;
            }

            var text = m_ModalWin.BodyText;
            bool hasText = !string.IsNullOrEmpty(r.Body);
            Ui.SetActive(text, hasText);
            float textH = 0f;
            if (hasText)
            {
                text.enableAutoSizing = false;
                text.fontSize = PixelUi.FontBody;
                Ui.SetText(text, r.Body);
                textH = PixelUi.Snap(text.GetPreferredValues(r.Body, inner, 0f).y + Px);
            }

            int n = r.Rows != null ? r.Rows.Length : 0;
            float gap = hasText && n > 0 ? 4 * Px : 0f;
            float max = safeH * 0.86f;
            // Строки не помещаются (много наград, низкий экран): они сжимаются до 12 пикселей арта, а не вылезают за рамку.
            float rowH = RowHeight;
            if (n > 0)
            {
                float free = max - chrome - y - textH - gap - (n - 1) * RowGap;
                if (n * rowH > free) rowH = Mathf.Max(12 * Px, Mathf.Floor(free / n / Px) * Px);
            }
            float rowsH = n > 0 ? n * rowH + (n - 1) * RowGap : 0f;
            float h = chrome + y + textH + gap + rowsH;
            if (h > max)
            {
                // Не помещается: текст получает остаток и уменьшается автоподбором.
                h = max;
                textH = Mathf.Max(20 * Px, max - chrome - y - gap - rowsH);
                text.enableAutoSizing = true;
                text.fontSizeMin = PixelUi.FontSmall;
                text.fontSizeMax = PixelUi.FontBody;
            }
            h = Mathf.Max(h, chrome + 24 * Px);
            // Чётное число пикселей арта: центр рамки — на сетке.
            h = Mathf.Ceil(h / (2 * Px)) * 2 * Px;
            m_ModalWin.Box.Anchor(0.06f, 0.5f, 0.94f, 0.5f, new Vector2(0f, -h / 2), new Vector2(0f, h / 2));

            if (hasText)
            {
                text.rectTransform.Anchor(0, 1, 1, 1, new Vector2(0f, -(y + textH)), new Vector2(0f, -y));
                y += textH + gap;
            }
            m_ModalRows.Anchor(0, 1, 1, 1, new Vector2(0f, -(y + rowsH)), new Vector2(0f, -y));
            for (int i = 0; i < n; i++)
            {
                if (i == m_RowViews.Count) m_RowViews.Add(CreateRow(m_ModalRows));
                var view = m_RowViews[i];
                var row = r.Rows[i];
                float top = i * (rowH + RowGap);
                view.Panel.rectTransform.Anchor(0, 1, 1, 1, new Vector2(0f, -(top + rowH)), new Vector2(0f, -top));
                PixelUi.SetIcon(view.Icon, row.Icon ?? "icon_star");
                view.Icon.rectTransform.anchoredPosition = new Vector2(4 * Px + 8 * Px, 0f) + CenterOffset(row.Icon ?? "icon_star", 1);
                Ui.SetText(view.Text, row.Text);
                view.Text.color = row.Color;
                Ui.SetActive(view.Panel, true);
            }
            for (int i = n; i < m_RowViews.Count; i++) Ui.SetActive(m_RowViews[i].Panel, false);
        }

        static RowView CreateRow(RectTransform parent)
        {
            var view = new RowView { Panel = PixelUi.Panel(parent, "row", PixelPanel.Inset) };
            view.Panel.raycastTarget = false;
            view.Icon = PixelUi.Icon(view.Panel.transform, "icon", "icon_star");
            view.Icon.rectTransform.anchorMin = view.Icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            view.Text = PixelUi.Text(view.Panel.transform, "text", "", PixelUi.FontBody, PixelUi.TextColor, TextAlignmentOptions.Left, false, true);
            view.Text.rectTransform.Anchor(0, 0, 1, 1, new Vector2(22 * Px, 0f), new Vector2(-4 * Px, 0f));
            view.Text.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(view.Text, PixelUi.FontSmall * 0.8f, PixelUi.FontBody);
            return view;
        }

        /// <summary>Стиль кнопки окна: явный <see cref="ModalButton.Style"/> или перевод цвета старого API.</summary>
        static PixelButton ModalStyle(ModalButton b)
        {
            if (b.Style.HasValue) return b.Style.Value;
            var c = b.Color;
            // Цвет не задан — главная кнопка окна, как прежде (Palette.UiButton).
            if (c.a <= 0f) return PixelButton.Gold;
            if (SameColor(c, Palette.UiGreen)) return PixelButton.Green;
            if (SameColor(c, Palette.UiPanelLight) || SameColor(c, Palette.UiPanel)) return PixelButton.Dark;
            if (SameColor(c, Palette.Gold) || SameColor(c, Palette.UiButton)) return PixelButton.Gold;
            if (Ui.ButtonStyleFor(c) == PixelButton.Red) return PixelButton.Red;
            return PixelButton.Blue;
        }

        static bool SameColor(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

        /// <summary>
        /// Окно сундука (ТЗ 10.3): крупный сундук, строки наград с иконками (карточки, эликсир). «Забрать» закрывает окно
        /// и затем вызывает onClaim — контроллер убирает сундук из сохраняемой очереди только после просмотра.
        /// </summary>
        public void ShowChest(ChestResult chest, Action onClaim = null)
        {
            if (chest == null) return;
            var s = m_Game.Session;
            var rows = new List<ModalRow>(chest.Cards.Count + 1);
            foreach (var c in chest.Cards)
            {
                string icon = s.Balance.Cards.TryGetValue(c.CardId, out var dto) ? CardIcon(dto) : "icon_cards";
                rows.Add(new ModalRow { Icon = icon, Text = Loc.F("hud.chest.card", PresentationCatalog.CardName(s.Balance, c.CardId), c.Count), Color = PixelUi.TextColor });
            }
            if (chest.Elixir.IsPositive)
                rows.Add(new ModalRow { Icon = "icon_elixir", Text = Loc.F("hud.chest.elixir", NumberFormat.Format(chest.Elixir)), Color = PixelUi.TextElixir });
            Modal(new ModalRequest
            {
                Title = PresentationCatalog.GachaName(s.Balance, chest.GachaId),
                Body = rows.Count == 0 ? Loc.T("hud.chest.empty") : null,
                // Сундук своего уровня (деревянный, железный, … легендарный) — как камень-сундук на поле.
                Icon = "icon_chest_" + PresentationCatalog.GachaTier(s.Balance, chest.GachaId),
                Rows = rows.ToArray(),
                Buttons = new[] { new ModalButton { Label = Loc.T("hud.chest.claim"), Style = PixelButton.Gold, Action = onClaim } },
            });
        }

        /// <summary>Окно «Пока вас не было» (ТЗ 12.3): начисления, разрушенные объекты, ворота и выход, сундуки, выплаты — строками с иконками.</summary>
        public void ShowOffline(OfflineSummary sum)
        {
            var sb = m_Sb.Clear();
            sb.Append(Loc.F("hud.offline.elapsed", NumberFormat.FormatDuration(sum.Seconds)));
            if (sum.Capped) sb.Append(' ').Append(Loc.T("hud.offline.capped"));
            var rows = new List<ModalRow>(10);
            if (sum.Gold.IsPositive) AddRow(rows, "icon_coin", Loc.F("hud.offline.gold", NumberFormat.Format(sum.Gold)), PixelUi.TextAccentLight);
            if (sum.Elixir.IsPositive) AddRow(rows, "icon_elixir", Loc.F("hud.offline.elixir", NumberFormat.Format(sum.Elixir)), PixelUi.TextElixir);
            if (sum.RocksDestroyed > 0) AddRow(rows, "icon_pickaxe", Loc.F("hud.offline.rocks", sum.RocksDestroyed), PixelUi.TextColor);
            if (sum.ShaftsCleared > 0) AddRow(rows, "icon_shaft", Loc.F("hud.offline.shafts_cleared", sum.ShaftsCleared), PixelUi.TextColor);
            if (sum.GatesBroken > 1) AddRow(rows, "icon_gate", Loc.F("hud.offline.gates_broken_many", sum.GatesBroken), PixelUi.TextColor);
            else if (sum.GatesBroken == 1) AddRow(rows, "icon_gate", Loc.T("hud.offline.gates_broken_one"), PixelUi.TextColor);
            if (sum.ExitBroken) AddRow(rows, "icon_exit", Loc.T("hud.offline.exit_open"), PixelUi.TextColor);
            if (sum.Chests > 0) AddRow(rows, "icon_chest", Loc.F("hud.offline.chests", sum.Chests), PixelUi.TextColor);
            if (sum.PayoutsReady > 0) AddRow(rows, "icon_income", Loc.F("hud.offline.payouts", sum.PayoutsReady), PixelUi.TextColor);
            if (sum.ContainersSpawned > 0) AddRow(rows, "icon_cat", Loc.T("hud.offline.free_cat"), PixelUi.TextColor);
            Modal(new ModalRequest
            {
                Title = Loc.T("hud.offline.title"),
                Body = sb.ToString(),
                Rows = rows.ToArray(),
                Buttons = new[] { new ModalButton { Label = Loc.T("hud.offline.ok"), Style = PixelButton.Green } },
            });
        }

        static void AddRow(List<ModalRow> rows, string icon, string text, Color color) =>
            rows.Add(new ModalRow { Icon = icon, Text = text, Color = color });

        // ───────────── Настройки ─────────────

        void BuildSettings()
        {
            m_Settings = PixelUi.Window(m_Safe, "Settings", Loc.T("hud.settings.title"), HideSettings);
            // Звук (общий выключатель), громкость групп Master/Music/SFX/Ambience, вибрация, «меньше эффектов».
            const int rows = 7;
            float h = WindowTop + rows * SettingsRowHeight + (rows - 1) * RowGap + WindowBottom;
            h = Mathf.Ceil(h / (2 * Px)) * 2 * Px;
            m_Settings.Box.Anchor(0.06f, 0.5f, 0.94f, 0.5f, new Vector2(0f, -h / 2), new Vector2(0f, h / 2));
            Ui.SetActive(m_Settings.BodyText, false);
            m_SoundToggle = SettingsRow(0, "icon_sound_on", Loc.T("hud.settings.sound"), OnSoundToggle, out m_SoundIcon);
            m_Volume[(int)AudioGroup.Master] = VolumeRow(1, "icon_sound_on", Loc.T("hud.settings.master"), AudioGroup.Master);
            m_Volume[(int)AudioGroup.Music] = VolumeRow(2, "icon_music", Loc.T("hud.settings.music"), AudioGroup.Music);
            m_Volume[(int)AudioGroup.Sfx] = VolumeRow(3, "icon_effects", Loc.T("hud.settings.sfx"), AudioGroup.Sfx);
            m_Volume[(int)AudioGroup.Ambience] = VolumeRow(4, "icon_ambience", Loc.T("hud.settings.ambience"), AudioGroup.Ambience);
            m_VibroToggle = SettingsRow(5, "icon_vibro_on", Loc.T("hud.settings.vibration"), OnVibrationToggle, out m_VibroIcon);
            m_ReduceToggle = SettingsRow(6, "icon_effects", Loc.T("hud.settings.reduce_effects"), OnReduceEffectsToggle, out _);
        }

        /// <summary>Строка настроек: панель, иконка и подпись слева; элемент управления — справа (возвращается корень строки).</summary>
        Image SettingsRowBase(int index, string icon, string label, out Image iconImage, float rightReserve)
        {
            var row = PixelUi.Panel(m_Settings.Body, "row" + index, PixelPanel.Inset);
            float top = index * (SettingsRowHeight + RowGap);
            row.rectTransform.Anchor(0, 1, 1, 1, new Vector2(0f, -(top + SettingsRowHeight)), new Vector2(0f, -top));
            iconImage = PixelUi.Icon(row.transform, "icon", icon);
            iconImage.rectTransform.anchorMin = iconImage.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            iconImage.rectTransform.anchoredPosition = new Vector2(4 * Px + 8 * Px, 0f);
            var text = PixelUi.Text(row.transform, "label", label, PixelUi.FontLarge, PixelUi.TextColor, TextAlignmentOptions.Left, true, true);
            text.rectTransform.Anchor(0, 0, 1, 1, new Vector2(24 * Px, 0f), new Vector2(-rightReserve, 0f));
            text.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(text, PixelUi.FontSmall, PixelUi.FontLarge);
            return row;
        }

        PixelToggle SettingsRow(int index, string icon, string label, UnityAction<bool> onChanged, out Image iconImage)
        {
            var row = SettingsRowBase(index, icon, label, out iconImage, 48 * Px);
            var toggle = PixelUi.Toggle(row.transform, "toggle", true, onChanged, 2);
            toggle.Rect.anchorMin = toggle.Rect.anchorMax = new Vector2(1f, 0.5f);
            toggle.Rect.anchoredPosition = new Vector2(-(4 * Px + 20 * Px), 0f);
            return toggle;
        }

        /// <summary>Строка громкости: ползунок занимает правую половину строки.</summary>
        PixelSlider VolumeRow(int index, string icon, string label, AudioGroup group)
        {
            var row = SettingsRowBase(index, icon, label, out _, 0f);
            var label2 = row.transform.Find("label") as RectTransform;
            label2.anchorMax = new Vector2(0.48f, 1f);
            var slider = PixelUi.Slider(row.transform, "volume", AudioMix.Get(group), v => OnVolume(group, v));
            slider.Rect.Anchor(0.5f, 0f, 1f, 1f, new Vector2(0f, 2 * Px), new Vector2(-6 * Px, -2 * Px));
            return slider;
        }

        /// <summary>
        /// Окно настроек (ТЗ 11.3, 13.1): звук, громкость групп, вибрация и «меньше эффектов» — настройки игрока
        /// в сохранении слота, применяются сразу.
        /// </summary>
        public void ShowSettings()
        {
            if (CampaignEndVisible) return;
            m_SoundShown = m_VibroShown = m_ReduceShown = -1;
            for (int g = 0; g < m_Volume.Length; g++) m_Volume[g].SetValue(AudioMix.Get((AudioGroup)g));
            RefreshSettings(m_Game != null ? m_Game.Session : null);
            m_Settings.Show();
        }

        public void HideSettings() => m_Settings.Hide();

        void RefreshSettings(GameSession s)
        {
            int sound = m_Game != null && m_Game.SoundOn ? 1 : 0;
            if (sound != m_SoundShown)
            {
                m_SoundShown = sound;
                m_SoundToggle.SetOn(sound == 1);
                PixelUi.SetIcon(m_SoundIcon, sound == 1 ? "icon_sound_on" : "icon_sound_off");
            }
            int vibro = s == null || VibrationOn(s) ? 1 : 0;
            if (vibro != m_VibroShown)
            {
                m_VibroShown = vibro;
                m_VibroToggle.SetOn(vibro == 1);
                PixelUi.SetIcon(m_VibroIcon, vibro == 1 ? "icon_vibro_on" : "icon_vibro_off");
            }
            int reduce = VisualSettings.ReduceEffects ? 1 : 0;
            if (reduce != m_ReduceShown)
            {
                m_ReduceShown = reduce;
                m_ReduceToggle.SetOn(reduce == 1);
            }
        }

        void OnVolume(AudioGroup group, float value)
        {
            if (m_Game != null) m_Game.SetVolume(group, value);
        }

        void OnReduceEffectsToggle(bool on)
        {
            if (m_Game != null) m_Game.SetReduceEffects(on);
            RefreshSettings(m_Game != null ? m_Game.Session : null);
        }

        void OnSoundToggle(bool on)
        {
            if (m_Game == null) return;
            if (on != m_Game.SoundOn) m_Game.ToggleSound();
            RefreshSettings(m_Game.Session);
        }

        void OnVibrationToggle(bool on)
        {
            var s = m_Game != null ? m_Game.Session : null;
            if (s != null && on != VibrationOn(s)) ToggleVibration();
            RefreshSettings(s);
        }

        // ───────────── Экран завершения кампании ─────────────

        void BuildCampaignEnd()
        {
            m_End = PixelUi.Window(m_Safe, "CampaignEnd", "", null);
            // Во весь экран (ТЗ 11.4): поле под ним почти не видно. Затемнение ловит лучи — жест над экраном не трогает поле.
            m_End.Shade.color = new Color(PixelPalette.Ink0.r, PixelPalette.Ink0.g, PixelPalette.Ink0.b, 0.94f);
            var star = PixelUi.Icon(m_End.Body, "star", "icon_star", 3);
            float starH = PixelUi.IconSize("icon_star", 3).y;
            star.rectTransform.anchorMin = star.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            star.rectTransform.anchoredPosition = CenterOffset("icon_star", 3) + new Vector2(0f, -starH * 0.5f - Px);
            m_End.BodyText.fontSize = PixelUi.FontLarge;
            // Неоткрытые сундуки остаются доступны и на финальном экране.
            m_EndChests = m_End.AddButton("chests", "", PixelButton.Gold, OnChestsBadgeClick);
            m_EndChests.SetActive(false);
            m_End.UpdateBodyArea();
            LayoutCampaignEnd();
        }

        /// <summary>
        /// Рамка экрана конца кампании — по содержимому, как у модального окна: звезда, текст FontLarge и кнопка сундуков
        /// (если есть) одним блоком по центру safe area, без пустой полосы между текстом и кнопкой. Не выше 86 % safe area:
        /// не помещается — текст получает остаток и уменьшается автоподбором. Вызывается при показе, смене кнопки сундуков
        /// и размера safe area (не каждый кадр).
        /// </summary>
        void LayoutCampaignEnd()
        {
            m_EndLayoutSize = m_Safe.rect.size;
            float safeW = m_Safe.rect.width, safeH = m_Safe.rect.height;
            if (safeW < 100 * Px) safeW = RefWidth;
            if (safeH < 100 * Px) safeH = RefHeight;
            // Ширина тела: рамка 6–94 % safe area без полей окна (12 пикселей арта с каждой стороны).
            float inner = safeW * 0.88f - 24 * Px;
            float y = PixelUi.IconSize("icon_star", 3).y + 6 * Px;
            bool button = m_EndChests != null && m_EndChests.Rect.gameObject.activeSelf;
            float chrome = WindowTop + (button ? WindowButtons : WindowBottom);

            var text = m_End.BodyText;
            text.enableAutoSizing = false;
            text.fontSize = PixelUi.FontLarge;
            string body = text.text;
            float textH = string.IsNullOrEmpty(body) ? 0f : PixelUi.Snap(text.GetPreferredValues(body, inner, 0f).y + Px);
            float max = safeH * 0.86f;
            float h = chrome + y + textH;
            if (h > max)
            {
                h = max;
                textH = Mathf.Max(20 * Px, max - chrome - y);
                Ui.AutoSize(text, PixelUi.FontSmall, PixelUi.FontLarge);
            }
            h = Mathf.Max(h, chrome + y + 24 * Px);
            // Чётное число пикселей арта: центр рамки — на сетке.
            h = Mathf.Ceil(h / (2 * Px)) * 2 * Px;
            m_End.Box.Anchor(0.06f, 0.5f, 0.94f, 0.5f, new Vector2(0f, -h / 2), new Vector2(0f, h / 2));
            text.rectTransform.Anchor(0, 1, 1, 1, new Vector2(0f, -(y + textH)), new Vector2(0f, -y));
        }

        /// <summary>
        /// Экран завершения кампании (ТЗ 11.4): во весь экран, без кнопки закрытия, под модальными окнами (сундук награды
        /// выхода виден поверх). Пока он показан, контекстная панель, нижняя часть HUD и стрелки скрыты.
        /// </summary>
        public void ShowCampaignEnd(string title, string body)
        {
            m_End.SetTitle(title);
            m_End.SetBody(body);
            LayoutCampaignEnd();
            if (BlueprintsOpen) ToggleBlueprints();
            if (SettingsOpen) m_Settings.Hide();
            m_End.Show();
            m_ModalWin.Root.SetAsLastSibling();
            m_NextMineValid = false;
            m_ChestsValid = false;
            Refresh();
        }

        public void HideCampaignEnd()
        {
            if (!CampaignEndVisible) return;
            m_End.Hide();
            m_NextMineValid = false;
            m_ChestsValid = false;
            Refresh();
        }
    }
}

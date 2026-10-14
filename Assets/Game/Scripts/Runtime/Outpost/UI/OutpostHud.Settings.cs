using System;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using AudioGroup = CatMiners.Core.Sim.AudioGroup;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Настройки (ТЗ 14): громкость (общая), музыка, звуки, атмосфера, вибрация, «меньше эффектов» и подсказки обучения.
    /// Раскладка — копия окна настроек старого HUD (HudWindows.BuildSettings). Значения — из профиля нового режима
    /// (OutpostSettings), изменения — командами контракта (SetVolume/SetVibration/SetReduceEffects), применяются сразу.
    /// </summary>
    public sealed partial class OutpostHud
    {
        const float SettingsRowHeight = 26 * Px, SettingsRowGap = 2 * Px;
        const int SettingsRows = 9;

        PixelWindow m_Settings;
        WindowEntry m_SettingsEntry;
        PixelToggle m_VibroToggle, m_ReduceToggle, m_TutorialToggle;
        Image m_VibroIcon;
        GameObject m_TutorialRow;
        TextMeshProUGUI m_PlayTimeText;
        Ui.ButtonRefs m_SettingsReset;
        long m_PlayTimeMinutesShown = -1;
        int m_VibroShown = -1, m_ReduceShown = -1, m_SettingsTutorialShown = -1;
        // Ползунки громкости: Master, Music, Sfx, Ambience (порядок — AudioGroup).
        readonly PixelSlider[] m_Volume = new PixelSlider[4];

        /// <summary>Окно настроек открыто.</summary>
        public bool SettingsOpen => m_Settings != null && m_Settings.Visible;

        /// <summary>
        /// Крючок playable-сборки: «Reset progress» после подтверждения вызывает его вместо <see cref="IOutpostGame.ResetProgress"/>
        /// (новая игра без архивов restart-*). Поле экземпляра, по умолчанию null — поведение игры; основная игра его не задаёт.
        /// </summary>
        public Action ResetProgressOverride;

        void BuildSettings()
        {
            m_Settings = PixelUi.Window(m_Safe, "Settings", Loc.T("outpost.settings.title"), HideSettings);
            m_SettingsEntry = new WindowEntry { Root = m_Settings.Root, Back = HideSettings };
            int rows = SettingsRows + (OutpostController.AndroidRuntime ? 1 : 0);
            float h = WindowTop + rows * SettingsRowHeight + (rows - 1) * SettingsRowGap + WindowBottom;
            h = Mathf.Ceil(h / (2 * Px)) * 2 * Px;
            m_Settings.Box.Anchor(0.06f, 0.5f, 0.94f, 0.5f, new Vector2(0f, -h / 2), new Vector2(0f, h / 2));
            Ui.SetActive(m_Settings.BodyText, false);
            m_Volume[(int)AudioGroup.Master] = VolumeRow(0, "icon_sound_on", Loc.T("outpost.settings.master"), AudioGroup.Master);
            m_Volume[(int)AudioGroup.Music] = VolumeRow(1, "icon_music", Loc.T("outpost.settings.music"), AudioGroup.Music);
            m_Volume[(int)AudioGroup.Sfx] = VolumeRow(2, "icon_effects", Loc.T("outpost.settings.sfx"), AudioGroup.Sfx);
            m_Volume[(int)AudioGroup.Ambience] = VolumeRow(3, "icon_ambience", Loc.T("outpost.settings.ambience"), AudioGroup.Ambience);
            m_VibroToggle = SettingsRow(4, "icon_vibro_on", Loc.T("outpost.settings.vibration"), OnVibrationToggle, out m_VibroIcon);
            m_ReduceToggle = SettingsRow(5, "icon_effects", Loc.T("outpost.settings.reduce_effects"), OnReduceEffectsToggle, out _);
            m_TutorialToggle = SettingsRow(6, "icon_info", Loc.T("outpost.settings.tutorial"), OnTutorialToggle, out _);
            m_TutorialRow = m_TutorialToggle.Rect.parent.gameObject;
            var timeRow = SettingsRowBase(7, "icon_info", Loc.T("outpost.settings.play_time"), out _, 96 * Px);
            m_PlayTimeText = PixelUi.Text(timeRow.transform, "playTime", "", PixelUi.FontLarge, PixelUi.TextColor,
                TextAlignmentOptions.Right, true, true);
            m_PlayTimeText.rectTransform.Anchor(0.55f, 0, 1, 1, new Vector2(0, 0), new Vector2(-6 * Px, 0));
            m_PlayTimeText.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(m_PlayTimeText, PixelUi.FontSmall, PixelUi.FontLarge);
            m_SettingsReset = PixelUi.Button(m_Settings.Body, "ResetProgress", Loc.T("outpost.settings.reset"),
                PixelButton.Red, ShowSettingsResetConfirmation);
            if (OutpostController.AndroidRuntime)
            {
                var privacy = PixelUi.Button(m_Settings.Body, "PrivacyChoices", "Privacy choices", PixelButton.Dark,
                    () => CatMiners.Runtime.Integrations.NeutralAgeFlow.OpenChoices(gameObject));
                float privacyTop = 9 * (SettingsRowHeight + SettingsRowGap);
                privacy.Rect.Anchor(0, 1, 1, 1, new Vector2(0, -(privacyTop + SettingsRowHeight)), new Vector2(0, -privacyTop));
            }
            float resetTop = 8 * (SettingsRowHeight + SettingsRowGap);
            m_SettingsReset.Image.rectTransform.Anchor(0, 1, 1, 1,
                new Vector2(0, -(resetTop + SettingsRowHeight)), new Vector2(0, -resetTop));
        }

        Image SettingsRowBase(int index, string icon, string label, out Image iconImage, float rightReserve)
        {
            var row = PixelUi.Panel(m_Settings.Body, "row" + index, PixelPanel.Inset);
            float top = index * (SettingsRowHeight + SettingsRowGap);
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

        PixelSlider VolumeRow(int index, string icon, string label, AudioGroup group)
        {
            var row = SettingsRowBase(index, icon, label, out _, 0f);
            var labelRt = row.transform.Find("label") as RectTransform;
            labelRt.anchorMax = new Vector2(0.48f, 1f);
            var slider = PixelUi.Slider(row.transform, "volume", AudioMix.Get(group), v => OnVolume(group, v));
            slider.Rect.Anchor(0.5f, 0f, 1f, 1f, new Vector2(0f, 2 * Px), new Vector2(-6 * Px, -2 * Px));
            return slider;
        }

        OutpostSettings CurrentSettings => m_Game != null && m_Game.State != null && m_Game.State.Profile != null ? m_Game.State.Profile.Settings : null;

        static float SettingsVolume(OutpostSettings s, AudioGroup group)
        {
            if (s == null) return AudioMix.Get(group);
            switch (group)
            {
                case AudioGroup.Master: return s.Master;
                case AudioGroup.Music: return s.Music;
                case AudioGroup.Sfx: return s.Sfx;
                default: return s.Ambience;
            }
        }

        /// <summary>Окно настроек (шестерёнка): значения из профиля, применяются сразу.</summary>
        public void ShowSettings()
        {
            m_VibroShown = m_ReduceShown = m_SettingsTutorialShown = -1;
            var s = CurrentSettings;
            for (int g = 0; g < m_Volume.Length; g++) m_Volume[g].SetValue(SettingsVolume(s, (AudioGroup)g));
            RefreshSettings();
            m_Settings.Show();
            Opened(m_SettingsEntry);
        }

        public void HideSettings()
        {
            m_Settings.Hide();
            Closed(m_SettingsEntry);
        }

        void RefreshSettings()
        {
            long minutes = (m_Game?.State?.Profile.PlayTimeMs ?? 0) / 60_000;
            if (minutes != m_PlayTimeMinutesShown)
            {
                m_PlayTimeMinutesShown = minutes;
                Ui.SetText(m_PlayTimeText, Loc.F("outpost.settings.play_time_value", minutes / 60, minutes % 60));
            }
            m_SettingsReset.SetInteractable(m_Game != null && m_Game.CanResetProgress);
            var s = CurrentSettings;
            int vibro = s == null || s.Vibration ? 1 : 0;
            if (vibro != m_VibroShown)
            {
                m_VibroShown = vibro;
                m_VibroToggle.SetOn(vibro == 1);
                PixelUi.SetIcon(m_VibroIcon, vibro == 1 ? "icon_vibro_on" : "icon_vibro_off");
            }
            int reduce = s != null ? (s.ReduceEffects ? 1 : 0) : (VisualSettings.ReduceEffects ? 1 : 0);
            if (reduce != m_ReduceShown)
            {
                m_ReduceShown = reduce;
                m_ReduceToggle.SetOn(reduce == 1);
            }
            int tutorial = m_Tutorial == null ? -2 : (m_Tutorial.Enabled ? 1 : 0);
            if (tutorial != m_SettingsTutorialShown)
            {
                m_SettingsTutorialShown = tutorial;
                if (m_TutorialRow.activeSelf != (tutorial != -2)) m_TutorialRow.SetActive(tutorial != -2);
                if (tutorial != -2) m_TutorialToggle.SetOn(tutorial == 1);
            }
        }

        void ShowSettingsResetConfirmation()
        {
            if (m_Game == null || !m_Game.CanResetProgress) return;
            bool debug = m_Game.IsDebugProfile;
            Modal(Loc.T("outpost.settings.reset_title"), Loc.T("outpost.settings.reset_body"),
                new ModalButton { Label = Loc.T("outpost.dev.reset_cancel"), Style = PixelButton.Blue, Cancel = true },
                new ModalButton
                {
                    Label = Loc.T("outpost.dev.reset_confirm"), Style = PixelButton.Red,
                    Action = () =>
                    {
                        if (debug != m_Game.IsDebugProfile || !m_Game.CanResetProgress) return;
                        if (ResetProgressOverride != null) ResetProgressOverride();
                        else m_Game.ResetProgress();
                    }
                });
        }

        void OnVolume(AudioGroup group, float value)
        {
            if (m_Game != null) m_Game.SetVolume(group, value);
        }

        void OnVibrationToggle(bool on)
        {
            if (m_Game != null) m_Game.SetVibration(on);
            m_VibroShown = -1;
            RefreshSettings();
        }

        void OnReduceEffectsToggle(bool on)
        {
            if (m_Game != null) m_Game.SetReduceEffects(on);
            m_ReduceShown = -1;
            RefreshSettings();
        }

        void OnTutorialToggle(bool on)
        {
            m_Tutorial?.SetEnabled(on);
            m_SettingsTutorialShown = -1;
            RefreshSettings();
        }
    }
}

using System.Collections.Generic;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CatMiners.Playable
{
    /// <summary>
    /// Короткие надписи-«вау» playable поверх поля (ТЗ playable §6): «NEW CAT!» — когда нанятый котик выходит из лагеря
    /// (Board.Cue WorkerSpawned), «GATE OPEN!» — ворота открылись (SectorOpened), «MINE CLEARED!» — шахта пройдена
    /// (StageCompleted), «PICKAXES ×2!» — куплен уровень вехи «Кирок». Без таблички: крупный текст FontTitle с тенью на тёмной
    /// полосе (затемнение кита), иконка ×2 на тёплом свечении fx_glow_warm, «поп» при появлении; на 72 % высоты safe area,
    /// лучи не ловит, игру не останавливает; видна <see cref="ShowSeconds"/> по часам контроллера, следующие ждут в очереди,
    /// одинаковые подряд складываются («3 NEW CATS!»). Только на открытом поле: пока открыто окно HUD или окно награды квеста,
    /// очередь ждёт, а показанная надпись снимается (поверх окон не рисуется). «MINE CLEARED!» снимается, когда открылись
    /// итоги шахты (они и отмечают этот момент), до них — ждёт. <see cref="Suppressed"/> (финальный экран) прячет надпись и
    /// очередь.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayableJuice : MonoBehaviour
    {
        public const double ShowSeconds = 1.3;
        const float Px = PixelUi.Px;
        const int IconScale = 2, GlowScale = 2;
        const int QueueLimit = 4;

        enum Kind
        {
            Cat,
            Gate,
            Cleared,
            Power,
        }

        struct Item
        {
            public Kind Kind;
            public int Count;
            public string Text;
            /// <summary>Шахта события (для «MINE CLEARED!»: в другой шахте надпись устарела).</summary>
            public string RunId;
        }

        OutpostController m_Game;
        RectTransform m_Root;
        Image m_Back, m_Glow, m_Icon;
        TextMeshProUGUI m_Text;
        PlayableQuests m_Quests;
        PixelPop m_Pop;
        readonly List<Item> m_Queue = new List<Item>(QueueLimit);
        Item m_Current;
        bool m_Showing;
        double m_Until;
        string m_RunId;
        int m_PickaxesSeen = -1;

        /// <summary>Показанная надпись или null.</summary>
        public string Text => m_Showing ? m_Text.text : null;
        public bool Visible => m_Showing;
        public RectTransform Root => m_Root;

        /// <summary>Финальный экран открыт: надпись скрыта, очередь сброшена, новые не показываются.</summary>
        public bool Suppressed
        {
            get => m_Suppressed;
            set
            {
                m_Suppressed = value;
                if (!value || m_Root == null) return;
                m_Queue.Clear();
                m_Showing = false;
                m_Root.gameObject.SetActive(false);
            }
        }

        bool m_Suppressed;

        void Awake() => m_Game = GetComponent<OutpostController>();

        void Start()
        {
            if (m_Game == null || m_Game.Hud == null) return;
            var safe = (RectTransform)m_Game.Hud.transform.Find("SafeArea");
            m_Root = Ui.Rect(safe, "PlayableJuice");
            m_Root.anchorMin = m_Root.anchorMax = new Vector2(0.5f, 0.72f);
            m_Root.pivot = new Vector2(0.5f, 0.5f);
            var group = m_Root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            // Тёмная полоса за текстом (не табличка тоста) — первой, позади свечения, иконки и текста.
            m_Back = PixelUi.Shade(m_Root, "back");
            m_Back.raycastTarget = false;
            var back = m_Back.rectTransform;
            back.anchorMin = back.anchorMax = new Vector2(0f, 0.5f);
            back.pivot = new Vector2(0f, 0.5f);
            // Свечение — по центру иконки (не ряда): светлая середина не ложится под жёлтый текст.
            m_Glow = PixelUi.Icon(m_Root, "glow", "fx_glow_warm", GlowScale);
            m_Glow.rectTransform.anchorMin = m_Glow.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            m_Icon = PixelUi.Icon(m_Root, "icon", "icon_star", IconScale);
            m_Icon.rectTransform.anchorMin = m_Icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            m_Text = PixelUi.Text(m_Root, "text", "", PixelUi.FontTitle, PixelUi.TextAccentLight, TextAlignmentOptions.Left, true, true);
            m_Text.textWrappingMode = TextWrappingModes.NoWrap;
            m_Text.overflowMode = TextOverflowModes.Overflow;
            m_Pop = m_Root.gameObject.AddComponent<PixelPop>();
            m_Pop.From = 0.6f;
            m_Pop.Duration = 0.2f;
            m_Root.gameObject.SetActive(false);
            if (m_Game.Board != null) m_Game.Board.Cue += OnCue;
            m_Game.EventPresented += OnEvent;
        }

        void OnDestroy()
        {
            if (m_Game == null) return;
            if (m_Game.Board != null) m_Game.Board.Cue -= OnCue;
            m_Game.EventPresented -= OnEvent;
        }

        void OnCue(OutpostViewCueArgs cue)
        {
            if (cue.Cue == OutpostViewCue.WorkerSpawned) Push(Kind.Cat);
            else if (cue.Cue == OutpostViewCue.SectorOpened) Push(Kind.Gate);
        }

        /// <summary>Окно HUD или окно награды квеста открыто: надписи ждут открытого поля.</summary>
        bool WindowOpen()
        {
            if (m_Game.Hud != null && m_Game.Hud.AnyWindowOpen) return true;
            if (m_Quests == null) m_Quests = GetComponent<PlayableQuests>();
            return m_Quests != null && m_Quests.PopupOpen;
        }

        void OnEvent(OutpostEvent e)
        {
            var sim = m_Game.Sim;
            if (sim == null) return;
            SyncRun(sim.Run);
            if (e.Type == OutpostEventType.StageCompleted) Push(Kind.Cleared);
            else if (e.Type == OutpostEventType.TeamUpgradePurchased && e.Branch == (int)UpgradeBranch.Pickaxes) CheckMilestone(sim.Run);
        }

        /// <summary>Новая шахта: уровень «Кирок» отсчитывается заново (покупки прежней шахты не дают надписей).</summary>
        void SyncRun(OutpostRun run)
        {
            if (run.RunId == m_RunId) return;
            m_RunId = run.RunId;
            m_PickaxesSeen = run.Levels[(int)UpgradeBranch.Pickaxes];
        }

        /// <summary>Покупка перешла порог вехи «Кирок» (профиль p3 — уровни 5 и 10): «PICKAXES ×N!» с множителем вехи.</summary>
        void CheckMilestone(OutpostRun run)
        {
            int now = run.Levels[(int)UpgradeBranch.Pickaxes], before = m_PickaxesSeen;
            m_PickaxesSeen = now;
            var milestones = run.Balance?.Branch(UpgradeBranch.Pickaxes).Milestones;
            if (milestones == null) return;
            foreach (int m in milestones)
                if (before < m && m <= now)
                {
                    Push(Kind.Power, Loc.F("playable.juice.power", OutpostUiText.Num(run.Balance.MilestoneMultiplier)));
                    return;
                }
        }

        void Push(Kind kind, string text = null)
        {
            if (m_Root == null || m_Suppressed) return;
            // Одинаковые подряд (×10 найма, двое ворот) складываются в показанную или последнюю в очереди.
            if (kind == Kind.Cat)
            {
                if (m_Showing && m_Current.Kind == Kind.Cat)
                {
                    m_Current.Count++;
                    Present(m_Current, false);
                    return;
                }
                for (int i = m_Queue.Count - 1; i >= 0; i--)
                    if (m_Queue[i].Kind == Kind.Cat)
                    {
                        var item = m_Queue[i];
                        item.Count++;
                        m_Queue[i] = item;
                        return;
                    }
            }
            if (m_Queue.Count >= QueueLimit) m_Queue.RemoveAt(0);
            m_Queue.Add(new Item { Kind = kind, Count = 1, Text = text, RunId = m_Game.Sim != null ? m_Game.Sim.Run.RunId : null });
        }

        void Update()
        {
            if (m_Root == null || m_Game == null || m_Game.Clock == null || m_Game.Hud == null) return;
            var run = m_Game.Sim != null ? m_Game.Sim.Run : null;
            if (run != null) SyncRun(run);
            double now = m_Game.Clock.MonotonicSeconds;
            bool window = WindowOpen();
            // Время вышло или открылось окно: надпись не рисуется поверх окон и под их затемнением.
            if (m_Showing && (now >= m_Until || window))
            {
                m_Showing = false;
                m_Root.gameObject.SetActive(false);
            }
            // «MINE CLEARED!» — итоги шахты уже на экране или шахта сменилась: момент отмечен окном итогов.
            for (int i = m_Queue.Count - 1; i >= 0; i--)
                if (m_Queue[i].Kind == Kind.Cleared && (m_Game.Hud.ResultsOpen || run == null || run.RunId != m_Queue[i].RunId))
                    m_Queue.RemoveAt(i);
            if (m_Showing || m_Queue.Count == 0 || window) return;
            // Шахта пройдена, итоги вот-вот откроются (HUD проверяет окна 10 раз в секунду): «MINE CLEARED!» ждёт их.
            if (m_Queue[0].Kind == Kind.Cleared && run.Phase == RunPhase.ReadyToContinue) return;
            m_Current = m_Queue[0];
            m_Queue.RemoveAt(0);
            Present(m_Current, true);
            m_Showing = true;
            m_Until = now + ShowSeconds;
        }

        void Present(Item item, bool fresh)
        {
            string icon;
            string text;
            switch (item.Kind)
            {
                case Kind.Cat:
                    icon = "icon_cat";
                    text = item.Count > 1 ? Loc.F("playable.juice.new_cats", item.Count) : Loc.T("playable.juice.new_cat");
                    break;
                case Kind.Gate:
                    icon = "icon_gate";
                    text = Loc.T("playable.juice.gate");
                    break;
                case Kind.Cleared:
                    icon = "icon_star";
                    text = Loc.T("playable.juice.cleared");
                    break;
                default:
                    icon = "icon_branch_pickaxes";
                    text = item.Text ?? Loc.F("playable.juice.power", 2);
                    break;
            }
            if (!PixelArt.Has(icon)) icon = "icon_star";
            PixelUi.SetIcon(m_Icon, icon, IconScale);
            Ui.SetText(m_Text, text);
            // Ряд по тексту: иконка ×2, зазор 3 пикселя арта, текст; свечение — по центру иконки, позади; под текстом тёмная
            // полоса с полями 3 пикселя арта по бокам и 2 сверху и снизу.
            var iconSize = PixelUi.IconSize(icon, IconScale);
            var preferred = m_Text.GetPreferredValues(text);
            float textW = Mathf.Ceil((preferred.x + 0.5f) / Px) * Px;
            float gap = 3 * Px;
            m_Root.sizeDelta = new Vector2(iconSize.x + gap + textW, iconSize.y);
            var iconCentre = new Vector2(iconSize.x * 0.5f, 0f);
            m_Icon.rectTransform.anchoredPosition = iconCentre + PlayableBanner.CenterOffset(icon, IconScale);
            m_Glow.rectTransform.anchoredPosition = iconCentre + PlayableBanner.CenterOffset("fx_glow_warm", GlowScale);
            m_Text.rectTransform.Anchor(0, 0, 1, 1, new Vector2(iconSize.x + gap, Px), Vector2.zero);
            float backH = Mathf.Min(iconSize.y, Mathf.Ceil(preferred.y / (2 * Px)) * 2 * Px) + 4 * Px;
            m_Back.rectTransform.sizeDelta = new Vector2(textW + 6 * Px, backH);
            m_Back.rectTransform.anchoredPosition = new Vector2(iconSize.x + gap - 3 * Px, 0f);
            if (fresh) m_Root.gameObject.SetActive(true);
            m_Root.SetAsLastSibling();
            m_Pop.Play();
        }
    }
}

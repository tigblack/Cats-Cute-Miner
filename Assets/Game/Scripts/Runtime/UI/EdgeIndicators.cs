using System;
using System.Collections.Generic;
using CatMiners.Core.Grid;
using CatMiners.Core.Sim;
using UnityEngine;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Стрелки у краёв поля (ТЗ 13.1): ведут к объектам вне кадра, которые ждут действия, — шахта в ReadyToActivate,
    /// ворота в Broken, готовая ручная выплата, контейнер бесплатного котика или ящик доставки, разрушенный выход.
    /// Объекты закрытых секторов не учитываются.
    /// Видимая полоса поля — доли safe area между панелями HUD (TopInset/BottomInset); при открытой контекстной панели
    /// нижний край поднимается до её верха. Цель считается в кадре, если в полосе видна хотя бы часть её клеток.
    /// Стрелка появляется, только если тап по ней покажет цель: прокрутка камеры ограничена, и без этой проверки цель
    /// у края карты (выход у верхнего упора на высоких экранах, контейнер на площадке под контекстной панелью у нижнего)
    /// держала бы стрелку, тап по которой ничего не меняет. Пересчёт раз в 0,2 с реального времени, стрелки переиспользуются.
    /// Ближайшие цели идут первыми; стрелки, которые перекрылись бы, объединяются в одну со счётчиком.
    /// Тап по стрелке переводит камеру к ближайшей цели. Пока открыто окно (модальное, «Чертежи», настройки), экран конца кампании или идёт
    /// перенос котика или динамита, стрелки скрыты: сброс у края поля не должен попадать в UI.
    /// Вид (раздел 6 руководства по стилю): ячейка-кнопка кита (ui_slot) с иконкой вида цели и пиксельная стрелка icon_arrow_*
    /// со стороны края поля, покачивающаяся на пиксель арта к цели. Спрайты не вращаются: направление — один из шести
    /// спрайтов (вверх/вниз и диагонали), без диагональных — прямая стрелка; без icon_arrow_down — отражённая icon_arrow_up.
    /// </summary>
    public sealed class EdgeIndicators : MonoBehaviour
    {
        /// <summary>Период пересчёта, секунды реального времени.</summary>
        public const float UpdateInterval = 0.2f;
        const int MaxArrows = 12;
        const float Px = PixelArt.UiPixel;
        /// <summary>Ячейка 24×24 пикселя арта; стрелка 16×16 выступает за неё к краю поля на 12 пикселей (4 перекрывают ячейку).</summary>
        const float ChipSize = 24 * Px, ArrowExtent = 12 * Px;
        const float ArrowWidth = ChipSize, ArrowHeight = ChipSize + ArrowExtent, EdgeMargin = 2 * Px, Gap = 2 * Px;
        /// <summary>Центр стрелки от центра ячейки: 16-пиксельная иконка стоит вплотную к внешнему краю выступа.</summary>
        const float ArrowOffset = ChipSize / 2f + ArrowExtent - 8 * Px;
        /// <summary>Период покачивания стрелки, секунды реального времени.</summary>
        const float BobPeriod = 0.3f;
        /// <summary>Сколько пикселей клеток цели должно попасть в полосу поля, чтобы цель считалась видимой.</summary>
        const float MinSeenPixels = 2f;

        // Виды целей: порядок совпадает с иконками ниже.
        const int KindShaft = 0, KindGate = 1, KindPayout = 2, KindExit = 3, KindFreeCat = 4, KindDelivery = 5;
        static readonly string[] s_KindIcons = { "icon_shaft", "icon_gate", "icon_coin", "icon_exit", "icon_cat", "icon_chest" };

        // Направления стрелки: 0–2 — вверх (прямо, влево, вправо), 3–5 — вниз.
        const int DirUp = 0, DirDown = 3;
        static readonly string[] s_ArrowSprites =
        {
            "icon_arrow_up", "icon_arrow_up_left", "icon_arrow_up_right", "icon_arrow_down", "icon_arrow_down_left", "icon_arrow_down_right",
        };

        struct Candidate
        {
            public int Kind;
            public float WorldZ;
            public Vector2 Screen;
            public bool Top;
            public float Distance;
        }

        sealed class Arrow
        {
            public Ui.ButtonRefs Chip;
            public RectTransform Rect;
            public UnityEngine.UI.Image Pointer;
            public float TargetZ;
            public float LocalX;
            public bool Top;
            public int Count;
            public int ShownKind = -1;
            public int ShownSide = -1;
            public int ShownDir = -1;
            public int ShownBob = -1;
        }

        GameController m_Game;
        Hud m_Hud;
        RectTransform m_Rect;
        readonly List<Arrow> m_Arrows = new List<Arrow>(MaxArrows);
        Candidate[] m_Candidates = new Candidate[32];
        int m_CandidateCount;
        int m_Active;
        float m_Next;
        bool m_Hidden = true;
        int m_Bob;

        // Полоса поля (пиксели экрана) и прокрутка камеры на время пересчёта.
        float m_BandBottom, m_BandTop;
        float m_FocusZ, m_MinZ, m_MaxZ, m_PixelsPerUnit;

        /// <summary>Сколько стрелок сейчас показано.</summary>
        public int ActiveArrowCount => m_Active;

        internal void Init(GameController game, Hud hud)
        {
            m_Game = game;
            m_Hud = hud;
            m_Rect = (RectTransform)transform;
        }

        /// <summary>Нажать стрелку с номером index (0 — ближайшая цель), как при касании.</summary>
        public void ClickArrow(int index)
        {
            if (index < 0 || index >= m_Active) throw new ArgumentOutOfRangeException(nameof(index), "нет активной стрелки " + index);
            OnArrowClick(m_Arrows[index]);
        }

        /// <summary>Пересчитать стрелки в ближайшем кадре (например, после перевода камеры).</summary>
        public void RequestRefresh() => m_Next = 0f;

        /// <summary>
        /// Пределы прокрутки камеры (FocusZ): упоры CameraRig.MinZ/MaxZ для поля, которое камера показывает.
        /// <paramref name="boardDepth"/> оставлен для совместимости; EdgeIndicatorTests сверяет результат с настоящими упорами камеры.
        /// </summary>
        public static void CameraScrollRange(CameraRig rig, float boardDepth, out float minZ, out float maxZ)
        {
            minZ = rig.MinZ;
            maxZ = rig.MaxZ;
        }

        void Update()
        {
            if (m_Rect == null) return;
            if (ShouldHide())
            {
                if (!m_Hidden || m_Active > 0) HideAll();
                m_Hidden = true;
                return;
            }
            if (m_Hidden)
            {
                m_Hidden = false;
                m_Next = 0f;
            }
            Bob();
            if (Time.unscaledTime < m_Next) return;
            m_Next = Time.unscaledTime + UpdateInterval;
            Rebuild();
        }

        /// <summary>Стрелки покачиваются к цели на пиксель арта: смена фазы раз в <see cref="BobPeriod"/>.</summary>
        void Bob()
        {
            int bob = (int)(Time.unscaledTime / BobPeriod) & 1;
            if (bob == m_Bob) return;
            m_Bob = bob;
            for (int i = 0; i < m_Active; i++) ApplyBob(m_Arrows[i]);
        }

        bool ShouldHide()
        {
            if (m_Game == null || m_Hud == null || m_Game.Session == null || m_Game.Board == null || m_Game.Rig == null || m_Game.Rig.Cam == null) return true;
            if (m_Hud.AnyWindowOpen || m_Hud.InTransition || m_Game.InTransition) return true;
            var input = m_Game.Input;
            return input != null && (input.IsDraggingCat || input.IsDraggingDynamite);
        }

        void HideAll()
        {
            for (int i = 0; i < m_Arrows.Count; i++) Ui.SetActive(m_Arrows[i].Rect, false);
            m_Active = 0;
        }

        // ───────────── Пересчёт ─────────────

        void Rebuild()
        {
            var s = m_Game.Session;
            var cam = m_Game.Rig.Cam;
            var safe = Screen.safeArea;
            if (safe.height <= 0f || safe.width <= 0f)
            {
                HideAll();
                return;
            }
            // Полоса поля в пикселях экрана: доли safe area, как в CameraRig и PointerInput (общий расчёт — в Hud).
            m_Hud.FieldBandPixels(out float bandBottom, out float bandTop);
            if (m_Hud.TryGetContextTop(out float ctxTop)) bandBottom = Mathf.Max(bandBottom, ctxTop);
            // Верхние стрелки не наезжают на кнопку перехода и причину под верхней панелью.
            float placeTop = bandTop;
            if (m_Hud.TryGetTopObstacleBottom(out float obstacle)) placeTop = Mathf.Min(placeTop, obstacle);

            m_BandBottom = bandBottom;
            m_BandTop = bandTop;
            // Куда камера может уйти по тапу: FocusOn упирается в пределы прокрутки. Текущая позиция всегда допустима.
            var rig = m_Game.Rig;
            CameraScrollRange(rig, s.Layout.Depth, out float minZ, out float maxZ);
            m_FocusZ = rig.FocusZ;
            m_MinZ = Mathf.Min(minZ, m_FocusZ);
            m_MaxZ = Mathf.Max(maxZ, m_FocusZ);
            // Пикселей экрана на единицу поля по Z: камера ортографическая, величина одна для всего поля.
            m_PixelsPerUnit = Mathf.Max(0f, cam.WorldToScreenPoint(Vector3.forward).y - cam.WorldToScreenPoint(Vector3.zero).y);

            CollectCandidates(s, cam);
            SortCandidates();

            var uiCam = m_Hud.UiCamera;
            ScreenToLocal(new Vector2(safe.center.x, placeTop), uiCam, out var localTop);
            ScreenToLocal(new Vector2(safe.center.x, bandBottom), uiCam, out var localBottom);
            var rect = m_Rect.rect;
            float minX = rect.xMin + ArrowWidth / 2f + EdgeMargin;
            float maxX = rect.xMax - ArrowWidth / 2f - EdgeMargin;
            if (minX > maxX) minX = maxX = rect.center.x;

            int active = 0;
            for (int i = 0; i < m_CandidateCount; i++)
            {
                var c = m_Candidates[i];
                ScreenToLocal(new Vector2(c.Screen.x, c.Top ? placeTop : bandBottom), uiCam, out var local);
                float x = Mathf.Clamp(local.x, minX, maxX);

                // Стрелка на той же стороне, с которой эта перекрылась бы: цель добавляется в её счётчик.
                Arrow merged = null;
                float bestDx = float.MaxValue;
                for (int j = 0; j < active; j++)
                {
                    var a = m_Arrows[j];
                    if (a.Top != c.Top) continue;
                    float dx = Mathf.Abs(a.LocalX - x);
                    if (dx < ArrowWidth + Gap && dx < bestDx)
                    {
                        merged = a;
                        bestDx = dx;
                    }
                }
                if (merged == null && active >= MaxArrows)
                {
                    // Пул исчерпан: цель уходит в ближайшую по горизонтали стрелку той же стороны.
                    for (int j = 0; j < active; j++)
                    {
                        var a = m_Arrows[j];
                        if (a.Top != c.Top) continue;
                        float dx = Mathf.Abs(a.LocalX - x);
                        if (dx < bestDx)
                        {
                            merged = a;
                            bestDx = dx;
                        }
                    }
                    if (merged == null) continue;
                }
                if (merged != null)
                {
                    merged.Count++;
                    continue;
                }

                var arrow = GetArrow(active++);
                arrow.Top = c.Top;
                arrow.LocalX = x;
                arrow.TargetZ = c.WorldZ;
                arrow.Count = 1;
                // Позиция — центр ячейки; стрелка выступает из неё к краю поля. Шаг — пиксель арта.
                float y = c.Top ? localTop.y - EdgeMargin - ArrowExtent - ChipSize / 2f : localBottom.y + EdgeMargin + ArrowExtent + ChipSize / 2f;
                arrow.Rect.anchoredPosition = PixelUi.Snap(new Vector2(x, y));
                ApplySide(arrow, c.Top);
                ApplyKind(arrow, c.Kind);
                // Остриё смотрит на цель: от точки стрелки на экране к проекции объекта; спрайт — ближайшее из шести направлений.
                float arrowScreenX = Mathf.Clamp(c.Screen.x, safe.xMin, safe.xMax);
                var dir = c.Screen - new Vector2(arrowScreenX, c.Top ? placeTop : bandBottom);
                ApplyDirection(arrow, ArrowDirection(dir, c.Top));
                Ui.SetActive(arrow.Rect, true);
            }

            for (int j = 0; j < active; j++) ApplyCount(m_Arrows[j]);
            for (int j = active; j < m_Arrows.Count; j++) Ui.SetActive(m_Arrows[j].Rect, false);
            m_Active = active;
        }

        void CollectCandidates(GameSession s, Camera cam)
        {
            m_CandidateCount = 0;
            var layout = s.Layout;
            var objects = layout.Objects;
            for (int ord = 0; ord < objects.Count; ord++)
            {
                var obj = objects[ord];
                var st = s.Mine.Objects[ord];
                int kind = -1;
                switch (obj.Kind)
                {
                    case ObjectKind.Shaft when st.Status == ObjectStatus.ReadyToActivate:
                        kind = KindShaft;
                        break;
                    case ObjectKind.Gate when st.Status == ObjectStatus.Broken:
                        kind = KindGate;
                        break;
                    case ObjectKind.Exit when st.Status == ObjectStatus.Broken:
                        kind = KindExit;
                        break;
                }
                if (kind < 0 && st.Gen != null && st.Status == ObjectStatus.Producing && st.Gen.PayoutReady && !st.Gen.Automated) kind = KindPayout;
                if (kind < 0 || !InOpenSector(s, obj)) continue;
                AddCandidate(kind, m_Game.Board.ObjectCenter(ord), obj.Depth / 2f, cam);
            }
            var boxes = s.Mine.Containers;
            for (int i = 0; i < boxes.Count; i++)
            {
                var box = boxes[i];
                if (box.Cell < 0 || box.Cell >= layout.CellCount || !s.IsCellOpen(box.Cell)) continue;
                // Бочка найма — та же стрелка, что у бочки бесплатного котика.
                AddCandidate(box.Kind == ContainerKind.Delivery ? KindDelivery : KindFreeCat, m_Game.Board.CellCenter(box.Cell), 0.5f, cam);
            }
        }

        /// <summary>Сектор объекта открыт; у ворот — сектор под ними, как в GameSession.IsTargetAvailable.</summary>
        static bool InOpenSector(GameSession s, ZoneObject obj)
        {
            if (obj.Cells == null || obj.Cells.Length == 0) return false;
            int sector = obj.Kind == ObjectKind.Gate ? s.Layout.SectorOfRow(obj.Row + 1) : s.Layout.CellSector[obj.Cells[0]];
            return s.IsSectorOpen(sector);
        }

        /// <summary>
        /// Цель с центром <paramref name="world"/> и клетками на ±<paramref name="halfDepth"/> по Z. Стрелка нужна, если цель
        /// не видна, а тап по стрелке (FocusOn к z цели в пределах прокрутки) её покажет. Цель, которую камера не может
        /// вывести в полосу, стрелку не получает: такая стрелка не исчезала бы, а тап по ней ничего не менял.
        /// </summary>
        void AddCandidate(int kind, Vector3 world, float halfDepth, Camera cam)
        {
            var p = cam.WorldToScreenPoint(world);
            float half = halfDepth * m_PixelsPerUnit;
            if (InBand(p.y, half)) return;
            // Сдвиг цели на экране после перевода камеры: камера встанет на z цели, но не дальше упора.
            float shift = (Mathf.Clamp(world.z, m_MinZ, m_MaxZ) - m_FocusZ) * m_PixelsPerUnit;
            if (!InBand(p.y - shift, half)) return;
            bool top = p.y > m_BandTop;
            if (m_CandidateCount == m_Candidates.Length) Array.Resize(ref m_Candidates, m_Candidates.Length * 2);
            m_Candidates[m_CandidateCount++] = new Candidate
            {
                Kind = kind,
                WorldZ = world.z,
                Screen = new Vector2(p.x, p.y),
                Top = top,
                Distance = top ? p.y - m_BandTop : m_BandBottom - p.y,
            };
        }

        /// <summary>
        /// Цель видна в полосе поля: её центр на экране <paramref name="y"/> внутри полосы или в полосу попадает
        /// хотя бы <see cref="MinSeenPixels"/> пикселей её клеток (отрезок y ± <paramref name="half"/>).
        /// </summary>
        bool InBand(float y, float half)
        {
            if (y >= m_BandBottom && y <= m_BandTop) return true;
            return Mathf.Min(y + half, m_BandTop) - Mathf.Max(y - half, m_BandBottom) >= MinSeenPixels;
        }

        /// <summary>Ближайшие к краю поля — первыми (вставками: целей мало, без выделений памяти).</summary>
        void SortCandidates()
        {
            for (int i = 1; i < m_CandidateCount; i++)
            {
                var c = m_Candidates[i];
                int j = i - 1;
                while (j >= 0 && m_Candidates[j].Distance > c.Distance)
                {
                    m_Candidates[j + 1] = m_Candidates[j];
                    j--;
                }
                m_Candidates[j + 1] = c;
            }
        }

        void ScreenToLocal(Vector2 screen, Camera uiCam, out Vector2 local)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Rect, screen, uiCam, out local)) local = Vector2.zero;
        }

        void OnArrowClick(Arrow arrow)
        {
            if (m_Game == null || m_Game.Rig == null) return;
            m_Game.Rig.FocusOn(arrow.TargetZ);
            m_Next = 0f;
        }

        // ───────────── Вид стрелки ─────────────

        Arrow GetArrow(int index)
        {
            while (m_Arrows.Count <= index) m_Arrows.Add(CreateArrow(m_Arrows.Count));
            return m_Arrows[index];
        }

        Arrow CreateArrow(int index)
        {
            var arrow = new Arrow();
            // Ячейка-кнопка кита: нажатие, звук клика и бейдж-счётчик — из PixelUi.
            var chip = PixelUi.IconButton(m_Rect, "Arrow" + index, s_KindIcons[0], () => OnArrowClick(arrow));
            var rt = chip.Rect;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(ChipSize, ChipSize);
            chip.Icon.rectTransform.anchoredPosition = new Vector2(0f, Px);
            Ui.SetActive(chip.Label, false);
            // Стрелка — ребёнок ячейки вне её прямоугольника: тап по стрелке тоже нажимает ячейку.
            arrow.Pointer = PixelUi.Icon(rt, "pointer", s_ArrowSprites[DirUp]);
            arrow.Pointer.raycastTarget = true;
            arrow.Chip = chip;
            arrow.Rect = rt;
            rt.gameObject.SetActive(false);
            return arrow;
        }

        /// <summary>Сторона: стрелка у края поля (над ячейкой для верхних целей, под ней для нижних), бейдж — с другой стороны.</summary>
        static void ApplySide(Arrow arrow, bool top)
        {
            int side = top ? 1 : 0;
            if (arrow.ShownSide == side) return;
            arrow.ShownSide = side;
            arrow.ShownBob = -1;
            arrow.Chip.Badge.PlaceAtCorner(new Vector2(1f, top ? 0f : 1f), PixelUi.BadgeCornerOffset);
        }

        static void ApplyKind(Arrow arrow, int kind)
        {
            if (arrow.ShownKind == kind) return;
            arrow.ShownKind = kind;
            PixelUi.SetIcon(arrow.Chip.Icon, s_KindIcons[kind]);
        }

        /// <summary>Направление к цели → индекс спрайта: диагональ, если отклонение от вертикали больше ~27°.</summary>
        static int ArrowDirection(Vector2 dir, bool top)
        {
            bool up = dir.sqrMagnitude < 1e-4f ? top : dir.y >= 0f;
            int d = up ? DirUp : DirDown;
            if (Mathf.Abs(dir.x) > Mathf.Abs(dir.y) * 0.5f) d += dir.x < 0f ? 1 : 2;
            return d;
        }

        static void ApplyDirection(Arrow arrow, int dir)
        {
            if (arrow.ShownDir == dir) return;
            arrow.ShownDir = dir;
            string name = s_ArrowSprites[dir];
            if (!PixelArt.Has(name)) name = s_ArrowSprites[dir < DirDown ? DirUp : DirDown];
            bool flip = false;
            if (!PixelArt.Has(name))
            {
                // Нет стрелки вниз — отражённая стрелка вверх (отражение разрешено, вращение — нет).
                flip = dir >= DirDown;
                name = s_ArrowSprites[DirUp];
            }
            PixelUi.SetIcon(arrow.Pointer, name);
            arrow.Pointer.rectTransform.localScale = new Vector3(1f, flip ? -1f : 1f, 1f);
        }

        /// <summary>Стрелка выступает из ячейки к краю поля и в фазе покачивания сдвигается на пиксель арта дальше.</summary>
        void ApplyBob(Arrow arrow)
        {
            int bob = m_Bob;
            if (arrow.ShownBob == bob) return;
            arrow.ShownBob = bob;
            float y = ArrowOffset + bob * Px;
            arrow.Pointer.rectTransform.anchoredPosition = new Vector2(0f, arrow.Top ? y : -y);
        }

        void ApplyCount(Arrow arrow)
        {
            arrow.Chip.SetBadge(arrow.Count > 1 ? arrow.Count : 0);
            ApplyBob(arrow);
        }
    }
}

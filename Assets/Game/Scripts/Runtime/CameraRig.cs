using CatMiners.Core.Sim;
using UnityEngine;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Ортографическая камера строго сверху (ТЗ 13.2, Docs/Art/PIXEL_STYLE_GUIDE.md): мировая +Z — вверх по экрану.
    /// По ширине safe area вписываются 7 клеток и около 0.65 клетки стены с каждой стороны; на экранах шире 9:16
    /// между панелями HUD видно не меньше 10 строк. По умолчанию прокрутка вертикальная; FreeExploration включает локальный вид и панорамирование по X/Z. Инерция, автопрокрутка у краёв при переносе.
    /// Упоры прокрутки считаются по полосе поля между панелями HUD (доли safe area): у нижнего низ последней строки
    /// стоит на нижней панели, у верхнего над полосой помещаются выход, его подпись и пузырь (<see cref="TopOverhang"/>).
    /// Открытая контекстная панель опускает нижний упор прокрутки на свою высоту (<see cref="SetContextTop"/>): то, что у низа
    /// карты, можно поднять над ней. Позиция камеры снапится к сетке пикселей арта (1/32 клетки): без «дрожания».
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public const float Pitch = 90f;
        /// <summary>Минимум строк поля, видимых между верхней и нижней панелями HUD (ТЗ 13.2).</summary>
        public const float MinVisibleRows = 10f;
        /// <summary>Видимая полоса стены с каждой стороны поля, клеток.</summary>
        public const float WallMargin = 0.65f;
        /// <summary>Отступ «домашнего» вида: низ кузницы (ворот) над нижней панелью HUD, клеток.</summary>
        public const float HomeBottomMargin = 0.3f;
        /// <summary>
        /// Выступ над строкой 0, клеток: спрайт выхода (2.5 клетки от опоры в строке 0, т. е. +1.5 над картой), подпись «ур. N»
        /// и пузырь статуса (~+2.1). У верхнего упора верх полосы поля — на этой высоте над картой.
        /// </summary>
        public const float TopOverhang = 2.2f;
        /// <summary>Зазор между низом выбранного объекта и верхом контекстной панели, клеток.</summary>
        public const float RevealMargin = 0.25f;
        /// <summary>Время плавного подъёма выбранного над контекстной панелью и возврата к упору после её закрытия, секунды.</summary>
        public const float RevealSeconds = 0.35f;
        /// <summary>Соотношение сторон для рендера в текстуру (скриншоты в тестах); 0 — по экрану.</summary>
        public static float OverrideAspect;
        public Camera Cam;
        public float FocusZ;            // точка на поле (по оси Z) в центре полосы между панелями HUD
        /// <summary>Opt-in local viewport and horizontal exploration; SetBoard defaults to legacy framing.</summary>
        public bool FreeExploration { get; private set; }
        public const float ExplorationVisibleColumns = 10f;
        public float FocusX { get; private set; }
        public float MinX => m_MinX;
        public float MaxX => m_MaxX;
        float m_MinX, m_MaxX;
        Vector2 m_Velocity;
        float m_MinZ, m_MaxZ;
        /// <summary>
        /// Нижний упор (FocusZ) для текущего поля и экрана: до него доводят <see cref="FocusOn"/>, «домашний» вид и стрелки.
        /// Пока открыта контекстная панель, прокрутка игроком и подъём выбранного над панелью опускаются ниже — на её высоту.
        /// </summary>
        public float MinZ => m_MinZ;
        /// <summary>Верхний упор прокрутки (FocusZ) для текущего поля и экрана.</summary>
        public float MaxZ => m_MaxZ;
        float m_BoardWidth = 7f;
        float m_BoardDepth;
        float m_TopUi = 0.12f, m_BottomUi = 0.24f;  // доли safe area под HUD
        float m_CtxExtra;           // насколько контекстная панель выше нижней панели HUD, клеток (0 — панель закрыта)
        bool m_HasBoard;

        // Плавный перевод камеры (после открытия ворот): любое действие игрока его отменяет.
        bool m_Smooth;
        float m_SmoothFrom, m_SmoothTo, m_SmoothT, m_SmoothSeconds;
        float m_SmoothFromX, m_SmoothToX;
        bool m_SmoothPanel;         // перевод начат из-за контекстной панели (подъём выбранного, возврат к упору)
        /// <summary>Идёт плавный перевод камеры по команде (<see cref="FocusOnSmooth"/>: ворота, неактивированная шахта).</summary>
        public bool IsFocusing => m_Smooth && !m_SmoothPanel;

        // Параметры экрана, под которые сделана последняя подгонка.
        int m_FitW, m_FitH;
        Rect m_FitSafe;
        float m_FitAspect;

        public static CameraRig Create(Camera cam)
        {
            var rig = cam.gameObject.GetComponent<CameraRig>();
            if (rig == null) rig = cam.gameObject.AddComponent<CameraRig>();
            rig.Cam = cam;
            cam.orthographic = true;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = PixelPalette.Navy0;
            cam.transform.rotation = Quaternion.Euler(Pitch, 0f, 0f);
            return rig;
        }

        public void SetBoard(int width, int depth, float startZ) => SetBoard(width, depth, startZ, false);

        public void SetBoard(int width, int depth, float startZ, bool freeExploration, float? startX = null)
        {
            FreeExploration = freeExploration;
            m_BoardWidth = width;
            m_BoardDepth = depth;
            m_HasBoard = true;
            // Новое поле — без выбора: панель, если она ещё открыта, заново сообщит свою высоту.
            m_CtxExtra = 0f;
            FitWidth();
            UpdateBounds();
            FocusX = Mathf.Clamp(startX ?? width / 2f, m_MinX, m_MaxX);
            FocusZ = Mathf.Clamp(startZ, m_MinZ, m_MaxZ);
            m_Velocity = Vector2.zero;
            m_Smooth = false;
            Apply();
        }

        /// <summary>
        /// Доли safe area под верхней и нижней панелями HUD. Они задают и размер (не меньше 10 строк в полосе), и упоры,
        /// и центр полосы, поэтому новые доли (поворот, safe area, окно редактора — HUD присылает их с задержкой) сразу
        /// пересчитывают камеру с сохранением позиции.
        /// </summary>
        public void SetUiInsets(float topFraction, float bottomFraction)
        {
            if (Mathf.Approximately(topFraction, m_TopUi) && Mathf.Approximately(bottomFraction, m_BottomUi)) return;
            m_TopUi = topFraction;
            m_BottomUi = bottomFraction;
            if (m_HasBoard && Cam != null) Refit();
        }

        /// <summary>
        /// Верх открытой контекстной панели в пикселях экрана (кадра камеры), ≤ 0 — панель закрыта. Пока панель открыта,
        /// упор прокрутки игроком и плавных переводов ниже <see cref="MinZ"/> на её высоту над нижней панелью HUD: низ карты
        /// можно поднять над панелью. После закрытия камера, оказавшаяся ниже <see cref="MinZ"/>, плавно возвращается к нему.
        /// Только арифметика, без выделений памяти.
        /// </summary>
        public void SetContextTop(float screenY)
        {
            if (Cam == null) return;
            float extra = 0f;
            float frameH = Cam.pixelHeight;
            if (screenY > 0f && frameH > 0f)
            {
                SafeFractions(out float safeMin, out float safeH);
                float bandBottom = safeMin + m_BottomUi * safeH;
                extra = Mathf.Max(0f, screenY / frameH - bandBottom) * VisibleGroundDepth();
            }
            // Меньше пикселя арта — не пересчитываем (панель не меняла высоту).
            if (Mathf.Abs(extra - m_CtxExtra) < 0.5f / PixelArt.CellPixels) return;
            m_CtxExtra = extra;
            if (!m_HasBoard) return;
            if (m_Smooth) m_SmoothTo = Mathf.Clamp(m_SmoothTo, LowMinZ, m_MaxZ);
            else if (FocusZ < LowMinZ - 1e-4f) PanelMove(LowMinZ);
        }

        /// <summary>Плавный перевод из-за контекстной панели: новый выбор может его перехватить (см. <see cref="IsFocusing"/>).</summary>
        void PanelMove(float z)
        {
            FocusOnSmooth(z, RevealSeconds);
            m_SmoothPanel = m_Smooth;
        }

        /// <summary>
        /// Поднять над контекстной панелью (верх — <paramref name="panelTopScreenY"/>, пиксели кадра) то, что выбрано:
        /// низ (<paramref name="bottomZ"/>) и верх (<paramref name="topZ"/>) по мировой z. Камера плавно сдвигается вниз,
        /// только если низ закрыт панелью, и не дальше, чем нужно, — верх выбранного остаётся в полосе поля.
        /// </summary>
        public void RevealAbove(float bottomZ, float topZ, float panelTopScreenY)
        {
            if (!m_HasBoard || Cam == null || panelTopScreenY <= 0f || Cam.pixelHeight <= 0) return;
            SafeFractions(out float safeMin, out float safeH);
            float depth = VisibleGroundDepth();
            // Отсчёт — от точки, где камера остановится (идёт возврат к упору — от его конца).
            float focus = m_Smooth ? m_SmoothTo : FocusZ;
            // FocusZ — центр полосы поля; доли кадра → мировая z относительно него.
            float bandCenter = safeMin + (m_BottomUi + (1f - m_TopUi - m_BottomUi) / 2f) * safeH;
            float zPanel = focus + (panelTopScreenY / Cam.pixelHeight - bandCenter) * depth;
            if (bottomZ + RevealMargin <= zPanel) return;
            float target = focus - (zPanel - bottomZ - RevealMargin);
            // Не уводим верх выбранного под верхнюю панель.
            float zTop = focus + (safeMin + (1f - m_TopUi) * safeH - bandCenter) * depth;
            if (topZ < zTop) target = Mathf.Max(target, focus - (zTop - topZ));
            if (target < focus - 1e-4f) PanelMove(target);
        }

        void UpdateBounds()
        {
            // FocusZ — центр полосы между панелями HUD: упоры считаются по самой полосе (доли HUD и safe area), а не по экрану.
            float halfWidth = SafeWorldWidth() / 2f;
            m_MinX = FreeExploration ? Mathf.Min(halfWidth - WallMargin, m_BoardWidth / 2f) : m_BoardWidth / 2f;
            m_MaxX = FreeExploration ? Mathf.Max(m_MinX, m_BoardWidth + WallMargin - halfWidth) : m_MinX;
            float half = BandWorldHeight() / 2f;
            // Низ полосы = низ последней строки.
            m_MinZ = Mathf.Min(half, m_BoardDepth / 2f);
            // Верх полосы = верх выхода с подписью и пузырём.
            m_MaxZ = Mathf.Max(m_MinZ, m_BoardDepth + TopOverhang - half);
        }

        /// <summary>Подогнать размер и упоры под экран и доли HUD, сохранив позицию.</summary>
        void Refit()
        {
            FitWidth();
            UpdateBounds();
            FocusX = Mathf.Clamp(FocusX, m_MinX, m_MaxX);
            FocusZ = Mathf.Clamp(FocusZ, LowMinZ, m_MaxZ);
            if (m_Smooth)
            {
                m_SmoothTo = Mathf.Clamp(m_SmoothTo, LowMinZ, m_MaxZ);
                m_SmoothFromX = Mathf.Clamp(m_SmoothFromX, m_MinX, m_MaxX);
                m_SmoothToX = Mathf.Clamp(m_SmoothToX, m_MinX, m_MaxX);
            }
            Apply();
        }

        /// <summary>
        /// Прокрутка игроком: ниже упора можно оказаться только после закрытия контекстной панели (камера туда опускалась
        /// для выбранного) — оттуда прокрутка вверх идёт без скачка, вниз — не дальше текущей точки.
        /// </summary>
        float ClampScroll(float z) => Mathf.Clamp(z, Mathf.Min(LowMinZ, FocusZ), m_MaxZ);

        /// <summary>Нижний упор прокрутки игроком и плавных переводов: ниже <see cref="MinZ"/> на высоту открытой контекстной панели.</summary>
        float LowMinZ => m_MinZ - m_CtxExtra;

        void FitWidth()
        {
            var safe = Screen.safeArea;
            float aspect = Screen.height > 0 ? safe.width / Mathf.Max(1f, Screen.height) : 0.5625f;
            if (OverrideAspect > 0f) aspect = OverrideAspect;
            float halfWidth = FreeExploration ? ExplorationVisibleColumns / 2f : m_BoardWidth / 2f + WallMargin;
            float size = halfWidth / Mathf.Max(0.3f, aspect);
            // На экранах шире 9:16 не крупним клетку: между панелями HUD (доли safe area) должно быть видно
            // не меньше 10 строк. Тогда 7 столбцов занимают меньше всей ширины экрана, по бокам видно больше стены.
            float band = Mathf.Max(0.3f, 1f - m_TopUi - m_BottomUi);
            float heightRatio = OverrideAspect > 0f || Screen.height <= 0 ? 1f : Screen.height / Mathf.Max(1f, safe.height);
            float minSize = MinVisibleRows * Mathf.Sin(Pitch * Mathf.Deg2Rad) / 2f * heightRatio / band;
            Cam.orthographicSize = FreeExploration ? size : Mathf.Max(size, minSize);
            m_FitW = Screen.width;
            m_FitH = Screen.height;
            m_FitSafe = safe;
            m_FitAspect = OverrideAspect;
        }

        float FrameAspect => OverrideAspect > 0f ? OverrideAspect : Cam.aspect;

        float SafeWorldWidth()
        {
            float safeRatio = OverrideAspect > 0f || Screen.width <= 0 ? 1f : Screen.safeArea.width / Screen.width;
            return 2f * Cam.orthographicSize * FrameAspect * safeRatio;
        }

        float HorizontalWorldPerPixel() => 2f * Cam.orthographicSize * FrameAspect / Mathf.Max(1, Cam.pixelWidth);

        bool ScreenChanged() =>
            Screen.width != m_FitW || Screen.height != m_FitH || Screen.safeArea != m_FitSafe || !Mathf.Approximately(OverrideAspect, m_FitAspect);

        /// <summary>Сколько строк поля помещается по высоте экрана.</summary>
        public float VisibleGroundDepth() => 2f * Cam.orthographicSize / Mathf.Sin(Pitch * Mathf.Deg2Rad);

        /// <summary>Мировые единицы поля на пиксель вертикального сдвига.</summary>
        public float WorldPerPixel() => 2f * Cam.orthographicSize / Mathf.Max(1, Cam.pixelHeight) / Mathf.Sin(Pitch * Mathf.Deg2Rad);

        /// <summary>Safe area в долях высоты экрана: низ и высота (рендер в текстуру — весь кадр).</summary>
        void SafeFractions(out float yMin, out float height)
        {
            yMin = 0f;
            height = 1f;
            if (OverrideAspect > 0f || Screen.height <= 0) return;
            var safe = Screen.safeArea;
            if (safe.height <= 0f) return;
            yMin = safe.yMin / Screen.height;
            height = safe.height / Screen.height;
        }

        /// <summary>Высота полосы поля между панелями HUD, в клетках.</summary>
        public float BandWorldHeight()
        {
            SafeFractions(out _, out float h);
            return VisibleGroundDepth() * Mathf.Max(0.05f, 1f - m_TopUi - m_BottomUi) * h;
        }

        /// <summary>
        /// «Домашний» вид текущей сессии: зона мержа (<see cref="GameSession.MergeRows"/>) и то, что под ней — кузница на старте,
        /// самые верхние открытые ворота после открытия, — внизу полосы между панелями HUD, сразу над нижней панелью;
        /// выше видны камни впереди. Значение FocusZ с учётом упоров прокрутки.
        /// </summary>
        public float HomeFocusZ(GameSession session)
        {
            if (session == null || session.Layout == null) return FocusZ;
            var l = session.Layout;
            int opened = session.Mine.GatesOpened;
            int baseRow = opened > 0 && opened <= l.Gates.Count ? l.Gates[opened - 1].Row : (l.Cart != null ? l.Cart.Row : l.Depth - 1);
            // Опора вида — нижняя из строк: кузница (ворота) или, если данные иные, нижний ряд зоны мержа.
            if (session.MergeRows(out _, out int lower)) baseRow = Mathf.Max(baseRow, lower);
            baseRow = Mathf.Clamp(baseRow, 0, l.Depth - 1);
            float bottomZ = l.Depth - baseRow - 1f;
            float z = bottomZ + BandWorldHeight() / 2f - HomeBottomMargin;
            return m_HasBoard ? Mathf.Clamp(z, m_MinZ, m_MaxZ) : z;
        }

        public void ScrollByPixels(float dy) => ScrollByPixels(new Vector2(0f, dy));

        /// <summary>Held-pointer movement in screen pixels; the field follows the pointer on both axes.</summary>
        public void ScrollByPixels(Vector2 delta)
        {
            m_Smooth = false;
            m_Velocity = Vector2.zero;
            FocusX = Mathf.Clamp(FocusX - (FreeExploration ? delta.x * HorizontalWorldPerPixel() : 0f), m_MinX, m_MaxX);
            FocusZ = ClampScroll(FocusZ - delta.y * WorldPerPixel());
            Apply();
        }

        public void Fling(float pixelsPerSecond) => Fling(new Vector2(0f, pixelsPerSecond));

        public void Fling(Vector2 pixelsPerSecond)
        {
            m_Smooth = false;
            m_Velocity = new Vector2(FreeExploration ? -pixelsPerSecond.x * HorizontalWorldPerPixel() : 0f,
                -pixelsPerSecond.y * WorldPerPixel());
        }

        public void StopFling()
        {
            m_Smooth = false;
            m_Velocity = Vector2.zero;
        }

        public void ScrollWorld(float dz) => ScrollWorld(new Vector2(0f, dz));

        /// <summary>World-space pan (x, z), also usable by legacy edge scrolling.</summary>
        public void ScrollWorld(Vector2 delta)
        {
            m_Smooth = false;
            m_Velocity = Vector2.zero;
            FocusX = Mathf.Clamp(FocusX + (FreeExploration ? delta.x : 0f), m_MinX, m_MaxX);
            FocusZ = ClampScroll(FocusZ + delta.y);
            Apply();
        }

        /// <summary>Мгновенный перевод камеры (загрузка, тесты, «Свободные котики», стрелки) в пределах <see cref="MinZ"/>…<see cref="MaxZ"/>.</summary>
        public void FocusOn(float z) => FocusOn(new Vector2(FocusX, z));

        public void FocusOn(Vector2 focus)
        {
            m_Smooth = false;
            FocusX = Mathf.Clamp(focus.x, m_MinX, m_MaxX);
            FocusZ = Mathf.Clamp(focus.y, m_MinZ, m_MaxZ);
            m_Velocity = Vector2.zero;
            Apply();
        }

        /// <summary>
        /// Плавный перевод камеры к точке z за <paramref name="seconds"/> (ТЗ 11.1: к площадке нового сектора).
        /// Прокрутка, инерция, касание поля, <see cref="FocusOn"/> и <see cref="SetBoard"/> его отменяют.
        /// </summary>
        public void FocusOnSmooth(float z, float seconds = 0.6f) => FocusOnSmooth(new Vector2(FocusX, z), seconds);

        public void FocusOnSmooth(Vector2 focus, float seconds = 0.6f)
        {
            m_Velocity = Vector2.zero;
            m_SmoothPanel = false;
            m_SmoothFromX = FocusX;
            m_SmoothToX = Mathf.Clamp(focus.x, m_MinX, m_MaxX);
            m_SmoothFrom = FocusZ;
            m_SmoothTo = Mathf.Clamp(focus.y, LowMinZ, m_MaxZ);
            m_SmoothT = 0f;
            m_SmoothSeconds = Mathf.Max(0.05f, seconds);
            m_Smooth = !Mathf.Approximately(m_SmoothFrom, m_SmoothTo) || !Mathf.Approximately(m_SmoothFromX, m_SmoothToX);
            if (!m_Smooth)
            {
                FocusX = m_SmoothToX;
                FocusZ = m_SmoothTo;
                Apply();
            }
        }

        // Импульс камеры на редких крупных событиях (ворота, выход, динамит): затухающее покачивание по вертикали
        // целыми пикселями арта, ≈0,3 с. С «меньше эффектов» не играет.
        const float KickSeconds = 0.3f;
        float m_KickT = -1f, m_KickPixels;

        /// <summary>Короткий импульс камеры (пиксели арта); отключается настройкой «меньше эффектов».</summary>
        public void Kick(float pixels = 2f)
        {
            if (VisualSettings.ReduceEffects) return;
            m_KickT = 0f;
            m_KickPixels = pixels;
        }

        float KickOffset()
        {
            if (m_KickT < 0f) return 0f;
            float t = m_KickT;
            float px = Mathf.Round(m_KickPixels * Mathf.Exp(-t / 0.09f) * Mathf.Cos(t * 55f));
            return px / PixelArt.CellPixels;
        }

        void LateUpdate()
        {
            if (Cam == null) return;
            if (m_KickT >= 0f)
            {
                m_KickT += FrameTime.Delta;
                if (m_KickT >= KickSeconds) m_KickT = -1f;
                Apply();
            }
            // Поворот экрана, смена окна или safe area: подогнать размер и границы, сохранив позицию.
            if (m_HasBoard && ScreenChanged()) Refit();
            if (m_Smooth)
            {
                m_SmoothT = Mathf.Min(1f, m_SmoothT + FrameTime.Delta / m_SmoothSeconds);
                // Плавный старт и мягкая остановка (ease-in-out cubic).
                float t = m_SmoothT;
                float k = t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
                FocusX = Mathf.Lerp(m_SmoothFromX, m_SmoothToX, k);
                FocusZ = Mathf.Lerp(m_SmoothFrom, m_SmoothTo, k);
                if (m_SmoothT >= 1f)
                {
                    FocusX = m_SmoothToX;
                    FocusZ = m_SmoothTo;
                    m_Smooth = false;
                }
                Apply();
                return;
            }
            if (m_Velocity.sqrMagnitude > 0.0001f)
            {
                float x = FocusX + m_Velocity.x * FrameTime.Delta;
                float z = FocusZ + m_Velocity.y * FrameTime.Delta;
                FocusX = Mathf.Clamp(x, m_MinX, m_MaxX);
                FocusZ = ClampScroll(z);
                // Stop each component at its own edge; a diagonal fling can continue along the other axis.
                if (!Mathf.Approximately(x, FocusX)) m_Velocity.x = 0f;
                if (!Mathf.Approximately(z, FocusZ)) m_Velocity.y = 0f;
                m_Velocity *= Mathf.Exp(-4f * FrameTime.Delta);
                Apply();
            }
        }

        void Apply()
        {
            // FocusZ — центр полосы поля между HUD (доли safe area): центр экрана сдвинут от него на разницу центров.
            SafeFractions(out float safeMin, out float safeH);
            float bandCenter = safeMin + (m_BottomUi + (1f - m_TopUi - m_BottomUi) / 2f) * safeH;
            float uiShift = (bandCenter - 0.5f) * VisibleGroundDepth();
            // Снап к сетке пикселей арта (1/32 клетки): спрайты сдвигаются целыми пикселями арта и не «дрожат» при прокрутке.
            float xShift = 0f;
            if (FreeExploration && OverrideAspect <= 0f && Screen.width > 0)
                xShift = (Screen.safeArea.center.x / Screen.width - 0.5f) * 2f * Cam.orthographicSize * FrameAspect;
            var target = PixelWorld.Snap(new Vector3(FocusX - xShift, 0f, FocusZ - uiShift + KickOffset()));
            Cam.transform.position = target - Cam.transform.forward * 40f;
        }

        /// <summary>Пересечение луча из экранной точки с плоскостью поля y = 0.</summary>
        public bool ScreenToGround(Vector2 screen, out Vector3 world)
        {
            var ray = Cam.ScreenPointToRay(screen);
            world = default;
            if (Mathf.Abs(ray.direction.y) < 1e-5f) return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0) return false;
            world = ray.origin + ray.direction * t;
            return true;
        }
    }
}

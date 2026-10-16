using System.Collections.Generic;
using CatMiners.Core.Outpost;
using UnityEngine;
using BigInteger = System.Numerics.BigInteger;

namespace CatMiners.Runtime.Outpost
{
    // Заводы ТЗ 2.2 на поле (VIEW_API §11): вид завода по состоянию, трубы и капсулы, склад товаров во дворе, «+N» прибытия,
    // события заводов → эффекты и подсказки звука. Только читает ядро.
    public sealed partial class OutpostBoardView
    {
        FactoryView[] m_FactoryViews;                 // по индексу цели (null — не завод)
        int[] m_FactoryStateIndex;                    // индекс цели → индекс в Run.Factories (кэш, сверяется по TargetId)
        readonly List<FactoryView> m_FactoryList = new List<FactoryView>();
        PipeNetworkView m_Pipes;
        GoodsPopups m_Popups;

        /// <summary>Трубы и капсулы текущей шахты (null до Build).</summary>
        public PipeNetworkView Pipes => m_Pipes;
        /// <summary>Виды заводов шахты по возрастанию ID цели.</summary>
        public IReadOnlyList<FactoryView> FactoryViews => m_FactoryList;
        /// <summary>Всплывающих «+N» у склада сейчас.</summary>
        public int GoodsPopupsActive => m_Popups != null ? m_Popups.Active : 0;

        public FactoryView FactoryViewOf(int targetId)
        {
            if (m_Map == null || m_FactoryViews == null) return null;
            int i = m_Map.TargetIndex(targetId);
            return i >= 0 && i < m_FactoryViews.Length ? m_FactoryViews[i] : null;
        }

        /// <summary>Центр footprint завода в мире (IOutpostGame.TryGetFactoryWorldPosition); false — не завод.</summary>
        public bool TryGetFactoryWorldPosition(int targetId, out Vector3 world)
        {
            var v = FactoryViewOf(targetId);
            var tv = v != null ? TargetViewOf(targetId) : null;
            world = tv != null ? Origin + tv.CenterLocal : Vector3.zero;
            return tv != null;
        }

        /// <summary>Точка над полосой завода в мире: под ней — здание, над ней — кнопка «Deliver ×N».</summary>
        public bool TryGetFactoryTopWorldPosition(int targetId, out Vector3 world)
        {
            var v = FactoryViewOf(targetId);
            world = v != null ? Origin + v.TopLocal : Vector3.zero;
            return v != null;
        }

        /// <summary>
        /// Завод на экране — от ног footprint (левый нижний угол) до верха полосы цикла (правый верхний угол), по ширине footprint:
        /// <see cref="FactoryOverlay"/> держит кнопку «Deliver» над своим зданием и уводит стрелки у края с полосы, колеса и здания.
        /// Середина верхнего края — <see cref="TryGetFactoryTopWorldPosition"/>. false — не завод.
        /// </summary>
        public bool TryGetFactoryWorldBounds(int targetId, out Vector3 bottomLeft, out Vector3 topRight)
        {
            var v = FactoryViewOf(targetId);
            var tv = v != null ? TargetViewOf(targetId) : null;
            if (tv == null)
            {
                bottomLeft = topRight = Vector3.zero;
                return false;
            }
            var half = new Vector3(tv.Width * 0.5f, 0f, 0f);
            bottomLeft = Origin + tv.FeetLocal - half;
            topRight = Origin + v.TopLocal + half;
            return true;
        }

        /// <summary>Склад товаров во дворе (IOutpostGame.WarehouseWorldPosition): «+N» прибытия и начало полёта монет продажи.</summary>
        public Vector3 WarehouseWorldPosition => m_Camp != null && m_Camp.HasWarehouse ? Origin + m_Camp.WarehouseIndicatorLocal : BaseWorldPosition;

        /// <summary>Середина здания склада в мире (луч выбора, камера).</summary>
        public Vector3 WarehouseCenterWorldPosition => m_Camp != null && m_Camp.HasWarehouse ? Origin + m_Camp.WarehouseCenterLocal : BaseWorldPosition;

        // ───────────── Сборка ─────────────

        /// <summary>Товар завода цели <c>s</c> из run (null — цель не завод или в run нет заводов: прежний вид месторождения).</summary>
        string FactoryGoodOf(MapTarget mt)
        {
            if (mt.Kind != 's' || m_Sim == null || m_Map.Factory(mt.Id) == null) return null;
            var f = FactoryQueries.Find(m_Sim.Run, mt.Id);
            return f?.GoodId;
        }

        void ResetFactories()
        {
            m_FactoryList.Clear();
            m_FactoryViews = null;
            m_FactoryStateIndex = null;
            m_Pipes = null;
            m_Popups = null;
        }

        void AttachFactory(OutpostTargetView v, int index)
        {
            if (!v.IsFactory) return;
            if (m_FactoryViews == null)
            {
                m_FactoryViews = new FactoryView[m_Map.Targets.Length];
                m_FactoryStateIndex = new int[m_Map.Targets.Length];
                for (int i = 0; i < m_FactoryStateIndex.Length; i++) m_FactoryStateIndex[i] = -1;
            }
            var fv = FactoryView.Attach(v, v.FactoryGood);
            m_FactoryViews[index] = fv;
            m_FactoryList.Add(fv);
        }

        /// <summary>Трубы и «+N» склада — после лагеря (вход трубы на коньке склада).</summary>
        void BuildFactoryNetwork()
        {
            m_Pipes = PipeNetworkView.Create(m_Root, m_Map, m_Sim?.Run, m_Camp);
            m_Popups = new GoodsPopups(m_Root);
        }

        // ───────────── Кадр ─────────────

        FactoryState FactoryStateAt(OutpostRun run, int targetIndex)
        {
            var list = run.Factories;
            if (list == null) return null;
            int id = m_Map.Targets[targetIndex].Id;
            int i = m_FactoryStateIndex[targetIndex];
            if (i >= 0 && i < list.Count && list[i] != null && list[i].TargetId == id) return list[i];
            for (i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].TargetId == id)
                {
                    m_FactoryStateIndex[targetIndex] = i;
                    return list[i];
                }
            return null;
        }

        void SyncFactoryView(OutpostRun run, int targetIndex, float time, float dt)
        {
            if (m_FactoryViews == null) return;
            var fv = m_FactoryViews[targetIndex];
            if (fv != null) fv.Sync(FactoryStateAt(run, targetIndex), run.Now, time, dt);
        }

        void SyncFactoryNetwork(OutpostRun run, float dt)
        {
            m_Pipes?.Sync(run, dt);
            m_Popups?.Tick(dt);
        }

        // ───────────── События ─────────────

        /// <summary>События заводов (CORE_SPEC §16.7) → эффекты и подсказки звука; false — событие не заводское.</summary>
        bool OnFactoryEvent(OutpostEvent e, Effects fx, Vector3 origin)
        {
            switch (e.Type)
            {
                case OutpostEventType.FactoryUnlocked:
                {
                    var fv = FactoryViewOf(e.TargetId);
                    var tv = TargetViewOf(e.TargetId);
                    if (fv != null && m_Live) fv.PlayUnlock(fx != null && TakeFx() ? fx : null, origin);
                    if (m_Live) m_Pipes?.BeginReveal(e.TargetId);
                    EmitFactory(OutpostViewCue.ChainBreak, tv != null ? tv.CenterLocal : BaseWorldPosition - origin, e);
                    return true;
                }
                case OutpostEventType.FactoryReady:
                {
                    var fv = FactoryViewOf(e.TargetId);
                    if (fv != null && fx != null && m_Live && TakeFx()) fx.Sparkle(origin + fv.TopLocal, 2);
                    EmitFactory(OutpostViewCue.FactoryReady, fv != null ? fv.TopLocal : Vector3.zero, e);
                    return true;
                }
                case OutpostEventType.FactoryAutomated:
                {
                    var fv = FactoryViewOf(e.TargetId);
                    if (fv != null && fx != null && m_Live && TakeFx()) fx.Sparkle(origin + fv.TopLocal, 4);
                    EmitFactory(OutpostViewCue.FactoryAutomated, fv != null ? fv.TopLocal : Vector3.zero, e);
                    return true;
                }
                case OutpostEventType.GoodsDispatched:
                {
                    var at = Vector3.zero;
                    if (m_Pipes != null && m_Pipes.TryGetCapLocal(e.TargetId, out var cap))
                    {
                        at = cap;
                        if (fx != null && m_Live && TakeFx()) fx.Sparkle(origin + cap, 1);
                    }
                    var args = FactoryCue(OutpostViewCue.PipeSend, at, e);
                    args.Automatic = e.Extra == 1;
                    // Автодоставка тише ручной и молчит, когда на экране больше трёх автоматических заводов (SOUNDS.md, «Заводы»).
                    if (args.Automatic) args.Count = AutomatedOnScreen();
                    Cue?.Invoke(args);
                    return true;
                }
                case OutpostEventType.GoodsArrived:
                {
                    var at = m_Camp != null && m_Camp.HasWarehouse ? m_Camp.WarehouseIndicatorLocal : new Vector3(Width / 2f, 0f, -3f);
                    m_Camp?.OnGoodsArrived();
                    // «+N» — левее склада: он в крайнем столбце площадки, справа надпись ушла бы за край экрана.
                    if (m_Popups != null && m_Live && TakeText())
                        m_Popups.Spawn(origin + at + new Vector3(-0.75f, 0f, 0f), PlusMoneyText(new BigInteger(e.Count)), FactoryText.GoodIcon(e.ItemId));
                    EmitFactory(OutpostViewCue.GoodsArrive, at, e);
                    return true;
                }
                case OutpostEventType.GoodsSold:
                {
                    var at = m_Camp != null && m_Camp.HasWarehouse ? m_Camp.WarehouseIndicatorLocal : new Vector3(Width / 2f, 0f, -3f);
                    var args = FactoryCue(OutpostViewCue.GoodsSold, at, e);
                    args.Value = e.Value;
                    Cue?.Invoke(args);
                    return true;
                }
            }
            return false;
        }

        OutpostViewCueArgs FactoryCue(OutpostViewCue cue, Vector3 local, OutpostEvent e)
        {
            var args = OutpostViewCueArgs.Create(cue, Origin + local);
            args.TargetId = e.TargetId;
            args.Count = e.Count;
            args.ItemId = e.ItemId;
            return args;
        }

        void EmitFactory(OutpostViewCue cue, Vector3 local, OutpostEvent e)
        {
            if (Cue == null) return;
            Cue(FactoryCue(cue, local, e));
        }

        /// <summary>Автоматических заводов в кадре камеры (центр здания во вьюпорте).</summary>
        int AutomatedOnScreen()
        {
            var cam = Camera.main;
            var run = m_Sim?.Run;
            if (cam == null || run?.Factories == null) return 0;
            int n = 0;
            for (int i = 0; i < m_FactoryList.Count; i++)
            {
                var fv = m_FactoryList[i];
                var f = FactoryQueries.Find(run, fv.TargetId);
                if (f == null || !f.Automated) continue;
                var tv = TargetViewOf(fv.TargetId);
                if (tv == null) continue;
                var p = cam.WorldToViewportPoint(Origin + tv.CenterLocal);
                if (p.x >= 0f && p.x <= 1f && p.y >= 0f && p.y <= 1f) n++;
            }
            return n;
        }

        // ───────────── «+N» у склада ─────────────

        /// <summary>
        /// «+N» и иконка товара над складом при прибытии партии (ТЗ 2.2 §5): поднимаются на 0,8 клетки за 1 с и гаснут ступенями,
        /// как FloatingText. Пул из <see cref="Size"/> надписей, без выделений в кадре.
        /// </summary>
        sealed class GoodsPopups
        {
            const int Size = 6;
            const float Seconds = 1f, RiseCells = 0.8f;
            const int Order = PixelWorld.OverlayBase + 100;

            readonly Transform[] m_Items = new Transform[Size];
            readonly Label3D[] m_Label = new Label3D[Size];
            readonly SpriteRenderer[] m_Icon = new SpriteRenderer[Size];
            readonly float[] m_T = new float[Size];
            readonly Vector3[] m_Start = new Vector3[Size];
            readonly int[] m_Step = new int[Size];
            int m_Next;

            public int Active { get; private set; }

            public GoodsPopups(Transform parent)
            {
                for (int i = 0; i < Size; i++)
                {
                    var go = new GameObject("goodsPopup");
                    go.transform.SetParent(parent, false);
                    m_Items[i] = go.transform;
                    var visual = PixelWorld.Visual(go.transform, "icon");
                    m_Icon[i] = PixelWorld.Sprite(visual, "goods", "goods_milk", Vector2.zero, Order);
                    m_Label[i] = Label3D.Create(go.transform, Vector3.zero, Label3D.LargeSize, Palette.Highlight);
                    m_Label[i].SetOrder(Order + 1);
                    m_T[i] = -1f;
                    go.SetActive(false);
                }
            }

            /// <summary>Показать «+N» с иконкой товара в мировой точке <paramref name="world"/>.</summary>
            public void Spawn(Vector3 world, string text, string icon)
            {
                int i = m_Next;
                m_Next = (m_Next + 1) % Size;
                if (m_T[i] < 0f) Active++;
                m_T[i] = 0f;
                m_Step[i] = -1;
                m_Start[i] = PixelWorld.Snap(world);
                var label = m_Label[i];
                label.Text.text = text;
                label.Text.color = Palette.Highlight;
                PixelWorld.SetSprite(m_Icon[i], icon);
                m_Icon[i].color = Color.white;
                // Иконка слева от числа: ширина текста — 6 пикселей шрифта (по 2 пикселя арта) на знак.
                float half = (text.Length * 12f + 18f) * 0.5f / PixelArt.CellPixels;
                m_Icon[i].transform.localPosition = new Vector3(PixelWorld.Snap(-half + 8f / PixelArt.CellPixels), 0f, 0f);
                label.transform.localPosition = new Vector3(PixelWorld.Snap(9f / PixelArt.CellPixels), 0.1f, 0f);
                m_Items[i].gameObject.SetActive(true);
                Apply(i);
            }

            public void Tick(float dt)
            {
                if (Active == 0) return;
                for (int i = 0; i < Size; i++)
                {
                    if (m_T[i] < 0f) continue;
                    m_T[i] += dt;
                    if (m_T[i] >= Seconds)
                    {
                        m_T[i] = -1f;
                        Active--;
                        m_Items[i].gameObject.SetActive(false);
                        continue;
                    }
                    Apply(i);
                }
            }

            void Apply(int i)
            {
                float u = m_T[i] / Seconds;
                float rise = RiseCells * (1f - (1f - u) * (1f - u));
                int step = Mathf.RoundToInt(rise * PixelArt.CellPixels);
                if (step != m_Step[i])
                {
                    m_Step[i] = step;
                    var p = m_Start[i];
                    p.z += step / (float)PixelArt.CellPixels;
                    m_Items[i].position = p;
                }
                float alpha = u < 0.6f ? 1f : (u < 0.73f ? 0.75f : (u < 0.86f ? 0.5f : 0.25f));
                var c = m_Label[i].Text.color;
                if (c.a != alpha)
                {
                    c.a = alpha;
                    m_Label[i].Text.color = c;
                    m_Icon[i].color = new Color(1f, 1f, 1f, alpha);
                }
            }
        }
    }
}

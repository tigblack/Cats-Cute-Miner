using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CatMiners.Core;
using CatMiners.Core.Data;
using CatMiners.Core.Grid;
using CatMiners.Core.Sim;
using CatMiners.Runtime;
using TMPro;
using UnityEngine;

namespace CatMiners.DevTools
{
    /// <summary>
    /// Инструменты разработки (ТЗ 17): только Editor и Development Build, только слот Debug.
    /// Runtime и сцена на эту сборку не ссылаются: панель создаётся сама после загрузки сцены.
    /// </summary>
    public sealed class DevPanel : MonoBehaviour
    {
        public const string PlayerValidationFile = "validation-player.json";

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        GameController m_Game;
        RectTransform m_Panel;
        TextMeshProUGUI m_ZoneLabel;
        TextMeshProUGUI m_Status;
        RectTransform m_Info;
        TextMeshProUGUI m_InfoTitle;
        TextMeshProUGUI m_InfoBody;
        int m_Zone = 1;
        int m_CatLevel = 3;
        bool m_GridOn;
        GameObject m_Grid;
        ZoneLayout m_GridLayout;
        Color32[] m_GridColors;
        float m_NextGrid;
        string m_LastValidation;
        // Показанные значения подписи зоны и строки состояния: текст собирается, только когда они изменились
        // (Update без выделений памяти; панель есть в каждом запуске редактора и Development-сборки).
        int m_ZoneShown = int.MinValue;
        bool m_StatusValid;
        string m_StatusSlot, m_StatusSaveDir, m_StatusValidation;
        double m_StatusSpeed;
        int m_StatusCat;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (GameBootstrap.Controller == null) return;
            var go = new GameObject("DevTools");
            var panel = go.AddComponent<DevPanel>();
            panel.m_Game = GameBootstrap.Controller;
            panel.Build();
        }

        void Build()
        {
            var hudCanvas = m_Game.Hud.transform;
            var safe = hudCanvas.Find("SafeArea");
            var toggle = Ui.Button(safe, "DevToggle", "DEV", 22, new Color(0.25f, 0.25f, 0.3f, 0.9f), () => m_Panel.gameObject.SetActive(!m_Panel.gameObject.activeSelf));
            toggle.Label.color = Color.white;
            toggle.Image.rectTransform.Anchor(0.0f, 0.82f, 0.12f, 0.86f, new Vector2(12, 0), new Vector2(0, 0));

            m_Panel = Ui.Panel(safe, "DevPanel", new Color(0.08f, 0.08f, 0.1f, 0.94f)).rectTransform.Anchor(0.03f, 0.40f, 0.97f, 0.81f);
            var list = Ui.Rect(m_Panel, "list").Anchor(0, 0, 1, 1);
            Ui.VList(list, 8, 14);

            m_Status = Ui.Text(list, "status", "", 22, Palette.UiTextDim, TextAlignmentOptions.TopLeft);
            Ui.Layout(m_Status, 92);

            var zoneRow = Row(list);
            Btn(zoneRow, "−10", () => SetZone(m_Zone - 10));
            Btn(zoneRow, "−1", () => SetZone(m_Zone - 1));
            m_ZoneLabel = Ui.Text(zoneRow, "zone", "", 26, Color.white);
            Btn(zoneRow, "+1", () => SetZone(m_Zone + 1));
            Btn(zoneRow, "+10", () => SetZone(m_Zone + 10));
            Btn(zoneRow, "Перейти (Debug)", GotoZone, 1.8f);

            var slotRow = Row(list);
            Btn(slotRow, "Новый Debug", () => { if (RequireBalance()) m_Game.SwitchSlot(SaveSlots.Debug, true, m_Zone); });
            Btn(slotRow, "Основной слот", () => { if (RequireBalance()) m_Game.SwitchSlot(SaveSlots.Main, false); });
            Btn(slotRow, "Валидатор", RunPlayerValidation);

            var speedRow = Row(list);
            foreach (var sp in new[] { 1, 5, 20, 100 })
            {
                int v = sp;
                Btn(speedRow, "×" + v, () => SetSpeed(v));
            }

            var giveRow = Row(list);
            Btn(giveRow, "+золото", () => DebugAction(GiveRichGold));
            Btn(giveRow, "+эликсир", () => DebugAction(s => s.DebugAddElixir(GameNumber.FromDouble(100000))));
            Btn(giveRow, "+динамит", () => DebugAction(s => s.DebugAddDynamite(4)));
            Btn(giveRow, "Карты зоны", () => DebugAction(s => s.DebugGiveZoneCards()));

            var catRow = Row(list);
            Btn(catRow, "ур. −", () => m_CatLevel = Mathf.Max(1, m_CatLevel - 1));
            Btn(catRow, "ур. +", () => m_CatLevel = Mathf.Min(40, m_CatLevel + 1));
            Btn(catRow, "+котик", () => DebugAction(s => s.DebugSpawnCat(m_CatLevel)), 1.5f);

            var targetRow = Row(list);
            Btn(targetRow, "Завершить цель", () => DebugAction(s =>
            {
                if (m_Game.SelectedOrdinal >= 0) s.DebugCompleteTarget(m_Game.SelectedOrdinal);
            }), 1.5f);
            Btn(targetRow, "Сетка", ToggleGrid);
            Btn(targetRow, "Формулы", () => { if (RequireDebugSlot()) ShowFormulaChain(); });

            BuildInfo(safe);
            m_Panel.gameObject.SetActive(false);
            m_Zone = m_Game.Session != null ? m_Game.Session.Mine.Zone : 1;
        }

        /// <summary>Окно инструментов (цепочка расчёта): длинный текст с автоподбором размера шрифта.</summary>
        void BuildInfo(Transform safe)
        {
            m_Info = Ui.Panel(safe, "DevInfo", new Color(0.05f, 0.05f, 0.07f, 0.97f)).rectTransform.Anchor(0.03f, 0.08f, 0.97f, 0.88f);
            m_InfoTitle = Ui.Text(m_Info, "title", "", 26, Palette.Highlight, TextAlignmentOptions.TopLeft);
            m_InfoTitle.rectTransform.Anchor(0, 0.95f, 1, 1, new Vector2(20, 0), new Vector2(-20, -10));
            m_InfoBody = Ui.Text(m_Info, "body", "", 22, Palette.UiText, TextAlignmentOptions.TopLeft);
            m_InfoBody.rectTransform.Anchor(0, 0.08f, 1, 0.95f, new Vector2(20, 0), new Vector2(-20, -4));
            m_InfoBody.enableAutoSizing = true;
            m_InfoBody.fontSizeMin = 12;
            m_InfoBody.fontSizeMax = 22;
            m_InfoBody.overflowMode = TextOverflowModes.Truncate;
            m_InfoBody.richText = false;
            var close = Ui.Button(m_Info, "close", "Закрыть", 24, new Color(0.35f, 0.38f, 0.5f), HideInfo);
            close.Label.color = Color.white;
            close.Image.rectTransform.Anchor(0.3f, 0.01f, 0.7f, 0.07f);
            m_Info.gameObject.SetActive(false);
        }

        void ShowInfo(string title, string body)
        {
            m_InfoTitle.text = title;
            m_InfoBody.text = body;
            m_Info.gameObject.SetActive(true);
            m_Info.SetAsLastSibling();
        }

        void HideInfo()
        {
            if (m_Info != null) m_Info.gameObject.SetActive(false);
        }

        static RectTransform Row(RectTransform parent)
        {
            var row = Ui.Rect(parent, "row");
            var h = row.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            h.spacing = 8;
            h.childForceExpandWidth = true;
            h.childControlWidth = true;
            h.childControlHeight = true;
            var le = row.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            le.preferredHeight = 70;
            le.minHeight = 70;
            return row;
        }

        static void Btn(RectTransform row, string label, UnityEngine.Events.UnityAction action, float flex = 1f)
        {
            var b = Ui.Button(row, label, label, 22, new Color(0.35f, 0.38f, 0.5f), action);
            b.Label.color = Color.white;
            var le = b.Image.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            le.flexibleWidth = flex;
        }

        void SetZone(int z)
        {
            if (m_Game.Balance == null) return;
            m_Zone = Mathf.Clamp(z, 1, m_Game.Balance.ZoneCount);
        }

        void GotoZone()
        {
            if (!RequireBalance()) return;
            if (m_Game.Session.IsDebugSlot)
            {
                m_Game.Session.DebugGotoZone(m_Zone);
                m_Game.RebuildBoard();
                m_Game.SaveNow();
            }
            else m_Game.SwitchSlot(SaveSlots.Debug, true, m_Zone);
        }

        void SetSpeed(int v)
        {
            if (!RequireBalance()) return;
            if (!m_Game.Session.IsDebugSlot && v != 1)
            {
                m_Game.Hud.Toast("Ускорение — только в слоте Debug");
                return;
            }
            m_Game.SimSpeed = v;
        }

        /// <summary>Баланс загружен и сессия есть (после фатальной ошибки загрузки инструменты недоступны).</summary>
        bool RequireBalance()
        {
            if (m_Game.Balance != null && m_Game.Session != null) return true;
            m_Game.Hud.Toast("Баланс не загружен — инструменты недоступны");
            return false;
        }

        /// <summary>Инструменты работают только в слоте Debug (ТЗ 17): в Main — подсказка и отказ.</summary>
        bool RequireDebugSlot()
        {
            if (!RequireBalance()) return false;
            if (m_Game.Session.IsDebugSlot) return true;
            m_Game.Hud.Toast("Инструменты работают только в слоте Debug");
            return false;
        }

        void DebugAction(System.Action<GameSession> action)
        {
            if (!RequireDebugSlot()) return;
            action(m_Game.Session);
            m_Game.MarkDirty();
        }

        void ToggleGrid()
        {
            if (!RequireDebugSlot()) return;
            m_GridOn = !m_GridOn;
            if (m_GridOn)
            {
                m_NextGrid = 0f;
                m_Game.Hud.Toast("Сетка: зелёный — рабочие клетки, синий — появление, жёлтый — якоря, оранжевый — footprint, " +
                                 "голубой — SpawnPad, красный — ворота, тон — сектора (тёмный — закрыт)", 6f);
            }
        }

        // ───────────── «+золото» ─────────────

        void GiveRichGold(GameSession s)
        {
            var gold = RichGold(s, out bool forge, out string source);
            s.DebugAddGold(gold);
            m_Game.Hud.Toast(forge
                ? "Шахт для расчёта нет (или все на максимуме) — взята цена кузницы: +" + NumberFormat.Format(gold) + " = 10 × цена " + source
                : "+" + NumberFormat.Format(gold) + " золота = 10 × цена " + source, 4f);
        }

        /// <summary>
        /// «Золото = 10 × цена следующего улучшения самой дорогой шахты» (ТЗ 17): максимум цены L → L+1 по шахтам зоны;
        /// ещё не производящая шахта считается с уровня 1, шахта на максимальном уровне пропускается, нижней границы нет.
        /// Если шахт нет или все на максимуме — цена кузницы (forge = true).
        /// </summary>
        static GameNumber RichGold(GameSession s, out bool forge, out string source)
        {
            forge = false;
            source = null;
            var best = GameNumber.Zero;
            bool found = false;
            foreach (var shaft in s.Layout.Shafts)
            {
                int ord = shaft.Ordinal;
                int level = 1;
                if (s.IsGenerator(ord))
                {
                    level = s.Mine.Objects[ord].Gen.Level;
                    if (level >= s.MaxGeneratorLevel(ord)) continue;
                }
                var cost = s.UpgradeCost(ord, level);
                if (found && cost <= best) continue;
                best = cost;
                found = true;
                source = "шахты «" + PresentationCatalog.ShaftName(shaft.TypeId) + "» ур. " + level + " → " + (level + 1) + " (" + NumberFormat.Format(cost) + ")";
            }
            if (!found)
            {
                forge = true;
                int cart = s.Layout.Cart.Ordinal;
                int level = s.IsGenerator(cart) ? s.Mine.Objects[cart].Gen.Level : 1;
                level = Mathf.Max(1, Mathf.Min(level, s.MaxGeneratorLevel(cart) - 1));
                best = s.UpgradeCost(cart, level);
                source = "кузницы ур. " + level + " → " + (level + 1) + " (" + NumberFormat.Format(best) + ")";
            }
            return best * GameNumber.FromDouble(10);
        }

        // ───────────── Валидатор в плеере (ТЗ 5.1: проверка в IL2CPP-сборке) ─────────────

        static string Backend
        {
            get
            {
#if ENABLE_IL2CPP
                return "IL2CPP";
#else
                return "Mono";
#endif
            }
        }

        /// <summary>
        /// Запустить BalanceValidator на загруженном балансе в плеере: итог в окне и в статусе панели, строка
        /// «[CatMiners] PlayerValidation …» в логе (adb logcat -s Unity), отчёт validation-player.json в persistentDataPath.
        /// </summary>
        void RunPlayerValidation()
        {
            var balance = m_Game.Balance;
            if (balance == null)
            {
                Debug.LogError("[CatMiners] PlayerValidation skipped: balance not loaded; platform=" + Application.platform +
                               " unity=" + Application.unityVersion + " backend=" + Backend);
                m_Game.Hud.Toast("Баланс не загружен — валидатор недоступен");
                return;
            }
            if (!RequireDebugSlot()) return;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            ValidationReport report;
            try
            {
                report = BalanceValidator.Validate(balance);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[CatMiners] PlayerValidation crashed: " + e);
                m_Game.Hud.Toast("Валидатор упал: " + e.Message);
                return;
            }
            sw.Stop();
            int warnings = report.Issues.Count - report.ErrorCount;
            bool hashOk = balance.Sha256 == BalanceRepository.ExpectedSha256;
            long textAssetBytes = -1;
            var boot = FindAnyObjectByType<GameBootstrap>();
            if (boot != null && boot.BalanceJson != null) textAssetBytes = boot.BalanceJson.bytes.Length;

            string path = Path.Combine(Application.persistentDataPath, PlayerValidationFile);
            var json = new StringBuilder();
            json.Append("{\n  \"sha256\": ").Append(Str(balance.Sha256)).Append(",\n");
            json.Append("  \"sha256MatchesExpected\": ").Append(hashOk ? "true" : "false").Append(",\n");
            json.Append("  \"sizeBytes\": ").Append(balance.SizeBytes.ToString(Inv)).Append(",\n");
            json.Append("  \"textAssetBytes\": ").Append(textAssetBytes.ToString(Inv)).Append(",\n");
            json.Append("  \"zones\": ").Append(report.Zones.Count.ToString(Inv)).Append(",\n");
            json.Append("  \"errors\": ").Append(report.ErrorCount.ToString(Inv)).Append(",\n");
            json.Append("  \"warnings\": ").Append(warnings.ToString(Inv)).Append(",\n");
            json.Append("  \"firstIssues\": [");
            for (int i = 0; i < report.Issues.Count && i < 20; i++) json.Append(i == 0 ? "" : ", ").Append(Str(report.Issues[i].ToString()));
            json.Append("],\n");
            json.Append("  \"durationMs\": ").Append(sw.ElapsedMilliseconds.ToString(Inv)).Append(",\n");
            json.Append("  \"backend\": ").Append(Str(Backend)).Append(",\n");
            json.Append("  \"platform\": ").Append(Str(Application.platform.ToString())).Append(",\n");
            json.Append("  \"isDebugBuild\": ").Append(Debug.isDebugBuild ? "true" : "false").Append(",\n");
            json.Append("  \"unityVersion\": ").Append(Str(Application.unityVersion)).Append(",\n");
            json.Append("  \"appVersion\": ").Append(Str(Application.version)).Append(",\n");
            json.Append("  \"checkedAtUtc\": ").Append(Str(System.DateTime.UtcNow.ToString("o", Inv))).Append("\n}\n");
            string written = path;
            try
            {
                File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
            }
            catch (System.Exception e)
            {
                written = "не записан (" + e.Message + ")";
            }

            string summary = "Зон: " + report.Zones.Count + ", ошибок: " + report.ErrorCount + ", предупреждений: " + warnings + ", SHA-256: " + balance.Sha256;
            Debug.Log("[CatMiners] PlayerValidation zones=" + report.Zones.Count + " errors=" + report.ErrorCount + " warnings=" + warnings +
                      " sha256=" + balance.Sha256 + " hashOk=" + hashOk + " platform=" + Application.platform + " unity=" + Application.unityVersion +
                      " backend=" + Backend + " ms=" + sw.ElapsedMilliseconds + " file=" + written);
            m_LastValidation = "Валидатор (" + Backend + "): зон " + report.Zones.Count + ", ошибок " + report.ErrorCount + ", предупреждений " + warnings;
            m_Game.Hud.Modal("Валидатор баланса",
                summary + "\n" + (hashOk ? "Hash совпадает с эталоном" : "Hash НЕ совпадает с эталоном") + "\n" +
                Application.platform + ", Unity " + Application.unityVersion + ", " + Backend + ", " + sw.ElapsedMilliseconds + " мс\n" +
                "Отчёт: " + written,
                new Hud.ModalButton { Label = "Закрыть" });
        }

        static string Str(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", Inv));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        // ───────────── Цепочка расчёта (ТЗ 17: база → правило Приложения C и аргумент → модификаторы → результат) ─────────────

        void ShowFormulaChain()
        {
            var s = m_Game.Session;
            int ord = m_Game.SelectedOrdinal;
            var sb = new StringBuilder();
            string title;
            var cat = m_Game.SelectedCatId > 0 ? s.FindCat(m_Game.SelectedCatId) : null;
            if (cat != null)
            {
                title = "котик";
                CatChain(s, cat, sb);
            }
            else if (ord >= 0 && ord < s.Layout.Objects.Count)
            {
                var obj = s.Layout.Objects[ord];
                title = PresentationCatalog.ObjectName(obj);
                ObjectHeader(s, obj, sb);
                if (s.IsGenerator(ord)) GeneratorChain(s, ord, sb);
                else if (obj.Kind == ObjectKind.Rock) RockChain(s, obj, sb);
                else if (obj.Kind == ObjectKind.Gate) GateChain(s, obj, sb);
                else if (obj.Kind == ObjectKind.Exit) ExitChain(s, obj, sb);
                else if (obj.Kind == ObjectKind.Shaft) InactiveShaftChain(s, obj, sb);
            }
            else
            {
                title = "найм и общие правила";
                HireChain(s, sb);
            }
            ShowInfo("Цепочка расчёта: " + title, sb.ToString().TrimEnd('\n'));
        }

        static string D(double v) => v.ToString("0.######", Inv);
        static string G(GameNumber v) => NumberFormat.Format(v);

        /// <summary>Форма F-0…F-3 с подставленными коэффициентами (подпись; значение считает Formula.Evaluate).</summary>
        static string Shape(int ft, double b, double m, double g, double x, double p, double r)
        {
            switch (ft)
            {
                case 0: return D(b) + " + " + D(m) + "·" + D(x) + " + " + D(g) + "·" + D(x) + "²";
                case 1: return D(b) + " + " + D(m) + "·" + D(g) + "^(" + D(x) + " − 1)";
                case 2: return D(b) + "·" + D(g) + "^(" + D(x) + " − 1) + " + D(m) + "·(" + D(x) + " − 1)";
                case 3: return "R(" + D(b) + " + " + D(m) + "·(" + D(g) + "·" + D(x) + ")^" + D(p) + ", " + D(r) + ")";
                default: return "?";
            }
        }

        /// <summary>Строка «подпись: F-t (x = …) = форма = результат», результат — Formula.Evaluate по полям DTO.</summary>
        static GameNumber Eval(StringBuilder sb, string label, int ft, double b, double m, double g, double x, double p = 0, double r = 0)
        {
            var v = Formula.Evaluate(ft, b, m, g, x, p, r);
            sb.Append(label).Append(": F-").Append(ft).Append(" (x = ").Append(D(x)).Append(") = ").Append(Shape(ft, b, m, g, x, p, r))
              .Append(" = ").Append(G(v)).Append('\n');
            return v;
        }

        static void ObjectHeader(GameSession s, ZoneObject obj, StringBuilder sb)
        {
            var st = s.Mine.Objects[obj.Ordinal];
            sb.Append('[').Append(s.ZoneId).Append(':').Append(obj.Index).Append("] токен ").Append(obj.Token).Append(" · ordinal ").Append(obj.Ordinal)
              .Append(" · якорь (").Append(obj.Col).Append(", ").Append(obj.Row).Append("), ").Append(obj.Width).Append('×').Append(obj.Depth)
              .Append(" · ").Append(st.Status).Append('\n');
            if (obj.IsTarget && !s.IsGenerator(obj.Ordinal) && s.Balance.HasHpTable(obj.TypeId))
            {
                sb.Append("HP = MiningTargetHealths.").Append(obj.TypeId).Append('[').Append(obj.Level).Append(" − 1] = ")
                  .Append(G(s.Balance.TargetHp(obj.TypeId, obj.Level))).Append(", осталось ").Append(G(st.Hp));
                int workers = s.WorkersOn(obj.Ordinal);
                if (workers > 0) sb.Append(", котиков ").Append(workers).Append(", ETA ").Append(NumberFormat.FormatDuration(s.EtaSeconds(obj.Ordinal)));
                sb.Append('\n');
            }
        }

        static void CatChain(GameSession s, CatState cat, StringBuilder sb)
        {
            var m = s.Balance.Miner;
            int L = cat.Level;
            sb.Append("Котик #").Append(cat.Id).Append(", уровень L = ").Append(L).Append('\n');
            Eval(sb, "Урон (E-02)", m.DamageFormulaType, m.DamageBase, m.DamageMultiplier, m.DamageGrowth, L);
            Eval(sb, "Задержка атаки, с", m.AttackDelaySecFormulaType, m.AttackDelaySecBase, m.AttackDelaySecMultiplier, m.AttackDelaySecGrowth, L);
            Eval(sb, "Шанс крита", m.CritChanceFormulaType, m.CritChanceBase, m.CritChanceMultiplier, m.CritChanceGrowth, L);
            Eval(sb, "Сила крита", m.CritPowerFormulaType, m.CritPowerBase, m.CritPowerMultiplier, m.CritPowerGrowth, L);
            sb.Append("Урон в секунду (без критов) = ").Append(G(s.CatDps(L))).Append('\n');
            if (cat.TargetIndex >= 0)
            {
                int ord = s.OrdinalOf(cat.TargetIndex);
                if (ord >= 0) sb.Append("Цель: ").Append(PresentationCatalog.ObjectName(s.Layout.Objects[ord])).Append(" [").Append(cat.TargetIndex).Append(']').Append('\n');
            }
            else sb.Append("Цели нет\n");
        }

        static void GeneratorChain(GameSession s, int ord, StringBuilder sb)
        {
            var obj = s.Layout.Objects[ord];
            var gen = s.Mine.Objects[ord].Gen;
            var def = s.DefFor(obj);
            int L = gen.Level;
            int k = Mathf.Min(gen.ThresholdsReached, def.Thresholds.Length);
            bool auto = s.IsAutoGenerator(ord);
            sb.Append("Генератор ").Append(def.Id).Append(", уровень ").Append(L).Append(" из ").Append(def.MaxLevel)
              .Append(auto ? ", автоматический (входит в ставку E-10)" : ", ручной (одна выплата, E-18)").Append('\n');
            Eval(sb, "Выход за цикл (E-03)", def.OutFt, def.OutB, def.OutM, def.OutG, L);
            sb.Append("× пороги (E-04): пройдено ").Append(k).Append(" из ").Append(def.Thresholds.Length).Append(" = ×").Append(G(def.Products[k])).Append('\n');
            sb.Append("× manager-карты (код 9, E-07) = ×").Append(G(s.ManagerMultiplier(def.Id))).Append('\n');
            sb.Append("× ворота и карты (код 10, E-08) = ×").Append(G(s.GlobalIncomeMultiplier())).Append('\n');
            sb.Append("= ").Append(G(s.GeneratorOutput(ord))).Append(" золота за цикл\n");
            Eval(sb, "Время цикла, с (E-03)", def.DelayFt, def.DelayB, def.DelayM, def.DelayG, L);
            sb.Append("В симуляции цикл = ").Append(D(GameSession.TicksToSeconds(s.GeneratorCycleTicks(ord)))).Append(" с\n");

            if (k < def.Thresholds.Length)
            {
                var o = def.Objectives;
                double zoneMult = s.Balance.ZoneMultipliers(s.Mine.Zone).GenObjectiveSoftCurrencyMultiplier;
                sb.Append("Следующий порог T").Append(k + 1).Append(" = уровень ").Append(s.NextThresholdLevel(ord)).Append(": выход ×")
                  .Append(D(o.CoreCurrencyMultiplier[k])).Append('\n');
                var raw = Eval(sb, "Эликсир за порог k = " + (k + 1) + " (E-04)", o.SoftCurrencyRewardFormulaType, o.SoftCurrencyRewardBase,
                    o.SoftCurrencyRewardMultiplier, o.SoftCurrencyRewardGrowth, k + 1);
                sb.Append("= max(1, floor(").Append(G(raw)).Append(" × GenObjectiveSoftCurrencyMultiplier ").Append(D(zoneMult)).Append(")) = ")
                  .Append(G(Formula.Payout(raw * GameNumber.FromDouble(zoneMult)))).Append(" эликсира\n");
            }
            else sb.Append("Все пороги пройдены\n");

            if (L < def.MaxLevel)
            {
                var rawCost = Eval(sb, "Цена " + L + " → " + (L + 1) + " (E-03)", def.CostFt, def.CostB, def.CostM, def.CostG, L);
                sb.Append("= ceil(").Append(G(rawCost)).Append("), не меньше 1 = ").Append(G(s.UpgradeCost(ord, L))).Append(" золота\n");
            }
            else sb.Append("Максимальный уровень: улучшений нет\n");
            if (obj.Kind == ObjectKind.Shaft && !gen.Automated) sb.Append(s.AutomationRequirement(ord)).Append('\n');
            IncomeRate(s, sb);
        }

        static void IncomeRate(GameSession s, StringBuilder sb)
        {
            sb.Append("Ставка наград (E-10) = max(доход кузницы и автоматических шахт ").Append(G(s.AutoIncomePerSecond())).Append("/с, NoGeneratorsCurrencyRate ")
              .Append(D(s.Balance.Props.NoGeneratorsCurrencyRate)).Append(") = ").Append(G(s.GetRewardIncomeRate())).Append("/с\n");
        }

        static void RockChain(GameSession s, ZoneObject obj, StringBuilder sb)
        {
            var rock = s.Balance.Rocks[obj.TypeId];
            var cycle = rock.RewardCycle;
            if (cycle == null || cycle.Length == 0)
            {
                sb.Append("Цикла наград нет\n");
                return;
            }
            s.Player.RewardCycle.TryGetValue(rock.Id, out long counter);
            int next = (int)(counter % cycle.Length);
            sb.Append("Награды (E-09): сквозной счётчик ").Append(rock.Id).Append(" = ").Append(counter).Append(", следующая запись [")
              .Append(next).Append('/').Append(cycle.Length).Append("], уровень камня x = ").Append(obj.Level).Append('\n');
            var shown = new HashSet<string>();
            for (int i = 0; i < cycle.Length; i++)
            {
                sb.Append(i == next ? "→ [" : "   [").Append(i).Append("] ");
                RockReward(s, rock, obj.Level, cycle[i], sb, shown);
            }
        }

        /// <summary>Одна запись цикла наград камня в порядке GrantRockReward: сундук, Core, Soft, Hard, иначе исключённая система.</summary>
        static void RockReward(GameSession s, RockDto rock, int level, RewardModelDto e, StringBuilder sb, HashSet<string> shown)
        {
            if (e == null)
            {
                sb.Append("пустая запись\n");
                return;
            }
            bool full = shown.Add(e.RewardType + ":" + e.DetailedType);
            if (e.RewardType == RewardTypes.Gacha)
            {
                sb.Append("сундук ").Append(e.DetailedType).Append('\n');
                if (full) ChestChain(s, e.DetailedType, sb, "      ");
                return;
            }
            switch (e.DetailedType)
            {
                case "Core":
                {
                    var seconds = Formula.Ft3Number(rock.CoreCurrencySecondsBase, rock.CoreCurrencySecondsMultiplier, rock.CoreCurrencySecondsGrowth,
                        rock.CoreCurrencySecondsPower, rock.CoreCurrencySecondsRound, level);
                    var rate = s.GetRewardIncomeRate();
                    sb.Append("золото = max(1, floor(секунды ").Append(G(seconds)).Append(" × ставка ").Append(G(rate)).Append(")) = ")
                      .Append(G(Formula.Payout(seconds * rate))).Append('\n');
                    if (full)
                    {
                        Eval(sb, "      секунды CoreCurrencySeconds* (FormulaType в данных " + rock.CoreCurrencySecondsFormulaType + ")", 3,
                            rock.CoreCurrencySecondsBase, rock.CoreCurrencySecondsMultiplier, rock.CoreCurrencySecondsGrowth, level,
                            rock.CoreCurrencySecondsPower, rock.CoreCurrencySecondsRound);
                        sb.Append("      ");
                        IncomeRate(s, sb);
                    }
                    break;
                }
                case "Soft":
                {
                    var raw = Formula.Evaluate(rock.SoftCurrencyFormulaType, rock.SoftCurrencyBase, rock.SoftCurrencyMultiplier, rock.SoftCurrencyGrowth, level);
                    double mult = s.Balance.ZoneMultipliers(s.Mine.Zone).MiningSoftCurrencyMultiplier;
                    sb.Append("эликсир = max(1, floor(").Append(G(raw)).Append(" × MiningSoftCurrencyMultiplier ").Append(D(mult)).Append(")) = ")
                      .Append(G(Formula.Payout(raw * GameNumber.FromDouble(mult)))).Append('\n');
                    if (full) Eval(sb, "      SoftCurrency*", rock.SoftCurrencyFormulaType, rock.SoftCurrencyBase, rock.SoftCurrencyMultiplier, rock.SoftCurrencyGrowth, level);
                    break;
                }
                case "Hard":
                    sb.Append("кристаллы = Quantity = ").Append(D(e.Quantity)).Append('\n');
                    break;
                default:
                    sb.Append(e.DetailedType).Append(" ×").Append(D(e.Quantity)).Append(": не выдаётся (исключённая система, E-09), только учёт\n");
                    break;
            }
        }

        /// <summary>Сундук (E-11…E-13): данные записи Gacha и результат, если открыть его следующим (копия ГСЧ сундуков, состояние не меняется).</summary>
        static void ChestChain(GameSession s, string gachaId, StringBuilder sb, string indent)
        {
            if (string.IsNullOrEmpty(gachaId) || !s.Balance.Gacha.TryGetValue(gachaId, out var g))
            {
                sb.Append(indent).Append("сундук ").Append(string.IsNullOrEmpty(gachaId) ? "—" : gachaId).Append(": нет в данных\n");
                return;
            }
            bool exitMin = g.GuaranteedCardMin != null && g.GuaranteedCardMin.Length > 0;
            sb.Append(indent).Append("Сундук ").Append(g.Id).Append(" «").Append(PresentationCatalog.GachaName(s.Balance, g.Id)).Append("», GachaType ")
              .Append(g.GachaType).Append(", BaseNumCards ").Append(g.BaseNumCards).Append(", эликсир ")
              .Append(exitMin || g.SoftCurrencyMax <= g.SoftCurrencyMin ? D(g.SoftCurrencyMin) : "[" + D(g.SoftCurrencyMin) + "; " + D(g.SoftCurrencyMax) + "]").Append('\n');
            if (g.GuaranteedCardIds != null && g.GuaranteedCardIds.Length > 0)
            {
                sb.Append(indent).Append(exitMin ? "Гарантированные (E-13: GuaranteedCardMin и SoftCurrencyMin, CardMultMax не применяется): " : "Гарантированные: ");
                for (int i = 0; i < g.GuaranteedCardIds.Length; i++)
                {
                    int count = exitMin ? g.GuaranteedCardMin[i] : (g.GuaranteedCardCounts != null && i < g.GuaranteedCardCounts.Length ? g.GuaranteedCardCounts[i] : 0);
                    sb.Append(i == 0 ? "" : ", ").Append(g.GuaranteedCardIds[i]).Append('×').Append(count);
                }
                sb.Append('\n');
            }
            try
            {
                var src = s.Player.Rng.Gacha;
                var rng = new Pcg32 { State = src.State, Inc = src.Inc };
                var r = GachaService.Open(s.Balance, g, s.BuildGachaContext(), rng);
                sb.Append(indent).Append("Если открыть следующим: эликсир ").Append(G(r.Elixir)).Append(", карты: ");
                if (r.Cards.Count == 0) sb.Append("нет");
                for (int i = 0; i < r.Cards.Count; i++) sb.Append(i == 0 ? "" : ", ").Append(r.Cards[i].CardId).Append('×').Append(r.Cards[i].Count);
                sb.Append('\n');
            }
            catch (System.Exception e)
            {
                sb.Append(indent).Append("Предпросмотр недоступен: ").Append(e.Message).Append('\n');
            }
        }

        static void GateChain(GameSession s, ZoneObject obj, StringBuilder sb)
        {
            sb.Append("Ворота ").Append(obj.GateOrder + 1).Append(" из ").Append(s.Layout.Gates.Count).Append(" (снизу), открыто ворот: ").Append(s.Mine.GatesOpened).Append('\n');
            sb.Append("Бонусы одних ворот (E-08, CheckPoint, x = 1):\n");
            var cp = s.Balance.CheckPoint;
            for (int i = 0; i < cp.StatModifierType.Length; i++)
            {
                int code = cp.StatModifierType[i];
                bool applied = code == 10 || code == 2 || code == 7 || code == 1;
                Eval(sb, "  код " + code + (applied ? "" : " (в v1 не применяется)"), cp.ModifierFormulaType[i], cp.ModifierBase[i], cp.ModifierMultiplier[i],
                    cp.ModifierGrowth[i], 1, cp.ModifierPower[i], cp.ModifierRound[i]);
            }
            sb.Append("Итого сейчас (ворота и карты):\n");
            sb.Append("  код 10 — выход генераторов: ").Append(G(s.CheckpointValue(10))).Append(" за ворота → ×").Append(G(s.GlobalIncomeMultiplier())).Append('\n');
            sb.Append("  код 2 — цена найма: ÷").Append(G(s.CheckpointValue(2))).Append(" за ворота → ÷").Append(G(s.HireDivisor())).Append('\n');
            sb.Append("  код 7 — бесплатный котик: −").Append(G(s.CheckpointValue(7))).Append(" с за ворота → интервал ").Append(D(s.FreeCatIntervalSeconds())).Append(" с\n");
            sb.Append("  код 1 — лимит котиков: +").Append(G(s.CheckpointValue(1))).Append(" за ворота → ").Append(s.UnitCap()).Append('\n');
            sb.Append("Награда за открытие:\n");
            ChestChain(s, obj.RewardId, sb, "  ");
        }

        static void ExitChain(GameSession s, ZoneObject obj, StringBuilder sb)
        {
            sb.Append("Награда выхода (E-13, выдаётся в CompleteZone):\n");
            ChestChain(s, obj.RewardId, sb, "  ");
            sb.Append(s.CanCompleteZone(out string reason) ? "Переход доступен" : "Переход недоступен: " + reason)
              .Append("; осталось камней ").Append(s.RemainingRocks()).Append(", сундучных ").Append(s.RemainingChestRocks()).Append('\n');
        }

        static void InactiveShaftChain(GameSession s, ZoneObject obj, StringBuilder sb)
        {
            int ord = obj.Ordinal;
            sb.Append("Шахта «").Append(PresentationCatalog.ShaftName(obj.TypeId)).Append("» (").Append(obj.TypeId).Append(") ещё не производит\n");
            sb.Append("Награда за активацию:\n");
            ChestChain(s, obj.RewardId, sb, "  ");
            sb.Append(s.AutomationRequirement(ord)).Append('\n');
            var def = s.DefFor(obj);
            Eval(sb, "После активации: выход ур. 1 (E-03)", def.OutFt, def.OutB, def.OutM, def.OutG, 1);
            Eval(sb, "Время цикла ур. 1, с", def.DelayFt, def.DelayB, def.DelayM, def.DelayG, 1);
            var rawCost = Eval(sb, "Цена 1 → 2 (E-03)", def.CostFt, def.CostB, def.CostM, def.CostG, 1);
            sb.Append("= ceil(").Append(G(rawCost)).Append(") = ").Append(G(s.UpgradeCost(ord, 1))).Append(" золота\n");
        }

        static void HireChain(GameSession s, StringBuilder sb)
        {
            var r = s.Balance.Reinforcement;
            int n = s.Mine.HireCount;
            sb.Append("Найм (E-05): n = ").Append(n).Append(" покупок в зоне\n");
            var raw = Eval(sb, "Цена, x = n + 1", r.CostFormulaType, r.CostBase, r.CostMultiplier, r.CostGrowth, n + 1);
            sb.Append("= ceil(").Append(G(raw)).Append(" ÷ делитель ворот (код 2) ").Append(G(s.HireDivisor())).Append("), не меньше 1 = ")
              .Append(G(s.HirePrice())).Append(" золота\n");
            var gauge = Eval(sb, "Шкала уровня, x = n", r.LevelFormulaType, r.LevelBase, r.LevelMultiplier, r.LevelGrowth, n);
            sb.Append("Уровень найма L_next = 1 + floor(").Append(G(gauge)).Append(") = ").Append(s.HireLevel()).Append('\n');

            var props = s.Balance.Props;
            sb.Append("Лимит котиков (код 1) = BaseUnitCap ").Append(props.BaseUnitCap).Append(" + ворота ").Append(G(s.CheckpointValue(1))).Append(" × ")
              .Append(s.Mine.GatesOpened);
            AppendCards(s, 1, sb);
            sb.Append(" = ").Append(s.UnitCap()).Append(", занято ").Append(s.OccupiedSlots).Append('\n');

            var cart = s.Balance.Cart;
            Eval(sb, "Бесплатный котик (E-06): база интервала, с", cart.SpawnDelaySecFormulaType, cart.SpawnDelaySecBase, cart.SpawnDelaySecMultiplier,
                cart.SpawnDelaySecGrowth, 1);
            sb.Append("− ворота (код 7) ").Append(G(s.CheckpointValue(7))).Append(" с × ").Append(s.Mine.GatesOpened);
            AppendCards(s, 7, sb);
            sb.Append(", не меньше ").Append(D(GameSession.MinFreeCatIntervalSeconds)).Append(" с = ").Append(D(s.FreeCatIntervalSeconds())).Append(" с\n");
            sb.Append("Уровень бесплатного котика = max(1, L_next ").Append(s.HireLevel()).Append(" + SpawnLevelOffset ").Append(cart.SpawnLevelOffset)
              .Append(") = ").Append(Mathf.Max(1, s.HireLevel() + cart.SpawnLevelOffset)).Append('\n');

            if (s.Player.Dynamite > 0)
            {
                var sp = s.Balance.Spell;
                int dl = s.DynamiteLevel();
                sb.Append("Динамит (E-16): зарядов ").Append(s.Player.Dynamite).Append(", уровень = максимальный уровень котика = ").Append(dl).Append('\n');
                Eval(sb, "Урон динамита", sp.EffectFormulaType, sp.EffectBase, sp.EffectMultiplier, sp.EffectGrowth, dl);
                sb.Append("В симуляции урон = ").Append(G(s.DynamiteDamage())).Append('\n');
            }
            else sb.Append("Динамита нет (E-16: цепочка появится с первым зарядом)\n");

            int claims = s.Player.DeliveryHistory.ClaimTimes.Count;
            Eval(sb, "Доставки (E-15): интервал без разброса, x = получено за окно + 1", props.DeliveryDelaySecFormulaType, props.DeliveryDelaySecBase,
                props.DeliveryDelaySecMultiplier, props.DeliveryDelaySecGrowth, claims + 1);
            sb.Append("× U[").Append(D(1 - props.DeliveryDelaySecVariance)).Append("; ").Append(D(1 + props.DeliveryDelaySecVariance)).Append("]\n");
            IncomeRate(s, sb);
        }

        /// <summary>Вклад карт с кодом эффекта (E-07, E-08): « + карта id ур. L: значение».</summary>
        static void AppendCards(GameSession s, int code, StringBuilder sb)
        {
            foreach (var c in s.Balance.CardsInOrder)
            {
                if (c.StatModifierType != code) continue;
                var v = s.CardEffectValue(c);
                sb.Append(code == 7 ? " − карта " : " + карта ").Append(c.Id);
                if (v.HasValue) sb.Append(" ур. ").Append(s.CardLevel(c.Id)).Append(": ").Append(G(v.Value));
                else sb.Append(": нет карты (0)");
            }
        }

        // ───────────── Кадр ─────────────

        void Update()
        {
            if (m_Game == null || m_Game.Session == null) return;
            var s = m_Game.Session;
            // Подписи панели — только пока она открыта и только при смене исходных значений: скрытая панель ничего не собирает.
            if (m_Panel != null && m_Panel.gameObject.activeSelf)
            {
                if (m_ZoneLabel != null && m_Zone != m_ZoneShown)
                {
                    m_ZoneShown = m_Zone;
                    m_ZoneLabel.text = "zone" + m_Zone;
                }
                if (m_Status != null && (!m_StatusValid || !ReferenceEquals(m_StatusSlot, s.State.Slot) || m_StatusSpeed != m_Game.SimSpeed ||
                                         m_StatusCat != m_CatLevel || !ReferenceEquals(m_StatusSaveDir, m_Game.SaveDirectory) ||
                                         !ReferenceEquals(m_StatusValidation, m_LastValidation)))
                {
                    m_StatusValid = true;
                    m_StatusSlot = s.State.Slot;
                    m_StatusSpeed = m_Game.SimSpeed;
                    m_StatusCat = m_CatLevel;
                    m_StatusSaveDir = m_Game.SaveDirectory;
                    m_StatusValidation = m_LastValidation;
                    m_Status.text = "Слот " + s.State.Slot + " · ×" + m_Game.SimSpeed + " · котик ур. " + m_CatLevel + " · rules v" + Formula.RulesVersion +
                                    "\nСохранения: " + m_Game.SaveDirectory +
                                    (m_LastValidation != null ? "\n" + m_LastValidation : "");
                }
            }
            if (!s.IsDebugSlot)
            {
                // Инструменты не работают в слоте Main (ТЗ 17): скорость ×1, сетка и окно расчёта закрываются.
                if (m_Game.SimSpeed != 1) m_Game.SimSpeed = 1;
                m_GridOn = false;
                if (m_Info != null && m_Info.gameObject.activeSelf) HideInfo();
            }
            if (m_GridOn && Time.unscaledTime >= m_NextGrid)
            {
                m_NextGrid = Time.unscaledTime + 0.5f;
                DrawGrid();
            }
            else if (!m_GridOn && m_Grid != null) DestroyGrid();
        }

        void DestroyGrid()
        {
            if (m_Grid != null) Destroy(m_Grid);
            m_Grid = null;
            m_GridLayout = null;
            m_GridColors = null;
        }

        static readonly Color32 WorkColor = new Color(0.2f, 1f, 0.3f);
        static readonly Color32 SpawnColor = new Color(0.3f, 0.5f, 1f);
        static readonly Color32 AnchorColor = new Color(1f, 0.9f, 0.2f);
        static readonly Color32 AnchorGoneColor = new Color(0.55f, 0.5f, 0.15f);
        static readonly Color32 FootprintColor = new Color(1f, 0.55f, 0.15f);
        static readonly Color32 FootprintGoneColor = new Color(0.5f, 0.3f, 0.12f);
        static readonly Color32 SpawnPadColor = new Color(0.35f, 0.85f, 0.95f);
        static readonly Color32 GateClosedColor = new Color(1f, 0.2f, 0.2f);
        static readonly Color32 GateOpenedColor = new Color(0.55f, 0.12f, 0.18f);

        /// <summary>
        /// Наложение сетки (ТЗ 17): один квад на клетку, цвет по приоритету — рабочие клетки выбранной цели (зелёные) →
        /// клетка появления (синяя) → якоря объектов (жёлтые) → остальные клетки footprint (оранжевые) → SpawnPad
        /// (голубые, без клеток кузницы) → строки ворот (красные, открытые — тёмно-красные) → тон сектора (оттенок по номеру,
        /// закрытый сектор темнее). Якоря и footprint объектов, которых на поле уже нет (разрушенные камни), приглушены.
        /// Квады пересоздаются только при изменении раскраски.
        /// </summary>
        void DrawGrid()
        {
            var s = m_Game.Session;
            var L = s.Layout;
            var work = new List<int>();
            int sel = m_Game.SelectedOrdinal;
            if (sel >= 0 && sel < L.Objects.Count && L.Objects[sel].IsTarget) s.WorkCells(sel, work);
            int spawn = s.FindSpawnCell();
            s.SpawnPadRows(out int padTop, out int padBottom);
            int cart = L.Cart != null ? L.Cart.Ordinal : -1;
            var openedGateRows = new HashSet<int>();
            foreach (var g in L.Gates)
                if (g.GateOrder >= 0 && g.GateOrder < s.Mine.GatesOpened) openedGateRows.Add(g.Row);

            var colors = new Color32[L.CellCount];
            var sizes = new float[L.CellCount];
            for (int cell = 0; cell < L.CellCount; cell++)
            {
                int owner = L.CellOwner[cell];
                int sector = L.CellSector[cell];
                int row = L.Row(cell);
                bool alive = owner >= 0 && s.ObjectAt(cell) >= 0;
                Color32 c;
                float size;
                if (work.Contains(cell)) { c = WorkColor; size = 0.5f; }
                else if (cell == spawn) { c = SpawnColor; size = 0.6f; }
                else if (owner >= 0 && L.Objects[owner].Index == cell) { c = alive ? AnchorColor : AnchorGoneColor; size = 0.7f; }
                else if (owner >= 0) { c = alive ? FootprintColor : FootprintGoneColor; size = 0.55f; }
                else if (row >= padTop && row <= padBottom && sector >= 0 && owner != cart) { c = SpawnPadColor; size = 0.6f; }
                else if (sector < 0) { c = openedGateRows.Contains(row) ? GateOpenedColor : GateClosedColor; size = 0.85f; }
                else { c = SectorColor(sector, s.IsSectorOpen(sector)); size = 0.85f; }
                colors[cell] = c;
                sizes[cell] = size;
            }
            if (m_Grid != null && m_GridLayout == L && SameColors(m_GridColors, colors)) return;

            if (m_Grid != null) Destroy(m_Grid);
            m_Grid = new GameObject("DevGrid");
            m_GridLayout = L;
            m_GridColors = colors;
            var quad = Meshes.Primitive(PrimitiveType.Quad);
            for (int cell = 0; cell < L.CellCount; cell++)
            {
                var pos = m_Game.Board.CellCenter(cell) + Vector3.up * 0.03f;
                var part = Meshes.Part(m_Grid.transform, "c", quad, Materials.Unlit(colors[cell]), pos, new Vector3(sizes[cell], sizes[cell], 1), Quaternion.Euler(90, 0, 0));
                var mr = part.GetComponent<MeshRenderer>();
                if (mr != null) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        static Color32 SectorColor(int sector, bool open)
        {
            float hue = (sector * 0.618034f) % 1f;
            return Color.HSVToRGB(hue, 0.45f, open ? 0.55f : 0.2f);
        }

        static bool SameColors(Color32[] a, Color32[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (!a[i].Equals(b[i])) return false;
            return true;
        }
    }
}

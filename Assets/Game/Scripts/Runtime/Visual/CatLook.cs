using UnityEngine;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Внешний вид котика (ART_DIRECTION.md, «Котики»): окрас с отметинами — по ID котика, оружие и цвет каски — по уровню,
    /// имена спрайтов кадров. Один источник для поля (<see cref="CatView"/>) и портрета в контекстной панели.
    /// Окрас — только индивидуальность: сила котика определяется уровнем (номер на бейдже, оружие, цвет каски).
    /// Прокачка (решение владельца от 8 октября 2026): оружие меняется с каждым уровнем по кругу из 8 —
    /// кирка, молот, дробилка, циркулярная пила, бур-машина, две кирки, бензопила, лазерган; на каждом новом круге
    /// (уровни 9, 17, 25, …) — новый цвет каски, и оружие снова начинается с кирки.
    /// Имена кадров и порядок — как manifest.CAT_FRAMES в Tools/PixelArt.
    /// </summary>
    public static class CatLook
    {
        /// <summary>Окрасы: рыжий и серый полосатые, чёрный «смокинг», белый калико, кремовый с «пойнтами», коричневый с проточиной.</summary>
        public static readonly string[] Coats = { "ginger", "gray", "black", "white", "cream", "brown" };

        /// <summary>Оружие по уровню внутри круга (manifest.WEAPONS).</summary>
        public static readonly string[] Weapons = { "pick", "hammer", "crusher", "saw", "drill", "dualpick", "chainsaw", "laser" };

        public const int Pick = 0, Hammer = 1, Crusher = 2, Saw = 3, Drill = 4, DualPick = 5, Chainsaw = 6, Laser = 7;

        /// <summary>Цвет каски по номеру круга (manifest.HELMET_COLORS); после восьмого круга цвета повторяются.</summary>
        public static readonly string[] HelmetColors = { "yellow", "blue", "red", "green", "purple", "teal", "pink", "white" };

        /// <summary>Уровней в круге оружия.</summary>
        public const int Cycle = 8;

        // Кадры (Tools/PixelArt/manifest.py, CAT_FRAMES): индексы групп.
        public const int Idle = 0, IdleCount = 6;
        public const int Blink = 6, BlinkDown = 7;
        public const int Stretch = 8, StretchCount = 10;
        public const int Walk = 18, WalkCount = 8;
        public const int MineReady = 26, MineReady2 = 27, MineAntic = 28, MineRaise = 29, MineTop = 30, MineStrike = 31,
            MineImpact = 32, MineRecoil = 33, MineReturn = 34;
        public const int Drag = 35, DragCount = 4;
        public const int Spawn = 39, SpawnCount = 8;
        public const int Joy = 47, JoyCount = 6;
        /// <summary>Добыча с упора (пила, бур, бензопила, лазер): те же фазы, что у mine_*, в том же порядке.</summary>
        public const int ToolReady = 53, ToolCount = 9;
        public const int FrameCount = 62;

        /// <summary>Имена кадров в порядке индексов.</summary>
        public static readonly string[] Frames = BuildFrames();

        static string[,] s_Body, s_Helmet, s_Weapon;

        static string[] BuildFrames()
        {
            var f = new string[FrameCount];
            for (int i = 0; i < IdleCount; i++) f[Idle + i] = "idle" + i;
            f[Blink] = "blink";
            f[BlinkDown] = "blink_d";
            for (int i = 0; i < StretchCount; i++) f[Stretch + i] = "stretch" + i;
            for (int i = 0; i < WalkCount; i++) f[Walk + i] = "walk" + i;
            string[] mine = { "ready", "ready2", "antic", "raise", "top", "strike", "impact", "recoil", "return" };
            for (int i = 0; i < mine.Length; i++)
            {
                f[MineReady + i] = "mine_" + mine[i];
                f[ToolReady + i] = "tool_" + mine[i];
            }
            for (int i = 0; i < DragCount; i++) f[Drag + i] = "drag" + i;
            for (int i = 0; i < SpawnCount; i++) f[Spawn + i] = "spawn" + i;
            for (int i = 0; i < JoyCount; i++) f[Joy + i] = "joy" + i;
            return f;
        }

        static void EnsureNames()
        {
            if (s_Body != null) return;
            s_Body = new string[Coats.Length, FrameCount];
            s_Helmet = new string[HelmetColors.Length, FrameCount];
            s_Weapon = new string[Weapons.Length, FrameCount];
            for (int f = 0; f < FrameCount; f++)
            {
                for (int c = 0; c < Coats.Length; c++) s_Body[c, f] = "cat_body_" + Coats[c] + "_" + Frames[f];
                for (int h = 0; h < HelmetColors.Length; h++) s_Helmet[h, f] = "cat_helmet_" + HelmetColors[h] + "_" + Frames[f];
                for (int w = 0; w < Weapons.Length; w++) s_Weapon[w, f] = "cat_weapon_" + Weapons[w] + "_" + Frames[f];
            }
        }

        /// <summary>Окрас котика по его ID (стабилен после загрузки сохранения).</summary>
        public static int Coat(int catId) => Mathf.Abs(catId * 7 + 3) % Coats.Length;

        /// <summary>Оружие уровня: 1 — кирка, 2 — молот, …, 8 — лазерган, 9 — снова кирка (новый цвет каски).</summary>
        public static int Weapon(int level) => (Mathf.Max(1, level) - 1) % Cycle;

        /// <summary>Цвет каски уровня: уровни 1–8 — первый цвет, 9–16 — второй и так далее по кругу цветов.</summary>
        public static int HelmetColor(int level) => (Mathf.Max(1, level) - 1) / Cycle % HelmetColors.Length;

        /// <summary>Инструмент «с упора» (пила, бур, бензопила, лазер): добыча — кадры tool_*, без замаха над головой.</summary>
        public static bool IsTool(int weapon) => weapon == Saw || weapon == Drill || weapon == Chainsaw || weapon == Laser;

        /// <summary>Кадр с учётом оружия: у инструмента кадры добычи mine_* заменяются кадрами tool_* той же фазы.</summary>
        public static int ForWeapon(int frame, int weapon) =>
            IsTool(weapon) && frame >= MineReady && frame <= MineReturn ? frame - MineReady + ToolReady : frame;

        public static string Body(int coat, int frame)
        {
            EnsureNames();
            return s_Body[coat, frame];
        }

        public static string Helmet(int colour, int frame)
        {
            EnsureNames();
            return s_Helmet[colour, frame];
        }

        /// <summary>Спрайт оружия кадра. Кадр — уже с учётом <see cref="ForWeapon"/> (у ударного оружия нет кадров tool_*).</summary>
        public static string WeaponSprite(int weapon, int frame)
        {
            EnsureNames();
            return s_Weapon[weapon, frame];
        }
    }
}

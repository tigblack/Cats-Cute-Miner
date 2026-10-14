using System.Collections.Generic;

namespace CatMiners.Core.Text
{
    public static partial class Loc
    {
        /// <summary>
        /// Раздел Board: надписи на поле: уровни, статусы объектов, подписи ящиков и котиков (Runtime/Visual).
        /// Ключи начинаются с «board.»; строки добавляются через d.Add("board.группа.имя", "Текст").
        /// </summary>
        static partial void RegisterBoard(Dictionary<string, string> d)
        {
            // Подпись над целью: уровень, работа и ETA (BoardView). {0} — уровень или уже собранная подпись уровня.
            d.Add("board.info.level", "Lv. {0}");
            d.Add("board.info.noWorkers", "{0}\nNo workers");   // две строки: подпись не шире клетки
            // Без «~» и «≈»: в Tiny5 тильда — ровная черта (читается как минус), «≈» — клетчатое пятно.
            d.Add("board.info.eta", "{0}\n{1}");           // {1} — NumberFormat.FormatDuration; две строки: соседние подписи не налезают

            // Статус шахты, ворот и выхода (BoardView).
            d.Add("board.status.activate", "Activate!");
            d.Add("board.status.collect", "Collect");
            d.Add("board.status.auto", "Auto");
            d.Add("board.status.openGate", "Open!");
            d.Add("board.status.exitOpen", "Exit open");

            // Подписи бочек и ящиков на поле (ContainerView в Views.cs).
            d.Add("board.container.cat", "Cat {0}");     // {0} — уровень котика
            d.Add("board.container.dynamite", "Dynamite");
            d.Add("board.container.elixir", "Elixir {0}"); // {0} — NumberFormat.Format(сумма)
            d.Add("board.container.gold", "Gold {0}");
        }
    }
}

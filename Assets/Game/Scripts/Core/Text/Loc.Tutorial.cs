using System.Collections.Generic;

namespace CatMiners.Core.Text
{
    public static partial class Loc
    {
        /// <summary>
        /// Раздел Tutorial: шаги и подсказки обучения (TutorialController).
        /// Ключи начинаются с «tutorial.»; строки добавляются через d.Add("tutorial.группа.имя", "Текст").
        /// </summary>
        static partial void RegisterTutorial(Dictionary<string, string> d)
        {
            // zone1, реплики наставника: найм (бочка), открыть бочку, котик на камень, добыча с камня, второй котик.
            d.Add("tutorial.hint.hire", "Spend gold to hire a miner cat");
            d.Add("tutorial.hint.open_barrel", "Tap the barrel to open it");
            d.Add("tutorial.hint.drag_to_rock", "Drag the cat onto a rock — it will start breaking it");
            d.Add("tutorial.hint.collect_loot", "Tap to collect the loot from the rock");
            d.Add("tutorial.hint.hire_second", "Hire a second cat");

            // Шаги 4–6: объединение, кузница, путь к выходу.
            d.Add("tutorial.hint.merge", "Merge cats of <color=#D9771F>the same level</color> to make them stronger!");
            d.Add("tutorial.hint.merged_mine", "Merged cats dig faster! Send the cat to a rock");
            d.Add("tutorial.hint.upgrade_forge", "Upgrade the forge to earn more gold! Tap it");
            d.Add("tutorial.hint.upgrade_forge_spend", "Spend coins to upgrade the forge");
            d.Add("tutorial.hint.clear_exit", "Clear the path to the exit at the top of the mine");
            d.Add("tutorial.hint.exit_door", "Send cats to break the exit door");
            d.Add("tutorial.hint.next_mine", "Great job! Now let's move on to the next mine");

            // Шаг 7 (zone2): ручная шахта.
            d.Add("tutorial.hint.clear_shaft", "Clear the Amethyst shaft on the left: send cats to it");
            d.Add("tutorial.hint.activate_shaft", "Tap the shaft to activate it");
            d.Add("tutorial.hint.collect_payout", "Tap the shaft to collect the payout");

            // Шаг 8 (zone3): автоматизация. blueprint_ready: {0} — название шахты.
            d.Add("tutorial.hint.blueprint_ready", "You got the card for the {0} shaft — clear it and it will work on its own");
            d.Add("tutorial.hint.activate_automated", "Activate the shaft — it becomes automatic right away");
            d.Add("tutorial.hint.upgrade_manager", "Open Cards and upgrade the manager — the shaft will start working on its own");

            // Шаг 10 (zone5): ворота.
            d.Add("tutorial.hint.open_gate", "Tap the gate to open a new sector");

            // Шаги 11 и 9: бочка бесплатного котика и ящик доставки.
            d.Add("tutorial.hint.free_cat", "A free cat is in the barrel — tap it");
            d.Add("tutorial.hint.delivery", "A delivery has arrived! Tap the crate");
        }
    }
}

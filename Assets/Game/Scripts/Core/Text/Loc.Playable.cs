using System.Collections.Generic;

namespace CatMiners.Core.Text
{
    public static partial class Loc
    {
        /// <summary>
        /// Раздел Playable: тексты отдельной playable-сборки (Assets/Game/Scripts/Playable, Docs/Playable/PLAYABLE_TZ.md) —
        /// автопереход, квесты, надписи, баннер и финальный экран. Ключи начинаются с «playable.».
        /// </summary>
        static partial void RegisterPlayable(Dictionary<string, string> d)
        {
            // Автопереход после итогов шахты (ТЗ playable 3.4).
            d.Add("playable.auto_next", "Next mine in a moment…");

            // Квесты (ТЗ playable 3.3): окно награды и счётчик панели. reward: {0} — золото крупно рядом с монетой (без «+»:
            // в Pixeloid Bold на FontTitle он выглядит точкой, а монета на свечении и так говорит, что это награда);
            // done: {0} — шаг, {1} — счётчик («Tap rocks 5/5»); counter: {0}/{1}.
            d.Add("playable.quest.complete", "QUEST COMPLETE!");
            d.Add("playable.quest.claim", "CLAIM");
            d.Add("playable.quest.reward", "{0}");
            d.Add("playable.quest.done", "{0} {1}");
            d.Add("playable.quest.counter", "{0}/{1}");

            // Цепочка (PlayableQuestBook): название (жёлтое), шаг рядом со счётчиком — указание, реплика наставника при старте —
            // одна строка, до ~40 знаков. В шаге и реплике {0} — цель квеста (у «clear» — номер шахты).
            d.Add("playable.quest.strike.title", "Rock Smasher");
            d.Add("playable.quest.strike.step", "Tap rocks");
            d.Add("playable.quest.strike.line", "Tap a rock to help your cats break it!");
            d.Add("playable.quest.pickaxes.title", "Sharper Picks");
            d.Add("playable.quest.pickaxes.step", "Upgrade Pickaxes");
            d.Add("playable.quest.pickaxes.line", "Upgrade Pickaxes in Base to hit harder!");
            d.Add("playable.quest.hire_one.title", "Bigger Crew");
            d.Add("playable.quest.hire_one.step", "Hire a cat");
            d.Add("playable.quest.hire_one.line", "Hire a cat in Base for more gold!");
            d.Add("playable.quest.deliver.title", "Payday");
            d.Add("playable.quest.deliver.step", "Deliver gold");
            d.Add("playable.quest.deliver.line", "Let your cats haul gold to the depot!");
            d.Add("playable.quest.clear.title", "Mine Master");
            d.Add("playable.quest.clear.step", "Clear Mine {0}");
            d.Add("playable.quest.clear.line", "Break every rock to clear the mine!");
            d.Add("playable.quest.gear.title", "Gear Up");
            d.Add("playable.quest.gear.step", "Equip new gear");
            d.Add("playable.quest.gear.line", "New gear! Put it on in Equipment.");
            d.Add("playable.quest.power.title", "Double Power");
            d.Add("playable.quest.power.step", "Get Pickaxes Lv. {0}");
            d.Add("playable.quest.power.line", "Pickaxes Lv. {0} doubles cat power!");
            d.Add("playable.quest.hire.title", "Hiring Spree");
            d.Add("playable.quest.hire.step", "Hire cats");
            d.Add("playable.quest.hire.line", "Hire more cats to dig faster!");
            d.Add("playable.quest.bags.title", "Bigger Bags");
            d.Add("playable.quest.bags.step", "Upgrade Bags");
            d.Add("playable.quest.bags.line", "Upgrade Bags for fewer depot trips!");
            d.Add("playable.quest.chest.title", "Treasure Hunt");
            d.Add("playable.quest.chest.step", "Open a chest");
            d.Add("playable.quest.chest.line", "Open the free chest in the Store!");
            d.Add("playable.quest.card.title", "Card Power");
            d.Add("playable.quest.card.step", "Level up a card");
            d.Add("playable.quest.card.line", "Level up a card to boost your team!");
            d.Add("playable.quest.skip.title", "Time Warp");
            d.Add("playable.quest.skip.step", "Use a Time Skip");
            d.Add("playable.quest.skip.line", "Grab a free Time Skip in the Store!");
            d.Add("playable.quest.fullkit.title", "Full Kit");
            d.Add("playable.quest.fullkit.step", "Equip all {0} items");
            d.Add("playable.quest.fullkit.line", "Fill every gear slot in Equipment!");
            d.Add("playable.quest.gate.title", "Gatebreaker");
            d.Add("playable.quest.gate.step", "Break the gate");
            d.Add("playable.quest.gate.line", "Break the gate to open a new sector!");
            d.Add("playable.quest.sector.title", "New Sector");
            d.Add("playable.quest.sector.step", "Open the next sector");
            d.Add("playable.quest.sector.line", "One more gate stands in the way!");
            d.Add("playable.quest.cats.title", "Big Team");
            d.Add("playable.quest.cats.step", "Reach {0} cats");
            d.Add("playable.quest.cats.line", "A bigger team clears mines faster!");
            d.Add("playable.quest.unload.title", "Quick Unload");
            d.Add("playable.quest.unload.step", "Upgrade Unloading");
            d.Add("playable.quest.unload.line", "Upgrade Unloading to clear the depot line!");
            d.Add("playable.quest.boots.title", "Swift Paws");
            d.Add("playable.quest.boots.step", "Upgrade Boots");
            d.Add("playable.quest.boots.line", "Upgrade Boots so cats walk faster!");

            // Короткие надписи поверх поля (PlayableJuice). new_cats: {0} — сколько котиков; power: {0} — множитель вехи
            // («×» есть в Pixeloid: им подписаны кнопки ×1/×10 «Базы»).
            d.Add("playable.juice.new_cat", "NEW CAT!");
            d.Add("playable.juice.new_cats", "{0} NEW CATS!");
            d.Add("playable.juice.gate", "GATE OPEN!");
            d.Add("playable.juice.cleared", "MINE CLEARED!");
            d.Add("playable.juice.power", "PICKAXES ×{0}!");

            // Баннер CTA под нижней панелью и финальный экран (ТЗ playable 3.5). Звёзды рейтинга — спрайты icon_star (у Pixeloid
            // нет ★). line: {0} — пройдено шахт; of: {0}/{1}; teaser: {0} — шахт в игре, {1} — карточек, «•» есть в Pixeloid.
            d.Add("playable.cta.title", "CAT MINERS");
            d.Add("playable.cta.play", "PLAY FREE");
            d.Add("playable.end.line.one", "You cleared {0} mine!");
            d.Add("playable.end.line.many", "You cleared {0} mines!");
            d.Add("playable.end.line.none", "Your cats are just getting started!");
            d.Add("playable.end.stat.mines", "Mines cleared");
            d.Add("playable.end.stat.cats", "Cats in the team");
            d.Add("playable.end.stat.gold", "Gold delivered");
            d.Add("playable.end.stat.cards", "Cards collected");
            d.Add("playable.end.of", "{0}/{1}");
            d.Add("playable.end.teaser", "{0} mines • {1} cards • legendary gear");
            d.Add("playable.end.keep", "Keep playing");
            d.Add("playable.end.again", "Play again");
        }
    }
}

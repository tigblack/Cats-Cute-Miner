using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatMiners.Core.Outpost;
using CatMiners.Core.Sim;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CatMiners.Tests
{
    /// <summary>
    /// Сверка аудита баланса с настоящей игрой (Docs/Rework/BALANCE_AUDIT.md): шахта проходится в сцене CoreGame контроллером
    /// Unity — покупки кнопками «Базы» (IOutpostGame.Buy), удары киркой через BeginStrike с пределом интерфейса, обычный цикл
    /// кадров, автосохранение и показ событий. Журнал применённых команд из событий затем повторяется в чистом ядре
    /// (как в BalanceAudit), и итог шахты должен совпасть побайтно: ускоренный аудит меряет ту же экономику, что и игра.
    /// </summary>
    public sealed class OutpostBalanceSessionTests
    {
        ManualClock m_Clock;
        string m_Dir;
        OutpostController Game => GameBootstrap.OutpostController;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_Dir = Path.Combine(Application.temporaryCachePath, "OutpostBalanceSession", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Dir);
            m_Clock = new ManualClock(1_800_000_000_000L);
            OutpostController.ClockOverride = m_Clock;
            OutpostController.SaveDirectoryOverride = m_Dir;
            OutpostController.FileSystemOverride = null;
            GameBootstrap.LegacyMode = false;
            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            Assert.That(Game, Is.Not.Null);
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var empty = SceneManager.CreateScene("OutpostBalanceSessionCleanup-" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            var loaded = SceneManager.GetSceneByName("CoreGame");
            if (loaded.IsValid()) yield return SceneManager.UnloadSceneAsync(loaded);
            OutpostController.ClockOverride = null;
            OutpostController.SaveDirectoryOverride = null;
            OutpostController.ConfigsOverride = null;
            OutpostController.SuppressOfflineSummary = false;
            OutpostHud.BottomReserve = 0f;
            OutpostController.FileSystemOverride = null;
            GameBootstrap.LegacyMode = false;
            try { Directory.Delete(m_Dir, true); } catch (IOException) { }
        }

        struct Applied
        {
            public long Tick;
            public OutpostCommand Command;
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator UnitySession_MatchesCoreReplay([Values(1, 48, 144)] int zone)
        {
            Game.SwitchProfile(true);
            for (int i = 0; i < 600 && Game.Phase != OutpostControllerPhase.Playing; i++) yield return null;
            Assert.That(Game.IsDebugProfile, Is.True);
            Game.LoadZone(zone);
            yield return null;
            Assert.That(Game.State.Run.Zone, Is.EqualTo(zone));
            Assert.That(Game.State.Run.Balance.Version, Is.EqualTo(OutpostIds.BalanceVersion));
            int speed = zone == 1 ? 5 : zone < 100 ? 20 : 100;
            Game.SimSpeed = speed;
            string start = OutpostJson.Serialize(Game.State);
            long startNow = Game.State.Run.Now;

            // Покупки записываются в момент нажатия: контроллер выполняет команду на текущей отметке ядра (Sim.Now после вызова).
            // Удары — по событию TargetStruck: команда уходит в кадр удара анимации. Показ событий ограничен 96 за кадр, поэтому
            // журнал покупок из событий на ×100 отставал бы от ядра.
            var applied = new List<Applied>();
            int purchaseEvents = 0;
            Action<OutpostEvent> onEvent = e =>
            {
                if (e.Type == OutpostEventType.TeamUpgradePurchased) purchaseEvents += e.Count;
                else if (e.Type == OutpostEventType.TargetStruck)
                    applied.Add(new Applied { Tick = e.Tick, Command = OutpostCommand.Strike(e.TargetId) });
            };
            Game.EventPresented += onEvent;
            long nextBuy = startNow + 30 * OutpostIds.TicksPerSecond;
            int frames = 0, strikesTried = 0;
            try
            {
                // После шахты 144 следующей нет и CanContinue ложно: ждём ReadyToContinue самой шахты.
                while (Game.State.Run.Phase != RunPhase.ReadyToContinue && frames < 40000)
                {
                    m_Clock.Advance(0.25);
                    frames++;
                    yield return null;
                    if (Game.Phase != OutpostControllerPhase.Playing || Game.State.Run.Phase != RunPhase.Running) continue;
                    // Модель free-cheapest через кнопки игры: раз в 30 с логического времени — самые дешёвые доступные ×1.
                    if (Game.Sim.Now >= nextBuy)
                    {
                        nextBuy = Game.Sim.Now + 30 * OutpostIds.TicksPerSecond;
                        for (int guard = 0; guard < 200; guard++)
                        {
                            UpgradeBranch? best = null;
                            System.Numerics.BigInteger bestCost = 0;
                            foreach (UpgradeBranch br in Enum.GetValues(typeof(UpgradeBranch)))
                            {
                                var q = Game.Quote(br, BuyMode.One);
                                if (q.CanBuy && (best == null || q.Cost < bestCost)) { best = br; bestCost = q.Cost; }
                            }
                            if (best == null || !Game.Buy(best.Value, BuyMode.One).Ok) break;
                            applied.Add(new Applied { Tick = Game.Sim.Now, Command = OutpostCommand.BuyUpgrade(best.Value) });
                        }
                    }
                    // Удары киркой первые 60 с шахты по цели, которую бьют котики: тап каждый кадр, интерфейс сам ограничит частоту.
                    if (Game.Sim.Now - startNow < 60 * OutpostIds.TicksPerSecond)
                    {
                        int target = -1, assigned = -1;
                        foreach (var mt in Game.Map.Targets)
                            if (Game.CanStrike(mt.Id) && Game.Sim.AssignedTo(mt.Id) > assigned) { target = mt.Id; assigned = Game.Sim.AssignedTo(mt.Id); }
                        if (target >= 0 && Game.TryGetTargetWorldPosition(target, out var world) && Game.BeginStrike(target, world)) strikesTried++;
                    }
                }
                // Догнать показ событий завершения.
                for (int i = 0; i < 30; i++) yield return null;
            }
            finally
            {
                Game.EventPresented -= onEvent;
            }
            Assert.That(Game.State.Run.Phase, Is.EqualTo(RunPhase.ReadyToContinue), "шахта " + zone + " не завершилась за " + frames + " кадров");
            Assert.That(zone == 144 ? Game.CampaignComplete : Game.CanContinue, Is.True);
            var unityRun = Game.State.Run;
            Assert.That(applied.Count(a => a.Command.Kind == CommandKind.BuyUpgrade), Is.GreaterThan(0));

            // Повтор в чистом ядре: те же команды на тех же отметках.
            var replayState = OutpostJson.Deserialize<OutpostGameState>(start);
            var map = OutpostLevelAdapter.ForRun(Game.Balance, replayState.Run);
            var sim = new OutpostSimulation(replayState, map, Game.Configs) { RecordEvents = false };
            // Удар выполняется в Update контроллера раньше кода теста в том же кадре, а часы двигаются только между кадрами:
            // при равной отметке удар предшествует покупкам; порядок покупок внутри кадра сохраняется.
            foreach (var a in applied.OrderBy(a => a.Tick).ThenBy(a => a.Command.Kind == CommandKind.Strike ? 0 : 1))
            {
                sim.Advance(a.Tick);
                Assert.That(sim.Submit(a.Command).Ok, Is.True, "повтор " + a.Command + " на отметке " + a.Tick);
            }
            var tail = new OutpostBot(BotPolicy.A).Run(sim);
            Assert.That(tail.Success, Is.True, tail.ToString());
            var replayRun = replayState.Run;
            string diag = "zone " + zone + ": purchases logged " + applied.Count(a => a.Command.Kind == CommandKind.BuyUpgrade) +
                ", presented purchase events " + purchaseEvents + ", strikes " + applied.Count(a => a.Command.Kind == CommandKind.Strike) +
                "; levels unity [" + string.Join(",", unityRun.Levels) + "] replay [" + string.Join(",", replayRun.Levels) + "]; completed unity " +
                unityRun.Counters.CompletedTick + " replay " + replayRun.Counters.CompletedTick;
            TestContext.WriteLine(diag);
            CollectionAssert.AreEqual(unityRun.Levels, replayRun.Levels, "уровни; " + diag);
            Assert.That(replayRun.Counters.CompletedTick, Is.EqualTo(unityRun.Counters.CompletedTick), "время завершения; " + diag);
            Assert.That(replayRun.TotalCredited, Is.EqualTo(unityRun.TotalCredited), "доставленное золото");
            Assert.That(replayRun.TotalSpent, Is.EqualTo(unityRun.TotalSpent), "потрачено");
            CollectionAssert.AreEqual(unityRun.Levels, replayRun.Levels, "уровни");
            Assert.That(replayRun.Counters.DeliveredUnits, Is.EqualTo(unityRun.Counters.DeliveredUnits));
            // После ReadyToContinue экономика стоит, но часы контроллера ещё идут несколько кадров: сравниваем снимок шахты
            // с тем же Now и UTC-маркером.
            var unityCopy = OutpostJson.DeepCopy(Game.State);
            unityCopy.Run.LastProcessedUtcMs = replayState.Run.LastProcessedUtcMs;
            unityCopy.Run.Now = replayState.Run.Now;
            Assert.That(OutpostJson.Serialize(replayState.Run), Is.EqualTo(OutpostJson.Serialize(unityCopy.Run)), "снимок шахты");

            string dir = Path.Combine(PlayModeUtil.ReportsDir(), "balance-audit", "unity");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "session-zone" + zone + ".json"), OutpostJson.Serialize(new
            {
                zone,
                balanceVersion = unityRun.Balance.Version,
                frames,
                simSpeed = speed,
                completedSec = unityRun.Counters.CompletedTick / (double)OutpostIds.TicksPerSecond,
                replayCompletedSec = replayRun.Counters.CompletedTick / (double)OutpostIds.TicksPerSecond,
                credited = unityRun.TotalCredited.ToString(),
                spent = unityRun.TotalSpent.ToString(),
                levels = unityRun.Levels,
                purchases = applied.Count(a => a.Command.Kind == CommandKind.BuyUpgrade),
                strikes = applied.Count(a => a.Command.Kind == CommandKind.Strike),
                strikeTaps = strikesTried,
                identicalRunSnapshot = true,
            }) + "\n");
        }
    }
}

using System.Numerics;
using CatMiners.Core.Outpost;
using NUnit.Framework;
using static CatMiners.Tests.Outpost.OutpostTestHelpersB;

namespace CatMiners.Tests.Outpost
{
    public class OutpostMovementRecoveryTests
    {
        [Test]
        public void MissingMovementTimer_RetriesAfterThreeSeconds_WithoutTeleportOrIncome()
        {
            var simulation = SingleRock().NewSim();
            var worker = simulation.Run.Workers[0];
            int cell = worker.Cell;
            long task = worker.TaskId;
            // Инъекция отказа таймера: проверяем требуемое восстановление без выдачи ресурсов/телепортации.
            worker.Action = null;
            simulation.Advance(3 * S - 1);
            Assert.AreEqual(cell, worker.Cell);
            Assert.IsNull(worker.Action, "до трёх секунд повтор не выполняется");
            simulation.Advance(3 * S);
            Assert.AreEqual(cell, worker.Cell, "пересчёт пути не перемещает котика");
            Assert.AreEqual(task, worker.TaskId, "корректная задача и её рабочая клетка сохранены");
            Assert.IsNotNull(worker.Action);
            Assert.AreEqual(ActionKind.Edge, worker.Action.Kind);
            Assert.Greater(worker.Action.Deadline, simulation.Now);
            Assert.AreEqual(0, worker.Bag.Count);
            Assert.AreEqual(BigInteger.Zero, simulation.Run.Wallet);
            AssertInvariants(simulation, "после восстановления таймера");
            RunToReadyOrFail(simulation, "восстановление движения");
        }

        [Test]
        public void SlowContinuousEdge_IsNotTreatedAsStuck()
        {
            var configs = ConfigsWith(json => json["BaseMoveSpeed"] = "1/10");
            var simulation = SingleRock(configs).NewSim();
            var worker = simulation.Run.Workers[0];
            var action = worker.Action;
            var path = worker.Path;
            long task = worker.TaskId;
            simulation.Advance(4 * S);
            Assert.AreSame(action, worker.Action, "доля ребра растёт, хотя клетка ещё не пройдена");
            Assert.AreSame(path, worker.Path);
            Assert.AreEqual(task, worker.TaskId);
            Assert.IsTrue(worker.Action.ProgressAt(simulation.Now) > Rational.Zero);
            AssertInvariants(simulation, "медленное корректное движение");
        }
    }
}

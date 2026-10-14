using System;
using System.Threading.Tasks;
using CatMiners.Core.Outpost;
using CatMiners.Runtime.Purchasing;

namespace CatMiners.Runtime.Outpost
{
    public sealed partial class OutpostController
    {
        int m_PaymentGeneration;
        PaymentProgress m_PaymentProgress;
        public bool CanUseMoneyStore => CommandsAllowed && !IsDebugProfile;
        void BindPurchases()
        {
            if (!AndroidRuntime || IsDebugProfile || State == null) return;
            if (m_PaymentProgress == null) m_PaymentProgress = new PaymentProgress(this, m_PaymentGeneration);
            GooglePaymentsRuntime.Attach(this, m_PaymentProgress);
        }
        void InvalidatePurchases()
        {
            m_PaymentGeneration++; m_PaymentProgress = null; GooglePaymentsRuntime.Instance?.Detach(this);
        }
        sealed class PaymentProgress : IPaymentRevenueProgress
        {
            readonly OutpostController controller;
            readonly int generation;
            public PaymentProgress(OutpostController controller, int generation) { this.controller = controller; this.generation = generation; }
            public bool Current => controller != null && controller.m_PaymentGeneration == generation && !controller.IsDebugProfile && controller.State != null;
            public OutpostProfile Profile => Current ? controller.State.Profile : throw new PaymentException("stale_context");
            public Task SaveRevenue(Action<OutpostProfile> change)
            {
                // Analytics cannot acquire the blocking purchase/gameplay save continuation.
                // This synchronous main-thread write clones the latest settled state only.
                if (!Current || !controller.CommandsAllowed || controller.m_Offline != null || controller.m_Saves == null)
                    throw new PaymentException("analytics_save_unavailable");
                var prepared = OutpostJson.DeepCopy(controller.State);
                change(prepared.Profile);
                var result = controller.m_Saves.Save(prepared, controller.m_Slot, controller.Map, resetBackup: true);
                if (!result.Ok) throw new PaymentException("analytics_save_failed");
                // Only recipient markers changed; keep the active simulation/UI and all pending callbacks intact.
                controller.State.Profile.Payments = prepared.Profile.Payments;
                return Task.CompletedTask;
            }
            public async Task Save(Action<OutpostProfile> change)
            {
                // Offline/transition snapshots must settle before cloning the latest balance. Never mutate their old copies.
                while (Current && (!controller.CommandsAllowed || controller.m_PendingSave != null || controller.m_Offline != null))
                    await Task.Yield();
                if (!Current) throw new PaymentException("stale_context");
                var prepared = OutpostJson.DeepCopy(controller.State); var map = controller.Map;
                change(prepared.Profile);
                var durable = new TaskCompletionSource<bool>();
                controller.Commit(prepared, map, () => {
                    if (!Current) { durable.TrySetException(new PaymentException("stale_context")); return; }
                    controller.SetSimulation(prepared, map); controller.Hud?.RefreshNow(); durable.TrySetResult(true);
                }, resetBackup: true);
                // Disk failure retains exactly this snapshot/continuation in Outpost's RetrySave.
                while (!durable.Task.IsCompleted && Current) await Task.Yield();
                if (!Current) throw new PaymentException("stale_context");
                await durable.Task;
            }
        }
    }
}

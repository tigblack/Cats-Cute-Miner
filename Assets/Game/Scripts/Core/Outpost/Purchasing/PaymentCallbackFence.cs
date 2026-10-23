using System.Collections.Generic;

namespace CatMiners.Core.Outpost
{
    public enum PaymentConfirmationResult { Ignored, Failed, Succeeded }
    // IAP does not expose request IDs for inventory. Keep one query barrier and explicit consume ownership.
    public sealed class PaymentCallbackFence
    {
        sealed class Consume { public int Generation; public string User; }
        readonly Dictionary<string, Consume> consuming = new Dictionary<string, Consume>();
        int queryGeneration;
        public bool QueryOutstanding { get; private set; }
        public bool BeginQuery(int generation)
        {
            if (QueryOutstanding) return false;
            QueryOutstanding = true; queryGeneration = generation; return true;
        }
        public bool EndQuery(int generation)
        {
            if (!QueryOutstanding) return false;
            QueryOutstanding = false; return queryGeneration == generation;
        }
        public bool BeginConsume(string hash, int generation, string user)
        {
            if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(user) || consuming.ContainsKey(hash)) return false;
            consuming.Add(hash, new Consume { Generation = generation, User = user }); return true;
        }
        public bool Consuming(string hash) => consuming.ContainsKey(hash);
        public void AbortConsume(string hash) => consuming.Remove(hash);
        public PaymentConfirmationResult EndConsume(string hash, int generation, string user, bool failed)
        {
            if (!consuming.TryGetValue(hash, out var request)) return PaymentConfirmationResult.Ignored;
            consuming.Remove(hash);
            if (request.Generation != generation || request.User != user) return PaymentConfirmationResult.Ignored;
            return failed ? PaymentConfirmationResult.Failed : PaymentConfirmationResult.Succeeded;
        }
    }
}

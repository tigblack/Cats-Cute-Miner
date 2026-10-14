using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CatMiners.Core.Outpost;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CatMiners.Tests.Outpost
{
    public class PaymentDeliveryTests
    {
        const string Sku = "gems_handful", Token = "fixture-purchase-token", Account = "guest-1";
        static JArray Items(long quantity = 160) => new JArray(new JObject { ["sku"] = "gems", ["quantity"] = quantity, ["nestedItems"] = null });
        static JObject Catalog() => new JObject { ["products"] = new JArray(PaymentRules.Products.Select(p => new JObject {
            ["productSku"] = p.Key, ["googlePlaySku"] = p.Key, ["productType"] = "Consumable", ["rewards"] = Items(p.Value) })) };
        static string Receipt(Action<JObject> edit = null)
        {
            var json = new JObject { ["packageName"] = PaymentRules.Package, ["productId"] = Sku, ["purchaseToken"] = Token,
                ["purchaseTime"] = 1, ["purchaseState"] = 0, ["quantity"] = 1, ["obfuscatedAccountId"] = OutpostJson.Sha256Hex(Account) };
            edit?.Invoke(json);
            return new JObject { ["Store"] = "GooglePlay", ["TransactionID"] = Token,
                ["Payload"] = new JObject { ["json"] = json.ToString(), ["signature"] = "fixture-signature" }.ToString() }.ToString();
        }
        static GooglePurchaseProof Proof(bool fresh = false) => GooglePurchaseProof.Parse(Receipt(fresh ? new Action<JObject>(j => j["purchaseTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) : null), Token, Sku, OutpostJson.Sha256Hex(Account));
        sealed class Backend : IPaymentBackend
        {
            public string UserId { get; set; } = Account;
            public JObject Payment = new JObject { ["paymentId"] = "payment-1", ["rewardId"] = "reward-1", ["environment"] = "production" };
            public JObject Inbox = new JObject { ["rewards"] = new JArray(new JObject { ["rewardId"] = "reward-1", ["reason"] = "payment", ["items"] = Items() }) };
            public string FailPath;
            public Action<string> OnRequest;
            public readonly List<string> Calls = new List<string>();
            public string SentReceipt;
            public Task<JObject> Request(string path, JObject body = null)
            {
                Calls.Add(path); OnRequest?.Invoke(path);
                if (path == FailPath) throw new PaymentHttpException(503);
                switch (path)
                {
                    case "/api/products/get": return Task.FromResult(Catalog());
                    case "/api/orders/create": return Task.FromResult(new JObject { ["orderId"] = "order-1", ["rewardItems"] = Items() });
                    case "/api/payment/google-play": SentReceipt = (string)body["receipt"]; return Task.FromResult((JObject)Payment.DeepClone());
                    case "/api/rewards/get-all-unclaimed": return Task.FromResult((JObject)Inbox.DeepClone());
                    case "/api/rewards/set-claimed": return Task.FromResult(new JObject());
                    default: throw new Exception("Unexpected fixture route");
                }
            }
        }
        sealed class Progress : IPaymentRevenueProgress
        {
            public bool Current { get; set; } = true;
            public OutpostGameState State = SaveKitA.MakeState(100);
            public OutpostProfile Profile => State.Profile;
            public MemoryFileSystemA Files = new MemoryFileSystemA();
            public OutpostSaveService Saves;
            public Progress()
            {
                Saves = new OutpostSaveService(SaveKitA.Root, Files) { Configs = OutpostKitA.Configs, MapResolver = s => OutpostKitA.Map(s.Run.Zone) };
                Assert.IsTrue(Saves.Save(State, resetBackup: true).Ok);
            }
            public Task Save(Action<OutpostProfile> change)
            {
                if (!Current) throw new PaymentException("stale_context");
                var prepared = OutpostJson.DeepCopy(State); change(prepared.Profile);
                var result = Saves.Save(prepared, resetBackup: true);
                if (!result.Ok) throw new PaymentException("disk_failure");
                State = prepared; return Task.CompletedTask;
            }
            public Task SaveRevenue(Action<OutpostProfile> change) => Save(change);
            public void Relaunch() { State = Saves.Load().State; Assert.NotNull(State); }
        }
        static async Task<(Progress, Backend, PaymentPipeline)> Prepared()
        {
            var p = new Progress(); var b = new Backend(); var pipe = new PaymentPipeline(b, p);
            await pipe.Prepare(Sku, "1.99", "USD"); return (p, b, pipe);
        }
        [TestCase("packageName", "other.package")]
        [TestCase("productId", "gems_sack")]
        [TestCase("purchaseToken", "other-token")]
        [TestCase("obfuscatedAccountId", "different-user")]
        public void ReceiptMismatchRejects(string key, string value) => Assert.Throws<PaymentException>(() =>
            GooglePurchaseProof.Parse(Receipt(j => j[key] = value), Token, Sku, OutpostJson.Sha256Hex(Account)));
        [TestCase(1)] [TestCase(2)] [TestCase(-1)]
        public void DeferredOrUnpurchasedStatesNeverEnterValidation(int state) => Assert.Throws<PaymentException>(() =>
            GooglePurchaseProof.Parse(Receipt(j => j["purchaseState"] = state), Token, Sku, OutpostJson.Sha256Hex(Account)));
        [TestCase(0)] [TestCase(2)] [TestCase(-1)]
        public void MultiQuantityRejected(int quantity) => Assert.Throws<PaymentException>(() =>
            GooglePurchaseProof.Parse(Receipt(j => j["quantity"] = quantity), Token, Sku, OutpostJson.Sha256Hex(Account)));
        [Test]
        public void MissingQuantityIsOneAndOriginalJsonUnchanged()
        {
            var wrapper = Receipt(j => j.Remove("quantity")); var proof = GooglePurchaseProof.Parse(wrapper, Token, Sku, OutpostJson.Sha256Hex(Account));
            Assert.AreEqual((string)JObject.Parse((string)JObject.Parse(wrapper)["Payload"])["json"], proof.OriginalJson);
            Assert.AreEqual(OutpostJson.Sha256Hex(Token), proof.TokenHash);
            Assert.Throws<PaymentException>(() => GooglePurchaseProof.Parse("{}", Token, Sku, ""));
            Assert.Throws<PaymentException>(() => GooglePurchaseProof.Parse(wrapper, "different", Sku, ""));
        }
        [TestCase("paymentId", "")] [TestCase("rewardId", "bad id")] [TestCase("environment", "unknown")]
        public async Task MalformedApprovalCannotCredit(string key, string value)
        {
            var (p, b, pipe) = await Prepared(); var balance = p.Profile.Gems; b.Payment[key] = value;
            Assert.ThrowsAsync<PaymentException>(async () => await pipe.ValidateAndDeliver(Proof(), false));
            Assert.AreEqual(balance, p.Profile.Gems); Assert.IsEmpty(p.Profile.Payments);
        }
        [Test]
        public async Task IncorrectRewardAndUnknownClaimedRewardNeverGrant()
        {
            var (p, b, pipe) = await Prepared(); var balance = p.Profile.Gems;
            b.Inbox["rewards"][0]["items"] = Items(500);
            Assert.ThrowsAsync<PaymentException>(async () => await pipe.ValidateAndDeliver(Proof(), false));
            b.Inbox["rewards"] = new JArray(new JObject { ["rewardId"] = "unrelated", ["items"] = Items() });
            Assert.ThrowsAsync<PaymentException>(async () => await pipe.ValidateAndDeliver(Proof(), false));
            Assert.AreEqual(balance, p.Profile.Gems);
        }
        [TestCase("/api/payment/google-play")] [TestCase("/api/rewards/get-all-unclaimed")]
        public async Task ServerOfflineKeepsIntentAndNeverGrants(string path)
        {
            var (p, b, pipe) = await Prepared(); b.FailPath = path; var balance = p.Profile.Gems;
            Assert.ThrowsAsync<PaymentHttpException>(async () => await pipe.ValidateAndDeliver(Proof(), false));
            p.Relaunch(); Assert.NotNull(p.Profile.PendingPurchase); Assert.IsEmpty(p.Profile.Payments); Assert.AreEqual(balance, p.Profile.Gems);
        }
        [Test]
        public async Task DiskFailureThenReplayAndRelaunchCreditExactlyOnce()
        {
            var (p, b, pipe) = await Prepared(); var balance = p.Profile.Gems;
            p.Files.ArmCrash(1, false);
            Assert.ThrowsAsync<PaymentException>(async () => await pipe.ValidateAndDeliver(Proof(), false));
            Assert.AreEqual(balance, p.Profile.Gems); p.Relaunch(); Assert.AreEqual(balance, p.Profile.Gems);
            p.Files = p.Files.CrashImage(); p.Saves = new OutpostSaveService(SaveKitA.Root, p.Files) { Configs = OutpostKitA.Configs, MapResolver = s => OutpostKitA.Map(s.Run.Zone) };
            var delivery = await pipe.ValidateAndDeliver(Proof(), false);
            Assert.AreEqual(balance + 160, p.Profile.Gems); Assert.AreEqual(1, p.Profile.Payments.Count);
            p.Relaunch(); b.Inbox["rewards"] = new JArray();
            await new PaymentPipeline(b, p).ValidateAndDeliver(Proof(), false);
            Assert.AreEqual(balance + 160, p.Profile.Gems); Assert.AreEqual(1, p.Profile.Payments.Count);
            var json = OutpostSaveService.SerializeState(p.State); Assert.IsFalse(json.Contains(Token)); Assert.IsFalse(json.Contains("purchaseToken"));
            Assert.AreEqual(Proof().OriginalJson, b.SentReceipt);
        }
        [Test]
        public async Task ClaimAndConsumptionAreIndependentAndReplayCannotCreateNewSale()
        {
            var (p, b, pipe) = await Prepared(); var d = await pipe.ValidateAndDeliver(Proof(), false); b.FailPath = "/api/rewards/set-claimed";
            Assert.ThrowsAsync<PaymentHttpException>(async () => await pipe.Claim(d));
            Assert.IsFalse(p.Profile.Payments.Single().Claimed);
            await pipe.Confirmed(d.TokenHash); p.Relaunch(); Assert.IsTrue(p.Profile.Payments.Single().Confirmed);
            Assert.IsTrue(PaymentRules.Pending(p.Profile)); b.FailPath = null;
            await pipe.Claim(p.Profile.Payments.Single()); p.Relaunch(); Assert.IsFalse(PaymentRules.Pending(p.Profile));
            await pipe.ValidateAndDeliver(Proof(), false); Assert.AreEqual(1, b.Calls.Count(x => x == "/api/orders/create"));
            Assert.AreEqual(160, p.Profile.Gems);
        }
        [Test]
        public async Task ClaimSavedButConsumeFailedRemainsPendingAcrossRestart()
        {
            var (p, b, pipe) = await Prepared(); var d = await pipe.ValidateAndDeliver(Proof(), false); await pipe.Claim(d); p.Relaunch();
            Assert.IsTrue(p.Profile.Payments.Single().Claimed); Assert.IsFalse(p.Profile.Payments.Single().Confirmed);
            Assert.IsTrue(PaymentRules.Pending(p.Profile));
            var replay = await pipe.ValidateAndDeliver(Proof(), false); await pipe.Confirmed(replay.TokenHash);
            Assert.IsFalse(PaymentRules.Pending(p.Profile)); Assert.AreEqual(160, p.Profile.Gems);
        }
        [Test]
        public async Task StaleCallbackOrChangedGuestCannotCredit()
        {
            var (p, b, pipe) = await Prepared(); b.OnRequest = path => { if (path == "/api/payment/google-play") p.Current = false; };
            Assert.ThrowsAsync<PaymentException>(async () => await pipe.ValidateAndDeliver(Proof(), false)); Assert.IsEmpty(p.Profile.Payments);
            p.Current = true; b.OnRequest = null; b.UserId = "guest-2";
            Assert.ThrowsAsync<PaymentException>(async () => await pipe.ValidateAndDeliver(Proof(), false)); Assert.IsEmpty(p.Profile.Payments);
        }
        [Test]
        public async Task BackendDifferentPaymentOrRewardOnReplayRejected()
        {
            var (p, b, pipe) = await Prepared(); await pipe.ValidateAndDeliver(Proof(), false); b.Payment["paymentId"] = "payment-2";
            Assert.ThrowsAsync<PaymentException>(async () => await pipe.ValidateAndDeliver(Proof(), false)); Assert.AreEqual(160, p.Profile.Gems);
        }
        [TestCase("sandbox", false, "excluded_sandbox")]
        [TestCase("production", true, "excluded_privacy")]
        [TestCase("production", false, "excluded_recovery_or_unknown_price")]
        public async Task SandboxChildrenAndUnknownRevenueNeverEmitMoney(string environment, bool child, string status)
        {
            var (p, b, pipe) = await Prepared(); b.Payment["environment"] = environment;
            var d = await pipe.ValidateAndDeliver(Proof(), child); Assert.AreEqual(status, d.RevenueStatus);
            Assert.AreEqual(160, p.Profile.Gems);
        }
        [Test]
        public async Task ResetBlocksPendingAndPreservesFinishedLedger()
        {
            var (p, b, pipe) = await Prepared(); var fresh = SaveKitA.MakeState(200);
            Assert.Throws<PaymentException>(() => PaymentRules.PreserveAcrossReset(p.Profile, fresh.Profile));
            var d = await pipe.ValidateAndDeliver(Proof(), false); await pipe.Claim(d); await pipe.Confirmed(d.TokenHash);
            PaymentRules.PreserveAcrossReset(p.Profile, fresh.Profile); p.State = fresh; await p.Save(_ => { }); p.Relaunch();
            await pipe.ValidateAndDeliver(Proof(), false); Assert.AreEqual(0, p.Profile.Gems); Assert.AreEqual(1, p.Profile.Payments.Count);
        }
        [Test]
        public async Task LatestStateAfterInterruptionIsClonedAndChangedAccountDuringResponseRejects()
        {
            var (p, b, pipe) = await Prepared();
            b.OnRequest = path => { if (path == "/api/rewards/get-all-unclaimed") p.State.Profile.Gems += 37; };
            await pipe.ValidateAndDeliver(Proof(), true); Assert.AreEqual(197, p.Profile.Gems);
            var second = await Prepared();
            second.Item2.OnRequest = path => { if (path == "/api/payment/google-play") second.Item2.UserId = "other-user"; };
            Assert.ThrowsAsync<PaymentException>(async () => await second.Item3.ValidateAndDeliver(Proof(), true));
            Assert.IsEmpty(second.Item1.Profile.Payments);
        }
        [Test]
        public void V1MigratesLedgerAndV2RejectsNullLedger()
        {
            var p = new Progress();
            var envelope = SaveKitA.ReadEnvelope(p.Files, SaveKitA.Main);
            var payload = SaveKitA.Payload(envelope);
            envelope["SchemaVersion"] = 1; payload["Profile"]["SchemaVersion"] = 1;
            ((JObject)payload["Profile"]).Remove("Payments"); ((JObject)payload["Profile"]).Remove("PendingPurchase");
            SaveKitA.SetPayload(envelope, payload, true); SaveKitA.WriteEnvelope(p.Files, SaveKitA.Main, envelope);
            p.Files.Delete(SaveKitA.Bak); p.Relaunch(); Assert.AreEqual(2, p.Profile.SchemaVersion); Assert.IsEmpty(p.Profile.Payments);
            p.Profile.Payments = null; Assert.IsFalse(p.Saves.Save(p.State).Ok);
        }
        [Test]
        public void V2MissingLedgerIsRejectedEvenWithValidChecksum()
        {
            var p = new Progress(); var env = SaveKitA.ReadEnvelope(p.Files, SaveKitA.Main); var payload = SaveKitA.Payload(env);
            ((JObject)payload["Profile"]).Remove("Payments"); SaveKitA.SetPayload(env, payload, true); SaveKitA.WriteEnvelope(p.Files, SaveKitA.Main, env);
            p.Files.Delete(SaveKitA.Bak); Assert.AreEqual(OutpostLoadStatus.Corrupt, p.Saves.Load().Status);
        }
        [Test]
        public void ActualCallbackFenceRejectsStaleAccountsAndDuplicateConsumesAndQueries()
        {
            var fence = new PaymentCallbackFence(); var hash = Proof().TokenHash;
            Assert.IsTrue(fence.BeginConsume(hash, 1, Account)); Assert.IsFalse(fence.BeginConsume(hash, 1, Account));
            Assert.AreEqual(PaymentConfirmationResult.Ignored, fence.EndConsume(hash, 2, Account, false));
            Assert.IsTrue(fence.BeginConsume(hash, 2, Account));
            Assert.AreEqual(PaymentConfirmationResult.Ignored, fence.EndConsume(hash, 2, "other-guest", false));
            Assert.IsTrue(fence.BeginConsume(hash, 2, Account)); Assert.AreEqual(PaymentConfirmationResult.Failed, fence.EndConsume(hash, 2, Account, true));
            Assert.IsTrue(fence.BeginConsume(hash, 2, Account)); Assert.AreEqual(PaymentConfirmationResult.Succeeded, fence.EndConsume(hash, 2, Account, false));
            Assert.AreEqual(PaymentConfirmationResult.Ignored, fence.EndConsume(hash, 2, Account, false));
            Assert.IsTrue(fence.BeginQuery(1)); Assert.IsFalse(fence.BeginQuery(2)); Assert.IsTrue(fence.QueryOutstanding);
            Assert.IsFalse(fence.EndQuery(2)); Assert.IsFalse(fence.QueryOutstanding);
            Assert.IsTrue(fence.BeginQuery(2)); Assert.IsTrue(fence.EndQuery(2)); Assert.IsFalse(fence.EndQuery(2));
        }
        [Test]
        public async Task ActualRunTransitionAndOfflineSnapshotsKeepIntentAndDeliveredLedger()
        {
            var (p, b, pipe) = await Prepared();
            p.State.Run.Phase = RunPhase.ReadyToContinue;
            var transition = StageProgressionService.PrepareTransition(p.State, OutpostKitA.Repo, OutpostKitA.Configs, 1000);
            Assert.IsTrue(transition.Ok); p.State = transition.State;
            Assert.AreEqual("order-1", p.Profile.PendingPurchase.OrderId);
            await pipe.ValidateAndDeliver(Proof(), true); Assert.AreEqual(2, p.State.Run.Zone);
            var job = new OfflineSimulator(transition.Map, OutpostKitA.Configs).Begin(p.State, 61_000);
            job.RunToEnd(); p.State = job.State; await p.Save(_ => { }); p.Relaunch();
            Assert.AreEqual(160, p.Profile.Gems); Assert.AreEqual(1, p.Profile.Payments.Count);
            await pipe.ValidateAndDeliver(Proof(), true); Assert.AreEqual(160, p.Profile.Gems);
        }
        sealed class RevenueSink : IPaymentRevenueSink
        {
            public readonly Dictionary<PaymentRevenueRecipient, string> Waiting = new Dictionary<PaymentRevenueRecipient, string>();
            public readonly List<PaymentRevenueRecipient> Calls = new List<PaymentRevenueRecipient>();
            public readonly List<PaymentDelivery> Data = new List<PaymentDelivery>();
            public PaymentRevenueRecipient? Throw;
            public string Unavailable(PaymentRevenueRecipient r) => Waiting.TryGetValue(r, out var status) ? status : null;
            public void Report(PaymentRevenueRecipient r, PaymentDelivery d)
            {
                Calls.Add(r); Data.Add(d);
                if (Throw == r) throw new Exception("Ambiguous SDK failure");
            }
        }
        static async Task<PaymentDelivery> Fresh(Progress p, PaymentPipeline pipe, bool child = false)
            => await pipe.ValidateAndDeliver(Proof(true), child, p.Profile.PendingPurchase.OrderId);
        [TestCase("1.99", "USD", 1990000L)]
        [TestCase("0.000001", "JPY", 1L)]
        [TestCase("0.0000001", "USD", 0L)]
        [TestCase("1,99", "EUR", 0L)]
        [TestCase("-1", "USD", 0L)]
        [TestCase("0", "USD", 0L)]
        [TestCase("1", "usd", 0L)]
        [TestCase("1", "US", 0L)]
        [TestCase("9223372036854.775808", "USD", 0L)]
        public void ExactPriceSnapshotNeverRoundsOrFabricates(string price, string currency, long expected)
            => Assert.AreEqual(expected, PaymentRevenue.PriceMicros(price, currency));
        [Test]
        public async Task FreshVerifiedPriceIsDurableAndReplayCannotDispatchTwice()
        {
            var (p, b, pipe) = await Prepared();
            Assert.AreEqual(1990000, p.Profile.PendingPurchase.PriceMicros);
            var d = await Fresh(p, pipe); Assert.IsTrue(PaymentRevenue.Eligible(d));
            p.Relaunch(); Assert.AreEqual(1990000, p.Profile.Payments.Single().PriceMicros);
            Assert.AreEqual("USD", p.Profile.Payments.Single().Currency);
            var sink = new RevenueSink(); await new PaymentRevenue(p).Dispatch(sink);
            await pipe.ValidateAndDeliver(Proof(), false);
            p.Relaunch(); await new PaymentRevenue(p).Dispatch(sink);
            Assert.AreEqual(3, sink.Calls.Count);
            Assert.IsTrue(sink.Data.All(x => x.PaymentId == "payment-1" && x.OrderId == "order-1" && x.PriceMicros == 1990000));
            Assert.IsTrue(p.Profile.Payments.All(x => x.AppMetricaRevenue == "handed_off" && x.MetaRevenue == "handed_off" && x.AdjustRevenue == "handed_off"));
        }
        [TestCase("sandbox", false)] [TestCase("production", true)]
        public async Task SandboxAndProtectedNeverDispatchEvenAfterConsentChange(string environment, bool child)
        {
            var (p, b, pipe) = await Prepared(); b.Payment["environment"] = environment;
            await Fresh(p, pipe, child); p.Relaunch(); var sink = new RevenueSink();
            await new PaymentRevenue(p).Dispatch(sink); Assert.IsEmpty(sink.Calls); Assert.AreEqual(160, p.Profile.Gems);
        }
        [Test]
        public async Task RestoreOldReceiptMismatchedIntentAndLegacyNeverFabricateRevenue()
        {
            foreach (var kind in new[] { "restored", "old", "wrong_order", "legacy_price", "invalid_price" })
            {
                var (p, b, pipe) = await Prepared();
                var proof = kind == "old" ? GooglePurchaseProof.Parse(Receipt(j => j["purchaseTime"] = 1), Token, Sku, OutpostJson.Sha256Hex(Account)) : Proof(true);
                if (kind == "legacy_price") p.Profile.PendingPurchase.PriceMicros = 0;
                if (kind == "invalid_price") p.Profile.PendingPurchase.Currency = "usd";
                await pipe.ValidateAndDeliver(proof, false, kind == "restored" ? null : kind == "wrong_order" ? "other-order" : "order-1");
                var sink = new RevenueSink(); await new PaymentRevenue(p).Dispatch(sink);
                Assert.IsEmpty(sink.Calls, kind); Assert.AreEqual(160, p.Profile.Gems);
            }
        }
        [Test]
        public async Task RecipientsAreIndependentAndNotConfiguredRemainsRecoverable()
        {
            var (p, b, pipe) = await Prepared(); await Fresh(p, pipe);
            var sink = new RevenueSink { Throw = PaymentRevenueRecipient.AppMetrica };
            sink.Waiting[PaymentRevenueRecipient.Adjust] = "not_configured";
            sink.Waiting[PaymentRevenueRecipient.Meta] = "waiting_for_consent";
            await new PaymentRevenue(p).Dispatch(sink); p.Relaunch();
            var d = p.Profile.Payments.Single(); Assert.AreEqual("ambiguous_not_resent", d.AppMetricaRevenue);
            Assert.AreEqual("not_configured", d.AdjustRevenue); Assert.AreEqual("waiting_for_consent", d.MetaRevenue);
            sink.Waiting.Clear(); await new PaymentRevenue(p).Dispatch(sink);
            Assert.AreEqual(3, sink.Calls.Count); Assert.AreEqual(1, sink.Calls.Count(x => x == PaymentRevenueRecipient.AppMetrica));
            await pipe.Claim(p.Profile.Payments.Single()); await pipe.Confirmed(d.TokenHash);
            Assert.AreEqual(160, p.Profile.Gems); Assert.IsFalse(PaymentRules.Pending(p.Profile));
        }
        [Test]
        public async Task CrashDispatchingIsAmbiguousAndOtherRecipientsStillRun()
        {
            var (p, b, pipe) = await Prepared(); await Fresh(p, pipe);
            await p.Save(x => x.Payments.Single().MetaRevenue = "dispatching"); p.Relaunch();
            var sink = new RevenueSink(); await new PaymentRevenue(p).Dispatch(sink);
            Assert.AreEqual("ambiguous_not_resent", p.Profile.Payments.Single().MetaRevenue);
            Assert.AreEqual(2, sink.Calls.Count); Assert.IsFalse(sink.Calls.Contains(PaymentRevenueRecipient.Meta));
        }
        [Test]
        public async Task DispatchSaveFailureNeverCallsSdkOrChangesMoneyAndCanRetry()
        {
            var (p, b, pipe) = await Prepared(); await Fresh(p, pipe); p.Files.ArmCrash(1, false);
            var sink = new RevenueSink(); await new PaymentRevenue(p).Dispatch(sink);
            Assert.IsEmpty(sink.Calls); Assert.AreEqual(160, p.Profile.Gems);
            p.Files = p.Files.CrashImage(); p.Saves = new OutpostSaveService(SaveKitA.Root, p.Files) { Configs = OutpostKitA.Configs, MapResolver = s => OutpostKitA.Map(s.Run.Zone) };
            await pipe.Claim(p.Profile.Payments.Single()); await pipe.Confirmed(p.Profile.Payments.Single().TokenHash);
            await new PaymentRevenue(p).Dispatch(sink); Assert.AreEqual(3, sink.Calls.Count);
        }
        [Test]
        public async Task PostCallSaveFailureCannotCauseAnotherInvocation()
        {
            var (p, b, pipe) = await Prepared(); await Fresh(p, pipe);
            var sink = new RevenueSink(); var crash = new CrashRevenueSink(sink, () => p.Files.ArmCrash(1, false));
            await new PaymentRevenue(p).Dispatch(crash);
            Assert.AreEqual(1, sink.Calls.Count); Assert.AreEqual("dispatching", p.Profile.Payments.Single().AppMetricaRevenue);
            p.Files = p.Files.CrashImage(); p.Saves = new OutpostSaveService(SaveKitA.Root, p.Files) { Configs = OutpostKitA.Configs, MapResolver = s => OutpostKitA.Map(s.Run.Zone) };
            p.Relaunch(); await new PaymentRevenue(p).Dispatch(sink);
            Assert.AreEqual(3, sink.Calls.Count); Assert.AreEqual("ambiguous_not_resent", p.Profile.Payments.Single().AppMetricaRevenue);
        }
        [Test]
        public async Task LegacySchema2WithoutRevenueFieldsIsSafeAndPreservesGrant()
        {
            var (p, b, pipe) = await Prepared(); await Fresh(p, pipe);
            var env = SaveKitA.ReadEnvelope(p.Files, SaveKitA.Main); var payload = SaveKitA.Payload(env);
            var record = (JObject)payload["Profile"]["Payments"][0];
            foreach (var field in new[] { "PriceMicros", "Currency", "NewRevenueEligible", "AppMetricaRevenue", "MetaRevenue", "AdjustRevenue" }) record.Remove(field);
            SaveKitA.SetPayload(env, payload, true); SaveKitA.WriteEnvelope(p.Files, SaveKitA.Main, env); p.Files.Delete(SaveKitA.Bak);
            p.Relaunch(); var sink = new RevenueSink(); await new PaymentRevenue(p).Dispatch(sink);
            Assert.IsEmpty(sink.Calls); Assert.AreEqual(160, p.Profile.Gems);
            await pipe.Claim(p.Profile.Payments.Single()); await pipe.Confirmed(p.Profile.Payments.Single().TokenHash);
        }
        [Test]
        public async Task UnverifiedRewardAndFailedGrantNeverAuthorizeAnyRecipient()
        {
            var (p, b, pipe) = await Prepared(); b.Inbox["rewards"][0]["items"] = Items(500);
            Assert.ThrowsAsync<PaymentException>(async () => await Fresh(p, pipe));
            var sink = new RevenueSink(); await new PaymentRevenue(p).Dispatch(sink); Assert.IsEmpty(sink.Calls);
            b.Inbox["rewards"][0]["items"] = Items(); p.Files.ArmCrash(1, false);
            Assert.ThrowsAsync<PaymentException>(async () => await Fresh(p, pipe));
            await new PaymentRevenue(p).Dispatch(sink); Assert.IsEmpty(sink.Calls); Assert.AreEqual(0, p.Profile.Gems);
        }
        sealed class CrashRevenueSink : IPaymentRevenueSink
        {
            readonly RevenueSink sink; readonly Action crash;
            public CrashRevenueSink(RevenueSink sink, Action crash) { this.sink = sink; this.crash = crash; }
            public string Unavailable(PaymentRevenueRecipient r) => sink.Unavailable(r);
            public void Report(PaymentRevenueRecipient r, PaymentDelivery d) { sink.Report(r, d); crash(); }
        }
        [Test]
        public void CatalogRejectsWrongRewardTypeMappingsAndLocalBonus()
        {
            PaymentRules.Catalog(Catalog());
            var c = Catalog(); c["products"][0]["productType"] = "NonConsumable";
            Assert.Throws<PaymentException>(() => PaymentRules.Catalog(c));
            c = Catalog(); c["products"][0]["googlePlaySku"] = "wrong";
            Assert.Throws<PaymentException>(() => PaymentRules.Catalog(c));
            c = Catalog(); c["products"][0]["rewards"] = Items(161);
            Assert.Throws<PaymentException>(() => PaymentRules.Catalog(c));
            var local = OutpostJson.DeepCopy(OutpostKitA.Configs.Store); PaymentRules.LocalCatalog(local);
            local.TryGet(Sku, out var item); item.BonusAmount = 320;
            Assert.Throws<PaymentException>(() => PaymentRules.LocalCatalog(local));
        }
        [Test]
        public async Task UnknownPurchaseInboxAndDeferredIntentCannotBeMockGranted()
        {
            var p = new Progress(); var b = new Backend(); var pipe = new PaymentPipeline(b, p);
            Assert.ThrowsAsync<PaymentException>(async () => await pipe.ValidateAndDeliver(Proof(), false)); Assert.IsEmpty(b.Calls);
            await pipe.Prepare(Sku, "1.99", "USD"); p.Relaunch(); Assert.AreEqual(0, p.Profile.Gems);
            Assert.ThrowsAsync<PaymentException>(async () => await pipe.Prepare(Sku, "1.99", "USD"));
        }
    }
}

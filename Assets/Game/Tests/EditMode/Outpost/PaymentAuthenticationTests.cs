using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CatMiners.Core.Outpost;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CatMiners.Tests.Outpost
{
    public class PaymentAuthenticationTests
    {
        sealed class Storage : IPrivatePaymentStorage
        {
            public JObject Session;
            public bool Fail;
            public JObject Load() => Session == null ? null : (JObject)Session.DeepClone();
            public void Save(JObject next) { if (Fail) throw new PaymentException("disk_failure"); Session = (JObject)next.DeepClone(); }
        }
        sealed class Transport : IPaymentTransport
        {
            public readonly List<string> Paths = new List<string>();
            public readonly List<string> Devices = new List<string>();
            public long LoginFailure, RefreshFailure, ProtectedFailure;
            public string RefreshUser = "guest-1";
            public bool Permanent401;
            public Task<JObject> Send(string path, JObject body, string token)
            {
                Paths.Add(path);
                if (path == "/api/auth/login/guest")
                {
                    Devices.Add((string)body["deviceId"]);
                    Assert.AreEqual(PaymentRules.GameId, (string)body["gameId"]); Assert.IsNull(token);
                    if (LoginFailure != 0) throw new PaymentHttpException(LoginFailure);
                    return Task.FromResult(new JObject { ["credentials"] = new JObject { ["userId"] = "guest-1", ["authToken"] = "auth-1", ["refreshToken"] = "refresh-1" } });
                }
                if (path == "/api/auth/refresh")
                {
                    if (RefreshFailure != 0) throw new PaymentHttpException(RefreshFailure);
                    Assert.AreEqual("refresh-1", (string)body["refreshToken"]);
                    return Task.FromResult(new JObject { ["userId"] = RefreshUser, ["authToken"] = "auth-2" });
                }
                if (ProtectedFailure != 0) throw new PaymentHttpException(ProtectedFailure);
                if (Permanent401 || token == "auth-1") throw new PaymentHttpException(401);
                return Task.FromResult(new JObject { ["ok"] = true });
            }
        }
        [Test]
        public async Task OnePersistentUuidPerInstallationAndOneRefresh()
        {
            var storage = new Storage(); var transport = new Transport(); var client = new AuthenticatedPaymentBackend(transport, storage);
            var result = await client.Request("/api/products/get"); Assert.IsTrue((bool)result["ok"]);
            Assert.AreEqual(1, transport.Paths.Count(p => p == "/api/auth/login/guest")); Assert.AreEqual(1, transport.Paths.Count(p => p == "/api/auth/refresh"));
            var uuid = (string)storage.Session["deviceId"]; Assert.IsTrue(Guid.TryParse(uuid, out _));
            var restarted = new AuthenticatedPaymentBackend(transport, storage); await restarted.Request("/api/products/get");
            Assert.AreEqual(uuid, (string)storage.Session["deviceId"]); Assert.AreEqual("guest-1", restarted.UserId);
            Assert.AreEqual(1, transport.Paths.Count(p => p == "/api/auth/login/guest"));
        }
        [TestCase(503)] [TestCase(408)]
        public void LoginTimeoutOr5xxNeverChangesDeviceIdentity(long error)
        {
            var storage = new Storage(); var t = new Transport { LoginFailure = error }; var c = new AuthenticatedPaymentBackend(t, storage);
            Assert.ThrowsAsync<PaymentHttpException>(async () => await c.Request("/api/products/get"));
            var relaunched = new AuthenticatedPaymentBackend(t, storage);
            Assert.ThrowsAsync<PaymentHttpException>(async () => await relaunched.Request("/api/products/get"));
            Assert.AreEqual(t.Devices[0], t.Devices[1]); Assert.IsNull(c.UserId);
        }
        [TestCase(401)] [TestCase(503)] [TestCase(408)]
        public void RefreshFailureNeverLogsInDifferentPlayer(long error)
        {
            var storage = new Storage(); var t = new Transport { RefreshFailure = error }; var c = new AuthenticatedPaymentBackend(t, storage);
            Assert.ThrowsAsync<PaymentHttpException>(async () => await c.Request("/api/products/get"));
            Assert.AreEqual("guest-1", c.UserId); Assert.AreEqual(1, t.Paths.Count(p => p == "/api/auth/login/guest"));
            Assert.AreEqual(1, t.Paths.Count(p => p == "/api/auth/refresh")); Assert.AreEqual("refresh-1", (string)storage.Session["refreshToken"]);
        }
        [Test]
        public void Permanent401RefreshesOnlyOnceAndChangedUserRejects()
        {
            var storage = new Storage(); var t = new Transport { Permanent401 = true }; var c = new AuthenticatedPaymentBackend(t, storage);
            Assert.ThrowsAsync<PaymentHttpException>(async () => await c.Request("/api/products/get"));
            Assert.AreEqual(1, t.Paths.Count(p => p == "/api/auth/refresh"));
            t.RefreshUser = "different";
            Assert.ThrowsAsync<PaymentException>(async () => await c.Request("/api/products/get"));
            Assert.AreEqual("guest-1", c.UserId);
        }
        [Test]
        public async Task CredentialWriteFailureNeverUsesUndurableLoginOrRefresh()
        {
            var storage = new Storage(); var t = new Transport(); var c = new AuthenticatedPaymentBackend(t, storage);
            storage.Fail = true;
            Assert.ThrowsAsync<PaymentException>(async () => await c.Authenticate()); Assert.IsNull(c.UserId);
            storage.Fail = false; await c.Authenticate(); storage.Fail = true;
            Assert.ThrowsAsync<PaymentException>(async () => await c.Request("/api/products/get"));
            Assert.AreEqual("auth-1", (string)storage.Session["authToken"]);
        }
        [TestCase(503)] [TestCase(408)]
        public void AuthenticatedOfflineDoesNotRefreshOrRecreatePlayer(long code)
        {
            var storage = new Storage(); var t = new Transport { ProtectedFailure = code }; var c = new AuthenticatedPaymentBackend(t, storage);
            Assert.ThrowsAsync<PaymentHttpException>(async () => await c.Request("/api/products/get"));
            Assert.AreEqual(0, t.Paths.Count(p => p == "/api/auth/refresh")); Assert.AreEqual("guest-1", c.UserId);
        }
        [Test]
        public void CorruptIdentityIsBlockedRatherThanReplaced()
        {
            var storage = new Storage { Session = new JObject { ["deviceId"] = "invalid" } };
            Assert.Throws<PaymentException>(() => new AuthenticatedPaymentBackend(new Transport(), storage));
            Assert.AreEqual("invalid", (string)storage.Session["deviceId"]);
        }
    }
}

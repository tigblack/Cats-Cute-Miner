using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CatMiners.Core.Outpost
{
    public sealed class PaymentHttpException : PaymentException
    {
        public readonly long Code;
        public PaymentHttpException(long code) : base(code == 401 ? "authentication_required" : code >= 500 || code == 0 ? "server_unavailable" : "server_rejected") { Code = code; }
    }
    public interface IPaymentTransport { Task<JObject> Send(string endpoint, JObject body, string token); }
    public interface IPrivatePaymentStorage { JObject Load(); void Save(JObject session); }
    // Independently testable authentication policy shared by the actual Android transport.
    public sealed class AuthenticatedPaymentBackend : IPaymentBackend
    {
        readonly IPaymentTransport transport;
        readonly IPrivatePaymentStorage storage;
        JObject session;
        public string UserId => PaymentRules.String(session, "userId");
        public AuthenticatedPaymentBackend(IPaymentTransport transport, IPrivatePaymentStorage storage)
        {
            this.transport = transport; this.storage = storage;
            session = storage.Load();
            if (session == null) { session = new JObject { ["deviceId"] = Guid.NewGuid().ToString("D") }; storage.Save(session); }
            if (!Guid.TryParse(PaymentRules.Id(session, "deviceId"), out _)) throw new PaymentException("device_identity_malformed");
        }
        static string Secret(JToken root, string key)
        {
            var value = PaymentRules.String(root, key);
            if (string.IsNullOrWhiteSpace(value) || value.Length > 16384) throw new PaymentException("credentials_malformed"); return value;
        }
        void Persist(JObject next) { storage.Save(next); session = next; }
        public async Task Authenticate()
        {
            if (!string.IsNullOrEmpty(UserId)) { PaymentRules.Id(session, "userId"); Secret(session, "authToken"); Secret(session, "refreshToken"); return; }
            var login = await transport.Send("/api/auth/login/guest", new JObject { ["deviceId"] = PaymentRules.Id(session, "deviceId"), ["gameId"] = PaymentRules.GameId, ["createProfile"] = true }, null);
            var credentials = login["credentials"];
            var next = (JObject)session.DeepClone(); next["userId"] = PaymentRules.Id(credentials, "userId");
            next["authToken"] = Secret(credentials, "authToken"); next["refreshToken"] = Secret(credentials, "refreshToken"); Persist(next);
        }
        public async Task<JObject> Request(string endpoint, JObject body = null)
        {
            await Authenticate();
            try { return await transport.Send(endpoint, body, Secret(session, "authToken")); }
            catch (PaymentHttpException e) when (e.Code == 401)
            {
                var refresh = await transport.Send("/api/auth/refresh", new JObject { ["refreshToken"] = Secret(session, "refreshToken"), ["gameId"] = PaymentRules.GameId }, null);
                if (PaymentRules.Id(refresh, "userId") != UserId) throw new PaymentException("account_mismatch");
                var next = (JObject)session.DeepClone(); next["authToken"] = Secret(refresh, "authToken"); Persist(next);
                return await transport.Send(endpoint, body, Secret(session, "authToken"));
            }
        }
    }
}

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CatMiners.Core.Outpost;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace CatMiners.Runtime.Purchasing
{
    // Android Keystore AES-GCM, installation-private noBackupFilesDir. No PlayerPrefs, progress, logs or backups.
    sealed class PrivateGuestSession : IPaymentBackend, IPaymentTransport, IPrivatePaymentStorage
    {
        readonly AuthenticatedPaymentBackend client;
        readonly string path;
        public string UserId => client.UserId;
        public PrivateGuestSession(bool requireExistingIdentity)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var dir = activity.Call<AndroidJavaObject>("getNoBackupFilesDir"))
                path = Path.Combine(dir.Call<string>("getAbsolutePath"), "multicast-guest.enc");
            if (requireExistingIdentity && !File.Exists(path)) throw new PaymentException("purchase_identity_missing");
            client = new AuthenticatedPaymentBackend(this, this);
#else
            throw new PaymentException("unsupported_platform");
#endif
        }
        public JObject Load() => File.Exists(path) ? JObject.Parse(Encoding.UTF8.GetString(Crypt(File.ReadAllBytes(path), false))) : null;
        public void Save(JObject session)
        {
            var bytes = Crypt(Encoding.UTF8.GetBytes(session.ToString(Newtonsoft.Json.Formatting.None)), true);
            using (var f = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None)) { f.Write(bytes, 0, bytes.Length); f.Flush(true); }
            #if UNITY_ANDROID && !UNITY_EDITOR
            using (var os = new AndroidJavaClass("android.system.Os")) os.CallStatic("rename", path + ".tmp", path);
#else
            throw new PaymentException("unsupported_platform");
#endif
        }
        static byte[] Crypt(byte[] data, bool encrypt)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            const string alias = "cats_cute_miner_multicast_v1";
            using (var keyStoreClass = new AndroidJavaClass("java.security.KeyStore"))
            using (var keys = keyStoreClass.CallStatic<AndroidJavaObject>("getInstance", "AndroidKeyStore"))
            {
                keys.Call("load", new object[] { null });
                if (!keys.Call<bool>("containsAlias", alias))
                {
                    if (!encrypt) throw new PaymentException("credential_key_missing");
                    using (var builder = new AndroidJavaObject("android.security.keystore.KeyGenParameterSpec$Builder", alias, 3))
                    using (var spec = builder.Call<AndroidJavaObject>("setBlockModes", new object[] { new string[] { "GCM" } }))
                    using (var padded = builder.Call<AndroidJavaObject>("setEncryptionPaddings", new object[] { new string[] { "NoPadding" } }))
                    using (var built = builder.Call<AndroidJavaObject>("build"))
                    using (var generatorClass = new AndroidJavaClass("javax.crypto.KeyGenerator"))
                    using (var generator = generatorClass.CallStatic<AndroidJavaObject>("getInstance", "AES", "AndroidKeyStore"))
                    { generator.Call("init", built); using (generator.Call<AndroidJavaObject>("generateKey")) { } }
                }
                using (var key = keys.Call<AndroidJavaObject>("getKey", alias, null))
                using (var cipherClass = new AndroidJavaClass("javax.crypto.Cipher"))
                using (var cipher = cipherClass.CallStatic<AndroidJavaObject>("getInstance", "AES/GCM/NoPadding"))
                {
                    if (encrypt)
                    {
                        cipher.Call("init", 1, key);
                        var iv = cipher.Call<byte[]>("getIV"); var encrypted = cipher.Call<byte[]>("doFinal", data);
                        if (iv.Length != 12) throw new PaymentException("credential_cipher_failed");
                        var result = new byte[12 + encrypted.Length]; Buffer.BlockCopy(iv, 0, result, 0, 12); Buffer.BlockCopy(encrypted, 0, result, 12, encrypted.Length); return result;
                    }
                    if (data.Length < 28) throw new PaymentException("credential_file_corrupt");
                    var nonce = new byte[12]; var ciphertext = new byte[data.Length - 12]; Buffer.BlockCopy(data, 0, nonce, 0, 12); Buffer.BlockCopy(data, 12, ciphertext, 0, ciphertext.Length);
                    using (var spec = new AndroidJavaObject("javax.crypto.spec.GCMParameterSpec", 128, nonce)) cipher.Call("init", 2, key, spec);
                    return cipher.Call<byte[]>("doFinal", ciphertext);
                }
            }
#else
            throw new PaymentException("unsupported_platform");
#endif
        }
        public Task Authenticate() => client.Authenticate();
        public Task<JObject> Request(string endpoint, JObject body = null) => client.Request(endpoint, body);
        public async Task<JObject> Send(string endpoint, JObject body, string token)
        {
            using (var request = new UnityWebRequest(PaymentRules.BaseUrl + endpoint, body == null ? "GET" : "POST"))
            {
                request.downloadHandler = new DownloadHandlerBuffer(); request.timeout = 20;
                if (body != null) { request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None))); request.SetRequestHeader("Content-Type", "application/json"); }
                if (token != null) request.SetRequestHeader("Authorization", "Bearer " + token);
                var operation = request.SendWebRequest(); while (!operation.isDone) await Task.Yield();
                if (request.result != UnityWebRequest.Result.Success) throw new PaymentHttpException(request.responseCode);
                if (string.IsNullOrWhiteSpace(request.downloadHandler.text)) return new JObject();
                try { return JObject.Parse(request.downloadHandler.text, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }); }
                catch { throw new PaymentException("server_malformed"); }
            }
        }
    }
}

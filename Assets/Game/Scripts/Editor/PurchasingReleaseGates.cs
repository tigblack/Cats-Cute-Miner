using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatMiners.Core.Outpost;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace CatMiners.Editor.GooglePlay
{
    public sealed class BillingRuntimeGate : BuildAndroid.IReleaseGate
    {
        public string Provider => "Billing";
        public IEnumerable<string> Validate(string packageName)
        {
            if (packageName != PaymentRules.Package) yield return "Billing package does not match accepted permanent game identity.";
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.purchasing");
            if (info?.version != "5.2.1") yield return "Unity IAP must resolve the verified 5.2.1 APIs.";
            if (info?.resolvedPath == null || !Directory.Exists(info.resolvedPath)) yield return "Resolved IAP sources unavailable.";
            else
            {
                var native = Directory.GetFiles(info.resolvedPath, "IAPAndroidDependencies.cs", SearchOption.AllDirectories);
                if (!native.Any(p => File.ReadAllText(p).Contains("8.3.0"))) yield return "Installed IAP Billing 8.3.0 dependency declaration missing; artifact resolution remains required.";
            }
            var symbols = PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.Android);
            if (symbols.Contains("IAP_TX_VERIFIER_ENABLED")) yield return "Transaction verifier/Unity Analytics must remain disabled for the mixed audience.";
            string path = "Assets/Game/Scripts/Runtime/Purchasing/GooglePaymentsRuntime.cs";
            if (!File.Exists(path) || !new[] { "UnityIAPServices.StoreController()", "OnPurchasePending", "ConfirmPurchase", "ProcessPendingOrdersOnPurchasesFetched(false)" }.All(s => File.ReadAllText(path).Contains(s)))
                yield return "Live IAP5 callback/confirmation wiring missing.";
            // Store activation and device checks follow the initial billing-enabled internal build.
            // Their pending status is kept separately; it cannot make the prerequisite build impossible.
        }
        public static string RequiredExternalEvidence => "Google product activation, installed internal billing release, artifact Billing version and real license-test/relaunch evidence remain separate pending gates.";
    }
    public sealed class MulticastPaymentsRuntimeGate : BuildAndroid.IReleaseGate
    {
        public string Provider => "MulticastPayments";
        public IEnumerable<string> Validate(string packageName)
        {
            if (packageName != PaymentRules.Package || PaymentRules.GameId != "gm_cats_cute_miner" || PaymentRules.BaseUrl != "https://backend.api-multicastgames.org")
                yield return "Own Multicast game/platform mapping missing.";
            var catalog = Resources.Load<TextAsset>("Outpost/store_catalog");
            bool valid = false;
            try { if (catalog != null) { PaymentRules.LocalCatalog(StoreCatalog.Parse(catalog.text)); valid = true; } } catch { }
            if (!valid) yield return "Six accepted consumable gem quantities/store IDs conflict with game catalog.";
            string controller = "Assets/Game/Scripts/Runtime/Outpost/OutpostController.Purchasing.cs";
            if (!File.Exists(controller) || !File.ReadAllText(controller).Contains("controller.Commit(prepared, map") || !File.ReadAllText(controller).Contains("resetBackup: true"))
                yield return "Real balance and payment ledger durable commit wiring missing.";
            foreach (var source in new[] {
                new { Path = "Assets/Game/Scripts/Runtime/Purchasing/PrivateGuestSession.cs", Marker = "SetRequestHeader(\"Authorization\", \"Bearer \" + token)" },
                new { Path = "Assets/Game/Scripts/Core/Outpost/Purchasing/PaymentDelivery.cs", Marker = "/api/payment/google-play" },
                new { Path = "Assets/Game/Scripts/Runtime/Outpost/OutpostController.Commands.cs", Marker = ".Buy(itemId)" },
                new { Path = "Assets/Game/Scripts/Runtime/Outpost/UI/OutpostHud.Store.cs", Marker = "RecoverPurchases" }
            })
                if (!File.Exists(source.Path) || !File.ReadAllText(source.Path).Contains(source.Marker)) yield return "Authenticated live store consumer missing: " + source.Path;
            // Actual payment/recovery proofs belong to device QA after an installable billing release.
        }
        public static string RequiredDeviceEvidence => "Real payment/reward IDs, durable grant, separate claim/consume retries and device recovery remain pending; catalog registration is not a payment test.";
    }
}

#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace CatMiners.Tests
{
    public sealed class ReleaseDiagnosticDefineGuardTests
    {
        [TestCase("UNITY_ENABLE_CLOUD_SERVICES_ENGINE_DIAGNOSTICS_MANAGED", false)]
        [TestCase("ENABLE_CLOUD_SERVICES_ENGINE_DIAGNOSTICS", false)]
        [TestCase("CATMINERS_APPMETRICA_DIAGNOSTICS", true)]
        [TestCase("DEVELOPMENT_BUILD", true)]
        public void NativeUnityDiagnosticsRemainAllowedWhileLocalConsoleCannotShip(string define, bool blocked)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(x => x.GetType("CatMiners.Editor.GooglePlay.BuildAndroid", false))
                .FirstOrDefault(x => x != null);
            Assert.IsNotNull(type);
            var method = type.GetMethod("UnsafeDefine", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            Assert.AreEqual(blocked, method.Invoke(null, new object[] { define }));
        }
    }
}
#endif

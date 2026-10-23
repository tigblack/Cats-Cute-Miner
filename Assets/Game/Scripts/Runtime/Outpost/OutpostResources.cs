using System;
using CatMiners.Core.Data;
using CatMiners.Core.Outpost;
using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    public static class OutpostResources
    {
        public static OutpostConfigSet LoadConfigs()
        {
            var texts = new string[OutpostConfigSet.ResourcePaths.Length];
            for (int i = 0; i < texts.Length; i++)
            {
                var asset = Resources.Load<TextAsset>(OutpostConfigSet.ResourcePaths[i]);
                if (asset == null) throw new InvalidOperationException("Missing resource: " + OutpostConfigSet.ResourcePaths[i]);
                texts[i] = asset.text;
            }
            // Все файлы по порядку ResourcePaths, включая factory_rules (ТЗ 2.2 §3.2).
            return OutpostConfigSet.ParseResources(texts);
        }

        public static BalanceRepository LoadSource(TextAsset source)
        {
            if (source == null) throw new InvalidOperationException("GameBootstrap.BalanceJson is missing");
            // Load verifies the immutable source checksum, also in Player/Android.
            return BalanceRepository.Load(source.bytes);
        }
    }
}

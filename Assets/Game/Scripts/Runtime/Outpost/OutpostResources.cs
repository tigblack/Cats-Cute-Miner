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
            return OutpostConfigSet.Parse(texts[0], texts[1], texts[2], texts[3], texts[4], texts[5], texts[6]);
        }

        public static BalanceRepository LoadSource(TextAsset source)
        {
            if (source == null) throw new InvalidOperationException("GameBootstrap.BalanceJson is missing");
            // Load verifies the immutable source checksum, also in Player/Android.
            return BalanceRepository.Load(source.bytes);
        }
    }
}

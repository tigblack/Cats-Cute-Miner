using CatMiners.Core.Outpost;
using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    public sealed partial class OutpostTargetView
    {
        // Assemble the entrance entirely from the game's existing 32 px/cell art.
        // No imported poster, resized bitmap, new texture or material is needed.
        static Sprite s_NativeFinalFrame;
        static readonly string[] s_FinalPiles =
        {
            "debris_rubble_0", "debris_rubble_1", "debris_planks_3",
            "debris_rubble_1", "debris_rubble_2", "debris_scrap_0",
        };
        static readonly Vector2[] s_FinalPileAt =
        {
            new Vector2(-0.65f, 0.65f), new Vector2(0.65f, 0.55f), new Vector2(0, 0.45f),
            new Vector2(-1, 0), new Vector2(0, 0), new Vector2(1, 0),
        };
        SpriteRenderer[] m_FinalPiles;

        void BuildNativeFinalEntrance(OutpostMap map, MapTarget target)
        {
            var opening = Spr("tunnelInterior", "px_white", new Vector2(0, 0.875f), LayerShadow, PixelPalette.Navy0);
            opening.transform.localScale = new Vector3(90, 48, 1);
            for (int side = -1; side <= 1; side += 2)
                Spr("entranceRails", "rails_v", new Vector2(side * 0.25f, 0.5f), LayerShadow + 1);
            m_Body = Spr("nativeEntranceFrame", "gate_frame", Vector2.zero, LayerBody);
            if (s_NativeFinalFrame == null)
            {
                // Keep the central four tiles at native pixel density; discard the side fence wings.
                var source = m_Body.sprite;
                var rect = source.rect;
                rect.x += 16;
                rect.width = 128;
                s_NativeFinalFrame = Sprite.Create(source.texture, rect,
                    new Vector2(0.5f, source.pivot.y / rect.height), source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                s_NativeFinalFrame.name = "native_final_frame";
            }
            m_Body.sprite = s_NativeFinalFrame;
            m_FinalPiles = new SpriteRenderer[s_FinalPiles.Length];
            for (int i = 0; i < m_FinalPiles.Length; i++)
            {
                var at = s_FinalPileAt[i];
                at.x = PixelWorld.Snap(at.x);
                at.y = PixelWorld.Snap(at.y);
                m_FinalPiles[i] = Spr("entranceRubble" + i, s_FinalPiles[i], at, LayerOver1 + i);
            }
            BuildDebris(map, target);
        }

        void ApplyNativeFinalStage(int stage)
        {
            if (m_FinalPiles == null) return;
            for (int i = 0; i < m_FinalPiles.Length; i++)
            {
                bool visible = stage < 5 && (stage < 2 || (stage == 2 && i >= 2) || (stage == 3 && i >= 4) || (stage == 4 && i == 4));
                m_FinalPiles[i].enabled = visible;
                if (!visible) continue;
                string damage = stage >= 3 ? "_d2" : stage >= 1 ? "_d1" : "";
                PixelWorld.SetSprite(m_FinalPiles[i], s_FinalPiles[i] + damage);
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace MazeRush.Shop
{
    [CreateAssetMenu(menuName = "MazeRush/Cosmetic Catalog")]
    public class CosmeticCatalog : ScriptableObject
    {
        public List<CosmeticItem> items = new List<CosmeticItem>();

        static CosmeticCatalog _default;
        public static CosmeticCatalog Default
        {
            get
            {
                if (_default == null)
                {
                    _default = CreateInstance<CosmeticCatalog>();
                    _default.items = BuildDefaults();
                }
                return _default;
            }
        }

        public CosmeticItem Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var i in items) if (i.id == id) return i;
            return null;
        }

        static CosmeticItem I(string id, string n, CosmeticCategory c, int p, Color col) =>
            new CosmeticItem { id = id, displayName = n, category = c, price = p, color = col };

        // Every cosmetic is a color. "color_rainbow" cycles hue over time (see RainbowId).
        public const string RainbowId = "color_rainbow";

        static List<CosmeticItem> BuildDefaults() => new List<CosmeticItem>
        {
            I("color_white", "White", CosmeticCategory.Color, 0, Color.white),
            I("color_red", "Red", CosmeticCategory.Color, 12, new Color(1f, 0.2f, 0.2f)),
            I("color_blue", "Blue", CosmeticCategory.Color, 12, new Color(0.25f, 0.5f, 1f)),
            I("color_lime", "Lime", CosmeticCategory.Color, 20, new Color(0.6f, 1f, 0.2f)),
            I("color_violet", "Violet", CosmeticCategory.Color, 20, new Color(0.65f, 0.35f, 1f)),
            I("color_gold", "Gold", CosmeticCategory.Color, 48, new Color(1f, 0.82f, 0.15f)),
            I(RainbowId, "Rainbow (changes color)", CosmeticCategory.Color, 100, Color.white),
        };
    }
}

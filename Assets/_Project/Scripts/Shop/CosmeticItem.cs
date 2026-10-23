using System;
using UnityEngine;

namespace MazeRush.Shop
{
    public enum CosmeticCategory { Skin, Color, Accessory }

    [Serializable]
    public class CosmeticItem
    {
        public string id;
        public string displayName;
        public CosmeticCategory category;
        public int price;
        public Color color = Color.white;
    }
}

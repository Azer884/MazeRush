using System;
using System.IO;
using System.Linq;
using MazeRush.Shop;
using UnityEngine;

namespace MazeRush.Services
{
    // Per-player JSON save on this machine: coins, owned colors, equipped color, stats.
    // Not tamper-proof (the player can edit the file). Swap back to Cloud Code for real security.
    public static class LocalSave
    {
        public const int MaxCoinsPerMatch = 40;
        public const string DefaultColor = "color_white";

        static string PathFor(string username)
        {
            string safe = new string((username ?? "player").Where(char.IsLetterOrDigit).ToArray());
            if (safe.Length == 0) safe = "player";
            return Path.Combine(Application.persistentDataPath, "save_" + safe + ".json");
        }

        public static PlayerProfile Load(string username)
        {
            PlayerProfile p = null;
            try
            {
                string path = PathFor(username);
                if (File.Exists(path)) p = JsonUtility.FromJson<PlayerProfile>(File.ReadAllText(path));
            }
            catch (Exception e) { Debug.LogWarning("[LocalSave] load failed, starting fresh: " + e.Message); }

            if (p == null) p = new PlayerProfile();
            p.username = username;
            if (p.owned == null) p.owned = new System.Collections.Generic.List<string>();
            if (!p.owned.Contains(DefaultColor)) p.owned.Add(DefaultColor);
            if (string.IsNullOrEmpty(p.equippedSkin)) p.equippedSkin = DefaultColor;
            return p;
        }

        // Write to a temp file then swap, so a crash mid-write never corrupts the save.
        public static void Save(PlayerProfile p)
        {
            try
            {
                string path = PathFor(p.username);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(p));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e) { Debug.LogWarning("[LocalSave] save failed: " + e.Message); }
        }

        public static PlayerProfile Purchase(string username, string id)
        {
            var p = Load(username);
            var item = CosmeticCatalog.Default.Find(id);
            if (item == null) throw new BackendException("Unknown item.");
            if (p.owned.Contains(id)) return p;                      // already owned: never charge twice
            if (p.coins < item.price) throw new BackendException("Not enough coins.");
            p.coins -= item.price;
            p.owned.Add(id);
            Save(p);
            return p;
        }

        public static PlayerProfile Equip(string username, string id)
        {
            var p = Load(username);
            if (CosmeticCatalog.Default.Find(id) == null) throw new BackendException("Unknown item.");
            if (!p.owned.Contains(id)) throw new BackendException("You do not own that item.");
            p.equippedSkin = id;
            Save(p);
            return p;
        }

        public static PlayerProfile ReportMatch(string username, bool won, int coins)
        {
            var p = Load(username);
            p.coins += Mathf.Clamp(coins, 0, MaxCoinsPerMatch);
            p.gamesPlayed++;
            if (won) p.wins++;
            Save(p);
            return p;
        }
    }
}

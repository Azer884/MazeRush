using System;
using System.Collections.Generic;

namespace MazeRush.Services
{
    // Client-side snapshot of server-owned data. Never trusted by the server.
    [Serializable]
    public class PlayerProfile
    {
        public string username = "";
        public int coins;                                   // permanent balance (Economy)
        public List<string> owned = new List<string>();     // cosmetic ids
        public string equippedSkin = "";
        public string equippedAccessory = "";
        public int wins;
        public int gamesPlayed;
    }
}

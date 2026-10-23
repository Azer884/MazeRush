using System;
using System.Collections.Generic;
using MazeRush.Services;
using UnityEngine;

namespace MazeRush
{
    // Builds the identical maze on every peer from the host's seed (via MatchState), shows the HUD.
    // Inspector fields are the HOST's match config; clients use the replicated values.
    public class MazeGame : MonoBehaviour
    {
        public int cells = 12;
        public int seed = 0; // 0 = random each match
        public int coinCount = 15;
        public int coinsRequired = 10;
        public float cellSize = 3f;
        public float wallHeight = 3f;

        // Server-side only: lets the monster agent hook into the freshly built maze.
        public static event Action<bool[,], float, int> MazeBuilt;
        public static MazeGame Instance { get; private set; }

        public int CoinCount => coinPos.Count;
        public Vector3 ExitWorld { get; private set; }

        float cs; // replicated cell size in use
        readonly List<Vector3> coinPos = new List<Vector3>();
        readonly List<GameObject> coinObjs = new List<GameObject>();
        GameObject root;
        string notice = "";
        float noticeUntil;
        bool unlocked;
        Material wallMat, floorMat, coinMat, exitLockedMat, exitOpenMat;
        Renderer exitRenderer;

        void Awake() { Instance = this; }

        public static MazeGame Ensure()
        {
            if (Instance == null) new GameObject("MazeGame").AddComponent<MazeGame>();
            return Instance;
        }

        void MakeMaterials()
        {
            if (wallMat != null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Material M(Color c, bool glow = false)
            {
                var m = new Material(shader); m.SetColor("_BaseColor", c);
                if (glow) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * 2.5f); }
                return m;
            }
            wallMat = M(new Color(0.25f, 0.3f, 0.45f));
            floorMat = M(new Color(0.18f, 0.18f, 0.2f));
            coinMat = M(new Color(1f, 0.85f, 0.1f));
            exitLockedMat = M(new Color(0.9f, 0.1f, 0.1f), true);
            exitOpenMat = M(new Color(0.15f, 1f, 0.3f), true);
        }

        public Vector3 CellPos(int gx, int gy) => new Vector3(gx * cs, 0f, gy * cs);
        public Vector3 CoinWorld(int i) => coinPos[i];

        // Returns the grid hash so the caller can compare it with the host's.
        public int Build(MatchState st)
        {
            Clear();
            MakeMaterials();
            cs = st.CellSize.Value;
            var walls = MazeGenerator.Generate(st.Cells.Value, st.Seed.Value);
            int hash = Hash(walls);
            BuildMaze(walls);
            PlaceCoinsAndExit(walls, st.Seed.Value, st.CoinCount.Value);
            Fx.ApplyAtmosphere();
            if (st.IsServer) MazeBuilt?.Invoke(walls, cs, st.Seed.Value);
            return hash;
        }

        static int Hash(bool[,] walls)
        {
            unchecked
            {
                int h = (int)2166136261;
                foreach (bool w in walls) h = (h ^ (w ? 1 : 0)) * 16777619;
                return h * 31 + walls.GetLength(0);
            }
        }

        void BuildMaze(bool[,] walls)
        {
            int size = walls.GetLength(0);
            root = new GameObject("Maze");

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(root.transform);
            floor.transform.position = new Vector3((size - 1) * cs / 2f, -0.5f, (size - 1) * cs / 2f);
            floor.transform.localScale = new Vector3(size * cs, 1f, size * cs);
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;

            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                {
                    if (!walls[x, y]) continue;
                    var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    w.name = "Wall";
                    w.transform.SetParent(root.transform);
                    w.transform.position = CellPos(x, y) + Vector3.up * wallHeight / 2f;
                    w.transform.localScale = new Vector3(cs, wallHeight, cs);
                    w.GetComponent<Renderer>().sharedMaterial = wallMat;
                }
        }

        void PlaceCoinsAndExit(bool[,] walls, int seed, int count)
        {
            int size = walls.GetLength(0);
            var rng = new System.Random(seed + 1);
            int exitX = size - 2, exitY = size - 2;
            var taken = new bool[size, size];

            var exit = GameObject.CreatePrimitive(PrimitiveType.Cube);
            exit.name = "Exit";
            exit.transform.SetParent(root.transform);
            // Tall glowing pillar: visible over the walls from anywhere in the maze.
            exit.transform.position = CellPos(exitX, exitY) + Vector3.up * 10f;
            exit.transform.localScale = new Vector3(cs * 0.8f, 20f, cs * 0.8f);
            ExitWorld = CellPos(exitX, exitY);
            exitRenderer = exit.GetComponent<Renderer>();
            exitRenderer.sharedMaterial = exitLockedMat;
            exit.GetComponent<Collider>().isTrigger = true;
            var ep = exit.AddComponent<Pickup>();
            ep.kind = PickupKind.Exit;
            ep.game = this;
            unlocked = false;

            int placed = 0, guard = 0;
            while (placed < count && guard++ < 10000)
            {
                int x = rng.Next(size), y = rng.Next(size);
                if (walls[x, y] || taken[x, y]) continue;
                if ((x == 1 && y == 1) || (x == exitX && y == exitY)) continue;
                taken[x, y] = true;
                var c = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                c.name = "Coin";
                c.transform.SetParent(root.transform);
                c.transform.position = CellPos(x, y) + Vector3.up * 1f;
                c.transform.localScale = Vector3.one * 0.7f;
                c.GetComponent<Renderer>().sharedMaterial = coinMat;
                c.GetComponent<Collider>().isTrigger = true;
                var p = c.AddComponent<Pickup>();
                p.kind = PickupKind.Coin;
                p.index = placed;
                p.game = this;
                coinPos.Add(CellPos(x, y));
                coinObjs.Add(c);
                placed++;
            }
        }

        public void Clear()
        {
            if (root != null) Destroy(root);
            root = null;
            coinPos.Clear();
            coinObjs.Clear();
            exitRenderer = null;
        }

        // Server confirmed a coin claim (any player).
        public void OnClaimed(int index, bool fx)
        {
            if (index < 0 || index >= coinObjs.Count || coinObjs[index] == null) return;
            if (fx) Fx.CoinCollected(coinObjs[index].transform.position);
            coinObjs[index].SetActive(false);
        }

        public void OnLocalCoins(int coins)
        {
            var st = MatchState.Instance;
            if (st == null || unlocked || coins < st.CoinsRequired.Value || exitRenderer == null) return;
            unlocked = true;
            exitRenderer.sharedMaterial = exitOpenMat;
            Fx.ExitUnlocked(ExitWorld);
            Notify("Exit unlocked!");
        }

        public void Notify(string msg) { notice = msg; noticeUntil = Time.time + 3f; }

        void OnGUI()
        {
            var st = MatchState.Instance;
            if (st == null || !st.Built) return;
            var big = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold };
            big.normal.textColor = Color.white;
            float cx = Screen.width / 2f, cy = Screen.height / 2f;
            int mine = PlayerController.Local != null ? PlayerController.Local.Coins.Value : 0;
            var opp = PlayerController.Opponent;
            GUI.Label(new Rect(20, 20, 500, 40), $"Coins: {mine} / {st.CoinsRequired.Value}", big);
            GUI.Label(new Rect(20, 50, 500, 40), $"Opponent: {(opp != null ? opp.Coins.Value : 0)} / {st.CoinsRequired.Value}", big);
            if (PlayerController.Local != null)
            {
                var ci = MazeRush.Shop.CosmeticCatalog.Default.Find(PlayerController.Local.ColorId.Value.ToString());
                var prev = GUI.color;
                GUI.color = ci == null ? Color.white
                    : ci.id == MazeRush.Shop.CosmeticCatalog.RainbowId ? Color.HSVToRGB(Mathf.Repeat(Time.time * 0.4f, 1f), 0.8f, 1f) : ci.color;
                GUI.DrawTexture(new Rect(Screen.width - 60, 20, 40, 40), Texture2D.whiteTexture);
                GUI.color = prev;
                float d = Vector2.Distance(new Vector2(PlayerController.Local.transform.position.x, PlayerController.Local.transform.position.z), new Vector2(ExitWorld.x, ExitWorld.z));
                GUI.Label(new Rect(20, 80, 700, 40), unlocked ? $"EXIT OPEN - {d:0}m (green pillar)" : $"Exit locked - collect {st.CoinsRequired.Value - mine} more coins", big);
            }
            var lp = PlayerController.Local;
            if (lp != null)
            {
                float fl = lp.FrozenLeft;
                if (fl > 0f)
                {
                    var prev2 = GUI.color;
                    float pulse = 0.25f + 0.1f * Mathf.Sin(Time.time * 14f);
                    GUI.color = new Color(1f, 0.15f, 0.1f, pulse);
                    GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                    GUI.color = prev2;
                    var huge = new GUIStyle(big) { fontSize = 56, alignment = TextAnchor.MiddleCenter };
                    huge.normal.textColor = new Color(1f, 0.85f, 0.2f);
                    GUI.Label(new Rect(0, cy - 90, Screen.width, 70), "STUNNED!", huge);
                    var sub = new GUIStyle(big) { alignment = TextAnchor.MiddleCenter };
                    GUI.Label(new Rect(0, cy - 25, Screen.width, 40), $"{fl:0.0}s", sub);
                }
                string pm = lp.PushMessage;
                if (pm.Length > 0)
                {
                    var pst = new GUIStyle(big) { fontSize = 40, alignment = TextAnchor.MiddleCenter };
                    pst.normal.textColor = pm == "MISS" ? new Color(1f, 1f, 1f, 0.7f) : new Color(0.4f, 1f, 0.5f);
                    GUI.Label(new Rect(0, cy + 70 - lp.PushMessageAge * 30f, Screen.width, 60), pm, pst);
                }
            }
            if (PlayerController.Local != null && st.CurrentPhase == MatchPhase.Playing)
            {
                float cd = PlayerController.Local.PushReadyIn;
                GUI.Label(new Rect(20, 160, 700, 40), cd <= 0f ? "Push monster [F / Click]: ready" : $"Push monster: {cd:0.0}s", big);
            }
            if (Time.time < noticeUntil) GUI.Label(new Rect(20, 120, 700, 40), notice, big);

            switch (st.CurrentPhase)
            {
                case MatchPhase.Waiting:
                    GUI.Label(new Rect(cx - 200, cy - 20, 400, 40), "Waiting for opponent...", big);
                    break;
                case MatchPhase.Countdown:
                    double left = Math.Max(0, st.CountdownEnd.Value - st.NetworkManager.ServerTime.Time);
                    GUI.Label(new Rect(cx - 100, cy - 20, 200, 40), Mathf.CeilToInt((float)left).ToString(), big);
                    break;
            }
        }
    }
}

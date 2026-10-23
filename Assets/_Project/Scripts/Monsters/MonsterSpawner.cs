// Networking agent: register the monster prefab on BOTH host and clients before starting, e.g.
//   NetworkManager.Singleton.AddNetworkPrefab(MazeRush.Monsters.MonsterFactory.CreatePrefab());
// Then, on the server/host after the maze is built: MonsterSpawner.Spawn(walls, cellSize, seed);
using System.Collections.Generic;
using Unity.AI.Navigation;
using Unity.Netcode;
using UnityEngine;

namespace MazeRush.Monsters
{
    public static class MonsterSpawner
    {
        static readonly List<NetworkObject> spawned = new List<NetworkObject>();

        public static void Spawn(bool[,] walls, float cellSize, int seed, MonsterConfig cfg = null)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            if (cfg == null) cfg = MonsterConfig.Default;

            foreach (var o in spawned) if (o != null && o.IsSpawned) o.Despawn();
            spawned.Clear();

            var maze = GameObject.Find("Maze");
            if (maze == null) { Debug.LogWarning("MonsterSpawner: no 'Maze' object"); return; }
            var surface = maze.GetComponent<NavMeshSurface>();
            if (surface == null) surface = maze.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();

            int n = walls.GetLength(0);
            var cells = new List<Vector2Int>();
            for (int x = 1; x < n - 1; x++)
                for (int y = 1; y < n - 1; y++)
                {
                    if (walls[x, y]) continue;
                    if (Mathf.Abs(x - 1) + Mathf.Abs(y - 1) < 6) continue;
                    if (Mathf.Abs(x - (n - 2)) + Mathf.Abs(y - (n - 2)) < 4) continue;
                    cells.Add(new Vector2Int(x, y));
                }

            var rng = new System.Random(seed + 2);
            var prefab = MonsterFactory.CreatePrefab();
            for (int i = 0; i < cfg.count && cells.Count > 0; i++)
            {
                int k = rng.Next(cells.Count);
                var c = cells[k]; cells.RemoveAt(k);
                var pos = new Vector3(c.x * cellSize, 0.1f, c.y * cellSize);
                var no = NetworkObject.InstantiateAndSpawn(prefab, nm, position: pos);
                if (no == null) continue;
                no.GetComponent<MonsterAI>().Init(cfg, walls, cellSize, seed + 100 + i);
                spawned.Add(no);
            }
        }
    }
}

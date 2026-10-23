using System.Reflection;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace MazeRush
{
    // Creates the NetworkManager and the network prefabs entirely from code.
    public static class NetBootstrap
    {
        public static GameObject StatePrefab { get; private set; }
        static NetworkManager nm;

        public static NetworkManager Ensure()
        {
            if (nm != null) return nm;
            if (NetworkManager.Singleton != null) return nm = NetworkManager.Singleton;

            // Prefabs live under an inactive root so they never run; clones are active.
            var root = new GameObject("NetPrefabs");
            root.SetActive(false);
            Object.DontDestroyOnLoad(root);
            var player = BuildPlayer(root.transform);
            StatePrefab = BuildState(root.transform);

            // Inactive so Awake runs after the config is set.
            var go = new GameObject("NetworkManager");
            go.SetActive(false);
            nm = go.AddComponent<NetworkManager>();
            var ut = go.AddComponent<UnityTransport>();
            nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = ut,
                PlayerPrefab = player,
                EnableSceneManagement = false,
                TickRate = 30,
            };
            nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = player });
            nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = StatePrefab });
            nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = MazeRush.Monsters.MonsterFactory.CreatePrefab() });
            MazeGame.MazeBuilt += (walls, cellSize, seed) => MazeRush.Monsters.MonsterSpawner.Spawn(walls, cellSize, seed);
            go.SetActive(true);
            return nm;
        }

        // Runtime prefabs have no asset id, so assign a fixed hash (identical on every peer).
        static void SetHash(NetworkObject no, uint hash)
        {
            typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .SetValue(no, hash);
        }

        static GameObject BuildPlayer(Transform parent)
        {
            var go = new GameObject("NetPlayer");
            go.transform.SetParent(parent, false);
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.4f;
            SetHash(go.AddComponent<NetworkObject>(), 0xA11CE001u);

            var nt = go.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner; // owner-authoritative movement
            nt.SyncRotAngleX = false; nt.SyncRotAngleZ = false;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;

            var pivot = new GameObject("CameraPivot").transform;
            pivot.SetParent(go.transform, false);
            pivot.localPosition = new Vector3(0f, 0.7f, 0f);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);

            var pc = go.AddComponent<PlayerController>();
            pc.cameraPivot = pivot;
            pc.body = body.GetComponent<Renderer>();
            return go;
        }

        static GameObject BuildState(Transform parent)
        {
            var go = new GameObject("MatchState");
            go.transform.SetParent(parent, false);
            SetHash(go.AddComponent<NetworkObject>(), 0xA11CE002u);
            go.AddComponent<MatchState>();
            return go;
        }
    }
}

using System.Reflection;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.AI;

namespace MazeRush.Monsters
{
    // Builds the monster network prefab entirely in code (no editor asset).
    public static class MonsterFactory
    {
        const uint PrefabHash = 0x4D4F4E53; // fixed so host and clients agree
        static GameObject prefab;

        public static GameObject CreatePrefab()
        {
            if (prefab) return prefab;
            // Template lives under an inactive holder (stays inert) but is itself active,
            // so spawned clones (which have no parent) come out active.
            var holder = new GameObject("MonsterPrefabs");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            var root = new GameObject("Monster");
            root.transform.SetParent(holder.transform, false);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0, 1f, 0);
            Object.Destroy(body.GetComponent<Collider>());
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = new Color(0.85f, 0.1f, 0.1f);
            body.GetComponent<Renderer>().sharedMaterial = mat;

            var eyeMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            eyeMat.color = Color.yellow;
            for (int i = -1; i <= 1; i += 2)
            {
                var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                eye.name = "Eye";
                eye.transform.SetParent(root.transform, false);
                eye.transform.localPosition = new Vector3(0.2f * i, 1.5f, 0.4f);
                eye.transform.localScale = Vector3.one * 0.18f;
                Object.Destroy(eye.GetComponent<Collider>());
                eye.GetComponent<Renderer>().sharedMaterial = eyeMat;
            }

            var col = root.AddComponent<CapsuleCollider>(); // players collide with monsters
            col.radius = 0.4f; col.height = 2f; col.center = new Vector3(0, 1f, 0);
            var rb = root.AddComponent<Rigidbody>(); rb.isKinematic = true;

            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.4f; agent.height = 2f;
            agent.enabled = false; // server enables after placing on the NavMesh

            var no = root.AddComponent<NetworkObject>();
            typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(no, PrefabHash);
            root.AddComponent<NetworkTransform>(); // server authoritative by default
            root.AddComponent<MonsterAI>();

            prefab = root;
            return prefab;
        }
    }
}

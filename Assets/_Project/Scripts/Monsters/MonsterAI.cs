using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace MazeRush.Monsters
{
    // Server-only brain: wander, chase nearest visible player, freeze on touch.
    public class MonsterAI : NetworkBehaviour
    {
        public MonsterConfig cfg;
        public bool[,] walls;
        public float cellSize = 3f;

        const float TouchDist = 1.3f;
        NavMeshAgent agent;
        Transform target;
        public NetworkVariable<bool> Stunned = new NetworkVariable<bool>(); // server writes; clients tint the body
        Material bodyMat;
        float nextHitTime, stunUntil, pauseUntil;
        Vector3 pushVel;
        System.Random rng;

        public void Init(MonsterConfig c, bool[,] w, float cs, int seed)
        {
            cfg = c; walls = w; cellSize = cs; rng = new System.Random(seed);
        }

        void ShowStun(bool on)
        {
            if (bodyMat == null) return;
            var c = on ? new Color(1f, 0.9f, 0.2f) : new Color(0.85f, 0.1f, 0.1f);
            bodyMat.color = c;
            if (bodyMat.HasProperty("_BaseColor")) bodyMat.SetColor("_BaseColor", c);
        }

        public override void OnNetworkSpawn()
        {
            var body = transform.Find("Body");
            if (body != null) bodyMat = body.GetComponent<Renderer>().material;
            Stunned.OnValueChanged += (_, on) => ShowStun(on);
            if (!IsServer) { enabled = false; return; }
            agent = GetComponent<NavMeshAgent>();
            if (cfg == null) cfg = MonsterConfig.Default;
            if (rng == null) rng = new System.Random();
            if (NavMesh.SamplePosition(transform.position, out var hit, 3f, NavMesh.AllAreas))
                transform.position = hit.position;
            agent.enabled = true;
            agent.speed = cfg.speed;
            agent.stoppingDistance = 1.1f; // stop at the player instead of shoving into them
            PickWander();
        }

        void Update()
        {
            if (!IsServer || agent == null || !agent.isOnNavMesh) return;
            var st = MatchState.Instance;
            bool live = st != null && st.CurrentPhase == MatchPhase.Playing;
            if (pushVel.sqrMagnitude > 0.01f)
            {
                agent.Move(pushVel * Time.deltaTime); // knockback, stays on the NavMesh
                pushVel = Vector3.MoveTowards(pushVel, Vector3.zero, 25f * Time.deltaTime);
            }
            bool stunned = Time.time < stunUntil;
            bool resting = Time.time < pauseUntil; // after a hit: stand still, no chase, no pushing
            if (Stunned.Value != stunned) { Stunned.Value = stunned; ShowStun(stunned); }
            agent.isStopped = !live || stunned || resting; // idle during waiting/countdown/finished or while stunned
            if (!live || stunned || resting) return;
            UpdateTarget();
            if (target != null)
            {
                agent.SetDestination(target.position);
                TryHit();
            }
            else if (!agent.pathPending && agent.remainingDistance < 0.6f) PickWander();
        }

        void UpdateTarget()
        {
            if (target != null)
            {
                float d = Vector3.Distance(transform.position, target.position);
                if (d > cfg.loseRange) { target = null; PickWander(); }
                return;
            }
            float best = cfg.detectRange;
            foreach (var kv in NetworkManager.ConnectedClients)
            {
                var po = kv.Value.PlayerObject;
                if (po == null) continue;
                float d = Vector3.Distance(transform.position, po.transform.position);
                if (d < best && !NavMesh.Raycast(transform.position, po.transform.position, out _, NavMesh.AllAreas))
                { best = d; target = po.transform; }
            }
        }

        // Server only: knock back and briefly stun.
        public void Push(Vector3 velocity)
        {
            pushVel = velocity;
            stunUntil = Time.time + 1.5f;
            Fx.MonsterHit(transform.position); // impact on the host's screen; the pusher hears it via PushResultRpc
        }

        void TryHit()
        {
            if (Time.time < nextHitTime) return;
            var d = target.position - transform.position; d.y = 0f; // flat: colliders keep bodies ~0.8 apart
            if (d.magnitude > TouchDist + 0.3f) return;
            var f = target.GetComponentInParent<IFreezable>();
            if (f == null) return;
            nextHitTime = Time.time + cfg.hitCooldown;
            f.Freeze(cfg.freezeSeconds);
            pauseUntil = Time.time + cfg.freezeSeconds + 0.5f; // stays put while the victim is stunned
            target = null; agent.ResetPath(); agent.velocity = Vector3.zero;
        }

        void PickWander()
        {
            if (walls == null) return;
            int n = walls.GetLength(0);
            for (int i = 0; i < 30; i++)
            {
                int x = rng.Next(1, n - 1), y = rng.Next(1, n - 1);
                if (walls[x, y]) continue;
                agent.SetDestination(new Vector3(x * cellSize, 0f, y * cellSize));
                return;
            }
        }
    }
}

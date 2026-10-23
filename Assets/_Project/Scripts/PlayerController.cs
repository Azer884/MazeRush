using System.Collections.Generic;
using MazeRush.Services;
using MazeRush.Shop;
using Unity.Collections;
using MazeRush.Monsters;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeRush
{
    // Owner-authoritative movement (NetworkTransform in Owner mode); server validates coin/exit claims.
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : NetworkBehaviour, IFreezable
    {
        static readonly List<PlayerController> all = new List<PlayerController>();
        public static PlayerController Local { get; private set; }
        public static PlayerController Opponent
        {
            get { foreach (var p in all) if (!p.IsOwner) return p; return null; }
        }
        public static PlayerController Find(ulong clientId)
        {
            foreach (var p in all) if (p.OwnerClientId == clientId) return p;
            return null;
        }

        public static bool InputBlocked; // set by the pause menu
        public float moveSpeed = 5f;
        public float jumpSpeed = 6.5f;
        public float gravity = -20f;
        public const float PushCooldown = 3f, PushRange = 3.6f, PushForce = 9f;
        float vy, nextPush, serverNextPush;
        string pushMsg = ""; float pushMsgUntil;
        public float FrozenLeft => Mathf.Max(0f, frozenUntil - Time.time);
        public float StunDuration { get; private set; }
        public string PushMessage => Time.time < pushMsgUntil ? pushMsg : "";
        public float PushMessageAge => 1f - Mathf.Clamp01((pushMsgUntil - Time.time) / 0.9f);
        void ShowPush(string m) { pushMsg = m; pushMsgUntil = Time.time + 0.9f; }
        public float PushReadyIn => Mathf.Max(0f, nextPush - Time.time);
        public float lookSensitivity = 0.1f;
        public Transform cameraPivot;
        public Renderer body;

        public NetworkVariable<int> Coins = new NetworkVariable<int>();
        // Equipped color id; the owner writes it, everyone tints the body from it.
        public NetworkVariable<FixedString32Bytes> ColorId = new NetworkVariable<FixedString32Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        Material bodyMat;

        CharacterController controller;
        float pitch, frozenUntil;
        bool placed;

        public override void OnNetworkSpawn()
        {
            controller = GetComponent<CharacterController>();
            all.Add(this);
            bodyMat = body.material;
            ColorId.OnValueChanged += (_, __) => ApplyColor();
            if (IsOwner) ColorId.Value = Backend.Profile.equippedSkin ?? "";
            ApplyColor();
            if (!IsOwner)
            {
                controller.enabled = false; // proxy position comes from the owner via NetworkTransform
                var cap = gameObject.AddComponent<CapsuleCollider>(); // so the local player collides with the opponent
                cap.radius = 0.4f; cap.height = 1.8f;
                return;
            }
            Local = this;
            body.enabled = false;
            Coins.OnValueChanged += (prev, cur) => { if (MazeGame.Instance != null) MazeGame.Instance.OnLocalCoins(cur); };
            Cursor.lockState = CursorLockMode.Locked;

            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                camGo.tag = "MainCamera";
                cam = camGo.GetComponent<Camera>();
            }
            cam.transform.SetParent(cameraPivot, false);
            cam.transform.localPosition = Vector3.zero;
            cam.transform.localRotation = Quaternion.identity;
        }

        public override void OnNetworkDespawn()
        {
            all.Remove(this);
            if (Local == this) Local = null;
        }

        void ApplyColor()
        {
            var item = CosmeticCatalog.Default.Find(ColorId.Value.ToString());
            Color c = item != null ? item.color : Color.white;
            if (bodyMat != null && bodyMat.HasProperty("_BaseColor")) bodyMat.SetColor("_BaseColor", c);
        }

        // Server calls this (monsters); the owner applies it.
        public void Freeze(float seconds)
        {
            if (IsSpawned) FreezeRpc(seconds);
            else frozenUntil = Time.time + seconds;
        }

        [Rpc(SendTo.Owner)]
        void FreezeRpc(float seconds)
        {
            StunDuration = Mathf.Clamp(seconds, 0f, 10f);
            frozenUntil = Time.time + StunDuration;
            Fx.MonsterHit(transform.position); // caught: buzz + flash on the player who got hit
        }

        [Rpc(SendTo.Owner)]
        void PushResultRpc(bool hit, Vector3 where)
        {
            ShowPush(hit ? "HIT!" : "MISS");
            if (hit) Fx.MonsterHit(where);
        }

        void Update()
        {
            if (IsSpawned && bodyMat != null && ColorId.Value == CosmeticCatalog.RainbowId)
                bodyMat.SetColor("_BaseColor", Color.HSVToRGB(Mathf.Repeat(Time.time * 0.4f, 1f), 0.8f, 1f));
            if (!IsSpawned || !IsOwner) return;
            var st = MatchState.Instance;
            if (st == null || !st.Built) return;

            if (!placed) Place(st);

            var mouse = Mouse.current;
            var keys = Keyboard.current;
            if (mouse == null || keys == null) return;
            if (st.CurrentPhase == MatchPhase.Finished) return;

            if (!InputBlocked)
            {
                Vector2 look = mouse.delta.ReadValue() * lookSensitivity;
                transform.Rotate(0f, look.x, 0f);
                pitch = Mathf.Clamp(pitch - look.y, -80f, 80f);
                cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            // feedback: shake while stunned, short FOV punch after a push
            bool stunned = FrozenLeft > 0f;
            cameraPivot.localPosition = new Vector3(0f, 0.7f, 0f) + (stunned ? (Vector3)Random.insideUnitCircle * 0.04f : Vector3.zero);
            var cam = Camera.main;
            if (cam != null)
            {
                float punch = Time.time < pushMsgUntil ? Mathf.Sin(PushMessageAge * Mathf.PI) * 8f : 0f;
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 60f + punch, 15f * Time.deltaTime);
            }

            bool canMove = st.CurrentPhase == MatchPhase.Playing && Time.time >= frozenUntil && !InputBlocked;
            Vector3 move = Vector3.zero;
            if (canMove)
            {
                float h = (keys.dKey.isPressed ? 1 : 0) - (keys.aKey.isPressed ? 1 : 0);
                float v = (keys.wKey.isPressed ? 1 : 0) - (keys.sKey.isPressed ? 1 : 0);
                move = (transform.right * h + transform.forward * v).normalized * moveSpeed;
                if (keys.spaceKey.wasPressedThisFrame && controller.isGrounded) vy = jumpSpeed;
                if ((keys.fKey.wasPressedThisFrame || mouse.leftButton.wasPressedThisFrame) && Time.time >= nextPush)
                {
                    nextPush = Time.time + PushCooldown;
                    ShowPush("PUSH!");
                    PushRpc();
                }
            }
            if (controller.isGrounded && vy < 0f) vy = -2f; // keep grounded
            vy += gravity * Time.deltaTime;
            move.y = vy;
            controller.Move(move * Time.deltaTime);
        }

        // Server validates the cooldown and finds a monster in front of the player.
        [Rpc(SendTo.Server)]
        void PushRpc()
        {
            if (Time.time < serverNextPush) return;
            serverNextPush = Time.time + PushCooldown;
            bool any = false;
            foreach (var m in FindObjectsByType<MonsterAI>(FindObjectsSortMode.None))
            {
                Vector3 to = m.transform.position - transform.position; to.y = 0f;
                if (to.magnitude > PushRange || Vector3.Dot(to.normalized, transform.forward) < 0.3f) continue;
                m.Push(to.normalized * PushForce);
                any = true;
                PushResultRpc(true, m.transform.position);
            }
            if (!any) PushResultRpc(false, transform.position);
        }

        void Place(MatchState st)
        {
            placed = true;
            var pos = MazeGame.Instance.CellPos(1, 1) + Vector3.up * 1f + Vector3.right * (OwnerClientId == 0 ? -0.6f : 0.6f);
            controller.enabled = false;
            transform.SetPositionAndRotation(pos, Quaternion.identity);
            GetComponent<NetworkTransform>().Teleport(pos, Quaternion.identity, transform.localScale);
            controller.enabled = true;
        }
    }
}

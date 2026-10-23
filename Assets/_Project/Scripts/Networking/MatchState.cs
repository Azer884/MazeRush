using Unity.Netcode;
using UnityEngine;

namespace MazeRush
{
    public enum MatchPhase { Waiting, Countdown, Playing, Finished }

    // Replicated match config + state machine. Server writes, everyone reads.
    public class MatchState : NetworkBehaviour
    {
        public static MatchState Instance { get; private set; }
        const float ClaimRange = 2.5f; // generous vs trigger size to absorb latency
        const float CountdownSeconds = 4f;

        public NetworkVariable<int> Seed = new();
        public NetworkVariable<int> Cells = new();
        public NetworkVariable<int> CoinCount = new();
        public NetworkVariable<int> CoinsRequired = new();
        public NetworkVariable<float> CellSize = new(3f);
        public NetworkVariable<int> GridHash = new();
        // Winner/Forfeit must be declared BEFORE Phase: NGO applies updates in declaration order, and the Phase callback reads them.
        public NetworkVariable<ulong> Winner = new(ulong.MaxValue);
        public NetworkVariable<bool> Forfeit = new();
        public NetworkVariable<int> Phase = new();
        public NetworkVariable<double> CountdownEnd = new();
        public NetworkList<int> Claimed = new();

        public MatchPhase CurrentPhase => (MatchPhase)Phase.Value;
        public bool Built { get; private set; }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            var game = MazeGame.Ensure();
            if (IsServer)
            {
                Seed.Value = game.seed != 0 ? game.seed : Random.Range(1, int.MaxValue);
                Cells.Value = game.cells;
                CoinCount.Value = game.coinCount;
                CoinsRequired.Value = Mathf.Min(game.coinsRequired, game.coinCount);
                CellSize.Value = game.cellSize;
                NetworkManager.OnClientConnectedCallback += OnConnected;
                NetworkManager.OnClientDisconnectCallback += OnDisconnected;
            }

            int hash = game.Build(this);
            if (IsServer) GridHash.Value = hash;
            else if (hash != GridHash.Value)
            {
                game.Notify("Maze mismatch with host");
                MatchFlow.Leave();
                return;
            }
            Built = true;

            Claimed.OnListChanged += OnClaimedChanged;
            Phase.OnValueChanged += OnPhaseChanged;
            for (int i = 0; i < Claimed.Count; i++) game.OnClaimed(Claimed[i], false);
            MatchFlow.RaiseState(CurrentPhase.ToString());
            if (IsServer) OnConnected(0); // re-evaluate: both players may already be here
        }

        public override void OnNetworkDespawn()
        {
            Claimed.OnListChanged -= OnClaimedChanged;
            Phase.OnValueChanged -= OnPhaseChanged;
            if (NetworkManager != null && IsServer)
            {
                NetworkManager.OnClientConnectedCallback -= OnConnected;
                NetworkManager.OnClientDisconnectCallback -= OnDisconnected;
            }
            // Host vanished mid-match: the remaining player wins by forfeit.
            if (!IsServer && Built && CurrentPhase != MatchPhase.Finished && !MatchFlow.Leaving)
                MatchFlow.EndMatch(true);
            if (Instance == this) Instance = null;
            Built = false;
            if (MazeGame.Instance != null) MazeGame.Instance.Clear();
        }

        void OnClaimedChanged(NetworkListEvent<int> e)
        {
            if (e.Type == NetworkListEvent<int>.EventType.Add) MazeGame.Instance.OnClaimed(e.Value, true);
        }

        void OnPhaseChanged(int prev, int cur)
        {
            MatchFlow.RaiseState(((MatchPhase)cur).ToString());
            if ((MatchPhase)cur == MatchPhase.Finished)
                MatchFlow.EndMatch(Winner.Value == NetworkManager.LocalClientId);
        }

        // ---- server only ----
        void Update()
        {
            if (!IsSpawned || !IsServer) return;
            if (CurrentPhase == MatchPhase.Countdown && NetworkManager.ServerTime.Time >= CountdownEnd.Value)
                Phase.Value = (int)MatchPhase.Playing;
        }

        void OnConnected(ulong _)
        {
            if (CurrentPhase == MatchPhase.Waiting && NetworkManager.ConnectedClientsIds.Count >= 2)
            {
                CountdownEnd.Value = NetworkManager.ServerTime.Time + CountdownSeconds;
                Phase.Value = (int)MatchPhase.Countdown;
            }
        }

        void OnDisconnected(ulong id)
        {
            if (id == NetworkManager.LocalClientId) return;
            if (CurrentPhase == MatchPhase.Countdown || CurrentPhase == MatchPhase.Playing)
            {
                Forfeit.Value = true;
                Finish(NetworkManager.LocalClientId);
            }
        }

        void Finish(ulong winner)
        {
            Winner.Value = winner;
            Phase.Value = (int)MatchPhase.Finished;
        }

        static float FlatDist(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        [Rpc(SendTo.Server)]
        public void ClaimCoinRpc(int index, RpcParams rpcParams = default)
        {
            var game = MazeGame.Instance;
            if (CurrentPhase != MatchPhase.Playing || index < 0 || index >= game.CoinCount || Claimed.Contains(index)) return;
            var p = PlayerController.Find(rpcParams.Receive.SenderClientId);
            if (p == null || FlatDist(p.transform.position, game.CoinWorld(index)) > ClaimRange) return;
            Claimed.Add(index); // first valid claim wins; later ones hit Contains
            p.Coins.Value++;
        }

        [Rpc(SendTo.Server)]
        public void ClaimExitRpc(RpcParams rpcParams = default)
        {
            if (CurrentPhase != MatchPhase.Playing) return;
            ulong id = rpcParams.Receive.SenderClientId;
            var p = PlayerController.Find(id);
            if (p == null || p.Coins.Value < CoinsRequired.Value) return;
            if (FlatDist(p.transform.position, MazeGame.Instance.ExitWorld) > ClaimRange) return;
            Finish(id);
        }
    }
}

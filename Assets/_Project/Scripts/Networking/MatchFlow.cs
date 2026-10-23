using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace MazeRush
{
    // Public API for UI: QuickPlayAsync / Leave / OnStateChanged / OnMatchEnded.
    public static class MatchFlow
    {
        public static event Action<string> OnStateChanged; // "Connecting", "Searching", "Joined", "Waiting", "Countdown", "Playing", "Finished", "Idle", "Error: ..."
        public static event Action<bool> OnMatchEnded;     // true = local player won
        public static bool Leaving { get; private set; }

        static ISession session;
        static bool busy, ended;

        public static void RaiseState(string s) => OnStateChanged?.Invoke(s);

        public static async Task QuickPlayAsync()
        {
            if (busy) return;
            busy = true; ended = false;
            try
            {
                RaiseState("Connecting");
                if (UnityServices.State != ServicesInitializationState.Initialized)
                    await UnityServices.InitializeAsync();
                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();

                var nm = NetBootstrap.Ensure();
                MazeGame.Ensure();

                RaiseState("Searching");
                var quick = new QuickJoinOptions { Timeout = TimeSpan.FromSeconds(5), CreateSession = true };
                var opts = new SessionOptions { MaxPlayers = 2 }.WithRelayNetwork();
                session = await MultiplayerService.Instance.MatchmakeSessionAsync(quick, opts);

                if (session.IsHost)
                {
                    // The SDK starts the host; wait for it, then spawn the shared match state.
                    for (int i = 0; i < 100 && !nm.IsListening; i++) await Task.Delay(100);
                    if (!nm.IsServer) throw new Exception("Host failed to start");
                    var go = UnityEngine.Object.Instantiate(NetBootstrap.StatePrefab);
                    go.GetComponent<NetworkObject>().Spawn();
                    RaiseState("Waiting");
                }
                else
                {
                    RaiseState("Joined");
                    for (int i = 0; i < 150 && MatchState.Instance == null; i++) await Task.Delay(100);
                    if (MatchState.Instance == null) throw new Exception("Could not reach host");
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                RaiseState("Error: " + e.Message);
                Leave();
            }
            finally { busy = false; }
        }

        public static void Leave() => _ = LeaveAsync();

        static async Task LeaveAsync()
        {
            Leaving = true;
            var s = session; session = null;
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening) nm.Shutdown();
            try
            {
                if (s != null) { if (s.IsHost) await s.AsHost().DeleteAsync(); else await s.LeaveAsync(); }
            }
            catch (Exception e) { Debug.LogWarning("Leave: " + e.Message); }
            if (MazeGame.Instance != null) MazeGame.Instance.Clear();
            Cursor.lockState = CursorLockMode.None;
            Leaving = false;
            RaiseState("Idle");
        }

        // Called once per match on every peer for its local player.
        public static void EndMatch(bool won)
        {
            if (ended) return;
            ended = true;
            int coins = PlayerController.Local != null ? PlayerController.Local.Coins.Value : 0;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            OnMatchEnded?.Invoke(won);
            _ = ReportAsync(won, coins);
        }

        static async Task ReportAsync(bool won, int coins)
        {
            try { await Services.Backend.ReportMatchAsync(won, coins); }
            catch (Exception e) { Debug.LogWarning("ReportMatch: " + e.Message); }
        }
    }
}

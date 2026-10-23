using UnityEngine;

namespace MazeRush
{
    public enum PickupKind { Coin, Exit }

    // Trigger on coins and the exit. The local player only REQUESTS a claim; the server decides.
    public class Pickup : MonoBehaviour
    {
        public PickupKind kind;
        public int index;
        public MazeGame game;
        float nextTry;

        void Update()
        {
            if (kind == PickupKind.Coin) transform.Rotate(0f, 120f * Time.deltaTime, 0f);
        }

        void OnTriggerStay(Collider other)
        {
            if (Time.time < nextTry) return;
            var pc = other.GetComponent<PlayerController>();
            if (pc == null || !pc.IsOwner) return; // opponent's proxy must not trigger on our machine
            var st = MatchState.Instance;
            if (st == null || st.CurrentPhase != MatchPhase.Playing) return;
            nextTry = Time.time + 0.3f; // retry while standing here, without spamming
            if (kind == PickupKind.Coin) st.ClaimCoinRpc(index);
            else if (pc.Coins.Value >= st.CoinsRequired.Value) st.ClaimExitRpc();
            else game.Notify($"Exit locked. Need {st.CoinsRequired.Value - pc.Coins.Value} more coins.");
        }
    }
}

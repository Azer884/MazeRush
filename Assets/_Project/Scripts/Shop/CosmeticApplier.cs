using UnityEngine;
using MazeRush.Services;

namespace MazeRush.Shop
{
    // Visual only: tints all child renderers by the equipped skin/color.
    public class CosmeticApplier : MonoBehaviour
    {
        public Renderer[] targets;

        void OnEnable() { Backend.ProfileChanged += OnProfile; Apply(Backend.Profile); }
        void OnDisable() { Backend.ProfileChanged -= OnProfile; }
        void OnProfile(PlayerProfile p) => Apply(p);

        public void Apply(PlayerProfile p)
        {
            if (p == null) return;
            var item = CosmeticCatalog.Default.Find(p.equippedSkin);
            if (item == null) return;
            var rs = (targets != null && targets.Length > 0) ? targets : GetComponentsInChildren<Renderer>();
            foreach (var r in rs)
            {
                if (r == null) continue;
                var m = r.material;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", item.color);
                else if (m.HasProperty("_Color")) m.SetColor("_Color", item.color);
            }
        }
    }
}

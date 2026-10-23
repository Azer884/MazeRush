using UnityEngine;

namespace MazeRush.Monsters
{
    [CreateAssetMenu(menuName = "MazeRush/Monster Config")]
    public class MonsterConfig : ScriptableObject
    {
        public int count = 3;
        public float speed = 3.5f;
        public float detectRange = 18f;
        public float loseRange = 28f;
        public float freezeSeconds = 2.5f;
        public float hitCooldown = 3f;

        static MonsterConfig cached;

        // Resources/MonsterConfig asset if present, otherwise a code-created default.
        public static MonsterConfig Default
        {
            get
            {
                if (cached) return cached;
                cached = Resources.Load<MonsterConfig>("MonsterConfig");
                if (!cached) cached = CreateInstance<MonsterConfig>();
                return cached;
            }
        }
    }
}

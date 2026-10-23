using System;
using System.Threading.Tasks;

namespace MazeRush.UI
{
    // Thin bridge so UI never touches networking types directly.
    public static class PlayAdapter
    {
        public static event Action<string> StateChanged;
        public static event Action<bool> MatchEnded;

        static bool hooked;

        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            MatchFlow.OnStateChanged += s => StateChanged?.Invoke(s);
            MatchFlow.OnMatchEnded += w => MatchEnded?.Invoke(w);
        }

        public static Task QuickPlayAsync() { Hook(); return MatchFlow.QuickPlayAsync(); }

        public static void Leave() { Hook(); MatchFlow.Leave(); }
    }
}

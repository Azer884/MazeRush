using System.Collections.Generic;

namespace MazeRush
{
    // Pure C#. true = wall. Grid size is (2*cells+1). Cell (cx,cy) sits at (2cx+1, 2cy+1).
    // Recursive backtracker makes a perfect maze, so start and exit are always connected.
    public static class MazeGenerator
    {
        public static bool[,] Generate(int cells, int seed)
        {
            int size = cells * 2 + 1;
            var walls = new bool[size, size];
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    walls[x, y] = true;

            var rng = new System.Random(seed);
            var visited = new bool[cells, cells];
            var stack = new Stack<(int x, int y)>();
            var dirs = new (int dx, int dy)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };

            visited[0, 0] = true;
            walls[1, 1] = false;
            stack.Push((0, 0));

            while (stack.Count > 0)
            {
                var (cx, cy) = stack.Peek();
                var options = new List<(int dx, int dy)>();
                foreach (var d in dirs)
                {
                    int nx = cx + d.dx, ny = cy + d.dy;
                    if (nx >= 0 && ny >= 0 && nx < cells && ny < cells && !visited[nx, ny])
                        options.Add(d);
                }

                if (options.Count == 0) { stack.Pop(); continue; }

                var pick = options[rng.Next(options.Count)];
                int tx = cx + pick.dx, ty = cy + pick.dy;
                walls[2 * cx + 1 + pick.dx, 2 * cy + 1 + pick.dy] = false;
                walls[2 * tx + 1, 2 * ty + 1] = false;
                visited[tx, ty] = true;
                stack.Push((tx, ty));
            }
            return walls;
        }
    }
}

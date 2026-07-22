using System.Collections.Generic;
using UnityEngine;

namespace TianZhang.Core
{
    /// <summary>
    /// 六角格网格管理器：寻路、范围查询、阻挡检测
    /// </summary>
    public class HexGrid
    {
        private Dictionary<HexCoord, bool> blocked = new Dictionary<HexCoord, bool>();
        private Dictionary<HexCoord, int> occupiedBy = new Dictionary<HexCoord, int>(); // 角色ID

        public void SetBlocked(HexCoord coord, bool isBlocked)
        {
            blocked[coord] = isBlocked;
        }

        public bool IsBlocked(HexCoord coord) =>
            blocked.TryGetValue(coord, out bool b) && b;

        public void SetOccupied(HexCoord coord, int characterId)
        {
            occupiedBy[coord] = characterId;
        }

        public void ClearOccupied(HexCoord coord)
        {
            occupiedBy.Remove(coord);
        }

        public bool IsOccupied(HexCoord coord) => occupiedBy.ContainsKey(coord);

        public int GetOccupant(HexCoord coord) =>
            occupiedBy.TryGetValue(coord, out int id) ? id : -1;

        /// <summary>BFS 寻路，返回路径（不含起点）</summary>
        public List<HexCoord> FindPath(HexCoord start, HexCoord end, int maxSteps = 999)
        {
            if (start == end) return new List<HexCoord>();
            if (IsBlocked(end)) return null;

            var cameFrom = new Dictionary<HexCoord, HexCoord>();
            var frontier = new Queue<HexCoord>();
            var visited = new HashSet<HexCoord> { start };
            frontier.Enqueue(start);

            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                if (current == end)
                    return ReconstructPath(cameFrom, start, end);

                foreach (var next in current.AllNeighbors())
                {
                    if (visited.Contains(next)) continue;
                    if (IsBlocked(next)) continue;
                    if (start.Distance(next) > maxSteps) continue;

                    visited.Add(next);
                    cameFrom[next] = current;
                    frontier.Enqueue(next);
                }
            }
            return null; // 无路径
        }

        private List<HexCoord> ReconstructPath(Dictionary<HexCoord, HexCoord> cameFrom,
            HexCoord start, HexCoord end)
        {
            var path = new List<HexCoord>();
            var current = end;
            while (current != start)
            {
                path.Add(current);
                current = cameFrom[current];
            }
            path.Reverse();
            return path;
        }

        /// <summary>获取可移动范围（不穿过敌人）</summary>
        public List<HexCoord> GetMoveRange(HexCoord start, int movePoints)
        {
            var results = new List<HexCoord>();
            var frontier = new Queue<(HexCoord, int)>();
            var visited = new HashSet<HexCoord> { start };
            frontier.Enqueue((start, 0));

            while (frontier.Count > 0)
            {
                var (current, dist) = frontier.Dequeue();
                foreach (var next in current.AllNeighbors())
                {
                    if (visited.Contains(next)) continue;
                    if (IsBlocked(next)) continue;
                    int newDist = dist + 1;
                    if (newDist > movePoints) continue;

                    visited.Add(next);
                    if (!IsOccupied(next))
                        results.Add(next);
                    frontier.Enqueue((next, newDist));
                }
            }
            return results;
        }
    }
}

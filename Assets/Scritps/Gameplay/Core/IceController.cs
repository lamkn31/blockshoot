using System.Collections.Generic;
using UnityEngine;

namespace Wayfu.Lamkn
{
    /// <summary>
    /// Tự sinh các khối BĂNG (prefab Ice) phủ lên vùng cell đánh dấu Iced trong level, kèm ĐẾM NGƯỢC số
    /// block còn phải phá để tan. Cell LIỀN KỀ CÙNG ngưỡng = 1 vùng băng; nếu vùng không thành 1 hình chữ
    /// nhật thì chia thành nhiều Ice — khối LỚN NHẤT hiện countdown, các khối lẻ tắt countdown. Kích thước
    /// mỗi Ice tự scale vừa số cell nó phủ. Khi tổng block phá ≥ ngưỡng → xoá cả cụm Ice của vùng
    /// (GridBlockManager tự cho các cell đó bắn được).
    /// <para>Đặt component này trên scene và gán prefab Ice (có child TMP làm countdown). Auto-tạo rỗng /
    /// chưa gán prefab thì không sinh gì (cell vẫn băng theo logic GridBlockManager).</para>
    /// </summary>
    public class IceController : Singleton<IceController>
    {
        [Tooltip("Pool khối băng — gán prefab Ice (root có component Ice, child TMP countdown) vào Item Prefab. " +
                 "Bỏ trống = không sinh băng.")]
        [SerializeField] private Pooler<Ice> _icePool = new Pooler<Ice>();
        [Tooltip("Nâng thêm theo Y sau khi đã đặt băng lên đỉnh block (tinh chỉnh cho khít mặt trên).")]
        [SerializeField] private float _yOffset = 0f;

        private Transform _root; // node cha CỐ ĐỊNH của pool (không xoá khi rebuild — chỉ ReturnAll)

        private class Region
        {
            public int Threshold;
            public readonly List<Ice> Ices = new List<Ice>();
            public Ice Main; // khối LỚN nhất — khối duy nhất hiện countdown/vùng
        }
        private readonly List<Region> _regions = new List<Region>();

        public void Build(LevelData level)
        {
            Clear();
            if (level == null || _icePool.ItemPrefab == null || level.Grids == null) return;
            EnsurePool();
            foreach (var grid in level.Grids)
                if (grid != null) BuildGrid(grid);
        }

        private void EnsurePool()
        {
            if (_root == null)
            {
                _root = new GameObject("IceBlocks").transform;
                _root.SetParent(transform);
            }
            _icePool.SetParent(_root); // prefab đã gán sẵn ở Item Prefab của pool
        }

        // Cập nhật countdown (= số block còn lại để tan) và trả Ice của vùng đủ ngưỡng về pool. Từ GameController.
        public void UpdateIce(int destroyed)
        {
            for (int i = _regions.Count - 1; i >= 0; i--)
            {
                var reg = _regions[i];
                if (destroyed >= reg.Threshold)
                {
                    foreach (var ice in reg.Ices) if (ice != null) ice.Despawn();
                    _regions.RemoveAt(i);
                }
                else reg.Main?.SetCountdown(Mathf.Max(0, reg.Threshold - destroyed));
            }
        }

        public void Clear()
        {
            _icePool.ReturnAll(); // trả hết Ice active về pool (không xoá node cha)
            _regions.Clear();
        }

        // ---- Dựng băng cho 1 grid ----
        private void BuildGrid(BlockGridData grid)
        {
            // Gom cell băng: (row, element) → ngưỡng tan.
            var iced = new Dictionary<(int, int), int>();
            for (int r = 0; r < grid.Rows; r++)
            {
                int n = grid.ElementsInRow(r);
                for (int e = 0; e < n; e++)
                {
                    var c = grid.GetCell(r, e);
                    if (c != null && c.Iced && c.IceThreshold > 0 && c.BlockStackCt > 0)
                        iced[(r, e)] = c.IceThreshold;
                }
            }
            if (iced.Count == 0) return;

            float colStep = GridColStep(grid);
            float rowStep = GridRowStep(grid);
            if (rowStep <= 0f) rowStep = colStep;
            if (colStep <= 0f) colStep = rowStep;

            // Mỗi VÙNG = thành phần liên thông (4 hướng) các cell băng CÙNG ngưỡng.
            var visited = new HashSet<(int, int)>();
            var stack = new Stack<(int, int)>();
            foreach (var kv in iced)
            {
                if (visited.Contains(kv.Key)) continue;
                int th = kv.Value;
                var comp = new HashSet<(int, int)>();
                stack.Clear(); stack.Push(kv.Key); visited.Add(kv.Key);
                while (stack.Count > 0)
                {
                    var (r, e) = stack.Pop();
                    comp.Add((r, e));
                    TryNeighbor(iced, visited, stack, th, r - 1, e);
                    TryNeighbor(iced, visited, stack, th, r + 1, e);
                    TryNeighbor(iced, visited, stack, th, r, e - 1);
                    TryNeighbor(iced, visited, stack, th, r, e + 1);
                }
                BuildRegion(grid, comp, th, colStep, rowStep);
            }
        }

        private static void TryNeighbor(Dictionary<(int, int), int> iced, HashSet<(int, int)> visited,
                                        Stack<(int, int)> stack, int th, int r, int e)
        {
            var key = (r, e);
            if (!visited.Contains(key) && iced.TryGetValue(key, out int nth) && nth == th)
            { visited.Add(key); stack.Push(key); }
        }

        // 1 vùng → chia thành các hình chữ nhật (lớn nhất trước); khối đầu hiện countdown, còn lại tắt.
        private void BuildRegion(BlockGridData grid, HashSet<(int, int)> comp, int threshold,
                                 float colStep, float rowStep)
        {
            // Nâng băng lên NẰM TRÊN đỉnh stack block của vùng; grid scale riêng dùng spacing riêng.
            float stackSpacing = grid.EffectiveStackSpacing;
            int maxStack = 1;
            foreach (var (rr, ee) in comp)
            {
                var cc = grid.GetCell(rr, ee);
                if (cc != null) maxStack = Mathf.Max(maxStack, cc.BlockStackCt);
            }
            float lift = stackSpacing * maxStack + _yOffset;

            var rects = Decompose(comp);
            var region = new Region { Threshold = threshold };
            for (int i = 0; i < rects.Count; i++)
            {
                var (r0, e0, r1, e1) = rects[i];
                Vector3 center = (grid.CellPos(r0, e0) + grid.CellPos(r0, e1)
                                  + grid.CellPos(r1, e0) + grid.CellPos(r1, e1)) * 0.25f;
                center.y += lift; // đặt băng trên đỉnh block
                float width = (e1 - e0 + 1) * colStep;
                float depth = (r1 - r0 + 1) * rowStep;
                bool main = i == 0; // rects[0] là khối LỚN nhất → hiện countdown
                var ice = _icePool.Get();
                ice.Fit(center, width, depth, grid.Rotation, main, threshold);
                region.Ices.Add(ice);
                if (main) region.Main = ice;
            }
            if (region.Ices.Count > 0) _regions.Add(region);
        }

        // Khoảng cách 2 cell kề theo cột / theo hàng của grid (để scale Ice cho vừa số cell).
        private static float GridColStep(BlockGridData grid)
        {
            for (int r = 0; r < grid.Rows; r++)
                if (grid.ElementsInRow(r) >= 2)
                    return Vector3.Distance(grid.CellPos(r, 0), grid.CellPos(r, 1));
            return 1f;
        }

        private static float GridRowStep(BlockGridData grid)
        {
            if (grid.Rows >= 2) return Vector3.Distance(grid.CellPos(0, 0), grid.CellPos(1, 0));
            return 1f;
        }

        // ---- Chia 1 tập cell thành các hình chữ nhật, LỚN NHẤT trước (greedy) ----
        private static List<(int r0, int e0, int r1, int e1)> Decompose(HashSet<(int, int)> cells)
        {
            var remaining = new HashSet<(int, int)>(cells);
            var result = new List<(int, int, int, int)>();
            while (remaining.Count > 0)
            {
                var best = MaxRect(remaining);
                result.Add(best);
                for (int r = best.Item1; r <= best.Item3; r++)
                    for (int e = best.Item2; e <= best.Item4; e++)
                        remaining.Remove((r, e));
            }
            return result;
        }

        // Hình chữ nhật đặc (mọi cell đều thuộc tập) có DIỆN TÍCH lớn nhất.
        private static (int, int, int, int) MaxRect(HashSet<(int, int)> cells)
        {
            (int, int, int, int) best = default;
            int bestArea = -1;
            foreach (var (r0, e0) in cells)
            {
                int maxE = e0;
                while (cells.Contains((r0, maxE + 1))) maxE++;
                for (int e1 = e0; e1 <= maxE; e1++)
                {
                    int r1 = r0;
                    while (RowIn(cells, r1 + 1, e0, e1)) r1++;
                    int area = (e1 - e0 + 1) * (r1 - r0 + 1);
                    if (area > bestArea) { bestArea = area; best = (r0, e0, r1, e1); }
                }
            }
            return best;
        }

        private static bool RowIn(HashSet<(int, int)> cells, int r, int e0, int e1)
        {
            for (int e = e0; e <= e1; e++) if (!cells.Contains((r, e))) return false;
            return true;
        }
    }
}

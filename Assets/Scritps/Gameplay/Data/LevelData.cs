using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Wayfu.Lamkn
{
    /// <summary>
    /// Cấu hình 1 level. Các thông số gameplay chung (gun/path/bullet/block spacing) nằm ở
    /// <see cref="GameSettings"/>; ở đây chỉ giữ dữ liệu RIÊNG theo level: path shape, camera, slots,
    /// grids block, props/obstacles, prefab. Bám sát schema PixelShoot_2 (per-cell, holes, obstacles).
    /// </summary>
    [CreateAssetMenu(fileName = "LevelData", menuName = "Wayfu/Level Data")]
    public class LevelData : ScriptableObject
    {
        [Header("Meta")]
        public GameDifficulty CurGameDifficulty = GameDifficulty.Easy;
        public bool SkipLevelLoop;
        public int NumberOfColors;
        public string[] MechanicNames;

        [Header("Gun Range Override")]
        [Tooltip("Tắt = dùng GunFireRange trong GameSettings. Bật = level này dùng range riêng bên dưới.")]
        public bool OverrideGunFireRange;
        [Min(0.01f), Tooltip("Range riêng chỉ áp dụng khi Override Gun Fire Range được bật.")]
        public float CustomGunFireRange = 3f;

        public float ResolveGunFireRange(GameSettings settings)
        {
            if (OverrideGunFireRange) return Mathf.Max(0.01f, CustomGunFireRange);
            return settings != null ? settings.GunFireRange : 3f;
        }

        [Header("Path shape (loop) — riêng theo level")]
        public List<Vector3> PathWaypoints = new List<Vector3>();
        public bool IsClosed = true;
        [Tooltip("RoundedCorner = nối thẳng + bo góc theo CornerRadius. Bezier = cong mượt toàn phần " +
                 "(Catmull-Rom → Bezier bậc 3), vẫn đi qua mọi waypoint; CornerRadius không dùng.")]
        public PathStyle PathStyle = PathStyle.RoundedCorner;
        [Tooltip("Bán kính bo góc — CHỈ dùng khi PathStyle = RoundedCorner.")]
        public float CornerRadius = 1f;
        // Path Width đã chuyển sang GameSettings (dùng CHUNG mọi level) — xem GameSettings.PathWidth.

        [Header("Slots (số hàng gun + thứ tự gun ra)")]
        [Tooltip("LOẠI MAP theo số slot (2..5). MapController spawn map prefab tương ứng; vị trí các slot do " +
                 "chính prefab quyết định. Số phần tử Slots nên = SlotCount (Level Tool tự co/giãn khi đổi).")]
        [Range(2, 5)] public int SlotCount = 2;
        public List<SlotData> Slots = new List<SlotData>();

        [Header("Hole capacity (số block xếp chồng mỗi cell)")]
        [Tooltip("Số block stack mỗi cell khi Generate Cells (stack = HoleCapacity).")]
        [Min(1)] public int HoleCapacity = 3;

        [Header("Grids block (mỗi grid định hình bằng 4 góc; nhiều grid / level)")]
        public List<BlockGridData> Grids = new List<BlockGridData>();

        [Header("Camera Frame (top-down)")]
        [Tooltip("Ortho Size = nửa chiều cao khung nhìn (world units) — dùng khi chưa gán Scene Camera.")]
        public float CameraOrthoSize = 9f;
        public Vector2 ScreenAspect = new Vector2(9f, 16f);
        public Vector3 CameraCenter;

        [Header("Props / Obstacles")]
        public List<GameBoardPropData> BoardProps = new List<GameBoardPropData>();
        public List<BlockObstacleData> Obstacles = new List<BlockObstacleData>();

        [Header("Prefabs (optional — null thì fallback primitive, đều dùng Pooler)")]
        public Gun GunPrefab;
        [Tooltip("Prefab 1 CELL (node chứa stack). Grid sinh cell từ đây, rồi cell sinh block từ BlockPrefab.")]
        public BlockCell BlockCellPrefab;
        public Block BlockPrefab;
        public Bullet BulletPrefab;

        /// <summary>Tổng số bullet theo màu (gộp toàn bộ gun trong mọi slot).</summary>
        public Dictionary<TypeColor, int> TotalBullets()
        {
            var d = new Dictionary<TypeColor, int>();
            foreach (var s in Slots)
            {
                if (s?.Guns == null) continue;
                foreach (var g in s.Guns)
                {
                    if (g == null) continue;
                    d.TryGetValue(g.Color, out var v);
                    d[g.Color] = v + g.CountBullet;
                }
            }
            return d;
        }

        /// <summary>Tổng số block theo màu (gộp BlockStackCt của mọi cell trong mọi grid).</summary>
        public Dictionary<TypeColor, int> TotalBlocks()
        {
            var d = new Dictionary<TypeColor, int>();
            foreach (var grid in Grids)
            {
                if (grid == null) continue;
                if (grid.Cells != null)
                    foreach (var c in grid.Cells)
                    {
                        if (c == null) continue;
                        // Bắn được theo VỊ TRÍ ô: cell ở ô không bắn được vẫn dồn sang ô bắn được → vẫn cần
                        // đạn khớp, tính bình thường.
                        if (c.BlockStackCt > 0)
                        {
                            d.TryGetValue(c.Color, out var v);
                            d[c.Color] = v + c.BlockStackCt;
                        }
                        // Cell Spawner/Spawner8: các cell trong hàng đợi cũng phải bắn → tính vào cân bằng.
                        if (!c.Type.IsSpawner() || c.Queue == null) continue;
                        foreach (var q in c.Queue)
                        {
                            if (q == null || q.BlockStackCt <= 0) continue;
                            d.TryGetValue(q.Color, out var qv);
                            d[q.Color] = qv + q.BlockStackCt;
                        }
                    }
                // Block trong hàng đợi refill của spawner cũng cần khớp bullet.
                if (grid.PendingRefill != null)
                    foreach (var p in grid.PendingRefill)
                    {
                        if (p == null || p.BlockStackCt <= 0) continue;
                        d.TryGetValue(p.Color, out var v);
                        d[p.Color] = v + p.BlockStackCt;
                    }
            }
            return d;
        }

        /// <summary>Kiểm tra: mỗi màu, tổng bullet == tổng block (yêu cầu #10).</summary>
        public bool ValidateColorBalance(out string report)
        {
            var bullets = TotalBullets();
            var blocks = TotalBlocks();

            var colors = new HashSet<TypeColor>();
            foreach (var k in bullets.Keys) colors.Add(k);
            foreach (var k in blocks.Keys) colors.Add(k);

            var sb = new StringBuilder();
            bool ok = true;
            foreach (var c in colors)
            {
                bullets.TryGetValue(c, out var b);
                blocks.TryGetValue(c, out var bl);
                bool match = b == bl;
                if (!match) ok = false;
                sb.AppendLine($"{c}: bullets={b} | blocks={bl} → {(match ? "OK" : "MISMATCH")}");
            }
            if (colors.Count == 0) sb.AppendLine("(chưa có gun/block nào)");
            report = sb.ToString();
            return ok;
        }
    }
}

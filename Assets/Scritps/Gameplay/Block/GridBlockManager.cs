using System.Collections.Generic;
using UnityEngine;

namespace Wayfu.Lamkn
{
    /// <summary>
    /// Quản lý các grid block dạng vòng cung. Mỗi grid = nhiều HÀNG cung; row 0 = ngoài cùng, SÁT PATH.
    /// Gun ăn từ ngoài vào trong: cell (r,e) chỉ bắn được khi các cell CHẶN nó ở hàng r-1 đã bị phá hết
    /// (Uniform → chặn 1:1; ArcLength → cell giữa bị 2 cell chặn theo góc). Row 0 luôn bắn được.
    /// Khi row 0 sạch → các hàng sau DỒN ra sát path, spawner refill thêm hàng ở phía trong.
    /// </summary>
    public class GridBlockManager : Singleton<GridBlockManager>
    {
        // Config lấy từ GameSettings, nạp lại mỗi lần Build.
        private float _collapseDuration = 0.25f;
        private bool _frontRowFirst; // CoreType = FrontRowFirst → ưu tiên cell hàng 0 hơn cell sập xuống

        // Nguồn Spawner: 1 Ô CỐ ĐỊNH trên lưới. Cell ở đó dồn lên như cell thường; hễ ô trống là nhả mục kế
        // trong hàng đợi ẩn ra đúng ô đó — lặp tới khi cạn.
        private class SpawnerSource
        {
            public int Row, Col;
            public Queue<PendingBlockData> Queue;
            public float DirAngle;
            public bool CustomDirection;
            public bool EightWay; // Spawner8: nhả vào ô trống ở 8 ô quanh ô gốc
            public Spawner8Directions EightDirections;
            public bool Line;     // SpawnerLine: nhả theo 1 đường (StepRow,StepCol) tới Reach ô
            public int Reach;     // SpawnerLine: số ô tối đa (0 = tới mép grid)
            public int StepRow, StepCol; // SpawnerLine: bước đi 1 ô theo hướng nhả
        }

        // Thứ tự nhả của Spawner8 quanh ô gốc: 4 ô dọc/ngang trước, 4 ô chéo sau
        // (dr = lệch hàng: −1 tiến về path; dc = lệch cột).
        private static readonly int[,] EightNeighbors =
        {
            { -1, 0 }, { 1, 0 }, { 0, -1 }, { 0, 1 },     // dọc/ngang
            { -1, -1 }, { -1, 1 }, { 1, -1 }, { 1, 1 },   // chéo
        };

        private class GridRuntime
        {
            public BlockGridData Data;
            public readonly List<BlockCell[]> Rows = new List<BlockCell[]>(); // [row][index]; null = đã phá
            /// <summary>
            /// [row][index] — true = ô LỖ, do designer xoá trong level (BlockStackCt &lt;= 0).
            /// Phải tách khỏi "null vì bị bắn sạch": lỗ là VĨNH VIỄN, không cell nào được dồn hay refill
            /// vào. Chỉ nhìn Rows[r][e] == null thì 2 thứ đó giống hệt nhau và lỗ sẽ bị lấp mất.
            /// </summary>
            public readonly List<bool[]> Holes = new List<bool[]>();
            /// <summary>
            /// [row][index] — true = ô GỐC của 1 Spawner8. Giữ CỐ ĐỊNH cả sau khi spawner đó nhả hết &amp;
            /// biến mất: 2 spawner KHÔNG được nhả cell vào ô của nhau, kể cả khi ô đó đã hết queue (yêu cầu).
            /// </summary>
            public readonly List<bool[]> SpawnerCells = new List<bool[]>();
            /// <summary>
            /// [row][index] — false = Ô này KHÔNG bắn được (theo VỊ TRÍ, không theo cell). Cell nào đang
            /// nằm ở ô này thì gun bỏ qua; cell dồn sang ô bắn được thì lại bắn được. Đọc từ
            /// BlockCellData.Shootable của ô lúc Build.
            /// </summary>
            public readonly List<bool[]> Shootable = new List<bool[]>();
            public readonly List<SpawnerSource> Sources = new List<SpawnerSource>();
            public Transform Root;                 // node cha để gắn cell (kể cả cell refill)
            public Queue<PendingBlockData> Pending; // hàng đợi refill mức GRID (lấp hàng sâu nhất)
            public bool HasIndicators;             // còn mũi tên spawner nào cần dọn/gắn không
        }

        private readonly List<GridRuntime> _grids = new List<GridRuntime>();
        private bool _everHadBlocks;

        // Wall/obstacle chắn tia bắn: collider của các obstacle sinh theo level (LevelController đăng ký sau
        // khi spawn). Gun không được CHỐT cell nếu đường thẳng gun→cell bị 1 obstacle nào cắt ngang (LOS).
        private readonly List<Collider> _obstacles = new List<Collider>();

        /// <summary>LevelController gọi sau khi Instantiate 1 obstacle: gom mọi collider của nó để chặn tia.</summary>
        public void RegisterObstacle(GameObject obstacle)
        {
            if (obstacle == null) return;
            foreach (var col in obstacle.GetComponentsInChildren<Collider>(true))
                if (col != null && !col.isTrigger) _obstacles.Add(col);
        }

        public void Build(LevelData level)
        {
            Clear();
            var gs = GameSettings.Instance;
            _collapseDuration = gs != null ? gs.BlockCollapseDuration : 0.25f;
            _frontRowFirst = gs != null && gs.CoreType == CoreGameType.FrontRowFirst;

            foreach (var grid in level.Grids)
            {
                if (grid == null) continue;
                var gridGo = new GameObject("Grid");
                gridGo.transform.SetParent(transform);

                var gr = new GridRuntime
                {
                    Data = grid,
                    Root = gridGo.transform,
                    Pending = BuildPendingQueue(grid),
                };

                for (int r = 0; r < grid.Rows; r++)
                {
                    int count = grid.ElementsInRow(r);
                    var row = new BlockCell[count];
                    var holes = new bool[count];
                    var spawners = new bool[count];
                    var shootable = new bool[count];
                    for (int e = 0; e < count; e++)
                    {
                        var cellData = grid.GetCell(r, e);
                        // Bắn được theo VỊ TRÍ ô (không theo cell): đọc từ data của ô. Ô không có data coi là bắn được.
                        shootable[e] = cellData == null || cellData.Shootable;
                        // Designer xoá ô này (stack <= 0) → LỖ vĩnh viễn: không dựng cell, và về sau cũng
                        // không cho cell nào dồn/refill vào (xem AdvanceOnce, TryRefill).
                        if (cellData == null || cellData.BlockStackCt <= 0) { holes[e] = true; continue; }
                        row[e] = CreateCell(gr, $"Cell_r{r}_e{e}", grid.CellPos(r, e), cellData);

                        if (!cellData.Type.IsSpawner()) continue;
                        bool eight = cellData.Type == BlockCellType.Spawner8;
                        bool line = cellData.Type == BlockCellType.SpawnerLine;
                        var q = new Queue<PendingBlockData>();
                        // Nguồn tĩnh (Spawner8/SpawnerLine) = conveyor: MÀU HIỆN TẠI (của chính ô gốc) là ĐẦU
                        // sequence → nhả ra trước, rồi mới tới các màu trong Queue. Spawner thường giữ nguyên:
                        // ô gốc là cell thường, Queue chỉ là các mục refill phía sau.
                        if (eight || line)
                            q.Enqueue(new PendingBlockData { Color = cellData.Color, BlockStackCt = cellData.BlockStackCt });
                        if (cellData.Queue != null)
                            foreach (var it in cellData.Queue)
                                if (it != null && it.BlockStackCt > 0) q.Enqueue(it);
                        if (q.Count == 0) continue;

                        // Ô gốc của MỌI loại spawner: cấm spawner khác nhả/dồn vào (yêu cầu). KHÔNG chặn dồn
                        // dọc cũ (AdvanceOnce không xét cờ này) nên spawner thường vẫn hoạt động như trước.
                        spawners[e] = true;

                        var srcObj = new SpawnerSource
                        {
                            Row = r, Col = e, Queue = q,
                            DirAngle = cellData.SpawnerDirectionAngleZ + grid.CellDirectionOffset,
                            CustomDirection = cellData.UseCustomDirection || line,
                            EightWay = eight,
                            // Asset cũ chưa có field này deserialize về None → giữ tương thích như Spawner8 8 hướng.
                            EightDirections = cellData.Spawner8Directions == Spawner8Directions.None
                                ? Spawner8Directions.All : cellData.Spawner8Directions,
                            Line = line,
                            Reach = cellData.SpawnerReach,
                        };
                        if (line) LineStep(grid, srcObj.DirAngle, out srcObj.StepRow, out srcObj.StepCol);
                        gr.Sources.Add(srcObj);
                    }
                    gr.Rows.Add(row);
                    gr.Holes.Add(holes);
                    gr.SpawnerCells.Add(spawners);
                    gr.Shootable.Add(shootable);
                }
                gr.HasIndicators = gr.Sources.Count > 0; // mồi cờ trước, Refresh tự cập nhật lại sau
                RefreshSpawnerIndicators(gr);
                _grids.Add(gr);
            }

            _everHadBlocks = RemainingBlocks > 0;
        }

        private static Queue<PendingBlockData> BuildPendingQueue(BlockGridData grid)
        {
            var q = new Queue<PendingBlockData>();
            if (grid.PendingRefill != null)
                foreach (var p in grid.PendingRefill)
                    if (p != null && p.BlockStackCt > 0) q.Enqueue(p);
            return q;
        }

        // Sinh 1 cell TỪ BlockCellPrefab (qua pool); chính cell sẽ sinh block từ BlockPrefab trong Build().
        private BlockCell CreateCell(GridRuntime gr, string cellName, Vector3 pos, BlockCellData data)
        {
            var cell = PoolManager.Instance.GetCell();
            cell.name = cellName;
            cell.transform.SetParent(gr.Root);
            cell.transform.position = pos;
            cell.transform.rotation = Quaternion.Euler(0f, CellAngle(gr, data), 0f);
            // Cell chỉ là node chứa → giữ scale 1; CellScale của grid áp thẳng lên BLOCK bên trong.
            cell.transform.localScale = Vector3.one;
            cell.Build(data, gr.Data.EffectiveStackSpacing, gr.Data.CellScale, this);
            cell.SetMultiSide(gr.Data.ShootableEdges != GridEdges.None);
            return cell;
        }

        /// <summary>
        /// Hướng dồn/nhả của cell. Rect: tính thẳng từ grid (mọi cell chung 1 hướng) nên xoay grid là
        /// khớp ngay, data cũ chưa Generate lại cũng không lệch. Arc: lấy góc riêng đã lưu trong data —
        /// mỗi cell 1 góc, và người dùng kéo mũi tên chỉnh tay được.
        /// </summary>
        private static float CellAngle(GridRuntime gr, BlockCellData data) =>
            // Rect/Spline luôn theo shape để các cell đồng bộ khi grid bị xoay hoặc uốn lại.
            // Chỉ cell được đánh dấu chỉnh hướng riêng (và SpawnerLine vốn phải nhả theo mũi tên)
            // mới đọc góc đã lưu. Arc vẫn giữ góc riêng cho mỗi cell như trước.
            gr.Data.CellAngleFromShape && !data.UseCustomDirection && data.Type != BlockCellType.SpawnerLine
                ? gr.Data.DefaultCellAngle(data.SpawnerDepth, data.BlockCol)
                : data.SpawnerDirectionAngleZ + gr.Data.CellDirectionOffset;

        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
            _grids.Clear();
            _obstacles.Clear(); // obstacle của level cũ bị Destroy cùng "Obstacles" root — bỏ ref chết
            _everHadBlocks = false;
        }

        // Mặc định cũ: cell (r,e) bắn được khi MỌI cell chặn nó ở hàng trước đã bị phá. Row 0 luôn bắn được.
        private static bool IsShootableLegacy(GridRuntime gr, int r, int e)
        {
            if (ReverseGridDirection(gr))
                return ExposedAlongRows(gr, r, e, +1); // offset 180° → mặt sau/hàng cuối là mặt bắn
            if (r <= 0) return true;
            var prev = gr.Rows[r - 1];
            BlockGridData.FrontIndices(gr.Rows[r].Length, prev.Length, e, out int a, out int b);
            if (a >= 0 && a < prev.Length && prev[a] != null) return false;
            if (b >= 0 && b < prev.Length && prev[b] != null) return false;
            return true;
        }

        // Cell (r,e) có bắn được KHÔNG XÉT vị trí gun — dùng để hỏi "còn cell màu này bắn được không".
        // Grid bật ShootableEdges: lộ ra ở BẤT KỲ cạnh nào đang bật. Cell Indestructible không bao giờ bị ngắm.
        private static bool IsShootable(GridRuntime gr, int r, int e)
        {
            var cell = gr.Rows[r][e];
            if (cell != null && cell.Indestructible) return false;

            var edges = gr.Data.ShootableEdges;
            if (edges == GridEdges.None) return IsShootableLegacy(gr, r, e);

            if ((edges & GridEdges.Front) != 0 && ExposedAlongRows(gr, r, e, -1)) return true;
            if ((edges & GridEdges.Back)  != 0 && ExposedAlongRows(gr, r, e, +1)) return true;
            if ((edges & GridEdges.Left)  != 0 && ExposedAlongRow(gr, r, e, -1)) return true;
            if ((edges & GridEdges.Right) != 0 && ExposedAlongRow(gr, r, e, +1)) return true;
            return false;
        }

        // Như IsShootable nhưng CÓ xét vị trí gun: với grid nhiều mặt, chỉ bắn được cell lộ ra ở cạnh mà
        // gun đang đứng về PHÍA NGOÀI cạnh đó — nhờ vậy gun đi 1 bên chỉ ăn mặt gần, KHÔNG xuyên qua grid
        // bắn luôn mặt bên kia. (Grid mặc định 1 chiều thì vị trí gun không đổi kết quả.)
        private static bool IsShootableFromGun(GridRuntime gr, int r, int e, Vector3 from)
        {
            var cell = gr.Rows[r][e];
            if (cell != null && cell.Indestructible) return false;

            var edges = gr.Data.ShootableEdges;
            if (edges == GridEdges.None)
            {
                Vector3 legacyCellPos = cell != null ? cell.transform.position : gr.Data.CellPos(r, e);
                Vector3 legacyToGun = from - legacyCellPos; legacyToGun.y = 0f;
                // Cell hướng vào phía trong grid.  Với Arc/Spline, hướng này khác
                // nhau ở từng ô; dùng Grid.Forward chung sẽ đảo/sai mặt của các ô
                // nằm ở đoạn cong và làm chúng không bao giờ được chọn.
                Vector3 legacyFwd = cell != null ? cell.transform.forward : -gr.Data.Forward;
                legacyFwd.y = 0f;
                legacyFwd = legacyFwd.sqrMagnitude < 1e-6f ? Vector3.forward : legacyFwd.normalized;
                // transform.forward là chiều cell trượt RA PATH, nên cũng chính là
                // pháp tuyến của mặt trước mà gun phải đứng ở phía ngoài.
                return Vector3.Dot(legacyToGun, legacyFwd) > 0f && IsShootableLegacy(gr, r, e);
            }

            Vector3 cellPos = cell != null ? cell.transform.position : gr.Data.CellPos(r, e);
            // Cùng lý do: mặt Front/Back của spline/arc phải theo hướng cell,
            // không phải một hướng cố định của toàn grid.
            Vector3 fwd = cell != null ? cell.transform.forward : -gr.Data.Forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude < 1e-6f ? Vector3.forward : fwd.normalized;
            Vector3 rgt = Vector3.Cross(Vector3.up, fwd);   // local +X trên sàn
            Vector3 toGun = from - cellPos; toGun.y = 0f;   // hướng từ cell ra gun

            // Lộ ra ở cạnh đang bật VÀ gun nằm về phía NGOÀI cạnh đó (dot với pháp tuyến ngoài > 0).
            if ((edges & GridEdges.Front) != 0 && ExposedAlongRows(gr, r, e, -1) && Vector3.Dot(toGun,  fwd) > 0f) return true;
            if ((edges & GridEdges.Back)  != 0 && ExposedAlongRows(gr, r, e, +1) && Vector3.Dot(toGun, -fwd) > 0f) return true;
            if ((edges & GridEdges.Left)  != 0 && ExposedAlongRow(gr, r, e, -1) && Vector3.Dot(toGun, -rgt) > 0f) return true;
            if ((edges & GridEdges.Right) != 0 && ExposedAlongRow(gr, r, e, +1) && Vector3.Dot(toGun,  rgt) > 0f) return true;
            return false;
        }

        // Lộ ra theo CỘT (Front/Back): mọi cell cùng index ở các hàng giữa (r, cạnh) đã trống chưa.
        // dir = -1 quét về hàng 0 (Front), +1 quét về hàng cuối (Back). Index kẹp theo độ dài hàng để
        // hợp cả grid cột lệch (Arc-ArcLength) lẫn cột thẳng (Rect/Uniform).
        private static bool ExposedAlongRows(GridRuntime gr, int r, int e, int dir)
        {
            int rr = r + dir;
            if (rr < 0 || rr >= gr.Rows.Count) return true;
            int idx = Mathf.Min(e, gr.Rows[rr].Length - 1);
            // A blank adjacent row separates two independent collapse segments.
            // Do not let cells from an older segment farther along the same column
            // keep this segment hidden.
            return idx < 0 || gr.Rows[rr][idx] == null;
        }

        // Lộ ra theo HÀNG (Left/Right): mọi cell giữa (r,e) và mép hàng đã trống chưa.
        // dir = -1 quét về index 0 (Left), +1 quét về index cuối (Right).
        private static bool ExposedAlongRow(GridRuntime gr, int r, int e, int dir)
        {
            var row = gr.Rows[r];
            for (int i = e + dir; i >= 0 && i < row.Length; i += dir)
                if (row[i] != null) return false;
            return true;
        }

        /// <summary>
        /// Chọn cell cùng màu để bắn, trong số các cell KHÔNG BỊ CHẶN và nằm trong
        /// <paramref name="detectRange"/> (vòng phát hiện, tròn trên sàn XZ). Xét MỌI grid — gun chạy giữa
        /// 2 grid sẽ ăn được cả hai bên.
        /// <para>Thứ tự ưu tiên theo <see cref="GameSettings.CoreType"/>: NearestCell = cell GẦN gun nhất
        /// (khoảng cách XZ) — ăn từ gần ra xa, bền với sai số IsShootable (cell sâu lỡ hở vẫn ở xa nên
        /// không bị chọn). FrontRowFirst = cell có Depth GỐC nhỏ thắng trước (Depth 0 = sinh ở hàng 0, chưa
        /// bị bắn), cùng Depth mới xét gần nhất → cell front gốc luôn hơn cell sập xuống (Depth ≥1) dù cell
        /// sập đã dồn ra ngang hàng 0 và ở gần hơn.</para>
        /// <para>Bộ lọc range+góc ở đây phải khớp Gun.InDetectZone: Gun gọi lại mỗi frame để buông target
        /// khi quạt đã trôi qua, hai bên lệch nhau thì target vừa chọn xong đã bị loại ngay frame sau.</para>
        /// </summary>
        /// <summary>
        /// Cell cùng màu gần nhất ở hàng ngoài cùng còn bắn được, trong vùng bắn của MỘT nòng: bán kính
        /// <paramref name="detectRange"/>, quạt tính TỪ hướng trước mặt <paramref name="forward"/> rồi toả
        /// sang sườn của nòng đúng <paramref name="spreadAngle"/> độ.
        /// <paramref name="side"/>: +1 = nòng phải, −1 = nòng trái.
        /// <paramref name="exclude"/>: target của nòng bên kia — để 2 nòng không bắn trùng 1 cell.
        /// <paramref name="losFrom"/>: gốc tia để xét CELL KHÁC chắn đường (vị trí NÒNG của bên đang
        /// quét). Cell chắn chỉ chặn nòng có tia bị cắt — nòng bên kia không vướng vẫn bắn bình thường.
        /// Bỏ trống thì dùng <paramref name="from"/> (tâm gun).
        /// </summary>
        /// <paramref name="readyBefore"/>: ≥0 bật chế độ ưu tiên PER-GUN (dùng cho LASER) — cell có
        /// <see cref="BlockCell.SettleStamp"/> ≤ mốc này (= đã đứng sẵn từ đầu lap của gun) được ưu tiên
        /// hơn cell vừa SẬP trong lap (SettleStamp &gt; mốc); cùng nhóm mới xét gần nhất. &lt;0 = tắt, dùng
        /// CoreType (NearestCell / FrontRowFirst) như đạn thường.
        /// <paramref name="claimant"/>: token của NÒNG đang hỏi — bỏ qua cell đã bị nòng KHÁC (gun nào
        /// cũng vậy) chốt, để 2 gun không cùng bắn 1 cell (đổ đạn trùng → số đạn bị lẻ). null = không xét.
        public BlockCell FindTargetCell(TypeColor color, Vector3 from, Vector3 forward, float side,
                                        float detectRange, float spreadAngle, BlockCell exclude = null,
                                        Vector3? losFrom = null, float readyBefore = -1f, object claimant = null)
        {
            Vector3 losOrigin = losFrom ?? from;
            bool useReady = readyBefore >= 0f;   // laser: ưu tiên theo lúc cell sập (per-gun), không theo Depth
            BlockCell best = null;
            int bestDepth = int.MaxValue;   // chỉ dùng khi _frontRowFirst; theo Depth GỐC, không phải row runtime
            bool bestFell = true;           // chỉ dùng khi useReady; true = "vừa sập" (kém ưu tiên nhất)
            float bestSqr = float.MaxValue;
            float detectSqr = detectRange * detectRange;

            // Quạt tính trên sàn XZ. Chuẩn hoá forward + dựng vector sườn 1 lần ở ngoài vòng lặp để so
            // bằng dot/cosin, khỏi gọi Vector3.Angle (Acos) cho từng cell.
            forward.y = 0f;
            bool hasDir = forward.sqrMagnitude > 1e-6f;
            if (hasDir) forward.Normalize();
            Vector3 sideVec = Vector3.Cross(Vector3.up, forward); // +X local của gun
            float sideSign = side >= 0f ? 1f : -1f;
            // Toả tối đa 180° là kín nửa mặt phẳng của nòng — quá số đó không còn ý nghĩa.
            float cosSpread = Mathf.Cos(Mathf.Clamp(spreadAngle, 0f, 180f) * Mathf.Deg2Rad);

            foreach (var gr in _grids)
            {
                // Grid gán CỨNG 1 bên → chỉ nòng cùng bên bắn được; grid của nòng bên kia thì bỏ nguyên.
                // Khi đã khớp bên thì KHÔNG kiểm tra sườn hình học nữa (nó lật dấu khi path cong / grid chếch).
                var gside = gr.Data.Side;
                bool sideLocked = gside != GridSide.Any;
                // Grid.Side is authoring metadata only.  The real barrel is
                // determined below from the cell's position against the current
                // path tangent; that remains correct on curved/reversed paths.

                for (int r = 0; r < gr.Rows.Count; r++)
                {
                    var row = gr.Rows[r];
                    for (int e = 0; e < row.Length; e++)
                    {
                        var cell = row[e];
                        if (cell == null || cell.Color != color) continue;
                        if (!IsPositionShootable(gr, r, e)) continue; // Ô này không bắn được (theo vị trí)
                        if (cell.Indestructible) continue; // Spawner8 ở giữa: không bao giờ bị ngắm
                        if (cell.Frozen) continue;         // cell băng: chưa tan thì không bắn được
                        if (cell == exclude) continue;  // nòng bên kia đang bắn cell này → không bắn trùng
                        if (claimant != null && !cell.ClaimFreeFor(claimant)) continue; // nòng KHÁC đã chốt
                        if (cell.PendingEntry) continue; // đang TRƯỢT (nhả mới / dồn hàng) → chưa cho ngắm
                        if (!IsShootableFromGun(gr, r, e, from)) continue;
                        Vector3 d = cell.transform.position - from; d.y = 0f;
                        float sqr = d.sqrMagnitude;
                        if (sqr > detectSqr) continue;
                        // Trong quạt của nòng ⇔ ĐÚNG SƯỜN (dot với vector sườn cùng dấu) VÀ lệch khỏi
                        // hướng trước mặt không quá spreadAngle (dot(forward, d̂) >= cos spread).
                        // sqr>eps để cell trùng vị trí gun không chia cho 0 (luôn coi là trong quạt).
                        // Grid gán bên → bỏ kiểm tra SƯỜN (đã chốt theo Side), nhưng GIỮ quạt trước mặt để
                        // không bắn giật lùi vào grid đã đi qua.
                        if (hasDir && sqr > 1e-6f)
                        {
                            // Path tangent chooses the barrel side at this exact point:
                            // right/left is measured from the current A -> B path direction.
                            if (Vector3.Dot(sideVec, d) * sideSign < 0f) continue;
                            // Grid đã gán bên vẫn được bắn khi ở NGANG gun (dot = 0),
                            // nhưng không được chọn cell đã ở phía sau. Nếu không, TickBarrel
                            // sẽ chốt rồi CanKeepTarget hủy target trước viên đầu tiên.
                            float forwardDot = Vector3.Dot(forward, d);
                            if (sideLocked)
                            {
                                if (forwardDot < -0.001f) continue;
                            }
                            else if (forwardDot < cosSpread * Mathf.Sqrt(sqr)) continue;
                        }
                        // NearestCell: gần nhất thắng. FrontRowFirst: cell có Depth GỐC nhỏ thắng trước
                        // (cell sinh ở hàng 0 = Depth 0, chưa bị bắn), cùng Depth mới xét gần nhất. Dùng
                        // Depth chứ KHÔNG dùng row runtime: cell sập xuống dồn ra hàng 0 vẫn giữ Depth cũ
                        // (≥1) nên luôn thua cell front gốc dù đã ngang hàng row 0 và ở gần hơn.
                        int depth = cell.Depth;
                        // Laser (useReady): cell "đã đứng sẵn từ đầu lap" (fell=false) luôn thắng cell
                        // "vừa sập trong lap" (fell=true); cùng nhóm mới xét gần nhất. Đạn thường thì theo
                        // CoreType (FrontRowFirst dùng Depth, còn lại dùng gần nhất).
                        bool fell = useReady && cell.SettleStamp > readyBefore;
                        bool better;
                        if (useReady)
                            better = (!fell && bestFell) || (fell == bestFell && sqr < bestSqr);
                        else if (_frontRowFirst)
                            better = depth < bestDepth || (depth == bestDepth && sqr < bestSqr);
                        else
                            better = sqr < bestSqr;
                        if (!better) continue;
                        // CELL KHÁC chắn giữa NÒNG và cell này → KHÔNG chốt (không bắn xuyên qua cell đứng
                        // trước). Chỉ xét khi cell đã thắng best hiện tại → LOS chạy tối đa 1 lần/lần cải
                        // thiện thay vì mọi ứng viên. Đo từ losOrigin (muzzle bên đang quét) chứ không phải
                        // tâm gun: cell chắn bên nào chỉ chặn nòng bên đó. (Obstacle/wall KHÔNG còn chặn.)
                        // Endpoint = ĐIỂM NGOÀI CÙNG của cell (mặt gần path nhất) = tâm + forward × nửa sâu:
                        // cell.forward chỉ về phía path (hướng dồn). Bắn tới mặt trước nên không bị chính
                        // phần thân cell che, và khớp với chỗ đạn thật sự chạm.
                        Vector3 aimPoint = cell.transform.position
                            + cell.transform.forward * (0.5f * Mathf.Max(0.01f, gr.Data.RowSpacing));
                        if (IsLineBlockedByCell(losOrigin, cell, aimPoint)) continue;
                        bestDepth = depth; bestSqr = sqr; bestFell = fell; best = cell;
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// Có CELL nào khác đứng chắn đoạn từ NÒNG (<paramref name="from"/> = muzzle left/right) tới ĐIỂM
        /// NGẮM của target (<paramref name="targetPos"/> = điểm ngoài cùng gần path của target, không phải
        /// tâm) không — line-of-sight đo trên sàn XZ như toàn bộ logic ngắm bắn. Mỗi cell chắn coi như 1
        /// HỘP đúng bằng PITCH của grid nó (BlockWidth+Spacing theo ngang, RowSpacing theo dọc) — các hộp
        /// phủ liền nhau nên vùng cell đặc chặn chắc chắn, tia KHÔNG luồn qua kẽ. Dùng chính transform của
        /// cell (đã xoay theo hướng dồn) để hộp thẳng hàng với lưới. Xét cell của MỌI grid: gun đứng giữa 2
        /// grid không bắn xuyên grid này sang grid kia.
        /// <para>Lưu ý BlockWidth trong data có thể = 0 (khoảng cách thật nằm ở Spacing/RowSpacing) nên
        /// KHÔNG được suy footprint từ mình BlockWidth — phải dùng pitch.</para>
        /// </summary>
        /// <summary>
        /// Kiểm lại LOS cho 1 target ĐÃ chốt (laser gọi mỗi frame): có cell khác chắn giữa <paramref name="from"/>
        /// (muzzle) và target không. Gun di chuyển làm cell lọt vào giữa thì trả true → gun buông target,
        /// không bắn xuyên. Dùng tâm cell làm điểm ngắm (ô kề sát target đã được loại trong IsLineBlockedByCell).
        /// </summary>
        public bool IsCellBlockedFrom(Vector3 from, BlockCell target)
        {
            if (target == null) return false;
            return IsLineBlockedByCell(from, target, target.transform.position);
        }

        private bool IsLineBlockedByCell(Vector3 from, BlockCell target, Vector3 targetPos)
        {
            foreach (var gr in _grids)
            {
                // Pitch = khoảng cách tâm-tới-tâm giữa 2 cell (xem CellPosAt: bước ngang = BlockWidth+Spacing,
                // bước dọc = RowSpacing). Hộp = nửa pitch mỗi bên → 2 hộp cạnh nhau chạm mép, không hở.
                float halfX = 0.5f * Mathf.Max(0.01f, gr.Data.BlockWidth + gr.Data.Spacing);
                float halfZ = 0.5f * Mathf.Max(0.01f, gr.Data.RowSpacing);

                // Bỏ qua cell KỀ SÁT target (vòng trong, kể cả ô chéo). Cell cạnh bên target không phải
                // vật che — nó chỉ NẰM CẠNH; nhưng khi gun bắn CHÉO, tia tới target quẹt qua hộp nó và bị
                // chặn oan (cell gần path nhất không bắn được). Ô cách target ≥ 2 pitch mới tính là che thật.
                // Ngưỡng = 1.2 × đường chéo pitch: loại đủ 8 ô quanh (kể cả chéo ~1.28), giữ ô cách ≥ 2 (~1.6).
                float nearSqr = 1.2f * 1.2f * (4f * halfX * halfX + 4f * halfZ * halfZ);

                for (int r = 0; r < gr.Rows.Count; r++)
                {
                    var row = gr.Rows[r];
                    for (int e = 0; e < row.Length; e++)
                    {
                        var c = row[e];
                        // IsEmpty = block đã bị đạn đang bay đặt chỗ hết → cell sắp biến mất, coi như
                        // không còn chắn (không thì cell chết dở khoá tia thêm vài frame vô ích).
                        if (c == null || c == target || c.IsEmpty) continue;
                        Vector3 off = c.transform.position - targetPos; off.y = 0f;
                        if (off.sqrMagnitude < nearSqr) continue; // ô kề sát target → không tính chắn
                        // Đưa 2 đầu đoạn về local space của cell (chỉ xoay quanh Y, scale = 1) rồi test
                        // đoạn↔hộp trên XZ — hộp cao vô hạn theo Y nên bỏ qua chênh lệch độ cao.
                        Vector3 la = c.transform.InverseTransformPoint(from);
                        Vector3 lb = c.transform.InverseTransformPoint(targetPos);
                        if (SegmentHitsBoxXZ(la, lb, new Vector3(-halfX, 0f, -halfZ), new Vector3(halfX, 0f, halfZ)))
                            return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Có obstacle nào cắt đoạn thẳng from→to không (line-of-sight). Đo trên sàn XZ như toàn bộ logic
        /// ngắm bắn: obstacle coi như tường CAO vô hạn theo footprint của nó, khỏi lệ thuộc chênh Y giữa
        /// gun/cell/tường. Với BoxCollider tính chính xác theo OBB (đã gồm xoay + scale); collider khác thì
        /// xấp xỉ bằng world-bounds AABB.
        /// <para>KHÔNG còn dùng để gate chọn target (đã đổi sang <see cref="IsLineBlockedByCell"/> theo
        /// yêu cầu) — giữ lại phòng khi cần bật lại chặn bằng wall.</para>
        /// </summary>
        private bool IsLineBlockedByObstacle(Vector3 from, Vector3 to)
        {
            if (_obstacles.Count == 0) return false;
            for (int i = 0; i < _obstacles.Count; i++)
            {
                var col = _obstacles[i];
                if (col == null || !col.enabled || !col.gameObject.activeInHierarchy) continue;

                if (col is BoxCollider bc)
                {
                    // Đưa 2 đầu đoạn về local space của box: ô hộp thành AABB [center±size/2]; xoay/scale đã
                    // gộp trong InverseTransformPoint. Bỏ trục Y (index 1) → xét footprint XZ.
                    Vector3 la = col.transform.InverseTransformPoint(from);
                    Vector3 lb = col.transform.InverseTransformPoint(to);
                    Vector3 half = bc.size * 0.5f;
                    if (SegmentHitsBoxXZ(la, lb, bc.center - half, bc.center + half)) return true;
                }
                else
                {
                    var b = col.bounds; // world AABB, bỏ trục Y
                    if (SegmentHitsBoxXZ(from, to, b.min, b.max)) return true;
                }
            }
            return false;
        }

        /// <summary>Đoạn a→b có cắt hình chữ nhật [min,max] trên mặt phẳng XZ không (slab test, bỏ trục Y = tường cao vô hạn).</summary>
        private static bool SegmentHitsBoxXZ(Vector3 a, Vector3 b, Vector3 min, Vector3 max)
        {
            Vector3 d = b - a;
            float tmin = 0f, tmax = 1f;
            // Chỉ xét trục X và Z; trục Y bỏ qua. Hot path (per candidate × per obstacle) nên không alloc.
            if (!ClipSlab(a.x, d.x, min.x, max.x, ref tmin, ref tmax)) return false;
            if (!ClipSlab(a.z, d.z, min.z, max.z, ref tmin, ref tmax)) return false;
            return true;
        }

        // Cắt tham số t của đoạn theo 1 dải [mn,mx] trên 1 trục; false = đã chắc chắn không giao.
        private static bool ClipSlab(float a, float d, float mn, float mx, ref float tmin, ref float tmax)
        {
            if (Mathf.Abs(d) < 1e-8f) return a >= mn && a <= mx; // song song trục: chỉ giao nếu nằm trong dải
            float inv = 1f / d;
            float t1 = (mn - a) * inv;
            float t2 = (mx - a) * inv;
            if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
            if (t1 > tmin) tmin = t1;
            if (t2 < tmax) tmax = t2;
            return tmin <= tmax;
        }

        public bool HasFrontCellOfColor(TypeColor color)
        {
            foreach (var gr in _grids)
                for (int r = 0; r < gr.Rows.Count; r++)
                {
                    var row = gr.Rows[r];
                    for (int e = 0; e < row.Length; e++)
                        if (row[e] != null && !row[e].Frozen && IsPositionShootable(gr, r, e) && row[e].Color == color && IsShootable(gr, r, e)) return true;
                }
            return false;
        }

        public void OnCellCleared(BlockCell cell)
        {
            foreach (var gr in _grids)
            {
                bool found = false;
                for (int r = 0; r < gr.Rows.Count && !found; r++)
                {
                    var row = gr.Rows[r];
                    for (int e = 0; e < row.Length; e++)
                    {
                        if (row[e] != cell) continue;
                        row[e] = null;
                        if (cell != null) cell.Despawn(); // trả cell về pool (thay cho Destroy)
                        found = true;
                        break;
                    }
                }
                if (found) { CollapseFront(gr); break; }
            }
            GameController.Instance?.OnBoardChanged();
        }

        // Dồn cell về phía path sau khi 1 cell bị phá, rồi spawner refill ở phía TRONG.
        // Cột thẳng (Rect / Arc-Uniform) → dồn theo CỘT: cell phía sau tiến lên lấp chỗ trống ngay,
        // không phải chờ cả hàng 0 sạch (gun ăn theo cột nên hàng 0 gần như không bao giờ sạch).
        // Arc-ArcLength → cột lệch (4/5/6) nên chỉ dồn được theo HÀNG như cũ.
        private void CollapseFront(GridRuntime gr)
        {
            // Lặp tới khi ổn định. Mỗi vòng chạy CẢ 3 cấp — nhưng quyền lấp Ô TRANH CHẤP là PER-Ô theo
            // KHOẢNG CÁCH (OwnerOf): ô chịu ảnh hưởng của nhiều spawner thì thuộc về nguồn GẦN nhất
            // (tính bằng số Ô); bằng khoảng cách thì Spawner thường > SpawnerLine > Spawner8. Nguồn cạn
            // queue mất ảnh hưởng → nguồn còn lại tiếp quản ô ("spawner hết thì mới đến line/8"). Mỗi cấp
            // trước khi lấp 1 ô đều hỏi OwnerOf, nên thứ tự chạy dưới đây không còn quyết định thắng thua.
            //   1) DỒN CELL HIỆN CÓ: dọc (→ ngang nếu Collapse2D) + refill ô gốc spawner thường + đáy.
            //   2) SPAWNER LINE: dồn tuần tự dọc đường (giới hạn Reach).
            //   3) SPAWNER8: nhả kề + lan tỏa (giới hạn Reach).
            for (int guard = 0; guard < 64; guard++)
            {
                bool moved = false, fed = false;

                // Cấp 1
                // Move each contiguous normal-cell segment by exactly one step.
                // Do not repeat this pass: separated segments must not jump across
                // their gap and merge in the same collapse event.
                moved = gr.Data.Collapse2D ? AdvanceCorner2D(gr) : AdvanceOnce(gr);
                if (moved)
                {
                    // A regular spawner that just moved leaves its source cell
                    // empty. Refill it in this same pass so even its final queued
                    // block appears at that old spawner position without a gap.
                    FeedRegular(gr);
                    // The same rule applies to a SpawnerLine: if its first cell
                    // on the configured line just moved, advance the conveyor and
                    // emit from the source in this same collapse pass.
                    FeedLineFrontGaps(gr);
                    FeedLine(gr, verticalPass: true);
                    FeedLine(gr, verticalPass: false);
                    break;
                }
                fed |= FeedRegular(gr) | TryRefill(gr);
                // Cấp 2 — SpawnerLine (dọc trước, ngang sau).
                fed |= FeedLine(gr, verticalPass: true) | FeedLine(gr, verticalPass: false);
                // Cấp 3 — Spawner8.
                fed |= FeedEightWayAll(gr);
                // One-dimensional grids may receive Spawner8 output, but their
                // existing cells must never be pulled sideways or diagonally.
                if (gr.Data.Collapse2D) moved |= CascadeTowardSpawners(gr);

                if (!moved && !fed) break;
            }
            // Số hàng giữ NGUYÊN (không xoá hàng rỗng) để Row của SpawnerSource luôn trỏ đúng ô.
            RefreshSpawnerIndicators(gr);
        }

        // Cell nằm ở Ô GỐC của Spawner đổi liên tục (cell cũ dồn đi, cell mới nhả ra) → mỗi lần dồn xong
        // phải gắn lại dấu hiệu cho đúng cell đang đứng ở ô đó.
        private static void RefreshSpawnerIndicators(GridRuntime gr)
        {
            // Cờ thay cho "Sources.Count == 0 thì return": thoát sớm theo Sources là SAI — lúc nguồn cuối
            // bị gỡ chính là lúc phải dọn mũi tên còn sót, thoát ra thì nó bật vĩnh viễn. Cờ chỉ để grid
            // không (còn) spawner nào khỏi quét lại toàn bộ cell mỗi lần dồn hàng.
            if (!gr.HasIndicators) return;

            foreach (var row in gr.Rows)
                foreach (var c in row)
                    if (c != null) c.ShowSpawnerIndicator(false);

            bool any = false;
            foreach (var src in gr.Sources)
            {
                // Hàng đợi đã CẠN → cell đang đứng ở ô gốc là cell CUỐI, sau nó không còn gì nhả ra nữa
                // → thôi vẽ mũi tên spawner cho nó.
                if (src.Queue.Count == 0) continue;
                if (src.Row >= gr.Rows.Count) continue;
                var row = gr.Rows[src.Row];
                if (src.Col >= row.Length || row[src.Col] == null) continue;

                // SpawnerLine: hướng do NGƯỜI DÙNG đặt (src.DirAngle) kể cả trên Rect/Spline auto-hướng — mũi
                // tên phải theo đúng hướng nhả. Còn lại: Rect/Spline auto theo grid, Arc dùng góc riêng.
                float ang = (gr.Data.CellAngleFromShape && !src.CustomDirection)
                    ? gr.Data.DefaultCellAngle(src.Row, src.Col) : src.DirAngle;
                row[src.Col].ShowSpawnerIndicator(true, ang, src.EightWay);
                any = true;
            }

            // Hàng đợi chỉ vơi đi chứ không đầy lại → hết mũi tên là hết hẳn, lần sau khỏi quét.
            gr.HasIndicators = any;
        }

        /// <summary>Ô (row, col) có nhận cell dồn/refill vào được không: phải trong mảng, đang trống, và
        /// KHÔNG phải lỗ designer xoá.</summary>
        private static bool CanEnter(GridRuntime gr, int row, int col, BlockCell[] rowCells)
        {
            if (col < 0 || col >= rowCells.Length || rowCells[col] != null) return false;
            // KHÔNG cho dồn cell vào Ô GỐC spawner: để dành ô đó cho spawner tự nhả queue (nếu để collapse
            // lấp bằng cell dồn từ sau thì FeedRegular thấy ô đầy → spawner thường không bao giờ nhả queue).
            return !IsHole(gr, row, col) && !IsSpawnerCell(gr, row, col);
        }

        private static bool IsHole(GridRuntime gr, int row, int col) =>
            row >= 0 && row < gr.Holes.Count && col >= 0 && col < gr.Holes[row].Length && gr.Holes[row][col];

        // Viết theo ĐÚNG thứ tự ưu tiên khi hoà khoảng cách: Regular < Line < Eight (so bằng "<").
        private enum FillOwner { None, Regular, Line, Eight }

        /// <summary>
        /// Nguồn nào SỞ HỮU ô trống (r,c) khi nhiều spawner cùng ảnh hưởng: nguồn GẦN nhất theo KHOẢNG
        /// CÁCH Ô thắng — Spawner thường đo dọc CỘT nó nhả lên (src.Row − r), SpawnerLine đo số bước dọc
        /// đường nhả (trong Reach), Spawner8 đo Chebyshev (trong Reach). BẰNG khoảng cách → Spawner
        /// thường > SpawnerLine > Spawner8. Nguồn cạn queue không còn ảnh hưởng → nguồn còn lại tiếp
        /// quản ô. Ảnh hưởng bị CẮT khi có lỗ / ô gốc spawner chắn giữa đường (cell không chảy xuyên qua).
        /// </summary>
        private static FillOwner OwnerOf(GridRuntime gr, int r, int c)
        {
            int bestDist = int.MaxValue;
            FillOwner best = FillOwner.None;

            foreach (var src in gr.Sources)
            {
                if (src.Queue.Count == 0) continue;
                int d; FillOwner kind;
                if (src.Line)
                {
                    if (src.StepRow == 0 && src.StepCol == 0) continue;
                    d = LineStepsTo(gr, src, r, c);
                    kind = FillOwner.Line;
                }
                else if (src.EightWay)
                {
                    // One-direction grids only let Spawner8 feed toward the
                    // path: forward, forward-left and forward-right.
                    if (!gr.Data.Collapse2D
                        && (r != src.Row - 1 || Mathf.Abs(c - src.Col) > 1)) continue;
                    d = Mathf.Max(Mathf.Abs(src.Row - r), Mathf.Abs(src.Col - c));
                    if (d < 1 || (src.Reach > 0 && d > src.Reach)) continue;
                    kind = FillOwner.Eight;
                }
                else
                {
                    // Spawner thường: nhả vào ô gốc rồi cell trôi DỌC CỘT về phía path (hàng nhỏ hơn).
                    if (c != src.Col || r >= src.Row) continue;
                    bool blocked = false;
                    for (int rr = r + 1; rr < src.Row && !blocked; rr++)
                        blocked = c >= gr.Rows[rr].Length || IsHole(gr, rr, c) || IsSpawnerCell(gr, rr, c);
                    if (blocked) continue;
                    d = src.Row - r;
                    kind = FillOwner.Regular;
                }
                if (d > 0 && (d < bestDist || (d == bestDist && kind < best))) { bestDist = d; best = kind; }
            }
            return best;
        }

        // Số bước từ nguồn Line tới (r,c) DỌC theo đường nhả của nó (1..maxStep); 0 = không nằm trên
        // đường / ngoài Reach / bị lỗ-ô spawner chắn trước khi tới nơi.
        private static int LineStepsTo(GridRuntime gr, SpawnerSource src, int r, int c)
        {
            int span = gr.Rows.Count;
            foreach (var row in gr.Rows) if (row.Length > span) span = row.Length;
            int maxStep = src.Reach > 0 ? Mathf.Min(src.Reach, span) : span;
            for (int s = 1; s <= maxStep; s++)
            {
                int rr = src.Row + src.StepRow * s, cc = src.Col + src.StepCol * s;
                if (rr < 0 || rr >= gr.Rows.Count || cc < 0 || cc >= gr.Rows[rr].Length) return 0;
                if (rr == r && cc == c) return s;
                if (IsHole(gr, rr, cc) || IsSpawnerCell(gr, rr, cc)) return 0; // băng chuyền không xuyên qua
            }
            return 0;
        }

        // Dồn cấp 1 (trọng lực — cách dồn phục vụ Spawner thường) chỉ được lấp ô KHÔNG thuộc về
        // SpawnerLine/Spawner8 gần hơn: ô của nguồn nào thì nguồn đó tự dồn/nhả theo cách của nó.
        private static bool GravityMayFill(GridRuntime gr, int r, int c)
        {
            var owner = OwnerOf(gr, r, c);
            return owner != FillOwner.Line && owner != FillOwner.Eight;
        }

        // Dồn 1 bước: cell ở hàng r tiến lên ô CHẶN nó ở hàng r-1 nếu ô đó trống — dùng đúng map góc của
        // FrontIndices. Cột thẳng (Rect / Arc-Uniform) → 1:1. Cột lệch (Arc-ArcLength) → cell giữa có 2 ô
        // để dồn lên (vd index 1 của hàng 5 dồn được lên index 0 hoặc 1 của hàng 4); 2 ô ngoài cùng khớp 1:1.
        private bool AdvanceOnce(GridRuntime gr)
        {
            if (ReverseGridDirection(gr)) return AdvanceBackwardOnce(gr);
            bool moved = false;
            for (int r = 1; r < gr.Rows.Count; r++)
            {
                var cur = gr.Rows[r];
                var prev = gr.Rows[r - 1];
                for (int e = 0; e < cur.Length; e++)
                {
                    var cell = cur[e];
                    if (cell == null) continue;
                    if (cell.Indestructible) continue; // nguồn Spawner8 đứng yên, không dồn lên

                    BlockGridData.FrontIndices(cur.Length, prev.Length, e, out int a, out int b);
                    // Lỗ do designer xoá KHÔNG phải chỗ trống để dồn vào — grid 3x3 xoá ô (0,0) thì cột 0
                    // dừng ở hàng 1, không bao giờ lấp kín hàng 0.
                    int slot = -1;
                    // Existing grid cells always get first right to collapse into a
                    // directly adjacent vacancy.  Spawner ownership applies only
                    // when the sources refill after this normal collapse settles.
                    if (CanEnter(gr, r - 1, a, prev)) slot = a;
                    else if (CanEnter(gr, r - 1, b, prev)) slot = b;
                    if (slot < 0) continue; // ô trước còn cell / là lỗ → bị chặn, chưa dồn được

                    prev[slot] = cell;
                    cur[e] = null;
                    cell.SetColumn(slot);
                    // Không gỡ PendingEntry ở đây: transform còn đang trượt. MoveTo tự gỡ khi tới nơi.
                    cell.MoveTo(gr.Data.CellPosAt(r - 1, slot, prev.Length), _collapseDuration);
                    moved = true;
                }
            }
            return moved;
        }

        // Dồn về hàng cuối khi toàn grid bị đảo 180°. Tìm mọi ô hàng sau nhận được từ ô hiện tại,
        // vì Arc/Spline có thể số cell mỗi hàng khác nhau.
        private bool AdvanceBackwardOnce(GridRuntime gr)
        {
            bool moved = false;
            for (int r = gr.Rows.Count - 2; r >= 0; r--)
            {
                var cur = gr.Rows[r]; var next = gr.Rows[r + 1];
                for (int e = 0; e < cur.Length; e++)
                {
                    var cell = cur[e];
                    if (cell == null || cell.Indestructible) continue;
                    int slot = -1;
                    for (int n = 0; n < next.Length; n++)
                    {
                        BlockGridData.FrontIndices(next.Length, cur.Length, n, out int a, out int b);
                        if ((a == e || b == e) && CanEnter(gr, r + 1, n, next)) { slot = n; break; }
                    }
                    if (slot < 0) continue;
                    next[slot] = cell; cur[e] = null; cell.SetColumn(slot);
                    cell.MoveTo(gr.Data.CellPosAt(r + 1, slot, next.Length), _collapseDuration);
                    moved = true;
                }
            }
            return moved;
        }

        // Dồn 2 chiều về GÓC (hàng 0, index 0) với ƯU TIÊN TUYỆT ĐỐI: VÉT CẠN dồn DỌC toàn grid trước, rồi
        // mới NGANG. Trả về ngay sau hướng đầu tiên còn dồn được → guard-loop lặp lại sẽ tiếp tục dọc cho tới
        // hết mới sang ngang. KHÔNG dồn CHÉO — chéo là việc của riêng Spawner8 (nhả 8 hướng trong Reach).
        private bool AdvanceCorner2D(GridRuntime gr)
        {
            int dr = ReverseGridDirection(gr) ? -1 : 1;
            if (AdvanceDir(gr, dr, 0)) return true; // 1. DỌC
            int dc = HorizontalCollapseStep(gr);
            if (AdvanceDir(gr, 0, dc)) return true; // 2. NGANG: ưu tiên hướng mũi tên của cell
            return false;
        }

        private static bool ReverseGridDirection(GridRuntime gr)
        {
            float offset = Mathf.Repeat(gr.Data.CellDirectionOffset, 360f);
            return offset > 90f && offset < 270f;
        }

        // dc=+1: ô bên phải kéo sang trái (hướng mặc định); dc=-1: ô bên trái kéo sang phải.
        private static int HorizontalCollapseStep(GridRuntime gr)
        {
            Vector3 right = Vector3.Cross(Vector3.up, gr.Data.Forward).normalized;
            int score = 0;
            foreach (var row in gr.Rows)
                foreach (var cell in row)
                {
                    if (cell == null || cell.Indestructible) continue;
                    float dot = Vector3.Dot(cell.transform.forward, right);
                    if (dot > 0.5f) score++;
                    else if (dot < -0.5f) score--;
                }
            if (score > 0) return -1; // cell quay phải → dồn sang phải
            if (score < 0) return 1;  // cell quay trái → dồn sang trái
            return 1;
        }

        // 1 lượt kéo lấp theo 1 hướng (dr,dc): mỗi ô trống kéo cell từ ô nguồn (r+dr, e+dc) vào.
        private bool AdvanceDir(GridRuntime gr, int dr, int dc)
        {
            bool moved = false;
            for (int r = 0; r < gr.Rows.Count; r++)          // từ hàng 0 (góc) ra
            {
                var row = gr.Rows[r];
                for (int e = 0; e < row.Length; e++)          // từ index 0 (góc) ra
                {
                    if (row[e] != null) continue;             // đã có cell
                    if (IsHole(gr, r, e) || IsSpawnerCell(gr, r, e)) continue; // lỗ/spawner: không lấp
                    if (!GravityMayFill(gr, r, e)) continue;  // ô thuộc SpawnerLine/Spawner8 gần hơn
                    if (TryPull(gr, r, e, r + dr, e + dc)) moved = true;
                }
            }
            return moved;
        }

        // Kéo cell từ ô nguồn (sr,sc) vào ô trống (r,e) nếu nguồn hợp lệ (trong grid, có cell di chuyển
        // được, không phải nguồn bất tử). true = đã kéo. CHÚ Ý: không chặn theo IsSpawnerCell — cell
        // THƯỜNG đang đứng ở ô gốc Spawner thường (vừa nhả ra) PHẢI kéo đi được, không thì ô gốc không
        // bao giờ trống → FeedRegular không bao giờ nhả queue → spawner thường tắc vĩnh viễn trên grid
        // Collapse2D. Nguồn tĩnh (Spawner8/SpawnerLine) đã có Indestructible giữ chân.
        private bool TryPull(GridRuntime gr, int r, int e, int sr, int sc)
        {
            if (sr < 0 || sr >= gr.Rows.Count) return false;
            var srow = gr.Rows[sr];
            if (sc < 0 || sc >= srow.Length) return false;
            var cell = srow[sc];
            if (cell == null || cell.Indestructible) return false;
            srow[sc] = null;
            gr.Rows[r][e] = cell;
            cell.SetColumn(e);
            cell.MoveTo(gr.Data.CellPosAt(r, e, gr.Rows[r].Length), _collapseDuration);
            return true;
        }

        // Dồn LAN TỎA RA NGOÀI từ Spawner8: mỗi ô trống (không phải lỗ / ô spawner) được lấp bằng cell ở
        // 8-neighbor GẦN spawner hơn nó — cell đó dịch RA lấp chỗ, để lại chỗ trống gần spawner hơn, cứ thế
        // lan vào. Ô sát spawner (không còn neighbor nào gần hơn) do FeedSources nhả bù. Lặp qua guard-loop
        // của CollapseFront cho tới khi kín. Khoảng cách = Chebyshev (8 hướng) tới Spawner8 GẦN nhất.
        private bool CascadeTowardSpawners(GridRuntime gr)
        {
            bool anySpawner = false;
            foreach (var s in gr.Sources) if (s.EightWay) { anySpawner = true; break; }
            if (!anySpawner) return false;

            bool moved = false;
            for (int r = 0; r < gr.Rows.Count; r++)
            {
                var row = gr.Rows[r];
                for (int c = 0; c < row.Length; c++)
                {
                    if (row[c] != null) continue;                             // đã có cell
                    if (IsHole(gr, r, c) || IsSpawnerCell(gr, r, c)) continue; // lỗ / ô spawner: không lấp
                    int dE = DistToNearestSpawner(gr, r, c);
                    if (dE <= 0) continue;
                    if (!WithinSpawnerReach(gr, r, c)) continue;              // ngoài Reach của mọi Spawner8
                    // Ô thuộc Spawner thường / SpawnerLine gần hơn → nguồn đó tự lấp, lan tỏa không đụng.
                    if (OwnerOf(gr, r, c) != FillOwner.Eight) continue;

                    // Chọn neighbor có cell di chuyển được và GẦN spawner hơn ô này (ưu tiên dọc/ngang trước
                    // chéo nhờ thứ tự EightNeighbors + so sánh chặt "<").
                    int bestD = dE, br = -1, bc = -1;
                    for (int k = 0; k < EightNeighbors.GetLength(0); k++)
                    {
                        int nr = r + EightNeighbors[k, 0];
                        if (nr < 0 || nr >= gr.Rows.Count) continue;
                        int nc = NeighborCol(gr, c + EightNeighbors[k, 1], gr.Rows[nr].Length);
                        if (nc < 0 || nc >= gr.Rows[nr].Length) continue;
                        var ncell = gr.Rows[nr][nc];
                        if (ncell == null || ncell.Indestructible) continue;   // trống / là spawner: không kéo
                        if (IsSpawnerCell(gr, nr, nc)) continue;
                        int dN = DistToNearestSpawner(gr, nr, nc);
                        if (dN >= 0 && dN < bestD) { bestD = dN; br = nr; bc = nc; }
                    }
                    if (br < 0) continue;

                    var cell = gr.Rows[br][bc];
                    gr.Rows[br][bc] = null;
                    row[c] = cell;
                    cell.SetColumn(c);
                    cell.MoveTo(gr.Data.CellPosAt(r, c, row.Length), _collapseDuration);
                    moved = true;
                }
            }
            return moved;
        }

        // Khoảng cách Chebyshev (8 hướng) tới ô gốc Spawner8 ĐANG hoạt động gần nhất; -1 nếu không còn cái nào.
        private static int DistToNearestSpawner(GridRuntime gr, int r, int c)
        {
            int best = int.MaxValue;
            foreach (var src in gr.Sources)
            {
                if (!src.EightWay) continue;
                int d = Mathf.Max(Mathf.Abs(src.Row - r), Mathf.Abs(src.Col - c));
                if (d < best) best = d;
            }
            return best == int.MaxValue ? -1 : best;
        }

        // Ô (r,c) có nằm trong Reach của MỘT Spawner8 đang hoạt động không (Reach ≤ 0 = vô hạn, tới mép).
        private static bool WithinSpawnerReach(GridRuntime gr, int r, int c)
        {
            foreach (var src in gr.Sources)
            {
                if (!src.EightWay) continue;
                int d = Mathf.Max(Mathf.Abs(src.Row - r), Mathf.Abs(src.Col - c));
                if (src.Reach <= 0 || d <= src.Reach) return true;
            }
            return false;
        }

        // Spawner THƯỜNG (1 hướng): ô gốc trống (cell cũ vừa dồn lên) thì nhả mục kế ra đúng ô đó.
        private bool FeedRegular(GridRuntime gr)
        {
            if (gr.Sources.Count == 0 || gr.Rows.Count == 0) return false;
            bool fed = false;
            for (int i = gr.Sources.Count - 1; i >= 0; i--)
            {
                var src = gr.Sources[i];
                if (src.EightWay || src.Line) continue;
                if (src.Row >= gr.Rows.Count || src.Col >= gr.Rows[src.Row].Length || src.Queue.Count == 0)
                { ReleaseExhaustedSpawnerCell(gr, src); gr.Sources.RemoveAt(i); continue; }
                int len = gr.Rows[src.Row].Length;
                if (gr.Rows[src.Row][src.Col] == null)
                {
                    bool spawned = SpawnFromQueue(gr, src.Queue, src.Row, src.Col,
                                                   gr.Data.CellPosAt(src.Row, src.Col, len), src.DirAngle);
                    fed |= spawned;
                    // The last emitted block is normal, so release the source
                    // marker in this same collapse pass rather than leaving a gap.
                    if (src.Queue.Count == 0)
                    {
                        ReleaseExhaustedSpawnerCell(gr, src);
                        gr.Sources.RemoveAt(i);
                    }
                }
            }
            return fed;
        }

        // Nhả của Spawner8 (nhả kề 8 ô, nội bộ ưu tiên dọc → ngang → chéo). KHÔNG gồm lan tỏa cascade.
        private bool FeedEightWayAll(GridRuntime gr)
        {
            if (gr.Sources.Count == 0 || gr.Rows.Count == 0) return false;
            bool fed = false;
            fed |= FeedEightWay(gr, tier: 0);
            fed |= FeedEightWay(gr, tier: 1);
            fed |= FeedEightWay(gr, tier: 2);
            return fed;
        }

        // SpawnerLine dồn TUẦN TỰ (giống Spawner8/spawner thường, KHÔNG teleport màu từ queue ra ô xa):
        // cell trên đường (StepRow,StepCol) dịch RA XA 1 bước lấp ô trống gần mép, và ô GẦN NHẤT sát nguồn
        // được nhả bù từ queue (màu hiện tại trước). Quét từ XA về GẦN nên mỗi cell chỉ nhích 1 ô/lượt →
        // qua guard-loop thành 1 băng chuyền chạy dần. Tối đa Reach ô (0 = tới mép grid).
        // verticalPass: true = nguồn DỌC (StepCol==0); false = NGANG/chéo (StepCol!=0).
        // A line source also participates in the grid's normal forward collapse.
        // Its configured line may be diagonal, but when the directly-front cell
        // opens, the source must immediately emit into that gap rather than leave
        // it empty waiting for a diagonal line position to open.
        private bool FeedLineFrontGaps(GridRuntime gr)
        {
            bool fed = false;
            for (int i = gr.Sources.Count - 1; i >= 0; i--)
            {
                var src = gr.Sources[i];
                if (!src.Line) continue;
                if (src.Row < 0 || src.Row >= gr.Rows.Count || src.Col < 0
                    || src.Col >= gr.Rows[src.Row].Length || src.Queue.Count == 0)
                { RemoveStaticSource(gr, i); continue; }

                int frontRow = src.Row - 1;
                if (frontRow < 0 || src.Col >= gr.Rows[frontRow].Length) continue;
                if (!CanEnter(gr, frontRow, src.Col, gr.Rows[frontRow])) continue;

                Vector3 origin = gr.Data.CellPosAt(src.Row, src.Col, gr.Rows[src.Row].Length);
                if (EmitHead(gr, src, frontRow, src.Col, origin))
                {
                    fed = true;
                    UpdateStaticSourceDisplay(gr, src);
                    if (src.Queue.Count == 0) RemoveStaticSource(gr, i);
                }
            }
            return fed;
        }

        private bool FeedLine(GridRuntime gr, bool verticalPass)
        {
            bool changed = false;
            for (int i = gr.Sources.Count - 1; i >= 0; i--)
            {
                var src = gr.Sources[i];
                if (!src.Line) continue;
                if (src.Row >= gr.Rows.Count || src.Col >= gr.Rows[src.Row].Length || src.Queue.Count == 0)
                { RemoveStaticSource(gr, i); continue; }
                if (src.StepRow == 0 && src.StepCol == 0) continue;
                bool vertical = src.StepCol == 0;
                if (vertical != verticalPass) continue;

                int span = gr.Rows.Count;
                foreach (var row in gr.Rows) if (row.Length > span) span = row.Length;
                int maxStep = src.Reach > 0 ? Mathf.Min(src.Reach, span) : span;
                Vector3 origin = gr.Data.CellPosAt(src.Row, src.Col, gr.Rows[src.Row].Length);

                // XA → GẦN: mỗi ô trống kéo cell từ ô KỀ phía trong (gần nguồn hơn 1 bước) ra lấp; ô bước 1
                // (sát nguồn) thì nguồn nhả bù từ queue.
                for (int step = maxStep; step >= 1; step--)
                {
                    int r = src.Row + src.StepRow * step;
                    int c = src.Col + src.StepCol * step;
                    if (r < 0 || r >= gr.Rows.Count || c < 0 || c >= gr.Rows[r].Length) continue; // ngoài grid
                    if (IsHole(gr, r, c) || IsSpawnerCell(gr, r, c)) continue; // lỗ/ô spawner: bỏ
                    if (gr.Rows[r][c] != null) continue;                        // đã có cell
                    // Ô này thuộc về nguồn khác GẦN hơn (vd Spawner thường cùng khoảng cách trở xuống)
                    // → để nguồn đó lấp theo cách của nó, line không đụng vào.
                    if (OwnerOf(gr, r, c) != FillOwner.Line) continue;

                    if (step == 1)
                    {
                        if (src.Queue.Count > 0 && EmitHead(gr, src, r, c, origin))
                        { changed = true; UpdateStaticSourceDisplay(gr, src); }
                        continue;
                    }
                    // Kéo cell từ ô trong hơn 1 bước (step-1) ra.
                    int pr = src.Row + src.StepRow * (step - 1);
                    int pc = src.Col + src.StepCol * (step - 1);
                    if (pr < 0 || pr >= gr.Rows.Count || pc < 0 || pc >= gr.Rows[pr].Length) continue;
                    var pcell = gr.Rows[pr][pc];
                    if (pcell == null || pcell.Indestructible || IsSpawnerCell(gr, pr, pc)) continue;
                    gr.Rows[pr][pc] = null;
                    gr.Rows[r][c] = pcell;
                    pcell.SetColumn(c);
                    pcell.MoveTo(gr.Data.CellPosAt(r, c, gr.Rows[r].Length), _collapseDuration);
                    changed = true;
                }
                if (src.Queue.Count == 0) RemoveStaticSource(gr, i);
            }
            return changed;
        }

        // Đổi góc hướng nhả (độ quanh Y) thành bước đi 1 ô trên lưới theo trục grid: +Forward = hàng cao
        // hơn, +Right(Cross(up,Forward)) = index cao hơn. Ngưỡng ~cos70° để dọc/ngang ra 1 trục, chéo ra 2.
        private static void LineStep(BlockGridData grid, float dirAngle, out int stepRow, out int stepCol)
        {
            Vector3 f = grid.Forward; f.y = 0f;
            f = f.sqrMagnitude < 1e-6f ? Vector3.forward : f.normalized;
            Vector3 rt = Vector3.Cross(Vector3.up, f);
            Vector3 dir = Quaternion.Euler(0f, dirAngle, 0f) * Vector3.forward;
            float fc = Vector3.Dot(dir, f), rc = Vector3.Dot(dir, rt);
            stepRow = Mathf.Abs(fc) > 0.35f ? (fc > 0f ? 1 : -1) : 0;
            stepCol = Mathf.Abs(rc) > 0.35f ? (rc > 0f ? 1 : -1) : 0;
            if (stepRow == 0 && stepCol == 0) stepRow = -1; // fallback: về hàng 0
        }

        // 1 lượt nhả của mọi Spawner8 theo tier hướng: 0 = dọc (dr≠0,dc=0), 1 = ngang (dr=0,dc≠0), 2 = chéo.
        private bool FeedEightWay(GridRuntime gr, int tier)
        {
            bool fed = false;
            for (int i = gr.Sources.Count - 1; i >= 0; i--)
            {
                var src = gr.Sources[i];
                if (!src.EightWay) continue;
                if (src.Row >= gr.Rows.Count || src.Col >= gr.Rows[src.Row].Length || src.Queue.Count == 0)
                { RemoveStaticSource(gr, i); continue; }

                int len = gr.Rows[src.Row].Length;
                Vector3 origin = gr.Data.CellPosAt(src.Row, src.Col, len);
                for (int k = 0; k < EightNeighbors.GetLength(0) && src.Queue.Count > 0; k++)
                {
                    int dr = EightNeighbors[k, 0], dc = EightNeighbors[k, 1];
                    if ((src.EightDirections & Spawner8DirectionFor(k)) == 0) continue;
                    // A one-direction grid never expands a Spawner8 sideways or
                    // backwards: only its three forward neighbours are valid.
                    if (!gr.Data.Collapse2D && dr != -1) continue;
                    int t = dr != 0 && dc != 0 ? 2 : dc == 0 ? 0 : 1; // dọc / ngang / chéo
                    if (t != tier) continue;                          // lượt này chỉ 1 tier hướng

                    int r = src.Row + dr;
                    if (r < 0 || r >= gr.Rows.Count) continue;
                    int c = NeighborCol(gr, src.Col + dc, gr.Rows[r].Length);
                    if (IsSpawnerCell(gr, r, c)) continue;           // chừa ô của spawner khác (yêu cầu)
                    if (!CanEnter(gr, r, c, gr.Rows[r])) continue;
                    // Ô kề này thuộc nguồn khác gần hơn/ưu tiên hơn (Spawner thường, SpawnerLine) → chừa.
                    if (OwnerOf(gr, r, c) != FillOwner.Eight) continue;

                    // Nhả MÀU HIỆN TẠI ra ô kề (cell mới trượt từ ô gốc ra), rồi ô gốc chuyển sang màu kế.
                    if (EmitHead(gr, src, r, c, origin)) { fed = true; UpdateStaticSourceDisplay(gr, src); }
                }
                if (src.Queue.Count == 0) RemoveStaticSource(gr, i);
            }
            return fed;
        }

        private static Spawner8Directions Spawner8DirectionFor(int neighborIndex)
        {
            switch (neighborIndex)
            {
                case 0: return Spawner8Directions.Front;
                case 1: return Spawner8Directions.Back;
                case 2: return Spawner8Directions.Left;
                case 3: return Spawner8Directions.Right;
                case 4: return Spawner8Directions.FrontLeft;
                case 5: return Spawner8Directions.FrontRight;
                case 6: return Spawner8Directions.BackLeft;
                default: return Spawner8Directions.BackRight;
            }
        }

        // Nhả màu ĐẦU Queue (màu ô gốc đang hiển thị) ra ô (r,c): dựng cell ở ô gốc rồi trượt sang, và
        // DEQUEUE đầu. Bỏ qua mục rác (stack ≤ 0). false = Queue không còn mục hợp lệ.
        private bool EmitHead(GridRuntime gr, SpawnerSource src, int r, int c, Vector3 spawnPos)
        {
            PendingBlockData head = null;
            while (src.Queue.Count > 0 && head == null)
            {
                head = src.Queue.Peek();
                if (head == null || head.BlockStackCt <= 0) { src.Queue.Dequeue(); head = null; }
            }
            if (head == null) return false;

            var data = new BlockCellData
            {
                Color = head.Color,
                BlockStackCt = head.BlockStackCt,
                BlockCol = c,
                SpawnerDepth = r,
                SpawnerDirectionAngleZ = gr.Data.DefaultCellAngle(r, c) - gr.Data.CellDirectionOffset,
            };
            var row = gr.Rows[r];
            var cell = CreateCell(gr, $"Cell_spawn_r{r}_e{c}", spawnPos, data);
            cell.MoveTo(gr.Data.CellPosAt(r, c, row.Length), _collapseDuration); // tự khoá ngắm khi trượt
            row[c] = cell;
            src.Queue.Dequeue(); // đã nhả đầu ra ngoài
            return true;
        }

        // Sau khi nhả đầu Queue: ô gốc chuyển sang hiển thị màu kế (đầu Queue mới), hoặc despawn nếu đã cạn.
        private void UpdateStaticSourceDisplay(GridRuntime gr, SpawnerSource src)
        {
            var cell = gr.Rows[src.Row][src.Col];
            if (src.Queue.Count == 0)
            {
                if (cell != null) { cell.Despawn(); gr.Rows[src.Row][src.Col] = null; }
                ReleaseExhaustedSpawnerCell(gr, src);
                return;
            }
            if (cell == null) return;
            var head = src.Queue.Peek();
            var data = new BlockCellData
            {
                Color = head.Color,
                BlockStackCt = head.BlockStackCt,
                BlockCol = src.Col,
                SpawnerDepth = src.Row,
                SpawnerDirectionAngleZ = src.DirAngle,
                Type = src.Line ? BlockCellType.SpawnerLine : BlockCellType.Spawner8, // giữ nguồn bất tử
            };
            cell.Build(data, gr.Data.EffectiveStackSpacing, gr.Data.CellScale, this);
            cell.SetMultiSide(gr.Data.ShootableEdges != GridEdges.None);
        }

        // Gỡ 1 nguồn tĩnh đã cạn: despawn ô gốc bất tử nếu còn (thành ô trống — vẫn là SpawnerCell nên
        // spawner khác không lấp vào).
        private void RemoveStaticSource(GridRuntime gr, int i)
        {
            var src = gr.Sources[i];
            if (src.Row < gr.Rows.Count && src.Col < gr.Rows[src.Row].Length)
            {
                var cell = gr.Rows[src.Row][src.Col];
                if (cell != null && cell.Indestructible) { cell.Despawn(); gr.Rows[src.Row][src.Col] = null; }
            }
            ReleaseExhaustedSpawnerCell(gr, src);
            gr.Sources.RemoveAt(i);
        }

        // Once a source is empty, an interior source cell becomes a normal grid
        // cell.  Keeping the marker would make CanEnter reject every collapse
        // into that position forever.  The final row remains reserved because no
        // normal cell exists behind it to feed the vacancy.
        private static void ReleaseExhaustedSpawnerCell(GridRuntime gr, SpawnerSource src)
        {
            if (src.Row < 0 || src.Row >= gr.Rows.Count - 1 || src.Col < 0
                || src.Col >= gr.SpawnerCells[src.Row].Length) return;
            gr.SpawnerCells[src.Row][src.Col] = false;
        }

        // Cột kề của Spawner8: vòng KÍN thì nối vòng (cột cuối kề cột 0); vòng hở thì tràn mép = không có ô.
        private static int NeighborCol(GridRuntime gr, int col, int len) =>
            gr.Data.IsFullRing && len > 0 ? ((col % len) + len) % len : col;

        // Ô (row, col) có phải ô gốc của 1 Spawner8 không (giữ cố định kể cả khi spawner đó đã hết queue).
        private static bool IsSpawnerCell(GridRuntime gr, int row, int col) =>
            row >= 0 && row < gr.SpawnerCells.Count && col >= 0 && col < gr.SpawnerCells[row].Length
            && gr.SpawnerCells[row][col];

        // Ô (row, col) có BẮN được không (theo VỊ TRÍ). Ngoài phạm vi = bắn được.
        private static bool IsPositionShootable(GridRuntime gr, int row, int col) =>
            row < 0 || row >= gr.Shootable.Count || col < 0 || col >= gr.Shootable[row].Length
            || gr.Shootable[row][col];

        /// <summary>
        /// Nhả mục hợp lệ kế tiếp trong <paramref name="queue"/> ra ô (row, col): dựng cell ở
        /// <paramref name="spawnPos"/> rồi trượt về đúng ô. false = hàng đợi không còn mục hợp lệ.
        /// Dùng cho Spawner THƯỜNG (1 hướng).
        /// </summary>
        private bool SpawnFromQueue(GridRuntime gr, Queue<PendingBlockData> queue, int row, int col,
                                    Vector3 spawnPos, float dirAngle)
        {
            PendingBlockData p = null;
            while (queue.Count > 0 && p == null)
            {
                p = queue.Dequeue();
                if (p == null || p.BlockStackCt <= 0) p = null; // mục rác trong data → bỏ, lấy mục kế
            }
            if (p == null) return false;

            var data = new BlockCellData
            {
                Color = p.Color,
                BlockStackCt = p.BlockStackCt,
                BlockCol = col,
                SpawnerDepth = row,
                SpawnerDirectionAngleZ = dirAngle - gr.Data.CellDirectionOffset,
            };
            var row_ = gr.Rows[row];
            var cell = CreateCell(gr, $"Cell_spawn_r{row}_e{col}", spawnPos, data);
            cell.MoveTo(gr.Data.CellPosAt(row, col, row_.Length), _collapseDuration); // tự khoá ngắm khi trượt
            row_[col] = cell;
            return true;
        }

        // Refill mức GRID (hàng đợi chung của grid): nhả vào các ô trống ở hàng SÂU NHẤT, rồi để AdvanceOnce
        // đẩy dần lên. Số hàng giữ nguyên nên không dựng thêm hàng như trước.
        private bool TryRefill(GridRuntime gr)
        {
            if (gr.Pending == null || gr.Pending.Count == 0 || gr.Rows.Count == 0) return false;

            int rowIndex = gr.Rows.Count - 1;
            var row = gr.Rows[rowIndex];
            int count = row.Length;
            bool fed = false;

            for (int e = 0; e < count && gr.Pending.Count > 0; e++)
            {
                if (row[e] != null || IsHole(gr, rowIndex, e)) continue; // lỗ designer xoá → không lấp
                if (!GravityMayFill(gr, rowIndex, e)) continue; // ô thuộc SpawnerLine/Spawner8: nguồn đó lấp
                var p = gr.Pending.Dequeue();
                if (p == null || p.BlockStackCt <= 0) continue;

                Vector3 pos = gr.Data.CellPosAt(rowIndex, e, count);
                // Xuất phát từ ĐÚNG vị trí hàng sâu hơn 1 bậc rồi trượt vào — hợp cả Arc lẫn Rect.
                Vector3 spawnPos = gr.Data.CellPosAt(rowIndex + 1, e, count);

                var data = new BlockCellData
                {
                    Color = p.Color,
                    BlockStackCt = p.BlockStackCt,
                    BlockCol = e,
                    SpawnerDepth = rowIndex,
                    SpawnerDirectionAngleZ = gr.Data.DefaultCellAngle(rowIndex, e) - gr.Data.CellDirectionOffset,
                };

                row[e] = CreateCell(gr, $"Cell_refill_r{rowIndex}_e{e}", spawnPos, data);
                row[e].MoveTo(pos, _collapseDuration); // MoveTo tự khoá ngắm tới khi cell trượt xong
                fed = true;
            }
            return fed;
        }

        public int RemainingBlocks
        {
            get
            {
                int s = 0;
                foreach (var gr in _grids)
                {
                    // Ô gốc Spawner8 (Indestructible) KHÔNG cộng ở đây: block nó đang hiển thị chính là ĐẦU
                    // Queue của nguồn, đã cộng ở vòng dưới → cộng cả 2 là đếm gấp đôi.
                    foreach (var row in gr.Rows)
                        foreach (var cell in row)
                            // Bắn được là theo VỊ TRÍ: cell ở ô không bắn được vẫn dồn sang ô bắn được để phá →
                            // VẪN tính vào điều kiện thắng (chỉ ô gốc spawner bất tử mới loại).
                            if (cell != null && !cell.Indestructible) s += cell.StackCount;
                    // Block chưa nhả ra: Spawner thường = các mục refill sau ô gốc; Spawner8 = cả sequence
                    // (gồm màu ô gốc đang hiển thị). Đều là block "chưa clear".
                    foreach (var src in gr.Sources)
                        foreach (var p in src.Queue)
                            if (p != null && p.BlockStackCt > 0) s += p.BlockStackCt;
                    if (gr.Pending != null)
                        foreach (var p in gr.Pending)
                            if (p != null && p.BlockStackCt > 0) s += p.BlockStackCt;
                }
                return s;
            }
        }

        public bool AllCleared => _everHadBlocks && RemainingBlocks == 0;

        // Băng tan cho mọi cell có ngưỡng ≤ tổng block đã phá (destroyed). Gọi từ GameController.OnBoardChanged.
        public void UpdateIce(int destroyed)
        {
            foreach (var gr in _grids)
                foreach (var row in gr.Rows)
                    foreach (var cell in row)
                        if (cell != null && cell.Frozen && cell.IceThreshold <= destroyed) cell.Melt();
        }
    }
}

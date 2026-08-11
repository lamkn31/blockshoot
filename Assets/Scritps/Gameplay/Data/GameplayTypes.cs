using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wayfu.Lamkn
{
    /// <summary>Độ khó của level (~ GameDifficulty của PixelShoot_2).</summary>
    public enum GameDifficulty { Easy, Normal, Hard, VeryHard, Expert }

    public static class GameDifficultyExt
    {
        /// <summary>
        /// Đổi <see cref="GameDifficulty"/> (5 mức, dùng trong data level) sang LevelDifficulty (3 mức,
        /// dùng cho UI).
        /// <para>BẮT BUỘC đi qua đây trước khi đưa vào popup: GamePlayPopup có đúng 3 slot
        /// difficultyObjects/settingObjects, truyền thẳng GameDifficulty thì VeryHard=3 / Expert=4 vượt
        /// mảng → ApplyDifficulty tắt sạch, mất luôn cả icon Setting.</para>
        /// </summary>
        public static LevelDifficulty ToLevelDifficulty(this GameDifficulty d)
        {
            switch (d)
            {
                case GameDifficulty.Hard: return LevelDifficulty.Hard;
                case GameDifficulty.VeryHard: return LevelDifficulty.VeryHard;
                case GameDifficulty.Expert: return LevelDifficulty.VeryHard;
                default: return LevelDifficulty.Easy; // Easy, Normal
            }
        }
    }

    /// <summary>Loại obstacle gắn lên block/cell (~ BlockObstacleType).</summary>
    public enum BlockObstacleType { None, Crate, Lock, Ice, Mystery, Barricade }

    /// <summary>
    /// Cách nòng nhả đạn vào cell đã chốt.
    /// <para><b>Single</b>: mỗi nhịp FireInterval nhả 1 viên, gặm dần cho tới khi cell sạch.</para>
    /// <para><b>BurstPerCell</b>: chốt cell xong nhả LUÔN 1 loạt đúng bằng số block còn lại của cell,
    /// mỗi viên bay tới 1 block trong stack → cả cell vỡ trong 1 lượt.</para>
    /// <para><b>Laser</b>: KHÔNG bắn viên đạn — vẽ 1 TIA liền mạch từ nòng (left/right) tới cell đang
    /// bám, gặm block liên tục theo FireInterval. Cell vỡ xong tia NỐI LIỀN sang cell kế trong tầm nên
    /// nhìn không ngắt quãng (xem Gun.UpdateBeam / LaserHit).</para>
    /// </summary>
    public enum GunFireMode { Single, BurstPerCell, Laser }

    /// <summary>
    /// Bó config bắn của gun: đọc 1 lần từ <see cref="GameSettings"/> rồi truyền xuống slot → gun.
    /// Gom lại thay vì truyền rời từng tham số — danh sách đã tới 7 cái với 3 float liền nhau
    /// (Range/Angle/BulletSpeed), hoán nhầm 2 cái thì compile vẫn trót lọt mà gameplay sai âm thầm.
    /// </summary>
    [Serializable]
    public struct GunFireConfig
    {
        public float Interval;
        public float Range;
        public float Angle;
        public float BulletSpeed;
        public GunFireMode Mode;
        public bool BurstSpawnStacked;
        public float BurstRowLead;

        /// <summary>Giá trị mặc định dùng khi chưa có asset GameSettings.</summary>
        public static GunFireConfig FromSettings(GameSettings gs) => new GunFireConfig
        {
            Interval = gs != null ? gs.FireInterval : 0.25f,
            Range = gs != null ? gs.GunFireRange : 3f,
            Angle = gs != null ? gs.GunFireAngle : 360f,
            BulletSpeed = gs != null ? gs.BulletSpeed : 14f,
            Mode = gs != null ? gs.FireMode : GunFireMode.Single,
            BurstSpawnStacked = gs != null && gs.BurstSpawnStacked,
            BurstRowLead = gs != null ? gs.BurstRowLead : 0f,
        };
    }

    /// <summary>
    /// Cách dựng đường cong của path từ danh sách waypoint.
    /// <para><b>RoundedCorner</b>: nối thẳng waypoint, chỉ BO tròn tại góc theo CornerRadius → đa số là
    /// đoạn thẳng.</para>
    /// <para><b>Bezier</b>: Bezier bậc 3 từng đoạn, control point suy từ 2 waypoint kề (Catmull-Rom) →
    /// đường cong MƯỢT toàn phần, vẫn ĐI QUA đúng mọi waypoint. CornerRadius không dùng.</para>
    /// </summary>
    public enum PathStyle { RoundedCorner, Bezier }

    /// <summary>
    /// Kiểu cell.
    /// <para><b>Normal</b>: phá hết stack là cell biến mất.</para>
    /// <para><b>Spawner</b>: có hàng đợi cell PHÍA SAU. Phá hết stack hiện tại → đẩy cell kế trong hàng đợi
    /// ra ĐÚNG vị trí đó (đổi màu/stack theo mục kế); hết hàng đợi mới thật sự biến mất.</para>
    /// <para><b>Spawner8</b>: như Spawner ở ô gốc, NHƯNG còn nhả thêm vào ô TRỐNG ở 8 ô xung quanh
    /// (4 dọc/ngang + 4 chéo) — không chỉ dồn theo 1 hướng cột như Spawner thường. Hợp với grid cột
    /// thẳng (Rect / Arc-Uniform); grid ArcLength hàng lệch số cell thì ô kề theo index có thể xiên.</para>
    /// </summary>
    /// <summary>
    /// <para><b>SpawnerLine</b>: nguồn nhả theo 1 ĐƯỜNG THẲNG (dọc/ngang/chéo theo hướng
    /// SpawnerDirectionAngleZ), lấp các ô trống trên đường đó tới tối đa <see cref="BlockCellData.SpawnerReach"/>
    /// ô. Bất tử, đứng yên như Spawner8; nhả màu hiện tại trước rồi tới queue (conveyor).</para>
    /// </summary>
    public enum BlockCellType { Normal, Spawner, Spawner8, SpawnerLine }

    [Flags]
    public enum Spawner8Directions
    {
        None = 0, Front = 1, Back = 2, Left = 4, Right = 8,
        FrontLeft = 16, FrontRight = 32, BackLeft = 64, BackRight = 128,
        All = Front | Back | Left | Right | FrontLeft | FrontRight | BackLeft | BackRight
    }

    public static class BlockCellTypeExt
    {
        /// <summary>Cell có hàng đợi nhả thêm không (gom mọi loại spawner) — mọi chỗ xử lý "cell có Queue"
        /// (build nguồn, cân bằng bullet↔block, vẽ ghost editor) đi qua đây để thêm loại spawner mới không
        /// phải sửa rải rác.</summary>
        public static bool IsSpawner(this BlockCellType t) =>
            t == BlockCellType.Spawner || t == BlockCellType.Spawner8 || t == BlockCellType.SpawnerLine;

        /// <summary>Nguồn BẤT TỬ + đứng yên (conveyor nhả ra ô khác): Spawner8 và SpawnerLine. Spawner
        /// thường thì KHÔNG — ô gốc là cell thường, tự dồn lên.</summary>
        public static bool IsStaticSource(this BlockCellType t) =>
            t == BlockCellType.Spawner8 || t == BlockCellType.SpawnerLine;
    }

    /// <summary>
    /// Cách chia số cell mỗi hàng của grid.
    /// <para><b>ArcLength</b>: số cell = chiều dài cung / (BlockWidth+Spacing) → hàng ra xa có NHIỀU cell hơn
    /// (4/5/6...). Cột không thẳng: 1 cell hàng sau bị chặn bởi TỐI ĐA 2 cell hàng trước (theo góc).</para>
    /// <para><b>Uniform</b>: mọi hàng CÙNG số cell (lấy theo hàng chật nhất = row 0 để không chồng cell)
    /// → cùng index = cùng góc = 1 cột xuyên tâm; chặn 1:1.</para>
    /// </summary>
    public enum BlockGridLayout { ArcLength, Uniform }

    /// <summary>
    /// Hình dạng grid block.
    /// <para><b>Arc</b>: vòng cung (fan) quanh Center — hàng = cung bán kính BaseRadius + row*RowSpacing.</para>
    /// <para><b>Rect</b>: lưới CHỮ NHẬT thông thường — hàng thẳng dọc trục X, sâu dần theo +Z;
    /// mọi hàng có đúng <see cref="BlockGridData.Columns"/> cell nên cột luôn thẳng (chặn 1:1).</para>
    /// <para><b>Spline</b>: grid UỐN LƯỢN bám theo 1 đường tự vẽ (SplineWaypoints) — hàng 0 chạy dọc
    /// đường đó, hàng sau lệch dần theo PHÁP TUYẾN. Dùng cho level dạng dải băng ngoằn ngoèo mà Arc
    /// (chỉ cong quanh 1 tâm) và Rect (chỉ thẳng) không tả được.</para>
    /// </summary>
    public enum BlockGridShape { Arc, Rect, Spline }

    /// <summary>
    /// Bên của grid so với path — gán CỨNG lúc thiết kế. Left/Right = CHỈ nòng cùng bên của gun bắn được
    /// grid này (bỏ qua kiểm tra sườn theo hình học vốn lật dấu khi path cong / grid nằm chếch).
    /// Any = để gun tự quyết theo quạt (2 nòng, hành vi cũ).
    /// </summary>
    public enum GridSide { Any, Left, Right }

    /// <summary>
    /// Kiểu ưu tiên chọn target của gun.
    /// <para><b>NearestCell</b>: luôn bắn cell GẦN gun nhất trong số cell bắn được — kể cả cell vừa sập
    /// xuống (hàng sâu tiến ra) đang ở gần.</para>
    /// <para><b>FrontRowFirst</b>: ưu tiên cell ở HÀNG 0 (sát path, chưa bị bắn) hơn cell đã sập xuống,
    /// dù cell sập ở gần hơn. Chỉ khi hết cell hàng 0 trong range mới ăn tới cell hàng sâu.</para>
    /// </summary>
    public enum CoreGameType { NearestCell, FrontRowFirst }

    /// <summary>
    /// Các CẠNH của grid mà gun bắn được — cho grid bị path bao quanh nhiều mặt.
    /// <para><b>None</b> (mặc định, giữ hành vi cũ): chỉ hàng 0 (mặt Front, sát path) bắn được, ăn dần
    /// vào trong; các hàng sâu bị hàng trước che.</para>
    /// <para>Bật cạnh nào thì cell lộ ra ở cạnh đó bắn được: <b>Front</b> = phía hàng 0, <b>Back</b> =
    /// phía hàng cuối, <b>Left</b> = đầu index 0 mỗi hàng, <b>Right</b> = cuối index mỗi hàng. Cell chỉ
    /// bắn được khi mọi ô giữa nó và 1 cạnh ĐANG BẬT đã trống → peel dần từ ngoài vào. Spawner8 ở giữa
    /// không bao giờ bị ngắm (xem <see cref="BlockCell"/>.Indestructible).</para>
    /// </summary>
    [Flags]
    public enum GridEdges { None = 0, Front = 1, Back = 2, Left = 4, Right = 8 }

    public enum GridDefaultDirection { Auto = 0, Up = 1, Down = 2, Left = 3, Right = 4 }

    [Flags]
    public enum GridCollapseDirections { None = 0, Front = 1, Back = 2, Left = 4, Right = 8 }

    /// <summary>
    /// 1 grid xếp block trên sàn XZ. Row 0 = ngoài cùng, gần path (gun ăn từ row 0 vào trong).
    /// <para><b>Shape = Arc</b>: vòng cung (fan). Mỗi hàng là 1 cung bán kính BaseRadius + row*RowSpacing,
    /// dãn đều trong góc mở ArcAngle. Số cell mỗi hàng theo <see cref="Layout"/>: ArcLength = chiều dài cung
    /// / (BlockWidth+Spacing) → ra xa nhiều cell hơn (cột lệch); Uniform = mọi hàng bằng row 0 (cột thẳng).</para>
    /// <para><b>Shape = Rect</b>: lưới CHỮ NHẬT thông thường — mỗi hàng có đúng <see cref="Columns"/> cell dãn
    /// theo trục X quanh Center, hàng sâu dần theo +Z. Cột luôn thẳng (chặn 1:1); Layout không dùng.</para>
    /// </summary>
    [Serializable]
    public class BlockGridData
    {
        [Tooltip("Arc = vòng cung quanh Center. Rect = lưới chữ nhật thông thường (hàng thẳng theo X).")]
        public BlockGridShape Shape = BlockGridShape.Arc;
        [Tooltip("Bên grid so với path. Left/Right = CHỈ nòng cùng bên của gun bắn được. Any = theo quạt gun.")]
        public GridSide Side = GridSide.Any;
        [Tooltip("Tâm grid (sàn XZ).")]
        public Vector3 Center;
        [Tooltip("Xoay cả grid quanh trục Y (độ). 0 = grid mở/sâu dần về +Z.")]
        public float Rotation;
        [Tooltip("Góc cộng thêm cho hướng của mọi cell trong grid (độ).")]
        public float CellDirectionOffset;
        [Tooltip("Hướng dồn mặc định của grid. Auto dùng hướng theo Rotation; cell có hướng riêng vẫn được ưu tiên riêng.")]
        public GridDefaultDirection DefaultDirection = GridDefaultDirection.Auto;
        [Tooltip("Arc: bán kính hàng đầu. Rect: khoảng cách từ Center tới hàng đầu (row 0, gần path).")]
        public float BaseRadius = 3f;
        [Tooltip("CHỈ dùng cho Rect: số cell mỗi hàng (mọi hàng bằng nhau → cột thẳng).")]
        [Min(1)] public int Columns = 5;
        [Tooltip("Bán kính tăng thêm mỗi hàng ra xa.")]
        public float RowSpacing = 1.2f;
        [Min(1)] public int Rows = 3;
        [Tooltip("Tổng góc mở/quét của cung (độ). >360 = cuộn nhiều vòng (xoắn ốc).")]
        public float ArcAngle = 90f;
        [Tooltip("Bán kính tăng thêm DỌC theo sweep: 0 = vòng cung phẳng; >0 = xoắn ốc.")]
        public float SpiralGrowth = 0f;
        [Tooltip("Độ rộng 1 block (world units).")]
        public float BlockWidth = 0.8f;
        [Tooltip("Khoảng cách giữa 2 block trên cùng hàng.")]
        public float Spacing = 0.2f;
        [Tooltip("Scale của MỌI block trong grid này (cell chỉ là node chứa nên scale áp thẳng lên block).")]
        public Vector3 CellScale = Vector3.one;
        [Tooltip("Khoảng cách giữa các block trong stack của grid khi CellScale khác (1,1,1).")]
        [Min(0.01f)] public float StackSpacing = 0.5f;
        [Tooltip("ArcLength = hàng ra xa nhiều cell hơn (cột lệch). Uniform = mọi hàng bằng nhau (cột thẳng).")]
        public BlockGridLayout Layout = BlockGridLayout.ArcLength;

        /// <summary>Grid scale mặc định dùng spacing chung; scale riêng dùng spacing riêng để stack khớp kích thước block.</summary>
        public bool UsesCustomStackSpacing => !Mathf.Approximately(CellScale.x, 1f)
                                            || !Mathf.Approximately(CellScale.y, 1f)
                                            || !Mathf.Approximately(CellScale.z, 1f);
        public float EffectiveStackSpacing => UsesCustomStackSpacing
            ? Mathf.Max(0.01f, StackSpacing)
            : Mathf.Max(0.01f, GameSettings.Instance != null ? GameSettings.Instance.BlockStackSpacing : 0.5f);
        [Tooltip("Các CẠNH gun bắn được (grid bị path bao quanh nhiều mặt). None = mặc định cũ: chỉ mặt " +
                 "Front (hàng 0). Bật thêm Back/Left/Right để phá được từ nhiều phía; Spawner8 ở giữa vẫn bất tử.")]
        public GridEdges ShootableEdges = GridEdges.None;
        [Tooltip("Bật = grid dồn cả DỌC (về hàng 0) LẪN NGANG (về index 0) → lấp lỗ 2 chiều về góc (hàng 0, " +
                 "index 0). Tắt = chỉ dồn dọc như cũ. (Chỉ áp dụng khi KHÔNG bật ShootableEdges.)")]
        public bool Collapse2D = false;
        [Tooltip("Custom hướng dồn của grid. None = dùng logic mặc định; có thể bật nhiều hướng cùng lúc.")]
        public GridCollapseDirections CustomCollapseDirections = GridCollapseDirections.None;

        [Header("Foundation (optional mesh below cells)")]
        [Tooltip("Tạo một mặt nền liền dưới toàn bộ grid. Với Shape = Spline, nền dùng chính spline nên bo/cong khớp cell.")]
        public bool GenerateFoundation;
        [Tooltip("Mesh nền tùy biến. Author mesh theo X = dọc spline, Z = chiều sâu grid, Y = độ cao. Khi gán, mesh này được bẻ theo shape của grid thay cho nền hộp tự sinh.")]
        public Mesh FoundationSourceMesh;
        [Tooltip("Material của nền. Cần gán khi bật Generate Foundation.")]
        public Material FoundationMaterial;
        [Min(0f)] [Tooltip("Nền nới ra ngoài cell theo cả hai chiều (world units).")]
        public float FoundationMargin = 0.25f;
        [Min(0f)] [Tooltip("Độ dày thành bên của nền. 0 = chỉ một mặt phẳng.")]
        public float FoundationThickness = 0.25f;
        [Tooltip("Độ cao của mặt nền so với tâm cell. Giá trị âm đặt nền nằm dưới block.")]
        public float FoundationYOffset = -0.12f;
        [Range(4, 128)] [Tooltip("Số đoạn theo chiều dài nền; tăng khi spline cong gắt.")]
        public int FoundationSegments = 32;
        [Tooltip("Các điểm local của foundation thủ công. Điểm cuối tự nối về điểm đầu.")]
        public List<Vector3> FoundationWaypoints = new List<Vector3>();
        public PathStyle FoundationStyle = PathStyle.RoundedCorner;
        [Min(0f)] public float FoundationCornerRadius = 0.5f;

        [Header("Spline (chỉ dùng khi Shape = Spline)")]
        [Tooltip("Waypoint của đường uốn lượn, toạ độ LOCAL so với Center + Rotation — dời/xoay grid là " +
                 "cả dải đi theo. Hàng 0 nằm cách đường này BaseRadius, hàng sau lệch thêm RowSpacing.")]
        public List<Vector3> SplineWaypoints = new List<Vector3>();
        [Tooltip("Khép kín đường uốn lượn thành vòng.")]
        public bool SplineClosed;
        [Tooltip("RoundedCorner = nối thẳng + bo góc. Bezier = cong mượt toàn phần.")]
        public PathStyle SplineStyle = PathStyle.RoundedCorner;
        [Tooltip("Bán kính bo góc — chỉ dùng khi SplineStyle = RoundedCorner.")]
        public float SplineCornerRadius = 1f;
        [Tooltip("Cell theo thứ tự (row, element). Chỉ dùng Color + BlockStackCt.")]
        public List<BlockCellData> Cells = new List<BlockCellData>();

        [Tooltip("Hàng đợi SPAWNER nhả thêm (~ PendingBlockDataArr của PixelShoot_2). Mỗi lần ring front " +
                 "bị thu hết → collapse, spawner dựng 1 ring mới ở NGOÀI CÙNG lấy lần lượt các mục này.")]
        public List<PendingBlockData> PendingRefill = new List<PendingBlockData>();

        private const int SampleCount = 48;

        /// <summary>Tổng số block đang chờ trong hàng đợi refill (∑BlockStackCt).</summary>
        public int PendingBlockTotal()
        {
            int t = 0;
            if (PendingRefill != null)
                foreach (var p in PendingRefill)
                    if (p != null && p.BlockStackCt > 0) t += p.BlockStackCt;
            return t;
        }

        /// <summary>Hướng "sâu dần" của grid trên sàn (local +Z sau khi xoay). Hàng 0 gần path nhất.</summary>
        public Vector3 Forward => Quaternion.Euler(0f, Rotation, 0f) * Vector3.forward;

        public float DefaultCollapseAngle
        {
            get
            {
                switch (DefaultDirection)
                {
                    case GridDefaultDirection.Up: return 0f;
                    case GridDefaultDirection.Down: return 180f;
                    case GridDefaultDirection.Left: return 270f;
                    case GridDefaultDirection.Right: return 90f;
                    default: return Mathf.Repeat(Mathf.Atan2((-Forward).x, (-Forward).z) * Mathf.Rad2Deg + CellDirectionOffset, 360f);
                }
            }
        }

        /// <summary>
        /// Cung đã khép kín thành VÒNG TRÒN: mỗi hàng là 1 vòng đồng tâm quanh Center, nhiều Rows =
        /// nhiều vòng lồng nhau (dùng cho level kiểu ring/bo tròn).
        /// <para>Xoắn ốc (SpiralGrowth &gt; 0) KHÔNG tính là vòng kín: đầu và cuối nằm ở 2 bán kính khác
        /// nhau nên không chạm nhau, vẫn phải trải đều như cung hở.</para>
        /// </summary>
        public bool IsFullRing => (Shape == BlockGridShape.Arc && ArcAngle >= 360f && SpiralGrowth <= 0f)
                               || (Shape == BlockGridShape.Spline && SplineClosed);

        /// <summary>
        /// Hướng cell do HÌNH DẠNG GRID quyết định (Rect: ngược Forward; Spline: pháp tuyến của đường)
        /// → tính thẳng từ grid, KHÔNG đọc SpawnerDirectionAngleZ trong data. Nhờ vậy xoay grid / uốn
        /// lại đường là hướng khớp ngay, data cũ chưa Generate lại cũng không lệch.
        /// <para>Arc thì ngược lại: mỗi cell 1 góc riêng và người dùng kéo mũi tên chỉnh tay được → phải
        /// tôn trọng giá trị trong data.</para>
        /// </summary>
        public bool CellAngleFromShape => Shape != BlockGridShape.Arc;

        /// <summary>
        /// Hướng dồn/nhả MẶC ĐỊNH của cell (độ quanh Y) — tiến về phía path, tức chiều cell trượt khi
        /// dồn hàng và chiều spawner đẩy cell ra. Nguồn DUY NHẤT cho mọi nơi tính hướng cell.
        /// <para><b>Rect</b>: lưới thẳng nên MỌI cell chung 1 hướng = ngược <see cref="Forward"/>
        /// (hàng sâu tiến ra hàng 0). Không phụ thuộc row/e.</para>
        /// <para><b>Arc</b>: theo từng cell — hướng từ cell về tâm grid (cung mở ra nên mỗi cell 1 góc).</para>
        /// </summary>
        public float DefaultCellAngle(int row, int e)
        {
            Vector3 v;
            if (Shape == BlockGridShape.Rect) v = -Forward;
            else if (Shape == BlockGridShape.Spline) v = SplineInward(row, e);
            else v = Center - CellPos(row, e); // Arc: hướng về tâm vốn đã vuông góc với cung
            v.y = 0f;
            float angle = v.sqrMagnitude > 1e-6f ? Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg : 0f;
            return Mathf.Repeat(angle + CellDirectionOffset, 360f);
        }

        /// <summary>
        /// Hướng cell của Spline grid: VUÔNG GÓC với đường spline và chỉ VỀ PHÍA đường đó (các hàng lệch
        /// ra theo +pháp tuyến nên hướng vào là −pháp tuyến). Cũng đúng chiều cell trượt khi dồn hàng.
        /// <para>Tiếp tuyến lấy từ 2 điểm sát nhau TRÊN CHÍNH HÀNG NÀY. Không lấy hiệu 2 hàng
        /// (CellPosAt(row) − CellPosAt(row+1)): mỗi hàng một chiều dài khác nhau nên cell cùng index ở 2
        /// hàng KHÔNG nằm trên cùng pháp tuyến, vào khúc cong là hướng xiên đi.</para>
        /// </summary>
        private Vector3 SplineInward(int row, int e)
        {
            int count = ElementsInRow(row);
            float total = RowLength(row);
            if (total < 1e-4f) return Vector3.forward;

            float target = count <= 1 ? total * 0.5f
                         : IsFullRing ? e * (total / count)
                                      : e * (total / (count - 1));

            float d = Mathf.Max(0.01f, total * 0.01f);
            Vector3 a = PosByLength(row, Mathf.Max(0f, target - d), total);
            Vector3 b = PosByLength(row, Mathf.Min(total, target + d), total);
            Vector3 tan = b - a; tan.y = 0f;
            if (tan.sqrMagnitude < 1e-8f) return Vector3.forward;

            return -Vector3.Cross(Vector3.up, tan.normalized);
        }

        /// <summary>Offset local (chưa xoay) → toạ độ world: xoay quanh Y rồi dời về Center.</summary>
        private Vector3 ToWorld(Vector3 local) => Center + Quaternion.Euler(0f, Rotation, 0f) * local;

        // Vị trí tâm-hàng theo s (0..1) dọc sweep — có xoắn ốc.
        /// <summary>Điểm trên dải nền/grid ở hàng có thể là số lẻ. Dùng cho mesh foundation để mép ôm đúng spline.</summary>
        public Vector3 SurfacePosition(float row, float s)
        {
            if (Shape == BlockGridShape.Rect)
            {
                float step = Mathf.Max(0.01f, BlockWidth + Spacing);
                float width = Mathf.Max(BlockWidth, (Mathf.Max(1, Columns) - 1) * step + BlockWidth + FoundationMargin * 2f);
                float lateral = (Mathf.Clamp01(s) - 0.5f) * width;
                return ToWorld(new Vector3(lateral, 0f, BaseRadius + row * RowSpacing));
            }
            return PosAlong(row, s);
        }

        public Vector3 FoundationWorldPoint(Vector3 local) => ToWorld(local);

        private Vector3 PosAlong(float row, float s)
        {
            if (Shape == BlockGridShape.Spline) return SplinePosAlong(row, s);
            float angleRad = Mathf.Lerp(-ArcAngle * 0.5f, ArcAngle * 0.5f, s) * Mathf.Deg2Rad;
            float radius = BaseRadius + row * RowSpacing + s * SpiralGrowth;
            return ToWorld(new Vector3(Mathf.Sin(angleRad) * radius, 0f, Mathf.Cos(angleRad) * radius));
        }

        // Đường uốn lượn đã dựng (world) + arc-length cộng dồn. NonSerialized: chỉ là cache tính lại được.
        [NonSerialized] private Vector3[] _splinePts;
        [NonSerialized] private float[] _splineArc;
        [NonSerialized] private int _splineKey;
        [NonSerialized] private bool _splineBuilt;

        /// <summary>World-space cached samples of the grid spline, intended for editor preview drawing.</summary>
        public IReadOnlyList<Vector3> SplineSamples
        {
            get { EnsureSpline(); return _splinePts; }
        }

        /// <summary>
        /// Dựng đường uốn lượn từ SplineWaypoints — dùng ĐÚNG bộ dựng của path
        /// (<see cref="RoundedPolylinePath.BuildSamples"/>) nên Spline grid và path cong y hệt nhau,
        /// và tự có luôn cả 2 kiểu RoundedCorner / Bezier.
        /// <para>Phải cache: PosAlong bị gọi 48 lần cho MỖI RowLength, mà RowLength lại nằm trong
        /// ElementsInRow → CellIndex → vòng lặp theo row. Dựng lại mỗi lần thì editor tụt frame ngay.</para>
        /// </summary>
        private void EnsureSpline()
        {
            if (_splineBuilt) return;
            int key = SplineKey();
            _splineKey = key;
            _splineBuilt = true;
            _splinePts = null;
            _splineArc = null;

            // Dựng ở LOCAL rồi mới ToWorld: ToWorld là phép dời+xoay cứng nên 2 thứ tự cho kết quả như nhau.
            var local = RoundedPolylinePath.BuildSamples(SplineWaypoints, SplineClosed, SplineCornerRadius,
                                                        8, SplineStyle);
            if (local == null || local.Length < 2) return;

            _splinePts = new Vector3[local.Length];
            _splineArc = new float[local.Length];
            for (int i = 0; i < local.Length; i++) _splinePts[i] = ToWorld(local[i]);
            for (int i = 1; i < local.Length; i++)
                _splineArc[i] = _splineArc[i - 1] + Vector3.Distance(_splinePts[i - 1], _splinePts[i]);
        }

        // Đổi mọi thứ ảnh hưởng hình dạng đường thành 1 số — lệch là dựng lại.
        private int SplineKey()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (SplineWaypoints != null ? SplineWaypoints.Count : 0);
                if (SplineWaypoints != null)
                    foreach (var w in SplineWaypoints) h = h * 31 + w.GetHashCode();
                h = h * 31 + SplineClosed.GetHashCode();
                h = h * 31 + SplineCornerRadius.GetHashCode();
                h = h * 31 + (int)SplineStyle;
                h = h * 31 + Center.GetHashCode();
                h = h * 31 + Rotation.GetHashCode();
                return h;
            }
        }

        /// <summary>Điểm trên hàng 'row' tại s (0..1) dọc đường: bám đường rồi lệch ra theo PHÁP TUYẾN.</summary>
        private Vector3 SplinePosAlong(float row, float s)
        {
            EnsureSpline();
            if (_splinePts == null) return Center;

            float total = _splineArc[_splineArc.Length - 1];
            float target = Mathf.Clamp01(s) * total;

            // Tìm nhị phân đoạn chứa target — quét tuyến tính ở đây là O(n) nhân với 48 lần gọi mỗi RowLength.
            int lo = 0, hi = _splineArc.Length - 1;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) >> 1;
                if (_splineArc[mid] <= target) lo = mid; else hi = mid;
            }

            float d0 = _splineArc[lo], d1 = _splineArc[hi];
            float t = d1 - d0 > 1e-5f ? (target - d0) / (d1 - d0) : 0f;
            Vector3 p = Vector3.Lerp(_splinePts[lo], _splinePts[hi], t);

            Vector3 tan = _splinePts[hi] - _splinePts[lo]; tan.y = 0f;
            if (tan.sqrMagnitude < 1e-8f) return p;
            Vector3 normal = Vector3.Cross(Vector3.up, tan.normalized);
            return p + normal * (BaseRadius + row * RowSpacing);
        }

        // Cache mẫu arc-length THEO HÀNG cho Arc/Spline — khoá theo hình dạng grid. RowLength/PosByLength
        // gốc gọi PosAlong 48 lần MỖI lần, mà editor lại gọi chúng cho từng cell mỗi frame → giật khi
        // nhiều grid. Dựng 1 lần rồi tra O(1)/nhị phân, lệch hình mới dựng lại. (Rect không đi qua đây:
        // ElementsInRow/CellPosAt của Rect tính thẳng, không đụng PosAlong.)
        private struct RowSample { public Vector3[] Pts; public float[] Arc; public float Total; }
        [NonSerialized] private Dictionary<int, RowSample> _rowCache;
        [NonSerialized] private int _geoKey;
        [NonSerialized] private bool _geoBuilt;

        // Mọi thứ ảnh hưởng PosAlong (cả Arc lẫn Spline) gộp thành 1 số — lệch là bỏ cache dựng lại.
        private int GeometryKey()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (int)Shape;
                h = h * 31 + ArcAngle.GetHashCode();
                h = h * 31 + BaseRadius.GetHashCode();
                h = h * 31 + RowSpacing.GetHashCode();
                h = h * 31 + SpiralGrowth.GetHashCode();
                h = h * 31 + Center.GetHashCode();
                h = h * 31 + Rotation.GetHashCode();
                if (Shape == BlockGridShape.Spline) h = h * 31 + SplineKey();
                return h;
            }
        }

        private RowSample GetRow(int row)
        {
            if (!_geoBuilt || _rowCache == null)
            {
                _geoKey = GeometryKey(); _geoBuilt = true;
                _rowCache = new Dictionary<int, RowSample>();
            }
            if (_rowCache.TryGetValue(row, out var rs)) return rs;

            var pts = new Vector3[SampleCount + 1];
            var arc = new float[SampleCount + 1];
            for (int i = 0; i <= SampleCount; i++) pts[i] = PosAlong(row, i / (float)SampleCount);
            for (int i = 1; i <= SampleCount; i++) arc[i] = arc[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
            rs = new RowSample { Pts = pts, Arc = arc, Total = arc[SampleCount] };
            _rowCache[row] = rs;
            return rs;
        }

        private float RowLength(int row) => GetRow(row).Total;

        /// <summary>
        /// Số cell hàng row. ArcLength: theo chiều dài cung (ra xa nhiều hơn).
        /// Uniform: mọi hàng lấy theo row 0 (hàng CHẬT nhất) để không hàng nào bị chồng cell.
        /// </summary>
        public int ElementsInRow(int row)
        {
            if (Shape == BlockGridShape.Rect) return Mathf.Max(1, Columns); // lưới chữ nhật: mọi hàng bằng nhau
            if (Layout == BlockGridLayout.Uniform) row = 0;
            float step = Mathf.Max(0.01f, BlockWidth + Spacing);
            return Mathf.Max(1, Mathf.FloorToInt(RowLength(row) / step));
        }

        /// <summary>
        /// Các index ở hàng TRƯỚC (row-1) chặn cell (row, e), map theo vị trí góc chuẩn hoá dọc cung.
        /// Uniform → luôn 1:1 (chỉ <paramref name="a"/>). ArcLength → cell giữa có 2 index chặn
        /// (<paramref name="a"/> và <paramref name="b"/>), cell đầu/cuối chỉ 1. b = -1 nếu không có.
        /// </summary>
        public static void FrontIndices(int curCount, int prevCount, int e, out int a, out int b)
        {
            a = -1; b = -1;
            if (prevCount <= 0) return;

            // Cùng số cell (Rect / Arc-Uniform) → cột THẲNG, map 1:1. Phải chặn sớm ở đây: đi qua công thức
            // tỉ lệ bên dưới sẽ dính sai số float (vd 3/9f*9f = 3.0000001 → Ceil ra 4), sinh index chặn giả
            // và cho cell dồn CHÉO sang cột bên cạnh.
            if (curCount == prevCount) { a = Mathf.Clamp(e, 0, prevCount - 1); return; }
            if (curCount <= 1 || prevCount <= 1) { a = 0; return; }

            float s = Mathf.Clamp01(e / (float)(curCount - 1)); // vị trí chuẩn hoá dọc cung
            float f = s * (prevCount - 1);

            // Rơi (gần như) đúng vào 1 index → chỉ 1 cell chặn. Snap để sai số float không đẻ ra index thứ 2.
            int fi = Mathf.RoundToInt(f);
            if (Mathf.Abs(f - fi) < 1e-4f) { a = Mathf.Clamp(fi, 0, prevCount - 1); return; }

            a = Mathf.Clamp(Mathf.FloorToInt(f), 0, prevCount - 1);
            int c = Mathf.Clamp(Mathf.CeilToInt(f), 0, prevCount - 1);
            if (c != a) b = c;
        }

        // Prefix-sum số cell theo hàng: _rowStart[r] = tổng cell các hàng < r, _rowStart[Rows] = tổng cả grid.
        // CellIndex/GetCell gốc cộng dồn ElementsInRow từ hàng 0 → O(row) MỖI cell, mà editor gọi cho từng
        // cell mỗi frame → O(rows²) khi grid nhiều row (thủ phạm chính gây giật). Dựng 1 lần rồi tra O(1).
        [NonSerialized] private int[] _rowStart;
        [NonSerialized] private int _layoutKey;
        [NonSerialized] private bool _layoutBuilt;

        /// <summary>
        /// Geometry is immutable between edits. Call this after changing the grid shape from code;
        /// the Level Tool calls it once after applying serialized edits.
        /// </summary>
        public void InvalidateGeometryCache()
        {
            _splineBuilt = false;
            _splinePts = null;
            _splineArc = null;
            _geoBuilt = false;
            _rowCache?.Clear();
            _layoutBuilt = false;
            _rowStart = null;
        }

        // Mọi thứ ảnh hưởng SỐ CELL mỗi hàng (khác GeometryKey vốn chỉ lo vị trí): thêm BlockWidth/Spacing/
        // Columns/Layout/Rows. Lệch là dựng lại prefix-sum.
        private int LayoutKey()
        {
            unchecked
            {
                int h = GeometryKey();
                h = h * 31 + BlockWidth.GetHashCode();
                h = h * 31 + Spacing.GetHashCode();
                h = h * 31 + Columns;
                h = h * 31 + (int)Layout;
                h = h * 31 + Rows;
                return h;
            }
        }

        private void EnsureRowStart()
        {
            if (_layoutBuilt && _rowStart != null) return;
            _layoutBuilt = true; _layoutKey = LayoutKey();
            int n = Mathf.Max(0, Rows);
            _rowStart = new int[n + 1];
            for (int r = 0; r < n; r++) _rowStart[r + 1] = _rowStart[r] + ElementsInRow(r);
        }

        public int TotalCells()
        {
            EnsureRowStart();
            return _rowStart[Mathf.Max(0, Rows)];
        }

        /// <summary>Index phẳng của cell (row, element) trong <see cref="Cells"/>.</summary>
        public int CellIndex(int row, int e)
        {
            EnsureRowStart();
            if (row < 0) return e;
            if (row >= _rowStart.Length) return _rowStart[_rowStart.Length - 1] + e; // vượt Rows: kẹp cuối
            return _rowStart[row] + e;
        }

        /// <summary>
        /// Vị trí phần tử e trong 'count' phần tử của hàng row.
        /// Arc: dãn ĐỀU theo chiều dài đường cong. Rect: lưới thẳng — cell dãn theo X quanh Center,
        /// hàng sâu dần theo +Z (row 0 gần path nhất).
        /// </summary>
        public Vector3 CellPosAt(int row, int e, int count)
        {
            if (Shape == BlockGridShape.Rect)
            {
                float step = Mathf.Max(0.01f, BlockWidth + Spacing);
                float lateral = (e - (count - 1) * 0.5f) * step; // canh giữa quanh Center
                return ToWorld(new Vector3(lateral, 0f, BaseRadius + row * RowSpacing));
            }
            float total = RowLength(row);
            // Vòng KÍN: chia total cho count, KHÔNG phải count-1. Chia count-1 thì cell cuối rơi đúng
            // lên cell đầu (góc −180 và +180 là cùng 1 điểm) → 2 cell chồng nhau ở mối nối, và hụt mất
            // 1 khe. Cung HỞ thì ngược lại: cell đầu/cuối phải nằm đúng 2 đầu mút nên chia count-1.
            float target = count <= 1 ? total * 0.5f
                         : IsFullRing ? e * (total / count)
                                      : e * (total / (count - 1));
            return PosByLength(row, target, total);
        }

        public Vector3 CellPos(int row, int e) => CellPosAt(row, e, ElementsInRow(row));

        private Vector3 PosByLength(int row, float targetLen, float totalLen)
        {
            var rs = GetRow(row);
            if (targetLen <= 0f) return rs.Pts[0];
            if (targetLen >= rs.Total) return rs.Pts[SampleCount];

            // Nhị phân trên arc-length đã cộng dồn sẵn thay cho quét tuyến tính + PosAlong 48 lần.
            int lo = 0, hi = SampleCount;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) >> 1;
                if (rs.Arc[mid] <= targetLen) lo = mid; else hi = mid;
            }
            float d = rs.Arc[hi] - rs.Arc[lo];
            return Vector3.Lerp(rs.Pts[lo], rs.Pts[hi], d > 1e-4f ? (targetLen - rs.Arc[lo]) / d : 0f);
        }

        public BlockCellData GetCell(int row, int e)
        {
            int idx = CellIndex(row, e);
            return (Cells != null && idx >= 0 && idx < Cells.Count) ? Cells[idx] : null;
        }
    }

    /// <summary>
    /// 1 mục trong hàng đợi refill của spawner (~ PendingBlockData của PixelShoot_2): màu + số block xếp chồng.
    /// Khi ring front bị thu hết, spawner lấy lần lượt các mục này để dựng cell mới ở ring ngoài cùng.
    /// </summary>
    [Serializable]
    public class PendingBlockData
    {
        public TypeColor Color;
        [Min(1)] public int BlockStackCt = 3;
    }

    /// <summary>Data 1 gun: màu + số đạn (yêu cầu #7).</summary>
    [Serializable]
    public class GunData
    {
        public TypeColor Color;
        [Min(1)] public int CountBullet = 5;
        [Tooltip("Gun ẨN: che màu (dùng material 'hidden') cho tới khi nó ra VỊ TRÍ ĐẦU (index 0) của slot " +
                 "mới lộ màu thật.")]
        public bool Hidden;
        [Tooltip("Nhóm CONNECT (0 = không connect). Các gun CÙNG số này (ở slot khác nhau) nối với nhau: chỉ " +
                 "deploy được khi TẤT CẢ đang ở index 0, và cả nhóm vào path CÙNG lúc.")]
        public int ConnectGroup;
    }

    /// <summary>Data 1 block đơn trong cell: index + vị trí local (yêu cầu #8).</summary>
    [Serializable]
    public class BlockData
    {
        public int IndexInStack;
        public Vector3 LocalPos;
    }

    /// <summary>
    /// Data 1 cell block — bám sát BlockCellData của PixelShoot_2 (per-cell).
    /// Cell cùng <see cref="BlockCol"/> tạo thành 1 cột; <see cref="SpawnerDepth"/> = vị trí trong cột
    /// (0 = ngoài cùng, gần collector). Khi cell depth 0 bị phá hết → các cell phía sau dồn lên.
    /// </summary>
    [Serializable]
    public class BlockCellData
    {
        public TypeColor Color;
        public Vector3 CellPos;
        public Vector3 CellScale = Vector3.one;

        [Tooltip("Nhóm cột — các cell cùng BlockCol dồn về nhau.")]
        public int BlockCol;
        [Tooltip("Vị trí trong cột: 0 = ngoài cùng (gần collector).")]
        public int SpawnerDepth;
        [Tooltip("Số block xếp chồng trong cell (~ BlockStackCt). 0 = ô LỖ: không dựng cell, và không cell " +
                 "nào được dồn/refill vào đó — grid giữ nguyên hình, chỉ thủng đúng ô này.")]
        [Min(0)] public int BlockStackCt = 3;
        [Tooltip("Gun có được BẮN cell này không. Tắt = cell không bao giờ bị ngắm (dù lộ ra) — chỉ bị dọn " +
                 "gián tiếp khi dồn. Dùng để custom chỉ MỘT SỐ cell của grid là bắn được.")]
        public bool Shootable = true;
        [Tooltip("Normal = phá xong biến mất. Spawner = còn hàng đợi phía sau, phá xong thì đẩy cell kế ra. " +
                 "Spawner8 = nhả vào ô trống ở 8 ô xung quanh. SpawnerLine = nhả theo 1 ĐƯỜNG (dọc/ngang " +
                 "theo hướng mũi tên) tới tối đa Reach ô.")]
        public BlockCellType Type = BlockCellType.Normal;
        [Tooltip("CHỈ dùng cho SpawnerLine: số ô tối đa nhả ra dọc theo hướng, tính từ ô gốc (0 = không giới hạn).")]
        [Min(0)] public int SpawnerReach = 0;
        [Tooltip("CHỈ dùng cho Spawner8: các hướng được phép nhả cell.")]
        public Spawner8Directions Spawner8Directions = Spawner8Directions.All;
        [Tooltip("CHỈ dùng cho Spawner/Spawner8/SpawnerLine: các cell PHÍA SAU, đẩy ra lần lượt khi có ô trống để nhả.")]
        public List<PendingBlockData> Queue = new List<PendingBlockData>();
        [Tooltip("Hướng dồn/spawn của cell trên sàn ngang XZ, tính bằng độ quanh trục Y (0° = +Z).")]
        public float SpawnerDirectionAngleZ;
        [Tooltip("Dùng hướng riêng của cell thay vì hướng tự động theo shape của grid. SpawnerLine luôn dùng hướng riêng.")]
        public bool UseCustomDirection;
        [Tooltip("Khi queue của Spawner/Spawner8/SpawnerLine hết: bật để xoá ô gốc và cho cell khác dồn vào; tắt để giữ ô gốc bị khóa.")]
        public bool AllowCollapseIntoAfterQueueEmpty = true;

        [Tooltip("Cell bị BĂNG phủ: KHÔNG bắn được cho tới khi băng tan (phá đủ block). IceController tự sinh " +
                 "khối Ice phủ vùng cell băng liền kề khi Play.")]
        public bool Iced;
        [Tooltip("Băng tan khi TỔNG số block đã phá trong màn ≥ ngưỡng này (cũng là countdown ban đầu). Cell " +
                 "liền kề CÙNG ngưỡng gộp thành 1 khối băng; khối lớn nhất hiện countdown.")]
        [Min(0)] public int IceThreshold;

        /// <summary>Hướng dồn (vector NGANG trên sàn XZ): 0°=+Z, 90°=+X, 180°=−Z, 270°=−X.</summary>
        public Vector3 DirectionVector => Quaternion.Euler(0f, SpawnerDirectionAngleZ, 0f) * Vector3.forward;
    }

    /// <summary>
    /// Data 1 slot: chỉ chứa danh sách gun theo thứ tự ra (yêu cầu #6). Vị trí/hướng slot lấy từ
    /// GunSlot đặt sẵn trên SCENE; spacing dùng chung từ <see cref="GameSettings"/>.
    /// </summary>
    [Serializable]
    public class SlotData
    {
        public System.Collections.Generic.List<GunData> Guns = new System.Collections.Generic.List<GunData>();
    }

    /// <summary>Prop trang trí trên board (~ GameBoardPropData).</summary>
    [Serializable]
    public class GameBoardPropData
    {
        public GameObject PropPrefab;
        public Vector3 PropPos;
        public Quaternion PropRot;
        public Vector3 PropScale = Vector3.one;
    }

    [Serializable]
    public class SignData
    {
        [HideInInspector] public GameObject Prefab;
        public Vector3 Pos = new Vector3(8.8f, 0f, -30f);
        public Vector3 Scale = Vector3.one;
    }

    /// <summary>Obstacle đặt trên board: 1 model 3D có vị trí/xoay/scale riêng, spawn khi Play.</summary>
    [Serializable]
    public class BlockObstacleData
    {
        public BlockObstacleType Type;
        [Tooltip("Model 3D của obstacle. Kích thước MẶC ĐỊNH trên map = bounds của model này (Scale = 1).")]
        public GameObject Prefab;
        public Vector3 Pos;
        [Tooltip("Xoay quanh trục đứng (độ). Kéo handle XANH LÁ trên map.")]
        public float RotationY;
        [Tooltip("Nhân lên kích thước model. (1,1,1) = đúng kích thước gốc của model.")]
        public Vector3 Scale = Vector3.one;
        [Tooltip("Index cell mà obstacle gắn vào (-1 = độc lập).")]
        public int TargetCellIndex = -1;
        [Min(1)] public int Strength = 1;
    }

}

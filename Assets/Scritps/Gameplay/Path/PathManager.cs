using System.Collections.Generic;
using UnityEngine;

namespace Wayfu.Lamkn
{
    /// <summary>
    /// Dựng đường path (RoundedPolylinePath + mặt đường LineRenderer) từ LevelData và quản lý các gun
    /// chạy trên đó. Mọi gun vào path tại ĐIỂM ĐẦU (FrontStationDistance, mặc định 0) rồi chạy loop liên
    /// tục bằng RoundedPolylineFollower. Gun chỉ được vào khi điểm đầu còn trống ít nhất
    /// GameSettings.GunSpacing; chưa đủ thì ĐỨNG CHỜ ngay tại điểm đầu (pos 0) cho tới lượt.
    /// Thua khi đủ MaxGunOnPath mà không gun nào bắn được.
    /// </summary>
    public class PathManager : Singleton<PathManager>
    {
        [Header("Mặt đường")]
        [Tooltip("LineRenderer vẽ mặt đường — gán sẵn trên scene. Bỏ trống thì không vẽ mặt đường.")]
        [SerializeField] private LineRenderer pathLine;
        [Tooltip("Material mặt đường. Bỏ trống thì giữ material đang gán trên LineRenderer.")]
        [SerializeField] private Material pathMaterial;

        [Header("Tunnel (chỉ path HỞ)")]
        [Tooltip("Prefab cửa hầm ở ĐẦU path — nơi gun đi ra. Chỉ sinh khi LevelData.IsClosed = false.")]
        [SerializeField] private GameObject tunnelInPrefab;
        [Tooltip("Prefab cửa hầm ở CUỐI path — nơi gun đi vào. Chỉ sinh khi LevelData.IsClosed = false.")]
        [SerializeField] private GameObject tunnelOutPrefab;
        [Tooltip("Tunnel quay mặt về điểm cách nó ĐOẠN NÀY dọc path (world units). Lớn quá thì hướng bị " +
                 "cắt ngang khúc cua; nhỏ quá thì nhiễu vì 2 điểm gần trùng nhau.")]
        [Min(0.01f)] [SerializeField] private float tunnelFaceDistance = 1f;

        [Header("Queue")]
        [Tooltip("Thời gian (giây) gun bay từ slot ra chỗ đứng chờ ở điểm vào path (pos 0).")]
        [SerializeField] private float queueMoveDuration = 0.15f;

        [Header("Nước chảy")]
        [Tooltip("Tốc độ cuộn UV material mặt đường để tạo hiệu ứng nước chảy. X = dọc theo path (chiều " +
                 "chảy), Y = ngang. (0,0) = tắt hiệu ứng. Đảo dấu X để chảy ngược.")]
        [SerializeField] private Vector2 waterScrollSpeed = new Vector2(-0.5f, 0f);
        [Tooltip("Các texture property được cuộn offset. Shader TCP2/URP dùng _BaseMap; Built-in/Legacy " +
                 "dùng _MainTex — để cả 2 cho chắc, property nào material không có sẽ tự bỏ qua.")]
        [SerializeField] private string[] waterScrollTextures = { "_BaseMap", "_MainTex" };

        private RoundedPolylinePath _path;
        private GameObject _tunnelIn, _tunnelOut;
        private readonly List<Gun> _guns = new List<Gun>();    // [0] = gun vào trước nhất
        private readonly List<Gun> _queue = new List<Gun>();   // [0] = gun sẽ vào path kế tiếp
        private float _gunSpeed = 3f;
        private float _minGunGap = 1.2f;     // khoảng cách arc-length tối thiểu giữa 2 gun
        private float _frontStationDistance; // điểm VÀO path của mọi gun (0 = đầu path)
        private int _maxGunOnPath = 5;

        // Nước chảy: bản sao material của pathLine để cuộn UV runtime (không dirty asset .mat). Giữ offset
        // gốc từng texture để cộng dồn scroll lên trên, không đè mất offset đã set trong material.
        private Material _waterMat;
        private string[] _waterProps;
        private Vector2[] _waterBaseOffset;
        private Vector2 _waterScroll;

        /// <summary>Gun đang chờ cũng chiếm chỗ — không cho click quá sức chứa của path.</summary>
        private int Reserved => _guns.Count + _queue.Count;

        public bool IsFull => Reserved >= _maxGunOnPath;
        public bool CanAccept => Reserved < _maxGunOnPath;
        /// <summary>Còn đủ chỗ nhận thêm <paramref name="n"/> gun cùng lúc không (cho nhóm connect).</summary>
        public bool CanAcceptCount(int n) => Reserved + n <= _maxGunOnPath;
        public int GunCount => _guns.Count;
        public int QueueCount => _queue.Count;
        public RoundedPolylinePath Path => _path;

        /// <summary>Dựng path từ level rồi nạp config gun. Gọi thay cho Init(path) cũ.</summary>
        public void Build(LevelData level)
        {
            Clear();

            var gs = GameSettings.Instance;
            _gunSpeed = gs != null ? gs.GunSpeed : 3f;
            _maxGunOnPath = gs != null ? gs.MaxGunOnPath : 5;
            _frontStationDistance = gs != null ? gs.FrontStationDistance : 0f;
            _minGunGap = gs != null ? Mathf.Max(0f, gs.GunSpacing) : 1.2f;

            _path = CreatePath(level);
            ApplyPathLine(_path);
            SetPathWidth(gs != null ? gs.PathWidth : 1.5f); // Path Width dùng chung từ GameSettings
            SpawnTunnels(level);
        }

        /// <summary>
        /// Sinh cửa hầm ở 2 ĐẦU MÚT của path hở. Path kín thì không có đầu mút nào để đặt → bỏ qua.
        /// Cả 2 tunnel đều quay mặt về điểm cách nó tunnelFaceDistance dọc path, tức đều nhìn VÀO trong
        /// đường: cửa vào nhìn theo chiều gun chạy ra, cửa ra nhìn ngược lại đón gun đang tới.
        /// </summary>
        private void SpawnTunnels(LevelData level)
        {
            if (level.IsClosed || _path == null || _path.samples == null || _path.samples.Length < 2) return;

            float len = _path.TotalLength;
            float x = Mathf.Min(tunnelFaceDistance, len); // path ngắn hơn x → nhìn thẳng sang đầu kia
            var s = _path.samples;

            // KHÔNG dùng GetPointAtDistance cho 2 đầu mút: nó Mathf.Repeat nên distance = TotalLength
            // wrap về 0, cửa ra sẽ nhảy về đúng chỗ cửa vào. Lấy thẳng từ mảng samples.
            _tunnelIn = CreateTunnel(tunnelInPrefab, "TunnelIn", s[0], _path.GetPointAtDistance(x));
            _tunnelOut = CreateTunnel(tunnelOutPrefab, "TunnelOut", s[s.Length - 1],
                                      _path.GetPointAtDistance(len - x));
        }

        private GameObject CreateTunnel(GameObject prefab, string name, Vector3 pos, Vector3 lookAt)
        {
            if (prefab == null) return null;
            var go = Instantiate(prefab, pos, Quaternion.identity, transform);
            go.name = name;

            Vector3 dir = lookAt - pos; dir.y = 0f; // chỉ xoay trên sàn, không chúc lên/xuống
            if (dir.sqrMagnitude > 1e-6f) go.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            return go;
        }

        private RoundedPolylinePath CreatePath(LevelData level)
        {
            var go = new GameObject("GunPath");
            go.transform.SetParent(transform);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var path = go.AddComponent<RoundedPolylinePath>();
            path.isClosed = level.IsClosed;
            path.style = level.PathStyle;
            path.cornerRadius = level.CornerRadius;
            path.waypoints = new List<Transform>();

            for (int i = 0; i < level.PathWaypoints.Count; i++)
            {
                var wp = new GameObject("WP_" + i).transform;
                wp.SetParent(go.transform);
                wp.position = level.PathWaypoints[i];
                path.waypoints.Add(wp);
            }
            path.GeneratePath();
            return path;
        }

        // Đổ đường bo góc vào LineRenderer đã gán sẵn trên scene.
        private void ApplyPathLine(RoundedPolylinePath path)
        {
            if (pathLine == null) return;

            // Trục Z của LineRenderer phải chỉ LÊN vì dùng LineAlignment.TransformZ — không thì mặt đường
            // dựng đứng. useWorldSpace nên transform chỉ ảnh hưởng hướng mặt, không ảnh hưởng toạ độ điểm.
            pathLine.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            pathLine.alignment = LineAlignment.TransformZ;
            pathLine.useWorldSpace = true;
            pathLine.loop = false; // samples đã tự khép kín khi IsClosed → bật loop sẽ nối thừa 1 đoạn
            pathLine.numCornerVertices = 6;
            pathLine.numCapVertices = 6;
            pathLine.textureMode = LineTextureMode.Tile;
            pathLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (pathMaterial != null) pathLine.sharedMaterial = pathMaterial;

            SetupWaterFlow();

            if (path != null && path.samples != null && path.samples.Length >= 2)
            {
                pathLine.positionCount = path.samples.Length;
                pathLine.SetPositions(path.samples);
            }
            else pathLine.positionCount = 0;
        }

        /// <summary>
        /// Chuẩn bị cuộn UV cho hiệu ứng nước chảy: lấy MỘT bản sao material của pathLine (pathLine.material
        /// tự clone sharedMaterial nên không ghi vào asset .mat), rồi ghi nhớ offset gốc của từng texture để
        /// mỗi frame cộng dồn scroll lên trên. Speed = 0 hoặc không có texture khớp thì tắt hẳn (đỡ tốn frame).
        /// </summary>
        private void SetupWaterFlow()
        {
            // Build mới: bỏ bản sao cũ đi (mỗi lần .material lại clone thêm 1 cái, không dọn là rò rỉ).
            if (_waterMat != null) { Destroy(_waterMat); _waterMat = null; }
            _waterProps = null;
            _waterBaseOffset = null;
            _waterScroll = Vector2.zero;

            if (pathLine == null || waterScrollSpeed == Vector2.zero || waterScrollTextures == null) return;

            var mat = pathLine.material; // clone → chỉ ảnh hưởng line này, không dirty asset dùng chung
            var props = new List<string>();
            var offs = new List<Vector2>();
            foreach (var p in waterScrollTextures)
                if (!string.IsNullOrEmpty(p) && mat.HasProperty(p))
                {
                    props.Add(p);
                    offs.Add(mat.GetTextureOffset(p));
                }

            if (props.Count == 0) { Destroy(mat); return; } // material không có texture nào khớp → khỏi giữ
            _waterMat = mat;
            _waterProps = props.ToArray();
            _waterBaseOffset = offs.ToArray();
        }

        /// <summary>Cuộn UV material mặt đường mỗi frame — texture Tile lặp lại + trôi dọc path = nước chảy.</summary>
        private void ScrollWater()
        {
            if (_waterMat == null) return;
            _waterScroll += waterScrollSpeed * Time.deltaTime;
            for (int i = 0; i < _waterProps.Length; i++)
                _waterMat.SetTextureOffset(_waterProps[i], _waterBaseOffset[i] + _waterScroll);
        }

        /// <summary>Chỉnh độ rộng mặt đường (world units). Gọi được lúc runtime để tinh chỉnh.</summary>
        public void SetPathWidth(float width)
        {
            if (pathLine == null) return;
            pathLine.enabled = width > 0f;
            pathLine.widthMultiplier = Mathf.Max(0f, width);
        }

        /// <summary>
        /// Gun vừa rời slot: vào path ngay nếu điểm đầu còn trống, không thì xếp hàng chờ.
        /// Queue là FIFO — hàng chờ còn người thì gun mới luôn phải đứng sau, kể cả lúc đầu path trống.
        /// </summary>
        public void RequestDeploy(Gun gun)
        {
            if (gun == null) return;
            gun.OnQueued();

            if (_queue.Count == 0 && IsEntryClear()) { Deploy(gun); return; }

            _queue.Add(gun);
            StageQueued(gun);
        }

        private void Update()
        {
            ScrollWater();

            if (_queue.Count == 0 || !IsEntryClear()) return;

            // Mỗi frame chỉ thả 1 gun: gun vừa vào đứng ngay điểm đầu nên IsEntryClear() lập tức false.
            var gun = _queue[0];
            _queue.RemoveAt(0);
            if (gun != null) Deploy(gun);
        }

        private void Deploy(Gun gun)
        {
            _guns.Add(gun);
            gun.OnDeployed();
            // MỌI gun đều vào path từ ĐIỂM ĐẦU (distance = FrontStationDistance, mặc định 0 = pos 0 của
            // path) rồi chạy tới. Khoảng cách giữa các gun do IsEntryClear() bảo đảm, không cộng offset
            // theo lượt deploy nữa.
            gun.DeployOnPath(_path, _frontStationDistance, _gunSpeed); // follower chạy vòng liên tục
        }

        /// <summary>Điểm vào path có gun nào đứng gần hơn _minGunGap không.</summary>
        private bool IsEntryClear()
        {
            if (_guns.Count >= _maxGunOnPath) return false;
            if (_path == null || _minGunGap <= 0f) return true;

            foreach (var g in _guns)
            {
                if (g == null) continue;
                if (ArcGap(_frontStationDistance, g.PathDistance, _path.TotalLength) < _minGunGap) return false;
            }
            return true;
        }

        /// <summary>
        /// Khoảng cách NGẮN NHẤT giữa 2 vị trí trên path, đo cả 2 chiều. Path luôn chạy vòng
        /// (GetPointAtDistance tự Mathf.Repeat) nên gun sắp lượn hết vòng về tới điểm đầu cũng phải tính
        /// là "đang chắn cửa" — không thì thả gun mới đè lên nó.
        /// </summary>
        private static float ArcGap(float a, float b, float total)
        {
            if (total <= 1e-4f) return Mathf.Abs(a - b);
            float d = Mathf.Repeat(b - a, total);
            return Mathf.Min(d, total - d);
        }

        /// <summary>
        /// Gun chờ đứng NGAY TẠI điểm vào path (pos 0) — cả hàng chờ chồng lên nhau ở đúng chỗ đó, tới
        /// lượt ai thì người đó chạy đi. Không xếp lùi dọc path nữa: điểm vào là 0 nên lùi ra sau cho
        /// distance ÂM, GetPointAtDistance wrap nó về CUỐI path — mà đoạn cuối đó là track sống, gun đang
        /// chạy vòng lao thẳng qua hàng chờ.
        /// Vị trí chờ không phụ thuộc thứ tự nên chỉ cần gọi 1 lần lúc gun vào queue.
        /// </summary>
        private void StageQueued(Gun gun)
        {
            if (gun == null || _path == null) return;

            Vector3 pos = _path.GetPointAtDistance(_frontStationDistance);
            gun.MoveTo(pos, queueMoveDuration);

            // Follower đang tắt lúc chờ → tự quay mặt gun theo hướng path để lát nữa vào đường không giật.
            Vector3 dir = _path.GetPointAtDistance(_frontStationDistance + 0.05f) - pos;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-6f)
                gun.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        public void RemoveGun(Gun gun)
        {
            _guns.Remove(gun); // gun khác vẫn chạy loop giữ nguyên khoảng cách — để lại 1 chỗ trống
            _queue.Remove(gun);
        }

        public void Clear()
        {
            _guns.Clear(); // gun trả về pool qua PoolManager.ReturnAll khi rebuild
            _queue.Clear();
            // pathLine nằm trên scene (không bị destroy cùng GunPath) → phải xoá điểm của level cũ.
            if (pathLine != null) pathLine.positionCount = 0;
            if (_waterMat != null) { Destroy(_waterMat); _waterMat = null; }
            if (_path != null) { Destroy(_path.gameObject); _path = null; }
            // Tunnel là con của PathManager (không phải của GunPath) → phải tự dọn, không thì level sau
            // chồng thêm 1 cặp nữa.
            if (_tunnelIn != null) { Destroy(_tunnelIn); _tunnelIn = null; }
            if (_tunnelOut != null) { Destroy(_tunnelOut); _tunnelOut = null; }
        }

        /// <summary>Có gun nào trên path còn cell cùng màu để bắn không (check LOSE).</summary>
        public bool AnyGunHasTarget()
        {
            if (GridBlockManager.Instance == null) return false;
            foreach (var g in _guns)
                // Gun connect hết đạn còn đứng chờ trên path → KHÔNG tính là còn bắn được (nó không nhả đạn nữa).
                if (g != null && g.HasBullets && GridBlockManager.Instance.HasFrontCellOfColor(g.Color)) return true;
            return false;
        }
    }
}

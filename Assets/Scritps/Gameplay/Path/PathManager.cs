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
        [Tooltip("Material hiệu ứng chảy phủ lên mặt đường. PathManager tự tạo một LineRenderer thứ hai dùng chung các điểm path.")]
        [SerializeField] private Material pathFlowMaterial;
        [Tooltip("Nâng lớp dòng chảy lên khỏi mặt nước để tránh z-fighting.")]
        [Min(0f)] [SerializeField] private float flowSurfaceOffset = 0.01f;
        [Header("Bọt nổi")]
        [SerializeField] private bool spawnBubbles = true;
        [SerializeField] private float bubbleSpawnInterval = 0.8f;
        [SerializeField] private float bubbleSize = 0.22f;
        [SerializeField] private float bubbleRiseSpeed = 0.7f;

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

        private RoundedPolylinePath _path;
        private GameObject _tunnelIn, _tunnelOut;
        private readonly List<Gun> _guns = new List<Gun>();    // [0] = gun vào trước nhất
        private readonly List<Gun> _queue = new List<Gun>();   // [0] = gun sẽ vào path kế tiếp
        private float _gunSpeed = 3f;
        private float _minGunGap = 1.2f;     // khoảng cách arc-length tối thiểu giữa 2 gun
        private float _frontStationDistance; // điểm VÀO path của mọi gun (0 = đầu path)
        private int _maxGunOnPath = 5;

        private LineRenderer _flowLine;
        private Material _baseMaterialInstance;
        private Material _flowMaterialInstance;
        private Material _bubbleMaterial;
        private readonly List<Transform> _bubbles = new List<Transform>();
        private float _bubbleTimer;

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

            SetupWaterFlow(path);

            if (path != null && path.samples != null && path.samples.Length >= 2)
            {
                pathLine.positionCount = path.samples.Length;
                pathLine.SetPositions(path.samples);

                if (_flowLine != null)
                {
                    var flowPositions = new Vector3[path.samples.Length];
                    for (int i = 0; i < flowPositions.Length; i++)
                        flowPositions[i] = path.samples[i] + Vector3.up * flowSurfaceOffset;
                    _flowLine.positionCount = flowPositions.Length;
                    _flowLine.SetPositions(flowPositions);
                }
            }
            else
            {
                pathLine.positionCount = 0;
                if (_flowLine != null) _flowLine.positionCount = 0;
            }
        }

        /// <summary>
        /// Tạo lớp LineRenderer trong suốt phủ lên mặt nước. LineRenderer Tile quy ước UV.x chạy dọc path,
        /// vì vậy waterScrollSpeed.x được truyền thẳng cho shader và luôn bám theo mọi khúc cua.
        /// </summary>
        private void SetupWaterFlow(RoundedPolylinePath path)
        {
            if (_baseMaterialInstance != null)
            {
                Destroy(_baseMaterialInstance);
                _baseMaterialInstance = null;
            }
            if (_flowMaterialInstance != null)
            {
                Destroy(_flowMaterialInstance);
                _flowMaterialInstance = null;
            }

            if (pathLine != null && pathLine.sharedMaterial != null)
            {
                _baseMaterialInstance = pathLine.material;
                ApplyPathDirection(_baseMaterialInstance, "_EdgeWaveSpeed");
                if (_baseMaterialInstance.HasProperty("_PathLength"))
                    _baseMaterialInstance.SetFloat("_PathLength", path != null ? path.TotalLength : 1f);
                if (_baseMaterialInstance.HasProperty("_PathClosed"))
                    _baseMaterialInstance.SetFloat("_PathClosed", path != null && path.isClosed ? 1f : 0f);
            }

            if (pathLine == null || pathFlowMaterial == null)
            {
                if (_flowLine != null) _flowLine.enabled = false;
                return;
            }

            if (_flowLine == null)
            {
                var go = new GameObject("WaterFlowLine");
                go.transform.SetParent(pathLine.transform, false);
                _flowLine = go.AddComponent<LineRenderer>();
            }

            CopyLineSettings(pathLine, _flowLine);
            _flowLine.enabled = pathLine.enabled;
            _flowLine.sharedMaterial = pathFlowMaterial;

            _flowMaterialInstance = _flowLine.material;
            if (_flowMaterialInstance.HasProperty("_FlowSpeed"))
                _flowMaterialInstance.SetVector("_FlowSpeed", waterScrollSpeed);
            if (_flowMaterialInstance.HasProperty("_SecondFlowSpeed"))
                _flowMaterialInstance.SetVector("_SecondFlowSpeed", waterScrollSpeed * 0.55f);
            if (_flowMaterialInstance.HasProperty("_BubbleSpeed"))
                _flowMaterialInstance.SetVector("_BubbleSpeed", -waterScrollSpeed * 0.35f);
        }

        private void ApplyPathDirection(Material material, string speedProperty)
        {
            if (material == null || !material.HasProperty(speedProperty)) return;
            float speed = Mathf.Abs(material.GetFloat(speedProperty));
            if (Mathf.Approximately(waterScrollSpeed.x, 0f))
                material.SetFloat(speedProperty, 0f);
            else
                material.SetFloat(speedProperty, speed * Mathf.Sign(waterScrollSpeed.x));
        }

        private static void CopyLineSettings(LineRenderer source, LineRenderer target)
        {
            target.alignment = source.alignment;
            target.useWorldSpace = source.useWorldSpace;
            target.loop = source.loop;
            target.numCornerVertices = source.numCornerVertices;
            target.numCapVertices = source.numCapVertices;
            target.textureMode = LineTextureMode.Tile;
            target.widthCurve = source.widthCurve;
            target.widthMultiplier = source.widthMultiplier;
            target.colorGradient = source.colorGradient;
            target.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            target.receiveShadows = false;
        }

        /// <summary>Chỉnh độ rộng mặt đường (world units). Gọi được lúc runtime để tinh chỉnh.</summary>
        public void SetPathWidth(float width)
        {
            if (pathLine == null) return;
            pathLine.enabled = width > 0f;
            pathLine.widthMultiplier = Mathf.Max(0f, width);
            if (_baseMaterialInstance != null && _baseMaterialInstance.HasProperty("_PathWidth"))
                _baseMaterialInstance.SetFloat("_PathWidth", pathLine.widthMultiplier);
            if (_flowLine != null)
            {
                _flowLine.enabled = pathLine.enabled && pathFlowMaterial != null;
                _flowLine.widthMultiplier = pathLine.widthMultiplier;
            }
        }

        /// <summary>
        /// Gun vừa rời slot: vào path ngay nếu điểm đầu còn trống, không thì xếp hàng chờ.
        /// Queue là FIFO — hàng chờ còn người thì gun mới luôn phải đứng sau, kể cả lúc đầu path trống.
        /// </summary>
        private void SetupBubbles()
        {
            if (!spawnBubbles || pathMaterial == null) return;
            var texture = pathMaterial.HasProperty("_BubbleMap") ? pathMaterial.GetTexture("_BubbleMap") : null;
            var shader = Shader.Find("WaterFlow/2D/Bubble Overlay");
            if (texture == null || shader == null) return;
            if (_bubbleMaterial != null) Destroy(_bubbleMaterial);
            _bubbleMaterial = new Material(shader);
            _bubbleMaterial.SetTexture("_BubbleMap", texture);
        }

        private void UpdateBubbles()
        {
            if (_bubbleMaterial == null || _path == null || _path.samples == null) return;
            _bubbleTimer -= Time.deltaTime;
            if (_bubbleTimer <= 0f)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(go.GetComponent<Collider>());
                go.name = "WaterBubble";
                go.transform.SetParent(transform);
                go.transform.SetPositionAndRotation(_path.GetPointAtDistance(Random.value * _path.TotalLength) + Vector3.up * (flowSurfaceOffset + 0.03f), Quaternion.Euler(90f, 0f, 0f));
                float s = bubbleSize * Random.Range(0.7f, 1.25f);
                go.transform.localScale = new Vector3(s, s, s);
                go.GetComponent<MeshRenderer>().sharedMaterial = _bubbleMaterial;
                _bubbles.Add(go.transform);
                _bubbleTimer = bubbleSpawnInterval * Random.Range(0.7f, 1.3f);
            }
            var cam = Camera.main;
            Vector3 up = cam != null ? Vector3.ProjectOnPlane(cam.transform.up, Vector3.up) : Vector3.forward;
            if (up.sqrMagnitude < 1e-5f) up = Vector3.forward;
            up.Normalize();
            float limit = Mathf.Max(0f, pathLine.widthMultiplier * 0.5f - bubbleSize * 0.5f);
            for (int i = _bubbles.Count - 1; i >= 0; i--)
            {
                var b = _bubbles[i];
                if (b == null) { _bubbles.RemoveAt(i); continue; }
                b.position += up * bubbleRiseSpeed * Time.deltaTime;
                if (DistanceToPath(b.position) > limit) { Destroy(b.gameObject); _bubbles.RemoveAt(i); }
            }
        }

        private float DistanceToPath(Vector3 point)
        {
            float best = float.MaxValue;
            for (int i = 1; i < _path.samples.Length; i++)
            {
                Vector3 a = _path.samples[i - 1]; a.y = 0f;
                Vector3 b = _path.samples[i]; b.y = 0f;
                Vector3 p = point; p.y = 0f;
                Vector3 ab = b - a;
                float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                best = Mathf.Min(best, Vector3.Distance(p, a + ab * t));
            }
            return best;
        }

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
            if (_flowLine != null) _flowLine.positionCount = 0;
            foreach (var bubble in _bubbles) if (bubble != null) Destroy(bubble.gameObject);
            _bubbles.Clear();
            if (_bubbleMaterial != null) { Destroy(_bubbleMaterial); _bubbleMaterial = null; }
            if (_baseMaterialInstance != null) { Destroy(_baseMaterialInstance); _baseMaterialInstance = null; }
            if (_flowMaterialInstance != null) { Destroy(_flowMaterialInstance); _flowMaterialInstance = null; }
            if (_path != null) { Destroy(_path.gameObject); _path = null; }
            // Tunnel là con của PathManager (không phải của GunPath) → phải tự dọn, không thì level sau
            // chồng thêm 1 cặp nữa.
            if (_tunnelIn != null) { Destroy(_tunnelIn); _tunnelIn = null; }
            if (_tunnelOut != null) { Destroy(_tunnelOut); _tunnelOut = null; }
        }

        /// <summary>Có gun nào trên path còn cell cùng màu để bắn không (check LOSE).</summary>
        public bool AnyGunHasTarget()
        {
            var grid = GridBlockManager.Instance;
            if (grid == null) return false;

            foreach (var g in _guns)
                // Lose is a board-level deadlock check, not a per-frame aiming
                // check. If any shootable cell in any grid matches a gun that
                // still has ammo, allow the gun to keep moving until it reaches it.
                if (g != null && g.HasBullets && grid.HasFrontCellOfColor(g.Color)) return true;
            return false;
        }
    }
}

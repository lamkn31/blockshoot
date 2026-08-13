using System.Collections.Generic;
using UnityEngine;
using Action = System.Action;

namespace Wayfu.Lamkn
{
    /// <summary>
    /// Dựng đường path (RoundedPolylinePath + pipe/water mesh uốn theo path) từ LevelData và quản lý các gun
    /// chạy trên đó. Mọi gun vào path tại ĐIỂM ĐẦU (FrontStationDistance, mặc định 0) rồi chạy loop liên
    /// tục bằng RoundedPolylineFollower. Gun chỉ được vào khi điểm đầu còn trống ít nhất
    /// GameSettings.GunSpacing; chưa đủ thì ĐỨNG CHỜ ngay tại điểm đầu (pos 0) cho tới lượt.
    /// Thua khi đủ MaxGunOnPath mà không gun nào bắn được.
    /// </summary>
    public class PathManager : Singleton<PathManager>
    {
        [Header("Path mesh")]
        [Tooltip("FBX chứa hai child MeshFilter tên pipe và water. Cả hai được bẻ theo cùng một path.")]
        [SerializeField] private GameObject tubeModel;
        [SerializeField] private string pipeMeshName = "pipe";
        [SerializeField] private string waterMeshName = "water";
        [SerializeField] private Material pipeMaterial;
        [SerializeField] private Material waterMaterial;
        [Tooltip("Trục dài của piece trong FBX. tube_test được author theo Z.")]
        [SerializeField] private Vector3 modelRotation = Vector3.zero;
        [Min(0.001f)] [SerializeField] private float meshLengthScale = 1f;
        [Tooltip("Chỉ pipe: lấy mỗi N mẫu cong để bo góc. Giá trị lớn hơn tạo ít vertices/triangles hơn.")]
        [Min(1)] [SerializeField] private int pipeCornerSampleStep = 4;
        [Min(0f)] [SerializeField] private float waterSurfaceOffset = 0.01f;
        [Min(0f)] [SerializeField] private float entryRevealDistance = 0.2f;
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
        [Tooltip("Tốc độ (world units/giây) gun bay từ slot ra chỗ đứng đợi trong quạt.")]
        [Min(0.01f)] [SerializeField] private float slotToEntrySpeed = 8f;

        [Header("Đám đông chờ vào path (cluster)")]
        [Tooltip("Khoảng cách giữa 2 gun kề trong đám đông (fallback nếu GameSettings.WaitClusterSpacing = 0). " +
                 "Ưu tiên chỉnh ở GameSettings để dùng chung mọi map.")]
        [Min(0.1f)] [SerializeField] private float clusterSpacing = 1.5f;
        [Tooltip("Biên độ xê dịch NGẪU NHIÊN mỗi gun quanh anchor (world units) — phá thế thẳng hàng cho tự nhiên.")]
        [Min(0f)] [SerializeField] private float clusterJitter = 0.28f;
        [Tooltip("Khoảng cách TÂM-TÂM tối thiểu giữa 2 gun chờ (≈ đường kính gun). Gun tới gần hơn số này sẽ " +
                 "ĐẨY nhau ra thay vì xuyên qua. Nên nhỏ hơn Wait Cluster Spacing để lúc đứng yên không rung.")]
        [Min(0f)] [SerializeField] private float crowdMinSeparation = 0.85f;

        [Header("Nước chảy")]
        [Tooltip("Tốc độ cuộn UV material mặt đường để tạo hiệu ứng nước chảy. X = dọc theo path (chiều " +
                 "chảy), Y = ngang. (0,0) = tắt hiệu ứng. Đảo dấu X để chảy ngược.")]
        [SerializeField] private Vector2 waterScrollSpeed = new Vector2(-0.5f, 0f);

        private RoundedPolylinePath _path;
        private GameObject _tunnelIn, _tunnelOut;
        private readonly List<Gun> _guns = new List<Gun>();    // [0] = gun vào trước nhất
        private readonly List<Gun> _queue = new List<Gun>();   // đám đông chờ (thứ tự tới); vào path = GẦN CỬA nhất trước
        private readonly Dictionary<Gun, Vector2> _queueJitter = new Dictionary<Gun, Vector2>(); // xê dịch cố định/gun
        private readonly Dictionary<Gun, Vector3> _queueTarget = new Dictionary<Gun, Vector3>();  // anchor+jitter hiện tại
        private readonly HashSet<Gun> _queueArrived = new HashSet<Gun>();                         // đã bay tới vùng chờ (đủ đk vào path)
        private float _gunSpeed = 3f;
        private float _minGunGap = 1.2f;     // khoảng cách arc-length tối thiểu giữa 2 gun
        private float _frontStationDistance; // điểm VÀO path của mọi gun (0 = đầu path)
        private int _maxGunOnPath = 5;
        private float _clusterSpacing = 0.75f; // khoảng cách gun trong đám đông chờ (nạp từ GameSettings lúc Build)

        // ===== Gate điều phối cửa vào path (path_0) =====
        // Mỗi lúc chỉ 1 gun được TRANSIT (GoOut) qua cửa. Gun loop muốn tái xuất (_emerge) chỉ nhường các
        // gun slot ĐANG đợi lúc nó xin (snapshot WaitFor) — nhường xong nhóm đó là ra ngay, gun slot tới
        // SAU xếp phía sau nó (không để dòng gun slot mới chen liên tục làm gun loop kẹt mãi ở cửa).
        // The last gun leaves its slot before it is staged in _queue, so this
        // multiplier applies consistently to slot -> queue, queue -> path_0,`n        // and path movement (including hold-screen speed boost).
        private float MovementSpeedMultiplier
        {
            get
            {
                var settings = GameSettings.Instance;
                if (settings == null) return 1f;

                float multiplier = SlotManager.IsActive && SlotManager.Instance.AreAllSlotsEmpty
                    ? Mathf.Max(1f, settings.EndgameSpeedMultiplier)
                    : 1f;
                if (IsFull)
                    multiplier *= Mathf.Max(1f, settings.FullPathSpeedMultiplier);
                if (GameController.IsActive && GameController.Instance.IsHoldScreenSpeedBoostActive)
                    multiplier *= Mathf.Max(1f, settings.HoldScreenSpeedMultiplier);
                return multiplier;
            }
        }

        private Gun _gateGun;                                             // gun đang transit; null = cửa rảnh
        private readonly List<EmergeReq> _emerge = new List<EmergeReq>(); // gun loop ẩn ở cửa chờ tái xuất (FIFO)
        private readonly HashSet<Gun> _emergeWaiting = new HashSet<Gun>();// gun đang ẩn chờ → IsEntryClear bỏ qua

        /// <summary>
        /// Một yêu cầu tái xuất của gun loop. WaitFor = ảnh chụp các gun slot đang đợi lúc gun này xin;
        /// nó chỉ phải nhường đúng nhóm đó, xong là được ra (EmergeReady).
        /// </summary>
        private class EmergeReq { public Gun Gun; public Action OnGranted; public HashSet<Gun> WaitFor; }

        private Mesh _pipePathMesh;
        private Mesh _waterPathMesh;
        private MeshFilter _pipeFilter;
        private MeshFilter _waterFilter;
        private MeshRenderer _pipeRenderer;
        private MeshRenderer _waterRenderer;
        private Material _waterMaterialInstance;
        private float _pathWidth;
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

        /// <summary>Add every gun owned by the current path/queue/gate state.</summary>
        public void CollectRuntimeGuns(HashSet<Gun> result)
        {
            if (result == null) return;
            foreach (var gun in _guns)
                if (gun != null && !gun.IsDead) result.Add(gun);
            foreach (var gun in _queue)
                if (gun != null && !gun.IsDead) result.Add(gun);
            if (_gateGun != null && !_gateGun.IsDead) result.Add(_gateGun);
            foreach (var req in _emerge)
                if (req?.Gun != null && !req.Gun.IsDead) result.Add(req.Gun);
        }
        /// <summary>Độ nâng của gun/queue so với đường tâm path, khớp mặt nước đã dựng.</summary>
        public float GunSurfaceOffset => waterSurfaceOffset;
        /// <summary>Vị trí miệng TunnelIn; path kín/fallback dùng điểm vào path trên mặt nước.</summary>
        public Vector3 TunnelInPosition => _tunnelIn != null ? _tunnelIn.transform.position : PathSurfacePoint(_frontStationDistance);
        /// <summary>Hướng miệng TunnelIn; PooledFx cộng Euler offset riêng lên rotation này.</summary>
        public Quaternion TunnelInRotation => _tunnelIn != null ? _tunnelIn.transform.rotation : TunnelInFallbackRotation();

        /// <summary>Dựng path từ level rồi nạp config gun. Gọi thay cho Init(path) cũ.</summary>
        public void Build(LevelData level)
        {
            Clear();

            var gs = GameSettings.Instance;
            _gunSpeed = gs != null ? gs.GunSpeed : 3f;
            _maxGunOnPath = gs != null ? gs.MaxGunOnPath : 5;
            _frontStationDistance = gs != null ? gs.FrontStationDistance : 0f;
            _minGunGap = gs != null ? Mathf.Max(0f, gs.GunSpacing) : 1.2f;
            _pathWidth = gs != null ? Mathf.Max(0f, gs.PathWidth) : 1.5f;
            // Khoảng cách gun trong đám đông chờ: GameSettings > 0 thì đè cấu hình Inspector của PathManager.
            _clusterSpacing = gs != null && gs.WaitClusterSpacing > 0f ? gs.WaitClusterSpacing : clusterSpacing;

            _path = CreatePath(level);
            ApplyPathMeshes(_path);
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

        // Bẻ từng mesh source thành các tile dọc theo path. Pipe và water dùng cùng samples,
        // nhưng kết quả là hai MeshRenderer nên có material/shader hoàn toàn độc lập.
        private void ApplyPathMeshes(RoundedPolylinePath path)
        {
            EnsurePathRenderers();
            if (path == null || path.samples == null || path.samples.Length < 2 || tubeModel == null) return;

            var pipeSource = FindSourceMesh(pipeMeshName);
            var waterSource = FindSourceMesh(waterMeshName);
            if (waterSource == null)
            {
                Debug.LogWarning("[PathManager] tubeModel must contain a MeshFilter named water.", this);
                return;
            }

            DestroyPathMeshes();
            _waterPathMesh = BuildBentMesh(path, waterSource, "PathWater", waterSurfaceOffset, stretchAtCorners: false);
            _waterFilter.sharedMesh = _waterPathMesh;
            if (_pipeRenderer != null) _pipeRenderer.enabled = false;
            _waterRenderer.sharedMaterial = waterMaterial != null ? waterMaterial : waterSource.GetComponent<MeshRenderer>()?.sharedMaterial;
            SetupWaterFlow(path);
        }

        private void EnsurePathRenderers()
        {
            if (_pipeFilter == null) _pipeFilter = CreatePathRenderer("PathPipe", out _pipeRenderer);
            if (_waterFilter == null) _waterFilter = CreatePathRenderer("PathWater", out _waterRenderer);
        }

        private MeshFilter CreatePathRenderer(string name, out MeshRenderer renderer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var filter = go.AddComponent<MeshFilter>();
            renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return filter;
        }

        private MeshFilter FindSourceMesh(string meshName)
        {
            var filters = tubeModel.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
                if (filters[i].sharedMesh != null && string.Equals(filters[i].name, meshName, System.StringComparison.OrdinalIgnoreCase)) return filters[i];
            return null;
        }

        private Mesh BuildBentMesh(RoundedPolylinePath path, MeshFilter sourceFilter, string meshName,
                                   float yOffset = 0f, bool stretchAtCorners = false)
        {
            Mesh source = sourceFilter != null ? sourceFilter.sharedMesh : null;
            if (source == null) return null;
            var sourceVertices = source.vertices;
            var sourceNormals = source.normals;
            var sourceUvs = source.uv;
            // The FBX child nodes can carry their own import scale/rotation (tube_test does).
            // Bake that node transform before bending so both pipe and water retain the authored profile.
            Matrix4x4 sourceToModel = tubeModel.transform.worldToLocalMatrix * sourceFilter.transform.localToWorldMatrix;
            // Normalize the authored piece to the same convention as the reference renderer:
            // longest local axis = path direction (Z), shortest = up (Y).
            Vector3 sourceSize = Vector3.zero;
            Vector3 sourceMin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 sourceMax = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < sourceVertices.Length; i++)
            {
                Vector3 p = sourceToModel.MultiplyPoint3x4(sourceVertices[i]);
                sourceMin = Vector3.Min(sourceMin, p); sourceMax = Vector3.Max(sourceMax, p);
            }
            sourceSize = sourceMax - sourceMin;
            int longAxis = 0, shortAxis = 1;
            if (sourceSize[1] > sourceSize[longAxis]) longAxis = 1;
            if (sourceSize[2] > sourceSize[longAxis]) longAxis = 2;
            if (sourceSize[2] < sourceSize[shortAxis]) shortAxis = 2;
            if (sourceSize[0] < sourceSize[shortAxis]) shortAxis = 0;
            Quaternion canonical = Quaternion.Inverse(Quaternion.LookRotation(AxisVector(longAxis), AxisVector(shortAxis)));
            Quaternion sourceRotation = Quaternion.Euler(modelRotation) * canonical;
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            for (int i = 0; i < sourceVertices.Length; i++)
            {
                Vector3 p = sourceRotation * sourceToModel.MultiplyPoint3x4(sourceVertices[i]);
                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            }
            // Keep the authored longitudinal size.  The old implementation remapped every
            // source piece to its tile interval, which stretched/compressed it whenever the
            // path length was not an exact multiple of the FBX piece length.  meshLengthScale
            // is intentionally not applied here: path fitting is done by clipping the final
            // piece, never by changing its aspect ratio.
            float pieceLength = Mathf.Max(0.0001f, maxZ - minZ);
            float crossScale = _pathWidth > 0f ? _pathWidth / Mathf.Max(0.0001f, maxX - minX) : 1f;
            // Pipe can add short, stretchable pieces at curve samples for a round silhouette.
            // Water intentionally keeps its authored longitudinal scale everywhere.
            var tileEnds = new List<float> { 0f, path.TotalLength };
            for (float d = pieceLength; d < path.TotalLength; d += pieceLength) tileEnds.Add(d);
            if (stretchAtCorners && path.sampleArc != null)
            {
                // The path stores many samples for movement precision. Rendering only needs a
                // subset: using every fourth sample keeps corners visually round while avoiding
                // a full source-mesh copy for every single path sample.
                int sampleStep = Mathf.Max(1, pipeCornerSampleStep);
                for (int i = sampleStep; i < path.sampleArc.Length - 1; i += sampleStep)
                    tileEnds.Add(path.sampleArc[i]);
            }
            tileEnds.Sort();

            const float minTileLength = 0.0001f;
            for (int tile = 0; tile < tileEnds.Count - 1; tile++)
            {
                float d0 = tileEnds[tile];
                float d1 = tileEnds[tile + 1];
                if (d1 - d0 < minTileLength) continue;
                int baseIndex = vertices.Count;
                for (int i = 0; i < sourceVertices.Length; i++)
                {
                    Vector3 local = sourceRotation * sourceToModel.MultiplyPoint3x4(sourceVertices[i]);
                    float longitudinalT = (local.z - minZ) / pieceLength;
                    float distance = stretchAtCorners
                        ? Mathf.Lerp(d0, d1, longitudinalT)
                        : Mathf.Clamp(d0 + (local.z - minZ), 0f, path.TotalLength);
                    float t = path.TotalLength > 1e-5f ? distance / path.TotalLength : 0f;
                    Vector3 center = path.Evaluate(t);
                    Quaternion rotation = path.Tangent(t);
                    vertices.Add(transform.InverseTransformPoint(center + rotation * new Vector3(local.x * crossScale, local.y + yOffset, 0f)));
                    Vector3 normal = sourceNormals != null && sourceNormals.Length == sourceVertices.Length ? sourceRotation * sourceToModel.MultiplyVector(sourceNormals[i]) : Vector3.up;
                    normals.Add(transform.InverseTransformDirection(rotation * normal).normalized);
                    uvs.Add(sourceUvs != null && sourceUvs.Length == sourceVertices.Length ? sourceUvs[i] : Vector2.zero);
                }
                var sourceTriangles = source.triangles;
                for (int i = 0; i < sourceTriangles.Length; i++) triangles.Add(baseIndex + sourceTriangles[i]);
            }
            var mesh = new Mesh { name = meshName, indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uvs); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 AxisVector(int axis) => axis == 0 ? Vector3.right : (axis == 1 ? Vector3.up : Vector3.forward);

        // Gun movement intentionally wraps even on an open path. Rendering must not: its last
        // tile has to terminate at the last sample instead of bending back to sample zero.
        private static Vector3 GetMeshPoint(RoundedPolylinePath path, float distance)
        {
            if (!path.isClosed && distance >= path.TotalLength) return path.samples[path.samples.Length - 1];
            return path.GetPointAtDistance(distance);
        }

        private void SetupWaterFlow(RoundedPolylinePath path)
        {
            if (_waterMaterialInstance != null) Destroy(_waterMaterialInstance);
            if (_waterRenderer == null || _waterRenderer.sharedMaterial == null) return;
            _waterMaterialInstance = _waterRenderer.material;
            ApplyPathDirection(_waterMaterialInstance, "_EdgeWaveSpeed");
            if (_waterMaterialInstance.HasProperty("_FlowSpeed")) _waterMaterialInstance.SetVector("_FlowSpeed", waterScrollSpeed);
            if (_waterMaterialInstance.HasProperty("_PathLength")) _waterMaterialInstance.SetFloat("_PathLength", path.TotalLength);
            if (_waterMaterialInstance.HasProperty("_PathClosed")) _waterMaterialInstance.SetFloat("_PathClosed", path.isClosed ? 1f : 0f);
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

        /// <summary>Chỉnh độ rộng mặt đường (world units). Gọi được lúc runtime để tinh chỉnh.</summary>
        public void SetPathWidth(float width)
        {
            float newWidth = Mathf.Max(0f, width);
            bool geometryChanged = !Mathf.Approximately(_pathWidth, newWidth);
            _pathWidth = newWidth;
            if (geometryChanged && _path != null) ApplyPathMeshes(_path);
            if (_waterRenderer != null) _waterRenderer.enabled = _pathWidth > 0f;
            if (_waterMaterialInstance != null && _waterMaterialInstance.HasProperty("_PathWidth"))
                _waterMaterialInstance.SetFloat("_PathWidth", _pathWidth);
        }

        /// <summary>
        /// Gun vừa rời slot: vào path ngay nếu điểm đầu còn trống, không thì xếp hàng chờ.
        /// Queue là FIFO — hàng chờ còn người thì gun mới luôn phải đứng sau, kể cả lúc đầu path trống.
        /// </summary>
        private void SetupBubbles()
        {
            if (!spawnBubbles || _waterMaterialInstance == null) return;
            var texture = _waterMaterialInstance.HasProperty("_BubbleMap") ? _waterMaterialInstance.GetTexture("_BubbleMap") : null;
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
                go.transform.SetPositionAndRotation(_path.GetPointAtDistance(Random.value * _path.TotalLength) + Vector3.up * (waterSurfaceOffset + 0.03f), Quaternion.Euler(90f, 0f, 0f));
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
            float limit = Mathf.Max(0f, _pathWidth * 0.5f - bubbleSize * 0.5f);
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

        /// <summary>
        /// Gun vừa rời slot: bay THẲNG tới điểm chờ NGOÀI cửa tunnel rồi xếp vào _queue. Việc transit thật
        /// (teleport về pos 0 + GoOut) do ServiceGate() điều phối theo lượt — xem quy tắc ưu tiên ở đó.
        /// </summary>
        public void RequestDeploy(Gun gun)
        {
            if (gun == null) return;

            // No waiting guns and an open entry gate: skip the staging crowd entirely.
            // DeployOnPath(playEmerge:false) moves this gun directly from its current
            // slot position to path_0, then hands control to the path follower.
            if (_queue.Count == 0 && _gateGun == null && _path != null
                && _guns.Count < _maxGunOnPath && IsEntrySpacingClear())
            {
                BeginSlotTransit(gun);
                return;
            }

            RequestDeployGroup(new[] { gun });
        }

        /// <summary>
        /// Adds an already ordered set of guns to the entry queue as one transaction.
        /// This is used by CONNECT groups so the queue never observes an intermediate
        /// one-gun state before every member has been assigned its final rank.
        /// </summary>
        public void RequestDeployGroup(IList<Gun> guns)
        {
            if (guns == null || guns.Count == 0) return;

            foreach (var gun in guns)
            {
                if (gun == null || _queue.Contains(gun)) continue;
                gun.OnQueued();
                // Xê dịch cố định của gun (hướng trong đĩa đơn vị) → mỗi lần restage vẫn giữ đúng "cá tính" chỗ đứng,
                // không nhảy loạn mỗi frame. Nhân clusterJitter lúc dùng để chỉnh biên độ được runtime.
                if (!_queueJitter.ContainsKey(gun)) _queueJitter[gun] = Random.insideUnitCircle;
                _queue.Add(gun);
            }
            RestageQueue(); // stage the completed batch once, preserving its supplied FIFO order
        }

        /// <summary>
        /// Gun loop chạy hết vòng, ẩn ở cửa và XIN tái xuất. Chưa gọi lại onGranted ngay: nếu còn gun slot
        /// đang chờ (_queue) thì gun này phải đợi cho slot vào hết. ServiceGate() gọi onGranted khi tới lượt.
        /// </summary>
        public void RequestEmerge(Gun gun, Action onGranted)
        {
            if (gun == null) { onGranted?.Invoke(); return; }
            RequestEmergeGroup(new[] { gun }, grantedGun => onGranted?.Invoke());
        }

        /// <summary>
        /// Xếp cả nhóm CONNECT tái xuất thành một batch liên tiếp. Mọi member dùng
        /// cùng snapshot queue nên gun khác không thể chen vào giữa nhóm; ServiceGate
        /// vẫn cấp từng gun và giữ đúng GunSpacing tại path_0.
        /// </summary>
        public void RequestEmergeGroup(IList<Gun> guns, System.Action<Gun> onGranted)
        {
            if (guns == null || guns.Count == 0) return;

            var waitFor = new HashSet<Gun>(_queue);
            foreach (var gun in guns)
            {
                if (gun == null || gun.IsDead || _emergeWaiting.Contains(gun)) continue;
                _emergeWaiting.Add(gun);
                _emerge.Add(new EmergeReq
                {
                    Gun = gun,
                    OnGranted = () => onGranted?.Invoke(gun),
                    WaitFor = new HashSet<Gun>(waitFor)
                });
            }
        }

        private void Update()
        {
            SimulateQueueCrowd(); // gun chờ tự bay tới chỗ đứng + ĐẨY nhau (không xuyên qua) trước khi xét cửa
            ServiceGate();
        }

        /// <summary>
        /// Mô phỏng đám đông chờ mỗi frame: mỗi gun (1) bay tới anchor của nó ở tốc độ slotToEntrySpeed, và
        /// (2) bị ĐẨY khỏi mọi gun chờ khác đang lại gần hơn crowdMinSeparation → các gun chen nhau ra thay vì
        /// xuyên qua. Thay cho MoveTo cũ (lerp thẳng, không va chạm). Chỉ tác động gun đang trong _queue; gun
        /// đã transit / trên path do follower lo.
        /// </summary>
        private void SimulateQueueCrowd()
        {
            int n = _queue.Count;
            if (n == 0) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float maxStep = slotToEntrySpeed * MovementSpeedMultiplier * dt;
            float sep = crowdMinSeparation;
            Vector3 entrance = PathSurfacePoint(_frontStationDistance);

            for (int i = 0; i < n; i++)
            {
                var gun = _queue[i];
                if (gun == null) continue;
                Vector3 pos = gun.transform.position;

                // (1) Bay tới anchor (giới hạn bước = tốc độ chờ) — tới nơi thì bước nhỏ dần rồi dừng.
                Vector3 seek = Vector3.zero;
                bool hasTarget = _queueTarget.TryGetValue(gun, out var target);
                if (hasTarget)
                {
                    Vector3 toTarget = target - pos; toTarget.y = 0f;
                    seek = Vector3.ClampMagnitude(toTarget, maxStep);
                }

                // (2) Đẩy khỏi các gun chờ khác đang chồng lên (separation kiểu boids).
                Vector3 push = Vector3.zero;
                if (sep > 0f)
                    for (int j = 0; j < n; j++)
                    {
                        if (j == i) continue;
                        var o = _queue[j];
                        if (o == null) continue;
                        Vector3 d = pos - o.transform.position; d.y = 0f;
                        float dist = d.magnitude;
                        if (dist > 1e-4f) { if (dist < sep) push += d / dist * (sep - dist); }
                        else push += new Vector3(Random.value - 0.5f, 0f, Random.value - 0.5f); // trùng khít → tách ngẫu nhiên
                    }
                push = Vector3.ClampMagnitude(push, maxStep);

                Vector3 moved = pos + seek + push;

                // Đánh dấu "đã tới vùng chờ" LẦN ĐẦU gun lại gần chỗ đứng (rộng tay 1 spacing) → từ đó đủ điều
                // kiện vào path DÙ sau này bị chen xô (không cần đứng im đúng anchor) và bị kẹp trong vùng. Gun
                // còn ĐANG BAY từ slot (xa chỗ đứng) thì CHƯA kẹp → bay mượt, không teleport vào vùng.
                bool arrived = _queueArrived.Contains(gun);
                if (!arrived && hasTarget && (moved - target).sqrMagnitude <= _clusterSpacing * _clusterSpacing)
                { _queueArrived.Add(gun); arrived = true; }

                Vector3 next = arrived ? ClampToWait(moved) : moved; // chỉ gun đã vào vùng mới bị kẹp biên
                // Queue cũng đứng trên mặt nước. Chỉ các gun đã rời slot mới đi
                // qua đây, nên không ảnh hưởng độ cao gun trong GunSlot.
                next.y = hasTarget ? target.y : pos.y;
                gun.transform.position = next;

                // Quay mặt VỀ CỬA (pos 0) — chờ hướng về đường, vào là chạy thẳng theo tiếp tuyến path.
                Vector3 face = entrance - next; face.y = 0f;
                if (face.sqrMagnitude > 1e-6f)
                    gun.transform.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);
            }
        }

        /// <summary>
        /// Điều phối cửa vào path mỗi frame. Chỉ 1 gun transit tại một thời điểm (_gateGun). Khi cửa rảnh:
        /// gun loop tái xuất được ra ngay khi đã nhường xong nhóm gun slot trong snapshot của nó
        /// (EmergeReady); ngoài ra ưu tiên gun slot đang chờ ở đầu hàng.
        /// </summary>
        private void ServiceGate()
        {
            if (_path == null) return;

            // Còn gun đang transit (đang chơi GoOut ở cửa) → đợi nó xong hẳn.
            if (_gateGun != null)
            {
                if (_gateGun.IsOnPath && _gateGun.PathEntryAnimating) return;
                _gateGun = null;
            }

            if (!IsEntrySpacingClear()) return; // gun vừa transit chưa chạy đủ xa → giữ khoảng cách, chưa mở lượt kế

            // Gun loop tái xuất đã nhường XONG nhóm gun slot mà nó phải nhường (snapshot lúc xin) → cho ra
            // NGAY, kể cả khi có gun slot MỚI tới sau đang đợi (chúng xếp sau gun loop này). Đây là chỗ sửa
            // starvation: trước đây gun loop nhường mọi gun slot nên bị kẹt mãi khi slot chen liên tục.
            if (_emerge.Count > 0 && EmergeReady(_emerge[0]))
            {
                GrantEmerge();
                return;
            }

            // Gun slot đang chờ ngoài cửa. FIFO: gun CLICK TRƯỚC (đầu hàng _queue[0]) vào path TRƯỚC — nó là gun
            // sớm nhất nên đứng RANK 0 (đầu) trong CỘT của mình, vào thẳng, không nhường gun sau.
            // Không ảnh hưởng ưu tiên gun loop: EmergeReady xét theo TẬP (mọi gun snapshot phải rời _queue).
            while (_queue.Count > 0 && _queue[0] == null) _queue.RemoveAt(0); // dọn ô null ở đầu hàng
            if (_queue.Count > 0)
            {
                // Sức chứa CHỈ áp cho gun MỚI: path đầy thì gun slot đợi (thường CanAcceptCount đã chặn từ
                // lúc click). Gun tái xuất KHÔNG kiểm cái này vì nó đã nằm sẵn trong _guns.
                if (_guns.Count >= _maxGunOnPath) return;

                var gun = _queue[0];
                // Đầu hàng còn đang BAY từ slot vào vùng chờ → ĐỢI nó tới, KHÔNG cho gun sau chen lên (giữ đúng
                // thứ tự click). Cờ _queueArrived bền nên gun đã vào vùng vẫn đủ điều kiện dù đang bị chen xô.
                if (!_queueArrived.Contains(gun)) return;

                _queue.RemoveAt(0);
                _queueJitter.Remove(gun);
                _queueTarget.Remove(gun);
                _queueArrived.Remove(gun);
                BeginSlotTransit(gun);
                RestageQueue(); // đầu hàng đã vào → cả hàng dồn LÊN 1 chỗ, gun kế chen vào vị trí đầu gần cửa
                return;
            }

            // Không còn gun slot nào chờ → cho gun loop ra (barrier coi như đã thoả).
            if (_emerge.Count > 0) GrantEmerge();
        }

        /// <summary>
        /// Gun loop đã tới lượt ra chưa: mọi gun slot trong snapshot WaitFor của nó đã rời hàng chờ (đã vào
        /// path hoặc bị hủy) chưa. Gun slot tới SAU không nằm trong snapshot nên không cản.
        /// </summary>
        private bool EmergeReady(EmergeReq req)
        {
            if (req == null) return false;
            if (req.WaitFor == null || req.WaitFor.Count == 0) return true;
            foreach (var g in _queue) if (req.WaitFor.Contains(g)) return false;
            return true;
        }

        /// <summary>Gun slot được vào: teleport về pos 0, hiện hình rồi bật follower NGAY (không GoOut).</summary>
        private void BeginSlotTransit(Gun gun)
        {
            if (gun == null) return;
            _gateGun = gun;
            _guns.Add(gun);
            gun.OnDeployed();
            gun.SetHiddenDuringPathEntry(false);
            // MỌI gun đều vào path từ ĐIỂM ĐẦU (FrontStationDistance, mặc định 0). Khoảng cách giữa các gun
            // do IsEntrySpacingClear() bảo đảm. playEmerge:false → gun slot KHÔNG chơi hiệu ứng GoOut.
            // entryMoveSpeed = slotToEntrySpeed: đoạn trượt vào pos 0 chạy nhanh liền mạch với lúc bay ra
            // khỏi slot, không bò chậm ở tốc độ path rồi mới vào (tránh cảm giác đứng đợi ở cửa).
            gun.DeployOnPath(_path, _frontStationDistance, _gunSpeed, playEmerge: false,
                entryMoveSpeed: slotToEntrySpeed * MovementSpeedMultiplier);
        }

        /// <summary>Mở cửa cho gun loop đầu hàng tái xuất: nó tự hiện hình + GoOut trong callback.</summary>
        private void GrantEmerge()
        {
            var req = _emerge[0];
            _emerge.RemoveAt(0);
            _emergeWaiting.Remove(req.Gun);
            if (req.Gun == null) return;
            _gateGun = req.Gun; // giữ cửa tới khi nó chơi xong GoOut (PathEntryAnimating về false)
            req.OnGranted?.Invoke();
        }

        private System.Collections.IEnumerator RevealGunAfterEntry(Gun gun, float startDistance)
        {
            if (gun == null) yield break;
            float revealAt = startDistance + entryRevealDistance;
            while (gun != null && gun.IsOnPath && gun.LapCount == 0 && gun.PathDistance < revealAt)
                yield return null;
            if (gun != null) gun.SetHiddenDuringPathEntry(false);
        }

        /// <summary>
        /// Cửa vào (pos 0) có đủ khoảng cách _minGunGap để 1 gun transit không — CHỈ xét spacing, KHÔNG
        /// xét sức chứa (sức chứa chỉ áp cho gun mới, kiểm riêng trong ServiceGate). Gun đang ẩn chờ tái
        /// xuất bị bỏ qua vì nó không thật sự chắn track.
        /// </summary>
        private bool IsEntrySpacingClear()
        {
            if (_path == null || _minGunGap <= 0f) return true;

            foreach (var g in _guns)
            {
                if (g == null) continue;
                // Gun đang TRANSIT qua hầm không chiếm track gần pos 0 theo nghĩa vật lý (nó ở trong hầm):
                //  • GoIn ở CUỐI path: follower tắt nên PathDistance đóng băng ~cuối, mà ArcGap coi cuối wrap
                //    về pos 0 → sẽ khoá oan cửa dù A chỉ đang biến mất ở cuối.
                //  • ẩn chờ tái xuất ở cửa (_emergeWaiting) hoặc GoOut vào: cũng không tính là chắn.
                // → Bỏ qua mọi gun PathEntryAnimating (bao trùm luôn _emergeWaiting) để không khoá cửa oan.
                if (g.PathEntryAnimating || _emergeWaiting.Contains(g)) continue;
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
        /// Khung vùng chờ để pack + kẹp đám đông. <paramref name="near"/> = tâm cạnh GẦN cửa, <paramref name="depthDir"/>
        /// = vào SÂU trong vùng, <paramref name="widthDir"/> = ngang, <paramref name="half"/> = nửa bề rộng,
        /// <paramref name="depth"/> = chiều sâu vùng, <paramref name="lateral"/> = vị trí ngang của CỬA path chiếu lên
        /// cạnh gần (đám đông dồn về phía này cho gần path). ƯU TIÊN vùng VẼ TRÊN MAP; map chưa vẽ → khung quanh cửa
        /// path, KHÔNG biên (half/depth = ∞) → gun bám cửa như fallback cũ. Trả false nếu không có biên.
        /// </summary>
        private bool WaitFrame(out Vector3 near, out Vector3 depthDir, out Vector3 widthDir,
                               out float half, out float depth, out float lateral)
        {
            Vector3 p0 = _path != null ? _path.GetPointAtDistance(_frontStationDistance) : Vector3.zero;

            var map = MapController.IsActive ? MapController.Instance.CurrentMapScript : null;
            if (map != null && map.GetWaitBasis(out near, out depthDir, out widthDir, out float width, out depth))
            {
                half = width * 0.5f;
                lateral = Mathf.Clamp(Vector3.Dot(p0 - near, widthDir), -half, half); // chiếu cửa lên cạnh gần
                return true;
            }

            near = p0;
            half = float.PositiveInfinity; depth = float.PositiveInfinity; lateral = 0f;
            Vector3 tangent = _path != null ? _path.GetPointAtDistance(_frontStationDistance + 0.1f) - p0 : Vector3.forward;
            tangent.y = 0f;
            depthDir = tangent.sqrMagnitude > 1e-6f ? -tangent.normalized : Vector3.forward; // ngược dòng, ra ngoài cửa
            widthDir = Vector3.Cross(Vector3.up, depthDir);
            return false;
        }

        /// <summary>Kẹp 1 điểm vào TRONG hình chữ nhật vùng chờ (chỉ khi có biên) — không cho gun tràn ra ngoài.</summary>
        private Vector3 ClampToWait(Vector3 pos)
        {
            if (!WaitFrame(out Vector3 near, out Vector3 depthDir, out Vector3 widthDir, out float half, out float depth, out _))
                return pos; // không có biên (map chưa vẽ vùng)
            Vector3 rel = pos - near;
            float w = Mathf.Clamp(Vector3.Dot(rel, widthDir), -half, half);
            float d = Mathf.Clamp(Vector3.Dot(rel, depthDir), 0f, Mathf.Max(0f, depth));
            Vector3 clamped = near + widthDir * w + depthDir * d;
            clamped.y = pos.y;
            return clamped;
        }

        /// <summary>
        /// Bố cục CỘT của vùng chờ (ổn định theo level): <paramref name="perRow"/> = số cột (kẹp để cả lưới lọt
        /// trong bề rộng vùng), <paramref name="gridStart"/> = across của cột 0, <paramref name="nearCol"/> = cột
        /// gần CỬA path nhất. Lưới dời về phía cửa nhưng luôn nằm TRỌN trong vùng. Kèm khung (near/dir/depth) để
        /// đặt anchor. Map chưa vẽ vùng → half/depth = ∞ (bám cửa, không biên).
        /// </summary>
        private void ColumnLayout(out Vector3 near, out Vector3 depthDir, out Vector3 widthDir, out float depth,
                                  out int perRow, out float gridStart, out int nearCol)
        {
            WaitFrame(out near, out depthDir, out widthDir, out float half, out depth, out float lateral);

            perRow = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1, _maxGunOnPath))));
            if (!float.IsInfinity(half))
                perRow = Mathf.Clamp(perRow, 1, Mathf.Max(1, Mathf.FloorToInt(2f * half / _clusterSpacing) + 1));

            float gridWidth = (perRow - 1) * _clusterSpacing;
            float gridCenter = float.IsInfinity(half) ? lateral
                             : (gridWidth >= 2f * half ? 0f
                                : Mathf.Clamp(lateral, -half + gridWidth * 0.5f, half - gridWidth * 0.5f));
            gridStart = gridCenter - gridWidth * 0.5f;
            nearCol = float.IsInfinity(half) ? 0
                    : Mathf.Clamp(Mathf.RoundToInt((lateral - gridStart) / Mathf.Max(0.01f, _clusterSpacing)), 0, perRow - 1);
        }

        /// <summary>
        /// Anchor cho gun ở HẠNG <paramref name="rank"/> (0 = đầu hàng, sát cửa). Lấp theo kiểu RẮN BÒ
        /// (boustrophedon): hàng 0 lấp từ phía CỬA, hàng kế lấp ngược lại… nên rank kề nhau luôn Ở Ô KỀ NHAU.
        /// Nhờ đó rank liên tục (0..n-1) → KHÔNG bao giờ có lỗ; và khi 1 gun vào path, mọi gun chỉ tụt 1 rank =
        /// nhích đúng 1 ô kề → cả hàng dồn LÊN MƯỢT như sâu bò, không dạt ngang loạn xạ.
        /// </summary>
        private Vector3 QueueSlotPos(int rank, Gun gun)
        {
            ColumnLayout(out Vector3 near, out Vector3 depthDir, out Vector3 widthDir, out float depth,
                         out int perRow, out float gridStart, out int nearCol);

            int row = rank / perRow;
            int posInRow = rank % perRow;
            // Hàng 0 lấp từ phía CỬA (để rank 0 = ô gần cửa nhất); hàng lẻ lấp ngược → 2 đầu hàng nối nhau kề ô.
            bool row0FromRight = nearCol >= perRow * 0.5f;
            bool fromRight = (row % 2 == 0) ? row0FromRight : !row0FromRight;
            int col = fromRight ? (perRow - 1 - posInRow) : posInRow;

            float across = gridStart + col * _clusterSpacing;
            float depthPos = _clusterSpacing * (row + 0.5f); // hàng 0 sát cửa (vào trước), hàng sau lùi vào
            if (!float.IsInfinity(depth)) depthPos = Mathf.Min(depthPos, Mathf.Max(0f, depth - _clusterSpacing * 0.5f));

            Vector3 anchor = near + widthDir * across + depthDir * depthPos;
            anchor += Vector3.up * waterSurfaceOffset;
            if (clusterJitter > 0f && _queueJitter.TryGetValue(gun, out var j))
                anchor += (widthDir * j.x + depthDir * j.y) * clusterJitter;
            return ClampToWait(anchor);
        }

        private Vector3 PathSurfacePoint(float distance)
        {
            return _path != null
                ? _path.GetPointAtDistance(distance) + Vector3.up * waterSurfaceOffset
                : Vector3.up * waterSurfaceOffset;
        }

        private Quaternion TunnelInFallbackRotation()
        {
            if (_path == null) return Quaternion.identity;
            Vector3 from = PathSurfacePoint(_frontStationDistance);
            Vector3 direction = PathSurfacePoint(_frontStationDistance + 0.1f) - from;
            direction.y = 0f;
            return direction.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                : Quaternion.identity;
        }


        /// <summary>
        /// Cập nhật CHỖ ĐỨNG (anchor) của cả đám đông: gun _queue[i] → ô rank i (nén front-first, không lỗ). Khi
        /// gun đầu hàng vào path, mọi gun tụt 1 rank = nhích 1 ô kề → dồn lên lấp chỗ trống mượt. Di chuyển thật
        /// + đẩy nhau do SimulateQueueCrowd() chạy mỗi frame.
        /// </summary>
        private void RestageQueue()
        {
            int rank = 0;
            for (int i = 0; i < _queue.Count; i++)
            {
                var gun = _queue[i];
                if (gun == null) continue;
                _queueTarget[gun] = QueueSlotPos(rank++, gun);
            }
        }

        public void RemoveGun(Gun gun)
        {
            _guns.Remove(gun); // gun khác vẫn chạy loop giữ nguyên khoảng cách — để lại 1 chỗ trống
            bool wasQueued = _queue.Remove(gun);
            _queueJitter.Remove(gun);
            _queueTarget.Remove(gun);
            _queueArrived.Remove(gun);
            // Gun chết/despawn khi đang ở cửa: dọn khỏi gate để không kẹt lượt cho gun sau.
            _emergeWaiting.Remove(gun);
            _emerge.RemoveAll(r => r.Gun == gun);
            if (_gateGun == gun) _gateGun = null;
            if (wasQueued) RestageQueue(); // gun đợi biến mất → hàng còn lại dồn lại cho khít
        }

        public void Clear()
        {
            _guns.Clear(); // gun trả về pool qua PoolManager.ReturnAll khi rebuild
            _queue.Clear();
            _queueJitter.Clear();
            _queueTarget.Clear();
            _queueArrived.Clear();
            _emerge.Clear();
            _emergeWaiting.Clear();
            _gateGun = null;
            DestroyPathMeshes();
            foreach (var bubble in _bubbles) if (bubble != null) Destroy(bubble.gameObject);
            _bubbles.Clear();
            if (_bubbleMaterial != null) { Destroy(_bubbleMaterial); _bubbleMaterial = null; }
            if (_waterMaterialInstance != null) { Destroy(_waterMaterialInstance); _waterMaterialInstance = null; }
            if (_path != null) { Destroy(_path.gameObject); _path = null; }
            // Tunnel là con của PathManager (không phải của GunPath) → phải tự dọn, không thì level sau
            // chồng thêm 1 cặp nữa.
            if (_tunnelIn != null) { Destroy(_tunnelIn); _tunnelIn = null; }
            if (_tunnelOut != null) { Destroy(_tunnelOut); _tunnelOut = null; }
        }

        private void DestroyPathMeshes()
        {
            if (_pipeFilter != null) _pipeFilter.sharedMesh = null;
            if (_waterFilter != null) _waterFilter.sharedMesh = null;
            if (_pipePathMesh != null) { Destroy(_pipePathMesh); _pipePathMesh = null; }
            if (_waterPathMesh != null) { Destroy(_waterPathMesh); _waterPathMesh = null; }
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

            // A click can fill the last path capacity while its gun is still
            // waiting in _queue. It must participate in this board-level check;
            // otherwise IsFull becomes true and the game loses before that gun
            // has been admitted to path_0.
            foreach (var g in _queue)
                if (g != null && g.HasBullets && grid.HasFrontCellOfColor(g.Color)) return true;
            return false;
        }
    }
}

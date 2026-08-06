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
        [Tooltip("Gun từ slot bay THẲNG tới điểm chờ NGOÀI cửa tunnel này rồi mới transit vào path. Offset " +
                 "(world units) tính NGƯỢC hướng path từ path_0 ra phía ngoài.")]
        [Min(0f)] [SerializeField] private float entryWaitOffset = 1.5f;
        [Tooltip("Tốc độ (world units/giây) gun bay từ slot ra điểm chờ ngoài cửa.")]
        [Min(0.01f)] [SerializeField] private float slotToEntrySpeed = 8f;

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

        // ===== Gate điều phối cửa vào path (path_0) =====
        // Mỗi lúc chỉ 1 gun được TRANSIT (GoOut) qua cửa. Ưu tiên: gun slot đang chờ (_queue) vào HẾT
        // trước; gun loop muốn tái xuất (_emerge) phải ẩn hẳn tại cửa đợi tới lượt.
        private Gun _gateGun;                                             // gun đang transit; null = cửa rảnh
        private readonly List<EmergeReq> _emerge = new List<EmergeReq>(); // gun loop ẩn ở cửa chờ tái xuất (FIFO)
        private readonly HashSet<Gun> _emergeWaiting = new HashSet<Gun>();// gun đang ẩn chờ → IsEntryClear bỏ qua

        /// <summary>Một yêu cầu tái xuất của gun loop: giữ callback để "mở cửa" khi tới lượt.</summary>
        private class EmergeReq { public Gun Gun; public Action OnGranted; }

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
            gun.OnQueued();
            _queue.Add(gun);
            StageOutside(gun);
        }

        /// <summary>
        /// Gun loop chạy hết vòng, ẩn ở cửa và XIN tái xuất. Chưa gọi lại onGranted ngay: nếu còn gun slot
        /// đang chờ (_queue) thì gun này phải đợi cho slot vào hết. ServiceGate() gọi onGranted khi tới lượt.
        /// </summary>
        public void RequestEmerge(Gun gun, Action onGranted)
        {
            if (gun == null) { onGranted?.Invoke(); return; }
            _emergeWaiting.Add(gun);
            _emerge.Add(new EmergeReq { Gun = gun, OnGranted = onGranted });
        }

        private void Update()
        {
            ServiceGate();
        }

        /// <summary>
        /// Điều phối cửa vào path mỗi frame. Chỉ 1 gun transit tại một thời điểm (_gateGun). Khi cửa rảnh:
        /// ƯU TIÊN gun slot đang chờ (_queue) — vào HẾT rồi mới tới gun loop tái xuất (_emerge).
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

            // Ưu tiên 1: gun slot đang chờ ngoài cửa.
            if (_queue.Count > 0)
            {
                var gun = _queue[0];
                if (gun == null) { _queue.RemoveAt(0); return; }
                // Sức chứa CHỈ áp cho gun MỚI: path đầy thì gun slot đợi (thường CanAcceptCount đã chặn từ
                // lúc click). Gun tái xuất KHÔNG kiểm cái này vì nó đã nằm sẵn trong _guns.
                if (_guns.Count >= _maxGunOnPath) return;
                // Đợi gun bay TỚI điểm chờ ngoài cửa rồi mới cho transit — để thấy nó "đi ra" khỏi slot,
                // không teleport vào pos 0 ngay lúc còn đang bay (cả khi cửa đang rảnh). Gun loop chờ tái
                // xuất cũng phải nhường trong lúc này (ưu tiên slot).
                const float arriveSqr = 0.2f * 0.2f;
                if ((gun.transform.position - EntryWaitPos()).sqrMagnitude > arriveSqr) return;
                _queue.RemoveAt(0);
                BeginSlotTransit(gun);
                return;
            }

            // Ưu tiên 2: gun loop ẩn ở cửa chờ tái xuất (chỉ khi KHÔNG còn gun slot nào chờ).
            if (_emerge.Count > 0) GrantEmerge();
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
            // do IsEntrySpacingClear() bảo đảm. playEmerge:false → gun slot KHÔNG chơi hiệu ứng GoOut
            // (chui ra khỏi hầm) như gun loop tái xuất; nó đã bay ra khỏi slot nên vào path chạy luôn.
            gun.DeployOnPath(_path, _frontStationDistance, _gunSpeed, playEmerge: false);
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
                // Gun loop đang ẩn ở cửa chờ tái xuất: nó parked tại pos 0 nhưng KHÔNG thật sự chắn đường
                // (đang nhường cho gun slot vào) → bỏ qua khỏi check spacing, nếu không sẽ tự khoá cửa.
                if (_emergeWaiting.Contains(g)) continue;
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
        /// Điểm chờ NGOÀI cửa tunnel: lùi ra khỏi path_0 một đoạn entryWaitOffset theo hướng NGƯỢC path.
        /// Gun slot đứng đây (ngoài đường) đợi tới lượt, không chắn track sống như khi đứng ngay pos 0.
        /// </summary>
        private Vector3 EntryWaitPos()
        {
            Vector3 p0 = _path.GetPointAtDistance(_frontStationDistance);
            if (entryWaitOffset <= 0f) return p0;
            Vector3 tangent = _path.GetPointAtDistance(_frontStationDistance + 0.1f) - p0;
            tangent.y = 0f;
            if (tangent.sqrMagnitude < 1e-6f) return p0;
            return p0 - tangent.normalized * entryWaitOffset;
        }

        /// <summary>
        /// Gun slot bay THẲNG từ slot ra điểm chờ ngoài cửa, tốc độ slotToEntrySpeed. Cả hàng chờ chồng
        /// lên nhau ở đúng điểm này (không phụ thuộc thứ tự) — tới lượt ai thì ServiceGate cho người đó vào.
        /// </summary>
        private void StageOutside(Gun gun)
        {
            if (gun == null || _path == null) return;

            Vector3 pos = EntryWaitPos();
            float dist = Vector3.Distance(gun.transform.position, pos);
            float dur = dist / Mathf.Max(0.01f, slotToEntrySpeed);
            gun.MoveTo(pos, dur);

            // Quay mặt gun về phía cửa (pos 0) để lát transit vào đường không giật.
            Vector3 dir = _path.GetPointAtDistance(_frontStationDistance) - pos;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-6f)
                gun.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        public void RemoveGun(Gun gun)
        {
            _guns.Remove(gun); // gun khác vẫn chạy loop giữ nguyên khoảng cách — để lại 1 chỗ trống
            _queue.Remove(gun);
            // Gun chết/despawn khi đang ở cửa: dọn khỏi gate để không kẹt lượt cho gun sau.
            _emergeWaiting.Remove(gun);
            _emerge.RemoveAll(r => r.Gun == gun);
            if (_gateGun == gun) _gateGun = null;
        }

        public void Clear()
        {
            _guns.Clear(); // gun trả về pool qua PoolManager.ReturnAll khi rebuild
            _queue.Clear();
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
            return false;
        }
    }
}

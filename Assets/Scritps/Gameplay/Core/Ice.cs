using TMPro;
using UnityEngine;

namespace Wayfu.Lamkn
{
    /// <summary>
    /// 1 khối BĂNG (item pooled). Gắn trên ROOT prefab Ice; child TMP = countdown. IceController điều khiển:
    /// <see cref="Fit"/> đặt vị trí + scale vừa vùng cell, <see cref="SetCountdown"/> cập nhật số, <see cref="Despawn"/>
    /// trả về pool. Tự đo bounds (renderer) để fit nên không cần biết kích thước prefab trước.
    /// </summary>
    public class Ice : MonoBehaviour, IItemPool<Ice>, IResetComponent
    {
        private TMP_Text _countdown;
        private Pooler<Ice> _pool;
        private Vector3 _origScale, _countdownOrigScale, _countdownOrigLocalPosition;
        private Quaternion _origRot, _countdownOrigRot;
        private bool _cached;
        private MeshFilter _sideFilter;
        private MeshRenderer _sideRenderer;
        private Material _sideMaterial;
        [SerializeField, Min(0f), Tooltip("Độ dài băng rủ xuống ở các cạnh ngoài của vùng Ice.")]
        private float _outerEdgeDrop = 0.65f;
        [SerializeField, Min(0f), Tooltip("Đẩy thành băng ra ngoài footprint cell; tăng để các cạnh chồng lên mặt trên, không hở seam.")]
        private float _outerEdgeOffset = 0.08f;
        [SerializeField, Min(0f), Tooltip("Phần mặt trên Ice phủ thêm ra ngoài mép thành mỗi phía, để che kín các góc bo.")]
        private float _topSurfaceOverlap = 0.08f;
        [SerializeField, Tooltip("Chỉ xoay Z cho countdown khi asset chữ bị ngược/dọc. X luôn giữ 0.")]
        private float _countdownZRotation;

        public void OnInitializedInPool(Pooler<Ice> pool) => _pool = pool;

        private void Awake() => CacheOriginals();

        // Nhớ scale/rotation gốc của prefab + TMP countdown (item pooled bị Fit ghi đè, phải reset trước khi dùng lại).
        private void CacheOriginals()
        {
            if (_cached) return;
            _origScale = transform.localScale;
            _origRot = transform.localRotation;
            _countdown = GetComponentInChildren<TMP_Text>(true);
            if (_countdown != null)
            {
                _countdownOrigScale = _countdown.transform.localScale;
                _countdownOrigLocalPosition = _countdown.transform.localPosition;
                _countdownOrigRot = _countdown.transform.localRotation;
            }
            _cached = true;
        }

        public void ResetComponent()
        {
            CacheOriginals();
            transform.localScale = _origScale;
            transform.localRotation = _origRot;
            if (_countdown != null)
            {
                _countdown.transform.localScale = _countdownOrigScale;
                _countdown.transform.localRotation = _countdownOrigRot;
            }
        }

        // Băng nằm PHẲNG (X=90) úp xuống sàn, nhìn từ trên xuống; xoay thêm theo yaw của grid.
        private static readonly Quaternion FlatBase = Quaternion.Euler(90f, 0f, 0f);

        /// <summary>Phủ vùng cell: tâm world, bề rộng/sâu world (theo trục grid), xoay Y theo grid; hiện countdown = value.</summary>
        public void Fit(Vector3 center, float width, float depth, float yaw, float coveredCellHeight,
                        bool showCountdown, int value)
        {
            CacheOriginals();
            // Đo bounds Ở TƯ THẾ PHẲNG (X=90, chưa yaw): local X → bề rộng sàn (world X), local Y → bề sâu (world Z).
            transform.localScale = _origScale;
            transform.rotation = FlatBase;
            transform.position = center;

            var b = VisualBounds();
            // Thành bắt đầu từ footprint gốc rồi được đẩy ra _outerEdgeOffset.
            // Mặt trên phải phủ thêm cả offset đó lẫn padding góc, nếu không thành sẽ lộ ra ngoài mặt trên.
            float surfaceWidth = width + (_outerEdgeOffset + _topSurfaceOverlap) * 2f;
            float surfaceDepth = depth + (_outerEdgeOffset + _topSurfaceOverlap) * 2f;
            float fx = Mathf.Max(1e-3f, b.size.x), fz = Mathf.Max(1e-3f, b.size.z);
            float kx = surfaceWidth / fx, ky = surfaceDepth / fz;
            transform.localScale = new Vector3(_origScale.x * kx, _origScale.y * ky, _origScale.z);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f) * FlatBase; // phẳng + xoay theo grid
            transform.position = center;
            BuildOuterEdgeIce(center, width, depth, yaw, Mathf.Max(_outerEdgeDrop, coveredCellHeight));

            if (_countdown != null)
            {
                // TMP must not be rotated below Ice's non-uniform fit scale: that causes shear.
                if (_countdown.transform.parent != transform.parent)
                    _countdown.transform.SetParent(transform.parent, true);
                _countdown.transform.position = transform.TransformPoint(_countdownOrigLocalPosition);
                _countdown.transform.localScale = Vector3.Scale(_countdownOrigScale, _origScale);
                _countdown.transform.rotation = Quaternion.Euler(90f, yaw,
                    _countdownZRotation + AutoCountdownFlipZ(yaw));
                _countdown.gameObject.SetActive(showCountdown);
                if (showCountdown)
                {
                    // Khối scale không đều → counter-scale số để không bị kéo méo.
                    // Scale and rotation were already set in world space above.
                    _countdown.text = value.ToString();
                }
            }
        }

        /// <summary>Cập nhật số countdown (chỉ khối đang hiện countdown).</summary>
        public void SetCountdown(int remaining)
        {
            if (_countdown == null) return;

            // The last visible tick is 1. Hide the glyph at that point so it cannot
            // remain stuck on screen when the melt/despawn callback arrives later.
            if (remaining <= 1)
            {
                _countdown.gameObject.SetActive(false);
                return;
            }

            if (!_countdown.gameObject.activeSelf) _countdown.gameObject.SetActive(true);
            _countdown.text = remaining.ToString();
        }

        private static float AutoCountdownFlipZ(float gridYaw)
        {
            Camera cam = Camera.main;
            if (cam == null) return 0f;
            // Camera.up projected onto the board is the direction that reads as "up" on screen.
            // A grid whose forward direction points to the opposite half-plane needs a 180° glyph flip.
            Vector3 screenUp = cam.transform.up; screenUp.y = 0f;
            if (screenUp.sqrMagnitude < 1e-6f) return 0f;
            float screenYaw = Mathf.Atan2(screenUp.x, screenUp.z) * Mathf.Rad2Deg;
            float delta = Mathf.DeltaAngle(gridYaw, screenYaw);
            // Near perpendicular means the glyph baseline is vertical on screen: rotate it
            // sideways into a readable horizontal orientation. Opposite direction needs a flip.
            if (delta > 45f && delta < 135f) return -90f;
            if (delta < -45f && delta > -135f) return 90f;
            return Mathf.Abs(delta) >= 135f ? 180f : 0f;
        }

        public void Despawn()
        {
            if (_pool != null) _pool.Release(this);
            else Destroy(gameObject);
        }

        private void BuildOuterEdgeIce(Vector3 center, float width, float depth, float yaw, float edgeDrop)
        {
            if (edgeDrop <= 0f) return;
            if (_sideFilter == null)
            {
                var side = new GameObject("OuterIceEdges");
                side.transform.SetParent(transform, false);
                _sideFilter = side.AddComponent<MeshFilter>();
                _sideRenderer = side.AddComponent<MeshRenderer>();
                _sideRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _sideRenderer.receiveShadows = false;
            }
            if (_sideMaterial == null)
            {
                var sprite = GetComponentInChildren<SpriteRenderer>(true);
                if (sprite != null && sprite.sprite != null)
                {
                    // SpriteRenderer supplies its sprite texture through a property block. A plain
                    // MeshRenderer does not, so create a small material instance with that texture.
                    var baseMaterial = sprite.sharedMaterial != null
                        ? sprite.sharedMaterial : new Material(Shader.Find("Sprites/Default"));
                    _sideMaterial = new Material(baseMaterial);
                    _sideMaterial.mainTexture = sprite.sprite.texture;
                }
                else
                {
                    var source = FindIceRenderer();
                    _sideMaterial = source != null ? source.sharedMaterial : null;
                }
                _sideRenderer.sharedMaterial = _sideMaterial;
            }

            Vector3 right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3[] top =
            {
                center - right * width * .5f - forward * depth * .5f,
                center + right * width * .5f - forward * depth * .5f,
                center + right * width * .5f + forward * depth * .5f,
                center - right * width * .5f + forward * depth * .5f
            };
            var vertices = new Vector3[16];
            var uvs = new Vector2[16];
            // Ice.mat renders both sides. Do not duplicate reversed triangles on the same vertices:
            // their opposite normals cancel in RecalculateNormals(), producing black/flickering walls.
            var triangles = new int[24];
            for (int edge = 0; edge < 4; edge++)
            {
                int next = (edge + 1) % 4;
                int v = edge * 4;
                Vector3 outward = (top[edge] + top[next]) * 0.5f - center;
                outward.y = 0f;
                outward = outward.sqrMagnitude > 1e-6f ? outward.normalized * _outerEdgeOffset : Vector3.zero;
                // Offset each side outwards and lengthen it at both ends by the same amount.
                // The neighboring sides therefore overlap at corners instead of leaving seams.
                Vector3 along = top[next] - top[edge]; along.y = 0f;
                along = along.sqrMagnitude > 1e-6f ? along.normalized * _outerEdgeOffset : Vector3.zero;
                Vector3 a = top[edge] + outward - along, b = top[next] + outward + along;
                vertices[v] = transform.InverseTransformPoint(a);
                vertices[v + 1] = transform.InverseTransformPoint(b);
                vertices[v + 2] = transform.InverseTransformPoint(b + Vector3.down * edgeDrop);
                vertices[v + 3] = transform.InverseTransformPoint(a + Vector3.down * edgeDrop);
                uvs[v] = new Vector2(0, 1); uvs[v + 1] = new Vector2(1, 1);
                uvs[v + 2] = new Vector2(1, 0); uvs[v + 3] = new Vector2(0, 0);
                int t = edge * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            var mesh = new Mesh { name = "IceOuterEdges" };
            mesh.vertices = vertices; mesh.uv = uvs; mesh.triangles = triangles;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            if (_sideFilter.sharedMesh != null) Destroy(_sideFilter.sharedMesh);
            _sideFilter.sharedMesh = mesh;
        }

        // Ignore the countdown TMP and the generated edge mesh: Ice must fit and shade from
        // the actual visual model (IceModel / ice.fbx), not from text UI.
        private Renderer FindIceRenderer()
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == _sideRenderer || renderer.GetComponent<TMP_Text>() != null) continue;
                return renderer;
            }
            return null;
        }

        private Bounds VisualBounds()
        {
            bool hasBounds = false;
            Bounds b = default;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == _sideRenderer || renderer.GetComponent<TMP_Text>() != null) continue;
                if (!hasBounds) { b = renderer.bounds; hasBounds = true; }
                else b.Encapsulate(renderer.bounds);
            }
            if (!hasBounds) return new Bounds(transform.position, Vector3.one);
            return b;
        }
    }
}

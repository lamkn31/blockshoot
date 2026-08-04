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
        private Vector3 _origScale, _countdownOrigScale;
        private Quaternion _origRot, _countdownOrigRot;
        private bool _cached;
        private MeshFilter _sideFilter;
        private MeshRenderer _sideRenderer;
        private Material _sideMaterial;
        [SerializeField, Min(0f), Tooltip("Độ dài băng rủ xuống ở các cạnh ngoài của vùng Ice.")]
        private float _outerEdgeDrop = 0.65f;
        [SerializeField, Min(0f), Tooltip("Đẩy thành băng ra ngoài footprint cell để không chồng mặt với block.")]
        private float _outerEdgeOffset = 0.025f;
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

            var b = CombinedBounds(gameObject);
            float fx = Mathf.Max(1e-3f, b.size.x), fz = Mathf.Max(1e-3f, b.size.z);
            float kx = width / fx, ky = depth / fz;
            transform.localScale = new Vector3(_origScale.x * kx, _origScale.y * ky, _origScale.z);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f) * FlatBase; // phẳng + xoay theo grid
            transform.position = center;
            BuildOuterEdgeIce(center, width, depth, yaw, Mathf.Max(_outerEdgeDrop, coveredCellHeight));

            if (_countdown != null)
            {
                _countdown.gameObject.SetActive(showCountdown);
                if (showCountdown)
                {
                    // Khối scale không đều → counter-scale số để không bị kéo méo.
                    _countdown.transform.localScale =
                        new Vector3(_countdownOrigScale.x / kx, _countdownOrigScale.y / ky, _countdownOrigScale.z);
                    _countdown.transform.localRotation = Quaternion.Euler(0f, 0f,
                        _countdownZRotation + AutoCountdownFlipZ(yaw));
                    _countdown.text = value.ToString();
                }
            }
        }

        /// <summary>Cập nhật số countdown (chỉ khối đang hiện countdown).</summary>
        public void SetCountdown(int remaining)
        {
            if (_countdown != null && _countdown.gameObject.activeSelf) _countdown.text = remaining.ToString();
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
                    var source = GetComponentInChildren<Renderer>(true);
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
            // Two-sided faces: this is a thin visual sheet and must remain textured/readable from
            // both the outside and camera-facing side regardless of mesh winding.
            var triangles = new int[48];
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
                int t = edge * 12;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
                triangles[t + 6] = v + 2; triangles[t + 7] = v + 1; triangles[t + 8] = v;
                triangles[t + 9] = v + 3; triangles[t + 10] = v + 2; triangles[t + 11] = v;
            }
            var mesh = new Mesh { name = "IceOuterEdges" };
            mesh.vertices = vertices; mesh.uv = uvs; mesh.triangles = triangles;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            if (_sideFilter.sharedMesh != null) Destroy(_sideFilter.sharedMesh);
            _sideFilter.sharedMesh = mesh;
        }

        private static Bounds CombinedBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }
    }
}

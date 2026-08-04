using System.Collections.Generic;
using UnityEngine;
namespace Wayfu.Lamkn
{
    // Loại FX trong game. Prefab được gán tập trung ở FxController, nơi gọi chỉ cần biết loại.
    public enum FxType
    {
        Done = 0,    // xe hoàn thành (đầy, rời đi) — blink done
        Collide = 1, // xe va chạm (đâm xe khác)
        Moving = 2,  // xe đang chạy (bật particle lửa, khói, bụi)
        Reveal = 3,  // xe Hidden lộ màu thật (blink hoi cham) — phát tại xe khi chuyển từ hidden sang màu vốn có
        TimerAura = 4, // xe ambulance aura
        MovingTimer = 5, // xe ambulance timer moving
        BlockHit = 6, // block trúng đạn vỡ — phát tại vị trí block, tô theo màu block
    }

    // Quản lý pool FX + phát FX tại vị trí cần. Toàn bộ prefab FX khai báo TẠI ĐÂY (không để ở car).
    // FX phát xong tự trả về pool (không Instantiate/Destroy mỗi lần). Pool tách theo từng prefab.
    [AddComponentMenu("Bus Game/Fx Controller")]
    public sealed class FxController : Singleton<FxController>
    {
        [SerializeField]
        [Tooltip("Khai báo prefab cho từng loại FX. 'prewarm' = số instance dựng sẵn trong pool lúc khởi động. Offset đặt riêng trên từng prefab (component PooledFx).")]
        private FxEntry[] _fx;

        [Header("Meta Hidden Scroll")]
        [SerializeField]
        [Tooltip("Material xe Hidden (metaHidden). Offset texture _BaseMap được cuộn theo thời gian để chạy hiệu ứng scroll.")]
        private Material _metaHiddenMaterial;

        [SerializeField]
        [Tooltip("Tốc độ cuộn UV mỗi giây (âm = cuộn ngược). Mặc định (-0.1, -0.1) theo yêu cầu.")]
        private Vector2 _metaHiddenScrollSpeed = new Vector2(-0.1f, -0.1f);

        private static readonly int s_baseMap = Shader.PropertyToID("_BaseMap");
        private Vector2 _metaHiddenScrollOffset;   // offset đang cuộn (tích luỹ theo thời gian)
        private Vector2 _metaHiddenBaseOffset;     // offset gốc của material để khôi phục khi tắt

        // Tra prefab theo loại FX.
        private readonly Dictionary<FxType, GameObject> _prefabByType = new();
        // Mỗi prefab → 1 hàng đợi các instance đang nghỉ.
        private readonly Dictionary<GameObject, Queue<PooledFx>> _pools = new();
        private Transform _root; // parent chứa FX đang nghỉ (gọn hierarchy)

        [System.Serializable]
        private struct FxEntry
        {
            public FxType type;
            public GameObject prefab;
            [Min(0)] public int prewarm;
        }

        protected override void OnAwake()
        {
            _root = new GameObject("FX Pool").transform;
            _root.SetParent(transform, false);

            if (_fx != null)
            {
                foreach (FxEntry e in _fx)
                {
                    if (e.prefab == null) continue;
                    _prefabByType[e.type] = e.prefab;
                    Prewarm(e.prefab, e.prewarm);
                }
            }

            // Nhớ offset gốc để trả lại khi thoát (material là asset chia sẻ, tránh dirty sau Play).
            if (_metaHiddenMaterial != null)
            {
                _metaHiddenBaseOffset = _metaHiddenMaterial.GetTextureOffset(s_baseMap);
                _metaHiddenScrollOffset = _metaHiddenBaseOffset;
            }
        }

        // Cuộn UV texture _BaseMap của material metaHidden theo thời gian → hiệu ứng scroll.
        private void Update()
        {
            if (_metaHiddenMaterial == null) return;

            _metaHiddenScrollOffset += _metaHiddenScrollSpeed * Time.deltaTime;
            // Giữ trong [0,1) để không trôi số quá lớn (offset lặp mỗi 1 đơn vị UV).
            _metaHiddenScrollOffset.x = Mathf.Repeat(_metaHiddenScrollOffset.x, 1f);
            _metaHiddenScrollOffset.y = Mathf.Repeat(_metaHiddenScrollOffset.y, 1f);
            _metaHiddenMaterial.SetTextureOffset(s_baseMap, _metaHiddenScrollOffset);
        }

        protected override void OnDestroy()
        {
            // Khôi phục offset gốc để không lưu đè lên asset material khi dừng Play.
            if (_metaHiddenMaterial != null)
                _metaHiddenMaterial.SetTextureOffset(s_baseMap, _metaHiddenBaseOffset);
            base.OnDestroy();
        }

        // Phát FX theo loại tại vị trí, không xoay.
        public PooledFx Play(FxType type, Vector3 position) => Play(type, position, Quaternion.identity);

        // Phát FX theo loại tại vị trí + góc xoay.
        public PooledFx Play(FxType type, Vector3 position, Quaternion rotation)
        {
            return _prefabByType.TryGetValue(type, out GameObject prefab)
                ? Play(prefab, position, rotation)
                : null;
        }

        // Phát FX theo prefab bất kỳ (dùng cho FX không nằm trong enum). Trả instance để tắt thủ công nếu cần.
        public PooledFx Play(GameObject prefab, Vector3 position) => Play(prefab, position, Quaternion.identity);

        public PooledFx Play(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return null;

            PooledFx fx = Get(prefab);
            fx.gameObject.SetActive(true);
            fx.Play(this, position, rotation); // PooledFx tự cộng offset riêng của nó
            return fx;
        }

        // Gắn FX (loop) làm con của 'parent' — dùng cho FX BÁM THEO (vd khói khi xe chạy). KHÔNG tự về pool;
        // gọi Return() thủ công khi xong (vd lúc xe rời đi) để FX khỏi bị huỷ theo parent.
        public PooledFx Attach(FxType type, Transform parent)
            => _prefabByType.TryGetValue(type, out GameObject prefab) ? Attach(prefab, parent) : null;

        public PooledFx Attach(GameObject prefab, Transform parent)
        {
            if (prefab == null || parent == null) return null;

            PooledFx fx = Get(prefab);
            fx.gameObject.SetActive(true);
            fx.PlayAttached(this, parent); // bám theo parent, dùng offset riêng làm localPosition
            return fx;
        }

        // Trả FX về pool (gọi bởi PooledFx khi phát xong, hoặc thủ công). Reparent về FxController nên FX không bị
        // huỷ theo parent cũ (vd xe bị Destroy) — chính là "trả smoke về fxcontroller làm parent".
        public void Return(PooledFx fx)
        {
            if (fx == null) return;

            fx.gameObject.SetActive(false);
            fx.transform.SetParent(_root, false);
            QueueFor(fx.SourcePrefab).Enqueue(fx);
        }

        // Dựng sẵn 'count' instance cho prefab và để nghỉ trong pool.
        public void Prewarm(GameObject prefab, int count)
        {
            if (prefab == null || count <= 0) return;

            Queue<PooledFx> queue = QueueFor(prefab);
            for (int i = 0; i < count; i++)
                queue.Enqueue(Create(prefab));
        }

        private PooledFx Get(GameObject prefab)
        {
            Queue<PooledFx> queue = QueueFor(prefab);
            return queue.Count > 0 ? queue.Dequeue() : Create(prefab);
        }

        private PooledFx Create(GameObject prefab)
        {
            GameObject go = Instantiate(prefab, _root);
            go.SetActive(false);

            PooledFx fx = go.GetComponent<PooledFx>();
            if (fx == null) fx = go.AddComponent<PooledFx>();
            fx.SourcePrefab = prefab; // nhớ prefab gốc để trả về đúng hàng đợi
            return fx;
        }

        private Queue<PooledFx> QueueFor(GameObject prefab)
        {
            if (!_pools.TryGetValue(prefab, out Queue<PooledFx> queue))
            {
                queue = new Queue<PooledFx>();
                _pools[prefab] = queue;
            }
            return queue;
        }
    }
}

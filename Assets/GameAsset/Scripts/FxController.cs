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
        BulletTrail = 7, // FX nước bám theo viên đạn đang bay (Projectiles_water) — rig trail dùng chung
        WaterGoout = 8, // FX nước bắn ra khi gun loop
        WaterLan = 9,   // FX mặt nước phát tại vị trí gun khi bắt đầu GoOut
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
        // Loại FX bật SHARED → 1 rig particle sống bền (mọi hit Emit vào cùng buffer → draw call ~cố định).
        private readonly Dictionary<FxType, SharedParticleEmitter> _sharedRigs = new();
        // Loại FX bật TRAIL → 1 rig sống bền rải particle dọc đường bay (mọi viên đạn dùng chung → draw call ~cố định).
        private readonly Dictionary<FxType, SharedTrailEmitter> _trailRigs = new();
        private Transform _root; // parent chứa FX đang nghỉ (gọn hierarchy)

        [System.Serializable]
        private struct FxEntry
        {
            public FxType type;
            public GameObject prefab;
            [Min(0)] public int prewarm;
            [Tooltip("BẬT = dùng RIG DÙNG CHUNG (SharedParticleEmitter): mọi hit Emit vào cùng 1 bộ ParticleSystem " +
                     "→ số draw call KHÔNG tăng theo số hit. Chỉ hợp FX burst 1 phát (KHÔNG loop, KHÔNG mảnh vỡ " +
                     "GameObject, KHÔNG cần xoay/tô màu riêng mỗi lần). Không đủ điều kiện sẽ tự fallback về pool.")]
            public bool shared;
            [Tooltip("BẬT = dùng RIG TRAIL DÙNG CHUNG (SharedTrailEmitter): FX BÁM VẬT BAY (vd đạn nước) rải particle " +
                     "dọc đường bay từ 1 bộ ParticleSystem chung → draw call KHÔNG tăng theo số viên đạn. Giữ đúng " +
                     "thông số prefab (start speed/shape/rate), bám vật qua inherit velocity. Ưu tiên hơn 'shared'.")]
            public bool trail;
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

                    // Bật trail → rig rải dọc đường bay (FX bám vật bay). Bật shared → rig burst đứng yên.
                    // Cả hai KHÔNG cần prewarm pool (không mượn instance). Không đủ điều kiện → fallback pool.
                    if (e.trail && IsEligibleForTrail(e.prefab))
                        _trailRigs[e.type] = BuildTrailRig(e.prefab);
                    else if (e.shared && IsEligibleForShared(e.prefab))
                        _sharedRigs[e.type] = BuildSharedRig(e.prefab);
                    else
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
            // Đường SHARED: emit vào rig dùng chung → không tăng draw call theo số hit. Rig bỏ qua rotation
            // (offset local của từng system đã bake sẵn theo prefab gốc) nên trả null (không có instance mượn ra).
            if (_sharedRigs.TryGetValue(type, out SharedParticleEmitter rig) && rig != null)
            {
                rig.EmitAt(position);
                return null;
            }

            return _prefabByType.TryGetValue(type, out GameObject prefab)
                ? Play(prefab, position, rotation)
                : null;
        }

        // Loại FX này có rig TRAIL dùng chung không? (nơi gọi kiểm tra trước khi quyết định dùng rig hay FX con riêng).
        public bool HasTrailRig(FxType type) => _trailRigs.TryGetValue(type, out SharedTrailEmitter rig) && rig != null;

        // Bắn các system BURST 1 phát của FX trail tại vị trí phóng (head/core), thừa hưởng vận tốc phóng.
        public void EmitTrailBurst(FxType type, Vector3 position, Vector3 velocity)
        {
            if (_trailRigs.TryGetValue(type, out SharedTrailEmitter rig) && rig != null)
                rig.EmitBurst(position, velocity);
        }

        // Rải particle trail của FX cho đoạn bay 'fromPos'->'toPos' (gọi mỗi bước khi vật đang bay). Rig rải
        // ĐỀU dọc đoạn (không dồn về điểm cuối) để rate-over-distance liền mạch dù bước thô.
        // 'velocity' = vận tốc world của vật → particle inherit để bay cùng vật (giữ nguyên start-velocity gốc).
        public void EmitTrail(FxType type, Vector3 fromPos, Vector3 toPos, Vector3 velocity)
        {
            if (_trailRigs.TryGetValue(type, out SharedTrailEmitter rig) && rig != null)
                rig.EmitTrail(fromPos, toPos, velocity);
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

        // Dựng 1 rig dùng chung sống bền cho prefab (Instantiate 1 lần, World space, tắt auto-emit). Mọi hit
        // của loại này Emit vào đây → draw call ~cố định. Bỏ PooledFx (nếu prefab lỡ có) để nó không tự về pool.
        private SharedParticleEmitter BuildSharedRig(GameObject prefab)
        {
            GameObject go = Instantiate(prefab, _root);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.SetActive(true);

            PooledFx legacy = go.GetComponent<PooledFx>();
            if (legacy != null) Destroy(legacy);

            SharedParticleEmitter rig = go.GetComponent<SharedParticleEmitter>();
            if (rig == null) rig = go.AddComponent<SharedParticleEmitter>();
            rig.Init(); // World space, tắt auto-emit, precompute burst count, giữ Play()
            return rig;
        }

        // Dựng 1 rig TRAIL sống bền cho prefab (Instantiate 1 lần, World space, tắt auto-emit). Mọi viên đạn
        // của loại này rải particle dọc đường bay vào đây → draw call ~cố định. Bỏ PooledFx để nó không tự về pool.
        private SharedTrailEmitter BuildTrailRig(GameObject prefab)
        {
            GameObject go = Instantiate(prefab, _root);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.SetActive(true);

            PooledFx legacy = go.GetComponent<PooledFx>();
            if (legacy != null) Destroy(legacy);

            SharedTrailEmitter rig = go.GetComponent<SharedTrailEmitter>();
            if (rig == null) rig = go.AddComponent<SharedTrailEmitter>();
            rig.Init(); // World space, inherit velocity, tắt auto-emit, precompute rate/burst, giữ Play()
            return rig;
        }

        // Prefab có dùng trail-emit được không? Điều kiện: có ParticleSystem (loop được phép — trail thường loop).
        private bool IsEligibleForTrail(GameObject prefab)
        {
            if (prefab == null) return false;
            return prefab.GetComponentInChildren<ParticleSystem>(true) != null;
        }

        // Prefab có dùng shared-emit được không? Điều kiện: có ParticleSystem, KHÔNG system nào loop
        // (loop = FX bám/aura kéo dài, không hợp mô hình "emit 1 phát tại vị trí").
        private bool IsEligibleForShared(GameObject prefab)
        {
            if (prefab == null) return false;
            ParticleSystem[] systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
            if (systems.Length == 0) return false;
            foreach (ParticleSystem ps in systems)
                if (ps.main.loop) return false;
            return true;
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

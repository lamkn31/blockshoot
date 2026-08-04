using UnityEngine;

namespace Wayfu.Lamkn
{
    /// <summary>
    /// Đạn: bay từ gun tới cell mục tiêu, tới nơi thì phá 1 block của cell rồi tự trả về pool.
    /// Dùng Pooler qua <see cref="IItemPool{TItem}"/> để tự release.
    /// </summary>
    public class Bullet : MonoBehaviour, IItemPool<Bullet>
    {
        [Tooltip("Độ cao đỉnh vòng cung = quãng đường × tỉ lệ này (kẹp bởi Arc Max Height). 0 = bay thẳng.")]
        [SerializeField] private float arcHeightRatio = 0.22f;
        [Tooltip("Giới hạn độ cao đỉnh vòng cung (world units) để cú bắn xa không vọt quá cao.")]
        [SerializeField] private float arcMaxHeight = 2.5f;
        [Tooltip("Sau khi trúng block, đạn đứng im tại chỗ bao lâu (giây) rồi mới biến mất.")]
        [SerializeField] private float lingerDuration = 1.5f;

        private Pooler<Bullet> _pool;
        private BlockCell _cell;
        private int _cellGen; // Generation của cell lúc bắn — lệch = cell pooled đã bị tái dùng
        private float _speed = 14f;
        private bool _active;
        private Renderer _renderer;
        private TrailRenderer _trail;
        private Vector3 _aimOffset; // lệch so với TÂM cell → nhắm đúng 1 block trong stack (bắn loạt)
        private bool _hitBottom;    // đạn LẺ dồn vào cell không đủ phá → phá block ĐÁY thay vì đỉnh
        private Vector3 _start;     // điểm xuất phát (nòng) — mốc nội suy ngang của parabol
        private float _duration;    // thời gian bay ≈ quãng đường / speed (chốt lúc Launch)
        private float _elapsed;     // thời gian đã bay → t = _elapsed / _duration
        private float _arcPeak;     // độ cao đỉnh vòng cung của cú bắn này
        private bool _lingering;    // đã trúng block, đang đứng im chờ biến mất
        private float _lingerTimer; // đếm ngược thời gian đứng im còn lại

        public void OnInitializedInPool(Pooler<Bullet> pool) => _pool = pool;

        private void Awake()
        {
            // Bullet di chuyển bằng code, không cần collider.
            var col = GetComponent<Collider>();
            if (col != null) Destroy(col);
            _trail = GetComponentInChildren<TrailRenderer>(true);
            _renderer = FindBodyRenderer();
        }

        /// <summary>
        /// Renderer của thân đạn để tô màu. TrailRenderer CŨNG là Renderer nên phải loại nó ra —
        /// không thì chỉ cần kéo "Trail" lên trên model trong prefab là màu đi tô nhầm vào vệt đạn.
        /// </summary>
        private Renderer FindBodyRenderer()
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (!(r is TrailRenderer)) return r;
            return null;
        }

        /// <param name="aimOffset">Lệch so với tâm cell — bắn loạt thì mỗi viên nhắm 1 block trong stack.
        /// Là OFFSET chứ không phải điểm world: cell còn trượt lúc dồn hàng, đạn phải bám theo cell.</param>
        public void Launch(Vector3 start, BlockCell target, float speed, TypeColor color,
                           Vector3 aimOffset = default, bool hitBottom = false)
        {
            transform.position = start;
            _start = start;
            _aimOffset = aimOffset;
            _hitBottom = hitBottom;

            // Chốt thời gian bay + độ cao vòng cung theo quãng đường TỚI target lúc bắn. Target còn bám cell
            // (có thể trượt) nhưng duration/peak giữ cố định là đủ cho hiệu ứng — t=1 luôn đáp đúng target.
            Vector3 firstTarget = target != null ? target.transform.position + aimOffset : start;
            float dist = Vector3.Distance(start, firstTarget);
            _duration = Mathf.Max(0.0001f, dist / Mathf.Max(0.01f, speed));
            _arcPeak = Mathf.Min(dist * arcHeightRatio, arcMaxHeight);
            _elapsed = 0f;

            // Bullet là item POOLED: TrailRenderer giữ nguyên các điểm của lượt bắn TRƯỚC khi object bị
            // tắt/bật lại. Pool bật đạn ở vị trí cũ (chỗ block vừa bị phá) rồi Launch mới teleport nó về
            // nòng → trail nối thẳng 1 vệt từ block cũ về gun. Clear() phải gọi SAU khi đã set position
            // mới, không thì trail vẫn mọc lại từ điểm cũ.
            if (_trail != null) _trail.Clear();

            _cell = target;
            _cellGen = target != null ? target.Generation : 0;
            _speed = speed;
            _active = true;
            _lingering = false;

            // Material lấy từ GlobalConfigManager theo TypeColor.
            if (_renderer == null) _renderer = FindBodyRenderer();
            var mat = GlobalConfigManager.MaterialOf(color, TypeObject.Bullet);
            if (_renderer != null && mat != null) _renderer.sharedMaterial = mat;
        }

        private void Update()
        {
            // Đã trúng block: đứng im tại chỗ, đếm ngược rồi mới biến mất.
            if (_lingering)
            {
                _lingerTimer -= Time.deltaTime;
                if (_lingerTimer <= 0f) Despawn();
                return;
            }

            if (!_active) return;
            // Cell đã bị phá — hoặc object pooled đã TÁI DÙNG thành cell khác (Generation lệch) → huỷ đạn,
            // không bay đuổi theo cell mới ở vị trí khác.
            if (_cell == null || _cell.Generation != _cellGen) { Despawn(); return; }

            Vector3 target = _cell.transform.position + _aimOffset; // bám cell (cell có thể đang trượt)

            _elapsed += Time.deltaTime;
            float t = _elapsed / _duration;
            if (t >= 1f)
            {
                // Tới đích: đáp đúng target rồi phá block.
                transform.position = target;
                _active = false;
                if (_hitBottom) _cell.ApplyHitBottom(); else _cell.ApplyHit(); // trừ 1 block + huỷ pending
                GameController.Instance?.OnBoardChanged();
                // Đứng im tại điểm trúng vài giây rồi mới trả về pool.
                _lingering = true;
                _lingerTimer = lingerDuration;
                return;
            }

            // Nội suy ngang start→target theo t, cộng vòng cung Y đỉnh giữa đường: 4t(1−t) = 1 khi t=0.5,
            // = 0 ở 2 đầu → đạn vọt lên rồi rơi xuống đúng target. Cộng SAU Lerp nên độc lập chênh cao 2 đầu.
            Vector3 pos = Vector3.Lerp(_start, target, t);
            pos.y += _arcPeak * 4f * t * (1f - t);
            transform.position = pos;
        }

        private void Despawn()
        {
            _active = false;
            _lingering = false;
            _cell = null;
            if (_pool != null) _pool.Release(this);
            else Destroy(gameObject);
        }
    }
}

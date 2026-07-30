using UnityEngine;

namespace Wayfu.Lamkn
{
    /// <summary>
    /// Đạn: bay từ gun tới cell mục tiêu, tới nơi thì phá 1 block của cell rồi tự trả về pool.
    /// Dùng Pooler qua <see cref="IItemPool{TItem}"/> để tự release.
    /// </summary>
    public class Bullet : MonoBehaviour, IItemPool<Bullet>
    {
        private const float HitDist = 0.25f;

        private Pooler<Bullet> _pool;
        private BlockCell _cell;
        private int _cellGen; // Generation của cell lúc bắn — lệch = cell pooled đã bị tái dùng
        private float _speed = 14f;
        private bool _active;
        private Renderer _renderer;
        private TrailRenderer _trail;
        private Vector3 _aimOffset; // lệch so với TÂM cell → nhắm đúng 1 block trong stack (bắn loạt)
        private bool _hitBottom;    // đạn LẺ dồn vào cell không đủ phá → phá block ĐÁY thay vì đỉnh

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
            _aimOffset = aimOffset;
            _hitBottom = hitBottom;

            // Bullet là item POOLED: TrailRenderer giữ nguyên các điểm của lượt bắn TRƯỚC khi object bị
            // tắt/bật lại. Pool bật đạn ở vị trí cũ (chỗ block vừa bị phá) rồi Launch mới teleport nó về
            // nòng → trail nối thẳng 1 vệt từ block cũ về gun. Clear() phải gọi SAU khi đã set position
            // mới, không thì trail vẫn mọc lại từ điểm cũ.
            if (_trail != null) _trail.Clear();

            _cell = target;
            _cellGen = target != null ? target.Generation : 0;
            _speed = speed;
            _active = true;

            // Material lấy từ GlobalConfigManager theo TypeColor.
            if (_renderer == null) _renderer = FindBodyRenderer();
            var mat = GlobalConfigManager.MaterialOf(color, TypeObject.Bullet);
            if (_renderer != null && mat != null) _renderer.sharedMaterial = mat;
        }

        private void Update()
        {
            if (!_active) return;
            // Cell đã bị phá — hoặc object pooled đã TÁI DÙNG thành cell khác (Generation lệch) → huỷ đạn,
            // không bay đuổi theo cell mới ở vị trí khác.
            if (_cell == null || _cell.Generation != _cellGen) { Despawn(); return; }

            Vector3 target = _cell.transform.position + _aimOffset;
            transform.position = Vector3.MoveTowards(transform.position, target, _speed * Time.deltaTime);

            if ((transform.position - target).sqrMagnitude <= HitDist * HitDist)
            {
                _active = false;
                if (_hitBottom) _cell.ApplyHitBottom(); else _cell.ApplyHit(); // trừ 1 block + huỷ pending
                GameController.Instance?.OnBoardChanged();
                Despawn();
            }
        }

        private void Despawn()
        {
            _active = false;
            _cell = null;
            if (_pool != null) _pool.Release(this);
            else Destroy(gameObject);
        }
    }
}

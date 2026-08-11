using System.Collections;
using UnityEngine;

namespace Wayfu.Lamkn
{
    /// <summary>1 block đơn (1 phần tử trong stack của 1 cell). Dùng Pooler — bị bắn thì trả về pool.</summary>
    public class Block : MonoBehaviour, IItemPool<Block>
    {
        [Tooltip("Thời gian (giây) block THU NHỎ về 0 rồi biến mất khi trúng đạn. 0 = biến mất tức thì.")]
        [SerializeField] private float shrinkDuration = 0.12f;

        public TypeColor Color { get; private set; }
        public BlockData Data { get; private set; }

        private BlockCell _cell;
        private Renderer _renderer;
        private Pooler<Block> _pool;
        private Vector3 _baseScale = Vector3.one; // scale gốc lúc Init — mốc để thu nhỏ và trả về khi despawn
        private Coroutine _shrinkRoutine;

        public void OnInitializedInPool(Pooler<Block> pool) => _pool = pool;

        public void Init(BlockCell cell, int indexInStack, TypeColor color)
        {
            // Block là item pooled: có thể đang thu nhỏ dở từ lượt trước → dừng anim, giữ đúng scale grid
            // vừa gán (Fill set localScale TRƯỚC khi gọi Init) làm mốc.
            if (_shrinkRoutine != null) { StopCoroutine(_shrinkRoutine); _shrinkRoutine = null; }
            _baseScale = transform.localScale;

            _cell = cell;
            Color = color;
            Data = new BlockData { IndexInStack = indexInStack, LocalPos = transform.localPosition };

            // Material lấy từ GlobalConfigManager theo TypeColor (không tô material.color nữa).
            if (_renderer == null) _renderer = GetComponentInChildren<Renderer>();
            var mat = GlobalConfigManager.MaterialOf(color, TypeObject.Block);
            if (_renderer != null && mat != null) _renderer.sharedMaterial = mat;
        }

        /// <summary>
        /// Trúng đạn: THU NHỎ về 0 rồi trả về pool (hiệu ứng vỡ). Fallback về <see cref="Despawn"/> tức thì
        /// nếu object đang tắt (không chạy được coroutine) hoặc shrinkDuration ≤ 0.
        /// </summary>
        public void HitDespawn()
        {
            // Hit FX: phát NGAY tại vị trí block (world). Phát trước mọi nhánh return để cả trường hợp biến mất
            // tức thì (shrinkDuration ≤ 0 / object đang tắt) vẫn có hiệu ứng vỡ.
            FxController.Instance?.Play(FxType.BlockHit, transform.position);
            SoundController.Instance?.PlayBlockDestroyedSound();

            if (shrinkDuration <= 0f || !gameObject.activeInHierarchy) { Despawn(); return; }
            // Block đang là CON của cell. Cell despawn (Pooler.SetActive(false)) NGAY khi block cuối vỡ →
            // block bị tắt theo cha, coroutine dừng, biến mất mà chưa kịp thu nhỏ. Tách khỏi cell (giữ vị
            // trí world) để block sống độc lập chạy hết anim; Despawn cuối anim tự trả về pool.
            transform.SetParent(null, true);
            if (_shrinkRoutine != null) StopCoroutine(_shrinkRoutine);
            _shrinkRoutine = StartCoroutine(ShrinkThenDespawn());
        }

        private IEnumerator ShrinkThenDespawn()
        {
            Vector3 from = transform.localScale;
            float t = 0f;
            while (t < shrinkDuration)
            {
                t += Time.deltaTime;
                transform.localScale = Vector3.Lerp(from, Vector3.zero, t / shrinkDuration);
                yield return null;
            }
            transform.localScale = Vector3.zero;
            _shrinkRoutine = null;
            Despawn();
        }

        /// <summary>Trả block về pool (thay cho Destroy). Dùng cho dọn dẹp tức thì (cell tái dùng).</summary>
        public void Despawn()
        {
            // Đang thu nhỏ dở mà bị dọn (cell rebuild) → dừng anim, trả scale gốc trước khi về pool để lượt
            // tái dùng sau không nhận block đã co lại (Fill sẽ set lại scale, nhưng trả sạch cho chắc).
            if (_shrinkRoutine != null) { StopCoroutine(_shrinkRoutine); _shrinkRoutine = null; }
            transform.localScale = _baseScale;
            if (_pool != null) _pool.Release(this);
            else Destroy(gameObject);
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Wayfu.Lamkn
{
    /// <summary>
    /// 1 cell block (~ BlockCell của PixelShoot_2): chứa 1 stack block cùng màu (lấy từ Pooler).
    /// Mỗi ĐẠN tới trừ 1 block; hết block → báo GridBlockManager dồn các cell phía sau (yêu cầu #5, #9).
    /// Có <see cref="_pendingHits"/> để không bắn dư đạn khi nhiều gun cùng nhắm 1 cell.
    /// </summary>
    public class BlockCell : MonoBehaviour, IItemPool<BlockCell>
    {
        [Tooltip("Node cha để gắn các block của stack (child 'BlocksContainer' trong prefab).")]
        [SerializeField] private Transform blocksContainer;
        [Tooltip("Bật khi cell đang nằm ở Ô GỐC của Spawner (child 'BlocksSpawnerIndicator').")]
        [SerializeField] private GameObject spawnerIndicator;
        [Tooltip("SpriteRenderer của mũi tên/indicator (child 'IndicatorSprite'). Trống sẽ tự tìm trong spawnerIndicator.")]
        [SerializeField] private SpriteRenderer indicatorRenderer;
        [Tooltip("Sprite ARROW hướng cho Spawner/SpawnerLine (trống = dùng sprite gốc trên IndicatorSprite).")]
        [SerializeField] private Sprite lineArrowSprite;
        [Tooltip("Sprite cho Spawner8 (spawner_amb_direction).")]
        [SerializeField] private Sprite eightDirectionSprite;
        [Tooltip("Nâng thêm độ cao của mũi tên so với ĐỈNH block trên cùng của cell.")]
        [SerializeField] private float indicatorHeightOffset = 0.3f;

        private Sprite _defaultIndicatorSprite;

        public TypeColor Color { get; private set; }
        public int BlockCol { get; private set; }
        public int Depth { get; private set; }

        /// <summary>
        /// Cell KHÔNG bao giờ bị gun ngắm và KHÔNG dồn hàng — đứng yên làm nguồn. Bật cho cell Spawner8
        /// (nguồn 8 hướng ở giữa grid bị path bao quanh): nó chỉ nhả block ra 8 ô xung quanh chứ bản thân
        /// không thể bị phá trực tiếp.
        /// </summary>
        public bool Indestructible { get; private set; }

        /// <summary>
        /// Cell thuộc grid bị path bao nhiều mặt (ShootableEdges != None). Gun dùng cờ này để KHÔNG tự khoá
        /// bắn "1 lượt/vòng" khi đang vòng quanh grid đó — nó cần bắn được từng mặt lần lượt khi đi qua.
        /// </summary>
        public bool MultiSideGrid { get; private set; }

        /// <summary>Gán khi dựng cell (GridBlockManager biết ShootableEdges của grid).</summary>
        public void SetMultiSide(bool on) => MultiSideGrid = on;

        /// <summary>Cell bị BĂNG phủ: KHÔNG bắn được cho tới khi băng tan (đủ block phá). Băng hiển thị bằng
        /// 1 Obstacle riêng đè lên; ở đây chỉ giữ trạng thái để gun bỏ qua khi ngắm.</summary>
        public bool Frozen { get; private set; }

        /// <summary>Tổng block phá trong màn cần đạt để băng cell này TAN (0 = không băng).</summary>
        public int IceThreshold { get; private set; }

        /// <summary>Băng tan: cell trở lại bắn được. Gọi khi tổng block phá ≥ IceThreshold.</summary>
        public void Melt() => Frozen = false;

        /// <summary>Cập nhật index cột khi cell dồn lên ô khác (Arc cột lệch: index có thể đổi).</summary>
        public void SetColumn(int col) => BlockCol = col;


        /// <summary>
        /// Cell đang TRƯỢT tới ô của nó (vừa nhả ra, hoặc đang dồn hàng) → gun không được ngắm.
        /// MoveTo tự bật khi bắt đầu trượt và tắt khi tới nơi. Nhờ vậy gun chỉ ngắm cell đã ĐỨNG YÊN
        /// ở hàng 0: cell kế vừa dồn tới nếu cùng màu thì bị bắn tiếp, khác màu thì gun bỏ cả cột đó
        /// (cell khác màu chặn mọi cell phía sau).
        /// </summary>
        public bool PendingEntry { get; private set; }

        /// <summary>
        /// Số THẾ HỆ — tăng mỗi lần Build. Cell là item pooled: object bị tái dùng cho cell khác ngay
        /// trong cùng frame, nên reference không đủ để biết "target còn sống". Gun/Bullet lưu Generation
        /// lúc chốt target; lệch = object đã thành cell khác → bỏ target, không bám theo ra ô mới.
        /// </summary>
        public int Generation { get; private set; }

        private readonly List<Block> _blocks = new List<Block>();
        private GridBlockManager _manager;
        private Quaternion _indicatorRestLocalRot = Quaternion.identity; // pose gốc của mũi tên trong prefab
        private Coroutine _moveRoutine;
        private int _pendingHits;
        private float _stackSpacing;
        private Vector3 _blockScale = Vector3.one;
        private Pooler<BlockCell> _pool;

        public void OnInitializedInPool(Pooler<BlockCell> pool) => _pool = pool;

        private void Awake()
        {
            // Nhớ pose gốc TRƯỚC khi ShowSpawnerIndicator kịp ghi đè — sau đó localRotation không còn là
            // giá trị dựng trong prefab nữa.
            if (spawnerIndicator != null) _indicatorRestLocalRot = spawnerIndicator.transform.localRotation;
            if (indicatorRenderer == null && spawnerIndicator != null)
                indicatorRenderer = spawnerIndicator.GetComponentInChildren<SpriteRenderer>(true);
            if (indicatorRenderer != null) _defaultIndicatorSprite = indicatorRenderer.sprite;
        }

        /// <summary>
        /// Thời điểm (Time.time) cell này SẬP/XUẤT HIỆN gần nhất — đặt khi bắt đầu trượt (MoveTo). Cell dựng
        /// lúc build = 0 (đã đứng sẵn từ đầu màn). Gun dùng để phân biệt PER-GUN: cell có SettleStamp SAU
        /// mốc bắt đầu lap của gun = "vừa sập trong lap này" → ưu tiên bắn SAU cell đã đứng sẵn. Lap mới thì
        /// mốc của gun tiến lên nên cell sập lap trước lại thành "ready".
        /// </summary>
        public float SettleStamp { get; private set; }

        // Nòng (của BẤT KỲ gun nào) đang CHỐT cell này làm target. Chống 2 gun cùng bắn 1 cell → đổ đạn
        // trùng, số đạn bị lẻ/phân mảnh. Reset khi Build (item pooled tái dùng). Token là object nòng.
        private object _claim;
        /// <summary>Cell còn trống claim hoặc do chính <paramref name="by"/> giữ → nòng khác không chốt được.</summary>
        public bool ClaimFreeFor(object by) => _claim == null || ReferenceEquals(_claim, by);
        /// <summary>Chốt claim nếu đang trống (hoặc của chính mình). Thua race (nòng khác giữ) → false.</summary>
        public bool TryClaim(object by)
        {
            if (_claim != null && !ReferenceEquals(_claim, by)) return false;
            _claim = by;
            return true;
        }
        /// <summary>Nhả claim — chỉ khi ĐANG do <paramref name="by"/> giữ (tránh xoá claim của nòng khác).</summary>
        public void ReleaseClaim(object by) { if (ReferenceEquals(_claim, by)) _claim = null; }

        public int StackCount => _blocks.Count;

        /// <summary>
        /// Offset từ TÂM cell tới block thứ i trong stack (0 = dưới cùng) — stack xếp theo trục Y.
        /// Trả offset chứ không trả vị trí world: cell còn trượt lúc dồn hàng, đạn phải bám theo cell
        /// (xem Bullet.Update) chứ không nhắm vào 1 điểm chết trong không gian.
        /// </summary>
        public Vector3 StackOffset(int i) => Vector3.up * _stackSpacing * Mathf.Max(0, i);
        /// <summary>Số block chưa bị đạn "đặt chỗ" (đạn đang bay) — gun chỉ bắn khi còn &gt; 0.</summary>
        public int Available => _blocks.Count - _pendingHits;
        public bool IsEmpty => _blocks.Count == 0;

        public void Build(BlockCellData data, float stackSpacing, Vector3 blockScale, GridBlockManager manager)
        {
            _manager = manager;
            _stackSpacing = stackSpacing;
            _blockScale = blockScale == Vector3.zero ? Vector3.one : blockScale;
            BlockCol = data.BlockCol;
            Depth = data.SpawnerDepth;
            // Nguồn tĩnh (Spawner8/SpawnerLine) = bất tử, đứng yên: chỉ là ô hiển thị "màu kế tiếp sẽ nhả
            // ra". Không bao giờ bị gun bắn trực tiếp; khi nhả hết sequence thì GridBlockManager tự despawn nó.
            Indestructible = data.Type.IsStaticSource();
            Frozen = data.Iced && data.IceThreshold > 0; // băng ngưỡng 0 = tan ngay, coi như không băng
            IceThreshold = data.IceThreshold;
            _pendingHits = 0;
            _claim = null;                   // item pooled tái dùng → xoá claim của cell cũ
            Generation++;                    // object pool tái dùng → đây là 1 cell MỚI
            SettleStamp = 0f;                // cell dựng lúc build = đã đứng sẵn từ đầu (không tính "vừa sập")
            PendingEntry = false;            // reset cho item pooled; MoveTo tự bật khi cell trượt
            ReleaseBlocks();                 // item pooled tái dùng: dọn stack cũ trước
            ShowSpawnerIndicator(false);

            Fill(data.Color, Mathf.Max(1, data.BlockStackCt));
        }

        /// <summary>
        /// Bật/tắt dấu hiệu "ô gốc Spawner" (ô cố định nhả cell ẩn ra) và quay nó theo hướng nhả.
        /// <para>Chỉ xoay quanh trục Y (world), CHỒNG lên pose gốc dựng trong prefab. Gán thẳng
        /// rotation = Euler(0,dirAngle,0) sẽ xoá luôn góc nghiêng đã dựng sẵn (mũi tên nằm phẳng trên
        /// sàn nhờ X=90) → mũi tên dựng đứng, camera top-down nhìn gần như không thấy.</para>
        /// <para>Đặt ở WORLD chứ không ăn theo cell: cell đứng ở ô gốc có thể là cell dồn từ hàng sau
        /// lên, mang góc riêng của nó — mũi tên phải theo hướng của NGUỒN, không phải của cell.</para>
        /// </summary>
        public void ShowSpawnerIndicator(bool on, float dirAngle = 0f, bool eightWay = false)
        {
            if (spawnerIndicator == null) return;
            spawnerIndicator.SetActive(on);
            if (!on) return;

            spawnerIndicator.transform.rotation =
                Quaternion.AngleAxis(dirAngle, Vector3.up) * _indicatorRestLocalRot;

            if (indicatorRenderer != null)
            {
                // Spawner8 = sprite spawner_amb_direction; còn lại (Spawner/SpawnerLine) = ARROW hướng.
                var sp = eightWay ? eightDirectionSprite
                                  : (lineArrowSprite != null ? lineArrowSprite : _defaultIndicatorSprite);
                if (sp != null) indicatorRenderer.sprite = sp;

                // Độ cao mũi tên = ĐỈNH block trên cùng (stack theo Y) + offset chỉnh tay. Xoay quanh Y không
                // đổi thành phần Y nên đặt local Y trực tiếp là đúng độ cao dù indicator đang xoay theo hướng.
                float topY = _stackSpacing * Mathf.Max(0, StackCount - 1) + indicatorHeightOffset;
                var lp = indicatorRenderer.transform.localPosition;
                indicatorRenderer.transform.localPosition = new Vector3(lp.x, topY, lp.z);
            }
        }

        // Dựng stack block cùng màu, gắn vào BlocksContainer của prefab (fallback: chính cell).
        private void Fill(TypeColor color, int n)
        {
            Color = color;
            var parent = blocksContainer != null ? blocksContainer : transform;
            for (int j = 0; j < n; j++)
            {
                var b = PoolManager.Instance.GetBlock();
                b.transform.SetParent(parent);
                // Set scale SAU khi SetParent: SetParent(worldPositionStays=true) bù localScale để giữ scale
                // world, nên phải gán đè thì scale của grid mới ăn. Cell không scale → local = world.
                b.transform.localScale = _blockScale;
                b.transform.position = transform.position + Vector3.up * _stackSpacing * j; // stack theo Y
                b.transform.rotation = transform.rotation;
                b.Init(this, j, Color);
                _blocks.Add(b);
            }
        }

        private void ReleaseBlocks()
        {
            foreach (var b in _blocks) if (b != null) b.Despawn();
            _blocks.Clear();
        }

        /// <summary>Trả cell (và toàn bộ block trong nó) về pool — thay cho Destroy.</summary>
        public void Despawn()
        {
            ReleaseBlocks();
            if (_pool != null) _pool.Release(this);
            else Destroy(gameObject);
        }

        /// <summary>Đặt chỗ 1 đạn đang bay tới cell này.</summary>
        public void ReserveHit() => _pendingHits++;

        /// <summary>Đạn tới nơi: trừ 1 pending + phá 1 block.</summary>
        public void ApplyHit()
        {
            if (_pendingHits > 0) _pendingHits--;
            HitOnce();
        }

        private void HitOnce()
        {
            if (_blocks.Count == 0) return;

            int last = _blocks.Count - 1;
            var b = _blocks[last];
            _blocks.RemoveAt(last);
            if (b != null) b.Despawn(); // trả block về pool

            if (_blocks.Count > 0) return;
            if (_manager != null) _manager.OnCellCleared(this);
        }

        public void MoveTo(Vector3 target, float duration)
        {
            if (_moveRoutine != null) StopCoroutine(_moveRoutine);
            SettleStamp = Time.time; // cell vừa SẬP/dời chỗ ở thời điểm này (gun dùng để xếp ưu tiên sau)
            if (!gameObject.activeInHierarchy || duration <= 0f)
            {
                transform.position = target;
                PendingEntry = false; // tới nơi ngay → cho ngắm
                return;
            }
            PendingEntry = true; // bắt đầu trượt → tạm khoá ngắm tới khi tới nơi
            _moveRoutine = StartCoroutine(MoveRoutine(target, duration));
        }

        private IEnumerator MoveRoutine(Vector3 target, float dur)
        {
            Vector3 start = transform.position;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                transform.position = Vector3.Lerp(start, target, t / dur);
                yield return null;
            }
            transform.position = target;
            _moveRoutine = null;
            PendingEntry = false; // đã trượt xong về đúng ô → giờ mới cho gun ngắm
        }
    }
}

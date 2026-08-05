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

        [Header("FX nước bám đạn (Projectiles_water)")]
        [Tooltip("BẬT = dùng RIG TRAIL DÙNG CHUNG của FxController (FxType.BulletTrail): mọi viên đạn rải particle " +
                 "vào 1 bộ ParticleSystem chung → draw call ~cố định dù bắn loạt. Rig giữ đúng thông số prefab " +
                 "Projectiles_water (start speed/shape/rate) và bám đạn qua inherit velocity. FX con trong đạn được " +
                 "TẮT khi dùng rig. TẮT cờ này (hoặc rig chưa cấu hình) → chạy FX con native (draw call theo số đạn).")]
        [SerializeField] private bool useSharedTrail = true;
        [Tooltip("Cứ bay được quãng đường này (world units) thì rải 1 nhịp particle vào rig trail chung. " +
                 "Nhỏ = mật độ vệt dày/mượt hơn nhưng nhiều lần Emit hơn. Chỉ dùng khi Use Shared Trail bật.")]
        [SerializeField] private float trailStepDistance = 0.2f;

        // FX nước con của prefab: chạy NATIVE (đúng thông số, Local space bám đạn) khi KHÔNG dùng rig chung.
        private ParticleSystem[] _fxSystems;
        private GameObject _fxRoot;     // GO gốc của FX con (để bật/tắt khi chọn rig vs native)
        private bool _useRig;           // viên này đang dùng rig trail chung?
        private Vector3 _trailLastPos;  // vị trí lần rải trail gần nhất → đo quãng đường bước kế

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
            _fxSystems = GetComponentsInChildren<ParticleSystem>(true); // FX nước con (Projectiles_water)
            // GO gốc của FX con = tổ tiên cao nhất còn nằm dưới viên đạn (để bật/tắt cả cụm khi chọn rig vs native).
            if (_fxSystems != null && _fxSystems.Length > 0)
            {
                Transform t = _fxSystems[0].transform;
                while (t.parent != null && t.parent != transform) t = t.parent;
                _fxRoot = t.gameObject;
            }
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

            // Chọn nguồn FX nước: rig trail dùng chung (ít draw call) nếu bật + đã cấu hình, ngược lại FX con native.
            _useRig = useSharedTrail && FxController.Instance != null
                      && FxController.Instance.HasTrailRig(FxType.BulletTrail);

            if (_useRig)
            {
                // Dùng rig chung: TẮT FX con để khỏi phát trùng, rồi bắn nhịp burst (head/core) tại nòng.
                if (_fxRoot != null) _fxRoot.SetActive(false);
                _trailLastPos = start;
                Vector3 launchVel = firstTarget != start ? (firstTarget - start).normalized * speed : Vector3.zero;
                FxController.Instance.EmitTrailBurst(FxType.BulletTrail, start, launchVel);
            }
            else if (_fxSystems != null)
            {
                // FX con native: bật lại cụm rồi restart sạch tại nòng. Clear() SAU khi đã set position (giống
                // _trail.Clear ở trên) để đạn pooled không kéo particle từ vị trí lượt trước; Play() phát đúng thông số prefab.
                if (_fxRoot != null) _fxRoot.SetActive(true);
                for (int i = 0; i < _fxSystems.Length; i++)
                {
                    ParticleSystem ps = _fxSystems[i];
                    if (ps == null) continue;
                    ps.Clear(true);
                    ps.Play(true);
                }
            }

            // Material lấy từ GlobalConfigManager theo TypeColor.
            if (_renderer == null) _renderer = FindBodyRenderer();
            if (_renderer != null) _renderer.enabled = true; // Bullet pooled: bật lại thân đạn đã ẩn ở lượt trước.
            //var mat = GlobalConfigManager.MaterialOf(color, TypeObject.Bullet);
            //if (_renderer != null && mat != null) _renderer.sharedMaterial = mat;
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
                // Tới đích: đáp đúng target rồi phá block. Rải nốt đoạn cuối để vệt không hụt — nhưng cấp
                // vận tốc = 0 (KHÔNG dùng đoạn/dt như lúc bay): particle inherit velocity, cho tiến tới thì
                // nước phun VỌT QUA block. Zero → nước đọng ngay tại điểm chạm rồi tự tan (giống "tắt" FX
                // ở đầu bay). Rig dùng chung không xoá riêng particle của viên này được nên chỉ chặn được
                // phần sinh MỚI tại đây; các hạt đã rải dọc đường tự hết theo startLifetime.
                transform.position = target;
                ShedTrail(target, Vector3.zero);
                _active = false;
                if (_hitBottom) _cell.ApplyHitBottom(); else _cell.ApplyHit(); // trừ 1 block + huỷ pending
                GameController.Instance?.OnBoardChanged();
                // Trúng block: TẮT FX nước, ẩn thân đạn, đứng im tại điểm trúng vài giây rồi mới trả về pool.
                StopFx();
                if (_renderer != null) _renderer.enabled = false;
                _lingering = true;
                _lingerTimer = lingerDuration;
                return;
            }

            // Nội suy ngang start→target theo t, cộng vòng cung Y đỉnh giữa đường: 4t(1−t) = 1 khi t=0.5,
            // = 0 ở 2 đầu → đạn vọt lên rồi rơi xuống đúng target. Cộng SAU Lerp nên độc lập chênh cao 2 đầu.
            Vector3 pos = Vector3.Lerp(_start, target, t);
            pos.y += _arcPeak * 4f * t * (1f - t);

            // Xoay đạn theo hướng bay: dùng vector di chuyển thực (pos mới − vị trí hiện tại) nên đầu đạn
            // luôn chúc theo tiếp tuyến vòng cung — vọt lên lúc đầu, chúc xuống khi rơi vào target.
            Vector3 dir = pos - transform.position;
            if (dir.sqrMagnitude > 1e-8f) transform.rotation = Quaternion.LookRotation(dir);

            // Vận tốc world frame này = quãng đi / dt → rig dùng để particle inherit, bay CÙNG đạn.
            Vector3 vel = Time.deltaTime > 0f ? dir / Time.deltaTime : Vector3.zero;
            transform.position = pos;
            ShedTrail(pos, vel); // rải vệt nước dọc đường bay vào rig trail dùng chung (chỉ khi _useRig)
        }

        // Rải particle trail vào rig chung mỗi khi bay đủ trailStepDistance (mật độ vệt ~cố định theo quãng đường,
        // độc lập frame rate). velocity truyền cho rig để particle inherit → bay cùng đạn. Chỉ chạy khi dùng rig.
        private void ShedTrail(Vector3 pos, Vector3 velocity)
        {
            if (!_useRig) return;

            float d = Vector3.Distance(pos, _trailLastPos);
            if (d < trailStepDistance) return;

            // Rải particle DỌC đoạn _trailLastPos -> pos (không dồn hết vào pos) → vệt rate-over-distance liền mạch.
            FxController.Instance.EmitTrail(FxType.BulletTrail, _trailLastPos, pos, velocity);
            _trailLastPos = pos;
        }

        // Trúng block → tắt FX nước bám đạn để không còn particle nào phun ra trong lúc đạn đứng im (linger).
        private void StopFx()
        {
            // Đường rig dùng chung: particle nằm trong buffer CHUNG của mọi viên → KHÔNG xoá ở đây (xoá cả
            // buffer sẽ mất luôn vệt của các viên khác đang bay). _active=false đã ngừng rải; vệt tự tan
            // theo startLifetime ngắn của prefab.
            if (_useRig) return;

            // FX con native (không dùng rig): mỗi viên có particle riêng → chỉ cần dừng+xoá của viên này.
            if (_fxSystems == null) return;
            for (int i = 0; i < _fxSystems.Length; i++)
            {
                ParticleSystem ps = _fxSystems[i];
                if (ps == null) continue;
                // StopEmittingAndClear: ngừng phát VÀ xoá sạch particle đang tồn tại ngay lập tức.
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
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

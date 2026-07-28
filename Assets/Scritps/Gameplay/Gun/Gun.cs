using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using System;
using System.Threading;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Wayfu.Lamkn
{
    /// <summary>
    /// Gun: nằm trong slot → click để ra path → chạy loop liên tục (RoundedPolylineFollower) và tự bắn
    /// cell ngoài cùng gần nhất cùng màu TRONG TẦM BẮN; hết đạn thì biến mất (yêu cầu #3, #7).
    /// </summary>
    public class Gun : MonoBehaviour, IItemPool<Gun>
    {
        private enum GunState { InSlot, Queued, OnPath, Dead }

        public GunData Data { get; private set; }
        public TypeColor Color => Data.Color;
        public GunSlot Slot { get; private set; }

        [Header("Nòng bắn (2 bên)")]
        [Tooltip("Điểm đạn xuất phát của nòng BÊN PHẢI (+X local của gun). Bỏ trống → bắn từ gốc gun.")]
        [SerializeField] private Transform muzzleRight;
        [Tooltip("Điểm đạn xuất phát của nòng BÊN TRÁI (−X local của gun). Bỏ trống → bắn từ gốc gun.")]
        [SerializeField] private Transform muzzleLeft;
        [Tooltip("Mất target bao lâu (giây) thì coi như quạt đã trôi qua grid → khoá bắn tới hết vòng. " +
                 "PHẢI lớn hơn GameSettings.BlockCollapseDuration: lúc cột đang dồn mọi cell đều " +
                 "PendingEntry nên target hụt trong chốc lát là bình thường, khoá ngay là gun chết oan giữa cột.")]
        [SerializeField] private float targetLostGrace = 0.4f;

        [Header("Hiển thị")]
        [Tooltip("Text đếm số đạn — gán sẵn trên prefab ('Text (TMP)'). Bỏ trống sẽ tự tìm TMP_Text " +
                 "trong children.")]
        [SerializeField] private TMP_Text bulletLabel;
        [Tooltip("Material dùng khi gun ẨN chưa ra vị trí đầu (che màu thật). Bỏ trống thì gun ẩn vẫn hiện màu.")]
        [SerializeField] private Material hiddenMaterial;
        [Tooltip("Các MeshRenderer cần tô màu theo TypeColor — kéo thả trong Inspector. Tô cho TẤT CẢ material " +
                 "slot của mỗi renderer. Bỏ trống → tự gom mọi renderer trong children (trừ TMP_Text).")]
        [SerializeField] private MeshRenderer[] colorRenderers;

        [Header("Laser (chỉ dùng khi FireMode = Laser)")]
        [Tooltip("Material của tia laser. Bỏ trống → dùng material màu Bullet của TypeColor gun (như đạn).")]
        [SerializeField] private Material laserMaterial;
        [Tooltip("Độ dày tia laser (world units).")]
        [SerializeField] private float laserWidth = 0.15f;
        [Tooltip("Nâng điểm cuối tia lên so với gốc cell (world units) — ngắm vào thân stack cho đẹp.")]
        [SerializeField] private float laserAimHeight = 0.25f;
        [Tooltip("Giữ tia thêm ngần này (giây) sau khi cell vỡ, trong lúc chờ chốt cell kế → tia NỐI LIỀN, " +
                 "không chớp tắt giữa 2 cell. Nên ≥ 2-3 frame (~0.05). Quá lớn thì tia còn treo khi thật " +
                 "sự hết target.")]
        [SerializeField] private float laserLinger = 0.06f;
        [Tooltip("Thời gian CỐ ĐỊNH (giây) để laser nổ HẾT 1 cell, chia đều cho số block trong cell → cell " +
                 "cao hay thấp đều nổ trong ngần này (thay cho FireInterval mỗi block). Nhỏ = nổ nhanh.")]
        [SerializeField] private float laserCellTime = 0.15f;

        /// <summary>Arc-length hiện tại trên path — PathManager đọc để giữ khoảng cách giữa các gun.</summary>
        public float PathDistance => _follower != null ? _follower.CurrentDistance : 0f;
        public bool IsOnPath => _state == GunState.OnPath;
        public bool IsDead => _state == GunState.Dead;
        /// <summary>Số VÒNG đã chạy trên path (mốc để biết gun vừa lap qua điểm path0). 0 khi ở slot.</summary>
        public int LapCount => _follower != null ? _follower.LapCount : 0;

        private GunState _state = GunState.InSlot;
        private GunFireConfig _fire = GunFireConfig.FromSettings(null);
        private Renderer[] _renderers;
        private Coroutine _moveRoutine;
        private Pooler<Gun> _pool;
        private RoundedPolylineFollower _follower;
        private int _lastLap;             // vòng path đã chạy, mốc để mở khoá bắn
        private float _lapStartStamp;     // Time.time lúc bắt đầu lap hiện tại — mốc PER-GUN phân biệt cell
                                          // "đã đứng sẵn" (SettleStamp ≤ mốc) vs "vừa sập trong lap này". Reset
                                          // mỗi lap → cell sập lap trước thành ready. (dùng cho laser readyBefore)
        private bool _atFront;            // gun đang ở VỊ TRÍ ĐẦU (index 0) của slot → gun ẩn lộ màu thật

        /// <summary>
        /// Một bên nòng. Mỗi bên có target + nhịp bắn RIÊNG và quạt hướng ra sườn gun (±X local), nên
        /// gun chạy dọc path là quét được cả 2 phía cùng lúc mà không phải quay mặt.
        /// </summary>
        /// 
        private Barrel barrel = null;
        [Serializable]        
        
        private class Barrel
        {
            public float Sign;        // +1 = phải (+X local), −1 = trái (−X local)
            public Transform Muzzle;
            public BlockCell Target;
            public int TargetGen;     // Generation lúc chốt — lệch = object pool đã thành cell khác
            public float FireTimer;
            public bool Armed;        // còn lượt bắn của vòng này không
            public bool HadTarget;    // vòng này đã bắt được cell nào chưa
            public float IdleTimer;   // quạt trống liên tục bao lâu rồi
            public bool FiredAtTarget; // đã nổ ÍT NHẤT 1 phát vào target hiện tại chưa — phân biệt cell
                                       // "bắn dở" (phải bắn hết) với cell mới chỉ CHỐT (qua vòng là bỏ)
            public bool MultiSide;    // target hiện/vừa rồi thuộc grid bị path bao nhiều mặt → gun đang đi
                                      // vòng quanh nó, KHÔNG tự khoá "1 lượt/vòng" mà bắt tiếp mặt kế
            public LineRenderer Beam; // tia laser của nòng (mode Laser) — tạo lười, tái dùng theo item pool
            public Vector3 BeamTo;    // điểm cuối tia lần vẽ gần nhất — giữ trong lúc linger (chuyển cell)
            public float BeamHold;    // còn giữ tia bao lâu dù đã mất target (bắc cầu qua lúc chốt cell kế)
            public float LaserInterval; // nhịp gặm/block của cell ĐANG bám = laserCellTime / số block lúc chốt
        }

        private readonly Barrel _right = new Barrel { Sign = 1f };
        private readonly Barrel _left = new Barrel { Sign = -1f };

        /// <summary>
        /// Target của nòng còn sống và đúng màu — object pooled có thể đã thành cell khác. PendingEntry
        /// (cell đang TRƯỢT lúc dồn hàng) coi như không còn sống: cell front đang bắn không bao giờ trượt
        /// (nó ở row 0, chẳng có gì tiến vào), nên check này chỉ loại target lộ ra thoáng qua khi dồn.
        /// </summary>
        private bool HasLiveTarget(Barrel b) => b.Target != null && b.Target.Generation == b.TargetGen
                                                && !b.Target.IsEmpty && !b.Target.PendingEntry
                                                && b.Target.Color == Data.Color;

        /// <summary>Góc toả tối đa của 1 nòng: quá 180° là đã kín nửa mặt phẳng của nó, không thêm được gì.</summary>
        private float Spread => Mathf.Clamp(_fire.Angle, 0f, 180f);

        public void OnInitializedInPool(Pooler<Gun> pool) => _pool = pool;

        private void Awake()
        {
            // Tắt follower khi ở slot; PathManager bật lại (DeployOnPath) khi gun được click lên path.
            _follower = GetComponentInChildren<RoundedPolylineFollower>(true);
            if (_follower != null) _follower.enabled = false;

            _right.Muzzle = muzzleRight;
            _left.Muzzle = muzzleLeft;

            CollectRenderers();

            // Collider của prefab thường nằm ở CHILD (vd "Model") → OnMouseDown gửi tới child, không tới
            // script Gun ở root. Gắn relay lên mọi collider để forward click về đây (yêu cầu click→deploy).
            foreach (var col in GetComponentsInChildren<Collider>(true))
            {
                var relay = col.GetComponent<GunClickRelay>();
                if (relay == null) relay = col.gameObject.AddComponent<GunClickRelay>();
                relay.Owner = this;
            }
        }

        /// <summary>
        /// Nguồn renderer để tô màu theo TypeColor. Ưu tiên <see cref="colorRenderers"/> đã kéo thả trong
        /// Inspector (chỉ định đúng renderer cần đổi). Bỏ trống thì tự gom MỌI renderer trong children —
        /// loại renderer của TMP_Text ra: nó dùng material font, đè material gun vào là mất chữ.
        /// </summary>
        private void CollectRenderers()
        {
            if (colorRenderers != null && colorRenderers.Length > 0)
            {
                _renderers = colorRenderers;
                return;
            }
            var list = new List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r.GetComponent<TMP_Text>() == null) list.Add(r);
            _renderers = list.ToArray();
        }

        public void Init(GunData data, GunFireConfig fire)
        {
            Data = new GunData { Color = data.Color, CountBullet = data.CountBullet, Hidden = data.Hidden, ConnectGroup = data.ConnectGroup };
            _fire = fire;
            _atFront = false; // item pooled tái dùng: mặc định CHƯA ở đầu; slot gọi SetAtFront sau Fill

            // Reset trạng thái (item pooled có thể tái dùng).
            _lastLap = 0;
            ResetBarrel(_right);
            ResetBarrel(_left);
            ArmForNewLap();
            if (_moveRoutine != null) { StopCoroutine(_moveRoutine); _moveRoutine = null; }

            // Item pooled tái dùng: gun vừa chạy trên path mang theo rotation của khúc đường CUỐI
            // (RoundedPolylineFollower ghi thẳng vào root). Không reset thì vào màn/retry mỗi khẩu trong
            // slot quay một kiểu theo chỗ nó chết ở lượt trước.
            // localRotation: GunSlot.Fill đã SetParent vào slot TRƯỚC khi gọi Init → gun thẳng hàng theo
            // slot, slot có xoay thì gun xoay theo.
            transform.localRotation = Quaternion.identity;

            // Material lấy từ GlobalConfigManager theo TypeColor (không tô material.color nữa).
            // sharedMaterial chỉ thay slot 0 — 'machine' có 2 slot, slot 1 (viền/chi tiết) giữ nguyên.
            ApplyColorVisual();

            EnsureLabel();
            UpdateLabel();
            _state = GunState.InSlot;

            // Item pooled tái dùng: tắt follower để gun đứng yên trong slot (bật lại khi deploy).
            if (_follower != null) _follower.enabled = false;
        }

        public void SetSlot(GunSlot s) => Slot = s;

        // Tô material cho gun: gun ẨN & CHƯA ra vị trí đầu → material 'hidden' (che màu); còn lại = màu thật.
        // Tô cho TẤT CẢ material slot của mỗi renderer (không chỉ slot 0) — 'machine', body, eye... có nhiều
        // slot, đè hết để cả khối đổi màu đồng nhất.
        private void ApplyColorVisual()
        {
            if (_renderers == null) CollectRenderers();
            Material mat = Data != null && Data.Hidden && !_atFront && hiddenMaterial != null
                ? hiddenMaterial
                : GlobalConfigManager.MaterialOf(Data != null ? Data.Color : TypeColor.None, TypeObject.Gun);
            if (mat == null) return;
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }
        }

        /// <summary>Slot báo gun này có đang ở VỊ TRÍ ĐẦU (index 0) không → gun ẩn lộ/che màu theo đó.</summary>
        public void SetAtFront(bool front)
        {
            if (_atFront == front) return;
            _atFront = front;
            if (Data != null && Data.Hidden) ApplyColorVisual();
        }

        // Được gọi từ GunClickRelay (collider ở child) hoặc trực tiếp nếu collider nằm cùng GO.
        public void HandleClick()
        {
            if (_state == GunState.InSlot) SlotManager.Instance?.OnGunClicked(this);
        }

        private void OnMouseDown() => HandleClick();

        /// <summary>Tách khỏi slot nhưng CHƯA lên path — gun đang xếp hàng chờ đủ khoảng cách.</summary>
        public void OnQueued()
        {
            _state = GunState.Queued;
            Slot = null;
            transform.SetParent(null);
            ResetBarrel(_right);
            ResetBarrel(_left);
        }

        public void OnDeployed()
        {
            _state = GunState.OnPath;
            Slot = null;
            transform.SetParent(null);
            ResetBarrel(_right);
            ResetBarrel(_left);
            // Gun chờ trong queue được MoveTo tới chỗ đứng; coroutine đó vẫn ghi transform.position mỗi
            // frame → phải dừng, không thì nó giành position với follower.
            if (_moveRoutine != null) { StopCoroutine(_moveRoutine); _moveRoutine = null; }
        }

        /// <summary>Bật RoundedPolylineFollower cho gun chạy vòng path liên tục từ startDistance (yêu cầu #3).</summary>
        public void DeployOnPath(RoundedPolylinePath path, float startDistance, float speed)
        {
            _lastLap = 0;   // follower.Init đưa LapCount về 0 — mốc đếm vòng bắt đầu từ đây
            _lapStartStamp = Time.time; // mốc "ready" ban đầu: mọi cell đang có coi như đã đứng sẵn
            ArmForNewLap(); // vào path tại pos 0 = bắt đầu lượt bắn đầu tiên
            if (_follower != null) { _follower.Init(path, startDistance, speed); _follower.enabled = true; }
            else if (path != null) transform.position = path.GetPointAtDistance(startDistance); // gun ko có follower
        }

        private void Update()
        {
            if (_state != GunState.OnPath)
            {
                // Không trên path (trong slot / chờ / chết) → tắt tia nếu đang bật.
                DisableBeam(_right);
                DisableBeam(_left);
                return;
            }

            // Mỗi vòng path, MỖI NÒNG chỉ được 1 lượt bắn. Về tới pos 0 (xong 1 vòng) = mở khoá lượt mới.
            int lap = _follower != null ? _follower.LapCount : 0;
            if (lap != _lastLap)
            {
                _lastLap = lap;
                _lapStartStamp = Time.time; // gun đi hết path vòng lại → RESET mốc ready: cell sập lap trước
                                            // giờ tính là đã đứng sẵn (ưu tiên như thường trở lại)
                ArmForNewLap();
            }

            // Nòng phải chạy trước, rồi tới nòng trái — mỗi bên nhận nòng kia để vừa loại trừ target
            // trùng, vừa chừa đủ đạn cho nó (xem TickBarrel).
            TickBarrel(_right, _left);
            if (_state != GunState.OnPath) return; // hết đạn giữa chừng → Die() đã despawn gun
            TickBarrel(_left, _right);

            // Vẽ tia laser SAU khi 2 nòng đã cập nhật target (mode Laser); mode khác thì tia luôn tắt.
            UpdateBeam(_right);
            UpdateBeam(_left);
        }

        /// <summary>Số đạn nòng này còn cần để bắn dứt điểm cell đang bám. 0 = đang rảnh.</summary>
        private int NeedOf(Barrel b) => HasLiveTarget(b) ? Mathf.Max(0, b.Target.Available) : 0;

        /// <summary>Chạy 1 bên nòng.</summary>
        private void TickBarrel(Barrel b, Barrel other)
        {
            if (Data.CountBullet <= 0) return; // hết đạn (gun connect đứng chờ cả nhóm) → không ngắm/bắn nữa
            bool justAcquired = false; // vừa CHỐT target mới ở frame này → chưa bắn (chờ 1 frame cho ổn)
            // Chỉ được CHỌN target mới khi cell đang bám đã bị phá HẾT (dứt điểm từng cell) VÀ nòng còn
            // lượt của vòng này. Hết lượt (!Armed) thì KHÔNG nhặt cell mới — nhưng cell đang bắn DỞ vẫn
            // được bắn nốt ở khối dưới; bắn xong thì target tự về null và nòng im tới khi qua vòng mới.
            if (!HasLiveTarget(b))
            {
                b.Target = null;
                b.TargetGen = 0;
                b.FiredAtTarget = false;

                // Quét target trong tầm TRƯỚC. Nếu có mà nòng đang KHÓA (đã hết lượt của vòng này) thì MỞ
                // KHÓA NGAY — khỏi chờ gun chạy hết 1 vòng path mới bắt được cell đã vào range từ lâu.
                // losFrom = vị trí NÒNG bên này: CELL KHÁC đứng chắn chỉ chặn nòng có tia muzzle→cell
                // bị cắt, nòng bên kia không vướng vẫn bắn được (range/quạt vẫn tính từ tâm gun như cũ).
                // Laser: ưu tiên cell đã đứng sẵn TỪ ĐẦU LAP của gun này (SettleStamp ≤ _lapStartStamp),
                // cell vừa sập trong lap này bắn sau. readyBefore = -1 (tắt) cho mode đạn. PER-GUN + reset
                // mỗi lap: xem _lapStartStamp.
                float readyBefore = _fire.Mode == GunFireMode.Laser ? _lapStartStamp : -1f;
                var cand = GridBlockManager.Instance?.FindTargetCell(
                    Data.Color, transform.position, transform.forward, b.Sign, _fire.Range, _fire.Angle,
                    other.Target, b.Muzzle != null ? b.Muzzle.position : (Vector3?)null,
                    readyBefore);
                if (cand != null && !b.Armed) { b.Armed = true; b.HadTarget = false; b.IdleTimer = 0f; }

                if (b.Armed)
                {
                    // Quạt CHỈ lọc lúc CHỌN target (bộ lọc nằm trong FindTargetCell). Đã chốt được cell
                    // thì bắn DỨT ĐIỂM hết stack, kể cả khi gun đã trôi qua và cell ra ngoài quạt.
                    bool sawCell = cand != null;

                    // NHƯỜNG ĐẠN: nòng kia đang bám cell thì phải chừa đủ đạn cho nó bắn dứt điểm cell đó.
                    // Phần còn lại không đủ nuốt trọn cell này thì THÔI CHỐT — 2 nòng cùng bắn dở 2 cell
                    // rồi hết đạn thì chẳng cell nào vỡ, cell dở còn chặn luôn cell phía sau.
                    // Chỉ chặn khi nòng kia ĐANG bận (need > 0): nó rảnh mà cũng chặn thì mấy viên đạn
                    // cuối không nòng nào dám bắn, gun chết với đạn còn nguyên.
                    int reserved = NeedOf(other);
                    if (cand != null && reserved > 0 && cand.Available > Data.CountBullet - reserved) cand = null;

                    b.Target = cand;
                    b.TargetGen = cand != null ? cand.Generation : 0;
                    justAcquired = cand != null;
                    if (cand != null) b.MultiSide = cand.MultiSideGrid;

                    // Laser: chốt cell mới → chia thời gian nổ CỐ ĐỊNH (laserCellTime) đều cho số block
                    // của cell lúc này, ra nhịp gặm/block. Cell cao/thấp đều nổ trong laserCellTime.
                    if (cand != null && _fire.Mode == GunFireMode.Laser)
                    {
                        b.LaserInterval = laserCellTime / Mathf.Max(1, cand.StackCount);
                        b.FireTimer = 0f; // bắn phát đầu ngay frame kế (không dính nhịp cell trước)
                    }

                    // sawCell (không phải b.Target): nòng nhường đạn vẫn coi như "còn thấy grid" → không
                    // tính là hết lượt, để khi nòng kia bắn xong và đạn rảnh ra thì nó vào cuộc được ngay.
                    if (sawCell) { b.HadTarget = true; b.IdleTimer = 0f; }
                    else if (b.HadTarget)
                    {
                        // Grid bị path bao nhiều mặt: gun đi VÒNG QUANH nó, mỗi lúc đối 1 mặt. Quạt trống
                        // giữa 2 mặt KHÔNG phải "đã đi qua grid" → đừng khoá; reset để bắt mặt kế tiếp khi
                        // gun vòng tới (chỉ bắn mặt đang đối diện, không xuyên qua bắn mặt sau — lọc trong
                        // IsShootableFromGun). Grid thường (1 mặt) vẫn giữ luật 1 lượt/vòng như cũ.
                        if (b.MultiSide) { b.HadTarget = false; b.IdleTimer = 0f; b.MultiSide = false; }
                        else
                        {
                            // Đã bắn xong cell của mình mà quạt không còn gì để chốt tiếp → nòng đã đi qua
                            // grid. HẾT LƯỢT: khoá tới hết vòng. Chờ targetLostGrace mới khoá: cột đang dồn
                            // thì cell nào cũng PendingEntry, quạt trống trong chốc lát là bình thường.
                            b.IdleTimer += Time.deltaTime;
                            if (b.IdleTimer >= targetLostGrace) b.Armed = false;
                        }
                    }
                }
            }

            b.FireTimer -= Time.deltaTime;
            // Bắn cell đang bám (kể cả khi đã hết lượt — cell dở phải được bắn hết). Chỉ bắn khi cell
            // còn block CHƯA bị đạn đang bay đặt chỗ (tránh bắn dư). KHÔNG bắn ở frame vừa chốt target:
            // cell lộ ra thoáng qua lúc dồn hàng (transient) sẽ bị thay ở frame sau → không phí đạn bắn nhầm.
            if (b.Target != null && !justAcquired && b.FireTimer <= 0f && b.Target.Available > 0)
            {
                if (_fire.Mode == GunFireMode.Laser)
                {
                    LaserHit(b);                 // tia gặm 1 block, không sinh viên đạn
                    b.FireTimer = b.LaserInterval; // nhịp = laserCellTime / số block → cả cell nổ đúng laserCellTime
                }
                else
                {
                    Fire(b);
                    b.FireTimer = _fire.Interval;
                }
            }
        }

        private static void ResetBarrel(Barrel b)
        {
            b.Target = null;
            b.TargetGen = 0;
            b.FireTimer = 0f;
            b.Armed = true;
            b.HadTarget = false;
            b.IdleTimer = 0f;
            b.FiredAtTarget = false;
            b.MultiSide = false;
            b.BeamHold = 0f; // gun tái dùng: không treo tia laser của lượt trước
            b.LaserInterval = 0f;
        }

        /// <summary>
        /// Mở khoá bắn cho vòng path mới (gun vừa về lại pos 0). Nòng bị khoá ở vòng trước được bật lại
        /// và chọn target từ đầu (kiểm tra range như thường). Cell đang bắn DỞ từ vòng trước thì GIỮ
        /// NGUYÊN — vứt ở đây là cell chết dở nằm chặn cột, vi phạm luật dứt điểm từng cell; nòng sẽ bắn
        /// hết nó rồi mới chọn cell mới.
        /// </summary>
        private void ArmForNewLap()
        {
            RearmBarrel(_right);
            RearmBarrel(_left);
        }

        private void RearmBarrel(Barrel b)
        {
            b.Armed = true;
            b.HadTarget = false;
            b.IdleTimer = 0f;
            // Chỉ giữ cell đang bắn DỞ (đã nổ ít nhất 1 phát) để bắn hết. Cell mới CHỐT mà chưa bắn phát
            // nào thì bỏ — không thì vừa quay lại loop gun đã nã vào target rất xa từ vòng trước, thay vì
            // chọn lại từ đầu theo range.
            if (!HasLiveTarget(b) || !b.FiredAtTarget)
            {
                b.Target = null;
                b.TargetGen = 0;
                b.FiredAtTarget = false;
            }
        }

        /// <summary>
        /// Cell có nằm trong vùng CHỌN target của nòng này không: bán kính _fire.Range, quạt tính TỪ hướng
        /// trước mặt của gun (thân bám path) rồi toả sang sườn của nòng đúng <see cref="Spread"/> độ. Đo
        /// trên sàn XZ (bỏ qua chênh lệch Y).
        /// <para>Chỉ dùng cho gizmo — vùng này KHÔNG gate phát bắn: chốt được cell rồi thì bắn dứt điểm
        /// dù đã trôi ra ngoài. Việc lọc lúc chọn nằm trong <see cref="GridBlockManager.FindTargetCell"/>;
        /// công thức 2 bên phải khớp nhau thì gizmo mới nói đúng sự thật.</para>
        /// </summary>
        private bool InDetectZone(BlockCell cell, Barrel b)
        {
            if (cell == null) return false;
            Vector3 d = cell.transform.position - transform.position; d.y = 0f;
            float sqr = d.sqrMagnitude;
            if (sqr > _fire.Range * _fire.Range) return false;
            if (sqr < 1e-6f) return true;
            if (Vector3.Dot(transform.right, d) * b.Sign < 0f) return false; // sai sườn
            return Vector3.Dot(transform.forward, d) >= Mathf.Cos(Spread * Mathf.Deg2Rad) * Mathf.Sqrt(sqr);
        }

        private void Fire(Barrel b)
        {
            b.FiredAtTarget = true; // từ giờ cell này là "bắn dở" — phải bắn hết, không được bỏ giữa chừng

            // BurstPerCell: nhả TRỌN 1 loạt đúng bằng số block cell còn nợ (Available), mỗi viên nhắm 1
            // block trong stack → cả cell vỡ trong 1 lượt. Kẹp theo CountBullet phòng khi băng không đủ.
            int shots = _fire.Mode == GunFireMode.BurstPerCell
                ? Mathf.Min(b.Target.Available, Data.CountBullet)
                : 1;

            // Block bị phá từ TRÊN xuống (xem BlockCell.HitOnce) → viên đầu nhắm block trên cùng, viên sau
            // lùi dần xuống. Chốt 'top' trước vòng lặp: ReserveHit không đổi StackCount nên nó đứng yên.
            int top = Mathf.Max(0, b.Target.StackCount - 1);
            for (int i = 0; i < shots; i++) FireOne(b, Mathf.Max(0, top - i));

            UpdateLabel();
            if (Data.CountBullet <= 0) OnEmptied();
        }

        public bool HasBullets => Data != null && Data.CountBullet > 0;

        // Gun hết đạn: gun connect KHÔNG tự hủy — SlotManager chỉ hủy khi CẢ NHÓM hết đạn (giữ chỗ trên path
        // tới lúc đó). Gun thường thì hủy ngay như cũ.
        private void OnEmptied()
        {
            if (Data.ConnectGroup != 0 && SlotManager.IsActive)
            {
                SlotManager.Instance.OnConnectGunEmptied(this);
                return;
            }
            Die();
        }

        /// <summary>Hủy gun ngay (SlotManager gọi khi cả nhóm connect đã hết đạn).</summary>
        public void Kill() => Die();

        /// <summary>
        /// Đẩy hàng đạn tiến sẵn về phía target: hàng đáy (blockIndex 0) đứng nguyên, mỗi hàng lên cao
        /// thêm BurstRowLead nữa. Gần đích hơn ⇒ tới TRƯỚC, nên stack vỡ dần từ trên xuống thay vì nổ
        /// một phát cả cột.
        /// Kẹp ở 80% quãng đường: lead quá tay là đạn sinh ngay sát (hoặc quá) block, chạm đích tức thì
        /// và mất luôn cái vệt bay.
        /// </summary>
        private Vector3 RowLeadOffset(Vector3 to, Vector3 from, int blockIndex)
        {
            if (_fire.BurstRowLead <= 0f || blockIndex <= 0) return Vector3.zero;
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            if (dist < 1e-4f) return Vector3.zero;
            return dir / dist * Mathf.Min(_fire.BurstRowLead * blockIndex, dist * 0.8f);
        }

        private void FireOne(Barrel b, int blockIndex)
        {
            Data.CountBullet--;
            b.Target.ReserveHit();

            var bullet = PoolManager.Instance != null ? PoolManager.Instance.GetBullet() : null;
            Vector3 aim = b.Target.StackOffset(blockIndex);        // lệch tới block trong stack
            Vector3 from = b.Muzzle != null ? b.Muzzle.position : transform.position;

            // BurstSpawnStacked: sinh viên đạn sẵn ở ĐÚNG độ cao của block nó nhắm → cả loạt xếp thành
            // cột ngay tại nòng rồi bay NGANG sang, không toả chéo. Chỉ có nghĩa khi bắn loạt: mode
            // Single luôn chỉ 1 viên nhắm block trên cùng, nâng nó lên chỉ làm đạn xuất phát lơ lửng.
            if (_fire.BurstSpawnStacked && _fire.Mode == GunFireMode.BurstPerCell)
            {
                from += aim;
                from += RowLeadOffset(b.Target.transform.position + aim, from, blockIndex);
            }

            if (bullet != null)
                bullet.Launch(from, b.Target, _fire.BulletSpeed, Data.Color, aim);
            else
            {
                b.Target.ApplyHit(); // fallback không có pool
                GameController.Instance?.OnBoardChanged();
            }
        }

        /// <summary>
        /// 1 nhịp laser: gặm NGAY 1 block của cell đang bám (không sinh viên, tia chạm tức thì). Cell vỡ
        /// hết thì frame sau TickBarrel tự chốt cell kế trong tầm → tia nối liền sang, nhìn không ngắt.
        /// Mỗi block vẫn trừ 1 CountBullet như đạn thường; hết đạn thì gun rời/huỷ như cũ.
        /// </summary>
        private void LaserHit(Barrel b)
        {
            b.FiredAtTarget = true;
            Data.CountBullet--;
            b.Target.ApplyHit(); // không ReserveHit: tia không có thời gian bay nên phá thẳng
            GameController.Instance?.OnBoardChanged();
            UpdateLabel();
            if (Data.CountBullet <= 0) OnEmptied();
        }

        /// <summary>
        /// Vẽ tia laser của nòng: từ muzzle (điểm left/right) tới cell đang bám. Chỉ hiện khi mode = Laser,
        /// gun trên path và nòng có target sống; ngoài ra tắt. Màu tia theo TypeColor gun (như đạn) nếu
        /// không gán laserMaterial riêng.
        /// </summary>
        private void UpdateBeam(Barrel b)
        {
            if (_fire.Mode != GunFireMode.Laser || _state != GunState.OnPath)
            {
                DisableBeam(b);
                return;
            }
            Vector3 from = b.Muzzle != null ? b.Muzzle.position : transform.position;

            if (HasLiveTarget(b))
            {
                // Có target → vẽ tia tới cell và NẠP LẠI linger. Điểm cuối lưu lại để lúc mất target
                // (cell vừa vỡ, chưa kịp chốt cell kế) vẫn giữ được tia bắc cầu qua.
                b.BeamTo = b.Target.transform.position + Vector3.up * laserAimHeight;
                b.BeamHold = laserLinger;
            }
            else if (b.BeamHold > 0f)
            {
                // Mất target trong khoảnh khắc chuyển cell → GIỮ tia (điểm cuối cũ) tới khi hết linger,
                // gốc tia vẫn bám muzzle nên nhìn như tia liền mạch đang quét sang, không chớp tắt.
                b.BeamHold -= Time.deltaTime;
            }
            else
            {
                DisableBeam(b);
                return;
            }

            EnsureBeam(b);
            var mat = laserMaterial != null
                ? laserMaterial
                : GlobalConfigManager.MaterialOf(Data.Color, TypeObject.Bullet);
            if (mat != null && b.Beam.sharedMaterial != mat) b.Beam.sharedMaterial = mat;
            b.Beam.enabled = true;
            b.Beam.SetPosition(0, from);
            b.Beam.SetPosition(1, b.BeamTo);
        }

        private void DisableBeam(Barrel b)
        {
            if (b.Beam != null && b.Beam.enabled) b.Beam.enabled = false;
        }

        // Tạo LineRenderer 1 lần cho nòng (item pooled tái dùng lại). Toạ độ world nên không lệ thuộc scale
        // gun; parent vào gun để tự dọn khi gun despawn.
        private void EnsureBeam(Barrel b)
        {
            if (b.Beam != null) return;
            var go = new GameObject("LaserBeam");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.widthMultiplier = laserWidth;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 0;
            lr.textureMode = LineTextureMode.Stretch;
            lr.alignment = LineAlignment.View;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            b.Beam = lr;
        }

        private void Die()
        {
            _state = GunState.Dead;
            DisableBeam(_right); // tắt tia trước khi trả gun về pool (item pooled tái dùng)
            DisableBeam(_left);
            PathManager.Instance?.RemoveGun(this);
            GameController.Instance?.OnBoardChanged();
            Despawn();
        }

        private void Despawn()
        {
            if (_pool != null) _pool.Release(this);
            else Destroy(gameObject);
        }

        public void MoveTo(Vector3 target, float duration)
        {
            if (_moveRoutine != null) StopCoroutine(_moveRoutine);
            if (!gameObject.activeInHierarchy || duration <= 0f) { transform.position = target; return; }
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
        }

        // Text đã có sẵn trên prefab → chỉ tìm và bật, KHÔNG sinh thêm. includeInactive: prefab để
        // 'Text (TMP)' tắt sẵn nên GetComponentInChildren mặc định sẽ không thấy.
        private void EnsureLabel()
        {
            if (bulletLabel == null) bulletLabel = GetComponentInChildren<TMP_Text>(true);
            if (bulletLabel != null && !bulletLabel.gameObject.activeSelf)
                bulletLabel.gameObject.SetActive(true);
        }

        private void UpdateLabel()
        {
            if (bulletLabel != null) bulletLabel.text = Data.CountBullet.ToString();
        }

#if UNITY_EDITOR
        // HAI quạt CHỌN target: mỗi nòng quét TỪ hướng trước mặt (thân gun bám path) toả sang sườn của nó
        // Spread độ. VÀNG = nòng còn lượt của vòng này; XÁM = nòng đã hết lượt (không chốt target mới nữa).
        //
        // Đường tới target — nhìn được nòng nào đang nhắm cell nào TRƯỚC khi nổ:
        //   TRẮNG đứt nét = đã CHỐT nhưng chưa bắn phát nào (qua vòng là nhả ra, xem RearmBarrel)
        //   XANH LÁ       = đang bắn dở, cell còn trong quạt
        //   ĐỎ            = đang bắn dở, cell đã ra ngoài quạt — vẫn bắn nốt cho hết stack
        // Ô vuông ở đầu đường = cell đang bị nhắm; nhãn = R/L + số block còn phải bắn (Available).
        private void OnDrawGizmos()
        {
            if (_state == GunState.Dead) return;

            DrawBarrelArc(_right);
            DrawBarrelArc(_left);

            if (_state != GunState.OnPath) return;
            DrawTargetLine(_right, "R");
            DrawTargetLine(_left, "L");
        }

        private void DrawBarrelArc(Barrel b)
        {
            // Xám theo TỪNG nòng: 2 nòng khoá độc lập, bên này hết lượt bên kia vẫn có thể còn.
            Handles.color = !b.Armed
                ? new Color(0.5f, 0.5f, 0.5f, 0.35f)
                : new Color(1f, 0.85f, 0.2f, 0.9f);
            // Góc âm = quét ngược chiều → nòng trái toả sang trái, nòng phải sang phải, chung mép ở forward.
            Handles.DrawSolidArc(transform.position, Vector3.up, transform.forward, Spread * b.Sign, _fire.Range);
        }

        private void DrawTargetLine(Barrel b, string label)
        {
            if (b.Target == null) return;
            barrel = b;
            Vector3 to = b.Target.transform.position;

            Color col = !b.FiredAtTarget ? UnityEngine.Color.white
                      : InDetectZone(b.Target, b) ? UnityEngine.Color.green
                      : UnityEngine.Color.red;

            Handles.color = col;
            // Chưa bắn → đứt nét: phân biệt ngay "mới nhắm" với "đang nã".
            if (b.FiredAtTarget) Handles.DrawLine(transform.position, to);
            else Handles.DrawDottedLine(transform.position, to, 4f);

            Handles.DrawWireCube(to, Vector3.one * 0.5f);
            Handles.Label(to + Vector3.up * 0.8f, $"{label}:{b.Target.Available}");
        }
#endif
    }

    /// <summary>
    /// Gắn lên GameObject có Collider (thường là child "Model" của gun) để forward OnMouseDown về
    /// <see cref="Gun"/> ở root — vì OnMouseDown chỉ gọi trên GO chứa collider, không lan lên parent.
    /// </summary>
    public class GunClickRelay : MonoBehaviour
    {
        public Gun Owner;
        private void OnMouseDown() { if (Owner != null) Owner.HandleClick(); }
    }
}

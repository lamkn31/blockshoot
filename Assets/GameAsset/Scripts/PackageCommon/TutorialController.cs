using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Wayfu.Lamkn
{
    /// <summary>
    /// Tutorial first-time theo từng level (lưu PlayerPrefs) — port cấu trúc từ SmashFest.TutorialController,
    /// adapt cho gameplay blockshoot (gun trong slot). Dùng hệ popup có sẵn:
    ///   • <see cref="TutorialPopup"/> (qua <see cref="PopupController"/>) lo BÀN TAY + GUIDE TEXT.
    ///   • <see cref="TutorialCanvasPopup"/> (<see cref="tutorialCanvas"/>) là CANVAS nền tutorial — làm
    ///     TỐI layout (dim). Show khi tutorial chạy, HideThen (fade) khi xong. Nếu canvas dùng material
    ///     TutorialDim (đọc stencil) và render qua gameplay camera thì GUN được gắn stencil sẽ "nổi" sáng.
    ///
    /// Luồng level 1 (yêu cầu: tối layout + chọn gun đầu tiên):
    ///   1. Mỗi lần nạp xong màn (<see cref="GameController.LevelLoaded"/> — first load / Next / Retry),
    ///      nếu level hiện tại khớp một <see cref="LevelTutorial"/> chưa xem (onlyOnce) thì bắt đầu.
    ///   2. Lấy GUN ĐẦU TIÊN (<see cref="SlotManager.FirstGun"/>) → chiếu ra screen → đặt BÀN TAY chỉ vào.
    ///   3. onShown: bật <see cref="tutorialCanvas"/> (tối nền) + (tuỳ chọn) gắn <see cref="objectStencilMask"/>
    ///      vào gun target để nó nổi trên nền tối.
    ///   4. Người chơi CHỌN gun đó (<see cref="SlotManager.GunDeployed"/>) → popup Complete →
    ///      onDone: gỡ material, HideThen canvas, lưu PlayerPrefs.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class TutorialController : MonoBehaviour
    {
        public static TutorialController Instance { get; private set; }

        public enum LevelTutorialType
        {
            ClickGun,
            ShowImage
        }

        [Serializable]
        public class LevelTutorial
        {
            [Tooltip("Loại tutorial: chỉ vào gun hoặc hiện ảnh và đóng khi click màn hình.")]
            public LevelTutorialType type = LevelTutorialType.ClickGun;

            [Tooltip("Ảnh hiển thị khi Type = ShowImage. Người chơi click bất kỳ đâu để đóng.")]
            public Sprite tutorialImage;
            [Tooltip("Level (1-based) sẽ hiện tutorial này.")]
            public int levelNumber = 1;
            [TextArea(1, 3)]
            [Tooltip("Message hiển thị kèm bàn tay.")]
            public string guideText = "Tap to select the gun!";
            [Tooltip("Tên animation Spine của bàn tay (vd 'Tap'). Bỏ trống = dùng defaultHandAnimation của popup.")]
            public string handAnimation = "";
            [Tooltip("Offset (pixel) cộng vào vị trí gun cho bàn tay. (0,0) = đúng vị trí gun.")]
            public Vector2 handScreenOffset = Vector2.zero;

            [Tooltip("Đặt Popup Text Des (mô tả) theo vị trí gun thay vì vị trí prefab. Tắt = giữ prefab.")]
            public bool positionTextDesAboveGun = true;
            [Tooltip("Offset (pixel) cộng vào vị trí gun cho Popup Text Des. Y > 0 = CAO hơn gun. " +
                     "Chỉ dùng khi positionTextDesAboveGun bật.")]
            public Vector2 textDesScreenOffset = new Vector2(0f, 300f);

            [Tooltip("Gắn stencil (nổi trên nền tối) cho gun target. Chỉ có tác dụng khi objectStencilMask " +
                     "được gán + tutorialCanvas dùng material TutorialDim render qua gameplay camera.")]
            public bool stencilGun = true;
        }

        [Header("Refs (auto-fill nếu trống)")]
        [Tooltip("Bỏ trống → lấy GameController.Instance lúc level sẵn sàng.")]
        [SerializeField] private GameController gameController;
        [Tooltip("Camera chiếu vị trí world của gun → screen. Trống → Camera.main.")]
        [SerializeField] private Camera gameplayCamera;
        [SerializeField] private bool debugLog = false;

        [Header("Tutorial UI")]
        [Tooltip("Canvas nền tutorial (TutorialCanvasPopup) làm TỐI layout. Show khi tutorial chạy, HideThen " +
                 "(fade) khi xong. Để gun 'nổi' trên nền tối: canvas phải Screen Space - Camera trỏ vào gameplay " +
                 "camera (share stencil buffer) và dùng material TutorialDim.")]
        [SerializeField] private TutorialCanvasPopup tutorialCanvas;
        [Tooltip("Material ObjectStencilMask.mat (shader BusSort/TutorialStencilMask) — gắn thêm vào gun target " +
                 "khi tutorial chạy để gun ghi stencil (nổi trên nền tối). Gỡ ra khi tutorial xong. Bỏ trống = " +
                 "chỉ tối nền, gun không nổi (vẫn chỉ tay + chọn được).")]
        [SerializeField] private Material objectStencilMask;

        [Header("Tutorials per level")]
        [SerializeField]
        private List<LevelTutorial> tutorials = new List<LevelTutorial>
        {
            new LevelTutorial { levelNumber = 1 },
        };

        [Header("Persistence")]
        [Tooltip("Chỉ hiện mỗi tutorial 1 lần (lưu PlayerPrefs theo level).")]
        [SerializeField] private bool onlyOnce = true;
        [Tooltip("Bump để buộc hiện lại tutorial cho người chơi cũ (đổi key PlayerPrefs).")]
        [SerializeField] private string prefVersion = "v1";

        private LevelTutorial _active;
        private Gun _targetGun;
        private bool _running;
        private bool _hooked;
        private bool _subscribed;

        // Lưu material gốc của từng renderer để khôi phục khi tutorial kết thúc.
        private struct MatBackup { public Renderer renderer; public Material[] shared; }
        private readonly List<MatBackup> _matBackups = new List<MatBackup>();

        private string PrefKey(int level) => $"tut_level{level}_{prefVersion}";

        private int CurrentLevel() => gameController != null ? gameController.DisplayLevel : 1;

        private void Awake()
        {
            Instance = this;
        }

        // Đăng ký ở CẢ OnEnable và Start (đảm bảo GameController.Instance đã sẵn sàng nếu execution order lệch).
        private void OnEnable() => Subscribe();
        private void Start() => Subscribe();

        private void OnDisable()
        {
            Unsubscribe();
            if (Instance == this) Instance = null;
            Cleanup(stopPopup: true); // gỡ material + ẩn popup nếu đang chạy dở (vd đổi scene)
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            if (gameController == null) gameController = GameController.Instance;
            if (gameController == null) return; // chưa có Instance → để Start thử lại
            gameController.LevelLoaded += HandleLevelLoaded;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            if (gameController != null) gameController.LevelLoaded -= HandleLevelLoaded;
            _subscribed = false;
        }

        // Mỗi lần GameController nạp xong màn → kiểm tra có tutorial cho level này chưa xem không.
        private void HandleLevelLoaded() => TryStart();

        /// <summary>Public entry (cũng dùng được cho nút debug).</summary>
        public void TryStart()
        {
            if (_running || gameController == null) return;

            int level = CurrentLevel();
            LevelTutorial cfg = FindConfig(level);
            if (cfg == null) return;
            if (onlyOnce && PlayerPrefs.GetInt(PrefKey(level), 0) == 1) return;

            _active = cfg;
            _running = true;
            StartCoroutine(StartRoutine(cfg));
        }

        /// <summary>
        /// Đợi TutorialPopup instantiate (Addressables async) + canvas layout xong rồi mới dựng step.
        /// Định vị bàn tay ngay frame popup vừa sinh sẽ sai vì RectTransform/Canvas chưa layout.
        /// </summary>
        private IEnumerator StartRoutine(LevelTutorial cfg)
        {
            PopupController pc = PopupController.Instance;
            if (pc == null) { _running = false; _active = null; yield break; }

            Task<TutorialPopup> ensure = pc.EnsureTutorialAsync();
            while (!ensure.IsCompleted) yield return null;
            TutorialPopup popup = ensure.Result;
            if (popup == null) { _running = false; _active = null; yield break; }

            yield return null;
            yield return new WaitForEndOfFrame();
            Canvas.ForceUpdateCanvases();

            if (cfg.type == LevelTutorialType.ShowImage)
            {
                if (cfg.tutorialImage == null)
                {
                    Debug.LogWarning($"[Tutorial] Level {cfg.levelNumber} uses ShowImage but has no tutorial image.", this);
                    _running = false;
                    _active = null;
                    yield break;
                }

                var imageStep = new TutorialStep
                {
                    id = $"level{cfg.levelNumber}_image",
                    showBanner = true,
                    bannerSprite = cfg.tutorialImage,
                    showHand = false,
                    advanceMode = TutorialAdvanceMode.Click
                };

                popup.StartTutorial(new List<TutorialStep> { imageStep }, OnTutorialDone, OnTutorialShown);
                yield break;
            }

            _targetGun = SlotManager.IsActive ? SlotManager.Instance.FirstGun : null;
            if (_targetGun == null) { _running = false; _active = null; yield break; }

            Camera cam = ResolveCamera();
            if (cam == null) { _running = false; _active = null; yield break; }

            Vector3 screen = cam.WorldToScreenPoint(_targetGun.transform.position);
            if (screen.z < 0f) { screen.x = Screen.width * 0.5f; screen.y = Screen.height * 0.5f; }
            Vector2 gunScreen = new Vector2(screen.x, screen.y);
            Vector2 handPos = gunScreen + cfg.handScreenOffset;
            Vector2 textDesPos = gunScreen + cfg.textDesScreenOffset;

            if (debugLog)
                Debug.Log($"[Tutorial] level={cfg.levelNumber} cam='{cam.name}' gun='{_targetGun.name}' " +
                          $"hand={handPos} textDes={textDesPos}");

            var step = new TutorialStep
            {
                id = $"level{cfg.levelNumber}_selectgun",
                guideText = cfg.guideText,
                showHand = true,
                handScreenPos = handPos,
                handAnimation = cfg.handAnimation,
                advanceMode = TutorialAdvanceMode.Action,
                useTextDesScreenPos = cfg.positionTextDesAboveGun,
                textDesScreenPos = textDesPos,
            };

            SlotManager.GunDeployed += HandleGunDeployed;
            _hooked = true;
            popup.StartTutorial(new List<TutorialStep> { step }, OnTutorialDone, OnTutorialShown);
        }

        private void OnTutorialShown()
        {
            if (tutorialCanvas != null)
            {
                // Canvas dim render QUA gameplay camera (Screen Space - Camera) thì mới dùng CHUNG stencil
                // buffer với gun 3D → gun ghi stencil, dim đọc stencil (NotEqual) → gun "nổi" sáng.
                EnsureCanvasCamera();
                tutorialCanvas.Show();
            }
            if (_active != null && _active.type == LevelTutorialType.ClickGun && _active.stencilGun)
                ApplyStencilToGun();
        }

        private void EnsureCanvasCamera()
        {
            Camera cam = ResolveCamera();
            if (cam == null || tutorialCanvas == null) return;
            Canvas canvas = tutorialCanvas.GetComponentInChildren<Canvas>(true);
            if (canvas == null) return;
            Canvas root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
            root.renderMode = RenderMode.ScreenSpaceCamera;
            root.worldCamera = cam;
            if (root.planeDistance <= 0f) root.planeDistance = 20f;
        }

        // Người chơi chọn 1 gun → chỉ hoàn tất khi ĐÚNG gun target (gun đầu tiên).
        private void HandleGunDeployed(Gun gun)
        {
            if (gun != _targetGun) return;
            PopupController.Instance?.NotifyTutorialActionDone();
        }

        private void OnTutorialDone()
        {
            if (onlyOnce && _active != null)
            {
                PlayerPrefs.SetInt(PrefKey(_active.levelNumber), 1);
                PlayerPrefs.Save();
            }
            Cleanup(stopPopup: false); // popup tự Complete rồi → không cần Stop lại
        }

        private void Cleanup(bool stopPopup)
        {
            if (_hooked) SlotManager.GunDeployed -= HandleGunDeployed;
            _hooked = false;
            RestoreMaterials();
            if (stopPopup) PopupController.Instance?.StopTutorial();
            if (tutorialCanvas != null) tutorialCanvas.HideThen(null);
            _running = false;
            _active = null;
            _targetGun = null;
        }

        private LevelTutorial FindConfig(int level)
        {
            if (tutorials == null) return null;
            for (int i = 0; i < tutorials.Count; i++)
                if (tutorials[i] != null && tutorials[i].levelNumber == level) return tutorials[i];
            return null;
        }

        private Camera ResolveCamera() => gameplayCamera != null ? gameplayCamera : Camera.main;

        // ---- Stencil mask: gắn/gỡ material cho gun nổi trên nền tối ----

        // Gắn thêm 1 slot stencil mask (vẽ vô hình, chỉ ghi stencil) vào MỌI renderer con của gun target.
        // Bỏ qua renderer bóng đổ (tên có 'shadow') và renderer đã gắn.
        private void ApplyStencilToGun()
        {
            if (objectStencilMask == null || _targetGun == null) return;

            Renderer[] rends = _targetGun.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < rends.Length; r++)
            {
                Renderer ren = rends[r];
                if (ren == null) continue;
                if (ren.name.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                bool already = false;
                for (int b = 0; b < _matBackups.Count; b++)
                    if (_matBackups[b].renderer == ren) { already = true; break; }
                if (already) continue;

                Material[] shared = ren.sharedMaterials;
                _matBackups.Add(new MatBackup { renderer = ren, shared = shared });

                var extended = new Material[shared.Length + 1];
                Array.Copy(shared, extended, shared.Length);
                extended[shared.Length] = objectStencilMask;
                ren.materials = extended; // instance array tạm; khôi phục lại sharedMaterials sau
            }
        }

        private void RestoreMaterials()
        {
            for (int i = 0; i < _matBackups.Count; i++)
            {
                MatBackup b = _matBackups[i];
                if (b.renderer != null) b.renderer.sharedMaterials = b.shared;
            }
            _matBackups.Clear();
        }
    }
}

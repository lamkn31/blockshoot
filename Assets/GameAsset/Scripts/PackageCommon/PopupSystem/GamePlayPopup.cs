using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Wayfu.Lamkn
{
    public class GamePlayPopup : BasePopup
    {
        #region Inspector
        [Header("HUD")]
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private TMP_Text pathCountText;
        [Tooltip("Format string for the path counter, {0} = current, {1} = max.")]
        [SerializeField] private string pathCountFormat = "{0}/{1}";
        [SerializeField] private Slider pathCountSlider;
        [Tooltip("Object hiển thị thanh fill khi tiến độ CHƯA đầy.")]
        [SerializeField] private GameObject fillGreen;
        [Tooltip("Object hiển thị thanh fill khi tiến độ ĐÃ đầy (current >= max).")]
        [SerializeField] private GameObject fillRed;

        [Header("Block Progress (tiến trình phá block)")]
        [Tooltip("Slider thể hiện số block ĐÃ phá (value = đã phá, max = tổng block đầu màn).")]
        [SerializeField] private Slider blockProgressSlider;

        [Header("Difficulty")]
        [Tooltip("Index 0 = Easy, 1 = Normal, 2 = Hard. Object at the matching index is enabled, others disabled.")]
        [SerializeField] private GameObject[] difficultyObjects = new GameObject[3];
        [Tooltip("LevelText riêng cho từng difficulty (song song với difficultyObjects). " +
                 "Để trống nếu muốn dùng levelText chung ở Header HUD.")]
        [SerializeField] private TMP_Text[] difficultyLevelTexts = new TMP_Text[3];
        [Tooltip("Index 0 = Easy, 1 = Normal, 2 = Hard. Image Setting riêng cho từng difficulty " +
                 "(song song với difficultyObjects). Object ở index khớp difficulty được bật, còn lại tắt.")]
        [SerializeField] private GameObject[] settingObjects = new GameObject[3];

        [Header("Setting")]
        [SerializeField] private Button settingButton;
        [Tooltip("Giấu nút Setting ở level 1 — quy ước của game gốc (màn 1 là tutorial). Tắt nếu game " +
                 "này không có tutorial ở màn đầu, không thì người chơi vào game là mất nút Setting.")]
        [SerializeField] private bool hideSettingOnFirstLevel;

        [Header("Completed Tick")]
        [Tooltip("Pool of UI checkmark images shown over a car when it Completes. Lives on this popup so its tick parent + canvas are colocated with the gameplay HUD.")]
        [SerializeField] private CompletedTickController tickController;

        [Header("Reason Lose")]
        [SerializeField] private GameObject reasonLoseRoot;
        [SerializeField] private TMP_Text reasonLoseText;
        [Tooltip("Animator diễn anim show/hide bảng lý do thua. Bật (enabled=true) khi show, tắt khi không show.")]
        [SerializeField] private Animator reasonLoseAnimator;
        [Tooltip("Trigger phát anim hiện.")]
        [SerializeField] private string reasonLoseShowTrigger = "Show";
        [Tooltip("Thời gian chờ anim SHOW chạy xong rồi mới show popup lose (unscaled).")]
        [SerializeField] private float reasonLoseShowDuration = 0.5f;
        /// <summary>Public accessor used by gameplay code (BusSort.GameplayController) to
        /// Show ticks on car completion and HideAll on init / reset / win / lose.</summary>
        public CompletedTickController TickController => tickController;

        /// <summary>True khi bảng lý do thua đang hiển thị (gồm lúc diễn anim show, trước khi popup Lose bật).
        /// Dùng để chặn click xe trong lúc show reason.</summary>
        public bool IsReasonLoseShowing => reasonLoseRoot != null && reasonLoseRoot.activeSelf;

        #endregion

        #region State

        private Func<int> _getCurrentPathCount;
        private int _maxPath;
        private int _lastCount = -1;
        private string[] _countCache; // pre-formatted "i/max" strings, zero-alloc lookup at runtime
        private Action _onRetry;
        private Coroutine _reasonLoseRoutine;

        #endregion

        #region Unity

        protected override void Awake()
        {
            base.Awake();
            if (settingButton != null) settingButton.onClick.AddListener(OnSettingClicked);
            // Không show → animator tắt. Chạy theo unscaled time để anim vẫn diễn khi game pause.
            if (reasonLoseAnimator != null)
            {
                reasonLoseAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
                reasonLoseAnimator.enabled = false;
            }
            // pathCountText changes every block in/out → isolate it in a sub-Canvas so the
            // mesh rebuild doesn't dirty the entire popup canvas (and behind it).
            if (pathCountText != null) CanvasOptimizer.IsolateInSubCanvas(pathCountText);
        }

        private void Update()
        {
            if (pathCountText == null || _getCurrentPathCount == null || _countCache == null) return;
            int c = _getCurrentPathCount();
            if (c == _lastCount) return;
            if ((uint)c >= (uint)_countCache.Length) return;
            _lastCount = c;
            pathCountText.text = _countCache[c]; // zero-alloc: just swap string reference
            if (pathCountSlider != null) pathCountSlider.value = c;
            UpdateFillState(c);
        }

        #endregion

        #region API

        public override void Show()
        {
            base.Show();
        }

        public void Show(int level, Func<int> getCurrentPathCount, int maxPath, int difficulty = 0, Action onRetry = null)
        {
            _onRetry = onRetry;
            ResetReasonLose(); // vào màn mới: tắt animator + ẩn bảng lý do thua
            ApplyDifficulty(difficulty);
            ApplySettingVisibility(level, difficulty);
            ApplyLevelText(level, difficulty);
            _getCurrentPathCount = getCurrentPathCount;
            _maxPath = maxPath;
            _lastCount = -1;
            BuildCountCache(maxPath);
            if (pathCountSlider != null)
            {
                pathCountSlider.wholeNumbers = true;
                pathCountSlider.minValue = 0;
                pathCountSlider.maxValue = Mathf.Max(0, maxPath);
            }
            if (pathCountText != null && _countCache != null)
            {
                int c = getCurrentPathCount != null ? getCurrentPathCount() : 0;
                if ((uint)c < (uint)_countCache.Length) pathCountText.text = _countCache[c];
                if (pathCountSlider != null) pathCountSlider.value = c;
                _lastCount = c;
                UpdateFillState(c);
            }
            else
            {
                UpdateFillState(getCurrentPathCount != null ? getCurrentPathCount() : 0);
            }
            base.Show();
        }

        /// <summary>Cập nhật thanh tiến trình "block đã phá / tổng block" trên HUD gameplay.
        /// GameController gọi lúc vào màn (0) và mỗi lần bàn chơi đổi.</summary>
        public void SetBlockProgress(int destroyed, int total)
        {
            destroyed = Mathf.Clamp(destroyed, 0, Mathf.Max(0, total));
            if (blockProgressSlider != null)
            {
                // Chạy theo GIÁ TRỊ THẬT (0..tổng block), value = số đã phá — không phải phân số/phần trăm.
                blockProgressSlider.wholeNumbers = true;
                blockProgressSlider.minValue = 0;
                blockProgressSlider.maxValue = Mathf.Max(0, total);
                blockProgressSlider.value = destroyed;
            }
        }

        private void UpdateFillState(int current)
        {
            bool isFull = _maxPath > 0 && current >= _maxPath;
            if (fillGreen != null) fillGreen.SetActive(!isFull);
            if (fillRed != null) fillRed.SetActive(isFull);
        }

        private void ApplyDifficulty(int difficulty)
        {
            if (difficultyObjects != null)
            {
                for (int i = 0; i < difficultyObjects.Length; i++)
                {
                    var go = difficultyObjects[i];
                    if (go != null) go.SetActive(i == difficulty);
                }
            }
            if (settingObjects != null)
            {
                for (int i = 0; i < settingObjects.Length; i++)
                {
                    var go = settingObjects[i];
                    if (go != null) go.SetActive(i == difficulty);
                }
            }
        }

        /// <summary>
        /// Ẩn nút Setting ở level 1 (tutorial) — chỉ khi <see cref="hideSettingOnFirstLevel"/> bật.
        /// Gọi sau ApplyDifficulty để ghi đè trạng thái settingObjects mà ApplyDifficulty vừa bật
        /// theo difficulty.
        /// </summary>
        private void ApplySettingVisibility(int level, int difficulty)
        {
            bool show = !hideSettingOnFirstLevel || level != 1;
            if (settingButton != null) settingButton.gameObject.SetActive(show);
            if (!show && settingObjects != null)
            {
                for (int i = 0; i < settingObjects.Length; i++)
                {
                    var go = settingObjects[i];
                    if (go != null) go.SetActive(false);
                }
            }
        }

        /// <summary>
        /// Ưu tiên set text cho levelText riêng của difficulty đang active.
        /// Fallback dùng levelText chung nếu mảng difficultyLevelTexts không có slot tương ứng.
        /// </summary>
        private void ApplyLevelText(int level, int difficulty)
        {
            string label = $"Level {level}";
            TMP_Text active = null;
            if (difficultyLevelTexts != null &&
                (uint)difficulty < (uint)difficultyLevelTexts.Length)
            {
                active = difficultyLevelTexts[difficulty];
            }

            if (active != null)
            {
                active.text = label;
                // Vẫn cập nhật levelText chung nếu có gán (giúp các UI khác đọc giá trị).
                if (levelText != null && levelText != active) levelText.text = label;
            }
            else if (levelText != null)
            {
                levelText.text = label;
            }
        }

        private void BuildCountCache(int maxPath)
        {
            // Pre-format every possible "i/max" once per level → runtime updates are just string swaps.
            int needed = Mathf.Max(0, maxPath) + 1;
            if (_countCache != null && _countCache.Length == needed) return;
            _countCache = new string[needed];
            for (int i = 0; i < needed; i++)
                _countCache[i] = string.Format(pathCountFormat, i, maxPath);
        }

        /// Diễn anim hiện bảng lý do thua → giữ 1 khoảng → tắt, rồi gọi onComplete (thường là ShowLose).
        public void ShowReasonLose(string reason, Action onComplete = null)
        {
            if (reasonLoseText != null) reasonLoseText.text = reason;
            if (reasonLoseRoot == null)
            {
                onComplete?.Invoke();
                return;
            }
            if (_reasonLoseRoutine != null) StopCoroutine(_reasonLoseRoutine);
            _reasonLoseRoutine = StartCoroutine(ReasonLoseRoutine(onComplete));
        }

        // Đưa bảng lý do thua về trạng thái tắt: dừng routine đang chạy, tắt animator, ẩn root.
        private void ResetReasonLose()
        {
            if (_reasonLoseRoutine != null) { StopCoroutine(_reasonLoseRoutine); _reasonLoseRoutine = null; }
            if (reasonLoseAnimator != null) reasonLoseAnimator.enabled = false;
            if (reasonLoseRoot != null) reasonLoseRoot.SetActive(false);
        }

        private IEnumerator ReasonLoseRoutine(Action onComplete)
        {
            reasonLoseRoot.SetActive(true);
            // Bật animator + phát anim show, chờ show chạy xong.
            if (reasonLoseAnimator != null)
            {
                reasonLoseAnimator.enabled = true;
                // Animator bị disable sẽ "đóng băng" ở state cuối của lần show trước → lần thua thứ 2
                // trigger Show không tạo transition. Rebind đưa về default state trước khi trigger.
                reasonLoseAnimator.Rebind();
                if (!string.IsNullOrEmpty(reasonLoseShowTrigger))
                    reasonLoseAnimator.SetTrigger(reasonLoseShowTrigger);
            }
            if (reasonLoseShowDuration > 0f)
                yield return new WaitForSecondsRealtime(reasonLoseShowDuration);

            _reasonLoseRoutine = null;
            onComplete?.Invoke(); // anim show xong → show popup lose
        }

        #endregion

        #region Callbacks

        private void OnSettingClicked()
        {
            if (PopupController.Instance == null) return;
            // Ignore the Settings button while the difficulty "noticehard" notification is on screen.
            if (PopupController.Instance.BlockGameInput) return;
            // Forward retry callback so the Setting popup's retry button restarts the level
            // (same action as Lose popup's retry).
            SoundController.Instance?.PlayButtonClick();
            VibrationController.Instance?.Vibrate(VibrationStyle.Light);
            PopupController.Instance.ShowSetting(_onRetry);
        }

        #endregion
    }
}

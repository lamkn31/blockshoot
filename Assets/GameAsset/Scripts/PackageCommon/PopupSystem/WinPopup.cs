using Spine;
using Spine.Unity;
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Wayfu.Lamkn
{
    public class WinPopup : BasePopup
    {
        public enum VictoryMetaMode { None, Progress, Show }

        #region Inspector

        [Header("Win Refs")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text rewardText;
        [SerializeField] private TMP_Text txtCoinAll;
        [SerializeField] private Button nextButton;
        [SerializeField] private GameObject fxWin;

        [Header("Home (optional)")]
        [SerializeField] private bool hasHomeButton = false;
        [SerializeField] private Button homeButton;

        [Header("Feature Meta")]
        [Tooltip("Object mặc định hiển thị khi không có feature (vd: logo).")]
        [SerializeField] private GameObject defaultMeta;
        [Tooltip("Object hiển thị tiến trình mở khóa feature (slider).")]
        [SerializeField] private GameObject winProgressMeta;
        [Tooltip("Object hiển thị khi feature đã mở khóa xong (100%).")]
        [SerializeField] private GameObject winShowMeta;

        [Header("Progress Slider (Win_ProgressMeta)")]
        [SerializeField] private Slider progressSlider;
        [SerializeField] private TMP_Text progressPercentText;
        [Tooltip("Thời gian animate slider chạy từ giá trị cũ đến mới")]
        [SerializeField] private float progressFillDuration = 0.8f;
        [Tooltip("Delay trước khi slider bắt đầu chạy (sau khi meta hiện ra)")]
        [SerializeField] private float progressFillDelay = 0.2f;
        [Tooltip("Delay sau khi slider đạt 100% trước khi swap sang Win_ShowMeta")]
        [SerializeField] private float progressToShowDelay = 0.4f;

        [Header("Feature Display (Icon + Title + Description)")]
        [SerializeField] private Image progressMetaIcon;
        [SerializeField] private TMP_Text progressMetaTitle;
        [SerializeField] private Image showMetaIcon;
        [SerializeField] private Image showMetaTitleIamges;
        [SerializeField] private TMP_Text showMetaTitle;
        [Tooltip("Mô tả feature hiển thị ở winShowMeta (khi đã unlock). Không hiện ở Progress.")]
        [SerializeField] private TMP_Text showMetaDescription;
        [SerializeField] private Animator animator;
        [Tooltip("State Animator khi đang hiện PROGRESS (feature chưa unlock xong).")]
        [SerializeField] private string metaProgressState = "victorycompletedmeta";
        [Tooltip("State Animator khi progress đầy 100% / feature đã unlock (ShowMeta).")]
        [SerializeField] private string metaCompletedState = "victorycompletedmetaUpdate";
        [Tooltip("State Animator khi KHÔNG có feature (defaultMeta/logo).")]
        [SerializeField] private string metaNoFeatureState = "victoryNoFeature";

        [Header("Ribbon Spine (victory)")]
        [SerializeField] [Tooltip("SkeletonGraphic của ribbon — chạy anim khi show popup.")]
        private SkeletonGraphic ribbonSpine;
        [SerializeField] [Tooltip("Tên anim spine chạy KHI SHOW (khớp animation trong ribbon, vd 'animation').")]
        private string ribbonWinAnim = "animation";
        [SerializeField] [Tooltip("Anim spine LẶP sau khi anim thắng chạy xong. Bỏ trống = giữ frame cuối.")]
        private string ribbonIdleAfter = "idle";
        [SerializeField] [Tooltip("Anim thắng có lặp không (thường tắt — chạy 1 lần rồi về idle).")]
        private bool ribbonWinLoop = false;

        [Header("cup (victory)")]
        [SerializeField]
        [Tooltip("SkeletonGraphic của cup — chạy anim khi show popup.")]
        private SkeletonGraphic cup;
        [SerializeField]
        [Tooltip("Tên anim spine chạy KHI SHOW (khớp animation trong cup, vd 'animation').")]
        private string cuppanim = "animation";
        [SerializeField]
        [Tooltip("Anim spine LẶP sau khi anim thắng chạy xong. Bỏ trống = giữ frame cuối.")]
        private string cupidle = "idle";
        #endregion

        #region State

        private Action _onNext;
        private Action _onHome;
        private int _rewardAmount;
        private int _totalCoinBefore;

        private VictoryMetaMode _metaMode = VictoryMetaMode.None;
        private float _progressFrom = 0f;
        private float _progressTo = 0f;
        private bool _swapToShowAfterProgress = false;
        private Coroutine _progressCoroutine;

        #endregion

        #region Unity

        // Win popup đóng chỉ fade, KHÔNG scale.
        protected override bool ScaleOnHide => false;

        protected override void Awake()
        {
            base.Awake();
            if (nextButton != null) nextButton.onClick.AddListener(OnNextClicked);
            if (homeButton != null) homeButton.onClick.AddListener(OnHomeClicked);
        }

        #endregion

        #region Feature API

        /// <summary>
        /// Set chế độ hiển thị meta (None = defaultMeta/logo, Progress = slider, Show = đã unlock).
        /// </summary>
        public void SetMetaMode(VictoryMetaMode mode)
        {
            _metaMode = mode;
            _swapToShowAfterProgress = false;
        }

        /// <summary>
        /// Cài đặt tiến trình hiển thị trên Win_ProgressMeta.
        /// from = giá trị slider trước khi animate (0..1), to = giá trị mục tiêu (0..1).
        /// Gọi trước Show().
        /// </summary>
        public void SetProgress(float from, float to)
        {
            _progressFrom = Mathf.Clamp01(from);
            _progressTo = Mathf.Clamp01(to);
        }

        /// <summary>
        /// Hiện Win_ProgressMeta, animate slider từ from -> to (thường to=1f), sau đó swap sang Win_ShowMeta.
        /// Dùng khi player vừa đạt unlockLevel để vẫn thấy slider chạy tới 100% rồi mới hiện ShowMeta.
        /// </summary>
        public void SetProgressThenShow(float from, float to)
        {
            _progressFrom = Mathf.Clamp01(from);
            _progressTo = Mathf.Clamp01(to);
            _metaMode = VictoryMetaMode.Progress;
            _swapToShowAfterProgress = true;
        }

        /// <summary>
        /// Set icon + title cho cả Win_ProgressMeta và Win_ShowMeta, đồng thời set description
        /// chỉ cho Win_ShowMeta. Truyền null/empty để clear. Nhận sprite + string trực tiếp để
        /// asmdef CoreUI không cần reference asmdef chứa FeatureUnlockEntry (caller phía game tự lookup).
        /// </summary>
        public void SetFeatureInfo(Sprite icon,Sprite titleImage, string title, string description = null)
        {
            if (progressMetaIcon != null)
            {
                progressMetaIcon.sprite = icon;
                progressMetaIcon.enabled = icon != null;
            }
            if (progressMetaTitle != null) progressMetaTitle.text = title ?? string.Empty;

            if (showMetaIcon != null)
            {
                showMetaIcon.sprite = icon;
                showMetaIcon.enabled = icon != null;
            }
            if (showMetaTitle != null)
            {
                if (title != null)
                    showMetaTitle.text = title.ToUpper();
                else
                    showMetaTitle.text = string.Empty;
            }

            // Description chỉ hiển thị ở winShowMeta.
            if (showMetaDescription != null)
            {
                showMetaDescription.text = description ?? string.Empty;
                showMetaDescription.gameObject.SetActive(!string.IsNullOrEmpty(description));
            }
            if(showMetaTitleIamges != null)
            {
                showMetaTitleIamges.sprite = titleImage;
                showMetaTitleIamges.enabled = titleImage != null;
            }
        }

        private GameObject GetActiveMetaObject()
        {
            switch (_metaMode)
            {
                case VictoryMetaMode.Progress: return winProgressMeta;
                case VictoryMetaMode.Show: return winShowMeta;
                default: return defaultMeta;
            }
        }

        #endregion

        #region API

        public void Show(string title, int reward, int totalCoinBefore = 0,
            Action onNext = null, Action onHome = null)
        {
            // Dọn state còn sót từ lần show trước (coroutine progress chạy dở, scale leftover...).
            // Re-show không reset sẽ kẹt slider hoặc meta cũ hiển thị nhầm.
            if (_progressCoroutine != null) { StopCoroutine(_progressCoroutine); _progressCoroutine = null; }
            if (fxWin != null) {
                fxWin.SetActive(true);
            }
            _rewardAmount = reward;
            _totalCoinBefore = totalCoinBefore;
            _onNext = onNext;
            _onHome = onHome;
            Utils.SetTextSafe(titleText, title);
            Utils.SetTextSafe(rewardText, reward);
            Utils.SetTextSafe(txtCoinAll, totalCoinBefore);

            if (homeButton != null) homeButton.gameObject.SetActive(hasHomeButton);

            // Set state meta object trước khi base.Show() để fade in đồng bộ
            ApplyMetaSelection();
            ApplyInitialProgressUI();

            // Tắt BGM, bật nhạc chiến thắng.
            SoundController.Instance?.PlayWinSound();

            base.Show();

            // Animator meta xử lý SAU base.Show(): Play cần GameObject đang ACTIVE mới ăn.
            // • Progress (feature chưa unlock xong) → victorycompletedmeta.
            // • Show (đã unlock) → victorycompletedmetaUpdate; progress đầy 100% giữa chừng
            //   → SwapProgressToShow cũng play state này.
            // • None (không có feature) → victoryNoFeature.
            switch (_metaMode)
            {
                case VictoryMetaMode.Progress: PlayMetaAnim(metaProgressState); break;
                case VictoryMetaMode.Show:     PlayMetaAnim(metaCompletedState); break;
                default:                       PlayMetaAnim(metaNoFeatureState); break;
            }

            PlayRibbonAnim(); // chạy anim spine ribbon (title) khi show popup victory
            PlayCupAnim();    // chạy anim spine cup tương tự title
        }

        // Chạy anim spine "thắng" trên ribbon khi popup hiện; xong thì về idle lặp (nếu có).
        private void PlayRibbonAnim()
        {
            if (ribbonSpine == null || string.IsNullOrEmpty(ribbonWinAnim)) return;
            // Bật GameObject ribbon khi popup show (đã bị tắt lúc Hide lần trước).
            if (!ribbonSpine.gameObject.activeSelf) ribbonSpine.gameObject.SetActive(true);
            // Popup vừa được SetActive → SkeletonGraphic có thể CHƯA init trong cùng frame → ép init.
            if (ribbonSpine.AnimationState == null) ribbonSpine.Initialize(false);
            var state = ribbonSpine.AnimationState;
            if (state == null) return;

            state.SetAnimation(0, ribbonWinAnim, ribbonWinLoop);
            if (!ribbonWinLoop && !string.IsNullOrEmpty(ribbonIdleAfter))
                state.AddAnimation(0, ribbonIdleAfter, true, 0f); // anim thắng xong → về idle lặp
            // Áp pose + rebuild mesh NGAY → chạy anim tức thì, hết bị "cache" frame cũ 1 nhịp.
            ribbonSpine.Update(0f);
            ribbonSpine.UpdateMesh();
        }
        // Chạy anim spine "thắng" trên cup khi popup hiện; xong thì về idle lặp (nếu có) — giống PlayRibbonAnim.
        private void PlayCupAnim()
        {
            if (cup == null || string.IsNullOrEmpty(cuppanim)) return;
            // Popup vừa được SetActive → SkeletonGraphic có thể CHƯA init trong cùng frame → ép init.
            if (cup.AnimationState == null) cup.Initialize(false);
            var state = cup.AnimationState;
            if (state == null) return;

            state.SetAnimation(0, cuppanim, false);
            if (!string.IsNullOrEmpty(cupidle))
                state.AddAnimation(0, cupidle, true, 0f); // anim thắng xong → về idle lặp
            // Áp pose + rebuild mesh NGAY → chạy anim tức thì, hết bị "cache" frame cũ 1 nhịp.
            cup.Update(0f);
            cup.UpdateMesh();
        }

        public override void Hide()
        {
            // Stop progress coroutine để không ghi đè slider sau khi popup đã đóng.
            if (_progressCoroutine != null) { StopCoroutine(_progressCoroutine); _progressCoroutine = null; }
            // Nếu đang ở defaultMeta (không có feature) → tắt defaultMeta TRƯỚC khi fade để đóng.
            if (_metaMode == VictoryMetaMode.None && defaultMeta != null && defaultMeta.activeSelf)
                defaultMeta.SetActive(false);
            // Tắt GameObject ribbon TRƯỚC khi fade để đóng.
            if (ribbonSpine != null && ribbonSpine.gameObject.activeSelf) ribbonSpine.gameObject.SetActive(false);
            if (fxWin != null) fxWin.SetActive(false);
            base.Hide();
            SoundController.Instance.PlayDefaultBGM();
        }

        #endregion

        #region Show flow

        private void ApplyMetaSelection()
        {
            GameObject active = GetActiveMetaObject();
            if (defaultMeta != null) defaultMeta.SetActive(active == defaultMeta);
            if (winProgressMeta != null) winProgressMeta.SetActive(active == winProgressMeta);
            if (winShowMeta != null) winShowMeta.SetActive(active == winShowMeta);
        }

        private void ApplyInitialProgressUI()
        {
            if (progressSlider != null)
            {
                progressSlider.minValue = 0f;
                progressSlider.maxValue = 1f;
                progressSlider.value = _progressFrom;
            }
            if (progressPercentText != null)
                progressPercentText.text = $"{Mathf.RoundToInt(_progressFrom * 100f)}%";
        }

        protected override void OnShowCompleted()
        {
            base.OnShowCompleted();

            // Sau khi popup fade-in xong, mới bắt đầu animate slider (nếu cần).
            if (_metaMode == VictoryMetaMode.Progress && progressSlider != null)
            {
                if (_progressCoroutine != null) StopCoroutine(_progressCoroutine);
                _progressCoroutine = StartCoroutine(AnimateProgressSlider());
            }
        }

        private IEnumerator AnimateProgressSlider()
        {
            if (progressFillDelay > 0f) yield return new WaitForSecondsRealtime(progressFillDelay);

            float timer = 0f;
            float duration = Mathf.Max(0.01f, progressFillDuration);

            while (timer < duration)
            {
                timer += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(timer / duration);
                float val = Mathf.Lerp(_progressFrom, _progressTo, t);

                if (progressSlider != null) progressSlider.value = val;
                if (progressPercentText != null)
                    progressPercentText.text = $"{Mathf.RoundToInt(val * 100f)}%";

                yield return null;
            }

            if (progressSlider != null) progressSlider.value = _progressTo;
            if (progressPercentText != null)
                progressPercentText.text = $"{Mathf.RoundToInt(_progressTo * 100f)}%";

            if (_swapToShowAfterProgress && winShowMeta != null)
            {
                _swapToShowAfterProgress = false;
                //yield return new WaitForSecondsRealtime(progressToShowDelay);
                SwapProgressToShow();
            }
        }

        private void SwapProgressToShow()
        {
            winProgressMeta.SetActive(false);
            winShowMeta.SetActive(true);
            _metaMode = VictoryMetaMode.Show;
            // Progress vừa đầy 100% + swap sang ShowMeta → chạy anim completed (thay vì idle).
            PlayMetaAnim(metaCompletedState);
        }

        // Bật animator và play 1 state theo tên từ đầu (normalizedTime = 0); Update(0) để áp pose ngay
        // trong frame, không bị giữ pose của state cũ 1 nhịp.
        private void PlayMetaAnim(string stateName)
        {
            if (animator == null || string.IsNullOrEmpty(stateName)) return;
            animator.enabled = true;
            animator.Play(stateName, 0, 0f);
            animator.Update(0f);
        }

        #endregion

        #region Callbacks

        private void OnNextClicked()
        {
            SoundController.Instance?.PlayButtonClick();
            VibrationController.Instance?.Vibrate(VibrationStyle.Light);

            // Cộng reward vào tổng coin và update text.
            int newTotal = _totalCoinBefore + _rewardAmount;
            Utils.SetTextSafe(txtCoinAll, newTotal);
            _totalCoinBefore = newTotal;

            _onNext?.Invoke();
            Hide();
        }

        private void OnHomeClicked()
        {
            SoundController.Instance?.PlayButtonClick();
            VibrationController.Instance?.Vibrate(VibrationStyle.Light);
            _onHome?.Invoke();
            Hide();
        }

        #endregion

    }
}

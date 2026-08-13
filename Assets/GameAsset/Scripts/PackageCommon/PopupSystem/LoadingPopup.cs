using Spine.Unity;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Wayfu.Lamkn
{
    public class LoadingPopup : BasePopup
    {
        public enum LoadingMode { FadeOnly, Progress }

        #region Inspector

        [Header("Title Spine (loading)")]
        [SerializeField] [Tooltip("SkeletonGraphic title — chạy anim khi show loading.")]
        private SkeletonGraphic titleSpine;
        [SerializeField] [Tooltip("Tên anim spine chạy khi show (KHÔNG loop). Khớp animation trong title, vd 'title'.")]
        private string titleAnim = "title";
        [SerializeField] private GameObject logoTitle;
        [Header("Loading Refs")]
        [SerializeField] private RectTransform logoTransform;
        [SerializeField] private GameObject progressRoot;
        [SerializeField] private Slider progressBar;
        [SerializeField] private TMP_Text progressText;
        [SerializeField] private TMP_Text loadingLabelText;
        [SerializeField] private TMP_Text tipText;

        [Header("Loading Settings")]
        [SerializeField] private float lerpSpeed = 10f;
        [SerializeField] private float logoScaleDuration = 0.5f;
        [Tooltip("Sorting order for the sub-Canvas on this popup. Higher = on top of other popups regardless of sibling order.")]
        [SerializeField] private int sortingOrder = 10000;
        [Tooltip("Thời gian chạy thanh loading (0 → 0.9) đồng thời là thời lượng tối thiểu giữ trên màn hình.")]
        [SerializeField] private float minDuration = 1f;
        [Tooltip("Time to ramp current → 1.0 after the work reports ready, before hiding.")]
        [SerializeField] private float finishDuration = 0.2f;

        #endregion

        #region State

        private LoadingMode _mode;
        private float _targetProgress;
        private float _displayProgress;
        private bool _running;
        private Coroutine _labelCoroutine;
        private Coroutine _logoCoroutine;

        #endregion

        #region API

        /// <summary>Giống <see cref="ShowFade"/> nhưng hiện ngay không có animation — dùng cho màn hình đầu tiên.</summary>
        public void ShowFadeInstant(string label = "Loading", string tip = null)
        {
            _mode = LoadingMode.FadeOnly;
            if (progressRoot != null) progressRoot.SetActive(false);
            ApplyLabelAndTip(label, tip);
            _running = false;
            if (logoTransform != null) logoTransform.localScale = Vector3.one;
            ShowInstant();
            PlayTitleAnim();
            StartLabelDots(label);
        }

        public void ShowFade(string label = "Loading", string tip = null)
        {
            if (logoTitle != null)
                logoTitle.SetActive(true);
            if(titleSpine != null)
                titleSpine.gameObject.SetActive(false);
            _mode = LoadingMode.FadeOnly;
            if (progressRoot != null) progressRoot.SetActive(false);
            ApplyLabelAndTip(label, tip);
            _running = false;

            // Hiện NGAY ở thông số ban đầu (không fade in); chỉ khi Hide mới fade out.
            ShowInstant();
            //PlayTitleAnim();

            if (logoTransform != null) logoTransform.localScale = Vector3.one;
            StartLabelDots(label);
        }

        public void ShowProgress(string label = "Loading", string tip = null)
        {
            _mode = LoadingMode.Progress;
            if (progressRoot != null) progressRoot.SetActive(true);
            ApplyLabelAndTip(label, tip);

            _targetProgress = 0f;
            _displayProgress = 0f;
            if (progressBar != null) progressBar.value = 0f;
            if (progressText != null) progressText.text = "0%";

            _running = true;

            // Nếu loading đang hiển thị (vd. được gọi sau ShowFadeInstant) thì
            // không chạy lại animation fade/scale/logo — chỉ update nội dung.
            // Loading hiện NGAY ở thông số ban đầu (không fade in); chỉ Hide mới fade out.
            if (IsShowing)
            {
                ShowInstant();
                PlayTitleAnim();
                if (logoTransform != null) logoTransform.localScale = Vector3.one;
                StartLabelDots(label);
            }
            else
            {
                ShowInstant();
                PlayTitleAnim();
                StartLogoAndLabel(label);
            }
        }

        // Chạy anim spine "title" khi show loading — KHÔNG loop (chạy 1 lần rồi giữ frame cuối).
        private void PlayTitleAnim()
        {
            // Chưa gắn SkeletonGraphic thì dùng logo tĩnh làm fallback và giữ nó suốt loading.
            // Logo chỉ biến mất cùng root CanvasGroup khi Hide() fade toàn bộ popup.
            bool hasSpine = titleSpine != null && !string.IsNullOrEmpty(titleAnim);
            if (logoTitle != null) logoTitle.SetActive(!hasSpine);
            if (titleSpine != null) titleSpine.gameObject.SetActive(hasSpine);
            if (!hasSpine) return;
            // Tắt auto-play "Starting Animation" khi enable (nguồn gây flash lúc mở lại) — mình tự điều khiển.
            titleSpine.startingAnimation = null;
            titleSpine.freeze = false;
            if (titleSpine.AnimationState == null) titleSpine.Initialize(false);
            var state = titleSpine.AnimationState;
            if (state == null)
            {
                if (logoTitle != null) logoTitle.SetActive(true);
                titleSpine.gameObject.SetActive(false);
                return;
            }
            state.SetAnimation(0, titleAnim, false);
            titleSpine.Update(0f);
            titleSpine.UpdateMesh();
        }

        public void SetProgress(float value01)
        {
            _targetProgress = Mathf.Clamp01(value01);
            if (_targetProgress >= 1f) _displayProgress = 1f;
        }

        /// <summary>
        /// Show progress, ramp the bar up to 0.9 over <see cref="minDuration"/>, then wait
        /// for <paramref name="isReady"/> to return true, ramp to 1.0 over <see cref="finishDuration"/>,
        /// and hide. Owns the full lifecycle — callers only describe "when is work done".
        /// </summary>
        public void RunUntilReady(System.Func<bool> isReady, System.Action onComplete = null, string label = "Loading", string tip = null)
        {
            ShowProgress(label, tip);
            StartCoroutine(RunUntilReadyRoutine(isReady, onComplete));
        }

        /// <summary>
        /// Overlay loading KHÔNG có thanh progress (chỉ "Loading..."): hiện ngay, chờ tới khi
        /// <paramref name="isReady"/> true (và đủ minDuration) rồi fade out. Dùng cho retry/next.
        /// </summary>
        public void RunFadeUntilReady(System.Func<bool> isReady, System.Action onComplete = null, string label = "Loading", string tip = null)
        {
            ShowFade(label, tip);
            StartCoroutine(FadeUntilReadyRoutine(isReady, onComplete));
        }

        private IEnumerator FadeUntilReadyRoutine(System.Func<bool> isReady, System.Action onComplete)
        {
            float t = 0f;
            float floor = Mathf.Max(0f, minDuration);
            // Giữ overlay tối thiểu minDuration và tới khi level mới build xong.
            while (t < floor || (isReady != null && !isReady()))
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            Hide();
            onComplete?.Invoke();
        }

        private IEnumerator RunUntilReadyRoutine(System.Func<bool> isReady, System.Action onComplete)
        {
            float t = 0f;
            float ramp = Mathf.Max(0.0001f, minDuration); // thanh chạy 0 → 0.9 theo minDuration (không dùng fakeDuration)

            // Phase 1+2: chạy thanh 0 → 0.9 trong minDuration; qua minDuration mà chưa ready thì giữ 0.9 chờ.
            while (t < ramp || (isReady != null && !isReady()))
            {
                t += Time.unscaledDeltaTime;
                SetProgress(Mathf.Clamp01(t / ramp) * 0.9f);
                yield return null;
            }

            // Phase 3: ramp current → 1.0 quickly, then hide
            float startP = Mathf.Min(0.9f, (t / ramp) * 0.9f);
            float ft = 0f;
            float finish = Mathf.Max(0.0001f, finishDuration);
            while (ft < finish)
            {
                ft += Time.unscaledDeltaTime;
                SetProgress(Mathf.Lerp(startP, 1f, ft / finish));
                yield return null;
            }
            SetProgress(1f);
            Hide();
            onComplete?.Invoke();
        }

        public override void Hide()
        {
            _running = false;
            if (_labelCoroutine != null) StopCoroutine(_labelCoroutine);
            if (_logoCoroutine != null) StopCoroutine(_logoCoroutine);
            _labelCoroutine = null;
            _logoCoroutine = null;
            // GIỮ frame title (không cho anim đổi/chạy lại) trong lúc fade đóng → hết nháy.
            if (titleSpine != null) titleSpine.freeze = true;
            base.Hide();
        }

        #endregion

        #region Unity

        protected override void Awake()
        {
            // Loading hiện/ẩn CHỈ bằng fade alpha — mờ ĐỀU cả nền lẫn nội dung, KHÔNG scale.
            //  - contentPanel để trống → BasePopup không chạy tween scale.
            //  - backgroundImage = null → fade qua alpha của root CanvasGroup (mờ toàn bộ overlay),
            //    thay vì chỉ fade riêng ảnh nền còn nội dung hiện tức thì.
            contentPanel = null;
            backgroundImage = null;

            // Sub-Canvas with overrideSorting forces this popup to render above every other popup
            // sharing the parent canvas, regardless of when other popups Show() later. Sibling
            // order alone isn't enough — a popup Shown after Loading would push Loading down.
            var canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;
            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();

            base.Awake();
        }

        private void Update()
        {
            if (_mode != LoadingMode.Progress || !_running) return;
            _displayProgress = Mathf.Lerp(_displayProgress, _targetProgress, Time.unscaledDeltaTime * lerpSpeed);
            if (progressBar != null) progressBar.value = _displayProgress;
            if (progressText != null) progressText.text = $"{Mathf.FloorToInt(_displayProgress * 100f)}%";
        }

        #endregion

        #region Helpers

        private void ApplyLabelAndTip(string label, string tip)
        {
            if (loadingLabelText != null) loadingLabelText.gameObject.SetActive(true);
            if (tipText != null)
            {
                bool hasTip = !string.IsNullOrEmpty(tip);
                tipText.gameObject.SetActive(hasTip);
                if (hasTip) tipText.text = tip;
            }
        }

        private void StartLogoAndLabel(string label)
        {
            if (logoTransform != null)
            {
                if (_logoCoroutine != null) StopCoroutine(_logoCoroutine);
                logoTransform.localScale = Vector3.zero;
                _logoCoroutine = StartCoroutine(LogoScaleRoutine());
            }
            StartLabelDots(label);
        }

        private void StartLabelDots(string label)
        {
            if (loadingLabelText == null) return;
            if (_labelCoroutine != null) StopCoroutine(_labelCoroutine);
            _labelCoroutine = StartCoroutine(LabelDotsRoutine(label));
        }

        private IEnumerator LogoScaleRoutine()
        {
            float timer = 0f;
            while (timer < logoScaleDuration)
            {
                timer += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(timer / logoScaleDuration);
                float ease = 1f - (1f - t) * (1f - t);
                logoTransform.localScale = Vector3.one * ease;
                yield return null;
            }
            logoTransform.localScale = Vector3.one;
            _logoCoroutine = null;
        }

        private IEnumerator LabelDotsRoutine(string label)
        {
            while (true)
            {
                loadingLabelText.text = label;
                yield return new WaitForSecondsRealtime(0.3f);
                loadingLabelText.text = label + ".";
                yield return new WaitForSecondsRealtime(0.3f);
                loadingLabelText.text = label + "..";
                yield return new WaitForSecondsRealtime(0.3f);
                loadingLabelText.text = label + "...";
                yield return new WaitForSecondsRealtime(0.3f);
            }
        }

        #endregion
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Wayfu.Lamkn
{
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class BasePopup : MonoBehaviour
    {
        #region Inspector

        [Header("Base Popup")]
        [Tooltip("Ảnh NỀN phía sau popup — fade alpha riêng khi mở/đóng.")]
        [SerializeField] protected Image backgroundImage;
        [Tooltip("Phần POPUP — scale 0→1 khi mở, 1→0 khi đóng (độc lập với nền).")]
        [SerializeField] protected RectTransform contentPanel;
        [SerializeField] protected float fadeInDuration = 0.2f;
        [SerializeField] protected float fadeOutDuration = 0.2f;
        [SerializeField] protected float contentScaleDuration = 0.25f;
        [SerializeField] protected AnimationCurve contentScaleCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Drawcall Optimization")]
        [Tooltip("Add a sub-Canvas to each text below so its mesh rebuilds don't dirty the whole " +
                 "popup canvas. Drag any TMP_Text whose content changes at runtime.")]
        [SerializeField] protected List<TMP_Text> textsToIsolate = new List<TMP_Text>();
        [SerializeField] protected int indexLayer = 0;
        #endregion

        #region State

        protected CanvasGroup _canvasGroup;
        private Coroutine _fadeCoroutine;
        private Coroutine _scaleCoroutine;
        private Coroutine _lifecycleCoroutine;
        private bool _isShown;
        private float _bgBaseAlpha = 1f; // alpha gốc của ảnh nền (cache lúc Awake) — fade KHÔNG vượt quá giá trị này
        private bool _bgBaseCached;

        #endregion

        #region Unity

        protected virtual void Awake()
        {
            EnsureInitialized();
            CacheBackgroundBaseAlpha();
            // Ẩn ban đầu. Có ảnh nền riêng → fade alpha của nền; không có → fade alpha của root (như cũ).
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;
            SetFadeAlpha(0f);
            if (contentPanel != null) contentPanel.localScale = Vector3.zero;
            _isShown = false;

            CanvasOptimizer.IsolateAll(textsToIsolate);
        }

        protected void EnsureInitialized()
        {
            if (_canvasGroup != null) return;
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        #endregion

        #region API

        public bool IsShowing => gameObject.activeSelf && _isShown;

        /// <summary>Hiện popup ngay lập tức, không chạy bất kỳ animation fade hay scale nào.</summary>
        public void ShowInstant()
        {
            EnsureInitialized();
            CacheBackgroundBaseAlpha();
            StopAnims();
            gameObject.SetActive(true);
            _canvasGroup.alpha = 1f;
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.interactable = true;
            SetFadeAlpha(1f);
            if (contentPanel != null) contentPanel.localScale = Vector3.one;
            _isShown = true;
            OnShowCompleted();
        }

        public virtual void Show()
        {
            EnsureInitialized();
            CacheBackgroundBaseAlpha();
            gameObject.SetActive(true);
            gameObject.transform.SetSiblingIndex(indexLayer);
            // Có ảnh nền riêng → bật root ngay để popup hiện trong lúc scale, nền tự fade.
            // Không có nền riêng → root alpha chính là kênh fade (giữ hành vi cũ).
            _canvasGroup.alpha = backgroundImage != null ? 1f : 0f;
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.interactable = true;
            _isShown = true;

            // Mở: gọi ĐỒNG THỜI fade nền 0→1 và scale popup 0→1.
            PlayBackgroundFade(0f, 1f, fadeInDuration);
            PlayContentScale(Vector3.zero, Vector3.one, contentScaleDuration);
            FireWhenDone(Mathf.Max(fadeInDuration, contentScaleDuration), OnShowCompleted);
        }

        public virtual void Hide()
        {
            if (!gameObject.activeSelf) return;

            EnsureInitialized();
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            _isShown = false;

            // Đóng: fade nền →0; scale popup →0 chỉ khi ScaleOnHide (lớp con có thể tắt).
            PlayBackgroundFade(FadeAlpha, 0f, fadeOutDuration);
            float hideDuration = fadeOutDuration;
            if (ScaleOnHide)
            {
                Vector3 fromScale = contentPanel != null ? contentPanel.localScale : Vector3.one;
                PlayContentScale(fromScale, Vector3.zero, contentScaleDuration);
                hideDuration = Mathf.Max(fadeOutDuration, contentScaleDuration);
            }
            FireWhenDone(hideDuration, () =>
            {
                gameObject.SetActive(false);
                OnHideCompleted();
            });
        }

        protected virtual void OnShowCompleted() { }
        protected virtual void OnHideCompleted() { }

        /// <summary>Lớp con override = false để KHÔNG scale popup khi đóng (chỉ fade).</summary>
        protected virtual bool ScaleOnHide => true;

        #endregion

        #region Animation

        // Fade alpha của ảnh nền (độc lập với scale popup).
        protected void PlayBackgroundFade(float from, float to, float duration)
        {
            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeRoutine(from, to, duration));
        }

        // Scale phần popup (độc lập với fade nền).
        protected void PlayContentScale(Vector3 from, Vector3 to, float duration)
        {
            if (contentPanel == null) return;
            if (_scaleCoroutine != null) StopCoroutine(_scaleCoroutine);
            _scaleCoroutine = StartCoroutine(ScaleRoutine(from, to, duration));
        }

        // Cache alpha gốc của ảnh nền (chỉ 1 lần) — đây là TRẦN alpha khi fade.
        private void CacheBackgroundBaseAlpha()
        {
            if (_bgBaseCached || backgroundImage == null) return;
            _bgBaseAlpha = backgroundImage.color.a;
            _bgBaseCached = true;
        }

        // Mức fade chuẩn hoá [0..1]: ảnh nền nếu có gán (quy về theo alpha gốc), không thì alpha root (hành vi cũ).
        private float FadeAlpha
        {
            get
            {
                if (backgroundImage == null) return _canvasGroup.alpha;
                return _bgBaseAlpha > 0.0001f ? Mathf.Clamp01(backgroundImage.color.a / _bgBaseAlpha) : 0f;
            }
        }

        // t = mức chuẩn hoá [0..1]. Ảnh nền: alpha thực = t * alpha gốc → KHÔNG bao giờ vượt alpha ban đầu.
        private void SetFadeAlpha(float t)
        {
            t = Mathf.Clamp01(t);
            if (backgroundImage != null)
            {
                Color c = backgroundImage.color;
                c.a = t * _bgBaseAlpha;
                backgroundImage.color = c;
            }
            else
            {
                _canvasGroup.alpha = t;
            }
        }

        private void StopAnims()
        {
            if (_fadeCoroutine != null) { StopCoroutine(_fadeCoroutine); _fadeCoroutine = null; }
            if (_scaleCoroutine != null) { StopCoroutine(_scaleCoroutine); _scaleCoroutine = null; }
            if (_lifecycleCoroutine != null) { StopCoroutine(_lifecycleCoroutine); _lifecycleCoroutine = null; }
        }

        private IEnumerator FadeRoutine(float from, float to, float duration)
        {
            if (duration <= 0f)
            {
                SetFadeAlpha(to);
                _fadeCoroutine = null;
                yield break;
            }

            float timer = 0f;
            SetFadeAlpha(from);
            while (timer < duration)
            {
                timer += Time.unscaledDeltaTime;
                SetFadeAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(timer / duration)));
                yield return null;
            }
            SetFadeAlpha(to);
            _fadeCoroutine = null;
        }

        private IEnumerator ScaleRoutine(Vector3 from, Vector3 to, float duration)
        {
            if (contentPanel == null || duration <= 0f)
            {
                if (contentPanel != null) contentPanel.localScale = to;
                _scaleCoroutine = null;
                yield break;
            }

            float timer = 0f;
            while (timer < duration)
            {
                timer += Time.unscaledDeltaTime;
                float t = contentScaleCurve.Evaluate(Mathf.Clamp01(timer / duration));
                contentPanel.localScale = Vector3.LerpUnclamped(from, to, t);
                yield return null;
            }
            contentPanel.localScale = to;
            _scaleCoroutine = null;
        }

        // Gọi onComplete sau 'duration' (unscaled) — chốt lifecycle sau khi 2 anim chạy xong; huỷ nếu Show/Hide mới.
        private void FireWhenDone(float duration, Action onComplete)
        {
            if (_lifecycleCoroutine != null) StopCoroutine(_lifecycleCoroutine);
            _lifecycleCoroutine = onComplete != null ? StartCoroutine(FireRoutine(duration, onComplete)) : null;
        }

        private IEnumerator FireRoutine(float duration, Action onComplete)
        {
            if (duration > 0f) yield return new WaitForSecondsRealtime(duration);
            _lifecycleCoroutine = null;
            onComplete();
        }

        #endregion
    }
}

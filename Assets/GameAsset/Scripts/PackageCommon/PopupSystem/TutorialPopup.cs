using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Wayfu.Lamkn
{
    public class TutorialPopup : BasePopup
    {
        #region Inspector

        [Header("Tutorial Refs")]
        [Tooltip("Bàn tay Spine animate. Nếu được gán, sẽ dùng thay cho handImage tĩnh.")]
        [SerializeField] private TutorialHandController handController;
        [Tooltip("Animation Spine mặc định khi step không chỉ định handAnimation riêng.")]
        [SerializeField] private string defaultHandAnimation = "Tap";
        [Tooltip("Ảnh tĩnh dùng làm fallback khi không gán handController.")]
        [SerializeField] private RectTransform handImage;
        [SerializeField] private TMP_Text guideText;
        [SerializeField] private GameObject guidePanel;
        [SerializeField] private Button tapBlocker;
        [Tooltip("Ảnh banner (Image) hiện khi step bật showBanner — pop/fade cùng content, bấm bất kỳ " +
                 "đâu (Click) để tắt. Đặt banner là con của contentPanel để pop scale. Bỏ trống nếu popup " +
                 "không dùng banner.")]
        [SerializeField] private Image bannerImage;
        [SerializeField] private Image highlightOverlay;
        [Tooltip("Khung highlight THỨ 2 (vd cartimer) — bật khi step có useHighlightScreenPos2.")]
        [SerializeField] private Image highlightOverlay2;
        [Tooltip("Object 'tap to continue' — hiện (scale phập phồng) khi step Click chờ tap (chỉ step có showTapToContinue).")]
        [SerializeField] private RectTransform tapToContinue;
        [Tooltip("Scale nhỏ/lớn khi phập phồng text tap-to-continue.")]
        [SerializeField] private float tapPulseMin = 0.9f;
        [SerializeField] private float tapPulseMax = 1.1f;
        [Tooltip("Tốc độ phập phồng (chu kỳ/giây).")]
        [SerializeField] private float tapPulseSpeed = 2f;
        [Tooltip("Thời gian scale 0→1 của highlightOverlay sau khi popup scale xong, trước khi hiện tay.")]
        [SerializeField] private float highlightScaleDuration = 0.25f;
        [Tooltip("Object mô tả (des) — CHỈ hiện (scale 0→1) khi step có hiển thị tay VÀ có guideText. " +
                 "Bỏ trống = bỏ qua.")]
        [SerializeField] private RectTransform popupTextDes;
        [Tooltip("Thời gian scale 0→1 của popupTextDes.")]
        [SerializeField] private float popupTextDesScaleDuration = 0.2f;

        #endregion

        #region State

        private List<TutorialStep> _flatSteps;
        private int _index = -1;
        private bool _waitingClick;
        private Action _onDone;
        // Fired the moment the popup is shown, BEFORE the content scale-in animation runs.
        private Action _onShown;
        private Coroutine _autoCoroutine;
        private TutorialStep _currentStep;
        // True once the Spine hand has been shown this run — lets subsequent steps glide
        // (MoveTo + re-anim) instead of re-fading in from scratch each time.
        private bool _handShown;

        public bool IsRunning => _flatSteps != null && _index >= 0 && _index < _flatSteps.Count;
        public int CurrentStepIndex => _index;

        #endregion

        #region Unity

        protected override void Awake()
        {
            base.Awake();
            if (tapBlocker != null) tapBlocker.onClick.AddListener(OnTapBlockerClicked);
        }

        #endregion

        #region API

        /// <param name="onShown">Gọi ngay khi popup vừa hiển thị, TRƯỚC khi chạy anim scale-in
        /// (dùng để bật camera/canvas tutorial đúng thời điểm popup xuất hiện).</param>
        public void StartTutorial(List<TutorialStep> steps, Action onDone = null, Action onShown = null)
        {
            if (steps == null || steps.Count == 0)
            {
                onDone?.Invoke();
                return;
            }

            _flatSteps = Flatten(steps);
            if (_flatSteps.Count == 0)
            {
                onDone?.Invoke();
                return;
            }

            _index = -1;
            _onDone = onDone;
            _onShown = onShown;
            _currentStep = null;
            Show();
            // Wait for the popup's content scale-in (BasePopup.Show, contentScaleDuration) to finish
            // BEFORE positioning the hand: while the content panel is still scaling from 0→1, the
            // hand's parent rect isn't at final size, so projecting the car onto it gives a wrong
            // position. Advance() (→ ExecuteStep → hand placement) runs only after the anim settles.
            StartCoroutine(BeginAfterContentScale());
        }

        private IEnumerator BeginAfterContentScale()
        {
            // 0) Popup vừa hiển thị, chưa chạy anim scale-in → bật camera/canvas tutorial.
            var onShown = _onShown;
            _onShown = null;
            onShown?.Invoke();

            // Sau StartTutorial _index == -1 nên step sắp hiện là _flatSteps[0].
            var upcoming = (_flatSteps != null && _flatSteps.Count > 0) ? _flatSteps[0] : null;
            bool banner = upcoming != null && upcoming.showBanner;
            // Highlight ĐỘC LẬP với banner: step có thể vừa hiện banner vừa highlight (vd tut timer).
            bool hasHighlight = upcoming != null && (upcoming.useHighlightScreenPos || upcoming.useHighlightScreenPos2);

            // 1) Ẩn tay + highlightOverlay(1,2) + popupTextDes + tap-to-continue (đặt scale 0) trước khi popup scale-in.
            HideHand();
            HideOverlay(highlightOverlay);
            HideOverlay(highlightOverlay2);
            HidePopupTextDes();
            ShowTapToContinue(false);
            // Banner: bật ảnh NGAY để nó pop (scale + fade) cùng content của BasePopup.Show.
            ShowBanner(banner, banner ? upcoming.bannerSprite : null);

            // 2) Đợi popup content scale-in xong (BasePopup dùng unscaled time → wait realtime).
            if (contentScaleDuration > 0f)
                yield return new WaitForSecondsRealtime(contentScaleDuration);
            // Layout flush để ma trận rect chuẩn trước khi chiếu vị trí.
            Canvas.ForceUpdateCanvases();

            // 3) Áp width/height + vị trí (theo xe target) cho highlightOverlay(1,2), rồi scale 0→1.
            if (hasHighlight)
            {
                if (highlightOverlay != null)
                {
                    ApplyHighlightSizeTo(highlightOverlay, upcoming.highlightSize);
                    if (upcoming.useHighlightScreenPos) PositionHighlightFor(highlightOverlay, upcoming.highlightScreenPos);
                }
                if (highlightOverlay2 != null && upcoming.useHighlightScreenPos2)
                {
                    ApplyHighlightSizeTo(highlightOverlay2, upcoming.highlightSize2);
                    PositionHighlightFor(highlightOverlay2, upcoming.highlightScreenPos2);
                }
                if (highlightOverlay != null) yield return ScaleInRoutine(highlightOverlay.transform, highlightScaleDuration);
                if (highlightOverlay2 != null && upcoming.useHighlightScreenPos2)
                    yield return ScaleInRoutine(highlightOverlay2.transform, highlightScaleDuration);
            }

            // 4) Mới hiện tay (Advance → ExecuteStep → ShowHandAt).
            Advance();
        }

        /// <summary>Đặt width/height cho highlightOverlay theo kích thước yêu cầu. (0,0) = giữ
        /// nguyên kích thước prefab. Dùng SetSizeWithCurrentAnchors để đúng cả khi overlay dùng
        /// anchor stretch.</summary>
        private void ApplyHighlightSize(Vector2 size) => ApplyHighlightSizeTo(highlightOverlay, size);

        private void ApplyHighlightSizeTo(Image overlay, Vector2 size)
        {
            if (overlay == null) return;
            if (size.x <= 0f && size.y <= 0f) return;
            var rt = overlay.rectTransform;
            if (size.x > 0f) rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
            if (size.y > 0f) rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);
        }

        // Ẩn 1 overlay (scale 0 + tắt) — dùng chung cho highlightOverlay(1,2).
        private static void HideOverlay(Image overlay)
        {
            if (overlay == null) return;
            overlay.transform.localScale = Vector3.zero;
            overlay.gameObject.SetActive(false);
        }

        // Định vị/kích thước overlay theo step (không animate reveal — reveal do BeginAfterContentScale lo).
        // use=false → ẩn overlay.
        private void ApplyStepHighlight(Image overlay, bool use, Vector2 size, Vector2 pos)
        {
            if (overlay == null) return;
            if (!use) { HideOverlay(overlay); return; }
            overlay.gameObject.SetActive(true);
            if (overlay.transform.localScale == Vector3.zero) overlay.transform.localScale = Vector3.one;
            ApplyHighlightSizeTo(overlay, size);
            PositionHighlightFor(overlay, pos);
        }

        /// <summary>Đặt highlightOverlay tại một vị trí screen (vd vị trí xe target). Quy đổi
        /// screen → world point trên mặt phẳng parent rồi set transform.position (không phụ thuộc
        /// pivot/anchor), dùng camera từ root canvas nên đúng mọi render mode.</summary>
        private void PositionHighlightAt(Vector2 screenPos) => PositionHighlightFor(highlightOverlay, screenPos);

        private void PositionHighlightFor(Image overlay, Vector2 screenPos)
        {
            if (overlay == null) return;
            var rt = overlay.rectTransform;
            var plane = (rt.parent as RectTransform) ?? rt;
            var canvas = rt.GetComponentInParent<Canvas>();
            var root = canvas != null ? (canvas.rootCanvas != null ? canvas.rootCanvas : canvas) : null;
            Camera uiCam = (root != null && root.renderMode != RenderMode.ScreenSpaceOverlay) ? root.worldCamera : null;
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(plane, screenPos, uiCam, out var world))
                rt.position = world;
        }

        private IEnumerator ScaleInRoutine(Transform target, float duration)
        {
            target.gameObject.SetActive(true);
            if (duration <= 0f) { target.localScale = Vector3.one; yield break; }

            float timer = 0f;
            target.localScale = Vector3.zero;
            while (timer < duration)
            {
                timer += Time.unscaledDeltaTime;
                float k = contentScaleCurve != null
                    ? contentScaleCurve.Evaluate(Mathf.Clamp01(timer / duration))
                    : Mathf.Clamp01(timer / duration);
                target.localScale = Vector3.one * k;
                yield return null;
            }
            target.localScale = Vector3.one;
        }

        public void Advance()
        {
            if (_currentStep != null) _currentStep.onStepExit?.Invoke();
            StopAuto();
            ShowTapToContinue(false); // rời step → tắt text tap-to-continue (step sau tự bật lại nếu cần)

            _index++;
            if (_flatSteps == null || _index >= _flatSteps.Count)
            {
                Complete();
                return;
            }

            _currentStep = _flatSteps[_index];

            if (_currentStep.delayBeforeShow > 0f)
                StartCoroutine(DelayThenExecute(_currentStep));
            else
                ExecuteStep(_currentStep);
        }

        public void NotifyActionDone()
        {
            if (!IsRunning || _currentStep == null) return;
            if (_currentStep.advanceMode == TutorialAdvanceMode.Action) Advance();
        }

        public void Stop()
        {
            StopAuto();
            _flatSteps = null;
            _currentStep = null;
            _index = -1;
            _waitingClick = false;
            SetVisuals(false);
            Hide();
        }

        /// <summary>
        /// DEBUG/TEST: chiếu một vị trí world (vd vị trí xe) lên UI tutorial và LOG ra:
        ///   - screen point (qua <paramref name="gameplayCamera"/>), có nằm trong màn hình không, z (trước/sau camera)
        ///   - root canvas + render mode + camera UI dùng để quy đổi
        ///   - world point trên mặt phẳng canvas + anchoredPosition tương ứng của bàn tay
        /// Nếu <paramref name="moveHand"/> = true sẽ hiện popup và đặt bàn tay ngay tại đó để xem trực quan.
        /// Trả về screen point đã tính (để caller dùng thêm nếu cần).
        /// </summary>
        public Vector3 DebugProjectWorldToUI(Camera gameplayCamera, Vector3 worldPos, bool moveHand = true)
        {
            if (gameplayCamera == null)
            {
                Debug.LogWarning("[TutorialPopup.Debug] gameplayCamera == null — không thể chiếu.");
                return Vector3.zero;
            }

            Vector3 screen = gameplayCamera.WorldToScreenPoint(worldPos);
            bool behind = screen.z < 0f;
            bool inScreen = screen.x >= 0 && screen.x <= Screen.width && screen.y >= 0 && screen.y <= Screen.height;

            // Mặt phẳng để quy đổi = parent rect của bàn tay (hoặc handImage nếu không có controller).
            RectTransform handRect = handController != null
                ? handController.transform as RectTransform
                : handImage;
            RectTransform plane = handRect != null ? (handRect.parent as RectTransform) ?? handRect : null;

            Canvas canvas = handRect != null ? handRect.GetComponentInParent<Canvas>() : null;
            Canvas root = canvas != null ? (canvas.rootCanvas != null ? canvas.rootCanvas : canvas) : null;
            Camera uiCam = (root != null && root.renderMode != RenderMode.ScreenSpaceOverlay) ? root.worldCamera : null;

            string convInfo = "n/a";
            Vector2 anchoredEquivalent = Vector2.zero;
            if (plane != null &&
                RectTransformUtility.ScreenPointToWorldPointInRectangle(plane, new Vector2(screen.x, screen.y), uiCam, out var world))
            {
                // anchoredPosition tương đương = world point đổi về local của parent.
                Vector3 local = plane.InverseTransformPoint(world);
                anchoredEquivalent = new Vector2(local.x, local.y);
                convInfo = $"world={world} -> parentLocal(≈anchored)={anchoredEquivalent}";
            }

            Debug.Log(
                "[TutorialPopup.Debug] ===== Project car/world onto tutorial UI =====\n" +
                $"world={worldPos}\n" +
                $"screen=({screen.x:F1},{screen.y:F1}) z={screen.z:F2} behindCam={behind} inScreen={inScreen} screenSize=({Screen.width}x{Screen.height})\n" +
                $"rootCanvas='{(root != null ? root.name : "null")}' renderMode={(root != null ? root.renderMode.ToString() : "?")} uiCam='{(uiCam != null ? uiCam.name : "null")}'\n" +
                $"handRect='{(handRect != null ? handRect.name : "null")}' plane(parent)='{(plane != null ? plane.name : "null")}' planePivot={(plane != null ? plane.pivot.ToString() : "?")} planeRect={(plane != null ? plane.rect.ToString() : "?")}\n" +
                $"conversion: {convInfo}");

            if (moveHand && handController != null && plane != null)
            {
                if (!IsShowing) Show();
                handController.ShowAtScreen(new Vector2(screen.x, screen.y),
                    string.IsNullOrEmpty(defaultHandAnimation) ? null : defaultHandAnimation);
                Debug.Log($"[TutorialPopup.Debug] hand anchoredPosition sau khi đặt = {((RectTransform)handController.transform).anchoredPosition} | position(world)={handController.transform.position}");
            }

            return screen;
        }

        #endregion

        #region Step Logic

        private static List<TutorialStep> Flatten(List<TutorialStep> source)
        {
            var result = new List<TutorialStep>();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++)
            {
                var s = source[i];
                if (s == null) continue;
                if (s.HasSubSteps) result.AddRange(Flatten(s.subSteps));
                else result.Add(s);
            }
            return result;
        }

        private IEnumerator DelayThenExecute(TutorialStep step)
        {
            if (tapBlocker != null) tapBlocker.gameObject.SetActive(false);
            _waitingClick = false;
            HideHand(); // ẩn tay trong lúc delay (click xong tắt hand); ExecuteStep sau delay sẽ hiện lại ở vị trí step mới
            yield return new WaitForSecondsRealtime(step.delayBeforeShow);
            ExecuteStep(step);
        }

        private void ExecuteStep(TutorialStep step)
        {
            step.onStepEnter?.Invoke();

            // Banner + tay ĐỘC LẬP: 1 step có thể vừa hiện banner vừa chỉ tay (tut timer click-car).
            ShowBanner(step.showBanner, step.bannerSprite);

            if (step.showHand)
            {
                string anim = string.IsNullOrEmpty(step.handAnimation) ? defaultHandAnimation : step.handAnimation;
                ShowHandAt(step.handScreenPos, anim);

                if (guideText != null) guideText.text = step.guideText;
                if (guidePanel != null) guidePanel.SetActive(!string.IsNullOrEmpty(step.guideText));

                // popupTextDes: CHỈ hiện (scale 0→1) khi step có HIỂN THỊ TAY và CÓ guideText (thông tin des).
                bool handShown = handController != null || handImage != null;
                ShowPopupTextDes(handShown && !string.IsNullOrEmpty(step.guideText), step);
            }
            else
            {
                HideHand();
                if (guidePanel != null) guidePanel.SetActive(false);
                HidePopupTextDes();
            }

            // highlightOverlay(1,2): reveal (scale 0→1) do BeginAfterContentScale lo cho step ĐẦU; ở đây
            // định vị/kích thước theo step (cho các step sau), và ẩn overlay nếu step này không dùng.
            ApplyStepHighlight(highlightOverlay, step.useHighlightScreenPos, step.highlightSize, step.highlightScreenPos);
            ApplyStepHighlight(highlightOverlay2, step.useHighlightScreenPos2, step.highlightSize2, step.highlightScreenPos2);

            switch (step.advanceMode)
            {
                case TutorialAdvanceMode.Click:
                    if (tapBlocker != null) tapBlocker.gameObject.SetActive(true);
                    _waitingClick = true;
                    ShowTapToContinue(step.showTapToContinue); // đang chờ tap → hiện text "tap to continue" (nếu bật)
                    break;
                case TutorialAdvanceMode.Auto:
                    if (tapBlocker != null) tapBlocker.gameObject.SetActive(false);
                    _waitingClick = false;
                    ShowTapToContinue(false);
                    _autoCoroutine = StartCoroutine(AutoAdvanceRoutine(step.autoDelay));
                    break;
                case TutorialAdvanceMode.Action:
                    if (tapBlocker != null) tapBlocker.gameObject.SetActive(false);
                    _waitingClick = false;
                    ShowTapToContinue(false);
                    break;
            }
        }

        private Coroutine _tapPulseCo;

        // Hiện/ẩn text "tap to continue" + phập phồng scale (unscaled). Ẩn = tắt + dừng pulse.
        private void ShowTapToContinue(bool show)
        {
            if (tapToContinue == null) return;
            if (_tapPulseCo != null) { StopCoroutine(_tapPulseCo); _tapPulseCo = null; }
            tapToContinue.gameObject.SetActive(show);
            tapToContinue.localScale = Vector3.one;
            if (show) _tapPulseCo = StartCoroutine(TapPulseLoop());
        }

        private IEnumerator TapPulseLoop()
        {
            float e = 0f;
            while (true)
            {
                e += Time.unscaledDeltaTime * Mathf.Max(0.01f, tapPulseSpeed);
                float k = 0.5f * (1f + Mathf.Sin(e * Mathf.PI * 2f)); // 0→1→0 mượt
                tapToContinue.localScale = Vector3.one * Mathf.Lerp(tapPulseMin, tapPulseMax, k);
                yield return null;
            }
        }

        private IEnumerator AutoAdvanceRoutine(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            Advance();
        }

        private void StopAuto()
        {
            if (_autoCoroutine != null)
            {
                StopCoroutine(_autoCoroutine);
                _autoCoroutine = null;
            }
        }

        private void Complete()
        {
            _currentStep = null;
            _flatSteps = null;
            _index = -1;
            _waitingClick = false;
            SetVisuals(false);
            Hide();

            var cb = _onDone;
            _onDone = null;
            cb?.Invoke();
        }

        private void SetVisuals(bool active)
        {
            if (tapBlocker != null) tapBlocker.gameObject.SetActive(active);
            if (active) { if (handImage != null) handImage.gameObject.SetActive(true); }
            else HideHand();
            if (highlightOverlay != null) highlightOverlay.gameObject.SetActive(active);
            if (highlightOverlay2 != null) highlightOverlay2.gameObject.SetActive(active);
            if (guidePanel != null) guidePanel.SetActive(active);
            if (!active) { HidePopupTextDes(); ShowTapToContinue(false); }
        }

        /// <summary>Hiện/ẩn popupTextDes. Khi hiện: (định vị theo step nếu có) + scale 0→1 (unscaled).
        /// Khi ẩn: scale 0 + tắt.</summary>
        private void ShowPopupTextDes(bool show, TutorialStep step = null)
        {
            if (popupTextDes == null) return;
            if (!show) { HidePopupTextDes(); return; }
            // Đặt vị trí trước khi scale-in (nếu step yêu cầu) — vd nâng des lên trên đống object.
            if (step != null && step.useTextDesScreenPos)
                PositionRectAtScreen(popupTextDes, step.textDesScreenPos);
            StartCoroutine(ScaleInRoutine(popupTextDes, popupTextDesScaleDuration));
        }

        /// <summary>Đặt một RectTransform tại vị trí screen (quy đổi screen → world trên mặt phẳng parent,
        /// dùng camera từ root canvas nên đúng mọi render mode). Dùng chung cho popupTextDes/overlay.</summary>
        private void PositionRectAtScreen(RectTransform rt, Vector2 screenPos)
        {
            if (rt == null) return;
            var plane = (rt.parent as RectTransform) ?? rt;
            var canvas = rt.GetComponentInParent<Canvas>();
            var root = canvas != null ? (canvas.rootCanvas != null ? canvas.rootCanvas : canvas) : null;
            Camera uiCam = (root != null && root.renderMode != RenderMode.ScreenSpaceOverlay) ? root.worldCamera : null;
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(plane, screenPos, uiCam, out var world))
                rt.position = world;
        }

        private void HidePopupTextDes()
        {
            if (popupTextDes == null) return;
            popupTextDes.localScale = Vector3.zero;
            popupTextDes.gameObject.SetActive(false);
        }

        /// <summary>Place the tutorial hand at a screen position. Prefers the Spine
        /// <see cref="TutorialHandController"/> (animated) and falls back to the static
        /// <see cref="handImage"/> when no controller is assigned.</summary>
        private void ShowHandAt(Vector2 screenPos, string animationName)
        {
            if (handController != null)
            {
                if (!_handShown)
                {
                    // First appearance: snap into place + fade in. ShowAtScreen converts the
                    // screen pixel into the canvas plane so it points correctly in any render mode.
                    handController.ShowAtScreen(screenPos, animationName);
                    _handShown = true;
                }
                else
                {
                    // Subsequent steps: glide to the new spot and (re)play the step's animation.
                    handController.MoveToScreen(screenPos);
                    handController.PlayAnimation(animationName);
                }
                return;
            }

            if (handImage != null)
            {
                handImage.gameObject.SetActive(true);
                handImage.position = screenPos;
            }
        }

        // Hiện/ẩn ảnh banner. show=false → tắt. show=true → bật; overrideSprite != null thì đổi
        // sprite, null thì giữ sprite gán sẵn trong prefab. Không tween — banner pop/scale-out
        // theo content của BasePopup.
        private void ShowBanner(bool show, Sprite overrideSprite = null)
        {
            if (bannerImage == null) return;
            if (!show)
            {
                bannerImage.gameObject.SetActive(false);
                return;
            }
            if (overrideSprite != null) bannerImage.sprite = overrideSprite;
            bannerImage.gameObject.SetActive(true);
        }

        private void HideHand()
        {
            _handShown = false;
            if (handController != null) handController.Hide();
            if (handImage != null) handImage.gameObject.SetActive(false);
        }

        private void OnTapBlockerClicked()
        {
            if (!_waitingClick) return;
            _waitingClick = false;
            Advance();
        }

        #endregion
    }
}

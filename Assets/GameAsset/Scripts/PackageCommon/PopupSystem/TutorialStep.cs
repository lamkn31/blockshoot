using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Wayfu.Lamkn
{
    public enum TutorialAdvanceMode { Click, Action, Auto }

    [Serializable]
    public class TutorialStep
    {
        public string id;

        [TextArea(2, 5)]
        public string guideText;

        public Vector2 handScreenPos;

        [Tooltip("Tên animation Spine của bàn tay cho bước này (vd 'Tap'). Bỏ trống = dùng " +
                 "defaultHandAnimation của TutorialPopup. Chỉ áp dụng khi popup có TutorialHandController.")]
        public string handAnimation = "";

        public GameObject highlightTarget;

        [Tooltip("Kích thước (width,height) áp cho highlightOverlay khi bước này hiện. " +
                 "(0,0) = giữ nguyên kích thước sẵn có trong prefab.")]
        public Vector2 highlightSize = Vector2.zero;

        [Tooltip("Vị trí (screen px) đặt highlightOverlay — thường là vị trí xe target. " +
                 "Bật useHighlightScreenPos để áp dụng; tắt thì giữ vị trí prefab.")]
        public Vector2 highlightScreenPos;
        public bool useHighlightScreenPos;

        [Header("Highlight thứ 2 (vd cartimer) — dùng highlightOverlay2")]
        public GameObject highlightTarget2;
        [Tooltip("Kích thước (w,h) cho highlightOverlay2. (0,0) = giữ nguyên prefab.")]
        public Vector2 highlightSize2 = Vector2.zero;
        [Tooltip("Vị trí (screen px) đặt highlightOverlay2. Bật useHighlightScreenPos2 để áp dụng.")]
        public Vector2 highlightScreenPos2;
        public bool useHighlightScreenPos2;

        [Tooltip("Bật: bước này hiện ẢNH BANNER (pop scale) thay cho tay/guide/highlight. " +
                 "Thường đi kèm advanceMode = Click (bấm bất kỳ đâu để tắt).")]
        public bool showBanner = false;

        [Tooltip("Override sprite cho banner của bước này. Bỏ trống = giữ nguyên sprite gán sẵn trên " +
                 "bannerImage trong prefab.")]
        public Sprite bannerSprite;

        public bool IsBanner => showBanner;

        [Tooltip("Hiện BÀN TAY chỉ vào handScreenPos (độc lập với banner). Mặc định false — banner mode để false; " +
                 "step chỉ tay (targeted/click-car) đặt true.")]
        public bool showHand = false;

        [Tooltip("Hiện text 'tap to continue' (scale phập phồng) khi step Click đang chờ tap. Chỉ dùng cho step banner timer.")]
        public bool showTapToContinue = false;

        [Header("Popup Text Des (mô tả) — vị trí")]
        [Tooltip("Bật: đặt Popup Text Des tại textDesScreenPos (screen px). Tắt: giữ nguyên vị trí trong prefab.")]
        public bool useTextDesScreenPos = false;
        [Tooltip("Vị trí (screen px) đặt Popup Text Des khi bật useTextDesScreenPos.")]
        public Vector2 textDesScreenPos;

        public TutorialAdvanceMode advanceMode = TutorialAdvanceMode.Click;
        public float autoDelay = 1.5f;
        public float delayBeforeShow = 0f;

        public UnityEvent onStepEnter;
        public UnityEvent onStepExit;

        public List<TutorialStep> subSteps = new List<TutorialStep>();

        public bool HasSubSteps => subSteps != null && subSteps.Count > 0;
    }
}

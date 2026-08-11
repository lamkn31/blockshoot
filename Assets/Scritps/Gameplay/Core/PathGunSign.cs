using TMPro;
using UnityEngine;

namespace Wayfu.Lamkn
{
    public class PathGunSign : MonoBehaviour
    {
        [SerializeField] private TMP_Text countLabel;
        [SerializeField, Min(0.1f)] private float fullFlashSpeed = 5f;
        [SerializeField] private Color fullFlashColor = Color.red;
        private string _lastValue;
        private Color _normalColor;

        private void Awake()
        {
            if (countLabel == null) countLabel = GetComponentInChildren<TMP_Text>(true);
            if (countLabel != null) _normalColor = countLabel.color;
        }

        private void Update()
        {
            // Tính cả gun đang queue sau khi click/deploy: chúng đã chiếm một chỗ trên path
            // dù chưa bắt đầu follower chạy vòng.
            int current = PathManager.IsActive
                ? PathManager.Instance.GunCount + PathManager.Instance.QueueCount
                : 0;
            int max = GameSettings.Instance != null ? GameSettings.Instance.MaxGunOnPath : 0;
            string value = current + "/" + max;
            if (_lastValue != value)
            {
                _lastValue = value;
                if (countLabel != null) countLabel.text = value;
            }

            if (countLabel == null) return;
            bool full = max > 0 && current >= max;
            if (full)
            {
                float flash = Mathf.PingPong(Time.time * fullFlashSpeed, 1f);
                countLabel.color = Color.Lerp(_normalColor, fullFlashColor, flash);
            }
            else countLabel.color = _normalColor;
        }
    }
}

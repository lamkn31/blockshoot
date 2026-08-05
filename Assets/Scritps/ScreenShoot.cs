using UnityEngine;
using System.IO;
#if UNITY_EDITOR
using UnityEditor; // Cần thư viện này để refresh Asset
#endif
public class ScreenShoot : MonoBehaviour
{
    [Header("Controls")]
    [SerializeField] private KeyCode captureKey = KeyCode.Space;

    [Header("Settings")]
    [SerializeField] private string folderName = "Screenshots"; // Sẽ tự tạo thư mục "Assets/Screenshots"
    [Tooltip("1 = Độ phân giải gốc, 2 = Gấp 2 lần, 3 = Gấp 3 lần, v.v.")]
    [SerializeField] private int superSize = 1;

    private void Update()
    {
        if (Input.GetKeyDown(captureKey))
        {
            CaptureScreen();
        }
    }

    public void CaptureScreen()
    {
        // Đặt đường dẫn trực tiếp vào trong thư mục Assets
        string directoryPath = Path.Combine(Application.dataPath, folderName);

        // Tạo thư mục nếu chưa có
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        string filename = $"Screenshot_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
        string fullPath = Path.Combine(directoryPath, filename);

        // Chụp màn hình
        ScreenCapture.CaptureScreenshot(fullPath, superSize);
        Debug.Log($"Đã lưu ảnh tại: {fullPath}");

        // Yêu cầu Unity Editor làm mới thư mục Assets để hiển thị ảnh ngay lập tức
#if UNITY_EDITOR
        AssetDatabase.Refresh();
#endif
    }
}

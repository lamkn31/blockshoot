using UnityEngine;

namespace Wayfu.Lamkn
{
public class RoundedPolylineFollower : MonoBehaviour
{
    [Header("References")]
    public RoundedPolylinePath targetPath;

    [Header("Movement Settings")]
    public float moveSpeed = 3.0f; // Tốc độ di chuyển thực tế ổn định (mét/giây)

    private float currentDistance = 0f; // Khoảng cách vật thể đã đi được trên đường
    private float startDistance = 0f;   // Điểm VÀO path — mốc đếm vòng
    private float verticalOffset;

    /// <summary>Arc-length hiện tại trên path (chưa wrap). PathManager đọc để tính khoảng cách giữa các gun.</summary>
    public float CurrentDistance => currentDistance;

    /// <summary>
    /// Số vòng đã chạy TRỌN quanh path tính từ điểm vào. Tăng 1 mỗi lần về lại đúng chỗ xuất phát.
    /// Tính thẳng từ currentDistance (biến này cộng dồn vô hạn, chỉ GetPointAtDistance mới wrap) nên
    /// không cần giữ thêm state.
    /// </summary>
    public int LapCount
    {
        get
        {
            if (targetPath == null) return 0;
            float total = targetPath.TotalLength;
            return total > 1e-4f ? Mathf.FloorToInt((currentDistance - startDistance) / total) : 0;
        }
    }

    /// <summary>Gắn path + vị trí bắt đầu + tốc độ (gọi khi gun được deploy lên path).</summary>
    public void Init(RoundedPolylinePath path, float startDistance, float speed, float pathVerticalOffset = 0f)
    {
        targetPath = path;
        currentDistance = startDistance;
        this.startDistance = startDistance;
        moveSpeed = speed;
        verticalOffset = pathVerticalOffset;
        if (targetPath != null) transform.position = PathPoint(currentDistance);
    }

    private void Start()
    {
        if (targetPath == null)
        {
            targetPath = FindFirstObjectByType<RoundedPolylinePath>();
        }

        if (targetPath == null)
        {
            Debug.LogError("Vui lòng gán RoundedPolylinePath hợp lệ!");
            enabled = false;
            return;
        }

        // Đặt vị trí ban đầu
        transform.position = PathPoint(currentDistance);
    }

    private void Update()
    {
        if (targetPath == null) return;

        // Tăng khoảng cách dựa trên vận tốc m/s thực tế nhân với delta time
        currentDistance += moveSpeed * Time.deltaTime;

        // Cập nhật tọa độ mới bằng hàm nội suy nhị phân
        Vector3 newPosition = PathPoint(currentDistance);
        transform.position = newPosition;

        // Tính hướng đi tiếp theo (nhìn trước 0.05 mét) để xoay mặt theo đường ray — trên SÀN XZ.
        Vector3 lookAheadPos = PathPoint(currentDistance + 0.05f);
        Vector3 direction = lookAheadPos - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 1e-6f)
            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private Vector3 PathPoint(float distance) => targetPath.GetPointAtDistance(distance) + Vector3.up * verticalOffset;
}
}

using UnityEngine;
using System.Collections.Generic;

namespace Wayfu.Lamkn
{
public class RoundedPolylinePath : MonoBehaviour
{
    [Header("Path Settings")]
    public List<Transform> waypoints = new List<Transform>();
    public bool isClosed = true;
    [Tooltip("RoundedCorner = nối thẳng + bo góc. Bezier = cong mượt toàn phần (cornerRadius bỏ qua).")]
    public PathStyle style = PathStyle.RoundedCorner;
    public float cornerRadius = 1.0f;

    [Header("Resolution Settings")]
    [Tooltip("Số điểm tạo thêm tại mỗi góc cua để làm mượt")]
    public int curveSamples = 8;

    [HideInInspector] public Vector3[] samples;
    [HideInInspector] public float[] sampleArc;
    public float TotalLength { get; private set; }

    // Path-space sampling API used by mesh bending. Keeping position and tangent on the same
    // normalized parameter guarantees adjacent mesh tiles share the exact same seam frame.
    public Vector3 Evaluate(float t)
    {
        if (samples == null || samples.Length < 2 || TotalLength <= 1e-5f) return samples != null && samples.Length > 0 ? samples[0] : Vector3.zero;
        t = isClosed ? Mathf.Repeat(t, 1f) : Mathf.Clamp01(t);
        if (!isClosed && t >= 1f) return samples[samples.Length - 1];
        return GetPointAtDistance(t * TotalLength);
    }

    public Quaternion Tangent(float t)
    {
        const float dt = 0.0005f;
        Vector3 a = Evaluate(t - dt), b = Evaluate(t + dt);
        Vector3 direction = b - a;
        if (direction.sqrMagnitude < 1e-8f) direction = Vector3.forward;
        return Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private void Awake()
    {
        GeneratePath();
    }

    // Hàm khởi tạo và tính toán toàn bộ đường đi (Port từ logic của bạn)
    public void GeneratePath()
    {
        if (waypoints == null || waypoints.Count < 2) return;

        var wp = new Vector3[waypoints.Count];
        for (int i = 0; i < waypoints.Count; i++)
        {
            if (waypoints[i] == null) return;
            wp[i] = waypoints[i].position;
        }

        samples = BuildSamples(wp, isClosed, cornerRadius, curveSamples, style);
        if (samples == null) return;

        sampleArc = new float[samples.Length];
        float acc = 0f;
        for (int i = 1; i < samples.Length; i++)
        {
            acc += Vector3.Distance(samples[i - 1], samples[i]);
            sampleArc[i] = acc;
        }
        TotalLength = acc;
    }

    /// <summary>
    /// Dựng chuỗi điểm của đường BO GÓC từ danh sách waypoint. Tách static để Level Tool vẽ được ĐÚNG
    /// đường cong như runtime — một nguồn duy nhất, không vẽ xấp xỉ bằng đoạn thẳng nối waypoint.
    /// </summary>
    public static Vector3[] BuildSamples(IList<Vector3> wpPositions, bool isClosed, float cornerRadius,
                                         int curveSamples = 8, PathStyle style = PathStyle.RoundedCorner)
    {
        int n = wpPositions != null ? wpPositions.Count : 0;
        if (n < 2) return null;

        if (style == PathStyle.Bezier) return BuildBezierSamples(wpPositions, isClosed, curveSamples);

        var pts = new List<Vector3>(n * (curveSamples + 2));

        int cornerStart = isClosed ? 0 : 1;
        int cornerEnd = isClosed ? n : n - 1;
        if (!isClosed) pts.Add(wpPositions[0]);

        for (int i = cornerStart; i < cornerEnd; i++)
        {
            Vector3 prev = wpPositions[(i - 1 + n) % n];
            Vector3 cur = wpPositions[i];
            Vector3 next = wpPositions[(i + 1) % n];

            Vector3 inDir = cur - prev;
            Vector3 outDir = next - cur;
            float inLen = inDir.magnitude;
            float outLen = outDir.magnitude;
            if (inLen < 1e-5f || outLen < 1e-5f) { pts.Add(cur); continue; }

            Vector3 inN = inDir / inLen;
            Vector3 outN = outDir / outLen;
            if (Vector3.Cross(inN, outN).sqrMagnitude < 1e-6f) { pts.Add(cur); continue; }

            float r = Mathf.Min(cornerRadius, 0.5f * Mathf.Min(inLen, outLen));
            Vector3 a = cur - inN * r; // Bắt đầu đoạn cong
            Vector3 b = cur + outN * r; // Kết thúc đoạn cong
            pts.Add(a);

            float cosA = Mathf.Clamp(Vector3.Dot(inN, outN), -1f, 1f);
            float alpha = Mathf.Acos(cosA);
            float tanHalf = Mathf.Tan(alpha * 0.5f);
            Vector3 bis = outN - inN;
            bool arcOk = false;

            if (bis.sqrMagnitude > 1e-10f && tanHalf > 1e-4f)
            {
                bis.Normalize();
                float R = r / tanHalf;
                Vector3 O = cur + bis * Mathf.Sqrt(r * r + R * R); // Tâm cung tròn
                Vector3 va = a - O;
                Vector3 vb = b - O;
                Vector3 axis = Vector3.Cross(va, vb);

                if (axis.sqrMagnitude > 1e-12f)
                {
                    axis.Normalize();
                    float sweepDeg = Vector3.Angle(va, vb);
                    for (int k = 1; k <= curveSamples; k++)
                    {
                        float ang = sweepDeg * (k / (float)(curveSamples + 1));
                        pts.Add(O + Quaternion.AngleAxis(ang, axis) * va);
                    }
                    arcOk = true;
                }
            }

            if (!arcOk)
            {
                // Fallback Bezier bậc 2 nếu cung tròn bị lỗi
                for (int k = 1; k <= curveSamples; k++)
                {
                    float t = k / (float)(curveSamples + 1);
                    float u = 1f - t;
                    pts.Add(u * u * a + 2f * u * t * cur + t * t * b);
                }
            }
            pts.Add(b);
        }

        if (isClosed) pts.Add(pts[0]); else pts.Add(wpPositions[n - 1]);
        return pts.ToArray();
    }

    /// <summary>
    /// Đường cong MƯỢT toàn phần đi qua đúng mọi waypoint: mỗi đoạn wp[i]→wp[i+1] là 1 Bezier bậc 3,
    /// 2 control point suy từ waypoint KỀ theo Catmull-Rom (tiếp tuyến tại wp[i] ∥ wp[i+1]−wp[i−1]).
    /// Nhờ 2 đoạn liền kề dùng chung tiếp tuyến tại điểm nối nên đường liền mạch, không gãy góc.
    /// <para>Khác RoundedCorner: không có đoạn thẳng nào, và cornerRadius vô nghĩa ở đây.</para>
    /// </summary>
    private static Vector3[] BuildBezierSamples(IList<Vector3> wp, bool isClosed, int curveSamples)
    {
        int n = wp.Count;
        int segs = isClosed ? n : n - 1;          // kín thì có thêm đoạn cuối→đầu
        int steps = Mathf.Max(2, curveSamples + 1);
        var pts = new List<Vector3>(segs * steps + 1);

        for (int i = 0; i < segs; i++)
        {
            Vector3 p0 = wp[WrapIndex(i - 1, n, isClosed)];
            Vector3 p1 = wp[WrapIndex(i, n, isClosed)];
            Vector3 p2 = wp[WrapIndex(i + 1, n, isClosed)];
            Vector3 p3 = wp[WrapIndex(i + 2, n, isClosed)];

            // Catmull-Rom → control point Bezier. Hệ số 1/6 là hệ số chuẩn để 2 dạng khớp nhau.
            Vector3 c1 = p1 + (p2 - p0) / 6f;
            Vector3 c2 = p2 - (p3 - p1) / 6f;

            // Bỏ t=1 (trùng đầu đoạn sau) → không sinh điểm trùng làm sampleArc có đoạn dài 0.
            for (int k = 0; k < steps; k++) pts.Add(CubicBezier(p1, c1, c2, p2, k / (float)steps));
        }
        pts.Add(wp[isClosed ? 0 : n - 1]); // chốt điểm cuối (kín thì khép về waypoint đầu)
        return pts.ToArray();
    }

    // Path hở: kẹp ở 2 biên → waypoint đầu/cuối tự làm điểm ảo, tiếp tuyến 2 đầu thành thẳng.
    private static int WrapIndex(int i, int n, bool isClosed) =>
        isClosed ? ((i % n) + n) % n : Mathf.Clamp(i, 0, n - 1);

    private static Vector3 CubicBezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
    {
        float u = 1f - t;
        return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
    }

    // Hàm lấy vị trí dựa trên khoảng cách tuyệt đối (distance) chạy trên đường thay vì dùng biến t (0->1) chung chung
    public Vector3 GetPointAtDistance(float distance)
    {
        if (samples == null || samples.Length == 0) GeneratePath();
        if (samples == null || samples.Length == 0) return Vector3.zero;

        // AUTO LOOP: luôn wrap khoảng cách quanh path, BẤT KỂ isClosed (yêu cầu #1). Gun chạy vòng
        // vô hạn. Nếu polyline chưa khép kín hình học thì có 1 điểm "nhảy" tại mối nối cuối→đầu.
        if (TotalLength > 1e-4f) distance = Mathf.Repeat(distance, TotalLength);

        // Tìm kiếm nhị phân (Binary Search) trên mảng _sampleArc để tìm phân đoạn chứa khoảng cách này
        int low = 0;
        int high = sampleArc.Length - 1;
        while (low < high - 1)
        {
            int mid = (low + high) / 2;
            if (sampleArc[mid] <= distance) low = mid;
            else high = mid;
        }

        // Nội suy tuyến tính chính xác giữa 2 điểm mẫu kế cận
        float d0 = sampleArc[low];
        float d1 = sampleArc[high];
        if (Mathf.Approximately(d0, d1)) return samples[low];

        float t = (distance - d0) / (d1 - d0);
        return Vector3.Lerp(samples[low], samples[high], t);
    }

    private void OnDrawGizmos()
    {
        // Tự động lấy các con làm waypoint trong Editor để dễ thao tác
        if (waypoints.Count == 0 && transform.childCount > 0)
        {
            foreach (Transform child in transform) waypoints.Add(child);
        }

        GeneratePath();

        if (samples == null || samples.Length < 2) return;

        // Vẽ đường bo góc (Màu xanh lá cây đặc trưng của đường thẳng bo cua)
        Gizmos.color = Color.green;
        for (int i = 1; i < samples.Length; i++)
        {
            Gizmos.DrawLine(samples[i - 1], samples[i]);
        }

        // Vẽ các đỉnh thô ban đầu để trực quan hóa
        Gizmos.color = Color.red;
        foreach (var wp in waypoints)
        {
            if (wp != null) Gizmos.DrawWireSphere(wp.position, 0.1f);
        }
    }
}
}

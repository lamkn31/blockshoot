using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Wayfu.Lamkn
{
    /// <summary>
    /// Gắn trên ROOT của mỗi MAP prefab. Giữ danh sách MỐC vị trí slot: mỗi Transform = nơi sinh
    /// GUN 0 (gun đầu) của 1 slot; các gun sau xếp lùi theo spacing chung (GameSettings). Thứ tự phần tử
    /// trong list = SlotIndex (0..N-1). SlotManager đọc list này để đặt gun đúng chỗ map định.
    /// </summary>
    public class Map : MonoBehaviour
    {
        [Serializable]
        public class SlotGunMovePath
        {
            [Tooltip("Các mốc world mà gun đi qua sau khi click slot, theo đúng thứ tự.")]
            public List<Transform> positions = new List<Transform>();
            [Min(0f)] public float cornerRadius = 1f;
            [Min(1)] public int curveSamples = 8;
            [Min(0.01f)] public float speedMultiplier = 1f;
            [Tooltip("Hệ số tốc độ từng đoạn A->B. Phần tử 0 là positions[0] -> positions[1]. Thiếu phần tử = 1x.")]
            public List<float> segmentSpeedMultipliers = new List<float>();
            [Header("After final position")]
            public bool moveRightAfterPath = true;
        }

        [Tooltip("Mỗi phần tử = mốc sinh GUN 0 của 1 slot (theo thứ tự slot 0..N-1). Đặt các Transform con " +
                 "vào đúng chỗ muốn slot mọc trên map.")]
        [SerializeField] private List<Transform> slotSpawns = new List<Transform>();
        [Header("Gun move to loop path")]
        [Tooltip("Mỗi phần tử ứng với một slot (0..N-1). Gun đi qua các positions rồi mới vào queue của path loop.")]
        [SerializeField] private List<SlotGunMovePath> slotMovePaths = new List<SlotGunMovePath>();
        [Tooltip("Pos End dùng chung: sau route riêng, mọi gun đi tới mốc này trước khi vào queue path loop.")]
        [SerializeField] private Transform rightMoveEndPosition;
        [Min(0.01f)] [SerializeField] private float rightMoveEndSpeedMultiplier = 1f;
        [Min(0.01f)] [SerializeField] private float gunMoveSpeed = 8f;
        [SerializeField] private bool rotateGunAlongMovePath = true;

        [Header("Vùng chờ vào path (vẽ trên map)")]
        [Tooltip("Mốc GIỮA cạnh GẦN cửa path — gun đầu hàng (vào path TRƯỚC) đứng quanh đây. Hướng từ mốc " +
                 "này tới 'Far' = chiều SÂU của vùng (càng xa cửa càng vào sau). Đặt trùng/sát đầu vào path.")]
        [SerializeField] private Transform waitAreaNear;
        [Tooltip("Mốc GIỮA cạnh XA của vùng chờ — xác định chiều sâu + hướng vùng.")]
        [SerializeField] private Transform waitAreaFar;
        [Tooltip("Bề RỘNG vùng chờ (vuông góc chiều sâu). 0 → hàng đơn 1 gun mỗi hàng.")]
        [Min(0f)] [SerializeField] private float waitAreaWidth = 2f;
        [Tooltip("Khoảng cách giữa 2 gun kề nhau trong vùng (cả theo sâu lẫn theo rộng).")]
        [Min(0.1f)] [SerializeField] private float waitSpacing = 1f;

        /// <summary>Map có định nghĩa vùng chờ không (đủ 2 mốc).</summary>
        public bool HasWaitArea => waitAreaNear != null && waitAreaFar != null;

        /// <summary>Số gun tối đa trên MỖI HÀNG ngang (theo bề rộng vùng).</summary>
        public int WaitPerRow => Mathf.Max(1, 1 + Mathf.FloorToInt(waitAreaWidth / Mathf.Max(0.01f, waitSpacing)));

        /// <summary>
        /// Vị trí chỗ đứng thứ <paramref name="index"/> trong vùng chờ. Lấp theo HÀNG: hàng 0 sát cạnh GẦN
        /// cửa (vào trước), đầy thì sang hàng lùi ra xa. Trong mỗi hàng lấp từ GIỮA toả 2 bên (0, +, −…) nên
        /// không dồn một phía "trái qua phải". Chỗ số 0 = giữa hàng gần cửa nhất = gun vào path trước nhất.
        /// </summary>
        public Vector3 WaitSlot(int index)
        {
            if (waitAreaNear == null) return Vector3.zero;
            Vector3 near = waitAreaNear.position;
            if (waitAreaFar == null) return near;

            Vector3 depth = waitAreaFar.position - near; depth.y = 0f;
            Vector3 depthDir = depth.sqrMagnitude > 1e-6f ? depth.normalized : Vector3.forward;
            Vector3 widthDir = Vector3.Cross(Vector3.up, depthDir); // vuông góc trên sàn XZ

            int perRow = WaitPerRow;
            int row = index / perRow;
            int col = index % perRow;

            // Cột center-out trong bề rộng.
            int k = (col + 1) / 2;
            float sign = (col % 2 == 1) ? 1f : -1f;
            float across = Mathf.Clamp(sign * k * waitSpacing, -waitAreaWidth * 0.5f, waitAreaWidth * 0.5f);

            return near + depthDir * (row * waitSpacing) + widthDir * across;
        }

        /// <summary>Số slot mà map này định vị trí (= số mốc trong list).</summary>
        public int SlotCount => slotSpawns != null ? slotSpawns.Count : 0;

        public IReadOnlyList<Transform> SlotSpawns => slotSpawns;

        /// <summary>Vị trí world sinh GUN 0 của slot index; false nếu index ngoài list / mốc null.</summary>
        public bool TryGetSlotPosition(int index, out Vector3 pos)
        {
            if (slotSpawns != null && index >= 0 && index < slotSpawns.Count && slotSpawns[index] != null)
            { pos = slotSpawns[index].position; return true; }
            pos = default;
            return false;
        }

        /// <summary>Di chuyển gun theo route riêng của slot. False = slot chưa có route, caller vào loop ngay.</summary>
        public bool TryMoveGunToLoop(int slotIndex, Gun gun, Action onComplete)
        {
            if (gun == null || slotMovePaths == null || slotIndex < 0 || slotIndex >= slotMovePaths.Count) return false;
            var route = slotMovePaths[slotIndex];
            if (route?.positions == null || route.positions.Count == 0) return false;

            gun.BeginMoveToLoopPath();
            StartCoroutine(MoveGunToLoopRoutine(gun, route, onComplete));
            return true;
        }

        private IEnumerator MoveGunToLoopRoutine(Gun gun, SlotGunMovePath route, Action onComplete)
        {
            var raw = new List<Vector3>();
            foreach (var marker in route.positions) if (marker != null) raw.Add(marker.position);
            var samples = raw.Count >= 2
                ? RoundedPolylinePath.BuildSamples(raw, false, route.cornerRadius, route.curveSamples, PathStyle.RoundedCorner)
                : raw.ToArray();
            for (int i = 0; i < samples.Length; i++)
            {
                if (gun == null) yield break;
                Vector3 start = gun.transform.position;
                Vector3 target = samples[i];
                float distance = Vector3.Distance(start, target);
                int segment = samples.Length > 1
                    ? Mathf.Min(route.segmentSpeedMultipliers != null ? route.segmentSpeedMultipliers.Count - 1 : -1,
                               Mathf.FloorToInt((float)i / (samples.Length - 1) * Mathf.Max(1, raw.Count - 1)))
                    : -1;
                float customMultiplier = segment >= 0 ? Mathf.Max(0.01f, route.segmentSpeedMultipliers[segment]) : 1f;
                float duration = distance / Mathf.Max(0.01f, gunMoveSpeed * route.speedMultiplier * customMultiplier);
                for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
                {
                    if (gun == null) yield break;
                    float t = Mathf.Clamp01(elapsed / duration);
                    // SmoothStep tạo ease-in ở đầu đoạn và ease-out ở cuối đoạn; khi hệ số
                    // speed giữa hai segment khác nhau, gun vẫn chuyển tốc độ không bị giật.
                    float easedT = t * t * (3f - 2f * t);
                    gun.transform.position = Vector3.Lerp(start, target, easedT);
                    if (rotateGunAlongMovePath)
                    {
                        Vector3 direction = target - start; direction.y = 0f;
                        if (direction.sqrMagnitude > 1e-6f)
                            gun.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                    }
                    yield return null;
                }
                gun.transform.position = target;
            }

            // Ra khỏi route ở điểm cuối: đi tới mốc Pos End của map trước khi vào queue path loop.
            if (gun != null && route.moveRightAfterPath && rightMoveEndPosition != null)
            {
                Vector3 start = gun.transform.position;
                Vector3 target = rightMoveEndPosition.position;
                float distance = Vector3.Distance(start, target);
                float duration = distance /
                                 Mathf.Max(0.01f, gunMoveSpeed * rightMoveEndSpeedMultiplier);
                for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
                {
                    if (gun == null) yield break;
                    float t = Mathf.Clamp01(elapsed / duration);
                    gun.transform.position = Vector3.Lerp(start, target, t);
                    if (rotateGunAlongMovePath)
                    {
                        Vector3 direction = target - start; direction.y = 0f;
                        if (direction.sqrMagnitude > 1e-6f)
                            gun.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                    }
                    yield return null;
                }
                gun.transform.position = target;

            }
            // Ẩn trong lúc chuyển từ Pos End tới điểm 0 của path loop; PathManager sẽ bật lại
            // đúng lúc bắt đầu staging/deploy.
            gun?.SetHiddenDuringPathEntry(true);
            onComplete?.Invoke();
        }

        private void OnDrawGizmosSelected()
        {
            if (slotMovePaths == null) return;
            for (int slot = 0; slot < slotMovePaths.Count; slot++)
            {
                var route = slotMovePaths[slot];
                if (route?.positions == null || route.positions.Count == 0) continue;
                var raw = new List<Vector3>();
                Gizmos.color = Color.Lerp(Color.cyan, Color.yellow, slot / Mathf.Max(1f, slotMovePaths.Count - 1f));
                foreach (var marker in route.positions)
                    if (marker != null) { raw.Add(marker.position); Gizmos.DrawWireSphere(marker.position, 0.12f); }
                if (raw.Count < 2) continue;
                var samples = RoundedPolylinePath.BuildSamples(raw, false, route.cornerRadius, route.curveSamples, PathStyle.RoundedCorner);
                for (int i = 1; i < samples.Length; i++) Gizmos.DrawLine(samples[i - 1], samples[i]);
                if (route.moveRightAfterPath && rightMoveEndPosition != null)
                {
                    Gizmos.DrawCube(rightMoveEndPosition.position, Vector3.one * 0.16f);
                    Gizmos.DrawLine(samples[samples.Length - 1], rightMoveEndPosition.position);
                }
            }
        }

        // Vẽ VÙNG CHỜ vào path — LUÔN hiện để căn trên scene. Xanh lá: khung vùng (cạnh gần cửa dày hơn) +
        // ô vuông từng chỗ đứng. Ô sát cạnh gần cửa = gun vào path trước nhất.
        private void OnDrawGizmos()
        {
            if (waitAreaNear == null || waitAreaFar == null) return;

            Vector3 near = waitAreaNear.position;
            Vector3 depth = waitAreaFar.position - near; depth.y = 0f;
            Vector3 depthDir = depth.sqrMagnitude > 1e-6f ? depth.normalized : Vector3.forward;
            Vector3 widthDir = Vector3.Cross(Vector3.up, depthDir);
            float half = waitAreaWidth * 0.5f;
            Vector3 far = waitAreaFar.position;

            // Khung vùng.
            Gizmos.color = new Color(0.2f, 1f, 0.55f, 0.9f);
            Vector3 nL = near - widthDir * half, nR = near + widthDir * half;
            Vector3 fL = far - widthDir * half, fR = far + widthDir * half;
            Gizmos.DrawLine(nL, nR); // cạnh gần cửa
            Gizmos.DrawLine(fL, fR);
            Gizmos.DrawLine(nL, fL);
            Gizmos.DrawLine(nR, fR);
            // Nhấn cạnh gần cửa (hàng vào trước) bằng đường thứ 2.
            Gizmos.DrawLine(nL + depthDir * 0.06f, nR + depthDir * 0.06f);

            // Chỗ đứng: rải đủ vài hàng để thấy layout (giới hạn để khỏi vẽ vô hạn).
            int perRow = WaitPerRow;
            int rows = depth.magnitude > 1e-3f ? Mathf.Max(1, Mathf.FloorToInt(depth.magnitude / Mathf.Max(0.01f, waitSpacing)) + 1) : 1;
            int total = Mathf.Min(perRow * rows, 40);
            Gizmos.color = new Color(0.2f, 1f, 0.55f, 0.55f);
            for (int i = 0; i < total; i++)
                Gizmos.DrawWireCube(WaitSlot(i), Vector3.one * 0.22f);
        }
    }
}

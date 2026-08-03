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
        }

        [Tooltip("Mỗi phần tử = mốc sinh GUN 0 của 1 slot (theo thứ tự slot 0..N-1). Đặt các Transform con " +
                 "vào đúng chỗ muốn slot mọc trên map.")]
        [SerializeField] private List<Transform> slotSpawns = new List<Transform>();
        [Header("Gun move to loop path")]
        [Tooltip("Mỗi phần tử ứng với một slot (0..N-1). Gun đi qua các positions rồi mới vào queue của path loop.")]
        [SerializeField] private List<SlotGunMovePath> slotMovePaths = new List<SlotGunMovePath>();
        [Min(0.01f)] [SerializeField] private float gunMoveSpeed = 8f;
        [Tooltip("Hệ số tốc độ khi điểm tiếp theo thấp hơn theo trục Y (đi xuống dốc).")]
        [Min(1f)] [SerializeField] private float downhillSpeedMultiplier = 1.5f;
        [SerializeField] private bool rotateGunAlongMovePath = true;

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
            StartCoroutine(MoveGunToLoopRoutine(gun, route.positions, onComplete));
            return true;
        }

        private IEnumerator MoveGunToLoopRoutine(Gun gun, List<Transform> positions, Action onComplete)
        {
            for (int i = 0; i < positions.Count; i++)
            {
                if (gun == null) yield break;
                var marker = positions[i];
                if (marker == null) continue;
                Vector3 start = gun.transform.position;
                Vector3 target = marker.position;
                float distance = Vector3.Distance(start, target);
                float speedMultiplier = target.y < start.y - 0.001f ? downhillSpeedMultiplier : 1f;
                float duration = distance / Mathf.Max(0.01f, gunMoveSpeed * speedMultiplier);
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
            onComplete?.Invoke();
        }
    }
}

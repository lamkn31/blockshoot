using System.Collections.Generic;
using UnityEngine;

namespace Wayfu.Lamkn
{
    /// <summary>
    /// 1 hàng gun — ĐẶT SẴN TRÊN SCENE (Slot0..4). Vị trí lấy từ transform; gun trong slot xếp theo
    /// index, cách nhau theo TRỤC Z với spacing DÙNG CHUNG (GameSettings). Level chỉ điền danh sách gun;
    /// số slot có gun = số slot active. Gun đầu (FrontGun) mới deploy được; gun đầu đi thì gun sau dồn lên.
    /// </summary>
    public class GunSlot : MonoBehaviour
    {
        [Tooltip("Chỉ số slot 0..4 — quyết định thứ tự active theo số lượng slot của level.")]
        public int SlotIndex;
        [SerializeField] private float shiftDuration = 0.15f;

        private readonly List<Gun> _guns = new List<Gun>();
        private float _spacing = 1f;

        public Gun FrontGun => _guns.Count > 0 ? _guns[0] : null;
        public int Count => _guns.Count;
        public IReadOnlyList<Gun> Guns => _guns;

        /// <summary>Vị trí của gun theo index — index 0 ở PHÍA TRƯỚC (gần path, +Z), index sau lùi về −Z.</summary>
        private Vector3 SlotPos(int index)
        {
            // Slot0 co the co khoang dau rieng; cac khoang con lai van dung spacing chung.
            if (index > 0)
            {
                float firstSpacing = GameSettings.Instance != null
                    ? GameSettings.Instance.FirstGunSpacing
                    : 0f;
                if (firstSpacing > 0f)
                    return transform.position - Vector3.forward * (firstSpacing + _spacing * (index - 1));
            }

            return transform.position - Vector3.forward * _spacing * index;
        }

        /// <summary>Đặt vị trí slot (dùng cho fallback khi scene chưa có slot).</summary>
        public void SetPosition(Vector3 pos) => transform.position = pos;

        /// <summary>Nạp gun cho slot (lấy từ pool); spacing dùng chung truyền vào.</summary>
        public void Fill(List<GunData> guns, float spacing, GunFireConfig fire)
        {
            _spacing = spacing;
            Clear();
            if (guns == null) return;
            for (int i = 0; i < guns.Count; i++)
            {
                if (guns[i] == null) continue;
                var g = PoolManager.Instance.GetGun();
                g.transform.SetParent(transform);
                g.transform.position = SlotPos(i);
                g.Init(guns[i], fire);
                g.SetSlot(this);
                _guns.Add(g);
            }
            RefreshReveal();
        }

        // Gun ở index 0 = ở VỊ TRÍ ĐẦU → gun ẩn lộ màu; các gun sau vẫn ẩn. Gọi mỗi khi list gun đổi.
        private void RefreshReveal()
        {
            for (int i = 0; i < _guns.Count; i++)
                if (_guns[i] != null) _guns[i].SetAtFront(i == 0);
        }

        public Gun RemoveFront()
        {
            if (_guns.Count == 0) return null;
            var front = _guns[0];
            _guns.RemoveAt(0);
            for (int i = 0; i < _guns.Count; i++)
                _guns[i].MoveTo(SlotPos(i), shiftDuration); // dồn gun sau lên
            RefreshReveal(); // gun mới lên đầu (index 0) → lộ màu nếu là gun ẩn
            return front;
        }

        // Gun được trả về pool qua PoolManager.ReturnAll khi rebuild — ở đây chỉ xoá list.
        public void Clear() => _guns.Clear();

        private void OnDrawGizmos()
        {
            float sp = _spacing > 0f ? _spacing : GameSettings.SlotSpacing;
            Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, 0.3f);
            Gizmos.DrawLine(transform.position, transform.position - Vector3.forward * sp); // hướng queue lùi (−Z)
        }
    }
}

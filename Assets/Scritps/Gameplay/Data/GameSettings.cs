using UnityEngine;

namespace Wayfu.Lamkn
{
    /// <summary>
    /// Cấu hình DÙNG CHUNG cho toàn game (không theo level). Đặt asset trong folder Resources tên
    /// "GameSettings" để runtime tự load; Level Tool tự tìm qua AssetDatabase.
    /// </summary>
    [CreateAssetMenu(fileName = "GameSettings", menuName = "Wayfu/Game Settings")]
    public class GameSettings : ScriptableObject
    {
        [Header("Core")]
        [Tooltip("NearestCell = luôn bắn cell gần gun nhất (kể cả cell vừa sập xuống ở gần).\n" +
                 "FrontRowFirst = nếu còn cell chưa bắn ở HÀNG 0 (sát path) trong range thì ưu tiên nó hơn " +
                 "cell đã sập xuống, dù cell sập ở gần hơn.")]
        public CoreGameType CoreType = CoreGameType.NearestCell;

        [Header("Slot")]
        [Tooltip("Spacing DÙNG CHUNG giữa các gun trong slot (theo trục Z).")]
        public float SlotGunSpacing = 1f;
        [Tooltip("Spacing rieng tu gun index 0 den gun index 1 trong Slot0. 0 = dung SlotGunSpacing.")]
        public float FirstGunSpacing = 0f;
        [Min(0f)] public float GunGoInDistanceBeforeEnd = 0f;
        [Min(0f)] public float GunPathGoInDistanceBeforeEnd = 0f;

        [Header("Guns / Path (config chung, không theo level)")]
        [Tooltip("Độ rộng mặt đường path — DÙNG CHUNG mọi level (trước ở LevelData, giờ gom về đây).")]
        [Min(0f)] public float PathWidth = 1.5f;
        [Min(1)] public int MaxGunOnPath = 5;
        public float GunSpeed = 3f;
        [Tooltip("Khoảng cách (arc-length) giữa các station gun trên path.")]
        public float GunSpacing = 1.2f;
        [Tooltip("Khoảng cách giữa các gun trong ĐÁM ĐÔNG CHỜ cạnh cửa path (càng lớn gun đứng càng xa nhau, " +
                 "càng nhỏ càng chen chúc). 0 = dùng mặc định của PathManager.")]
        [Min(0f)] public float WaitClusterSpacing = 1.5f;
        public float FireInterval = 0.25f;
        [Tooltip("Single = mỗi nhịp FireInterval nhả 1 viên, gặm dần cho tới khi cell sạch.\n" +
                 "BurstPerCell = chốt cell xong nhả LUÔN cả loạt đúng bằng số block còn lại của cell, mỗi " +
                 "viên bay tới 1 block trong stack → cả cell vỡ trong 1 lượt. FireInterval khi đó chỉ là " +
                 "nhịp nghỉ giữa 2 CELL, không phải giữa 2 viên.\n" +
                 "Laser = KHÔNG bắn viên; vẽ tia liền mạch từ nòng tới cell, gặm 1 block mỗi FireInterval, " +
                 "cell vỡ xong tia nối liền sang cell kế (nhìn không ngắt quãng).")]
        public GunFireMode FireMode = GunFireMode.Single;
        [Tooltip("CHỈ dùng khi FireMode = BurstPerCell.\n" +
                 "Tắt = cả loạt sinh CHỤM tại 1 điểm ở nòng rồi toả ra từng block.\n" +
                 "Bật = mỗi viên sinh sẵn ở đúng ĐỘ CAO của block nó nhắm (spacing trục Y lấy luôn của " +
                 "stack cell đó) → cả loạt xếp thành cột ngay tại nòng rồi bay NGANG sang, không chéo.")]
        public bool BurstSpawnStacked;
        [Tooltip("CHỈ dùng khi BurstSpawnStacked bật. Mỗi hàng đạn tính từ DƯỚI lên được đẩy sẵn về phía " +
                 "target thêm ngần này (world units): hàng đáy 0, hàng 2 = x, hàng 3 = 2x…\n" +
                 "Hàng càng cao càng gần đích → tới nơi TRƯỚC, nhìn rõ stack vỡ dần từ trên xuống (khớp " +
                 "thứ tự block bị gỡ). 0 = cả loạt xuất phát cùng vạch, tới gần như cùng lúc.\n" +
                 "BulletSpeed càng cao thì càng phải để lớn mới thấy: 100 đơn vị/giây thì lệch 1 đơn vị " +
                 "chỉ ra 10ms.")]
        [Min(0f)] public float BurstRowLead;
        [Tooltip("Bán kính vùng CHỌN target của mỗi nòng. Chỉ lọc lúc CHỌN, không phải điều kiện bắn: " +
                 "chốt được cell rồi thì nòng bắn dứt điểm hết stack, kể cả khi gun đã trôi qua và cell " +
                 "đã ra ngoài tầm.")]
        public float GunFireRange = 3f;
        [Tooltip("Góc TOẢ của mỗi nòng (độ), tính TỪ hướng trước mặt của gun (thân luôn bám path, không " +
                 "quay mặt về target) rồi mở sang sườn của nòng đó: nòng phải toả sang phải, nòng trái " +
                 "sang trái — 2 quạt chung mép ở trục trước mặt.\n" +
                 "Ví dụ 60 = mỗi nòng quét từ 0° tới 60° về phía mình. 180 = 2 quạt phủ kín vòng tròn " +
                 "(mỗi nòng gác trọn 1 nửa mặt phẳng). Trên 180 không thêm được gì, sẽ bị kẹp về 180.\n" +
                 "Như GunFireRange: chỉ lọc lúc CHỌN target, không gate phát bắn.")]
        [Range(1f, 180f)] public float GunFireAngle = 60f;
        [Tooltip("Arc-length của station trước nhất trên path.")]
        public float FrontStationDistance = 0f;
        [Tooltip("Tốc độ bay của bullet.")]
        public float BulletSpeed = 14f;

        [Header("Block")]
        [Tooltip("Khoảng cách giữa các block trong 1 stack (theo trục Y).")]
        public float BlockStackSpacing = 0.5f;
        [Tooltip("Tốc độ dồn hàng: thời gian (giây) cell trượt từ ô cũ sang ô mới. NHỎ = dồn NHANH.")]
        [Min(0f)] public float BlockCollapseDuration = 0.25f;

        private static GameSettings _instance;

        public static GameSettings Instance
        {
            get
            {
                if (_instance != null) return _instance;
                _instance = Resources.Load<GameSettings>("GameSettings");
#if UNITY_EDITOR
                if (_instance == null)
                {
                    var guids = UnityEditor.AssetDatabase.FindAssets("t:GameSettings");
                    if (guids.Length > 0)
                        _instance = UnityEditor.AssetDatabase.LoadAssetAtPath<GameSettings>(
                            UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
                }
#endif
                return _instance;
            }
        }

        /// <summary>Spacing gun-trong-slot dùng chung (fallback 1f nếu chưa có asset).</summary>
        public static float SlotSpacing => Instance != null ? Instance.SlotGunSpacing : 1f;
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Wayfu.Lamkn
{
    /// <summary>
    /// Đọc LevelData → dựng path, grid block, slot và khởi động GameController.
    /// Level lấy từ <see cref="LevelList"/> theo INDEX (UserProgressSO.currentLevelIndex); gán
    /// <see cref="levelOverride"/> để ghim 1 level cố định khi test.
    /// Tự tạo các manager nếu scene chưa có, nên chỉ cần đặt 1 LevelController là chạy.
    /// </summary>
    public class LevelController : Singleton<LevelController>
    {
        [Header("Nguồn level")]
        [Tooltip("Danh sách level theo thứ tự chơi. Bỏ trống → tự load asset 'LevelList' trong Resources.")]
        [SerializeField] private LevelList levelList;
        [Tooltip("GHI ĐÈ để test: gán 1 level thì bỏ qua Level List + index. Bỏ trống = chơi theo index " +
                 "của UserProgressSO.currentLevelIndex.")]
        [FormerlySerializedAs("levelData")] // giữ tham chiếu đã gán sẵn trên scene khi đổi tên field
        [SerializeField] private LevelData levelOverride;

        private LevelData _level;

        /// <summary>Level ĐANG dựng (đã giải ra từ Level List hoặc override).</summary>
        public LevelData Level => _level;

        /// <summary>Index của level đang chơi trong Level List.</summary>
        public int CurrentIndex { get; private set; }

        private IEnumerator Start()
        {
            // Chờ toàn bộ object có sẵn trong scene chạy Start xong trước khi dựng level.
            // Một số component trên prefab obstacle phụ thuộc manager/scene bootstrap;
            // build ngay trong Start khiến lần vào đầu chưa init, còn Retry thì lại đúng.
            yield return null;

#if UNITY_EDITOR
            var forced = ConsumeToolLevel();
            if (forced != null)
            {
                Debug.Log($"[LevelController] Level Tool ghim level: {forced.name}");
                PlayLevelNow(forced);
                yield break;
            }
#endif
            LoadCurrent();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Level Tool bấm ▶: gửi level cần chơi qua SessionState rồi vào Play mode. Phải dùng
        /// SessionState chứ không phải static — vào Play mode là domain reload, static bị xoá sạch.
        /// </summary>
        public const string PlayLevelKey = "Wayfu.LevelTool.PlayLevelGuid";

        // Đọc XONG là xoá: lần bấm Play sau (Ctrl+P bình thường) phải quay về chơi theo tiến trình,
        // không thì level bị ghim dính luôn.
        private static LevelData ConsumeToolLevel()
        {
            string guid = UnityEditor.SessionState.GetString(PlayLevelKey, "");
            if (string.IsNullOrEmpty(guid)) return null;
            UnityEditor.SessionState.EraseString(PlayLevelKey);
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : UnityEditor.AssetDatabase.LoadAssetAtPath<LevelData>(path);
        }
#endif

        /// <summary>Nạp level theo tiến trình đã lưu (UserProgressSO.currentLevelIndex).</summary>
        public void LoadCurrent() => LoadLevel(ProgressIndex);

        /// <summary>
        /// Nạp + dựng level theo index trong Level List. Index vượt quá cuối danh sách sẽ LẶP
        /// (xem <see cref="LevelList.Get"/>), nên không cần tự chặn biên.
        /// </summary>
        public void LoadLevel(int index)
        {
            CurrentIndex = Mathf.Max(0, index);
            _level = Resolve(CurrentIndex);
            if (_level == null)
            {
                Debug.LogError($"[LevelController] Không lấy được level index {CurrentIndex} — chưa gán " +
                               "Level List (hoặc chưa có asset 'LevelList' trong Resources) và cũng chưa gán Level Override.");
                return;
            }
            Build();
        }

        /// <summary>
        /// Chơi ĐÚNG level này ngay, BỎ QUA cả <see cref="levelOverride"/> lẫn tiến trình đã lưu
        /// (Level Tool bấm ▶). Không đi qua Resolve(): ở đó override luôn thắng, nên bấm play level nào
        /// cũng ra level override.
        /// </summary>
        public void PlayLevelNow(LevelData data)
        {
            if (data == null) return;
            _level = data;
            var l = levelList != null ? levelList : LevelList.Instance;
            CurrentIndex = l != null ? Mathf.Max(0, l.IndexOf(data)) : 0;
            Build();
        }

        /// <summary>Qua level kế: tăng tiến trình đã lưu rồi nạp level mới. Chưa có ai gọi — nối vào nút Next của WinPopup.</summary>
        public void Next()
        {
            Progress?.AdvanceLevel(); // tự Save()
            LoadCurrent();
        }

        /// <summary>Ghim 1 level cụ thể mà không đụng tiến trình (tool/debug). Gọi Build() để dựng.</summary>
        public void SetLevel(LevelData data) => _level = data;

        private LevelData Resolve(int index)
        {
            if (levelOverride != null) return levelOverride; // override thắng: giữ đúng hành vi scene cũ
            var list = levelList != null ? levelList : LevelList.Instance;
            return list != null ? list.Get(index) : null;
        }

        // Không dùng UserDataController.Instance: Singleton.Instance log error khi scene chưa có nó.
        // Thiếu UserDataController → coi như index 0, không phải lỗi.
        private static UserProgressSO Progress =>
            UserDataController.IsActive ? UserDataController.Instance.UserProgress : null;

        private static int ProgressIndex
        {
            get { var p = Progress; return p != null ? Mathf.Max(0, p.currentLevelIndex) : 0; }
        }

        public void Build()
        {
            if (_level == null)
            {
                Debug.LogError("[LevelController] Chưa có LevelData để dựng");
                return;
            }

            EnsureManagers();
            PoolManager.Instance.Init(_level); // set prefab + trả hết item cũ về pool
            ClearAll();

            PathManager.Instance.Build(_level); // tự dựng RoundedPolylinePath + mặt đường
            GridBlockManager.Instance.Build(_level);
            MapController.Instance?.Build(_level); // spawn map theo SlotCount → tạo các GunSlot cho SlotManager
            SlotManager.Instance.Build(_level);
            SpawnBoardProps(_level);
            SpawnObstacles(_level);
            IceController.Instance?.Build(_level); // tự sinh Ice phủ các vùng cell băng (theo IceThreshold)
            GameController.Instance.StartLevel(); // bàn chơi xong → GameController vào Playing + dựng HUD
        }

        public void Retry() => Build();

        private Transform _propsRoot;
        private Transform _obstaclesRoot;

        private void ClearAll()
        {
            PathManager.Instance?.Clear();
            GridBlockManager.Instance?.Clear();
            if (MapController.IsActive) MapController.Instance.Clear();
            SlotManager.Instance?.Clear();
            if (_propsRoot != null) Destroy(_propsRoot.gameObject);
            if (_obstaclesRoot != null) Destroy(_obstaclesRoot.gameObject);
            if (IceController.IsActive) IceController.Instance.Clear();
        }

        private void SpawnBoardProps(LevelData level)
        {
            if (level.BoardProps == null || level.BoardProps.Count == 0) return;
            _propsRoot = new GameObject("BoardProps").transform;
            foreach (var p in level.BoardProps)
            {
                if (p?.PropPrefab == null) continue;
                var rot = p.PropRot.Equals(default(Quaternion)) ? Quaternion.identity : p.PropRot;
                var scale = p.PropScale == Vector3.zero ? Vector3.one : p.PropScale;
                var go = Instantiate(p.PropPrefab, p.PropPos, rot, _propsRoot);
                go.transform.localScale = scale;
            }
        }

        // Obstacle: instantiate model tại Pos, xoay quanh Y theo RotationY, và NHÂN scale model với Scale
        // của obstacle (Scale = 1 → giữ đúng kích thước gốc, khớp footprint vẽ trong Level Tool).
        private void SpawnObstacles(LevelData level)
        {
            if (level.Obstacles == null || level.Obstacles.Count == 0) return;
            _obstaclesRoot = new GameObject("Obstacles").transform;
            foreach (var o in level.Obstacles)
            {
                if (o?.Prefab == null) continue;
                var go = Instantiate(o.Prefab, o.Pos, Quaternion.Euler(0f, o.RotationY, 0f), _obstaclesRoot);
                Vector3 s = o.Scale == Vector3.zero ? Vector3.one : o.Scale;
                go.transform.localScale = Vector3.Scale(go.transform.localScale, s);
                // Đăng ký collider để gun không bắn xuyên obstacle (LOS trong GridBlockManager.FindTargetCell).
                GridBlockManager.Instance?.RegisterObstacle(go);
            }
        }

        private void EnsureManagers()
        {
            Ensure<PoolManager>();
            Ensure<GameController>();
            Ensure<GridBlockManager>();
            Ensure<MapController>();
            Ensure<IceController>();
            Ensure<SlotManager>();
            Ensure<PathManager>();
        }

        private void Ensure<T>() where T : MonoBehaviour
        {
            if (FindObjectOfType<T>() == null)
                new GameObject(typeof(T).Name).AddComponent<T>();
        }

    }
}

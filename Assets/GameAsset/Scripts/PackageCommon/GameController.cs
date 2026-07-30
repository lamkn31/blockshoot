using System;
using BusGame.Gameplay;
using UnityEngine;

namespace Wayfu.Lamkn
{
    /// <summary>
    /// Máy trạng thái gameplay: theo dõi WIN (phá hết block) / LOSE (path đầy gun & không gun nào
    /// bắn được) — yêu cầu #3 — và dựng UI theo trạng thái đó.
    /// <para>Vào màn → popup GamePlay. WIN → popup Win, nút Next sang màn kế + tăng tiến trình đã lưu.
    /// LOSE → popup Lose, nút Retry dựng lại đúng màn đó. Vẫn phát OnWin/OnLose cho chỗ khác lắng nghe
    /// (fx, sound, analytics…).</para>
    /// </summary>
    public class GameController : Singleton<GameController>
    {
        public enum GameState { None, Playing, Win, Lose }

        [Header("Win")]
        [Tooltip("Số coin thưởng hiện trên popup Win.")]
        [SerializeField] private int winReward = 100;

        public GameState State { get; private set; } = GameState.None;

        public event Action OnWin;
        public event Action OnLose;

        private int _blocksAtStart; // mốc để tính % hoàn thành hiện trên popup Lose

        // Spine cảnh báo độ khó đang chờ loading đóng mới được diễn.
        private LevelDifficulty _pendingNotify;
        private bool _waitingLoading;

        /// <summary>
        /// <see cref="LevelController.Build"/> gọi ở CUỐI, sau khi bàn chơi đã dựng xong — nên đây cũng
        /// là chỗ dựng lại HUD cho mỗi lần vào màn / retry / next, khỏi cần event riêng.
        /// </summary>
        public void StartLevel()
        {
            State = GameState.Playing;
            // Chốt tổng block NGAY sau khi dựng: lúc này RemainingBlocks đang là 100%.
            _blocksAtStart = GridBlockManager.Instance != null ? GridBlockManager.Instance.RemainingBlocks : 0;
            ShowGamePlayHud();
            Popup?.SetBlockProgress(0, _blocksAtStart); // thanh tiến trình phá block về 0/total
        }

        /// <summary>Gọi sau mỗi thay đổi bàn chơi (deploy gun / bắn / cột bị phá).</summary>
        public void OnBoardChanged()
        {
            if (State != GameState.Playing) return;
            // Tổng block đã phá trong màn → tan băng (cell + obstacle băng) khi đạt ngưỡng.
            int left = GridBlockManager.Instance != null ? GridBlockManager.Instance.RemainingBlocks : 0;
            int destroyed = Mathf.Max(0, _blocksAtStart - left);
            GridBlockManager.Instance?.UpdateIce(destroyed);   // tan trạng thái băng của cell (cho bắn được)
            IceController.Instance?.UpdateIce(destroyed);       // countdown + xoá Ice hình khi đủ ngưỡng
            Popup?.SetBlockProgress(destroyed, _blocksAtStart); // cập nhật thanh tiến trình phá block
            if (CheckWin()) return;
            CheckLose();
        }

        /// <summary>
        /// Nhóm connect KHÔNG deploy được (vượt sức chứa path) VÀ gun trên path không bắn được cell nào →
        /// bế tắc → THUA. Gọi từ SlotManager khi người chơi bấm nhóm connect mà không đủ chỗ.
        /// </summary>
        public void NotifyConnectStuck()
        {
            if (State != GameState.Playing) return;
            var pm = PathManager.Instance;
            if (pm != null && pm.GunCount > 0 && !pm.AnyGunHasTarget()) Lose();
        }

        /// <summary>Chơi lại ĐÚNG màn hiện tại, không đụng tiến trình đã lưu.</summary>
        public void Retry() => Level?.Retry();

        /// <summary>Sang màn kế: tăng tiến trình đã lưu rồi dựng màn mới.</summary>
        public void Next() => Level?.Next();

        private bool CheckWin()
        {
            if (GridBlockManager.Instance != null && GridBlockManager.Instance.AllCleared)
            {
                Win();
                return true;
            }
            return false;
        }

        private void CheckLose()
        {
            var pm = PathManager.Instance;
            if (pm == null) return;
            if (!pm.IsFull) return;            // chỉ xét khi path đã đầy gun
            if (pm.AnyGunHasTarget()) return;  // còn gun bắn được → chưa thua
            Lose();
        }

        private void Win()
        {
            State = GameState.Win;
            OnWin?.Invoke();
            Popup?.ShowWin($"LEVEL {DisplayLevel}", winReward, 0, Next);
        }

        private void Lose()
        {
            State = GameState.Lose;
            OnLose?.Invoke();

            // % block đã phá, để popup Lose cho thấy còn thiếu bao nhiêu.
            int left = GridBlockManager.Instance != null ? GridBlockManager.Instance.RemainingBlocks : 0;
            float done = Mathf.Clamp01(_blocksAtStart > 0 ? 1f - (float)left / _blocksAtStart : 0f);
            string title = $"LEVEL {DisplayLevel}";

            // Diễn bảng LÝ DO THUA ("Out of moves!") trên HUD rồi mới bật popup Lose. Không có HUD thì
            // ShowReasonLose gọi thẳng onComplete.
            var pc = Popup;
            if (pc != null) pc.ShowReasonLose("Out of moves!", () => pc.ShowLose(title, Retry, null, done));
        }

        private void ShowGamePlayHud()
        {
            var pc = Popup;
            if (pc == null) return;

            // Retry/Next bấm TỪ popup → popup đó vẫn đang mở, phải tự dọn không nó che luôn màn mới.
            pc.HideWin();
            pc.HideLose();

            int maxGun = GameSettings.Instance != null ? GameSettings.Instance.MaxGunOnPath : 5;

            // PHẢI đổi 5 mức GameDifficulty → 3 mức LevelDifficulty: popup chỉ có 3 slot, nhét thẳng
            // VeryHard=3 / Expert=4 vào là nó tắt sạch cả icon Setting (xem GameDifficultyExt).
            var diff = Level != null && Level.Level != null
                ? Level.Level.CurGameDifficulty.ToLevelDifficulty()
                : LevelDifficulty.Easy;

            // Bộ đếm của popup vốn là "path" bên game gốc; ở đây map sang gun trên path — đầy thanh đúng
            // bằng điều kiện LOSE (xem CheckLose) nên thanh đỏ mang nghĩa thật, không phải trang trí.
            pc.ShowGamePlay(DisplayLevel,
                            () => PathManager.Instance != null ? PathManager.Instance.GunCount : 0,
                            maxGun, (int)diff, Retry);

            NotifyDifficultyWhenVisible(diff);
        }

        /// <summary>
        /// Diễn spine cảnh báo Hard / Very Hard, nhưng CHỜ loading đóng xong mới diễn.
        /// <para>Vào game lần đầu, LevelController.Build() (→ StartLevel) chạy ngay khi scene GamePlay
        /// vừa load, lúc đó overlay loading vẫn đang che kín. Diễn luôn ở đó là spine chạy hết 3 giây
        /// dưới lớp overlay, loading tắt xong thì cũng vừa hết — người chơi không thấy gì.</para>
        /// <para>Retry/Next thì loading đã đóng từ lâu → diễn ngay, không phải chờ.</para>
        /// </summary>
        private void NotifyDifficultyWhenVisible(LevelDifficulty diff)
        {
            if (diff == LevelDifficulty.Easy) return; // popup tự bỏ qua, nhưng khỏi hook thừa

            if (GameplayReadiness.IsLoadingComplete) { Popup?.ShowDifficultyNotification(diff); return; }

            _pendingNotify = diff;
            if (_waitingLoading) return; // đã hook rồi, chỉ cập nhật độ khó đang chờ
            _waitingLoading = true;
            GameplayReadiness.OnLoadingComplete += OnLoadingComplete;
        }

        private void OnLoadingComplete()
        {
            GameplayReadiness.OnLoadingComplete -= OnLoadingComplete;
            _waitingLoading = false;
            Popup?.ShowDifficultyNotification(_pendingNotify);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            // OnLoadingComplete là event STATIC → không gỡ là giữ tham chiếu tới instance đã chết,
            // lần chơi sau bắn vào object hỏng.
            if (_waitingLoading) GameplayReadiness.OnLoadingComplete -= OnLoadingComplete;
        }

        /// <summary>Index nội bộ đếm từ 0, người chơi thì đếm từ 1.</summary>
        private int DisplayLevel => (Level != null ? Level.CurrentIndex : 0) + 1;

        // Không dùng thẳng .Instance: Singleton.Instance log error khi scene chưa có object đó.
        private static LevelController Level => LevelController.IsActive ? LevelController.Instance : null;
        private static PopupController Popup => PopupController.IsActive ? PopupController.Instance : null;
    }
}

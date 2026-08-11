using System;
using System.Collections.Generic;
using System.Text;
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

        [Header("Feature Unlock (meta trên WinPopup như SmashFest)")]
        [Tooltip("Gán FeatureUnlockConfig.asset. Để trống thì WinPopup chỉ hiện meta mặc định (không có feature).")]
        [SerializeField] private FeatureUnlockSO featureConfig;

        public GameState State { get; private set; } = GameState.None;

        public event Action OnWin;
        public event Action OnLose;

        /// <summary>Bắn ra mỗi khi một màn vừa dựng xong (first load / Retry / Next) — lúc này slot đã điền
        /// gun, bàn chơi sẵn sàng. TutorialController nghe để bắt đầu tutorial theo level.</summary>
        public event Action LevelLoaded;

        private int _blocksAtStart; // mốc để tính % hoàn thành hiện trên popup Lose

        // Spine cảnh báo độ khó đang chờ loading đóng mới được diễn.
        private LevelDifficulty _pendingNotify;
        private bool _loseCheckPending;
        private bool _waitingLoading;
        private bool _reportedOutOfGunBalance;
        private bool _reportedEndgameBalance;
        private bool _reportedEndgameDeadlock;
        private bool _runtimeBalanceWasOk = true;

        /// <summary>
        /// Prints the real runtime balance per color. A color is balanced when
        /// bullets still held by live guns + bullets already in flight equals
        /// all live/queued blocks of that color.
        /// </summary>
        public bool LogRuntimeColorBalance(string reason, UnityEngine.Object context = null, bool emit = true)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var gunCount = new Dictionary<TypeColor, int>();
            var bullets = new Dictionary<TypeColor, int>();
            // Read only guns owned by the current runtime managers. A scene scan
            // either misses temporarily inactive tunnel guns or includes stale
            // inactive objects retained by the pool from a previous run.
            var runtimeGuns = new HashSet<Gun>();
            if (SlotManager.IsActive) SlotManager.Instance.CollectRuntimeGuns(runtimeGuns);
            if (PathManager.IsActive) PathManager.Instance.CollectRuntimeGuns(runtimeGuns);
            foreach (var gun in runtimeGuns)
            {
                if (gun == null || gun.IsDead || gun.Data == null) continue;
                var color = gun.Data.Color;
                gunCount.TryGetValue(color, out int gc);
                gunCount[color] = gc + 1;
                bullets.TryGetValue(color, out int bc);
                bullets[color] = bc + Mathf.Max(0, gun.Data.CountBullet);
            }

            var grid = GridBlockManager.Instance;
            var blocks = grid != null ? grid.RemainingBlocksByColor() : new Dictionary<TypeColor, int>();
            var flying = grid != null ? grid.PendingHitsByColor() : new Dictionary<TypeColor, int>();
            var colors = new HashSet<TypeColor>(gunCount.Keys);
            colors.UnionWith(bullets.Keys);
            colors.UnionWith(blocks.Keys);
            colors.UnionWith(flying.Keys);
            var ordered = new List<TypeColor>(colors);
            ordered.Sort((a, b) => ((int)a).CompareTo((int)b));

            bool allMatch = true;
            var sb = new StringBuilder($"[RuntimeBalance] {reason}");
            foreach (var color in ordered)
            {
                gunCount.TryGetValue(color, out int guns);
                bullets.TryGetValue(color, out int held);
                flying.TryGetValue(color, out int inFlight);
                blocks.TryGetValue(color, out int remaining);
                bool match = held + inFlight == remaining;
                allMatch &= match;
                // Keep the complete snapshot on ONE console line so Unity's log
                // list shows the numbers without requiring the entry to be selected.
                sb.Append($" | {color}: guns={guns}, bullets={held}, inFlight={inFlight}, " +
                          $"blocks={remaining}, balance={held + inFlight} {(match ? "OK" : "MISMATCH")}");
            }
            sb.Append($" | overall={(allMatch ? "OK" : "MISMATCH")}, slotsEmpty=" +
                      $"{(SlotManager.IsActive && SlotManager.Instance.AreAllSlotsEmpty)}");
            if (emit)
            {
                if (allMatch) Debug.Log(sb.ToString(), context);
                else Debug.LogWarning(sb.ToString(), context);
            }
            return allMatch;
#else
            return true;
#endif
        }

        /// <summary>
        /// <see cref="LevelController.Build"/> gọi ở CUỐI, sau khi bàn chơi đã dựng xong — nên đây cũng
        /// là chỗ dựng lại HUD cho mỗi lần vào màn / retry / next, khỏi cần event riêng.
        /// </summary>
        public void StartLevel()
        {
            State = GameState.Playing;
            _reportedOutOfGunBalance = false;
            _reportedEndgameBalance = false;
            _reportedEndgameDeadlock = false;
            // Chốt tổng block NGAY sau khi dựng: lúc này RemainingBlocks đang là 100%.
            _blocksAtStart = GridBlockManager.Instance != null ? GridBlockManager.Instance.RemainingBlocks : 0;
            ShowGamePlayHud();
            Popup?.SetBlockProgress(0, _blocksAtStart); // thanh tiến trình phá block về 0/total
            LevelLoaded?.Invoke(); // bàn chơi + slot đã sẵn sàng → Tutorial có thể bắt đầu
            _runtimeBalanceWasOk = LogRuntimeColorBalance("Level started", this);
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            bool balanceNow = LogRuntimeColorBalance("Board changed", this, emit: false);
            if (_runtimeBalanceWasOk && !balanceNow)
                LogRuntimeColorBalance("FIRST OK -> MISMATCH transition", this);
            _runtimeBalanceWasOk = balanceNow;
#endif
            if (!_reportedEndgameBalance && SlotManager.IsActive && SlotManager.Instance.AreAllSlotsEmpty)
            {
                _reportedEndgameBalance = true;
                LogRuntimeColorBalance("Entered endgame (all slots empty)", this);
            }
            if (CheckWin()) return;
            RequestLoseCheck();
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

        // Gun targeting runs in Update. A board change can happen before that
        // Update, so check loss in LateUpdate after every gun has scanned.
        private void RequestLoseCheck() => _loseCheckPending = true;

        private void LateUpdate()
        {
            if (!_loseCheckPending || State != GameState.Playing) return;
            _loseCheckPending = false;
            CheckLose();
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
            var grid = GridBlockManager.Instance;
            bool slotsEmpty = SlotManager.IsActive && SlotManager.Instance.AreAllSlotsEmpty;
            bool anyTarget = pm.AnyGunHasTarget();
            if (!_reportedEndgameDeadlock && slotsEmpty && pm.GunCount > 0 && !anyTarget)
            {
                _reportedEndgameDeadlock = true;
                LogRuntimeColorBalance("Endgame has no matching gun target", this);
            }
            if (!_reportedOutOfGunBalance && pm.GunCount == 0
                && slotsEmpty
                && grid != null && grid.RemainingBlocks > 0
                && grid.PendingHitCount == 0)
            {
                _reportedOutOfGunBalance = true;
                Debug.LogWarning($"[Balance] No guns remain but board still has {grid.RemainingBlocks} blocks: " +
                                 grid.RemainingBlocksByColorReport());
            }
            if (!pm.IsFull) return;            // chỉ xét khi path đã đầy gun
            if (anyTarget) return;             // còn gun bắn được → chưa thua
            Lose();
        }

        private void Win()
        {
            State = GameState.Win;
            OnWin?.Invoke();
            // Cấu hình meta feature-unlock trên WinPopup theo level VỪA thắng rồi mới show (như SmashFest).
            ShowWinWithFeature($"LEVEL {DisplayLevel}", DisplayLevel);
        }

        // Đảm bảo WinPopup đã tạo → cấu hình meta feature theo level vừa thắng → show (flow port từ SmashFest).
        private async void ShowWinWithFeature(string title, int beatenLevel)
        {
            var pc = Popup;
            if (pc == null) return;
            WinPopup winPopup = await pc.EnsureWinAsync();
            if (winPopup != null) ConfigureWinPopupFeature(winPopup, beatenLevel);
            pc.ShowWin(title, winReward, 0, Next);
        }

        // Cấu hình meta feature-unlock trên WinPopup theo level hiện tại (port từ SmashFest.GameController).
        public void ConfigureWinPopupFeature(WinPopup popup, int currentLevel)
        {
            if (popup == null) return;

            FeatureUnlockSO cfg = featureConfig;
            if (cfg == null)
            {
                popup.SetMetaMode(WinPopup.VictoryMetaMode.None);
                popup.SetFeatureInfo(null, null, null);
                return;
            }

            FeatureUnlockEntry feature = cfg.GetFeatureForLevel(currentLevel);
            if (feature == null)
            {
                popup.SetMetaMode(WinPopup.VictoryMetaMode.None);
                popup.SetFeatureInfo(null, null, null);
                return;
            }

            // Nếu prevLevel thuộc feature khác (hoặc chưa có) thì slider start từ 0,
            // tránh nhảy giật khi vừa chuyển sang feature mới.
            int prevLevel = currentLevel - 1;
            FeatureUnlockEntry prevFeature = cfg.GetFeatureForLevel(prevLevel);
            float fromP = prevFeature == feature ? cfg.GetProgressFor(prevFeature, prevLevel) : 0f;
            float toP = cfg.GetProgressFor(feature, currentLevel);

            if (currentLevel == feature.unlockLevel)
            {
                // Đạt unlock level: animate tới 100% rồi swap sang ShowMeta.
                popup.SetProgressThenShow(fromP, toP);
            }
            else
            {
                popup.SetProgress(fromP, toP);
                popup.SetMetaMode(WinPopup.VictoryMetaMode.Progress);
            }
            popup.SetFeatureInfo(feature.icon, feature.titleImage, feature.title, feature.description);
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
        public int DisplayLevel => (Level != null ? Level.CurrentIndex : 0) + 1;

        // Không dùng thẳng .Instance: Singleton.Instance log error khi scene chưa có object đó.
        private static LevelController Level => LevelController.IsActive ? LevelController.Instance : null;
        private static PopupController Popup => PopupController.IsActive ? PopupController.Instance : null;
    }
}

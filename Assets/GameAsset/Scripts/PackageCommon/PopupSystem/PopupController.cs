using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Wayfu.Lamkn
{
    public class PopupController : Singleton<PopupController>
    {
        #region Inspector

        [Header("Popup Root")]
        [SerializeField] private Transform popupRoot;

        [Header("Settings")]
        [Tooltip("Prefab hiệu ứng khi người chơi chạm vào màn hình")]
        public GameObject tapEffectPrefab;
        [Tooltip("Số instance pre-warm cho pool tap effect")]
        [SerializeField] private int tapEffectPoolSize = 5;

        [Header("Addressable Popup References")]
        [SerializeField] private AssetReferenceGameObject loadingPopupRef;
        [SerializeField] private AssetReferenceGameObject settingPopupRef;
        [SerializeField] private AssetReferenceGameObject winPopupRef;
        [SerializeField] private AssetReferenceGameObject losePopupRef;
        [SerializeField] private AssetReferenceGameObject tutorialPopupRef;
        [SerializeField] private AssetReferenceGameObject gamePlayPopupRef;
        [SerializeField] private AssetReferenceGameObject difficultyNotificationRef;

        [Header("Preload (optional)")]
        [Tooltip("Pre-instantiate frequently-used popups at startup so first Show() is instant.")]
        [SerializeField] private bool preloadLoading = true;
        [SerializeField] private bool preloadSetting = false;
        [SerializeField] private bool preloadWin = false;
        [SerializeField] private bool preloadLose = false;
        [SerializeField] private bool preloadTutorial = false;
        [SerializeField] private bool preloadGamePlay = false;
        [SerializeField] private bool preloadDifficultyNotification = true;

        #endregion

        #region State

        private LoadingPopup _loading;
        private SettingPopup _setting;
        private WinPopup _win;
        private LosePopup _lose;
        private TutorialPopup _tutorial;
        private GamePlayPopup _gamePlay;
        private LevelDifficultyNotificationUI _difficultyNotification;

        // Track loading handles so we can release them on destroy.
        private readonly Dictionary<AssetReferenceGameObject, AsyncOperationHandle<GameObject>> _handles
            = new Dictionary<AssetReferenceGameObject, AsyncOperationHandle<GameObject>>();

        // Track in-flight loads to avoid duplicate Instantiate when 2 calls come at once.
        private readonly Dictionary<AssetReferenceGameObject, Task<GameObject>> _pendingLoads
            = new Dictionary<AssetReferenceGameObject, Task<GameObject>>();

        // Pool cho tap effect.
        private readonly Stack<GameObject> _tapEffectPool = new Stack<GameObject>();

        #endregion

        #region Unity

        protected override void OnAwake()
        {
            if (popupRoot == null) popupRoot = transform;
            // PopupController is typically a child of a Canvas root. Singleton.Awake only flags THIS
            // GameObject as DontDestroyOnLoad — Unity ignores that on children, so we promote the root
            // so the whole Canvas tree (including pre-placed loading popup) survives scene swaps.
            var root = transform.root.gameObject;
            if (root != gameObject) DontDestroyOnLoad(root);
            _ = PreloadAsync();
            WarmTapEffectPool();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            ReleaseAll();
            ClearTapEffectPool();
        }

        #endregion

        #region Accessors

        public LoadingPopup Loading => _loading;
        public SettingPopup Setting => _setting;
        public WinPopup Win => _win;
        public LosePopup Lose => _lose;
        public TutorialPopup Tutorial => _tutorial;
        public GamePlayPopup GamePlay => _gamePlay;
        public LevelDifficultyNotificationUI DifficultyNotification => _difficultyNotification;

        /// <summary>True while gameplay input (car taps + HUD buttons like Settings) must be
        /// suppressed. Currently raised while the difficulty notification ("noticehard") is on
        /// screen so the player can't tap a car or open Settings behind it.</summary>
        public bool BlockGameInput =>
            (_difficultyNotification != null && _difficultyNotification.IsShowing) ||
            (_loading != null && _loading.IsShowing) ||
            (_setting != null && _setting.IsShowing) ||
            (_win != null && _win.IsShowing) ||
            (_lose != null && _lose.IsShowing) ||
            (_gamePlay != null && _gamePlay.IsReasonLoseShowing);

        #endregion

        #region Preload

        private async Task PreloadAsync()
        {
            if (preloadLoading) await GetOrCreateAsync<LoadingPopup>(loadingPopupRef, p => _loading = p);
            if (preloadSetting) await GetOrCreateAsync<SettingPopup>(settingPopupRef, p => _setting = p);
            if (preloadWin) await GetOrCreateAsync<WinPopup>(winPopupRef, p => _win = p);
            if (preloadLose) await GetOrCreateAsync<LosePopup>(losePopupRef, p => _lose = p);
            if (preloadTutorial) await GetOrCreateAsync<TutorialPopup>(tutorialPopupRef, p => _tutorial = p);
            if (preloadGamePlay) await GetOrCreateAsync<GamePlayPopup>(gamePlayPopupRef, p => _gamePlay = p);
            if (preloadDifficultyNotification) await GetOrCreateAsync<LevelDifficultyNotificationUI>(difficultyNotificationRef, p => _difficultyNotification = p);
        }

        #endregion

        #region Async Loaders

        private async Task<T> GetOrCreateAsync<T>(AssetReferenceGameObject reference, Action<T> assign)
            where T : BasePopup
        {
            if (reference == null || !reference.RuntimeKeyIsValid())
            {
                Debug.LogError($"[PopupController] AssetReference for {typeof(T).Name} is not set.");
                return null;
            }

            // De-dupe concurrent loads for the same reference.
            if (_pendingLoads.TryGetValue(reference, out var pending))
            {
                var existingGo = await pending;
                return existingGo != null ? existingGo.GetComponent<T>() : null;
            }

            var handle = reference.InstantiateAsync(popupRoot, false);
            _handles[reference] = handle;

            var tcs = new TaskCompletionSource<GameObject>();
            _pendingLoads[reference] = tcs.Task;

            try
            {
                await handle.Task;

                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                {
                    Debug.LogError($"[PopupController] Failed to load {typeof(T).Name} from Addressables.");
                    tcs.SetResult(null);
                    return null;
                }

                var go = handle.Result;
                go.transform.SetParent(popupRoot, false);

                var popup = go.GetComponent<T>();
                if (popup == null)
                {
                    Debug.LogError($"[PopupController] Loaded prefab has no {typeof(T).Name} component.");
                    tcs.SetResult(null);
                    return null;
                }

                assign?.Invoke(popup);
                tcs.SetResult(go);
                return popup;
            }
            finally
            {
                _pendingLoads.Remove(reference);
            }
        }

        public async Task<LoadingPopup> EnsureLoadingAsync()
        {
            if (_loading != null) return _loading;
            return await GetOrCreateAsync<LoadingPopup>(loadingPopupRef, p => _loading = p);
        }

        public async Task<SettingPopup> EnsureSettingAsync()
        {
            if (_setting != null) return _setting;
            return await GetOrCreateAsync<SettingPopup>(settingPopupRef, p => _setting = p);
        }

        public async Task<WinPopup> EnsureWinAsync()
        {
            if (_win != null) return _win;
            return await GetOrCreateAsync<WinPopup>(winPopupRef, p => _win = p);
        }

        public async Task<LosePopup> EnsureLoseAsync()
        {
            if (_lose != null) return _lose;
            return await GetOrCreateAsync<LosePopup>(losePopupRef, p => _lose = p);
        }

        public async Task<TutorialPopup> EnsureTutorialAsync()
        {
            if (_tutorial != null) return _tutorial;
            return await GetOrCreateAsync<TutorialPopup>(tutorialPopupRef, p => _tutorial = p);
        }

        public async Task<GamePlayPopup> EnsureGamePlayAsync()
        {
            if (_gamePlay != null) return _gamePlay;
            return await GetOrCreateAsync<GamePlayPopup>(gamePlayPopupRef, p => _gamePlay = p);
        }

        public async Task<LevelDifficultyNotificationUI> EnsureDifficultyNotificationAsync()
        {
            if (_difficultyNotification != null) return _difficultyNotification;
            return await GetOrCreateAsync<LevelDifficultyNotificationUI>(difficultyNotificationRef, p => _difficultyNotification = p);
        }

        #endregion

        #region Loading API

        public async void ShowLoadingInstant(string label = "Loading", string tip = null)
        {
            var p = await EnsureLoadingAsync();
            if (p != null) p.ShowFadeInstant(label, tip);
        }

        public async void ShowLoadingFade(string label = "Loading", string tip = null)
        {
            var p = await EnsureLoadingAsync();
            if (p != null) p.ShowFade(label, tip);
        }

        public async void ShowLoadingProgress(string label = "Loading", string tip = null)
        {
            var p = await EnsureLoadingAsync();
            if (p != null) p.ShowProgress(label, tip);
        }

        /// <summary>
        /// Show loading + run fake progress bar that hides once <paramref name="isReady"/> returns true.
        /// Timing (fake duration, finish ramp) lives inside <see cref="LoadingPopup"/> — callers stay agnostic.
        /// </summary>
        public async void ShowLoadingUntilReady(System.Func<bool> isReady, System.Action onComplete = null, string label = "Loading", string tip = null)
        {
            var p = await EnsureLoadingAsync();
            if (p != null) p.RunUntilReady(isReady, onComplete, label, tip);
        }

        /// <summary>
        /// Như <see cref="ShowLoadingUntilReady"/> nhưng KHÔNG có thanh progress (chỉ overlay "Loading...").
        /// Dùng cho retry/next — thanh loading chỉ hiện ở lần init đầu vào game.
        /// </summary>
        public async void ShowLoadingFadeUntilReady(System.Func<bool> isReady, System.Action onComplete = null, string label = "Loading", string tip = null)
        {
            var p = await EnsureLoadingAsync();
            if (p != null) p.RunFadeUntilReady(isReady, onComplete, label, tip);
        }

        public void SetLoadingProgress(float value01)
        {
            if (_loading != null) _loading.SetProgress(value01);
        }

        public void HideLoading()
        {
            if (_loading != null) _loading.Hide();
        }

        #endregion

        #region Setting API

        public async void ShowSetting()
        {
            var p = await EnsureSettingAsync();
            if (p != null) p.Show();
        }

        public async void ShowSetting(Action onRetry)
        {
            var p = await EnsureSettingAsync();
            if (p != null) p.Show(onRetry);
        }

        public void HideSetting()
        {
            if (_setting != null) _setting.Hide();
        }

        #endregion

        #region Win / Lose API

        [Button]
        public async void ShowWin(string title, int reward, int totalCoinBefore = 0,
            Action onNext = null, Action onHome = null)
        {
            var p = await EnsureWinAsync();
            if (p != null) p.Show(title, reward, totalCoinBefore, onNext, onHome);
        }

        public void HideWin()
        {
            if (_win != null) _win.Hide();
        }

        public async void ShowLose(string title, Action onRetry = null, Action onHome = null, float progress01 = 0f)
        {
            var p = await EnsureLoseAsync();
            if (p != null) p.Show(title, onRetry, onHome, progress01);
        }

        public void HideLose()
        {
            if (_lose != null) _lose.Hide();
        }

        #endregion

        #region Tutorial API

        public async void StartTutorial(List<TutorialStep> steps, Action onDone = null)
        {
            var p = await EnsureTutorialAsync();
            if (p != null) p.StartTutorial(steps, onDone);
        }

        public void NotifyTutorialActionDone()
        {
            if (_tutorial != null) _tutorial.NotifyActionDone();
        }

        public void StopTutorial()
        {
            if (_tutorial != null) _tutorial.Stop();
        }

        #endregion

        #region GamePlay API

        public async void ShowGamePlay()
        {
            var p = await EnsureGamePlayAsync();
            if (p != null) p.Show();
        }

        public async void ShowGamePlay(int level, Func<int> getCurrentPathCount, int maxPath, int difficulty = 0, Action onRetry = null)
        {
            var p = await EnsureGamePlayAsync();
            if (p != null) p.Show(level, getCurrentPathCount, maxPath, difficulty, onRetry);
        }

        public void HideGamePlay()
        {
            if (_gamePlay != null) _gamePlay.Hide();
        }

        /// <summary>Đẩy tiến trình "block đã phá / tổng" lên HUD gameplay (nếu đang có).</summary>
        public void SetBlockProgress(int destroyed, int total)
        {
            if (_gamePlay != null) _gamePlay.SetBlockProgress(destroyed, total);
        }

        /// <summary>Diễn bảng LÝ DO THUA trên HUD rồi gọi onComplete (thường là ShowLose). Không có HUD
        /// thì gọi thẳng onComplete.</summary>
        public void ShowReasonLose(string reason, Action onComplete)
        {
            if (_gamePlay != null) _gamePlay.ShowReasonLose(reason, onComplete);
            else onComplete?.Invoke();
        }

        #endregion

        #region Difficulty Notification API

        public async void ShowDifficultyNotification(LevelDifficulty difficulty)
        {
            var p = await EnsureDifficultyNotificationAsync();
            if (p != null) p.Show(difficulty);
        }

        public void HideDifficultyNotification()
        {
            if (_difficultyNotification != null) _difficultyNotification.Hide();
        }

        #endregion

        #region Tap Effect

        private void WarmTapEffectPool()
        {
            if (tapEffectPrefab == null) return;
            for (int i = 0; i < tapEffectPoolSize; i++)
                _tapEffectPool.Push(CreatePooledEffect());
        }

        private GameObject CreatePooledEffect()
        {
            var go = Instantiate(tapEffectPrefab, popupRoot);

            // Cấu hình unscaled time một lần khi tạo, không cần lặp lại mỗi lần dùng.
            var ps = go.GetComponent<ParticleSystem>();
            if (ps != null) { var m = ps.main; m.useUnscaledTime = true; }

            var anim = go.GetComponent<Animator>();
            if (anim != null) anim.updateMode = AnimatorUpdateMode.UnscaledTime;

            go.SetActive(false);
            return go;
        }

        private GameObject GetFromPool()
        {
            if (_tapEffectPool.Count > 0)
            {
                var go = _tapEffectPool.Pop();
                if (go != null) return go;
            }
            // Pool trống hoặc object đã bị destroy — tạo thêm.
            return CreatePooledEffect();
        }

        private void ReturnToPool(GameObject effect)
        {
            if (effect == null) return;
            effect.SetActive(false);
            _tapEffectPool.Push(effect);
        }

        private void ClearTapEffectPool()
        {
            while (_tapEffectPool.Count > 0)
            {
                var go = _tapEffectPool.Pop();
                if (go != null) Destroy(go);
            }
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
                SpawnTapEffect(Input.mousePosition);
        }

        private void SpawnTapEffect(Vector3 screenPos)
        {
            if (tapEffectPrefab == null) return;

            var effect = GetFromPool();
            effect.transform.SetParent(popupRoot, false);
            effect.transform.position = screenPos;
            effect.transform.localScale = Vector3.one;
            effect.SetActive(true);
            effect.transform.SetAsLastSibling();

            // Replay particle nếu có.
            var ps = effect.GetComponent<ParticleSystem>();
            if (ps != null) { ps.Clear(); ps.Play(); }

            SoundController.Instance?.PlayTapSound();

            StartCoroutine(ReturnToPoolRoutine(effect, 1.0f));
        }

        private IEnumerator ReturnToPoolRoutine(GameObject effect, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            ReturnToPool(effect);
        }

        #endregion

        #region Release

        private void ReleaseAll()
        {
            foreach (var kvp in _handles)
            {
                if (kvp.Value.IsValid())
                {
                    // ReleaseInstance also destroys the spawned GameObject.
                    Addressables.ReleaseInstance(kvp.Value);
                }
            }
            _handles.Clear();
            _pendingLoads.Clear();

            _loading = null;
            _setting = null;
            _win = null;
            _lose = null;
            _tutorial = null;
            _gamePlay = null;
            _difficultyNotification = null;
        }

        #endregion
    }
}

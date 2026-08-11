using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Wayfu.Lamkn
{
    public class SoundController : Singleton<SoundController>
    {
        #region Constants

        private const string PREF_BGM = "Sound_BGM";
        private const string PREF_SFX = "Sound_SFX";

        #endregion

        #region Inspector

        [Header("Sources")]
        [SerializeField] private AudioSource bgmSource;
        [SerializeField] private AudioSource sfxSource;

        [Header("Audio Database")]
        [Tooltip("Database AudioType -> clips. Bỏ trống nếu chỉ dùng Default Clips bên dưới.")]
        [SerializeField] private AudioData audioData;

        [Header("SFX Limits")]
        [Min(1)][Tooltip("Số SFX được phát đồng thời tối đa. Vượt quá thì SFX mới bị bỏ qua.")]
        [SerializeField] private int maxConcurrentSfx = 10;

        [Min(0f)][Tooltip("Khoảng cách tối thiểu (giây) giữa hai SFX. SFX đến sớm hơn khoảng này bị bỏ qua.")]
        [SerializeField] private float minSfxInterval = 0.1f;

        [Header("Default Clips")]
        [SerializeField] private AudioClip defaultBgm;
        [SerializeField] private AudioClip winClip;
        [SerializeField] private AudioClip loseClip;
        [SerializeField] private AudioClip buttonClip;
        [SerializeField] private AudioClip tapClip;
        [SerializeField] private AudioClip alightClip;
        [SerializeField] private AudioClip boardClip;
        [SerializeField] private AudioClip carCompletedClip;
        [SerializeField] private AudioClip touchClip;
        [SerializeField] private AudioClip collideClip;
        [SerializeField] private AudioClip cannonShootClip;

        [Header("Volumes")]
        [Range(0f, 1f)][SerializeField] private float bgmVolume = 0.8f;
        [Range(0f, 1f)][SerializeField] private float sfxVolume = 1f;

        [Header("Board SFX")]
        [Min(1)][Tooltip("Số tiếng 'người nhảy lên xe' được phát đồng thời tối đa.")]
        [SerializeField] private int maxConcurrentBoardSfx = 10;
        private int _boardSfxPlaying;

        #endregion

        #region State

        public bool IsBgmEnabled { get; private set; }
        public bool IsSfxEnabled { get; private set; }

        private Dictionary<AudioType, List<AudioClip>> _clipMap;

        // Số SFX đang phát và thời điểm SFX gần nhất, dùng cho giới hạn chung ở CanPlaySfx.
        private int _sfxPlaying;
        private float _lastSfxTime = float.NegativeInfinity;

        #endregion

        #region Unity

        protected override void OnAwake()
        {
            IsBgmEnabled = PlayerPrefs.GetInt(PREF_BGM, 1) == 1;
            IsSfxEnabled = PlayerPrefs.GetInt(PREF_SFX, 1) == 1;

            if (bgmSource != null) bgmSource.volume = bgmVolume;
            if (sfxSource != null) sfxSource.volume = sfxVolume;

            CacheClipMap();
            ApplyBgmState();
        }

        private void CacheClipMap()
        {
            _clipMap = new Dictionary<AudioType, List<AudioClip>>();
            if (audioData == null || audioData.entries == null) return;

            foreach (AudioData.Entry entry in audioData.entries)
            {
                if (entry.type == AudioType.None || entry.clips == null || entry.clips.Count == 0) continue;
                _clipMap[entry.type] = entry.clips;
            }
        }

        #endregion

        #region BGM API

        public void PlayDefaultBGM()
        {
            PlayBgm(defaultBgm);
        }

        /// <summary>Tắt BGM, phát nhạc chiến thắng qua kênh SFX (một lần, không loop).</summary>
        public void PlayWinSound()
        {
            StopBgm();
            if (IsSfxEnabled && sfxSource != null && winClip != null)
                sfxSource.PlayOneShot(winClip);
        }

        /// <summary>Tắt BGM, phát âm thanh thua qua kênh SFX (một lần, không loop).</summary>
        public void PlayLoseSound()
        {
            StopBgm();
            if (IsSfxEnabled && sfxSource != null && loseClip != null)
                sfxSource.PlayOneShot(loseClip);
        }

        public void PlayBgm(AudioClip clip = null, bool loop = true)
        {
            if (bgmSource == null) return;
            AudioClip target = clip != null ? clip : defaultBgm;
            if (target == null) return;

            bgmSource.clip = target;
            bgmSource.loop = loop;
            if (IsBgmEnabled) bgmSource.Play();
        }

        public void StopBgm()
        {
            if (bgmSource != null) bgmSource.Stop();
        }

        public void ToggleBgm()
        {
            IsBgmEnabled = !IsBgmEnabled;
            PlayerPrefs.SetInt(PREF_BGM, IsBgmEnabled ? 1 : 0);
            PlayerPrefs.Save();
            ApplyBgmState();
        }

        public void SetBgmEnabled(bool enabled)
        {
            if (IsBgmEnabled == enabled) return;
            ToggleBgm();
        }

        private void ApplyBgmState()
        {
            if (bgmSource == null) return;
            bgmSource.mute = !IsBgmEnabled;
            if (IsBgmEnabled && bgmSource.clip != null && !bgmSource.isPlaying) bgmSource.Play();
        }

        #endregion

        #region SFX API

        public void PlaySfx(AudioClip clip, float volumeScale = 1f)
        {
            if (!IsSfxEnabled || sfxSource == null || clip == null) return;
            if (!CanPlaySfx()) return;
            PlayOneShotTracked(clip, volumeScale);
        }

        /// <summary>Phát SFX theo AudioType, lấy clip từ AudioData (random nếu type có nhiều clip).</summary>
        public void PlaySfx(AudioType type, float volumeScale = 1f)
        {
            if (!IsSfxEnabled || sfxSource == null || type == AudioType.None) return;
            if (!CanPlaySfx()) return;

            AudioClip clip = GetRandomClip(type);
            if (clip == null) return;
            PlayOneShotTracked(clip, volumeScale);
        }

        /// <summary>Phát BGM theo AudioType (vd MenuMusic, GameplayMusic).</summary>
        public void PlayBgm(AudioType type, bool loop = true)
        {
            AudioClip clip = GetRandomClip(type);
            if (clip != null) PlayBgm(clip, loop);
        }

        public AudioClip GetRandomClip(AudioType type)
        {
            if (_clipMap == null) CacheClipMap();
            if (!_clipMap.TryGetValue(type, out List<AudioClip> clips) || clips.Count == 0) return null;
            return clips.Count == 1 ? clips[0] : clips[Random.Range(0, clips.Count)];
        }

        // Giới hạn CHUNG cho mọi SFX: tối đa maxConcurrentSfx tiếng cùng lúc và hai tiếng
        // phải cách nhau ít nhất minSfxInterval giây. Chặn spam khi nhiều object cùng va chạm.
        private bool CanPlaySfx()
        {
            if (_sfxPlaying >= maxConcurrentSfx) return false;
            if (minSfxInterval > 0f && Time.unscaledTime - _lastSfxTime < minSfxInterval) return false;
            return true;
        }

        private void PlayOneShotTracked(AudioClip clip, float volumeScale)
        {
            _lastSfxTime = Time.unscaledTime;
            _sfxPlaying++;
            sfxSource.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
            StartCoroutine(ReleaseSfxAfter(clip.length));
        }

        private IEnumerator ReleaseSfxAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (_sfxPlaying > 0) _sfxPlaying--;
        }

        public void PlayButtonClick()
        {
            PlaySfx(buttonClip);
        }

        public void PlayTapSound()
        {
            PlaySfx(tapClip);
        }

        // Tiếng bắn là phản hồi trực tiếp cho thao tác của người chơi -> KHÔNG đi qua CanPlaySfx:
        // không bị SFX khác chiếm chỗ (maxConcurrentSfx) hay chặn vì quá gần nhau (minSfxInterval),
        // và cũng không tính vào bộ đếm chung để khỏi làm nghẹt các SFX còn lại.
        public void PlayCannonShootSound(float volumeScale = 1f)
        {
            if (!IsSfxEnabled || sfxSource == null || cannonShootClip == null) return;
            sfxSource.PlayOneShot(cannonShootClip, Mathf.Clamp01(volumeScale));
        }

        public void PlayAlightSound()
        {
            PlaySfx(alightClip);
        }

        // Tiếng người nhảy lên xe: giới hạn số tiếng phát cùng lúc (tránh chồng quá nhiều khi đông khách).
        public void PlayBoardSound()
        {
            if (!IsSfxEnabled || sfxSource == null || boardClip == null) return;
            if (_boardSfxPlaying >= maxConcurrentBoardSfx) return;
            _boardSfxPlaying++;
            sfxSource.PlayOneShot(boardClip);
            StartCoroutine(ReleaseBoardSfxAfter(boardClip.length));
        }

        private IEnumerator ReleaseBoardSfxAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (_boardSfxPlaying > 0) _boardSfxPlaying--;
        }

        public void PlayCarCompletedSound()
        {
            PlaySfx(carCompletedClip);
        }

        public void PlayTouchSound()
        {
            PlaySfx(touchClip);
        }

        public void PlayCollideSound()
        {
            PlaySfx(collideClip);
        }

        /// <summary>Âm thanh block bị đạn phá. Dùng collideClip đang được gán trên prefab SoundController.</summary>
        public void PlayBlockDestroyedSound()
        {
            PlaySfx(collideClip);
        }

        public void ToggleSfx()
        {
            IsSfxEnabled = !IsSfxEnabled;
            PlayerPrefs.SetInt(PREF_SFX, IsSfxEnabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void SetSfxEnabled(bool enabled)
        {
            if (IsSfxEnabled == enabled) return;
            ToggleSfx();
        }

        #endregion
    }
}

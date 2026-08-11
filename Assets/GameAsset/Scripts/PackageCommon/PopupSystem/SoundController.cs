using System.Collections;
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

        #endregion

        #region Unity

        protected override void OnAwake()
        {
            IsBgmEnabled = PlayerPrefs.GetInt(PREF_BGM, 1) == 1;
            IsSfxEnabled = PlayerPrefs.GetInt(PREF_SFX, 1) == 1;

            if (bgmSource != null) bgmSource.volume = bgmVolume;
            if (sfxSource != null) sfxSource.volume = sfxVolume;

            ApplyBgmState();
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
            sfxSource.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
        }

        public void PlayButtonClick()
        {
            PlaySfx(buttonClip);
        }

        public void PlayTapSound()
        {
            PlaySfx(tapClip);
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

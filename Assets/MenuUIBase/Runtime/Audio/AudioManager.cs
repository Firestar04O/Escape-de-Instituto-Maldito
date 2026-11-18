using System;
using System.Collections;
using MenuUIBase.Core;
using UnityEngine;

namespace MenuUIBase.Audio
{
    [DisallowMultipleComponent]
    public sealed class AudioManager : PersistentService<AudioManager>, IAudioService
    {
        private const string MasterVolumeKey = "MenuUIBase.Audio.Master";
        private const string MusicVolumeKey = "MenuUIBase.Audio.Music";
        private const string SoundEffectsVolumeKey = "MenuUIBase.Audio.SoundEffects";

        [Header("Optional preconfigured sources")]
        [SerializeField] private AudioSource musicSource;
        [SerializeField] private AudioSource soundEffectsSource;

        private Coroutine musicTransition;
        private float musicFadeMultiplier = 1f;
        private float currentMusicScale = 1f;
        private float masterVolume;
        private float musicVolume;
        private float soundEffectsVolume;

        public event Action VolumesChanged;

        public float MasterVolume => masterVolume;
        public float MusicVolume => musicVolume;
        public float SoundEffectsVolume => soundEffectsVolume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureServiceExists()
        {
            _ = Instance;
        }

        protected override void OnServiceReady()
        {
            EnsureSources();
            masterVolume = PlayerPrefs.GetFloat(MasterVolumeKey, 1f);
            musicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, 0.8f);
            soundEffectsVolume = PlayerPrefs.GetFloat(SoundEffectsVolumeKey, 1f);
            ApplyVolumes();
        }

        public void Play(AudioCue cue)
        {
            if (cue == null || cue.Clip == null)
                return;

            if (cue.Channel == AudioChannel.Music)
                PlayMusic(cue.Clip, cue.Loop, cue.FadeDuration, cue.Volume);
            else
                PlaySoundEffect(cue.Clip, cue.Volume);
        }

        public void PlayMusic(
            AudioClip clip,
            bool loop = true,
            float fadeDuration = 0.35f,
            float volumeScale = 1f)
        {
            if (clip == null)
                return;

            EnsureSources();
            currentMusicScale = Mathf.Clamp01(volumeScale);

            if (musicSource.clip == clip && musicSource.isPlaying)
            {
                musicSource.loop = loop;
                musicFadeMultiplier = 1f;
                ApplyVolumes();
                return;
            }

            if (musicTransition != null)
                StopCoroutine(musicTransition);

            musicTransition = StartCoroutine(SwitchMusic(clip, loop, Mathf.Max(0f, fadeDuration)));
        }

        public void StopMusic(float fadeDuration = 0.35f)
        {
            EnsureSources();

            if (musicTransition != null)
                StopCoroutine(musicTransition);

            musicTransition = StartCoroutine(FadeOutAndStop(Mathf.Max(0f, fadeDuration)));
        }

        public void PlaySoundEffect(AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null)
                return;

            EnsureSources();
            soundEffectsSource.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
        }

        public void SetMasterVolume(float value)
        {
            masterVolume = SaveVolume(MasterVolumeKey, value);
            ApplyVolumesAndNotify();
        }

        public void SetMusicVolume(float value)
        {
            musicVolume = SaveVolume(MusicVolumeKey, value);
            ApplyVolumesAndNotify();
        }

        public void SetSoundEffectsVolume(float value)
        {
            soundEffectsVolume = SaveVolume(SoundEffectsVolumeKey, value);
            ApplyVolumesAndNotify();
        }

        private void EnsureSources()
        {
            if (musicSource == null)
            {
                musicSource = gameObject.AddComponent<AudioSource>();
                musicSource.playOnAwake = false;
                musicSource.ignoreListenerPause = true;
            }

            if (soundEffectsSource == null)
            {
                soundEffectsSource = gameObject.AddComponent<AudioSource>();
                soundEffectsSource.playOnAwake = false;
            }
        }

        private IEnumerator SwitchMusic(AudioClip clip, bool loop, float duration)
        {
            if (musicSource.isPlaying)
                yield return Fade(1f, 0f, duration * 0.5f);

            musicSource.Stop();
            musicSource.clip = clip;
            musicSource.loop = loop;
            musicFadeMultiplier = duration > 0f ? 0f : 1f;
            ApplyVolumes();
            musicSource.Play();

            if (duration > 0f)
                yield return Fade(0f, 1f, duration * 0.5f);

            musicFadeMultiplier = 1f;
            ApplyVolumes();
            musicTransition = null;
        }

        private IEnumerator FadeOutAndStop(float duration)
        {
            if (musicSource.isPlaying && duration > 0f)
                yield return Fade(musicFadeMultiplier, 0f, duration);

            musicSource.Stop();
            musicSource.clip = null;
            musicFadeMultiplier = 1f;
            ApplyVolumes();
            musicTransition = null;
        }

        private IEnumerator Fade(float from, float to, float duration)
        {
            if (duration <= 0f)
            {
                musicFadeMultiplier = to;
                ApplyVolumes();
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                musicFadeMultiplier = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
                ApplyVolumes();
                yield return null;
            }
        }

        private float SaveVolume(string key, float value)
        {
            float normalized = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(key, normalized);
            PlayerPrefs.Save();
            return normalized;
        }

        private void ApplyVolumesAndNotify()
        {
            ApplyVolumes();
            VolumesChanged?.Invoke();
        }

        private void ApplyVolumes()
        {
            if (musicSource != null)
                musicSource.volume = masterVolume * musicVolume * currentMusicScale * musicFadeMultiplier;

            if (soundEffectsSource != null)
                soundEffectsSource.volume = masterVolume * soundEffectsVolume;
        }
    }
}

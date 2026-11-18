using UnityEngine;

namespace MenuUIBase.Audio
{
    [CreateAssetMenu(fileName = "AudioCue", menuName = "Menu UI Base/Audio Cue")]
    public sealed class AudioCue : ScriptableObject
    {
        [SerializeField] private AudioClip clip;
        [SerializeField] private AudioChannel channel = AudioChannel.SoundEffects;
        [SerializeField, Range(0f, 1f)] private float volume = 1f;
        [SerializeField] private bool loop;
        [SerializeField, Min(0f)] private float fadeDuration = 0.35f;

        public AudioClip Clip => clip;
        public AudioChannel Channel => channel;
        public float Volume => volume;
        public bool Loop => loop;
        public float FadeDuration => fadeDuration;
    }
}

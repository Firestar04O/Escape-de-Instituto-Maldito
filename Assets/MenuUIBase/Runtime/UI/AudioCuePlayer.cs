using MenuUIBase.Audio;
using UnityEngine;

namespace MenuUIBase.UI
{
    public sealed class AudioCuePlayer : MonoBehaviour
    {
        [SerializeField] private AudioCue cue;
        [SerializeField] private AudioClip fallbackClip;
        [SerializeField, Range(0f, 1f)] private float fallbackVolume = 1f;

        public void Play()
        {
            if (cue != null)
                AudioManager.Instance.Play(cue);
            else if (fallbackClip != null)
                AudioManager.Instance.PlaySoundEffect(fallbackClip, fallbackVolume);
        }
    }
}

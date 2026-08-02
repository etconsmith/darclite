using UnityEngine;

namespace Darclite.Core
{
    // Project-wide UI sound convention: pop on hovering something new, click on clicking
    // something. A single shared AudioSource/singleton so any UI element can call these without
    // needing its own AudioSource.
    [AddComponentMenu("Darclite/UI Audio Player")]
    public class UIAudioPlayer : MonoBehaviour
    {
        public static UIAudioPlayer Instance { get; private set; }

        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip hoverClip;
        [SerializeField] private AudioClip clickClip;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public static void PlayHover()
        {
            if (Instance != null && Instance.audioSource != null && Instance.hoverClip != null)
            {
                Instance.audioSource.PlayOneShot(Instance.hoverClip);
            }
        }

        public static void PlayClick()
        {
            if (Instance != null && Instance.audioSource != null && Instance.clickClip != null)
            {
                Instance.audioSource.PlayOneShot(Instance.clickClip);
            }
        }
    }
}

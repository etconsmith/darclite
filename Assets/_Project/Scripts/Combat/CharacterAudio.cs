using UnityEngine;

namespace Darclite.Combat
{
    [RequireComponent(typeof(AudioSource))]
    [AddComponentMenu("Darclite/Character Audio")]
    public class CharacterAudio : MonoBehaviour
    {
        [Header("Footsteps")]
        [SerializeField] private AudioClip[] walkClips;
        [SerializeField] private AudioClip[] runClips;

        [Header("Movement")]
        [SerializeField] private AudioClip dashClip;
        [SerializeField] private AudioClip slideClip;
        [SerializeField] private AudioClip jumpTakeoffClip;
        [SerializeField] private AudioClip jumpLandClip;

        [Header("Combat")]
        [SerializeField] private AudioClip[] punchImpactClips;
        [SerializeField] private AudioClip heavyPunchImpactClip;

        [Header("Block/Dodge")]
        [SerializeField] private AudioClip guardBlockHitClip;
        [SerializeField] private AudioClip guardDodgeHitClip;
        [SerializeField] private AudioClip blockBreakClip;

        private AudioSource _audioSource;

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 1f;
        }

        public void PlayFootstep(bool isRunning)
        {
            PlayRandom(isRunning ? runClips : walkClips);
        }

        public void PlayDash()
        {
            PlayOneShot(dashClip);
        }

        public void PlaySlide()
        {
            PlayOneShot(slideClip);
        }

        public void PlayJumpTakeoff()
        {
            PlayOneShot(jumpTakeoffClip);
        }

        public void PlayJumpLand()
        {
            PlayOneShot(jumpLandClip);
        }

        public void PlayPunchImpact(bool isHeavy)
        {
            if (isHeavy)
            {
                PlayOneShot(heavyPunchImpactClip);
            }
            else
            {
                PlayRandom(punchImpactClips);
            }
        }

        public void PlayGuardBlockHit()
        {
            PlayOneShot(guardBlockHitClip);
        }

        public void PlayGuardDodgeHit()
        {
            PlayOneShot(guardDodgeHitClip);
        }

        public void PlayBlockBreak()
        {
            PlayOneShot(blockBreakClip);
        }

        private void PlayRandom(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0)
            {
                return;
            }

            PlayOneShot(clips[Random.Range(0, clips.Length)]);
        }

        private void PlayOneShot(AudioClip clip)
        {
            if (clip == null || _audioSource == null)
            {
                return;
            }

            _audioSource.PlayOneShot(clip);
        }
    }
}

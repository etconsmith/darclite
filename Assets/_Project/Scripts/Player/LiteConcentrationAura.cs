using System.Collections;
using Darclite.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.VFX;

namespace Darclite.Player
{
    // Lite Concentration's full effect package: ambient motes + rising wisps drifting around each
    // forearm, a hand-authored VFX Graph effect at each hand (both just Play()/Stop() plus their
    // own lifetime curves — no scripted intensity), a Fresnel rim glow on the forearms themselves,
    // a one-shot cast flash + expanding ring burst the instant the key is pressed, a looping
    // ambient hum for the duration, and a brief global Bloom boost — the light/rim/bloom group all
    // share one scripted "punch in, settle, hold, fade out" envelope so they read as one cohesive
    // pulse of light rather than separate timers; the loop audio starts at full volume immediately
    // (no fade-in) and only fades out at the very end.
    //
    // One component handles BOTH Lite Concentration and its upgrade, Lite Concentration II, rather
    // than duplicating this whole apparatus — since the Lite skill tree's tier-replace behavior
    // guarantees only one of the two is ever equipped at a time, whichever one actually activates
    // just selects a slightly stronger hand VFX/rim glow/damage multiplier for that cast.
    [AddComponentMenu("Darclite/Lite Concentration Aura")]
    public class LiteConcentrationAura : MonoBehaviour
    {
        private const string TierOneAbilityName = "Lite Concentration";
        private const string TierTwoAbilityName = "Lite Concentration II";
        private const float ActiveDuration = 20f;

        private const float LightPeakIntensity = 0.7f;
        private const float RimPeakIntensityTierOne = 2.2f;
        private const float RimPeakIntensityTierTwo = 2.6f;
        private const float BloomBoost = 0.25f;

        // Read by AttackCombo — 20%/30% bonus damage while active, matching whichever tier is
        // actually equipped.
        public const float DamageMultiplierTierOne = 1.2f;
        public const float DamageMultiplierTierTwo = 1.3f;

        private const float PunchInDuration = 0.12f;
        private const float SettleDuration = 0.18f;
        private const float PunchOvershoot = 1.25f;
        private const float FadeOutDuration = 0.45f;

        [SerializeField] private ParticleSystem[] moteParticles = new ParticleSystem[2];
        [SerializeField] private ParticleSystem[] wispParticles = new ParticleSystem[2];
        [SerializeField] private ParticleSystem[] castFlashParticles = new ParticleSystem[2];
        [SerializeField] private ParticleSystem[] castRingBurstParticles = new ParticleSystem[2];
        [SerializeField] private Light[] armLights = new Light[2];
        [SerializeField] private VisualEffect[] handVfx = new VisualEffect[2];
        [SerializeField] private VisualEffectAsset tierOneHandVfxAsset;
        [SerializeField] private VisualEffectAsset tierTwoHandVfxAsset;
        [SerializeField] private Material rimGlowMaterial;
        [SerializeField] private Volume gameplayVolume;
        [SerializeField] private AudioSource loopAudioSource;
        [SerializeField] private AudioClip loopClip;

        // Read by AttackCombo to decide whether a landed punch should use the bigger Lite Hit VFX
        // instead of the normal one.
        public bool IsActive { get; private set; }

        // Which tier actually activated most recently — drives DamageMultiplier and the visual
        // tuning below for the duration of that cast.
        public float DamageMultiplier { get; private set; } = DamageMultiplierTierOne;

        private bool _isTierTwo;
        private Coroutine _activeRoutine;
        private Coroutine _audioFadeRoutine;
        private Bloom _bloom;
        private float _baselineBloomIntensity;

        private void Awake()
        {
            if (gameplayVolume != null && gameplayVolume.profile != null && gameplayVolume.profile.TryGet(out Bloom bloom))
            {
                _bloom = bloom;
                _baselineBloomIntensity = bloom.intensity.value;
            }

            // Guarantees everything starts fully off regardless of whatever value the shared
            // material/light assets happened to be left at from a previous editor/play session.
            ApplyIntensity(0f);

            // VisualEffect assets auto-fire their "OnPlay" event once as soon as they're enabled —
            // stop both hand VFX immediately so the ability visibly starts off instead of already
            // running the moment the scene loads.
            for (int i = 0; i < 2; i++)
            {
                SetVfxPlaying(handVfx[i], false);
            }
        }

        private void OnEnable()
        {
            AbilityLoadout.Activated += HandleActivated;
        }

        private void OnDisable()
        {
            AbilityLoadout.Activated -= HandleActivated;
        }

        private void HandleActivated(int slotIndex)
        {
            string activatedAbilityName = AbilityLoadout.GetAbilityName(slotIndex);
            bool isTierOne = activatedAbilityName == TierOneAbilityName;
            bool isTierTwo = activatedAbilityName == TierTwoAbilityName;
            if (!isTierOne && !isTierTwo)
            {
                return;
            }

            _isTierTwo = isTierTwo;
            DamageMultiplier = isTierTwo ? DamageMultiplierTierTwo : DamageMultiplierTierOne;

            VisualEffectAsset handVfxAsset = isTierTwo ? tierTwoHandVfxAsset : tierOneHandVfxAsset;
            for (int i = 0; i < 2; i++)
            {
                if (handVfx[i] != null && handVfxAsset != null)
                {
                    handVfx[i].visualEffectAsset = handVfxAsset;
                }

                RestartOneShot(castFlashParticles[i]);
                RestartOneShot(castRingBurstParticles[i]);
            }

            if (_activeRoutine != null)
            {
                StopCoroutine(_activeRoutine);
            }
            if (_audioFadeRoutine != null)
            {
                StopCoroutine(_audioFadeRoutine);
                _audioFadeRoutine = null;
            }
            _activeRoutine = StartCoroutine(PlayAura());
        }

        private static void RestartOneShot(ParticleSystem system)
        {
            if (system == null)
            {
                return;
            }
            system.Clear(true);
            system.Play();
        }

        private IEnumerator PlayAura()
        {
            IsActive = true;
            SetSustainedParticlesPlaying(true);
            PlayLoopAudio();

            yield return DriveIntensity(0f, PunchOvershoot, PunchInDuration);
            yield return DriveIntensity(PunchOvershoot, 1f, SettleDuration);

            float sustainDuration = Mathf.Max(0f, ActiveDuration - PunchInDuration - SettleDuration - FadeOutDuration);
            yield return new WaitForSeconds(sustainDuration);

            _audioFadeRoutine = StartCoroutine(FadeOutLoopAudio(FadeOutDuration));
            yield return DriveIntensity(1f, 0f, FadeOutDuration);

            SetSustainedParticlesPlaying(false);
            IsActive = false;
            _activeRoutine = null;
        }

        // Starts instantly at full volume — no fade-in — so the cue reads as an immediate response
        // to the key press rather than swelling in.
        private void PlayLoopAudio()
        {
            if (loopAudioSource == null || loopClip == null)
            {
                return;
            }

            loopAudioSource.clip = loopClip;
            loopAudioSource.loop = true;
            loopAudioSource.volume = 1f;
            loopAudioSource.Play();
        }

        private IEnumerator FadeOutLoopAudio(float duration)
        {
            if (loopAudioSource == null)
            {
                yield break;
            }

            float startVolume = loopAudioSource.volume;
            float timer = 0f;
            while (timer < duration)
            {
                timer += Time.deltaTime;
                loopAudioSource.volume = Mathf.Lerp(startVolume, 0f, timer / duration);
                yield return null;
            }

            loopAudioSource.Stop();
            _audioFadeRoutine = null;
        }

        private void SetSustainedParticlesPlaying(bool playing)
        {
            for (int i = 0; i < 2; i++)
            {
                SetPlaying(moteParticles[i], playing);
                SetPlaying(wispParticles[i], playing);
                SetVfxPlaying(handVfx[i], playing);
            }
        }

        // VisualEffect has no shared base type with ParticleSystem, so it needs its own Play/Stop
        // pair — Stop still lets any already-emitted particles inside that graph finish naturally,
        // it just stops new ones from spawning.
        private static void SetVfxPlaying(VisualEffect effect, bool playing)
        {
            if (effect == null)
            {
                return;
            }

            if (playing)
            {
                effect.Play();
            }
            else
            {
                effect.Stop();
            }
        }

        // StopEmitting (rather than clearing outright) lets particles already alive finish their
        // own fade-out naturally instead of vanishing the instant the ability's window ends.
        private static void SetPlaying(ParticleSystem system, bool playing)
        {
            if (system == null)
            {
                return;
            }

            if (playing)
            {
                system.Play();
            }
            else
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private IEnumerator DriveIntensity(float from, float to, float duration)
        {
            if (duration <= 0f)
            {
                ApplyIntensity(to);
                yield break;
            }

            float timer = 0f;
            while (timer < duration)
            {
                timer += Time.deltaTime;
                ApplyIntensity(Mathf.Lerp(from, to, timer / duration));
                yield return null;
            }

            ApplyIntensity(to);
        }

        // Single driver for every scripted (non-self-animating) part of the effect — lights, the
        // shared rim-glow shell material, and a temporary Bloom boost — so they always move
        // together as one pulse instead of drifting out of sync.
        private void ApplyIntensity(float value)
        {
            float lightValue = LightPeakIntensity * value;
            for (int i = 0; i < 2; i++)
            {
                if (armLights[i] != null)
                {
                    armLights[i].intensity = lightValue;
                }
            }

            if (rimGlowMaterial != null)
            {
                float rimPeak = _isTierTwo ? RimPeakIntensityTierTwo : RimPeakIntensityTierOne;
                rimGlowMaterial.SetFloat("_Intensity", rimPeak * value);
            }

            if (_bloom != null)
            {
                _bloom.intensity.value = _baselineBloomIntensity + BloomBoost * value;
            }
        }
    }
}

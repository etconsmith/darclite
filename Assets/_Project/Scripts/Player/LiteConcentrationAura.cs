using System.Collections;
using Darclite.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Darclite.Player
{
    // Lite Concentration's full effect package: ambient motes + rising wisps drifting around each
    // forearm (their own fade is just Play()/Stop() plus each particle's own lifetime curve), a
    // Fresnel rim glow on the forearms themselves, a rotating rune ring at each palm, a one-shot
    // cast flash + expanding ring burst the instant the key is pressed, and a brief global Bloom
    // boost — the light/rim/ring/bloom group all share one scripted "punch in, settle, hold, fade
    // out" envelope so they read as one cohesive pulse of light rather than four separate timers.
    [AddComponentMenu("Darclite/Lite Concentration Aura")]
    public class LiteConcentrationAura : MonoBehaviour
    {
        private const string AbilityName = "Lite Concentration";
        private const float ActiveDuration = 6f;

        private const float LightPeakIntensity = 0.7f;
        private const float RimPeakIntensity = 2.2f;
        private const float RingPeakAlpha = 0.85f;
        private const float BloomBoost = 0.25f;

        private const float PunchInDuration = 0.12f;
        private const float SettleDuration = 0.18f;
        private const float PunchOvershoot = 1.25f;
        private const float FadeOutDuration = 0.45f;

        [SerializeField] private ParticleSystem[] moteParticles = new ParticleSystem[2];
        [SerializeField] private ParticleSystem[] wispParticles = new ParticleSystem[2];
        [SerializeField] private ParticleSystem[] castFlashParticles = new ParticleSystem[2];
        [SerializeField] private ParticleSystem[] castRingBurstParticles = new ParticleSystem[2];
        [SerializeField] private Light[] armLights = new Light[2];
        [SerializeField] private Material rimGlowMaterial;
        [SerializeField] private Material ringMaterial;
        [SerializeField] private Volume gameplayVolume;

        private Coroutine _activeRoutine;
        private Bloom _bloom;
        private float _baselineBloomIntensity;
        private Color _ringBaseColor = Color.white;

        private void Awake()
        {
            if (ringMaterial != null)
            {
                Color c = ringMaterial.GetColor("_Color");
                _ringBaseColor = new Color(c.r, c.g, c.b, 1f);
            }

            if (gameplayVolume != null && gameplayVolume.profile != null && gameplayVolume.profile.TryGet(out Bloom bloom))
            {
                _bloom = bloom;
                _baselineBloomIntensity = bloom.intensity.value;
            }

            // Guarantees everything starts fully off regardless of whatever value the shared
            // material/light assets happened to be left at from a previous editor/play session.
            ApplyIntensity(0f);
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
            if (AbilityLoadout.GetAbilityName(slotIndex) != AbilityName)
            {
                return;
            }

            for (int i = 0; i < 2; i++)
            {
                RestartOneShot(castFlashParticles[i]);
                RestartOneShot(castRingBurstParticles[i]);
            }

            if (_activeRoutine != null)
            {
                StopCoroutine(_activeRoutine);
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
            SetSustainedParticlesPlaying(true);

            yield return DriveIntensity(0f, PunchOvershoot, PunchInDuration);
            yield return DriveIntensity(PunchOvershoot, 1f, SettleDuration);

            float sustainDuration = Mathf.Max(0f, ActiveDuration - PunchInDuration - SettleDuration - FadeOutDuration);
            yield return new WaitForSeconds(sustainDuration);

            yield return DriveIntensity(1f, 0f, FadeOutDuration);

            SetSustainedParticlesPlaying(false);
            _activeRoutine = null;
        }

        private void SetSustainedParticlesPlaying(bool playing)
        {
            for (int i = 0; i < 2; i++)
            {
                SetPlaying(moteParticles[i], playing);
                SetPlaying(wispParticles[i], playing);
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
        // shared rim-glow shell material, the shared palm-ring material, and a temporary Bloom
        // boost — so they always move together as one pulse instead of drifting out of sync.
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
                rimGlowMaterial.SetFloat("_Intensity", RimPeakIntensity * value);
            }

            if (ringMaterial != null)
            {
                ringMaterial.SetColor("_Color", new Color(_ringBaseColor.r, _ringBaseColor.g, _ringBaseColor.b, RingPeakAlpha * value));
            }

            if (_bloom != null)
            {
                _bloom.intensity.value = _baselineBloomIntensity + BloomBoost * value;
            }
        }
    }
}

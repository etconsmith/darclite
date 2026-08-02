using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Darclite.Player
{
    // The temporary top-of-screen XP bar — hidden until the player gains XP, then fades in,
    // plays a chunked fill animation (a "gain ghost" snaps ahead to the target instantly while
    // the real fill catches up to it, the mirror of PlayerHealthUI's damage trail), and fades
    // back out after a hold period. Crossing a level threshold resets the bar and keeps filling
    // with whatever XP is left over, repeating faster for each additional level gained at once.
    [AddComponentMenu("Darclite/Xp Bar UI")]
    public class XpBarUI : MonoBehaviour
    {
        [SerializeField] private PlayerStats playerStats;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Text levelText;
        [SerializeField] private Text fractionText;
        [SerializeField] private Image fillImage;
        [SerializeField] private Image gainGhostImage;

        [Header("Visibility")]
        [SerializeField] private float visibleAlpha = 0.92f;
        [SerializeField] private float fadeInDuration = 0.25f;
        [SerializeField] private float fadeOutDuration = 0.6f;
        [SerializeField] private float holdDuration = 3f;

        [Header("Fill Steps")]
        [SerializeField] private float firstStepDuration = 1f;
        [SerializeField] private float stepSpeedUpFactor = 0.65f;
        [SerializeField] private float minStepDuration = 0.24f;

        private struct FillStep
        {
            public int level;
            public int fromXp;
            public int toXp;
            public int required;
            public bool leveledUp;
        }

        private Coroutine _fadeCoroutine;
        private Coroutine _hideCoroutine;
        private Coroutine _fillCoroutine;

        private void Awake()
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
        }

        private void OnEnable()
        {
            if (playerStats != null)
            {
                playerStats.XpGained += HandleXpGained;
            }
        }

        private void Start()
        {
            // PlayerStats lives on a different GameObject (the Player, not this HUD canvas), so
            // Unity doesn't guarantee its Awake() ran before ours — read the real values here.
            if (playerStats != null)
            {
                SetImmediate(playerStats.Level, playerStats.CurrentXp);
            }
        }

        private void OnDisable()
        {
            if (playerStats != null)
            {
                playerStats.XpGained -= HandleXpGained;
            }
        }

        private void SetImmediate(int level, int xp)
        {
            int required = PlayerStats.GetXpRequiredForLevel(level);
            float fraction = GetFraction(xp, required);

            if (levelText != null) levelText.text = level.ToString();
            if (fillImage != null) fillImage.fillAmount = fraction;
            if (gainGhostImage != null) gainGhostImage.fillAmount = fraction;
            UpdateFractionText(level, xp);
        }

        private void HandleXpGained(XpGainInfo info)
        {
            ShowBar();

            if (_fillCoroutine != null)
            {
                StopCoroutine(_fillCoroutine);
            }
            _fillCoroutine = StartCoroutine(PlaySteps(BuildSteps(info)));
        }

        private static List<FillStep> BuildSteps(XpGainInfo info)
        {
            List<FillStep> steps = new List<FillStep>();
            int simLevel = info.startLevel;
            int simXp = info.startXp;
            int remaining = info.amountGained;

            while (true)
            {
                int required = PlayerStats.GetXpRequiredForLevel(simLevel);
                int needed = required - simXp;

                if (remaining >= needed)
                {
                    steps.Add(new FillStep { level = simLevel, fromXp = simXp, toXp = required, required = required, leveledUp = true });
                    remaining -= needed;
                    simXp = 0;
                    simLevel++;
                }
                else
                {
                    steps.Add(new FillStep { level = simLevel, fromXp = simXp, toXp = simXp + remaining, required = required, leveledUp = false });
                    break;
                }
            }

            return steps;
        }

        private IEnumerator PlaySteps(List<FillStep> steps)
        {
            float duration = firstStepDuration;

            for (int i = 0; i < steps.Count; i++)
            {
                FillStep step = steps[i];

                if (levelText != null) levelText.text = step.level.ToString();
                if (gainGhostImage != null) gainGhostImage.fillAmount = GetFraction(step.toXp, step.required);
                if (fillImage != null) fillImage.fillAmount = GetFraction(step.fromXp, step.required);
                UpdateFractionText(step.level, step.fromXp);

                float timer = 0f;
                while (timer < duration)
                {
                    timer += Time.deltaTime;
                    float t = Mathf.Clamp01(timer / duration);
                    float xp = Mathf.Lerp(step.fromXp, step.toXp, t);
                    if (fillImage != null) fillImage.fillAmount = GetFraction(xp, step.required);
                    UpdateFractionText(step.level, Mathf.RoundToInt(xp));
                    yield return null;
                }

                if (fillImage != null) fillImage.fillAmount = GetFraction(step.toXp, step.required);
                UpdateFractionText(step.level, step.toXp);

                if (step.leveledUp)
                {
                    // Brief pause at a full bar so the level-up actually reads before it resets.
                    yield return new WaitForSeconds(0.1f);

                    float drainDuration = duration * 0.4f;
                    float drainTimer = 0f;
                    while (drainTimer < drainDuration)
                    {
                        drainTimer += Time.deltaTime;
                        float fraction = Mathf.Lerp(1f, 0f, drainTimer / drainDuration);
                        if (fillImage != null) fillImage.fillAmount = fraction;
                        if (gainGhostImage != null) gainGhostImage.fillAmount = fraction;
                        yield return null;
                    }

                    int nextLevel = step.level + 1;
                    if (levelText != null) levelText.text = nextLevel.ToString();
                    if (fillImage != null) fillImage.fillAmount = 0f;
                    if (gainGhostImage != null) gainGhostImage.fillAmount = 0f;
                    UpdateFractionText(nextLevel, 0);
                }

                duration = Mathf.Max(minStepDuration, duration * stepSpeedUpFactor);
            }

            _fillCoroutine = null;
            ScheduleHide();
        }

        private void UpdateFractionText(int level, int xp)
        {
            if (fractionText != null)
            {
                fractionText.text = $"{xp}/{PlayerStats.GetXpRequiredForLevel(level)}";
            }
        }

        private static float GetFraction(float xp, int required)
        {
            return required > 0 ? Mathf.Clamp01(xp / required) : 0f;
        }

        private void ShowBar()
        {
            if (_hideCoroutine != null)
            {
                StopCoroutine(_hideCoroutine);
                _hideCoroutine = null;
            }
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
            }
            _fadeCoroutine = StartCoroutine(FadeCanvas(visibleAlpha, fadeInDuration));
        }

        private void ScheduleHide()
        {
            if (_hideCoroutine != null)
            {
                StopCoroutine(_hideCoroutine);
            }
            _hideCoroutine = StartCoroutine(HoldThenFadeOut());
        }

        private IEnumerator HoldThenFadeOut()
        {
            yield return new WaitForSeconds(holdDuration);
            yield return FadeCanvas(0f, fadeOutDuration);
            _hideCoroutine = null;
        }

        private IEnumerator FadeCanvas(float targetAlpha, float duration)
        {
            if (canvasGroup == null)
            {
                yield break;
            }

            float start = canvasGroup.alpha;
            float timer = 0f;
            while (timer < duration)
            {
                timer += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(start, targetAlpha, timer / duration);
                yield return null;
            }
            canvasGroup.alpha = targetAlpha;
        }
    }
}

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Darclite.Core
{
    // Drag-to-pan for the Lite skill tree. The tree's actual nodes only use a small slice of the
    // big bordered background this pans around inside of — that's deliberate headroom for future
    // trees/tiers. Dragging past the border gives a little elastic resistance instead of a hard
    // wall (reads as "slippery"/alive), but always eases back inside on release. Letting go
    // mid-flick keeps the drag's velocity and glides it to a stop via exponential decay rather
    // than cutting off dead. A short-lived ghost trail of the background's grid layer kicks in
    // above a speed threshold as a cheap stand-in for real motion blur (Screen Space - Overlay
    // canvases aren't touched by the camera's post-process stack, so a real post-process blur
    // isn't an option here without a dedicated UI camera/render-texture rework).
    [AddComponentMenu("Darclite/Skill Tree Pan Zone")]
    public class SkillTreePanZone : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        [SerializeField] private RectTransform viewport;
        [SerializeField] private RectTransform panContent;

        // Bounding box (in panContent's local space) of the big background the tree pans around
        // inside of — computed once by StatMenuBootstrapper from the same fixed background size
        // it builds, centered on the tree's own node bounds.
        [SerializeField] private Vector2 contentMin;
        [SerializeField] private Vector2 contentMax;

        [Header("Zoom")]
        [SerializeField] private float minZoom = 0.6f;
        [SerializeField] private float maxZoom = 1.6f;

        [Header("Motion Blur (fake)")]
        [SerializeField] private Sprite trailSprite;
        [SerializeField] private Vector2 trailSize;
        [SerializeField] private Vector2 trailCenterOffset;
        [SerializeField] private Color trailColor = new Color(0.4f, 0.55f, 0.75f, 1f);

        private const float OverDragResistance = 0.35f;
        private const float MaxOverDrag = 90f;
        private const float SnapBackDuration = 0.35f;
        private const float VelocitySmoothing = 12f;
        private const float MomentumDecay = 3.2f;
        private const float MinMomentumSpeed = 8f;
        private const float FastPanSpeedThreshold = 1400f;
        private const float GhostSpawnInterval = 0.035f;
        private const float GhostLifetime = 0.16f;
        private const float GhostStartAlpha = 0.3f;
        private const int GhostPoolSize = 6;
        private const float ZoomStep = 0.12f;

        private Vector2 _pointerLocalPosition;
        private Vector2 _velocity;
        private Vector2 _panMin;
        private Vector2 _panMax;
        private float _zoom = 1f;
        private bool _isDragging;
        private float _ghostSpawnTimer;

        private Coroutine _momentumRoutine;
        private Coroutine _snapBackRoutine;

        private Image[] _ghostImages;
        private float[] _ghostSpawnTimes;
        private int _nextGhostIndex;

        private void Awake()
        {
            if (trailSprite != null && viewport != null)
            {
                BuildGhostPool();
            }
        }

        private void Start()
        {
            RecalculateBounds();
        }

        private void OnDisable()
        {
            StopMomentum();
            StopSnapBack();
            _isDragging = false;
        }

        private void RecalculateBounds()
        {
            if (viewport == null)
            {
                return;
            }

            // contentMin/contentMax are authored at zoom 1 — scale them by the current zoom so
            // the border stays snug against the background's actual on-screen edges at any zoom
            // level instead of only being correct when zoomed all the way out.
            Vector2 half = viewport.rect.size * 0.5f;
            _panMin = half - contentMax * _zoom;
            _panMax = -half - contentMin * _zoom;
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (panContent == null || viewport == null)
            {
                return;
            }

            float previousZoom = _zoom;
            float targetZoom = Mathf.Clamp(_zoom + eventData.scrollDelta.y * ZoomStep, minZoom, maxZoom);
            if (Mathf.Approximately(targetZoom, previousZoom))
            {
                return;
            }

            // Zoom toward the cursor: capture the point currently under it in panContent's
            // unscaled space, rescale, then shift anchoredPosition so that same point stays under
            // the cursor instead of the view visibly recentering on every scroll tick.
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, eventData.position, eventData.pressEventCamera, out Vector2 viewportPoint))
            {
                Vector2 unscaledPointer = (viewportPoint - panContent.anchoredPosition) / previousZoom;
                _zoom = targetZoom;
                panContent.localScale = Vector3.one * _zoom;
                panContent.anchoredPosition = viewportPoint - unscaledPointer * _zoom;
            }
            else
            {
                _zoom = targetZoom;
                panContent.localScale = Vector3.one * _zoom;
            }

            StopMomentum();
            StopSnapBack();
            RecalculateBounds();
            panContent.anchoredPosition = ClampHard(panContent.anchoredPosition);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (panContent == null || viewport == null)
            {
                return;
            }

            StopMomentum();
            StopSnapBack();
            _isDragging = true;
            _velocity = Vector2.zero;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, eventData.position, eventData.pressEventCamera, out _pointerLocalPosition);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_isDragging || panContent == null || viewport == null)
            {
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
            {
                return;
            }

            Vector2 delta = localPoint - _pointerLocalPosition;
            _pointerLocalPosition = localPoint;

            panContent.anchoredPosition = ApplyOverDragResistance(panContent.anchoredPosition + delta);

            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            _velocity = Vector2.Lerp(_velocity, delta / dt, Mathf.Clamp01(VelocitySmoothing * dt));

            UpdateGhostTrail(_velocity, dt);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _isDragging = false;
            if (panContent == null)
            {
                return;
            }

            if (IsOutsideBounds(panContent.anchoredPosition))
            {
                StartSnapBack();
            }
            else if (_velocity.sqrMagnitude > MinMomentumSpeed * MinMomentumSpeed)
            {
                StartMomentum();
            }
        }

        private bool IsOutsideBounds(Vector2 position)
        {
            return position.x < _panMin.x || position.x > _panMax.x || position.y < _panMin.y || position.y > _panMax.y;
        }

        private Vector2 ClampHard(Vector2 position)
        {
            return new Vector2(
                Mathf.Clamp(position.x, _panMin.x, _panMax.x),
                Mathf.Clamp(position.y, _panMin.y, _panMax.y));
        }

        private Vector2 ApplyOverDragResistance(Vector2 position)
        {
            return new Vector2(
                ApplyAxisResistance(position.x, _panMin.x, _panMax.x),
                ApplyAxisResistance(position.y, _panMin.y, _panMax.y));
        }

        private static float ApplyAxisResistance(float value, float min, float max)
        {
            if (value < min)
            {
                float over = min - value;
                return min - Mathf.Min(over * OverDragResistance, MaxOverDrag);
            }
            if (value > max)
            {
                float over = value - max;
                return max + Mathf.Min(over * OverDragResistance, MaxOverDrag);
            }
            return value;
        }

        private void StartMomentum()
        {
            StopMomentum();
            _momentumRoutine = StartCoroutine(MomentumRoutine());
        }

        private void StopMomentum()
        {
            if (_momentumRoutine != null)
            {
                StopCoroutine(_momentumRoutine);
                _momentumRoutine = null;
            }
        }

        private System.Collections.IEnumerator MomentumRoutine()
        {
            Vector2 velocity = _velocity;

            while (velocity.magnitude > MinMomentumSpeed)
            {
                float dt = Time.unscaledDeltaTime;
                Vector2 target = panContent.anchoredPosition + velocity * dt;
                Vector2 clamped = ClampHard(target);

                if (!Mathf.Approximately(clamped.x, target.x))
                {
                    velocity.x = 0f;
                }
                if (!Mathf.Approximately(clamped.y, target.y))
                {
                    velocity.y = 0f;
                }

                panContent.anchoredPosition = clamped;
                UpdateGhostTrail(velocity, dt);

                // Exponential decay reads as smooth, physically-plausible friction rather than a
                // linear slide that visibly cuts off — this is the "don't stop abruptly" feel.
                velocity *= Mathf.Exp(-MomentumDecay * dt);
                yield return null;
            }

            _momentumRoutine = null;
        }

        private void StartSnapBack()
        {
            StopSnapBack();
            _snapBackRoutine = StartCoroutine(SnapBackRoutine());
        }

        private void StopSnapBack()
        {
            if (_snapBackRoutine != null)
            {
                StopCoroutine(_snapBackRoutine);
                _snapBackRoutine = null;
            }
        }

        private System.Collections.IEnumerator SnapBackRoutine()
        {
            Vector2 start = panContent.anchoredPosition;
            Vector2 end = ClampHard(start);
            float t = 0f;

            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / SnapBackDuration;
                float eased = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
                panContent.anchoredPosition = Vector2.LerpUnclamped(start, end, eased);
                yield return null;
            }

            panContent.anchoredPosition = end;
            _snapBackRoutine = null;
        }

        private void BuildGhostPool()
        {
            _ghostImages = new Image[GhostPoolSize];
            _ghostSpawnTimes = new float[GhostPoolSize];

            for (int i = 0; i < GhostPoolSize; i++)
            {
                GameObject ghostObject = new GameObject($"PanGhost_{i}", typeof(RectTransform), typeof(Image));
                ghostObject.transform.SetParent(viewport, false);
                RectTransform rect = ghostObject.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = trailSize;

                Image image = ghostObject.GetComponent<Image>();
                image.sprite = trailSprite;
                image.type = Image.Type.Tiled;
                image.raycastTarget = false;
                image.color = new Color(trailColor.r, trailColor.g, trailColor.b, 0f);

                ghostObject.SetActive(false);
                _ghostImages[i] = image;
                _ghostSpawnTimes[i] = -1f;
            }
        }

        private void UpdateGhostTrail(Vector2 velocity, float dt)
        {
            if (_ghostImages == null || panContent == null)
            {
                return;
            }

            if (velocity.magnitude >= FastPanSpeedThreshold)
            {
                _ghostSpawnTimer -= dt;
                if (_ghostSpawnTimer <= 0f)
                {
                    SpawnGhost();
                    _ghostSpawnTimer = GhostSpawnInterval;
                }
            }

            for (int i = 0; i < _ghostImages.Length; i++)
            {
                Image ghost = _ghostImages[i];
                if (ghost == null || !ghost.gameObject.activeSelf)
                {
                    continue;
                }

                float age = Time.unscaledTime - _ghostSpawnTimes[i];
                if (age >= GhostLifetime)
                {
                    ghost.gameObject.SetActive(false);
                    continue;
                }

                float alpha = Mathf.Lerp(GhostStartAlpha, 0f, age / GhostLifetime);
                ghost.color = new Color(trailColor.r, trailColor.g, trailColor.b, alpha);
            }
        }

        private void SpawnGhost()
        {
            Image ghost = _ghostImages[_nextGhostIndex];
            ghost.rectTransform.anchoredPosition = panContent.anchoredPosition + trailCenterOffset * _zoom;
            ghost.rectTransform.sizeDelta = trailSize * _zoom;
            ghost.color = new Color(trailColor.r, trailColor.g, trailColor.b, GhostStartAlpha);
            ghost.gameObject.SetActive(true);
            _ghostSpawnTimes[_nextGhostIndex] = Time.unscaledTime;

            _nextGhostIndex = (_nextGhostIndex + 1) % _ghostImages.Length;
        }
    }
}

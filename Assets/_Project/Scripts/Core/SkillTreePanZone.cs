using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Darclite.Core
{
    // Drag-to-pan (and scroll-to-zoom) for the Lite skill tree. The tree's actual nodes only use a
    // small slice of the big bordered background this pans around inside of — that's deliberate
    // headroom for future trees/tiers. Panning and zooming are both hard-clamped to the
    // background's edges — no elastic overdrag, no bounce — letting go mid-flick keeps the drag's
    // velocity and glides it to a stop via exponential decay rather than cutting off dead.
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

        private const float VelocitySmoothing = 12f;
        private const float MomentumDecay = 3.2f;
        private const float MinMomentumSpeed = 8f;
        private const float ZoomStep = 0.12f;

        private Vector2 _pointerLocalPosition;
        private Vector2 _velocity;
        private Vector2 _panMin;
        private Vector2 _panMax;
        private float _zoom = 1f;
        private bool _isDragging;

        private Coroutine _momentumRoutine;

        private void Start()
        {
            RecalculateBounds();
        }

        private void OnDisable()
        {
            StopMomentum();
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

            panContent.anchoredPosition = ClampHard(panContent.anchoredPosition + delta);

            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            _velocity = Vector2.Lerp(_velocity, delta / dt, Mathf.Clamp01(VelocitySmoothing * dt));
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _isDragging = false;
            if (panContent == null)
            {
                return;
            }

            if (_velocity.sqrMagnitude > MinMomentumSpeed * MinMomentumSpeed)
            {
                StartMomentum();
            }
        }

        private Vector2 ClampHard(Vector2 position)
        {
            return new Vector2(
                Mathf.Clamp(position.x, _panMin.x, _panMax.x),
                Mathf.Clamp(position.y, _panMin.y, _panMax.y));
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

                // Exponential decay reads as smooth, physically-plausible friction rather than a
                // linear slide that visibly cuts off — this is the "don't stop abruptly" feel.
                velocity *= Mathf.Exp(-MomentumDecay * dt);
                yield return null;
            }

            _momentumRoutine = null;
        }
    }
}

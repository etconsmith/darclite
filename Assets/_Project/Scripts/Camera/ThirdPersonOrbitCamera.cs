using UnityEngine;
using UnityEngine.InputSystem;

namespace Darclite.CameraSystem
{
    [AddComponentMenu("Darclite/Third Person Orbit Camera")]
    public class ThirdPersonOrbitCamera : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1.6f, 0f);

        [Header("Orbit")]
        [SerializeField] private float distance = 5f;
        [SerializeField] private float minDistance = 1.5f;
        [SerializeField] private float maxDistance = 10f;
        [SerializeField] private float zoomSpeed = 5f;
        [SerializeField] private float mouseSensitivity = 0.15f;
        [SerializeField] private float minPitch = -35f;
        [SerializeField] private float maxPitch = 75f;

        [Header("Collision")]
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField] private float collisionRadius = 0.2f;

        private float _yaw;
        private float _pitch = 15f;
        private float _currentDistance;
        private float _shakeTimer;
        private float _shakeDuration;
        private float _shakeMagnitude;

        public float Yaw => _yaw;

        public void Shake(float duration, float magnitude)
        {
            _shakeDuration = Mathf.Max(duration, 0.01f);
            _shakeTimer = _shakeDuration;
            _shakeMagnitude = magnitude;
        }

        private void Awake()
        {
            _currentDistance = distance;
            Vector3 angles = transform.eulerAngles;
            _yaw = angles.y;
            _pitch = angles.x;
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            HandleCursorState();
            HandleRotationInput();
            HandleZoomInput();

            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 pivot = target.position + targetOffset;
            Vector3 desiredPosition = pivot - rotation * Vector3.forward * _currentDistance;

            desiredPosition = ResolveCollision(pivot, desiredPosition);
            desiredPosition += HandleShake();

            transform.SetPositionAndRotation(desiredPosition, rotation);
        }

        private static void HandleCursorState()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void HandleRotationInput()
        {
            if (Mouse.current == null)
            {
                return;
            }

            Vector2 delta = Mouse.current.delta.ReadValue();
            _yaw += delta.x * mouseSensitivity;
            _pitch -= delta.y * mouseSensitivity;
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
        }

        private void HandleZoomInput()
        {
            if (Mouse.current == null)
            {
                return;
            }

            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Approximately(scroll, 0f))
            {
                return;
            }

            _currentDistance = Mathf.Clamp(_currentDistance - scroll * zoomSpeed * 0.01f, minDistance, maxDistance);
        }

        private Vector3 HandleShake()
        {
            if (_shakeTimer <= 0f)
            {
                return Vector3.zero;
            }

            _shakeTimer -= Time.deltaTime;
            float damper = Mathf.Clamp01(_shakeTimer / _shakeDuration);
            return Random.insideUnitSphere * (_shakeMagnitude * damper);
        }

        private Vector3 ResolveCollision(Vector3 pivot, Vector3 desiredPosition)
        {
            Vector3 direction = desiredPosition - pivot;
            float length = direction.magnitude;
            if (length < 0.0001f)
            {
                return desiredPosition;
            }

            direction /= length;

            if (Physics.SphereCast(pivot, collisionRadius, direction, out RaycastHit hit, length, collisionMask, QueryTriggerInteraction.Ignore))
            {
                float clampedDistance = Mathf.Max(hit.distance - collisionRadius, minDistance * 0.25f);
                return pivot + direction * clampedDistance;
            }

            return desiredPosition;
        }
    }
}

using UnityEngine;

namespace Darclite.CameraSystem
{
    // Feeds the GodRays fullscreen shader the sun's current screen-space position every frame —
    // the shader itself has no concept of "where the light is," it just radially samples toward
    // whatever point this script gives it. That radial math still works correctly even when the
    // sun's projected position lands well outside the [0,1] viewport (the rays just converge
    // toward an off-screen point, which reads fine), so visibility is kept at full strength across
    // a wide viewing cone and only fades out as the camera swings toward facing directly away from
    // the sun — not merely away from its exact on-screen position.
    [AddComponentMenu("Darclite/God Ray Sun Tracker")]
    public class GodRaySunTracker : MonoBehaviour
    {
        private static readonly int SunScreenPosId = Shader.PropertyToID("_SunScreenPos");
        private static readonly int SunVisibilityId = Shader.PropertyToID("_SunVisibility");

        [SerializeField] private Light sun;
        [SerializeField] private Material godRayMaterial;
        [SerializeField] private float sunDistance = 5000f;
        // Full strength anywhere within this angle of looking straight at the sun...
        [SerializeField] private float fullVisibilityAngle = 65f;
        // ...fading to zero by this angle, safely before 90° (where the sun's projected position
        // crosses behind the camera and the screen-space math stops being meaningful).
        [SerializeField] private float fadeOutAngle = 88f;

        private Camera _camera;

        private void LateUpdate()
        {
            if (sun == null || godRayMaterial == null)
            {
                return;
            }

            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null)
                {
                    return;
                }
            }

            // Treat the sun as an infinitely distant point behind its own light direction, then
            // project that point to screen space the same way an actual sun disc would appear.
            Vector3 towardSun = -sun.transform.forward;
            Vector3 sunWorldPosition = _camera.transform.position + towardSun * sunDistance;
            Vector3 viewportPoint = _camera.WorldToViewportPoint(sunWorldPosition);

            float angleFromForward = Vector3.Angle(_camera.transform.forward, towardSun);
            float visibility = 1f - Mathf.Clamp01((angleFromForward - fullVisibilityAngle) / Mathf.Max(1f, fadeOutAngle - fullVisibilityAngle));

            godRayMaterial.SetVector(SunScreenPosId, viewportPoint);
            godRayMaterial.SetFloat(SunVisibilityId, visibility);
        }
    }
}

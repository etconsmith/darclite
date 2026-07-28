using UnityEngine;

namespace Darclite.Player
{
    public class DashGhostFader : MonoBehaviour
    {
        private static readonly int ColorParam = Shader.PropertyToID("_Color");

        private Material _material;
        private Color _startColor;
        private float _lifetime;
        private float _timer;

        public void Initialize(Material material, Color startColor, float lifetime)
        {
            _material = material;
            _startColor = startColor;
            _lifetime = Mathf.Max(lifetime, 0.01f);
            _timer = 0f;
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            float t = Mathf.Clamp01(_timer / _lifetime);

            Color color = _startColor;
            color.a = Mathf.Lerp(_startColor.a, 0f, t);
            _material.SetColor(ColorParam, color);

            if (t >= 1f)
            {
                Destroy(_material);
                Destroy(gameObject);
            }
        }
    }
}

using UnityEngine;

namespace BrainshEditor
{
    /// <summary>Keeps the interface inside the safe area of the phone screen.</summary>
    [AddComponentMenu("")]
    public class SafeAreaFitter : MonoBehaviour
    {
        public RectTransform target;

        private Rect _last;

        private void Awake()
        {
            if (target == null) target = (RectTransform)transform;
        }

        private void Update()
        {
            Rect safe = Screen.safeArea;
            if (safe == _last) return;

            _last = safe;
            Apply(safe);
        }

        private void Apply(Rect safe)
        {
            if (target.parent == null) return;

            RectTransform parent = target.parent as RectTransform;
            if (parent == null) return;

            Rect parentRect = parent.rect;
            if (parentRect.width <= 0f || parentRect.height <= 0f) return;

            Vector2 min = parent.InverseTransformPoint(safe.position);
            Vector2 max = parent.InverseTransformPoint(safe.position + safe.size);

            // Screen.safeArea is in pixels, anchors are a fraction of the parent rect.
            target.anchorMin = new Vector2(
                Mathf.Clamp01((min.x - parentRect.xMin) / parentRect.width),
                Mathf.Clamp01((min.y - parentRect.yMin) / parentRect.height));

            target.anchorMax = new Vector2(
                Mathf.Clamp01((max.x - parentRect.xMin) / parentRect.width),
                Mathf.Clamp01((max.y - parentRect.yMin) / parentRect.height));

            target.offsetMin = Vector2.zero;
            target.offsetMax = Vector2.zero;
        }
    }
}
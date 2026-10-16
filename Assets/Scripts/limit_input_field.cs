using UnityEngine;

public class limit_input_field : MonoBehaviour
{
    [Tooltip("Поле, высота которого ограничивается. Если не задано — берётся RectTransform этого объекта.")]
    public RectTransform input_text_rect;

    [Tooltip("Максимальная высота поля в единицах Canvas.")]
    public float max_height = 6.5f;

    void LateUpdate()
    {
        RectTransform rect = input_text_rect;

        if (rect == null)
            rect = transform as RectTransform;

        if (rect == null)
            return;

        ClampHeight(rect, max_height);
    } 

    // Обрезает высоту прямоугольника до limit, сохраняя его центр (anchoredPosition).
    static void ClampHeight(RectTransform rect, float limit)
    {
        if (limit <= 0f || rect.rect.height <= limit)
            return;

        // Для растянутых якорей rect.height = высота родителя * (anchorMax.y - anchorMin.y) + sizeDelta.y
        float stretched = 0f;
        RectTransform parent = rect.parent as RectTransform;
        if (parent != null)
            stretched = (rect.anchorMax.y - rect.anchorMin.y) * parent.rect.height;

        Vector2 size_delta = rect.sizeDelta;
        size_delta.y = limit - stretched;
        rect.sizeDelta = size_delta;
    }
}
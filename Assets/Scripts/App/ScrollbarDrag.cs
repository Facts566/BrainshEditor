using UnityEngine;
using UnityEngine.EventSystems;

namespace BrainshEditor
{
    /// <summary>Forwards pointer events from the scroll bar to the editor.</summary>
    [AddComponentMenu("")]
    public class ScrollbarDrag : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public CodeEditorView editor;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (editor != null) editor.OnScrollbarDown(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (editor != null) editor.OnScrollbarDrag(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (editor != null) editor.OnScrollbarUp(eventData);
        }
    }
}
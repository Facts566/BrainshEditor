using UnityEngine;
using UnityEngine.UI;

namespace BrainshEditor
{
    /// <summary>
    /// Background of the editor: a soft band behind the current line and the selection
    /// rectangles. Drawn under the glyphs.
    /// </summary>
    [AddComponentMenu("")]
    public class RectBandGraphic : MaskableGraphic
    {
        public CodeLayout layout;
        public int caret;
        public int anchor;

        public int firstLine;
        public int lineWindow = 60;

        public float paddingX = 8f;
        public float paddingY = 6f;
        public float contentWidth = 200f;

        public Color lineColor = Ui.CurrentLine;
        public Color selectionColor = Ui.Selection;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (layout == null) return;

            float lineHeight = layout.LineHeight;

            int from = Mathf.Max(0, Mathf.Min(firstLine, firstLine + lineWindow - 1));
            int to = firstLine + Mathf.Max(1, lineWindow) - 1;

            // Current line
            int caretLine = layout.LineOf(caret);
            if (caretLine >= from && caretLine <= to)
            {
                float y = paddingY + caretLine * lineHeight;
                AddRect(vh, 0f, y, Mathf.Max(rectTransform.rect.width, contentWidth), lineHeight, lineColor);
            }

            // Selection
            if (anchor != caret)
            {
                int start = Mathf.Min(anchor, caret);
                int end = Mathf.Max(anchor, caret);

                int first = layout.LineOf(start);
                int last = layout.LineOf(end);

                first = Mathf.Max(first, from);
                last = Mathf.Min(last, to);

                for (int line = first; line <= last; line++)
                {
                    int lineStart = layout.StartOfLine(line);
                    int lineEnd = layout.EndOfLine(line);

                    int fromIndex = Mathf.Max(start, lineStart);
                    int toIndex = Mathf.Min(end, lineEnd);

                    float x1 = paddingX + layout.XOf(fromIndex);
                    float x2 = paddingX + layout.XOf(toIndex);
                    if (x2 <= x1) continue;

                    float y = paddingY + line * lineHeight;
                    AddRect(vh, x1, y, x2 - x1, lineHeight, selectionColor);
                }
            }
        }

        private static void AddRect(VertexHelper vh, float x, float y, float width, float height, Color tint)
        {
            int index = vh.currentVertCount;

            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = tint;
            vertex.uv0 = Vector2.zero;

            vertex.position = new Vector3(x, y, 0f);
            vh.AddVert(vertex);
            vertex.position = new Vector3(x, y + height, 0f);
            vh.AddVert(vertex);
            vertex.position = new Vector3(x + width, y + height, 0f);
            vh.AddVert(vertex);
            vertex.position = new Vector3(x + width, y, 0f);
            vh.AddVert(vertex);

            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index + 2, index + 1, index + 3);
        }
    }
}
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BrainshEditor
{
    /// <summary>
    /// Colours, font and widget factories. The whole interface is built from code,
    /// so the scene only needs an empty canvas.
    /// </summary>
    public static class Ui
    {
        // Interface
        public static readonly Color Background = new Color32(0x12, 0x16, 0x1C, 0xFF);
        public static readonly Color Panel = new Color32(0x1B, 0x21, 0x29, 0xFF);
        public static readonly Color PanelLight = new Color32(0x25, 0x2D, 0x38, 0xFF);
        public static readonly Color Border = new Color32(0x2E, 0x37, 0x44, 0xFF);
        public static readonly Color Accent = new Color32(0x3F, 0x8E, 0xFC, 0xFF);
        public static readonly Color Run = new Color32(0x2F, 0xA8, 0x63, 0xFF);
        public static readonly Color Stop = new Color32(0xC8, 0x45, 0x45, 0xFF);
        public static readonly Color Key = new Color32(0x2B, 0x33, 0x3F, 0xFF);
        public static readonly Color KeyDown = new Color32(0x39, 0x44, 0x54, 0xFF);
        public static readonly Color TextMain = new Color32(0xE4, 0xE9, 0xF0, 0xFF);
        public static readonly Color TextDim = new Color32(0x88, 0x94, 0xA6, 0xFF);
        public static readonly Color Error = new Color32(0xFF, 0x6B, 0x6B, 0xFF);
        public static readonly Color Good = new Color32(0x7E, 0xE0, 0xA8, 0xFF);
        public static readonly Color Caret = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        public static readonly Color Selection = new Color32(0x3F, 0x8E, 0xFC, 0x55);
        public static readonly Color CurrentLine = new Color32(0xFF, 0xFF, 0xFF, 0x10);

        // Syntax
        public static readonly Color CodePlain = new Color32(0xD6, 0xDE, 0xEA, 0xFF);
        public static readonly Color CodeComment = new Color32(0x64, 0x74, 0x88, 0xFF);
        public static readonly Color CodeMacro = new Color32(0xC2, 0x8E, 0xF0, 0xFF);
        public static readonly Color CodeKeyword = new Color32(0x6F, 0xE0, 0xB8, 0xFF);
        public static readonly Color CodeIo = new Color32(0xF5, 0xC2, 0x6B, 0xFF);
        public static readonly Color CodeMove = new Color32(0xF2, 0x77, 0x8C, 0xFF);
        public static readonly Color CodeMath = new Color32(0x9C, 0xC4, 0xF8, 0xFF);
        public static readonly Color CodeRandom = new Color32(0xE8, 0xA5, 0x62, 0xFF);
        public static readonly Color CodeNumber = new Color32(0xB6, 0xD4, 0xA0, 0xFF);
        public static readonly Color CodeLoop = new Color32(0xDA, 0x9C, 0xE4, 0xFF);
        public static readonly Color CodeInclude = new Color32(0x8F, 0xD1, 0xB0, 0xFF);
        public static readonly Color CodeString = new Color32(0xEC, 0xC4, 0x87, 0xFF);

        private static Font _font;

        /// <summary>Built-in dynamic font, available in every build.</summary>
        public static Font Font
        {
            get
            {
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : 5;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(RectTransform rect, float left = 0, float bottom = 0,
            float right = 0, float top = 0)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        public static RectTransform AtTop(RectTransform rect, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(0f, -height);
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        public static RectTransform AtBottom(RectTransform rect, float height)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = new Vector2(0f, height);
            return rect;
        }

        public static RectTransform LeftOf(RectTransform rect, float width)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = new Vector2(width, 0f);
            return rect;
        }

        /// <summary>Background of a panel: fills the parent, size it afterwards if needed.</summary>
        public static Image Box(string name, Transform parent, Color color)
        {
            RectTransform rect = Node(name, parent);
            Stretch(rect);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static Text Label(string name, Transform parent, string value, int size, Color color,
            TextAnchor anchor = TextAnchor.UpperLeft, bool wrap = false)
        {
            RectTransform rect = Node(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = size;
            text.text = value;
            text.color = color;
            text.alignment = anchor;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.lineSpacing = 1f;
            return text;
        }

        public static Button Btn(string name, Transform parent, string caption, Color color,
            UnityAction onClick, int fontSize = 18, float horizontalPadding = 6f)
        {
            Image box = Box(name, parent, color);

            // A button only reacts to touches when something under the pointer is raycastable.
            box.raycastTarget = true;

            var button = box.gameObject.AddComponent<Button>();
            button.targetGraphic = box;

            var tint = button.colors;
            tint.normalColor = Color.white;
            tint.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
            tint.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            tint.selectedColor = Color.white;
            tint.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            tint.fadeDuration = 0.05f;
            button.colors = tint;

            if (onClick != null) button.onClick.AddListener(onClick);

            Text label = Label("label", box.transform, caption, fontSize, TextMain, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform, horizontalPadding, 0, horizontalPadding, 0);
            return button;
        }

        /// <summary>Adds a vertical/horizontal layout group to a rect.</summary>
        public static VerticalLayoutGroup Column(RectTransform rect, float spacing, RectOffset padding = null)
        {
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding ?? new RectOffset(0, 0, 0, 0);
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childAlignment = TextAnchor.UpperLeft;
            return layout;
        }

        public static HorizontalLayoutGroup Row(RectTransform rect, float spacing, RectOffset padding = null)
        {
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding ?? new RectOffset(0, 0, 0, 0);
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;
            return layout;
        }

        public static LayoutElement Size(GameObject go, float width = -1f, float height = -1f)
        {
            var element = go.GetComponent<LayoutElement>();
            if (element == null) element = go.AddComponent<LayoutElement>();
            if (width >= 0f) element.preferredWidth = width;
            if (height >= 0f) element.preferredHeight = height;
            return element;
        }
    }
}
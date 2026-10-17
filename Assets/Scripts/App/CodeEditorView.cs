using System;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BrainshEditor
{
    /// <summary>
    /// The code editing surface: caret, selection, scrolling, clipboard and history.
    ///
    /// The text itself is drawn by a Text component (with rich text colours), this class
    /// owns the caret, the selection, the scrolling and the hit testing.
    ///
    /// Touch behaviour:
    ///   tap            - move the caret
    ///   drag           - scroll the text
    ///   long press     - start a selection, then drag to extend it
    /// </summary>
    [AddComponentMenu("")]
    [RequireComponent(typeof(RectTransform))]
    public class CodeEditorView : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IDragHandler, IScrollHandler
    {
        public const float LongPressSeconds = 0.5f;
        public const float DragThreshold = 8f;
        public const float ScrollStep = 60f;

        // A finger that lands and then moves is the beginning of a scroll, not the
        // beginning of a selection: this much travel right after the long press gives
        // the scroll back.
        public const float SelectToPanSeconds = 0.3f;
        public const float SelectToPanDistance = 24f;

        public RectTransform viewport;
        public RectTransform content;
        public RectTransform numbersContent;
        public Text code;
        public Text lineNumbers;
        public RectBandGraphic bands;
        public Image caret;
        public RectTransform scrollbarHandle;

        [Range(14, 48)] public int fontSize = 25;
        public float paddingX = 10f;
        public float paddingY = 8f;

        public event Action Changed;
        public event Action<int, int> CaretMoved;

        private readonly CodeBuffer _buffer = new CodeBuffer();
        private readonly StringBuilder _numbers = new StringBuilder();

        private CodeLayout _layout;
        private Font _layoutFont;
        private int _layoutSize;

        private float _scrollX;
        private float _scrollY;
        private Vector2 _lastViewportSize = new Vector2(-1f, -1f);

        private bool _pressing;
        private bool _panning;
        private float _selectTime;
        private Vector2 _selectPosition;
        private bool _selecting;
        private Vector2 _pressPosition;
        private Vector2 _lastDragPosition;
        private float _pressTime;

        private float _blinkTimer;
        private bool _caretVisible = true;
        private bool _draggingHandle;

        public CodeBuffer Buffer
        {
            get { return _buffer; }
        }

        public string Text
        {
            get { return _buffer.Text; }
        }

        public float ScrollY
        {
            get { return _scrollY; }
        }

        /// <summary>Which gesture a finger is in right now: dragging the text or selecting.</summary>
        public string Gesture
        {
            get { return _panning ? "pan" : _selecting ? "select" : _pressing ? "press" : "none"; }
        }

        private void Awake()
        {
            _buffer.Changed += OnBufferChanged;
        }

        private void OnDestroy()
        {
            _buffer.Changed -= OnBufferChanged;
        }

        private void Update()
        {
            if (_pressing && !_panning && !_selecting && Time.unscaledTime - _pressTime > LongPressSeconds)
                BeginSelection();

            _blinkTimer += Time.unscaledDeltaTime;
            if (_blinkTimer >= 0.55f)
            {
                _blinkTimer = 0f;
                _caretVisible = !_caretVisible;

                if (caret != null) caret.enabled = _caretVisible || _buffer.HasSelection;
            }
        }

        public void SetText(string text)
        {
            _buffer.SetText(text, 0);
            _scrollX = 0f;
            _scrollY = 0f;

            Refresh();
            ScrollCaretIntoView();
        }

        public string SelectedText()
        {
            return _buffer.SelectedText();
        }

        // ---------------------------------------------------------------- text input

        public void OnKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            switch (key)
            {
                case "Enter":
                    InsertNewLine();
                    break;
                case "Tab":
                    Insert("    ");
                    break;
                case "Back":
                    _buffer.Backspace();
                    break;
                case "Del":
                    _buffer.DeleteForward();
                    break;
                case "Left":
                    _buffer.MoveBy(-1, false);
                    break;
                case "Right":
                    _buffer.MoveBy(1, false);
                    break;
                case "Up":
                    _buffer.MoveLine(-1, false);
                    break;
                case "Down":
                    _buffer.MoveLine(1, false);
                    break;
                case "Home":
                    _buffer.MoveCaret(_buffer.StartOfLine(_buffer.LineOf(_buffer.Caret)), false);
                    break;
                case "End":
                    _buffer.MoveCaret(_buffer.EndOfLine(_buffer.LineOf(_buffer.Caret)), false);
                    break;
                case "Undo":
                    _buffer.Undo();
                    break;
                case "Redo":
                    _buffer.Redo();
                    break;
                case "Copy":
                    CopySelection();
                    break;
                case "Cut":
                    CutSelection();
                    break;
                case "Paste":
                    PasteClipboard();
                    break;
                case "SelectAll":
                    _buffer.SelectAll();
                    break;
                default:
                    Insert(key);
                    break;
            }

            ScrollCaretIntoView();
        }

        public void Insert(string value)
        {
            if (string.IsNullOrEmpty(value)) return;

            _buffer.Insert(value);
            ScrollCaretIntoView();
        }

        private void InsertNewLine()
        {
            // Keep the indentation of the current line, aligned code is easier to read.
            int caret = _buffer.Caret;
            int lineStart = _buffer.StartOfLine(_buffer.LineOf(caret));

            string indent = string.Empty;
            for (int i = lineStart; i < caret; i++)
            {
                char c = _buffer.Text[i];
                if (c != ' ' && c != '\t') break;
                indent += c;
            }

            _buffer.Insert("\n" + indent);
        }

        public void CopySelection()
        {
            string selected = _buffer.SelectedText();
            if (selected.Length > 0) GUIUtility.systemCopyBuffer = selected;
        }

        public void CutSelection()
        {
            CopySelection();
            _buffer.DeleteSelection();
        }

        public void PasteClipboard()
        {
            string clipboard = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(clipboard)) return;

            clipboard = clipboard.Replace("\r\n", "\n");
            _buffer.ReplaceSelection(clipboard);
            ScrollCaretIntoView();
        }

        // ---------------------------------------------------------------- pointer

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_draggingHandle) return;

            _pressing = true;
            _panning = false;
            _selecting = false;
            _pressPosition = eventData.position;
            _lastDragPosition = eventData.position;
            _pressTime = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_pressing) return;

            if (!_panning && !_selecting)
            {
                int index = IndexAtScreen(eventData.position);

                if (eventData.clickCount >= 2) SelectWordAt(index);
                else _buffer.Caret = index;

                ScrollCaretIntoView();
            }

            _pressing = false;
            _panning = false;
            _selecting = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_pressing) return;

            if (!_panning && !_selecting &&
                (eventData.position - _pressPosition).magnitude > DragThreshold)
            {
                _panning = true;
            }

            // The long press can fire in the moment a scroll starts: the thumb lands,
            // waits a fraction of a second and then runs. Whoever moves far right after
            // it wanted to scroll, so the scroll wins and the selection is dropped.
            if (_selecting && !_panning &&
                Time.unscaledTime - _selectTime < SelectToPanSeconds &&
                (eventData.position - _selectPosition).magnitude > SelectToPanDistance * CanvasScale())
            {
                _selecting = false;
                _panning = true;
                _lastDragPosition = eventData.position;
            }

            if (_panning)
            {
                // The text follows the finger, like a page under the thumb: a finger
                // going up shows the lines below, so the offset grows.
                Vector2 delta = eventData.position - _lastDragPosition;
                float scale = CanvasScale();

                _scrollX += delta.x / scale;
                _scrollY += delta.y / scale;

                ClampScroll();
                Refresh();
            }
            else if (_selecting)
            {
                _buffer.MoveCaret(IndexAtScreen(eventData.position), true);
                ScrollCaretIntoView();
            }

            _lastDragPosition = eventData.position;
        }

        /// <summary>
        /// Mouse wheel. scrollDelta.y is positive when the wheel goes up, and up means
        /// "show the text above", so the offset must decrease.
        /// </summary>
        public void OnScroll(PointerEventData eventData)
        {
            _scrollY -= eventData.scrollDelta.y * ScrollStep;
            ClampScroll();
            Refresh();
        }

        /// <summary>Drag handler for the scroll bar handle.</summary>
        public void OnScrollbarDown(PointerEventData eventData)
        {
            _draggingHandle = true;
            JumpToHandle(eventData);
        }

        public void OnScrollbarDrag(PointerEventData eventData)
        {
            if (_draggingHandle) JumpToHandle(eventData);
        }

        public void OnScrollbarUp(PointerEventData eventData)
        {
            _draggingHandle = false;
        }

        private void JumpToHandle(PointerEventData eventData)
        {
            var track = scrollbarHandle.parent as RectTransform;
            if (track == null) return;

            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(track, eventData.position,
                    CanvasCamera(), out local)) return;

            float trackHeight = track.rect.height;
            float handleHeight = scrollbarHandle.rect.height;
            if (trackHeight <= handleHeight || handleHeight <= 0f) return;

            float usable = trackHeight - handleHeight;
            float t = Mathf.Clamp01((trackHeight * 0.5f - local.y - handleHeight * 0.5f) / usable);

            _scrollY = t * MaxScrollY();
            Refresh();
        }

        /// <summary>Double click: selects the whole word, like every other editor.</summary>
        private void SelectWordAt(int index)
        {
            string text = _buffer.Text;
            if (text.Length == 0) return;

            index = Mathf.Clamp(index, 0, text.Length - 1);

            if (IsSpace(text[index]))
            {
                _buffer.Caret = index;
                return;
            }

            int start = index;
            int end = index + 1;

            while (start > 0 && !IsSpace(text[start - 1])) start--;
            while (end < text.Length && !IsSpace(text[end])) end++;

            _buffer.Anchor = start;
            _buffer.MoveCaret(end, true);
        }

        private static bool IsSpace(char c)
        {
            return c == ' ' || c == '\t' || c == '\n' || c == '\r';
        }

        public int Caret
        {
            get { return _buffer.Caret; }
        }

        public float LayoutLineHeight
        {
            get { EnsureLayout(); return _layout.LineHeight; }
        }

        /// <summary>Width the layout believes the text has.</summary>
        public float XOfIndex(int index)
        {
            EnsureLayout();
            return _layout.XOf(index);
        }

        public float LayoutWidth
        {
            get { EnsureLayout(); return _layout.Width; }
        }

        /// <summary>
        /// How far the calculated width differs from the width Unity really draws.
        /// Anything but "about zero" means the caret drifts away from the text.
        /// </summary>
        public string MeasureDrift()
        {
            EnsureLayout();

            if (code == null) return "no text component";

            float mine = _layout.Width;
            float real = code.preferredWidth;

            return "layoutWidth=" + mine.ToString("0.0") +
                   " textWidth=" + real.ToString("0.0") +
                   " drift=" + (mine - real).ToString("0.0");
        }

        private void BeginSelection()
        {
            _selecting = true;
            _selectTime = Time.unscaledTime;
            _selectPosition = _pressPosition;

            int index = IndexAtScreen(_pressPosition);
            _buffer.MoveCaret(index, false);
        }

        // ---------------------------------------------------------------- layout

        private void OnBufferChanged()
        {
            Refresh();

            if (Changed != null) Changed();
        }

        public void Refresh()
        {
            if (code == null || viewport == null) return;

            EnsureLayout();

            string text = _buffer.Text;

            code.text = BrainshMarkup.ToRichText(text, BrainshHighlighter.Colors(text));
            SyncLineHeight();
            RebuildLineNumbers();

            if (bands != null)
            {
                bands.layout = _layout;
                bands.caret = _buffer.Caret;
                bands.anchor = _buffer.Anchor;
                bands.paddingX = paddingX;
                bands.paddingY = paddingY;
                bands.contentWidth = Mathf.Max(1f, viewport.rect.width);
                bands.SetVerticesDirty();
            }

            UpdateScrollbar();
            NotifyCaret();

            _caretVisible = true;
            _blinkTimer = 0f;
            if (caret != null) caret.enabled = true;
        }

        /// <summary>
        /// Layout for the current text. It is rebuilt on every call: the metrics are used
        /// by hit testing and by the caret, and stale ones put the caret on a wrong spot.
        /// One pass over the text is cheap, a script of a few kilobytes is nothing.
        /// </summary>
        private void EnsureLayout()
        {
            Font font = code != null ? code.font : null;
            int size = code != null ? code.fontSize : fontSize;

            if (_layout == null || _layoutFont != font || _layoutSize != size)
            {
                _layout = new CodeLayout(font, size, code != null ? code.fontStyle : FontStyle.Normal);
                _layoutFont = font;
                _layoutSize = size;
            }

            _layout.Rebuild(_buffer.Text);
        }

        /// <summary>Copies the row pitch of the drawn text into the layout.</summary>
        private void SyncLineHeight()
        {
            if (_layout == null || code == null) return;

            TextGenerator generator = code.cachedTextGenerator;
            if (generator == null || generator.lineCount < 2 || generator.lines == null) return;

            float pitch = generator.lines[1].topY - generator.lines[0].topY;
            if (pitch > 0f) _layout.SetLineHeight(pitch);
        }

        private void RebuildLineNumbers()
        {
            if (lineNumbers == null) return;

            _numbers.Length = 0;

            int count = _layout.LineCount;
            for (int i = 0; i < count; i++)
            {
                if (i > 0) _numbers.Append('\n');
                _numbers.Append(i + 1);
            }

            lineNumbers.text = _numbers.ToString();
        }

        public void ScrollCaretIntoView()
        {
            if (code == null || viewport == null) return;

            EnsureLayout();

            float lineHeight = Mathf.Max(1f, _layout.LineHeight);
            float viewHeight = Mathf.Max(1f, viewport.rect.height);
            float viewWidth = Mathf.Max(1f, viewport.rect.width);

            int line = _layout.LineOf(_buffer.Caret);
            float lineTop = line * lineHeight;
            float lineBottom = lineTop + lineHeight;

            if (lineTop < _scrollY) _scrollY = lineTop;
            else if (lineBottom > _scrollY + viewHeight) _scrollY = lineBottom - viewHeight;

            float caretX = _layout.XOf(_buffer.Caret) + paddingX;
            const float caretWidth = 8f;

            if (caretX < _scrollX) _scrollX = caretX - 8f;
            else if (caretX + caretWidth > _scrollX + viewWidth) _scrollX = caretX + caretWidth - viewWidth;

            ClampScroll();
            Refresh();
        }

        private void ClampScroll()
        {
            if (_layout == null) return;

            float maxX = Mathf.Max(0f, _layout.Width + paddingX * 2 - viewport.rect.width);
            float maxY = Mathf.Max(0f, _layout.Height + paddingY * 2 - viewport.rect.height);

            _scrollX = Mathf.Clamp(_scrollX, 0f, maxX);
            _scrollY = Mathf.Clamp(_scrollY, 0f, maxY);
        }

        private float MaxScrollY()
        {
            if (_layout == null) return 0f;

            return Mathf.Max(0f, _layout.Height + paddingY * 2 - viewport.rect.height);
        }

        private void ApplyScroll()
        {
            if (_layout == null || content == null) return;

            float width = Mathf.Max(_layout.Width + paddingX * 2, viewport.rect.width);
            float height = Mathf.Max(_layout.Height + paddingY * 2, viewport.rect.height);

            // Both transforms hang from the top left corner of the viewport, so a larger
            // scroll offset has to raise anchoredPosition, not lower it: hiding rows above
            // moves the text up, where y grows upwards, but hiding columns on the left
            // moves it left, where x shrinks. With a minus sign on y the text walked
            // down the screen instead of up.
            content.sizeDelta = new Vector2(width, height);
            content.anchoredPosition = new Vector2(-_scrollX, _scrollY);

            if (numbersContent != null)
            {
                numbersContent.sizeDelta = new Vector2(numbersContent.sizeDelta.x, height);
                numbersContent.anchoredPosition = new Vector2(0f, _scrollY);
            }

            if (caret != null)
            {
                int index = _buffer.Caret;
                int line = _layout.LineOf(index);

                caret.rectTransform.anchoredPosition =
                    new Vector2(paddingX + _layout.XOf(index), -(paddingY + line * _layout.LineHeight));
                caret.rectTransform.sizeDelta = new Vector2(3f, _layout.LineHeight);
            }
        }

        private void UpdateScrollbar()
        {
            if (scrollbarHandle == null || _layout == null) return;

            var track = scrollbarHandle.parent as RectTransform;
            if (track == null) return;

            float trackHeight = track.rect.height;
            float maxY = MaxScrollY();

            if (maxY <= 0.01f)
            {
                scrollbarHandle.gameObject.SetActive(false);
                return;
            }

            scrollbarHandle.gameObject.SetActive(true);

            float contentHeight = _layout.Height + paddingY * 2;
            float ratio = Mathf.Clamp01(trackHeight / Mathf.Max(1f, contentHeight));
            float handleHeight = Mathf.Max(24f, trackHeight * ratio);

            scrollbarHandle.sizeDelta = new Vector2(scrollbarHandle.sizeDelta.x, handleHeight);

            float t = _scrollY / maxY;
            scrollbarHandle.anchoredPosition = new Vector2(0f,
                (trackHeight - handleHeight) * 0.5f - t * (trackHeight - handleHeight));
        }

        private void NotifyCaret()
        {
            if (CaretMoved == null) return;

            CaretMoved(_buffer.LineOf(_buffer.Caret) + 1, _buffer.ColumnOf(_buffer.Caret) + 1);
        }

        /// <summary>
        /// Character under a screen point.
        /// Local coordinates start at the centre of the rect and grow upwards,
        /// the text is laid out from the top left, so both axes have to be converted.
        /// </summary>
        private int IndexAtScreen(Vector2 screenPosition)
        {
            EnsureLayout();

            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, screenPosition,
                    CanvasCamera(), out local)) return _buffer.Caret;

            Vector2 text = TextCoordinates(local);
            return _layout.IndexAt(text.x, text.y);
        }

        /// <summary>
        /// Converts a point of the viewport (origin at its pivot, y upwards)
        /// into text coordinates (origin at the top left, y downwards).
        /// </summary>
        private Vector2 TextCoordinates(Vector2 local)
        {
            Rect area = viewport.rect;

            return new Vector2(
                local.x + area.width * 0.5f - paddingX + _scrollX,
                (area.height * 0.5f - local.y) - paddingY + _scrollY);
        }

        /// <summary>The inverse of <see cref="TextCoordinates"/>.</summary>
        private Vector2 ViewportCoordinates(Vector2 text)
        {
            Rect area = viewport.rect;

            return new Vector2(
                text.x - area.width * 0.5f + paddingX - _scrollX,
                area.height * 0.5f - (text.y + paddingY - _scrollY));
        }

        /// <summary>Geometry of the scrollable area, to see why scrolling stops short.</summary>
        public string DescribeScrolling()
        {
            EnsureLayout();

            Canvas canvas = Canvas();

            Rect view = viewport.rect;
            Rect contentRect = content.rect;
            Rect codeRect = code != null ? code.rectTransform.rect : new Rect();

            return "строк=" + _layout.LineCount +
                   " высотаСтроки=" + _layout.LineHeight.ToString("0.0") +
                   " высотаТекста=" + _layout.Height.ToString("0") +
                   " текстРисует=" + (code != null ? code.preferredHeight.ToString("0") : "-") +
                   " viewport=" + view.height.ToString("0") +
                   " прокрутка=" + _scrollY.ToString("0") +
                   " максимум=" + MaxScrollY().ToString("0") +
                   " content=" + contentRect.height.ToString("0") +
                   " contentY=" + content.anchoredPosition.y.ToString("0") +
                   " codeRect=" + codeRect.height.ToString("0") +
                   " codeY=" + (code != null ? code.rectTransform.anchoredPosition.y.ToString("0") : "-") +
                   " масштабХолста=" + (canvas != null ? canvas.scaleFactor.ToString("0.00") : "-");
        }

        /// <summary>Prints every step of the screen to character conversion.</summary>
        public string DescribeHit(Vector2 screenPosition)
        {
            EnsureLayout();

            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, screenPosition,
                CanvasCamera(), out local);

            Rect area = viewport.rect;
            Vector2 text = TextCoordinates(local);

            return "screen=" + screenPosition + " local=" + local + " area=" + area +
                   " scroll=" + new Vector2(_scrollX, _scrollY) +
                   " text=" + text +
                   " lineHeight=" + _layout.LineHeight +
                   " lines=" + _layout.LineCount +
                   " index=" + _layout.IndexAt(text.x, text.y);
        }

        /// <summary>Screen position of a character, the inverse of <see cref="IndexAtScreen"/>.</summary>
        public Vector2 ScreenPositionOf(int index)
        {
            EnsureLayout();

            int line = _layout.LineOf(index);

            Vector2 text = new Vector2(_layout.XOf(index), line * _layout.LineHeight + _layout.LineHeight * 0.5f);
            Vector2 local = ViewportCoordinates(text);

            Vector3 world = viewport.TransformPoint(new Vector3(local.x, local.y, 0f));
            return RectTransformUtility.WorldToScreenPoint(CanvasCamera(), world);
        }

        private Canvas Canvas()
        {
            return GetComponentInParent<Canvas>();
        }

        private Camera CanvasCamera()
        {
            Canvas canvas = Canvas();
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
            return canvas.worldCamera;
        }

        private float CanvasScale()
        {
            Canvas canvas = Canvas();
            return canvas != null ? canvas.scaleFactor : 1f;
        }

        private void LateUpdate()
        {
            // The visible area changes with rotation, the keyboard and the menus.
            Vector2 size = viewport != null ? viewport.rect.size : Vector2.zero;
            if (size != _lastViewportSize)
            {
                _lastViewportSize = size;
                Refresh();
            }

            ApplyScroll();
        }
    }
}
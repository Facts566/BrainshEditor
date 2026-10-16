using System.Collections.Generic;
using UnityEngine;

namespace BrainshEditor
{
    /// <summary>
    /// Character metrics for a block of text: where every line starts, how far each
    /// character sits from the line start, and hit testing.
    ///
    /// Everything is measured with Unity's own text generator, configured exactly like the
    /// Text component that draws the code. Measuring the font directly gives slightly
    /// different numbers, and a few percent of error is enough to move the caret away
    /// from the characters - especially in a language made of '&lt;' and '&gt;'.
    ///
    /// The editor never wraps text, long lines simply scroll.
    /// </summary>
    public sealed class CodeLayout
    {
        private readonly Font _font;
        private readonly int _size;
        private readonly FontStyle _style;
        private readonly TextGenerator _generator = new TextGenerator();
        private readonly TextGenerationSettings _settings;

        private string _text = string.Empty;
        private int[] _lineStart = { 0 };
        private int[] _lineEnd = { 0 };
        private float[] _charX = new float[1];
        private float[] _charAdvance = new float[1];

        private float _lineHeight = 16f;
        private float _fallbackAdvance = 10f;
        private bool _lineHeightMeasured;

        public CodeLayout(Font font, int size, FontStyle style = FontStyle.Normal)
        {
            _font = font;
            _size = Mathf.Max(1, size);
            _style = style;

            _settings = new TextGenerationSettings
            {
                font = _font,
                color = Color.white,
                fontSize = _size,
                lineSpacing = 1f,
                richText = true,
                scaleFactor = 1f,
                fontStyle = _style,
                textAnchor = TextAnchor.UpperLeft,
                alignByGeometry = false,
                resizeTextForBestFit = false,
                resizeTextMinSize = 10,
                resizeTextMaxSize = 40,
                updateBounds = false,
                verticalOverflow = VerticalWrapMode.Overflow,
                horizontalOverflow = HorizontalWrapMode.Overflow,
                generationExtents = new Vector2(65536f, 65536f),
                pivot = Vector2.zero,
                generateOutOfBounds = true
            };

            Measure();
        }

        public float LineHeight
        {
            get { return _lineHeight; }
        }

        public int LineCount
        {
            get { return _lineStart.Length; }
        }

        public float Width { get; private set; }

        public float Height
        {
            get { return LineCount * _lineHeight; }
        }

        /// <summary>
        /// Takes the row pitch of the Text component. The measurement of the generator is
        /// used on its own, this only aligns it with the component that really draws.
        /// The value is frozen: a pitch that changes while the user clicks breaks the
        /// round trip between the caret and the pointer.
        /// </summary>
        public void SetLineHeight(float height)
        {
            if (_lineHeightMeasured || height <= 0f) return;

            _lineHeight = height;
            _lineHeightMeasured = true;
        }

        public void Rebuild(string text)
        {
            _text = text ?? string.Empty;

            SplitLines();
            Measure();
        }

        public int StartOfLine(int line)
        {
            line = Mathf.Clamp(line, 0, _lineStart.Length - 1);
            return _lineStart[line];
        }

        public int EndOfLine(int line)
        {
            line = Mathf.Clamp(line, 0, _lineEnd.Length - 1);
            return _lineEnd[line];
        }

        public int LineOf(int index)
        {
            if (index <= 0) return 0;

            int low = 0;
            int high = _lineStart.Length - 1;

            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (_lineStart[middle] <= index) low = middle;
                else high = middle - 1;
            }

            return low;
        }

        /// <summary>Distance from the start of the line to the character at <paramref name="index"/>.</summary>
        public float XOf(int index)
        {
            if (index <= 0) return 0f;
            if (index >= _charX.Length) return _charX.Length > 0 ? _charX[_charX.Length - 1] : 0f;

            return _charX[index];
        }

        public float IndentOf(int line)
        {
            int start = StartOfLine(line);
            int end = EndOfLine(line);

            float width = 0f;
            for (int i = start; i < end; i++)
            {
                char c = _text[i];
                if (c != ' ' && c != '\t') break;
                width += AdvanceAt(i);
            }

            return width;
        }

        /// <summary>Character index at a position inside the text, top left is (0, 0).</summary>
        public int IndexAt(float x, float y)
        {
            // A point exactly on the upper edge of a row belongs to that row, not to the
            // previous one: without the epsilon the rounding noise decides where the caret goes.
            int line = Mathf.FloorToInt((y + 0.01f) / _lineHeight);
            line = Mathf.Clamp(line, 0, LineCount - 1);

            int start = _lineStart[line];
            int end = _lineEnd[line];

            if (x <= 0f) return start;
            if (end - start <= 0) return end;

            for (int i = start; i < end; i++)
            {
                float left = _charX[i];
                float right = left + AdvanceAt(i);
                if (x < right) return i + (x - left > (right - left) * 0.5f ? 1 : 0);
            }

            return end;
        }

        private void SplitLines()
        {
            var starts = new List<int>();
            var ends = new List<int>();

            starts.Add(0);

            for (int i = 0; i < _text.Length; i++)
            {
                if (_text[i] != '\n') continue;

                ends.Add(i);
                starts.Add(i + 1);
            }

            ends.Add(_text.Length);

            _lineStart = starts.ToArray();
            _lineEnd = ends.ToArray();
        }

        /// <summary>
        /// Fills the character positions from the text generator: it reports the pen
        /// position of every character, which is exactly where the Text component puts it.
        /// </summary>
        private void Measure()
        {
            if (_font == null || _text.Length == 0)
            {
                _charX = new float[_text.Length + 1];
                _charAdvance = new float[_text.Length + 1];
                Width = 0f;
                return;
            }

            _font.RequestCharactersInTexture(_text, _size, _style);
            _generator.PopulateWithErrors(_text, _settings, null);

            UICharInfo[] characters = _generator.GetCharactersArray();
            UILineInfo[] lines = _generator.GetLinesArray();

            bool usable = characters != null && characters.Length >= _text.Length;

            _charX = new float[_text.Length + 1];
            _charAdvance = new float[_text.Length + 1];

            float widest = 0f;

            for (int line = 0; line < _lineStart.Length; line++)
            {
                int start = _lineStart[line];
                int end = _lineEnd[line];

                float origin = usable ? characters[start].cursorPos.x : 0f;
                float pen = 0f;

                for (int i = start; i < end; i++)
                {
                    if (usable)
                    {
                        _charX[i] = characters[i].cursorPos.x - origin;
                        _charAdvance[i] = PenAfter(characters, i) - characters[i].cursorPos.x;
                        if (_charAdvance[i] <= 0f) _charAdvance[i] = FontAdvance(_text[i]);
                    }
                    else
                    {
                        _charX[i] = pen;
                        _charAdvance[i] = FontAdvance(_text[i]);
                        pen += _charAdvance[i];
                    }
                }

                // The position after the last character is the end of the line. It is not a
                // character of the text, but the caret stands there and the click lands there,
                // so it needs a real x - otherwise the caret jumps to the start of the line.
                if (end < _charX.Length)
                    _charX[end] = end > start ? _charX[end - 1] + _charAdvance[end - 1] : 0f;

                float lineWidth = _charX[end];
                if (lineWidth > widest) widest = lineWidth;
            }

            Width = widest;
            _fallbackAdvance = FontAdvance(' ');

            if (lines != null && lines.Length > 0 && lines[0].height > 0)
            {
                _lineHeight = lines[0].height;
                _lineHeightMeasured = true;
            }
        }

        /// <summary>Pen position after a character, taken from the next one.</summary>
        private static float PenAfter(UICharInfo[] characters, int index)
        {
            if (index + 1 < characters.Length) return characters[index + 1].cursorPos.x;
            return characters[index].cursorPos.x;
        }

        /// <summary>Width of the character at this position, measured by the generator.</summary>
        private float AdvanceAt(int index)
        {
            if (index < 0 || index >= _charAdvance.Length) return _fallbackAdvance;

            float advance = _charAdvance[index];
            if (advance > 0f) return advance;

            char c = _text[index];

            // Tabs behave like four spaces.
            if (c == '\t') return _fallbackAdvance * 4f;

            return FontAdvance(c);
        }

        private float FontAdvance(char c)
        {
            CharacterInfo info;
            if (_font.GetCharacterInfo(c, out info, _size, _style)) return info.advance;
            if (_font.GetCharacterInfo('?', out info, _size, _style)) return info.advance;

            return _size * 0.5f;
        }
    }
}
using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace BrainshEditor
{
    /// <summary>
    /// Program output and the input line used by the ',' command.
    /// The output is trimmed so a runaway program cannot lock the device.
    /// </summary>
    [AddComponentMenu("")]
    public class ConsoleView : MonoBehaviour
    {
        public const int MaxLines = 400;

        public RectTransform viewport;
        public RectTransform content;
        public Text output;
        public Text inputLine;

        private readonly StringBuilder _buffer = new StringBuilder();
        private int _lines;
        private int _trimmed;
        private bool _awaitingInput;

        public bool AwaitingInput
        {
            get { return _awaitingInput; }
        }

        public string InputBuffer { get; private set; }

        public event Action<string> InputSubmitted;

        public void Append(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            _buffer.Append(Sanitize(text));

            for (int i = 0; i < text.Length; i++)
                if (text[i] == '\n') _lines++;

            // Keep the last lines only, a runaway program must not grow without bound.
            while (_lines > MaxLines && _trimmed < _buffer.Length)
            {
                int cut = _buffer.ToString().IndexOf('\n', _trimmed);
                if (cut < 0) break;

                _trimmed = cut + 1;
                _lines--;
            }

            output.text = _buffer.ToString(_trimmed, _buffer.Length - _trimmed);
            FitToContent();
            ScrollToBottom();
        }

        public void Clear()
        {
            _buffer.Length = 0;
            _trimmed = 0;
            _lines = 0;
            InputBuffer = string.Empty;

            output.text = string.Empty;
            FitToContent();
            UpdateInputLine();
            ScrollToBottom();
        }

        /// <summary>
        /// Called when the page gets its real size: the wrapped height of the text
        /// depends on the width, so the scroll area is fitted again.
        /// </summary>
        public void Refresh()
        {
            FitToContent();
            ScrollToBottom();
        }

        public void SetAwaitingInput(bool value)
        {
            _awaitingInput = value;
            UpdateInputLine();
        }

        /// <summary>Handles a key while the program waits for input.</summary>
        public void OnKey(string key)
        {
            if (!_awaitingInput) return;

            switch (key)
            {
                case "Enter":
                {
                    Action<string> handler = InputSubmitted;
                    if (handler != null) handler(InputBuffer);

                    InputBuffer = string.Empty;
                    UpdateInputLine();
                    break;
                }

                case "Back":
                    if (InputBuffer.Length > 0) InputBuffer = InputBuffer.Substring(0, InputBuffer.Length - 1);
                    UpdateInputLine();
                    break;

                case "Del":
                    break;

                case "Left":
                case "Right":
                case "Up":
                case "Down":
                case "Tab":
                    break;

                default:
                    InputBuffer += key;
                    UpdateInputLine();
                    break;
            }
        }

        private void UpdateInputLine()
        {
            if (inputLine == null) return;

            if (!_awaitingInput)
            {
                inputLine.text = string.Empty;
                return;
            }

            inputLine.text = "in> " + InputBuffer;
        }

        /// <summary>Grows the scrollable area to the height of the text.</summary>
        private void FitToContent()
        {
            if (output == null || content == null) return;

            float height = output.preferredHeight;
            content.sizeDelta = new Vector2(0f, Mathf.Max(height, 0f));
        }

        private void ScrollToBottom()
        {
            if (viewport == null || content == null) return;

            float overflow = content.rect.height - viewport.rect.height;
            float target = Mathf.Min(0f, -Mathf.Max(0f, overflow));

            content.anchoredPosition = new Vector2(0f, target);
        }

        /// <summary>Replaces control characters the font cannot draw.</summary>
        private static string Sanitize(string text)
        {
            var sb = new StringBuilder(text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n' || c == '\t' || c >= 32) sb.Append(c);
                else if (c == '\r') continue;
                else sb.Append('.');
            }

            return sb.ToString();
        }
    }
}
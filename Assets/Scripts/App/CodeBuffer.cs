using System;
using System.Collections.Generic;
using System.Text;

namespace BrainshEditor
{
    /// <summary>
    /// Editable text with a caret, a selection and undo history.
    /// Plain C# so it can be reasoned about (and tested) without Unity.
    /// </summary>
    public sealed class CodeBuffer
    {
        private readonly StringBuilder _text = new StringBuilder();
        private readonly List<State> _undo = new List<State>();
        private readonly List<State> _redo = new List<State>();

        private int _caret;
        private int _anchor;

        /// <summary>Merges consecutive single character edits into one undo step.</summary>
        public int UndoMergeDelay = 600;

        private struct State
        {
            public string Text;
            public int Caret;
            public int Anchor;
        }

        private int _lastEditTime;
        private bool _lastEditWasTyping;

        public event Action Changed;

        public int Caret
        {
            get { return _caret; }
            set
            {
                _caret = Clamp(value);
                _anchor = _caret;
            }
        }

        public int Anchor
        {
            get { return _anchor; }
            set { _anchor = Clamp(value); }
        }

        public string Text
        {
            get { return _text.ToString(); }
            set { SetText(value, 0); }
        }

        public int Length
        {
            get { return _text.Length; }
        }

        public bool HasSelection
        {
            get { return _anchor != _caret; }
        }

        public int SelectionStart
        {
            get { return Math.Min(_anchor, _caret); }
        }

        public int SelectionEnd
        {
            get { return Math.Max(_anchor, _caret); }
        }

        public int LineCount
        {
            get
            {
                int lines = 1;
                for (int i = 0; i < _text.Length; i++)
                    if (_text[i] == '\n') lines++;

                return lines;
            }
        }

        public void SetText(string text, int caret = 0)
        {
            _undo.Clear();
            _redo.Clear();

            _text.Length = 0;
            _text.Append(text ?? string.Empty);

            _caret = Clamp(caret);
            _anchor = _caret;
            _lastEditWasTyping = false;

            Raise();
        }

        public void Insert(string value)
        {
            if (string.IsNullOrEmpty(value)) return;

            PushHistory("typing");
            DeleteSelectionInternal();

            _text.Insert(_caret, value);
            _caret += value.Length;
            _anchor = _caret;

            Raise();
        }

        public void DeleteSelection()
        {
            if (!HasSelection) return;

            PushHistory("typing");
            DeleteSelectionInternal();
            Raise();
        }

        public void Backspace()
        {
            if (HasSelection)
            {
                PushHistory("typing");
                DeleteSelectionInternal();
                Raise();
                return;
            }

            if (_caret == 0) return;

            PushHistory("typing");
            _text.Remove(_caret - 1, 1);
            _caret--;
            _anchor = _caret;

            Raise();
        }

        public void DeleteForward()
        {
            if (HasSelection)
            {
                PushHistory("typing");
                DeleteSelectionInternal();
                Raise();
                return;
            }

            if (_caret >= _text.Length) return;

            PushHistory("typing");
            _text.Remove(_caret, 1);
            _anchor = _caret;

            Raise();
        }

        public void MoveCaret(int position, bool extend)
        {
            _caret = Clamp(position);
            if (!extend) _anchor = _caret;
            Raise();
        }

        public void MoveBy(int offset, bool extend)
        {
            MoveCaret(_caret + offset, extend);
        }

        public void MoveLine(int direction, bool extend)
        {
            int line = LineOf(_caret);
            int column = _caret - StartOfLine(line);

            int target = line + direction;
            if (target < 0)
            {
                MoveCaret(0, extend);
                return;
            }

            if (target >= LineCount)
            {
                MoveCaret(_text.Length, extend);
                return;
            }

            int start = StartOfLine(target);
            int end = EndOfLine(target);
            MoveCaret(Math.Min(start + column, end), extend);
        }

        public void MoveWord(int direction, bool extend)
        {
            int position = _caret;

            if (direction < 0)
            {
                while (position > 0 && IsSpace(_text[position - 1])) position--;
                while (position > 0 && !IsSpace(_text[position - 1])) position--;
            }
            else
            {
                while (position < _text.Length && !IsSpace(_text[position])) position++;
                while (position < _text.Length && IsSpace(_text[position])) position++;
            }

            MoveCaret(position, extend);
        }

        public void SelectAll()
        {
            _anchor = 0;
            _caret = _text.Length;
            Raise();
        }

        public string SelectedText()
        {
            return HasSelection ? _text.ToString(SelectionStart, SelectionEnd - SelectionStart) : string.Empty;
        }

        public void ReplaceSelection(string value)
        {
            PushHistory("typing");
            DeleteSelectionInternal();
            _text.Insert(_caret, value ?? string.Empty);
            _caret += (value ?? string.Empty).Length;
            _anchor = _caret;
            Raise();
        }

        public bool Undo()
        {
            if (_undo.Count == 0) return false;

            State current = MakeState();
            State step = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);

            _redo.Add(current);
            Restore(step);

            _lastEditWasTyping = false;
            Raise();
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0) return false;

            State current = MakeState();
            State step = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);

            _undo.Add(current);
            Restore(step);

            _lastEditWasTyping = false;
            Raise();
            return true;
        }

        public int StartOfLine(int line)
        {
            if (line <= 0) return 0;

            int seen = 0;
            for (int i = 0; i < _text.Length; i++)
            {
                if (_text[i] != '\n') continue;
                seen++;
                if (seen == line) return i + 1;
            }

            return _text.Length;
        }

        public int EndOfLine(int line)
        {
            int start = StartOfLine(line);
            for (int i = start; i < _text.Length; i++)
                if (_text[i] == '\n') return i;

            return _text.Length;
        }

        public int LineOf(int index)
        {
            index = Clamp(index);
            int line = 0;
            for (int i = 0; i < index && i < _text.Length; i++)
                if (_text[i] == '\n') line++;

            return line;
        }

        public int ColumnOf(int index)
        {
            index = Clamp(index);
            return index - StartOfLine(LineOf(index));
        }

        public string LineText(int line)
        {
            int start = StartOfLine(line);
            int end = EndOfLine(line);
            return _text.ToString(start, end - start);
        }

        private void DeleteSelectionInternal()
        {
            if (!HasSelection) return;

            _text.Remove(SelectionStart, SelectionEnd - SelectionStart);
            _caret = SelectionStart;
            _anchor = _caret;
        }

        private void PushHistory(string kind)
        {
            int now = Environment.TickCount;

            if (kind == "typing" && _lastEditWasTyping && now - _lastEditTime < UndoMergeDelay &&
                _undo.Count > 0)
            {
                _lastEditTime = now;
                return;
            }

            _undo.Add(MakeState());
            if (_undo.Count > 200) _undo.RemoveAt(0);

            _redo.Clear();
            _lastEditTime = now;
            _lastEditWasTyping = kind == "typing";
        }

        private State MakeState()
        {
            return new State { Text = _text.ToString(), Caret = _caret, Anchor = _anchor };
        }

        private void Restore(State snapshot)
        {
            _text.Length = 0;
            _text.Append(snapshot.Text);
            _caret = snapshot.Caret;
            _anchor = snapshot.Anchor;
        }

        private int Clamp(int index)
        {
            if (index < 0) return 0;
            return index > _text.Length ? _text.Length : index;
        }

        private static bool IsSpace(char c)
        {
            return c == ' ' || c == '\t' || c == '\n' || c == '\r';
        }

        private void Raise()
        {
            Action handler = Changed;
            if (handler != null) handler();
        }
    }
}
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BrainshEditor
{
    /// <summary>
    /// On screen keyboard. Brainsh needs characters that no phone keyboard shows,
    /// so the editor ships its own: symbols, digits and letters.
    ///
    /// The key buttons are a fixed pool: switching a page only changes their captions.
    /// Keys are square: the cell size is the same horizontally and vertically, so a
    /// finger gets a target that is as wide as it is tall.
    /// </summary>
    [AddComponentMenu("")]
    public class SoftKeyboard : MonoBehaviour
    {
        private const int MaxKeys = 30;
        private const int Columns = 8;
        private const int Rows = 4;

        private const float Pad = 6f;
        private const float Gap = 4f;
        private const float TabHeight = 40f;

        private static readonly string[] SymbolKeys =
        {
            "[", "]", "(", ")", "{", "}", "<", ">", "+", "-",
            ".", ",", "#", "@", "%", ":", ";", "\"", "'", "/"
        };

        private static readonly string[] NumberKeys =
        {
            "1", "2", "3", "4", "5", "6", "7", "8", "9", "0",
            "+", "-", "*", "/", "%", "=", "(", ")", "<", ">"
        };

        private static readonly string[] LetterKeys =
        {
            "q", "w", "e", "r", "t", "y", "u", "i", "o", "p",
            "a", "s", "d", "f", "g", "h", "j", "k", "l", "'",
            "z", "x", "c", "v", "b", "n", "m", ",", ".", "?"
        };

        private static readonly string[] PageNames = { "123", "SYM", "ABC" };

        private readonly string[] _captions = new string[MaxKeys];
        private readonly Button[] _keyButtons = new Button[MaxKeys];
        private readonly Text[] _keyLabels = new Text[MaxKeys];
        private readonly List<Button> _controlButtons = new List<Button>();

        private RectTransform _controls;
        private RectTransform _tabs;
        private RectTransform _gridRoot;
        private GridLayoutGroup _grid;
        private Button[] _pageButtons = new Button[3];
        private Button _shiftButton;

        private int _page = 1;
        private bool _upper;
        private float _height = 280f;

        /// <summary>Raised with the caption of the pressed key ("Enter", "Bksp", ...).</summary>
        public event Action<string> KeyPressed;

        /// <summary>Height the panel needs for the current key size.</summary>
        public float Height
        {
            get { return _height; }
        }

        public void Build(RectTransform root)
        {
            // Control row
            _controls = Ui.Node("controls", root);
            Ui.AtTop(_controls, 52f);
            _controls.offsetMin = new Vector2(Pad, -52f);
            _controls.offsetMax = new Vector2(-Pad, 0f);

            HorizontalLayoutGroup row = Ui.Row(_controls, Gap);
            row.childForceExpandWidth = false;
            row.childAlignment = TextAnchor.MiddleCenter;

            // The caption is only a label: the second argument is the command the editor gets.
            AddControl(row, "Enter", "Enter");
            AddControl(row, "Bksp", "Back");
            AddControl(row, "Left", "Left");
            AddControl(row, "Right", "Right");
            AddControl(row, "Tab", "Tab");
            AddControl(row, "Space", " ");
            _shiftButton = AddControl(row, "Shift", "Shift", ToggleShift);
            AddControl(row, "Hide", "Hide");

            // Page tabs
            _tabs = Ui.Node("tabs", root);
            Ui.AtTop(_tabs, TabHeight);
            _tabs.offsetMin = new Vector2(Pad, -(Pad + 52f + Gap + TabHeight));
            _tabs.offsetMax = new Vector2(-Pad, -(Pad + 52f + Gap));

            HorizontalLayoutGroup tabRow = Ui.Row(_tabs, Gap);
            tabRow.childForceExpandWidth = true;

            for (int i = 0; i < PageNames.Length; i++)
            {
                int index = i;
                Button button = Ui.Btn("page" + i, tabRow.transform, PageNames[i], Ui.PanelLight,
                    () => ShowPage(index), 15);

                LayoutElement element = Ui.Size(button.gameObject, -1, TabHeight);
                element.flexibleWidth = 1f;

                _pageButtons[i] = button;
            }

            // Key grid
            _gridRoot = Ui.Node("keys", root);
            _gridRoot.anchorMin = Vector2.zero;
            _gridRoot.anchorMax = Vector2.one;
            _gridRoot.offsetMin = new Vector2(Pad, Pad);
            _gridRoot.offsetMax = new Vector2(-Pad, -100f);

            _grid = _gridRoot.gameObject.AddComponent<GridLayoutGroup>();
            _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _grid.constraintCount = Columns;
            _grid.spacing = new Vector2(Gap, Gap);
            _grid.childAlignment = TextAnchor.UpperCenter;

            for (int i = 0; i < MaxKeys; i++)
            {
                int index = i;
                Button button = Ui.Btn("key" + i, _gridRoot, string.Empty, Ui.Key, () => PressKey(index), 18);
                _keyButtons[i] = button;
                _keyLabels[i] = button.GetComponentInChildren<Text>();
            }

            ShowPage(_page);
        }

        /// <summary>
        /// Fits square keys to the width of the panel and reports the height the panel
        /// needs. Safe to call every frame: the layout is only touched when the size
        /// really changes, so a call cannot fight the layout pass.
        /// </summary>
        public float Measure(float width)
        {
            if (_grid == null) return _height;

            width = Mathf.Max(240f, width);

            float cell = Mathf.Floor((width - Pad * 2f - Gap * (Columns - 1)) / Columns);
            cell = Mathf.Clamp(cell, 30f, 64f);

            var size = new Vector2(cell, cell);
            if (Vector2.Distance(_grid.cellSize, size) > 0.5f) _grid.cellSize = size;

            // Square control row: the same cell as the keys, eight equal columns.
            float controlsBottom = -(Pad + cell);
            if (_controls != null && Mathf.Abs(_controls.offsetMin.y - controlsBottom) > 0.5f)
            {
                _controls.offsetMin = new Vector2(Pad, controlsBottom);
                _controls.offsetMax = new Vector2(-Pad, -Pad);
            }

            for (int i = 0; i < _controlButtons.Count; i++)
            {
                var element = _controlButtons[i].GetComponent<LayoutElement>();
                if (element != null && Mathf.Abs(element.preferredWidth - cell) > 0.5f)
                    element.preferredWidth = cell;
            }

            float tabsTop = Pad + cell + Gap;
            if (_tabs != null && Mathf.Abs(_tabs.offsetMax.y + tabsTop) > 0.5f)
            {
                _tabs.offsetMin = new Vector2(Pad, -(tabsTop + TabHeight));
                _tabs.offsetMax = new Vector2(-Pad, -tabsTop);
            }

            float gridTop = tabsTop + TabHeight + Gap;
            if (Mathf.Abs(_gridRoot.offsetMax.y + gridTop) > 0.5f)
                _gridRoot.offsetMax = new Vector2(-Pad, -gridTop);

            // Top padding, controls, tabs, four key rows and the bottom padding.
            _height = Pad + cell + Gap + TabHeight + Gap +
                      Rows * cell + (Rows - 1) * Gap + Pad;

            return _height;
        }

        public void ShowPage(int page)
        {
            _page = Mathf.Clamp(page, 0, 2);

            for (int i = 0; i < _pageButtons.Length; i++)
            {
                if (_pageButtons[i] == null) continue;

                var image = _pageButtons[i].targetGraphic as Image;
                if (image != null) image.color = i == _page ? Ui.Accent : Ui.PanelLight;
            }

            string[] keys = _page == 0 ? NumberKeys : _page == 1 ? SymbolKeys : LetterKeys;

            for (int i = 0; i < MaxKeys; i++)
            {
                bool visible = i < keys.Length;
                if (_keyButtons[i] == null) continue;

                _keyButtons[i].gameObject.SetActive(visible);
                if (!visible) continue;

                _captions[i] = CaptionOf(keys[i]);
                if (_keyLabels[i] != null) _keyLabels[i].text = _captions[i];
            }
        }

        private string CaptionOf(string key)
        {
            if (_page == 2 && _upper) return key.ToUpperInvariant();
            return key;
        }

        private void PressKey(int index)
        {
            if (index < 0 || index >= _captions.Length) return;

            string caption = _captions[index];
            if (string.IsNullOrEmpty(caption)) return;

            Action<string> handler = KeyPressed;
            if (handler != null) handler(caption);

            // Как на телефонной клавиатуре: Shift поднимает регистр одной буквы,
            // а после неё клавиатура снова пишет маленькими.
            if (_upper && char.IsLetter(caption[0])) SetUpper(false);
        }

        private void ToggleShift()
        {
            SetUpper(!_upper);
        }

        private void SetUpper(bool on)
        {
            _upper = on;

            if (_shiftButton != null)
            {
                var image = _shiftButton.targetGraphic as Image;
                if (image != null) image.color = _upper ? Ui.Accent : Ui.PanelLight;
            }

            if (_page != 2) return;

            for (int i = 0; i < MaxKeys && i < LetterKeys.Length; i++)
            {
                if (_keyLabels[i] == null) continue;

                _captions[i] = CaptionOf(LetterKeys[i]);
                _keyLabels[i].text = _captions[i];
            }
        }

        private Button AddControl(HorizontalLayoutGroup row, string caption, string key,
            UnityEngine.Events.UnityAction action = null)
        {
            Button button = Ui.Btn(caption, row.transform, caption, Ui.PanelLight,
                action ?? (() => Key(key)), 13, 2f);

            LayoutElement element = Ui.Size(button.gameObject, 52f, 0f);
            element.flexibleWidth = 0f;

            _controlButtons.Add(button);
            return button;
        }

        private void Key(string caption)
        {
            Action<string> handler = KeyPressed;
            if (handler != null) handler(caption);
        }
    }
}

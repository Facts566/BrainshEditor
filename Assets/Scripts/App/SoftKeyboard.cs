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
    /// </summary>
    [AddComponentMenu("")]
    public class SoftKeyboard : MonoBehaviour
    {
        private const int MaxKeys = 30;

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

        private RectTransform _gridRoot;
        private GridLayoutGroup _grid;
        private Button[] _pageButtons = new Button[3];
        private Button _shiftButton;

        private int _page = 1;
        private bool _upper;

        /// <summary>Raised with the caption of the pressed key ("Enter", "Bksp", ...).</summary>
        public event Action<string> KeyPressed;

        public static float PreferredHeight
        {
            get { return 236f; }
        }

        public void Build(RectTransform root)
        {
            const float gap = 4f;
            const float pad = 6f;

            // Control row
            RectTransform controls = Ui.Node("controls", root);
            Ui.AtTop(controls, 46f);
            controls.offsetMin = new Vector2(pad, -46f);
            controls.offsetMax = new Vector2(-pad, 0f);

            HorizontalLayoutGroup row = Ui.Row(controls, gap);
            row.childForceExpandWidth = true;

            // The caption is only a label: the second argument is the command the editor gets.
            AddControl(row, "Enter", "Enter", 1.3f);
            AddControl(row, "Bksp", "Back", 1.0f);
            AddControl(row, "Left", "Left", 1.0f);
            AddControl(row, "Right", "Right", 1.0f);
            AddControl(row, "Tab", "Tab", 0.9f);
            AddControl(row, "Space", " ", 1.4f);
            _shiftButton = AddControl(row, "Shift", "Shift", 1.1f, ToggleShift);
            AddControl(row, "Hide", "Hide", 0.9f);

            // Page tabs
            RectTransform tabs = Ui.Node("tabs", root);
            Ui.AtTop(tabs, 30f);
            tabs.offsetMin = new Vector2(pad, -84f);
            tabs.offsetMax = new Vector2(-pad, -50f);

            HorizontalLayoutGroup tabRow = Ui.Row(tabs, gap);
            tabRow.childForceExpandWidth = true;

            for (int i = 0; i < PageNames.Length; i++)
            {
                int index = i;
                Button button = Ui.Btn("page" + i, tabRow.transform, PageNames[i], Ui.PanelLight,
                    () => ShowPage(index), 16);

                LayoutElement element = Ui.Size(button.gameObject, -1, 30f);
                element.flexibleWidth = 1f;

                _pageButtons[i] = button;
            }

            // Key grid
            _gridRoot = Ui.Node("keys", root);
            _gridRoot.anchorMin = Vector2.zero;
            _gridRoot.anchorMax = Vector2.one;
            _gridRoot.offsetMin = new Vector2(pad, pad);
            _gridRoot.offsetMax = new Vector2(-pad, -90f);

            _grid = _gridRoot.gameObject.AddComponent<GridLayoutGroup>();
            _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _grid.constraintCount = 10;
            _grid.spacing = new Vector2(gap, gap);
            _grid.childAlignment = TextAnchor.UpperLeft;

            for (int i = 0; i < MaxKeys; i++)
            {
                int index = i;
                Button button = Ui.Btn("key" + i, _gridRoot, string.Empty, Ui.Key, () => Key(_captions[index]), 20);
                _keyButtons[i] = button;
                _keyLabels[i] = button.GetComponentInChildren<Text>();
            }

            ShowPage(_page);
        }

        /// <summary>
        /// Fits the keys to the width of the panel. Safe to call every frame: the layout is
        /// only touched when the key size really changes.
        /// </summary>
        public void Resize()
        {
            if (_grid == null) return;

            const float gap = 4f;
            const float pad = 6f;

            // The real width of the panel: Screen.width still reports the startup size
            // while the window is being created.
            float width = _gridRoot != null && _gridRoot.rect.width > 100f
                ? _gridRoot.rect.width + pad * 2f
                : Mathf.Max(240f, Screen.width);

            float cell = Mathf.Floor((width - pad * 2f - gap * 9f) / 10f);
            if (cell <= 1f) return;

            if (Mathf.Abs(_grid.cellSize.x - cell) < 0.5f) return;

            _grid.cellSize = new Vector2(cell, 42f);
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

        private void ToggleShift()
        {
            _upper = !_upper;

            if (_shiftButton != null)
            {
                var image = _shiftButton.targetGraphic as Image;
                if (image != null) image.color = _upper ? Ui.Accent : Ui.PanelLight;
            }

            if (_page != 2) return;

            for (int i = 0; i < MaxKeys && i < LetterKeys.Length; i++)
                if (_keyLabels[i] != null) _keyLabels[i].text = CaptionOf(LetterKeys[i]);
        }

        private Button AddControl(HorizontalLayoutGroup row, string caption, string key, float weight,
            UnityEngine.Events.UnityAction action = null)
        {
            Button button = Ui.Btn(caption, row.transform, caption, Ui.PanelLight,
                action ?? (() => Key(key)), 15);

            LayoutElement element = Ui.Size(button.gameObject, -1, 40f);
            element.flexibleWidth = weight;

            return button;
        }

        private void Key(string caption)
        {
            Action<string> handler = KeyPressed;
            if (handler != null) handler(caption);
        }
    }
}
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Brainsh;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BrainshEditor
{
    /// <summary>
    /// Builds the whole interface and wires the editor, the keyboard, the file list
    /// and the runner together. Nothing has to be set up in the scene.
    /// </summary>
    [AddComponentMenu("")]
    public class AppController : MonoBehaviour
    {
        private const float ToolbarPadding = 6f;
        private const float ToolbarGap = 4f;
        private const float StatusHeight = 30f;
        private const float ConsoleHeaderHeight = 52f;
        private const float ConsoleInputHeight = 30f;
        private const float GutterWidth = 52f;
        private const float ScrollbarWidth = 10f;
        private const int EditorFontSize = 25;
        private const float CodePaddingX = 10f;
        private const float paddingY = 8f;

        private readonly List<MenuEntry> _menu = new List<MenuEntry>();
        private readonly List<Button> _toolbarButtons = new List<Button>();

        private Canvas _canvas;
        private ScriptStore _store;
        private CodeEditorView _editor;
        private SoftKeyboard _keyboard;
        private ConsoleView _console;
        private ProgramRunner _runner;
        private ConsoleIo _io;

        private RectTransform _safeArea;
        private RectTransform _toolbar;
        private RectTransform _editorArea;
        private RectTransform _consolePanel;
        private RectTransform _statusBar;
        private RectTransform _keyboardPanel;
        private Text _statusText;
        private Text _fileText;
        private Button _runButton;
        private Button _stopButton;
        private Button _showKeyboardButton;
        private Button _consoleKeyboardButton;
        private RectTransform _overlay;
        private GameObject _promptRoot;
        private InputField _promptInput;

        private float _toolbarHeight = 52f;
        private float _layoutWidth;

        /// <summary>Set once, read on every start: the size the user picked.</summary>
        private const string FontSizeKey = "brainsh.fontSize";

        // On a desktop the real keyboard is used, the on screen one only gets in the way.
        private bool _keyboardVisible = Application.isMobilePlatform;
        private bool _consoleVisible;
        private string _currentFile;
        private float _messageTimer;

        private string[] _shots;
        private int _shotIndex;
        private float _shotTimer;
        private float _shotDelay = 2.5f;
        private bool _shotStarted;
        private bool _runOnStart;
        private bool _selfTest;
        private int _testStep;
        private bool _touchTest;
        private int _touchStep;
        private int _touchShot;
        private bool _gestureStarted;
        private bool _gestureDone;
        private float _scrollBefore;
        private int _textBefore;
        private int _caretLine = 1;
        private int _caretColumn = 1;

        private sealed class MenuEntry
        {
            public string Caption;
            public Action Action;
            public bool Danger;
        }

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            ReadDebugArguments();

            Build();
        }

        private void OnDestroy()
        {
            // A debug run types into the code: it must not overwrite the script on disk.
            if (!IsDebugRun) Save();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && !IsDebugRun) Save();
        }

        /// <summary>A debug run types into the code, it must not touch the scripts on disk.</summary>
        private bool IsDebugRun
        {
            get { return _selfTest || _touchTest || _shots != null; }
        }

        // ---------------------------------------------------------------- build

        private void Build()
        {
            _store = new ScriptStore();
            Examples.Seed(_store);

            BuildCanvas();

            Image root = Ui.Box("root", _canvas.transform, Ui.Background);
            Ui.Stretch(root.rectTransform);

            _safeArea = Ui.Node("safe area", root.transform);
            Ui.Stretch(_safeArea);
            var fitter = root.gameObject.AddComponent<SafeAreaFitter>();
            fitter.target = _safeArea;

            BuildToolbar();
            BuildEditor();
            BuildConsole();
            BuildStatus();
            BuildKeyboard();

            _runner = gameObject.AddComponent<ProgramRunner>();
            _runner.OutputReady += text => _console.Append(text);
            _runner.Finished += OnProgramFinished;

            _io = new ConsoleIo();

            string start = _store.Exists("hello.bsh") ? "hello.bsh" : _store.NextName();
            LoadFile(start);

            Relayout();
        }

        private void BuildCanvas()
        {
            _canvas = FindObjectOfType<Canvas>();

            if (_canvas == null)
            {
                GameObject go = new GameObject("Canvas", typeof(Canvas));
                _canvas = go.GetComponent<Canvas>();
            }

            if (_canvas.GetComponent<GraphicRaycaster>() == null)
                _canvas.gameObject.AddComponent<GraphicRaycaster>();

            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Clicks need an EventSystem, the scene may not have one.
            if (EventSystem.current == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(_canvas.transform, false);
            }

            Ui.ApplyToCanvas(_canvas);
        }

        private void BuildToolbar()
        {
            RectTransform bar = Ui.Node("toolbar", _safeArea);
            Ui.AtTop(bar, _toolbarHeight);
            _toolbar = bar;

            Ui.Box("bar background", bar, Ui.Panel);

            HorizontalLayoutGroup row = Ui.Row(bar, ToolbarGap,
                new RectOffset((int)ToolbarPadding, (int)ToolbarPadding,
                    (int)ToolbarPadding, (int)ToolbarPadding));

            // Ширину кнопок ставит Relayout по ширине экрана: все восемь - квадраты.
            row.childForceExpandWidth = false;
            row.childAlignment = TextAnchor.MiddleCenter;

            _runButton = ToolButton(row, "Run", Ui.Run, StartProgram);
            _stopButton = ToolButton(row, "Stop", Ui.Stop, StopProgram);

            ToolButton(row, "Save", Ui.PanelLight, () => Save(true));
            ToolButton(row, "Undo", Ui.PanelLight, () => _editor.OnKey("Undo"));
            ToolButton(row, "Redo", Ui.PanelLight, () => _editor.OnKey("Redo"));
            ToolButton(row, "Files", Ui.PanelLight, ShowFiles);
            ToolButton(row, "Menu", Ui.PanelLight, ShowMainMenu);
            ToolButton(row, "Exit", Ui.Stop, QuitApp);
        }

        private Button ToolButton(HorizontalLayoutGroup row, string caption, Color color, Action action)
        {
            Button button = Ui.Btn(caption, row.transform, caption, color, () => action(), 13, 2f);
            LayoutElement element = Ui.Size(button.gameObject, -1, 0f);
            element.flexibleWidth = 0f;

            _toolbarButtons.Add(button);
            return button;
        }

        private void BuildEditor()
        {
            _editorArea = Ui.Node("editor", _safeArea);
            Ui.Stretch(_editorArea);

            // Gutter with the line numbers, scrolled together with the code
            RectTransform gutter = Ui.Node("gutter", _editorArea);
            Ui.LeftOf(gutter, GutterWidth);
            gutter.gameObject.AddComponent<RectMask2D>();

            RectTransform numbersContent = Ui.Node("numbers", gutter);
            numbersContent.anchorMin = new Vector2(0f, 1f);
            numbersContent.anchorMax = new Vector2(0f, 1f);
            numbersContent.pivot = new Vector2(0f, 1f);
            numbersContent.anchoredPosition = Vector2.zero;
            numbersContent.sizeDelta = new Vector2(GutterWidth, 100f);

            Text numbers = Ui.Label("numbers text", numbersContent, string.Empty, EditorFontSize, Ui.TextDim,
                TextAnchor.UpperRight);
            Ui.Stretch(numbers.rectTransform, 4f, paddingY, 4f, paddingY);
            numbers.supportRichText = false;

            // Code area
            RectTransform viewport = Ui.Node("viewport", _editorArea);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(GutterWidth, 0f);
            viewport.offsetMax = new Vector2(-ScrollbarWidth, 0f);

            var background = viewport.gameObject.AddComponent<Image>();
            background.color = Ui.Panel;
            background.raycastTarget = true;

            viewport.gameObject.AddComponent<RectMask2D>();

            _editor = viewport.gameObject.AddComponent<CodeEditorView>();
            _editor.viewport = viewport;
            _editor.fontSize = EditorFontSize;
            _editor.paddingY = paddingY;

            RectTransform content = Ui.Node("content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(100f, 100f);
            _editor.content = content;
            _editor.numbersContent = numbersContent;

            RectTransform bandsRect = Ui.Node("bands", content);
            Ui.Stretch(bandsRect);
            bandsRect.pivot = new Vector2(0f, 1f);

            var bands = bandsRect.gameObject.AddComponent<RectBandGraphic>();
            bands.raycastTarget = false;
            _editor.bands = bands;

            RectTransform codeRect = Ui.Node("code", content);
            Ui.Stretch(codeRect);
            codeRect.pivot = new Vector2(0f, 1f);

            Text code = Ui.Label("code text", codeRect, string.Empty, EditorFontSize, Ui.TextMain, TextAnchor.UpperLeft);
            Ui.Stretch(code.rectTransform, CodePaddingX, paddingY, 0f, paddingY);
            code.supportRichText = true;
            code.raycastTarget = false;
            _editor.code = code;

            RectTransform caretRect = Ui.Node("caret", content);
            caretRect.anchorMin = new Vector2(0f, 1f);
            caretRect.anchorMax = new Vector2(0f, 1f);
            caretRect.pivot = new Vector2(0f, 1f);
            caretRect.sizeDelta = new Vector2(3f, 22f);

            var caret = caretRect.gameObject.AddComponent<Image>();
            caret.color = Ui.Caret;
            caret.raycastTarget = false;
            _editor.caret = caret;

            // Scroll bar
            RectTransform track = Ui.Node("scrollbar", _editorArea);
            track.anchorMin = new Vector2(1f, 0f);
            track.anchorMax = new Vector2(1f, 1f);
            track.pivot = new Vector2(1f, 0.5f);
            track.offsetMin = new Vector2(-ScrollbarWidth, 0f);
            track.offsetMax = Vector2.zero;

            Ui.Box("scrollbar track", track, Ui.Panel);

            RectTransform handle = Ui.Node("handle", track);
            handle.anchorMin = new Vector2(0.5f, 0.5f);
            handle.anchorMax = new Vector2(0.5f, 0.5f);
            handle.pivot = new Vector2(0.5f, 0.5f);
            handle.sizeDelta = new Vector2(ScrollbarWidth - 2f, 60f);

            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = Ui.TextDim;

            var handleDrag = handle.gameObject.AddComponent<ScrollbarDrag>();
            handleDrag.editor = _editor;

            _editor.scrollbarHandle = handle;
            _editor.lineNumbers = numbers;

            // Клавиатуру можно убрать с экрана, но тогда нужна кнопка, чтобы вернуть её.
            _showKeyboardButton = Ui.Btn("show keyboard", _editorArea, "Kbd", Ui.PanelLight,
                ToggleKeyboard, 13, 4f);

            RectTransform fab = (RectTransform)_showKeyboardButton.transform;
            fab.anchorMin = new Vector2(1f, 0f);
            fab.anchorMax = new Vector2(1f, 0f);
            fab.pivot = new Vector2(1f, 0f);
            fab.sizeDelta = new Vector2(52f, 52f);
            fab.anchoredPosition = new Vector2(-(ScrollbarWidth + 10f), 10f);

            _editor.Changed += OnEditorChanged;
            _editor.CaretMoved += OnCaretMoved;

            // Размер шрифта, выбранный в прошлый раз: он переживает перезапуск.
            ApplyFontSize(Mathf.Clamp(PlayerPrefs.GetInt(FontSizeKey, EditorFontSize), 10, 40));
        }

        private void OnCaretMoved(int line, int column)
        {
            _caretLine = line;
            _caretColumn = column;

            if (_messageTimer <= 0f) ShowCaretPosition();
        }

        private void ShowCaretPosition()
        {
            SetStatus("Ln " + _caretLine + "  Col " + _caretColumn, Ui.TextDim);
        }

        private void OnEditorChanged()
        {
            if (_fileText != null && _currentFile != null)
                _fileText.text = _currentFile + " *";
        }

        /// <summary>
        /// Program output lives on a page of its own: it covers the editor, and the cross
        /// in its header stops the program and brings the code back.
        /// </summary>
        private void BuildConsole()
        {
            _consolePanel = Ui.Node("console", _safeArea);
            Ui.Stretch(_consolePanel);
            Ui.Box("console background", _consolePanel, Ui.Background);

            RectTransform header = Ui.Node("header", _consolePanel);
            Ui.AtTop(header, ConsoleHeaderHeight);
            Ui.Box("header background", header, Ui.Panel);

            Text title = Ui.Label("title", header, "Output", 20, Ui.TextMain, TextAnchor.MiddleLeft);
            Ui.Stretch(title.rectTransform, 14f, 0f, ConsoleHeaderHeight * 2f + 8f, 0f);

            Button close = Ui.Btn("close output", header, "×", Ui.PanelLight, CloseConsole, 24, 0f);
            RectTransform closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.sizeDelta = new Vector2(ConsoleHeaderHeight - 8f, ConsoleHeaderHeight - 8f);
            closeRect.anchoredPosition = new Vector2(-4f, -4f);

            // Ввод для ',' живёт на этой странице: клавиатуру нужно вернуть отсюда же.
            _consoleKeyboardButton = Ui.Btn("show keyboard output", header, "Kbd", Ui.PanelLight,
                ToggleKeyboard, 13, 4f);

            RectTransform kbdRect = (RectTransform)_consoleKeyboardButton.transform;
            kbdRect.anchorMin = new Vector2(1f, 1f);
            kbdRect.anchorMax = new Vector2(1f, 1f);
            kbdRect.pivot = new Vector2(1f, 1f);
            kbdRect.sizeDelta = new Vector2(ConsoleHeaderHeight - 8f, ConsoleHeaderHeight - 8f);
            kbdRect.anchoredPosition = new Vector2(-(ConsoleHeaderHeight - 8f + 8f), -4f);

            RectTransform viewport = Ui.Node("viewport", _consolePanel);
            Ui.Stretch(viewport, 10f, ConsoleInputHeight + 4f, 10f, ConsoleHeaderHeight + 2f);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = Ui.Node("content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            Text output = Ui.Label("output", content, string.Empty, 18, Ui.TextMain, TextAnchor.UpperLeft, true);
            Ui.Stretch(output.rectTransform);

            // The height is set by hand: a ContentSizeFitter is not re-evaluated
            // when only Text.text changes, and the output would stay invisible.
            Text input = Ui.Label("input line", _consolePanel, string.Empty, 18, Ui.Accent, TextAnchor.MiddleLeft);
            RectTransform inputRect = input.rectTransform;
            inputRect.anchorMin = new Vector2(0f, 0f);
            inputRect.anchorMax = new Vector2(1f, 0f);
            inputRect.pivot = new Vector2(0.5f, 0f);
            inputRect.offsetMin = new Vector2(12f, 2f);
            inputRect.offsetMax = new Vector2(-12f, ConsoleInputHeight);

            _console = _consolePanel.gameObject.AddComponent<ConsoleView>();
            _console.viewport = viewport;
            _console.content = content;
            _console.output = output;
            _console.inputLine = input;
            _console.InputSubmitted += OnInputSubmitted;
        }

        private void BuildStatus()
        {
            _statusBar = Ui.Node("status", _safeArea);
            Ui.Box("status background", _statusBar, Ui.Panel);

            _statusText = Ui.Label("status", _statusBar, string.Empty, 17, Ui.TextDim, TextAnchor.MiddleLeft);
            Ui.Stretch(_statusText.rectTransform, 10f, 0f, 0f, 0f);

            _fileText = Ui.Label("file", _statusBar, string.Empty, 17, Ui.TextDim, TextAnchor.MiddleRight);
            Ui.Stretch(_fileText.rectTransform, 0f, 0f, 10f, 0f);
        }

        private void BuildKeyboard()
        {
            _keyboardPanel = Ui.Node("keyboard", _safeArea);
            Ui.Box("keyboard background", _keyboardPanel, Ui.Panel);

            _keyboard = _keyboardPanel.gameObject.AddComponent<SoftKeyboard>();
            _keyboard.Build(_keyboardPanel);
            _keyboard.Measure(Ui.ScreenInDp.x);
            _keyboard.KeyPressed += OnKeyPressed;
        }

        // ---------------------------------------------------------------- layout

        private void Relayout()
        {
            float width = _safeArea != null && _safeArea.rect.width > 100f
                ? _safeArea.rect.width
                : Ui.ScreenInDp.x;

            _layoutWidth = width;

            // Восемь кнопок тулбара - квадраты: сторона растёт с шириной экрана,
            // но на планшете упирается в предел и строка встаёт по центру.
            float button = Mathf.Clamp(
                Mathf.Floor((width - ToolbarPadding * 2f - ToolbarGap * (8 - 1)) / 8f), 32f, 56f);

            _toolbarHeight = button + ToolbarPadding * 2f;
            Ui.AtTop(_toolbar, _toolbarHeight);

            for (int i = 0; i < _toolbarButtons.Count; i++)
            {
                var element = _toolbarButtons[i].GetComponent<LayoutElement>();
                if (element != null) element.preferredWidth = button;
            }

            float keyboardHeight = 0f;
            if (_keyboardVisible && _keyboard != null) keyboardHeight = _keyboard.Measure(width);

            SetBottom(_keyboardPanel, 0f, keyboardHeight);
            _keyboardPanel.gameObject.SetActive(_keyboardVisible);

            float bottom = keyboardHeight + StatusHeight;
            SetBottom(_statusBar, keyboardHeight, bottom);

            // Вывод открывается как отдельная страница: пока она открыта, редактор скрыт.
            _editorArea.gameObject.SetActive(!_consoleVisible);
            _consolePanel.gameObject.SetActive(_consoleVisible);

            _editorArea.offsetMin = new Vector2(0f, bottom);
            _editorArea.offsetMax = new Vector2(0f, -_toolbarHeight);

            _consolePanel.offsetMin = new Vector2(0f, bottom);
            _consolePanel.offsetMax = new Vector2(0f, -_toolbarHeight);

            if (_showKeyboardButton != null)
                _showKeyboardButton.gameObject.SetActive(!_keyboardVisible);

            if (_consoleKeyboardButton != null)
                _consoleKeyboardButton.gameObject.SetActive(!_keyboardVisible);

            // Страница получила настоящий размер: текст переносится по-новому, и
            // область прокрутки нужно подогнать. Так же и редактор после возврата.
            if (_consoleVisible)
            {
                if (_console != null) _console.Refresh();
            }
            else if (_editor != null)
            {
                _editor.Refresh();
            }
        }

        private static void SetBottom(RectTransform rect, float bottom, float top)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(0f, bottom);
            rect.offsetMax = new Vector2(0f, top);
        }

        // ---------------------------------------------------------------- files

        public void LoadFile(string name)
        {
            if (string.IsNullOrEmpty(name)) name = _store.NextName();

            _currentFile = name;
            _editor.SetText(_store.Read(name));
            _fileText.text = name;
        }

        public void Save(bool announce = false)
        {
            // One place, one rule: a run started with the debug switches never writes to disk.
            // Those runs type into the code, and the files on the phone must survive that.
            if (IsDebugRun) return;
            if (string.IsNullOrEmpty(_currentFile)) return;

            bool ok = _store.Write(_currentFile, _editor.Text);

            if (ok)
            {
                _fileText.text = _currentFile;
                if (announce) SetStatus("saved " + _currentFile, Ui.Good);
            }
            else if (announce)
            {
                SetStatus("cannot save " + _currentFile, Ui.Error);
            }
        }

        private void ShowFiles()
        {
            _menu.Clear();

            foreach (string name in _store.List())
            {
                // List() отдаёт имена без расширения, а текущий файл хранится с ним.
                string file = name + ScriptStore.Extension;

                _menu.Add(new MenuEntry { Caption = (file == _currentFile ? "> " : "   ") + file,
                    Action = () =>
                    {
                        Save();
                        LoadFile(file);
                        CloseMenu();
                    } });
            }

            _menu.Add(new MenuEntry { Caption = "-", Action = () => { } });

            _menu.Add(new MenuEntry
            {
                Caption = "New file… (type a name)",
                Action = ShowNewFilePrompt
            });

            if (!string.IsNullOrEmpty(_currentFile))
            {
                _menu.Add(new MenuEntry
                {
                    Caption = "Delete " + _currentFile,
                    Danger = true,
                    Action = () =>
                    {
                        _store.Delete(_currentFile);
                        _currentFile = null;
                        LoadFile(_store.NextName());
                        ShowFiles();
                    }
                });
            }

            ShowMenu("Scripts", _menu);
        }

        /// <summary>
        /// Asks for a file name. The editor has a keyboard of its own for Brainsh, but a
        /// name is ordinary text, so a real InputField is used: on Android it raises the
        /// phone keyboard, on a desktop the hardware one types into it.
        /// </summary>
        private void ShowNewFilePrompt()
        {
            CloseMenu();
            ClosePrompt();

            Image dim = Ui.Box("prompt dim", _safeArea, new Color(0f, 0f, 0f, 0.72f));
            Ui.Stretch(dim.rectTransform);
            dim.raycastTarget = true;
            _promptRoot = dim.gameObject;

            float safeWidth = _safeArea.rect.width > 100f ? _safeArea.rect.width : Ui.ScreenInDp.x;
            float cardWidth = Mathf.Min(440f, safeWidth - 32f);

            RectTransform card = Ui.Node("card", dim.transform);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(cardWidth, 250f);
            card.anchoredPosition = Vector2.zero;

            Ui.Box("card background", card, Ui.PanelLight);

            Text title = Ui.Label("title", card, "File name", 20, Ui.TextMain, TextAnchor.MiddleLeft);
            RectTransform titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.offsetMin = new Vector2(16f, -48f);
            titleRect.offsetMax = new Vector2(-16f, -8f);

            RectTransform field = Ui.Node("field", card);
            field.anchorMin = new Vector2(0f, 1f);
            field.anchorMax = new Vector2(1f, 1f);
            field.pivot = new Vector2(0.5f, 1f);
            field.offsetMin = new Vector2(16f, -110f);
            field.offsetMax = new Vector2(-16f, -56f);

            var background = field.gameObject.AddComponent<Image>();
            background.color = Ui.Background;
            background.raycastTarget = true;

            Text value = FieldText("text", field, Ui.TextMain, string.Empty);
            Text placeholder = FieldText("placeholder", field, Ui.TextDim, "my_program");

            _promptInput = field.gameObject.AddComponent<InputField>();
            _promptInput.textComponent = value;
            _promptInput.placeholder = placeholder;
            _promptInput.lineType = InputField.LineType.SingleLine;
            _promptInput.characterLimit = 32;
            _promptInput.caretColor = Ui.Accent;
            _promptInput.customCaretColor = true;
            _promptInput.selectionColor = Ui.Selection;
            _promptInput.text = string.Empty;
            _promptInput.onEndEdit.AddListener(OnPromptEndEdit);

            Text hint = Ui.Label("hint", card, "letters, digits, _ and -", 14, Ui.TextDim,
                TextAnchor.MiddleLeft);
            RectTransform hintRect = hint.rectTransform;
            hintRect.anchorMin = new Vector2(0f, 1f);
            hintRect.anchorMax = new Vector2(1f, 1f);
            hintRect.pivot = new Vector2(0.5f, 1f);
            hintRect.offsetMin = new Vector2(16f, -134f);
            hintRect.offsetMax = new Vector2(-16f, -112f);

            RectTransform buttons = Ui.Node("buttons", card);
            buttons.anchorMin = new Vector2(0f, 0f);
            buttons.anchorMax = new Vector2(1f, 0f);
            buttons.pivot = new Vector2(0.5f, 0f);
            buttons.offsetMin = new Vector2(16f, 16f);
            buttons.offsetMax = new Vector2(-16f, 64f);

            HorizontalLayoutGroup row = Ui.Row(buttons, 10f);
            row.childForceExpandWidth = true;

            Button cancel = Ui.Btn("cancel", buttons, "Cancel", Ui.Panel, ClosePrompt, 16);
            Ui.Size(cancel.gameObject, -1, 48f).flexibleWidth = 1f;

            Button create = Ui.Btn("create", buttons, "Create", Ui.Run, AcceptNewFile, 16);
            Ui.Size(create.gameObject, -1, 48f).flexibleWidth = 1f;

            _promptInput.Select();
            _promptInput.ActivateInputField();
        }

        private static Text FieldText(string name, RectTransform field, Color color, string value)
        {
            Text text = Ui.Label(name, field, value, 20, color, TextAnchor.MiddleLeft);
            Ui.Stretch(text.rectTransform, 12f, 0f, 12f, 0f);
            text.raycastTarget = false;
            return text;
        }

        private void AcceptNewFile()
        {
            string typed = _promptInput != null ? _promptInput.text : string.Empty;
            string name = ScriptStore.Sanitize(typed);

            if (name.EndsWith(ScriptStore.Extension, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - ScriptStore.Extension.Length);

            if (string.IsNullOrEmpty(name))
            {
                SetStatus("file name cannot be empty", Ui.Error, 3f);
                return;
            }

            ClosePrompt();

            // Текущую работу не теряем: она уходит в свой файл перед переключением.
            Save();

            string file = name + ScriptStore.Extension;
            bool existing = _store.Exists(file);

            if (!existing) _store.Write(file, string.Empty);

            LoadFile(file);
            SetStatus((existing ? "opened " : "created ") + file, Ui.Good, 3f);
        }

        /// <summary>Enter on a hardware keyboard creates the file; the buttons still work.</summary>
        private void OnPromptEndEdit(string value)
        {
            if (_promptInput == null) return;
            if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter)) return;

            AcceptNewFile();
        }

        private void ClosePrompt()
        {
            if (_promptInput != null)
            {
                _promptInput.DeactivateInputField();
                _promptInput = null;
            }

            if (_promptRoot != null)
            {
                Destroy(_promptRoot);
                _promptRoot = null;
            }
        }

        private void ShowMainMenu()
        {
            _menu.Clear();

            _menu.Add(new MenuEntry { Caption = "Save now", Action = () => { Save(true); CloseMenu(); } });
            _menu.Add(new MenuEntry { Caption = "Clear editor", Action = () => { _editor.SetText(string.Empty); CloseMenu(); } });

            foreach (Examples.Example example in Examples.All)
            {
                Examples.Example item = example;
                _menu.Add(new MenuEntry
                {
                    Caption = "Example: " + item.FileName,
                    Action = () =>
                    {
                        Save();
                        LoadFile(item.FileName);
                        CloseMenu();
                    }
                });
            }

            _menu.Add(new MenuEntry { Caption = "-", Action = () => { } });
            _menu.Add(new MenuEntry { Caption = "Steps limit: " + FormatSteps(_runner.StepLimit),
                Action = () => ShowStepLimit() });

            _menu.Add(new MenuEntry { Caption = "Font size: " + _editor.fontSize,
                Action = () => ShowFontSize() });

            _menu.Add(new MenuEntry
            {
                Caption = _keyboardVisible ? "Hide keyboard" : "Show keyboard",
                Action = () =>
                {
                    ToggleKeyboard();
                    ShowMainMenu();
                }
            });

            _menu.Add(new MenuEntry
            {
                Caption = _consoleVisible ? "Hide output" : "Show output",
                Action = () =>
                {
                    ToggleConsole();
                    ShowMainMenu();
                }
            });

            _menu.Add(new MenuEntry { Caption = "Stop program", Danger = true,
                Action = () => { StopProgram(); CloseMenu(); } });

            ShowMenu("Brainsh editor", _menu);
        }

        private void ShowStepLimit()
        {
            _menu.Clear();

            long[] limits = { 1000000L, 10000000L, 50000000L, 200000000L, 0L };

            foreach (long limit in limits)
            {
                long value = limit;
                _menu.Add(new MenuEntry
                {
                    Caption = value == 0 ? "Unlimited" : FormatSteps(value) + (value == _runner.StepLimit ? "  *" : ""),
                    Action = () =>
                    {
                        _runner.StepLimit = value;
                        ShowMainMenu();
                    }
                });
            }

            ShowMenu("Step limit", _menu);
        }

        private void ShowFontSize()
        {
            _menu.Clear();

            int[] sizes = { 16, 18, 20, 22, 26, 30 };

            foreach (int size in sizes)
            {
                int value = size;
                _menu.Add(new MenuEntry
                {
                    Caption = value + (value == _editor.fontSize ? "  *" : ""),
                    Action = () =>
                    {
                        ApplyFontSize(value);
                        PlayerPrefs.SetInt(FontSizeKey, value);
                        PlayerPrefs.Save();
                        ShowMainMenu();
                    }
                });
            }

            ShowMenu("Font size", _menu);
        }

        /// <summary>One place that changes the text size: code, line numbers and layout.</summary>
        private void ApplyFontSize(int size)
        {
            size = Mathf.Clamp(size, 10, 40);

            if (_editor == null) return;

            _editor.fontSize = size;

            if (_editor.code != null) _editor.code.fontSize = size;

            // Номера строк стоят рядом с кодом, кегль обязан совпадать, иначе
            // после смены размера номера съезжают относительно строк.
            if (_editor.lineNumbers != null) _editor.lineNumbers.fontSize = size;

            _editor.Refresh();
        }

        private Camera CanvasCamera()
        {
            if (_canvas == null || _canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
            return _canvas.worldCamera;
        }

        private static string FormatSteps(long steps)
        {
            if (steps <= 0) return "unlimited";
            if (steps >= 1000000L) return (steps / 1000000L) + "M";
            if (steps >= 1000L) return (steps / 1000L) + "K";
            return steps.ToString();
        }

        // ---------------------------------------------------------------- menu overlay

        private void ShowMenu(string title, List<MenuEntry> entries)
        {
            CloseMenu();

            Image dim = Ui.Box("menu dim", _safeArea, new Color(0f, 0f, 0f, 0.65f));
            Ui.Stretch(dim.rectTransform);
            dim.raycastTarget = true;
            dim.gameObject.AddComponent<Button>().onClick.AddListener(CloseMenu);

            _overlay = dim.rectTransform;

            RectTransform card = Ui.Node("card", dim.transform);
            card.anchorMin = new Vector2(0f, 0f);
            card.anchorMax = new Vector2(1f, 1f);
            card.offsetMin = new Vector2(18f, 48f);
            card.offsetMax = new Vector2(-18f, -48f);

            Ui.Box("card background", card, Ui.PanelLight);

            RectTransform header = Ui.Node("header", card);
            Ui.AtTop(header, 62f);
            Ui.Label("title", header, title, 20, Ui.TextMain, TextAnchor.MiddleLeft);
            Ui.Stretch(((RectTransform)header.Find("title")), 14f, 0f, 0f, 0f);

            Button close = Ui.Btn("close", header, "Close", Ui.Panel, CloseMenu, 15);
            RectTransform closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.sizeDelta = new Vector2(150f, 52f);
            closeRect.anchoredPosition = new Vector2(-10f, 0f);

            RectTransform viewport = Ui.Node("list", card);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(10f, 10f);
            viewport.offsetMax = new Vector2(-10f, -64f);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = Ui.Node("content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);

            VerticalLayoutGroup list = Ui.Column(content, 4f);
            list.childForceExpandWidth = true;
            list.childControlHeight = true;

            foreach (MenuEntry entry in entries)
            {
                if (entry.Caption == "-")
                {
                    RectTransform line = Ui.Node("line", content);
                    Ui.Size(line.gameObject, -1, 8f);
                    continue;
                }

                MenuEntry item = entry;
                Button button = Ui.Btn("entry", content, item.Caption,
                    item.Danger ? Ui.Stop : Ui.Panel, () => item.Action(), 17);

                Ui.Size(button.gameObject, -1, 60f);
            }

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;
        }

        private void CloseMenu()
        {
            if (_overlay == null) return;

            Destroy(_overlay.gameObject);
            _overlay = null;
        }

        // ---------------------------------------------------------------- input

        private void OnKeyPressed(string key)
        {
            if (key == "Hide")
            {
                ToggleKeyboard();
                return;
            }

            if (key == "Shift") return;

            if (_console != null && _console.AwaitingInput) _console.OnKey(key);
            else _editor.OnKey(key);
        }

        private void OnInputSubmitted(string line)
        {
            if (_io != null) _io.PushInput(line);
        }

        private void LateUpdate()
        {
            // The first layout pass gives the real width. When it changes (rotation,
            // a different window, a safe area on a phone) the squares are measured again.
            if (_safeArea == null) return;

            float width = _safeArea.rect.width;
            if (width > 100f && Mathf.Abs(width - _layoutWidth) > 0.5f) Relayout();
        }

        private void Update()
        {
            if (_runner != null && _runner.IsRunning && _io != null)
            {
                bool waiting = _io.WaitingForInput;
                if (waiting != _console.AwaitingInput)
                {
                    _console.SetAwaitingInput(waiting);

                    // ',' ждёт строку: на телефоне без экранной клавиатуры её не набрать.
                    if (waiting && !_keyboardVisible && Application.isMobilePlatform)
                    {
                        _keyboardVisible = true;
                        Relayout();
                    }
                }
            }

            if (_messageTimer > 0f)
            {
                _messageTimer -= Time.unscaledDeltaTime;
                if (_messageTimer <= 0f && _currentFile != null) ShowCaretPosition();
            }

            HandleKeyboard();

            if (Input.GetKeyDown(KeyCode.Escape)) HandleBack();

            RunSelfTest();
            RunDebugCapture();
        }

        /// <summary>
        /// Development helper, used to check the interface on a real player:
        ///
        ///   -brainsh-shot a.png,b.png   screenshots, one every "wait" seconds
        ///   -brainsh-run                start the program from the loaded script
        ///   -brainsh-selftest           click Run, a key, Menu and Out, and report the result
        ///   -brainsh-touch              finger gestures on a long text, with screenshots
        ///   -brainsh-wait 3             seconds between screenshots (default 2.5)
        ///   -brainsh-size 720x1280      window size
        /// </summary>
        private void ReadDebugArguments()
        {
            string[] args = Environment.GetCommandLineArgs();

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-brainsh-shot" && i + 1 < args.Length)
                {
                    _shots = args[i + 1].Split(',');
                    for (int k = 0; k < _shots.Length; k++) _shots[k] = _shots[k].Trim();
                }
                else if (args[i] == "-brainsh-run")
                {
                    _runOnStart = true;
                }
                else if (args[i] == "-brainsh-selftest")
                {
                    _selfTest = true;
                }
                else if (args[i] == "-brainsh-touch")
                {
                    _touchTest = true;
                }
                else if (args[i] == "-brainsh-wait" && i + 1 < args.Length)
                {
                    float seconds;
                    if (float.TryParse(args[i + 1], out seconds)) _shotDelay = seconds;
                }
            }
        }

        private void RunDebugCapture()
        {
            if (_shots == null || _shots.Length == 0) return;

            _shotTimer += Time.unscaledDeltaTime;

            if (!_shotStarted)
            {
                _shotStarted = true;

                if (_runOnStart) StartProgram();
            }

            // One screenshot every "delay" seconds, then leave a second to write the file.
            if (_shotIndex < _shots.Length && _shotTimer >= _shotDelay * (_shotIndex + 1))
            {
                LogLayout();
                ScreenCapture.CaptureScreenshot(_shots[_shotIndex]);
                _shotIndex++;
                return;
            }

            if (_shotIndex >= _shots.Length && _shotTimer >= _shotDelay * (_shots.Length + 1))
                Application.Quit();
        }

        /// <summary>
        /// Clicks the real buttons through the event system and checks that something
        /// happened, the same way a finger would. Used with -brainsh-selftest.
        /// </summary>
        private void RunSelfTest()
        {
            if (_touchTest)
            {
                RunTouchTest();
                return;
            }

            if (!_selfTest) return;

            _shotTimer += Time.unscaledDeltaTime;

            switch (_testStep)
            {
                case 0:
                    if (_shotTimer < 0.5f) return;

                    ClickControl("Run");
                    Debug.Log("SELFTEST 1: pressed Run");
                    _testStep = 1;
                    return;

                case 1:
                    if (_shotTimer < 1.5f) return;

                    Debug.Log("SELFTEST 2: status='" + _statusText.text + "' console='" +
                              _console.output.text.Replace("\n", "\\n") + "'");
                    _testStep = 2;
                    return;

                case 2:
                    if (_shotTimer < 2f) return;

                    // На компьютере экранная клавиатура скрыта, а кнопки Kbd в тулбаре
                    // больше нет - показываем её напрямую.
                    _keyboardVisible = true;
                    Relayout();
                    _textBefore = _editor.Text.Length;
                    _testStep = 3;
                    return;

                case 3:
                    if (_shotTimer < 2.4f) return;

                    ClickControl("key0");
                    _testStep = 4;
                    return;

                case 4:
                    if (_shotTimer < 3f) return;

                    Debug.Log("SELFTEST 3: on screen key changed the code " + _textBefore + " -> " +
                              _editor.Text.Length + (_editor.Text.Length > _textBefore ? " OK" : " MISMATCH"));

                    ClickControl("Bksp");
                    _testStep = 5;
                    return;

                case 5:
                    if (_shotTimer < 3.2f) return;

                    Debug.Log("SELFTEST 3b: Bksp restored the code to " + _editor.Text.Length);
                    _testStep = 6;
                    return;

                case 6:
                    if (_shotTimer < 3.4f) return;

                    ClickControl("Menu");
                    _testStep = 7;
                    return;

                case 7:
                    if (_shotTimer < 4f) return;

                    Debug.Log("SELFTEST 4: menu opened=" + (_overlay != null));
                    _testStep = 8;
                    return;

                case 8:
                    if (_shotTimer < 4.4f) return;

                    // Кнопки Out в тулбаре больше нет, переключаем панель напрямую.
                    ToggleConsole();
                    Debug.Log("SELFTEST 5: output panel toggled, visible=" + _consoleVisible);
                    _testStep = 9;
                    return;

                case 9:
                {
                    if (_shotTimer < 4.8f) return;

                    // Страница вывода закрывается, редактор снова на экране: кликать
                    // по скрытому полю бессмысленно.
                    if (_consoleVisible) ToggleConsole();

                    // Попадание указателя: кликаем по пятому символу пятой строки.
                    int line = 4;
                    int column = 4;
                    int target = Mathf.Min(_editor.Buffer.StartOfLine(line) + column, _editor.Text.Length);

                    Vector2 point = _editor.ScreenPositionOf(target);

                    Debug.Log("SELFTEST 6b: " + _editor.MeasureDrift());

                    Debug.Log("SELFTEST 6a: target=" + target + " rowStart=" +
                              _editor.Buffer.StartOfLine(line) + " lineHeight=" +
                              _editor.LayoutLineHeight + "\n           " + _editor.DescribeHit(point));

                    ClickAt(_editor.viewport.gameObject, point);

                    int got = _editor.Caret;
                    Debug.Log("SELFTEST 6c: end of line x=" + _editor.XOfIndex(_editor.Buffer.EndOfLine(line)) +
                              " last char x=" + _editor.XOfIndex(_editor.Buffer.EndOfLine(line) - 1) +
                              " first char x=" + _editor.XOfIndex(_editor.Buffer.StartOfLine(line)));

                    Debug.Log("SELFTEST 6: click at line " + (line + 1) + " column " + (column + 1) +
                              " -> caret " + got + " (expected " + target + ") " +
                              (got == target ? "OK" : "MISMATCH"));
                    _testStep = 10;
                    return;
                }

                case 10:
                {
                    if (_shotTimer < 5f) return;

                    // Клик в самый конец строки: каретка должна встать после последнего символа.
                    int lineEnd = 2;
                    int target = _editor.Buffer.EndOfLine(lineEnd);

                    Vector2 point = _editor.ScreenPositionOf(target);
                    ClickAt(_editor.viewport.gameObject, point);

                    int got = _editor.Caret;
                    Debug.Log("SELFTEST 7: click at the end of line " + (lineEnd + 1) +
                              " -> caret " + got + " (expected " + target + ") " +
                              (got == target ? "OK" : "MISMATCH") +
                              " x=" + _editor.XOfIndex(got) +
                              " lastCharX=" + _editor.XOfIndex(got - 1));
                    _testStep = 11;
                    return;
                }

                case 11:
                {
                    if (_shotTimer < 5.4f) return;

                    // Прокрутка колесом: вниз — код сдвигается вверх, вверх — обратно.
                    Wheel(0f, -3f);
                    float down = _editor.ScrollY;
                    Wheel(0f, 3f);
                    float back = _editor.ScrollY;

                    Debug.Log("SELFTEST 8: колесо вниз → прокрутка " + down.ToString("0") +
                              " (ожидалось больше 0), колесо вверх → " + back.ToString("0") +
                              " (ожидалось 0) " +
                              (down > 0f && back < 0.5f ? "OK" : "MISMATCH"));

                    _testStep = 12;
                    return;
                }

                case 12:
                {
                    if (_shotTimer < 5.8f) return;

                    // Прокручиваем до упора вниз и смотрим, что происходит с геометрией.
                    Wheel(0f, -40f);

                    Debug.Log("SELFTEST 9: " + _editor.DescribeScrolling());
                    _testStep = 13;
                    return;
                }

                case 13:
                    if (_shotTimer < 6.2f) return;

                    // Регистр: Shift поднимает одну букву, а следующая снова строчная.
                    _textBefore = _editor.Caret;
                    ClickControl("page2");
                    _testStep = 14;
                    return;

                case 14:
                    if (_shotTimer < 6.5f) return;

                    ClickControl("Shift");
                    ClickControl("key0");
                    _testStep = 15;
                    return;

                case 15:
                    if (_shotTimer < 6.8f) return;

                    ClickControl("key1");
                    _testStep = 16;
                    return;

                case 16:
                {
                    if (_shotTimer < 7.2f) return;

                    string text = _editor.Text;
                    string got = _textBefore >= 0 && _textBefore + 2 <= text.Length
                        ? text.Substring(_textBefore, 2)
                        : string.Empty;

                    Debug.Log("SELFTEST 10: Shift, затем q и w -> '" + got +
                              "' (ждали 'Qw') " + (got == "Qw" ? "OK" : "MISMATCH"));
                    _testStep = 17;
                    return;
                }

                case 17:
                    if (_shotTimer < 7.5f) return;

                    // Страница вывода и её крестик: открыть, закрыть, проверить состояние.
                    ToggleConsole();
                    _testStep = 18;
                    return;

                case 18:
                    if (_shotTimer < 7.9f) return;

                    Debug.Log("SELFTEST 11: output page opened=" + _consoleVisible +
                              " editor active=" + _editorArea.gameObject.activeSelf);
                    ClickControl("close output");
                    _testStep = 19;
                    return;

                case 19:
                    if (_shotTimer < 8.3f) return;

                    Debug.Log("SELFTEST 12: cross closed the page=" + !_consoleVisible +
                              " editor back=" + _editorArea.gameObject.activeSelf +
                              (_consoleVisible ? " MISMATCH" : " OK"));

                    // Диалог имени файла: поле ввода должно собраться и получить фокус.
                    ShowNewFilePrompt();
                    _testStep = 20;
                    return;

                case 20:
                {
                    if (_shotTimer < 8.7f) return;

                    bool open = _promptRoot != null && _promptInput != null;
                    bool focused = _promptInput != null && _promptInput.isFocused;

                    Debug.Log("SELFTEST 13: name dialog open=" + open + " field focused=" +
                              focused + (open && focused ? " OK" : " MISMATCH"));

                    ClosePrompt();
                    _testStep = 21;
                    return;
                }

                case 21:
                    if (_shotTimer < 9.1f) return;

                    Debug.Log("SELFTEST done");
                    Application.Quit();
                    return;
            }
        }

        private void Wheel(float x, float y)
        {
            var data = new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(x, y) };
            ExecuteEvents.Execute(_editor.viewport.gameObject, data, ExecuteEvents.scrollHandler);
        }

        // ---------------------------------------------------------------- touch stand

        /// <summary>
        /// Reproduces the gestures a finger makes on the phone: a quick flick, a slow drag
        /// with a pause before it, and a drag of the scroll bar handle. Every step reports
        /// what the editor did with it and takes a screenshot, because the report alone
        /// does not show what the user sees.
        /// </summary>
        private void RunTouchTest()
        {
            _shotTimer += Time.unscaledDeltaTime;

            // One gesture at a time: two fingers in one press would fight over the same
            // state and the report would mean nothing.
            if (!_gestureDone && _gestureStarted) return;

            switch (_touchStep)
            {
                case 0:
                    if (_shotTimer < 0.6f) return;

                    // Phone layout: keyboard takes its place, the text is long. The output
                    // is a separate page now, so it stays closed for the gestures.
                    _keyboardVisible = true;
                    _consoleVisible = false;
                    Relayout();

                    // Текст меняется только в памяти: отладочный запуск ничего не пишет.
                    _editor.SetText(LongScript());
                    _touchShot = 0;
                    Shoot("TOUCH 1: начало " + _editor.DescribeScrolling());
                    _touchStep = 1;
                    return;

                case 1:
                    Gesture(GesturePoint(0f), GesturePoint(1f), 0.4f, 0f, "быстрый flick вверх");
                    _touchStep = 2;
                    return;

                case 2:
                    Check("flick вверх", 220f, 60f);
                    _touchStep = 3;
                    return;

                case 3:
                    // The same, but the finger rests on the screen first: this is what
                    // happens when a scroll starts a moment late.
                    Gesture(GesturePoint(0f), GesturePoint(1f), 0.5f, 0.45f, "протяжка вверх с паузой");
                    _touchStep = 4;
                    return;

                case 4:
                    Check("протяжка вверх с паузой", 440f, 60f);
                    _touchStep = 5;
                    return;

                case 5:
                    Gesture(GesturePoint(1f), GesturePoint(0f), 0.5f, 0f, "протяжка вниз");
                    _touchStep = 6;
                    return;

                case 6:
                    Check("протяжка вниз", 220f, 60f);
                    _touchStep = 7;
                    return;

                case 7:
                    HandleDrag("перетаскивание ползунка");
                    _touchStep = 8;
                    return;

                case 8:
                    // Ползунок вниз до упора: предел зависит от высоты поля, поэтому
                    // ожидание берётся у редактора, а не из константы.
                    Check("перетаскивание ползунка", _editor.MaxScrollY(), 30f);
                    _touchStep = 9;
                    return;

                case 9:
                    if (_shotTimer < 1.2f) return;

                    // Большое поле кода и щелчок по центру при прокрутке: попадание
                    // каретки проверяется на глаз по снимку и по номеру строки.
                    _keyboardVisible = false;
                    _consoleVisible = false;
                    Relayout();

                    Wheel(0f, -2f);
                    _touchStep = 10;
                    return;

                case 10:
                    if (_shotTimer < 2.2f) return;

                    Vector2 middle = (Vector2)_editor.viewport.position +
                                     new Vector2(_editor.viewport.rect.width * 0.5f,
                                                 _editor.viewport.rect.height * 0.5f);

                    ClickAt(_editor.viewport.gameObject, middle);

                    Debug.Log("TOUCH: клик в центре поля при прокрутке " + _scrollBefore.ToString("0") +
                              " -> строка " + (_editor.Buffer.LineOf(_editor.Caret) + 1) +
                              ", каретка " + _editor.Caret + ", " + _editor.DescribeHit(middle));

                    _scrollBefore = _editor.ScrollY;
                    _touchStep = 11;
                    return;

                case 11:
                    if (_shotTimer < 2.6f) return;

                    Debug.Log("TOUCH: после клика прокрутка = " + _editor.ScrollY.ToString("0") +
                              " (была " + _scrollBefore.ToString("0") + ")");
                    Shoot("TOUCH 6: каретка после клика при прокрутке " + _editor.DescribeScrolling());
                    _touchStep = 12;
                    return;

                case 12:
                    if (_shotTimer < 4.5f) return;

                    Debug.Log("TOUCH: текст в редакторе не изменился = " +
                              (_editor.Text == LongScript() ? "OK" : "MISMATCH") +
                              ", выделено символов = " + _editor.SelectedText().Length);
                    _touchStep = 13;
                    return;

                case 13:
                    if (_shotTimer < 6.5f) return;

                    Debug.Log("TOUCH done");
                    Application.Quit();
                    return;
            }
        }

        /// <summary>Compares the scroll after a gesture with what it has to be.</summary>
        private void Check(string what, float expected, float tolerance)
        {
            Shoot("TOUCH: " + what + " прокрутка " + _editor.ScrollY.ToString("0") +
                  ", ждали " + expected.ToString("0") + " " +
                  (Mathf.Abs(_editor.ScrollY - expected) <= tolerance ? "OK" : "MISMATCH") +
                  ", выделено = " + _editor.SelectedText().Length + ", " + _editor.DescribeScrolling());
        }

        /// <summary>Starts the next finger gesture, unless one is already running.</summary>
        private void Gesture(Vector2 from, Vector2 to, float seconds, float hold, string what)
        {
            _scrollBefore = _editor.ScrollY;
            _gestureStarted = true;
            _gestureDone = false;
            StartCoroutine(Finger(from, to, seconds, hold, what));
        }

        /// <summary>A point in the code area: 0 is the middle row, 1 is 220 pixels up.</summary>
        private Vector2 GesturePoint(float t)
        {
            Rect area = _editor.viewport.rect;

            var centre = (Vector2)_editor.viewport.position +
                         new Vector2(area.width * 0.5f, area.height * 0.5f);

            return centre + new Vector2(0f, t * 220f * _canvas.scaleFactor);
        }

        /// <summary>A finger: press, optional pause, a drag in steps, release.</summary>
        private IEnumerator Finger(Vector2 from, Vector2 to, float seconds, float hold, string what)
        {
            var data = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = from
            };

            ExecuteEvents.Execute(_editor.viewport.gameObject, data, ExecuteEvents.pointerDownHandler);
            Debug.Log("FINGER " + what + ": нажали, жест = " + _editor.Gesture);

            if (hold > 0f) yield return new WaitForSecondsRealtime(hold);

            const int steps = 20;

            Vector2 previous = from;

            for (int i = 1; i <= steps; i++)
            {
                data.position = Vector2.Lerp(from, to, i / (float)steps);
                ExecuteEvents.Execute(_editor.viewport.gameObject, data, ExecuteEvents.dragHandler);

                if (i == steps)
                    Debug.Log("FINGER " + what + ": конец протяжки на экране +" +
                              (data.position.y - previous.y).ToString("0") + " px, жест = " +
                              _editor.Gesture + ", прокрутка = " + _editor.ScrollY.ToString("0"));

                yield return null;
            }

            ExecuteEvents.Execute(_editor.viewport.gameObject, data, ExecuteEvents.pointerUpHandler);
            Debug.Log("FINGER " + what + ": отпустили, жест = " + _editor.Gesture +
                      ", прокрутка = " + _editor.ScrollY.ToString("0") +
                      ", выделено = " + _editor.SelectedText().Length);

            _gestureDone = true;
        }

        /// <summary>Drags the scroll bar handle from where it is to the bottom of the track.</summary>
        private void HandleDrag(string what)
        {
            _gestureStarted = true;
            _gestureDone = false;
            StartCoroutine(DragHandle(what));
        }

        private IEnumerator DragHandle(string what)
        {
            var handle = _editor.scrollbarHandle;
            if (handle == null)
            {
                Debug.Log("FINGER " + what + ": ползунка нет");
                yield break;
            }

            var data = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left
            };

            data.position = RectTransformUtility.WorldToScreenPoint(null, handle.position);
            ExecuteEvents.Execute(handle.gameObject, data, ExecuteEvents.pointerDownHandler);
            Debug.Log("FINGER " + what + ": взяли ползунок");

            var track = handle.parent as RectTransform;
            Vector2 bottom = RectTransformUtility.WorldToScreenPoint(null,
                track.TransformPoint(new Vector3(0f, -track.rect.height * 0.5f + 10f, 0f)));

            for (int i = 1; i <= 20; i++)
            {
                data.position = Vector2.Lerp(data.position, bottom, i / 20f);
                ExecuteEvents.Execute(handle.gameObject, data, ExecuteEvents.dragHandler);
                yield return null;
            }

            ExecuteEvents.Execute(handle.gameObject, data, ExecuteEvents.pointerUpHandler);
            Debug.Log("FINGER " + what + ": отпустили, прокрутка = " + _editor.ScrollY.ToString("0"));

            _gestureDone = true;
        }

        /// <summary>Long enough text to have to scroll, without touching the files on disk.</summary>
        private static string LongScript()
        {
            var script = new StringBuilder();

            for (int i = 0; i < 70; i++)
            {
                if (i > 0) script.Append('\n');
                script.Append(":72. ").Append(i).Append(". { строка ").Append(i).Append(" }");
            }

            return script.ToString();
        }

        private void Shoot(string message)
        {
            string path = "/tmp/opencode/touch" + _touchShot + ".png";
            _touchShot++;
            Debug.Log(message + " -> " + path);
            ScreenCapture.CaptureScreenshot(path);
        }

        /// <summary>Presses a control at a screen position, like a real pointer.</summary>
        private void ClickAt(GameObject target, Vector2 screenPosition)
        {
            var down = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = screenPosition
            };

            ExecuteEvents.Execute(target, down, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(target, down, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(target, down, ExecuteEvents.pointerClickHandler);
        }

        private void ClickControl(string name)
        {
            Button[] buttons = _canvas.GetComponentsInChildren<Button>(true);

            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i].name != name) continue;

                var data = new PointerEventData(EventSystem.current)
                {
                    button = PointerEventData.InputButton.Left
                };

                ExecuteEvents.Execute(buttons[i].gameObject, data, ExecuteEvents.pointerClickHandler);
                return;
            }

            Debug.Log("SELFTEST: control not found: " + name);
        }

        /// <summary>Prints where every panel ended up, in screen pixels.</summary>
        private void LogLayout()
        {
            var canvasRect = (RectTransform)_canvas.transform;

            Debug.Log("LAYOUT screen=" + Screen.width + "x" + Screen.height +
                      " scale=" + _canvas.scaleFactor +
                      " canvas=" + Rect(canvasRect) +
                      " safeAreaRect=" + Screen.safeArea);

            Panel("root", _safeArea.parent as RectTransform);
            Panel("safe", _safeArea);
            Panel("toolbar", _toolbar);
            Panel("editor", _editorArea);
            Panel("console", _consolePanel);
            Panel("status", _statusBar);
            Panel("keyboard", _keyboardPanel);

            DumpImages();

            if (_console != null && _console.output != null)
                Debug.Log("LAYOUT consoleText=" + _console.output.text.Length +
                          " contentRect=" + Rect(_console.content) +
                          " outputRect=" + Rect(_console.output.rectTransform) +
                          " status='" + (_statusText != null ? _statusText.text : "?") + "'");

            Debug.Log("LAYOUT store=" + _store.Root +
                      " file=" + _currentFile +
                      " storedChars=" + (_currentFile != null ? _store.Read(_currentFile).Length : -1) +
                      " editorChars=" + _editor.Text.Length +
                      " textEnd=\"" + Preview(_editor.Text) + "\"" +
                      " font=" + (Ui.Font != null ? Ui.Font.name : "null") +
                      " fontSize=" + _editor.fontSize +
                      " canvasScale=" + _canvas.scaleFactor);
        }

        /// <summary>Prints every image with its screen rectangle, to hunt down stray panels.</summary>
        private void DumpImages()
        {
            Image[] images = _canvas.GetComponentsInChildren<Image>(true);

            foreach (Image image in images)
            {
                RectTransform rect = image.rectTransform;
                Vector3 min = RectTransformUtility.WorldToScreenPoint(CanvasCamera(), rect.TransformPoint(rect.rect.min));
                Vector3 max = RectTransformUtility.WorldToScreenPoint(CanvasCamera(), rect.TransformPoint(rect.rect.max));

                Debug.Log("IMAGE " + GetPath(rect) + " color=" + image.color +
                          " active=" + rect.gameObject.activeInHierarchy +
                          " screen=[" + (int)min.x + "," + (int)min.y + " -> " + (int)max.x + "," + (int)max.y + "]");
            }
        }

        /// <summary>Last characters of the buffer, for checking typed input in the log.</summary>
        private static string Preview(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            string tail = text.Length <= 30 ? text : text.Substring(text.Length - 30);
            return tail.Replace("\n", "|");
        }

        private static string GetPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                if (transform.name == "safe area" || transform.name == "Canvas") break;
                path = transform.name + "/" + path;
            }

            return path;
        }

        private void Panel(string name, RectTransform rect)
        {
            if (rect == null)
            {
                Debug.Log("LAYOUT " + name + ": missing");
                return;
            }

            Vector3 min = RectTransformUtility.WorldToScreenPoint(CanvasCamera(), rect.TransformPoint(rect.rect.min));
            Vector3 max = RectTransformUtility.WorldToScreenPoint(CanvasCamera(), rect.TransformPoint(rect.rect.max));

            Debug.Log("LAYOUT " + name + ": rect=" + Rect(rect) +
                      " pos=" + rect.anchoredPosition +
                      " screen=[" + (int)min.x + "," + (int)min.y + " -> " + (int)max.x + "," + (int)max.y + "]");
        }

        private static string Rect(RectTransform rect)
        {
            if (rect == null) return "-";

            return string.Format("{0:0}x{1:0}", rect.rect.width, rect.rect.height);
        }

        /// <summary>Hardware keyboard for desktop testing: typing, editing, shortcuts.</summary>
        private void HandleKeyboard()
        {
            // Имя файла набирается в поле ввода: эти символы не должны попасть ещё и в код.
            if (_promptInput != null && _promptInput.isFocused) return;

            bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                           Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);

            string typed = Input.inputString;

            if (!string.IsNullOrEmpty(typed) && !control)
            {
                foreach (char c in typed)
                {
                    // Control characters arrive here as well (\b for Backspace, \r for Enter,
                    // \t for Tab) and are handled once more by the key checks below.
                    // Letting them through would delete two characters or add two line breaks.
                    if (c < ' ' || c == 127) continue;

                    OnKeyPressed(c.ToString());
                }
            }

            if (control)
            {
                if (Input.GetKeyDown(KeyCode.C)) OnKeyPressed("Copy");
                if (Input.GetKeyDown(KeyCode.X)) OnKeyPressed("Cut");
                if (Input.GetKeyDown(KeyCode.V)) OnKeyPressed("Paste");
                if (Input.GetKeyDown(KeyCode.A)) OnKeyPressed("SelectAll");
                if (Input.GetKeyDown(KeyCode.Z)) OnKeyPressed("Undo");
                if (Input.GetKeyDown(KeyCode.Y)) OnKeyPressed("Redo");
                return;
            }

            if (Input.GetKeyDown(KeyCode.Backspace)) OnKeyPressed("Back");
            if (Input.GetKeyDown(KeyCode.Delete)) OnKeyPressed("Del");
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) OnKeyPressed("Enter");
            if (Input.GetKeyDown(KeyCode.Tab)) OnKeyPressed("Tab");
            if (Input.GetKeyDown(KeyCode.LeftArrow)) OnKeyPressed("Left");
            if (Input.GetKeyDown(KeyCode.RightArrow)) OnKeyPressed("Right");
            if (Input.GetKeyDown(KeyCode.UpArrow)) OnKeyPressed("Up");
            if (Input.GetKeyDown(KeyCode.DownArrow)) OnKeyPressed("Down");
            if (Input.GetKeyDown(KeyCode.Home)) OnKeyPressed("Home");
            if (Input.GetKeyDown(KeyCode.End)) OnKeyPressed("End");
        }

        public void QuitApp()
        {
            if (!IsDebugRun) Save();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void HandleBack()
        {
            if (_promptRoot != null)
            {
                ClosePrompt();
                return;
            }

            if (_overlay != null)
            {
                CloseMenu();
                return;
            }

            if (_consoleVisible)
            {
                CloseConsole();
                return;
            }

            if (_keyboardVisible)
            {
                ToggleKeyboard();
                return;
            }

            if (_runner != null && _runner.IsRunning)
            {
                StopProgram();
                return;
            }

            QuitApp();
        }

        // ---------------------------------------------------------------- running

        public void StartProgram()
        {
            if (_runner.IsRunning) return;

            BrainshProgram program;
            try
            {
                program = BrainshCompiler.Compile(_editor.Text, _store, _currentFile);
            }
            catch (BrainshException e)
            {
                ShowError(e);
                return;
            }

            _io = new ConsoleIo();
            _console.Clear();
            _console.SetAwaitingInput(false);

            _runner.StartProgram(program, _io);

            // Вывод — отдельная страница: запуск сразу показывает её.
            _consoleVisible = true;
            Relayout();

            SetStatus("running", Ui.Accent, 2f);
        }

        public void StopProgram()
        {
            if (_runner == null || !_runner.IsRunning) return;

            _runner.Stop();
            _console.SetAwaitingInput(false);
            SetStatus("stopped", Ui.TextDim, 3f);
        }

        private void OnProgramFinished(string message)
        {
            _console.SetAwaitingInput(false);

            if (string.IsNullOrEmpty(message))
            {
                SetStatus("finished", Ui.Good, 3f);
            }
            else if (message == "stopped")
            {
                SetStatus("stopped", Ui.TextDim, 3f);
            }
            else
            {
                _console.Append("\n" + message + "\n");
                SetStatus(message, Ui.Error);
            }
        }

        private void ShowError(BrainshException e)
        {
            _consoleVisible = true;
            Relayout();

            _console.Clear();
            _console.Append(e.FullMessage + "\n");
            SetStatus(e.Message, Ui.Error);

            if (e.Line > 1)
            {
                CodeBuffer buffer = _editor.Buffer;
                int index = buffer.StartOfLine(e.Line - 1);
                buffer.Caret = index;
                _editor.ScrollCaretIntoView();
            }
        }

        // ---------------------------------------------------------------- status

        private void SetStatus(string message, Color color, float hold = 0f)
        {
            if (_statusText == null) return;

            _statusText.text = message;
            _statusText.color = color;
            _messageTimer = hold;
        }

        private void ToggleConsole()
        {
            _consoleVisible = !_consoleVisible;
            Relayout();
        }

        /// <summary>The cross on the output page: stop the program and go back to the code.</summary>
        private void CloseConsole()
        {
            if (_runner != null && _runner.IsRunning) StopProgram();

            _consoleVisible = false;
            Relayout();
        }

        private void ToggleKeyboard()
        {
            _keyboardVisible = !_keyboardVisible;
            Relayout();
        }
    }
}
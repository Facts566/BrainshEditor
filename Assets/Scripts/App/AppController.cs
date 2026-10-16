using System;
using System.Collections.Generic;
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
        private const float ToolbarHeight = 52f;
        private const float StatusHeight = 26f;
        private const float ConsoleHeight = 150f;
        private const float GutterWidth = 46f;
        private const float ScrollbarWidth = 10f;
        private const int EditorFontSize = 22;
        private const float CodePaddingX = 8f;
        private const float paddingY = 6f;

        private readonly List<MenuEntry> _menu = new List<MenuEntry>();

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
        private RectTransform _overlay;

        // On a desktop the real keyboard is used, the on screen one only gets in the way.
        private bool _keyboardVisible = Application.isMobilePlatform;
        private bool _consoleVisible = true;
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
            get { return _selfTest || _shots != null; }
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

            var scaler = _canvas.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = _canvas.gameObject.AddComponent<CanvasScaler>();

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        private void BuildToolbar()
        {
            RectTransform bar = Ui.Node("toolbar", _safeArea);
            Ui.AtTop(bar, ToolbarHeight);
            _toolbar = bar;

            Ui.Box("bar background", bar, Ui.Panel);

            HorizontalLayoutGroup row = Ui.Row(bar, 6f, new RectOffset(8, 8, 7, 7));
            row.childForceExpandWidth = true;

            _runButton = ToolButton(row, "Run", Ui.Run, StartProgram);
            _stopButton = ToolButton(row, "Stop", Ui.Stop, StopProgram);

            ToolButton(row, "Save", Ui.PanelLight, () => Save(true));
            ToolButton(row, "Undo", Ui.PanelLight, () => _editor.OnKey("Undo"));
            ToolButton(row, "Redo", Ui.PanelLight, () => _editor.OnKey("Redo"));
            ToolButton(row, "Out", Ui.PanelLight, ToggleConsole);
            ToolButton(row, "Kbd", Ui.PanelLight, ToggleKeyboard);
            ToolButton(row, "Files", Ui.PanelLight, ShowFiles);
            ToolButton(row, "Menu", Ui.PanelLight, ShowMainMenu);
            ToolButton(row, "Exit", Ui.Stop, QuitApp);
        }

        private Button ToolButton(HorizontalLayoutGroup row, string caption, Color color, Action action)
        {
            Button button = Ui.Btn(caption, row.transform, caption, color, () => action(), 15);
            LayoutElement element = Ui.Size(button.gameObject, -1, 0f);
            element.flexibleWidth = 1f;
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
            caretRect.sizeDelta = new Vector2(2f, 20f);

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

            _editor.Changed += OnEditorChanged;
            _editor.CaretMoved += OnCaretMoved;
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

        private void BuildConsole()
        {
            _consolePanel = Ui.Node("console", _safeArea);
            Ui.Box("console background", _consolePanel, Ui.Panel);

            RectTransform viewport = Ui.Node("viewport", _consolePanel);
            Ui.Stretch(viewport, 8f, 26f, 8f, 6f);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = Ui.Node("content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            Text output = Ui.Label("output", content, string.Empty, 16, Ui.TextMain, TextAnchor.UpperLeft, true);
            Ui.Stretch(output.rectTransform);

            // The height is set by hand: a ContentSizeFitter is not re-evaluated
            // when only Text.text changes, and the output would stay invisible.
            Text input = Ui.Label("input line", _consolePanel, string.Empty, 16, Ui.Accent, TextAnchor.LowerLeft);
            Ui.Stretch(input.rectTransform, 8f, 4f, 8f, 0f);

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

            _statusText = Ui.Label("status", _statusBar, string.Empty, 15, Ui.TextDim, TextAnchor.MiddleLeft);
            Ui.Stretch(_statusText.rectTransform, 10f, 0f, 0f, 0f);

            _fileText = Ui.Label("file", _statusBar, string.Empty, 15, Ui.TextDim, TextAnchor.MiddleRight);
            Ui.Stretch(_fileText.rectTransform, 0f, 0f, 10f, 0f);
        }

        private void BuildKeyboard()
        {
            _keyboardPanel = Ui.Node("keyboard", _safeArea);
            Ui.Box("keyboard background", _keyboardPanel, Ui.Panel);

            _keyboard = _keyboardPanel.gameObject.AddComponent<SoftKeyboard>();
            _keyboard.Build(_keyboardPanel);
            _keyboard.Resize();
            _keyboard.KeyPressed += OnKeyPressed;
        }

        // ---------------------------------------------------------------- layout

        private void Relayout()
        {
            float keyboardHeight = _keyboardVisible ? SoftKeyboard.PreferredHeight : 0f;
            float consoleHeight = _consoleVisible ? ConsoleHeight : 0f;

            _keyboardPanel.gameObject.SetActive(_keyboardVisible);
            _consolePanel.gameObject.SetActive(_consoleVisible);

            SetBottom(_keyboardPanel, 0f, keyboardHeight);
            SetBottom(_statusBar, keyboardHeight, keyboardHeight + StatusHeight);
            SetBottom(_consolePanel, keyboardHeight + StatusHeight, keyboardHeight + StatusHeight + consoleHeight);

            _editorArea.offsetMin = new Vector2(0f, keyboardHeight + StatusHeight + consoleHeight);
            _editorArea.offsetMax = new Vector2(0f, -ToolbarHeight);

            if (_keyboardVisible) _keyboard.Resize();
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
            string text = _store.Read(name);

            if (string.IsNullOrEmpty(text))
            {
                text = _store.Read("hello.bsh");
                name = "hello.bsh";
            }

            _currentFile = name;
            _editor.SetText(text);
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
                string file = name;
                _menu.Add(new MenuEntry { Caption = (file == _currentFile ? "> " : "   ") + file + ScriptStore.Extension,
                    Action = () =>
                    {
                        Save();
                        LoadFile(file);
                        CloseMenu();
                    } });
            }

            _menu.Add(new MenuEntry { Caption = "-", Action = () => { } });

            string fresh = _store.NextName();
            _menu.Add(new MenuEntry
            {
                Caption = "New file: " + fresh,
                Action = () =>
                {
                    _store.Write(fresh, _editor.Text);
                    LoadFile(fresh);
                    CloseMenu();
                }
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
                        _editor.fontSize = value;
                        _editor.code.fontSize = value;
                        _editor.Refresh();
                        ShowMainMenu();
                    }
                });
            }

            ShowMenu("Font size", _menu);
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
            card.offsetMin = new Vector2(24f, 60f);
            card.offsetMax = new Vector2(-24f, -60f);

            Ui.Box("card background", card, Ui.PanelLight);

            RectTransform header = Ui.Node("header", card);
            Ui.AtTop(header, 54f);
            Ui.Label("title", header, title, 20, Ui.TextMain, TextAnchor.MiddleLeft);
            Ui.Stretch(((RectTransform)header.Find("title")), 14f, 0f, 0f, 0f);

            Button close = Ui.Btn("close", header, "Close", Ui.Panel, CloseMenu, 16);
            RectTransform closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.sizeDelta = new Vector2(110f, 36f);
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
                    item.Danger ? Ui.Stop : Ui.Panel, () => item.Action(), 18);

                Ui.Size(button.gameObject, -1, 46f);
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
            // The keyboard fits its keys to the panel, once the layout of the frame is known.
            if (_keyboard != null) _keyboard.Resize();
        }

        private void Update()
        {
            if (_runner != null && _runner.IsRunning && _io != null)
            {
                bool waiting = _io.WaitingForInput;
                if (waiting != _console.AwaitingInput) _console.SetAwaitingInput(waiting);
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

                    // На компьютере экранная клавиатура скрыта, включаем её кнопкой.
                    ClickControl("Kbd");
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

                    ClickControl("Out");
                    Debug.Log("SELFTEST 5: output panel toggled, visible=" + _consoleVisible);
                    _testStep = 9;
                    return;

                case 9:
                {
                    if (_shotTimer < 4.8f) return;

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
                    if (_shotTimer < 6.5f) return;

                    Debug.Log("SELFTEST done");
                    Application.Quit();
                    return;
            }
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
            if (_overlay != null)
            {
                CloseMenu();
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

        private void ToggleKeyboard()
        {
            _keyboardVisible = !_keyboardVisible;
            Relayout();
        }
    }
}
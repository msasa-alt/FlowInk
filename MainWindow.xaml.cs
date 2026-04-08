using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace FlowInk;

public partial class MainWindow : Window
{
    private readonly Brush _normalButtonBackground = new SolidColorBrush(Color.FromRgb(45, 45, 45));
    private readonly Brush _selectedButtonBackground = new SolidColorBrush(Color.FromRgb(90, 130, 210));
    private readonly Brush _normalButtonForeground = Brushes.White;
    private readonly Brush _selectedButtonForeground = Brushes.White;

    private bool _isClickThroughEnabled;
    private bool _hasShownClickThroughTrayMessage;
    private readonly Forms.NotifyIcon _notifyIcon = new();
    private readonly DispatcherTimer _toastTimer = new();
    private Forms.ToolStripMenuItem? _trayEnableClickThroughMenuItem;
    private Forms.ToolStripMenuItem? _trayDisableClickThroughMenuItem;
    private ToolMode _currentTool = ToolMode.Pen;
    private InteractionState _currentInteractionState = InteractionState.None;

    private Color _currentPenColor = Color.FromArgb(255, 255, 0, 0);
    private double _currentPenWidth = 4;
    private string _currentTextFontFamilyName = DefaultTextFontFamilyName;
    private double _currentTextFontSize = DefaultTextFontSize;
    private FontStyle _currentTextFontStyle = FontStyles.Normal;
    private FontWeight _currentTextFontWeight = FontWeights.Normal;

    private List<Color> _presetColors = new();
    private List<Color> _recentColors = new();
    private List<int> _customColorValues = new();

    private bool _isStraightLineDrawing;
    private Point _straightLineStartPoint;
    private Stroke? _straightLinePreviewStroke;
    private Stroke? _straightLinePreviewArrowHeadStroke;

    private bool _isRectangleDrawing;
    private Point _rectangleStartPoint;
    private Stroke? _rectanglePreviewStroke;

    private TextBox? _activeTextBox;
    private Point _activeTextStartPoint;
    private readonly List<Border> _textElements = new();

    private readonly UndoHistoryManager<IUndoableAction, MainWindow> _history;
    private bool _isApplyingHistory;
    private bool _suppressStrokeHistory;
    private bool _isEraserGestureActive;
    private readonly List<Stroke> _eraserGestureAddedStrokes = new();
    private readonly List<Stroke> _eraserGestureRemovedStrokes = new();
    private Point _textDragCommittedStartPoint;

    private Border? _draggingTextElement;
    private bool _isDraggingTextElement;
    private Point _textDragStartMousePoint;
    private Point _textDragStartElementPoint;

    private Border? _editingTextOriginalElement;
    private Color? _editingTextOriginalColor;
    private string? _editingTextOriginalFontFamilyName;
    private double? _editingTextOriginalFontSize;
    private FontStyle? _editingTextOriginalFontStyle;
    private FontWeight? _editingTextOriginalFontWeight;

    private const int MaxRecentColors = 8;
    private const int MaxHistory = 200;
    private const int MaxCustomColors = 16;

    private const string DefaultTextFontFamilyName = "Segoe UI";
    private const double DefaultTextFontSize = 28.0;
    private const double MinTextFontSize = 8.0;
    private const double MaxTextFontSize = 144.0;
    private const double TextMinWidth = 80.0;
    private const double TextMaxWidth = 400.0;
    private const double TextPaddingX = 4.0;
    private const double TextPaddingY = 2.0;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID_TOGGLE_CLICKTHROUGH = 1;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint VK_T = 0x54;

    private const string AppSettingsFileName = "app-settings.json";

    private enum ToolMode
    {
        Pen,
        Rectangle,
        Text,
        Eraser
    }

    private enum InteractionState
    {
        None,
        DrawingPen,
        DrawingLine,
        DrawingArrow,
        DrawingRect,
        Erasing,
        EditingText,
        MovingText
    }

    private interface IUndoableAction
    {
        void Undo(MainWindow window);
        void Redo(MainWindow window);
    }

    private sealed class StrokeCollectionAction : IUndoableAction
    {
        private readonly List<Stroke> _added;
        private readonly List<Stroke> _removed;

        public StrokeCollectionAction(IEnumerable<Stroke> added, IEnumerable<Stroke> removed)
        {
            _added = new List<Stroke>(added);
            _removed = new List<Stroke>(removed);
        }

        public void Undo(MainWindow window)
        {
            window.ExecuteWithoutStrokeHistory(() =>
            {
                foreach (Stroke stroke in _added)
                {
                    window.RemoveStrokeIfPresent(stroke);
                }

                foreach (Stroke stroke in _removed)
                {
                    window.AddStrokeIfMissing(stroke);
                }
            });
        }

        public void Redo(MainWindow window)
        {
            window.ExecuteWithoutStrokeHistory(() =>
            {
                foreach (Stroke stroke in _removed)
                {
                    window.RemoveStrokeIfPresent(stroke);
                }

                foreach (Stroke stroke in _added)
                {
                    window.AddStrokeIfMissing(stroke);
                }
            });
        }
    }

    private sealed class TextAddAction : IUndoableAction
    {
        private readonly Border _element;

        public TextAddAction(Border element)
        {
            _element = element;
        }

        public void Undo(MainWindow window)
        {
            window.RemoveCommittedTextElement(_element);
        }

        public void Redo(MainWindow window)
        {
            window.AddCommittedTextElement(_element);
        }
    }

    private sealed class TextRemoveAction : IUndoableAction
    {
        private readonly Border _element;

        public TextRemoveAction(Border element)
        {
            _element = element;
        }

        public void Undo(MainWindow window)
        {
            window.AddCommittedTextElement(_element);
        }

        public void Redo(MainWindow window)
        {
            window.RemoveCommittedTextElement(_element);
        }
    }

    private sealed class TextReplaceAction : IUndoableAction
    {
        private readonly Border _before;
        private readonly Border _after;

        public TextReplaceAction(Border before, Border after)
        {
            _before = before;
            _after = after;
        }

        public void Undo(MainWindow window)
        {
            window.RemoveCommittedTextElement(_after);
            window.AddCommittedTextElement(_before);
        }

        public void Redo(MainWindow window)
        {
            window.RemoveCommittedTextElement(_before);
            window.AddCommittedTextElement(_after);
        }
    }

    private sealed class TextMoveAction : IUndoableAction
    {
        private readonly Border _element;
        private readonly Point _before;
        private readonly Point _after;

        public TextMoveAction(Border element, Point before, Point after)
        {
            _element = element;
            _before = before;
            _after = after;
        }

        public void Undo(MainWindow window)
        {
            window.SetTextElementPosition(_element, _before);
        }

        public void Redo(MainWindow window)
        {
            window.SetTextElementPosition(_element, _after);
        }
    }

    private sealed class ClearAction : IUndoableAction
    {
        private readonly List<Stroke> _removedStrokes;
        private readonly List<Border> _removedTextElements;

        public ClearAction(IEnumerable<Stroke> removedStrokes, IEnumerable<Border> removedTextElements)
        {
            _removedStrokes = new List<Stroke>(removedStrokes);
            _removedTextElements = new List<Border>(removedTextElements);
        }

        public void Undo(MainWindow window)
        {
            window.ExecuteWithoutStrokeHistory(() =>
            {
                foreach (Stroke stroke in _removedStrokes)
                {
                    window.AddStrokeIfMissing(stroke);
                }
            });

            foreach (Border element in _removedTextElements)
            {
                window.AddCommittedTextElement(element);
            }
        }

        public void Redo(MainWindow window)
        {
            window.ExecuteWithoutStrokeHistory(() =>
            {
                foreach (Stroke stroke in _removedStrokes)
                {
                    window.RemoveStrokeIfPresent(stroke);
                }
            });

            foreach (Border element in _removedTextElements)
            {
                window.RemoveCommittedTextElement(element);
            }
        }
    }

    private sealed class PresetColorSlot
    {
        public int Index { get; init; }
        public Color Color { get; init; }
    }

    private sealed class AppSettings
    {
        public List<string> PresetColors { get; set; } = new();
        public List<string> RecentColors { get; set; } = new();
        public List<int> CustomColors { get; set; } = new();
        public double PenWidth { get; set; } = 4.0;
        public string? CurrentColor { get; set; }
        public string? TextFontFamily { get; set; }
        public double TextFontSize { get; set; } = DefaultTextFontSize;
        public bool TextBold { get; set; }
        public bool TextItalic { get; set; }
    }

    public MainWindow()
    {
        _history = new UndoHistoryManager<IUndoableAction, MainWindow>(
            MaxHistory,
            static (action, window) => action.Undo(window),
            static (action, window) => action.Redo(window));

        InitializeComponent();

        InitializeButtonStyles();

        LoadAppSettings();

        BuildPresetColorButtons();
        BuildRecentColorButtons();

        ApplyPenColor(_currentPenColor, addToRecent: true);

        DrawingCanvas.EditingMode = InkCanvasEditingMode.Ink;
        DrawingCanvas.UseCustomCursor = true;
        DrawingCanvas.IsHitTestVisible = true;
        DrawingCanvas.Focusable = true;

        DrawingCanvas.PreviewMouseLeftButtonDown += DrawingCanvas_PreviewMouseLeftButtonDown;
        DrawingCanvas.PreviewMouseMove += DrawingCanvas_PreviewMouseMove;
        DrawingCanvas.PreviewMouseLeftButtonUp += DrawingCanvas_PreviewMouseLeftButtonUp;
        DrawingCanvas.PreviewMouseWheel += DrawingCanvas_PreviewMouseWheel;
        DrawingCanvas.LostMouseCapture += DrawingCanvas_LostMouseCapture;
        DrawingCanvas.Strokes.StrokesChanged += DrawingCanvas_StrokesChanged;

        PreviewKeyDown += MainWindow_PreviewKeyDown;

        UpdateToolHighlight();
        InitializeNotifyIcon();
        InitializeToastTimer();

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        SetClickThrough(false);

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        HwndSource? source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);

        bool registered = RegisterHotKey(
            hwnd,
            HOTKEY_ID_TOGGLE_CLICKTHROUGH,
            MOD_CONTROL | MOD_ALT,
            VK_T);

        if (!registered)
        {
            MessageBox.Show(
                "Ctrl + Alt + T のグローバルホットキー登録に失敗しました。\n他のアプリで使われている可能性があります。",
                "FlowInk",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _toastTimer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(hwnd, HOTKEY_ID_TOGGLE_CLICKTHROUGH);
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_isClickThroughEnabled)
        {
            return;
        }

        if (_activeTextBox != null && _activeTextBox.IsKeyboardFocusWithin)
        {
            return;
        }

        bool isControlPressed = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (!isControlPressed)
        {
            return;
        }

        if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            UndoHistory();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Y || (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Shift) != 0))
        {
            RedoHistory();
            e.Handled = true;
        }
    }

    private void DrawingCanvas_StrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
    {
        if (_isApplyingHistory || _suppressStrokeHistory)
        {
            return;
        }

        if (e.Added.Count == 0 && e.Removed.Count == 0)
        {
            return;
        }

        if (_isEraserGestureActive)
        {
            AccumulateStrokeDelta(_eraserGestureAddedStrokes, _eraserGestureRemovedStrokes, e.Added, e.Removed);
            return;
        }

        PushHistory(new StrokeCollectionAction(ToStrokeList(e.Added), ToStrokeList(e.Removed)));
    }

    private static void AccumulateStrokeDelta(
        List<Stroke> addedNet,
        List<Stroke> removedNet,
        IEnumerable<Stroke> added,
        IEnumerable<Stroke> removed)
    {
        foreach (Stroke stroke in added)
        {
            if (!RemoveReference(removedNet, stroke) && !ContainsReference(addedNet, stroke))
            {
                addedNet.Add(stroke);
            }
        }

        foreach (Stroke stroke in removed)
        {
            if (!RemoveReference(addedNet, stroke) && !ContainsReference(removedNet, stroke))
            {
                removedNet.Add(stroke);
            }
        }
    }

    private static bool ContainsReference(List<Stroke> strokes, Stroke target)
    {
        foreach (Stroke stroke in strokes)
        {
            if (ReferenceEquals(stroke, target))
            {
                return true;
            }
        }

        return false;
    }

    private static bool RemoveReference(List<Stroke> strokes, Stroke target)
    {
        for (int i = 0; i < strokes.Count; i++)
        {
            if (ReferenceEquals(strokes[i], target))
            {
                strokes.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    private void BeginEraserGesture()
    {
        _isEraserGestureActive = true;
        _eraserGestureAddedStrokes.Clear();
        _eraserGestureRemovedStrokes.Clear();
    }

    private void CompleteEraserGesture()
    {
        if (!_isEraserGestureActive)
        {
            return;
        }

        _isEraserGestureActive = false;

        if (_eraserGestureAddedStrokes.Count == 0 && _eraserGestureRemovedStrokes.Count == 0)
        {
            _eraserGestureAddedStrokes.Clear();
            _eraserGestureRemovedStrokes.Clear();
            _currentInteractionState = InteractionState.None;
            return;
        }

        PushHistory(new StrokeCollectionAction(_eraserGestureAddedStrokes, _eraserGestureRemovedStrokes));
        _eraserGestureAddedStrokes.Clear();
        _eraserGestureRemovedStrokes.Clear();
        _currentInteractionState = InteractionState.None;
    }

    private void CompleteEraserGestureDeferred()
    {
        Dispatcher.InvokeAsync(CompleteEraserGesture, DispatcherPriority.Background);
    }

    private void PushHistory(IUndoableAction action)
    {
        if (_isApplyingHistory)
        {
            return;
        }

        _history.Push(action);
    }

    private void UndoHistory()
    {
        if (!_history.CanUndo)
        {
            return;
        }

        FinalizeOrCancelCurrentOperation();

        _isApplyingHistory = true;
        try
        {
            _history.Undo(this);
        }
        finally
        {
            _isApplyingHistory = false;
        }
    }

    private void RedoHistory()
    {
        if (!_history.CanRedo)
        {
            return;
        }

        FinalizeOrCancelCurrentOperation();

        _isApplyingHistory = true;
        try
        {
            _history.Redo(this);
        }
        finally
        {
            _isApplyingHistory = false;
        }
    }

    private void FinalizeOrCancelCurrentOperation()
    {
        switch (_currentInteractionState)
        {
            case InteractionState.DrawingPen:
                CancelPenInteraction();
                break;

            case InteractionState.DrawingLine:
            case InteractionState.DrawingArrow:
                CancelStraightLineInteraction();
                break;

            case InteractionState.DrawingRect:
                CancelRectangleInteraction();
                break;

            case InteractionState.EditingText:
                CancelTextEditingInteraction();
                break;

            case InteractionState.MovingText:
                CancelTextMoveInteraction();
                break;

            case InteractionState.Erasing:
                CompleteEraserGesture();
                break;
        }

        ColorPopup.IsOpen = false;
        _currentInteractionState = InteractionState.None;
    }

    private void CancelPenInteraction()
    {
    }

    private void CancelStraightLineInteraction()
    {
        CancelStraightLinePreview();
        _isStraightLineDrawing = false;

        if (DrawingCanvas.IsMouseCaptured)
        {
            DrawingCanvas.ReleaseMouseCapture();
        }
    }

    private void CancelRectangleInteraction()
    {
        CancelRectanglePreview();
        _isRectangleDrawing = false;

        if (DrawingCanvas.IsMouseCaptured)
        {
            DrawingCanvas.ReleaseMouseCapture();
        }
    }

    private void CancelTextEditingInteraction()
    {
        CancelActiveTextInput();
    }

    private void CancelTextMoveInteraction()
    {
        CancelTextElementDrag();
    }

    private void ExecuteWithoutStrokeHistory(Action action)
    {
        bool previous = _suppressStrokeHistory;
        _suppressStrokeHistory = true;
        try
        {
            action();
        }
        finally
        {
            _suppressStrokeHistory = previous;
        }
    }

    private void AddStrokeIfMissing(Stroke stroke)
    {
        if (!DrawingCanvas.Strokes.Contains(stroke))
        {
            DrawingCanvas.Strokes.Add(stroke);
        }
    }

    private void RemoveStrokeIfPresent(Stroke stroke)
    {
        if (DrawingCanvas.Strokes.Contains(stroke))
        {
            DrawingCanvas.Strokes.Remove(stroke);
        }
    }

    private static List<Stroke> ToStrokeList(IEnumerable<Stroke> strokes)
    {
        return new List<Stroke>(strokes);
    }

    private void InitializeNotifyIcon()
    {
        _trayEnableClickThroughMenuItem = new Forms.ToolStripMenuItem("描画OFFにする");
        _trayDisableClickThroughMenuItem = new Forms.ToolStripMenuItem("描画ONに戻す");
        var trayExitMenuItem = new Forms.ToolStripMenuItem("Exit");

        _trayEnableClickThroughMenuItem.Click += TrayEnableClickThroughMenuItem_Click;
        _trayDisableClickThroughMenuItem.Click += TrayDisableClickThroughMenuItem_Click;
        trayExitMenuItem.Click += TrayExitMenuItem_Click;

        _notifyIcon.Text = "FlowInk";
        _notifyIcon.Icon = Drawing.SystemIcons.Application;
        _notifyIcon.Visible = true;
        _notifyIcon.ContextMenuStrip = new Forms.ContextMenuStrip();
        _notifyIcon.ContextMenuStrip.Items.Add(_trayEnableClickThroughMenuItem);
        _notifyIcon.ContextMenuStrip.Items.Add(_trayDisableClickThroughMenuItem);
        _notifyIcon.ContextMenuStrip.Items.Add(new Forms.ToolStripSeparator());
        _notifyIcon.ContextMenuStrip.Items.Add(trayExitMenuItem);
        _notifyIcon.DoubleClick += NotifyIcon_DoubleClick;

        UpdateNotifyIconMenu();
    }

    private void UpdateNotifyIconMenu()
    {
        _notifyIcon.Text = _isClickThroughEnabled
            ? "FlowInk - 描画OFF"
            : "FlowInk - 描画ON";

        if (_trayEnableClickThroughMenuItem != null)
        {
            _trayEnableClickThroughMenuItem.Enabled = !_isClickThroughEnabled;
        }

        if (_trayDisableClickThroughMenuItem != null)
        {
            _trayDisableClickThroughMenuItem.Enabled = _isClickThroughEnabled;
        }
    }

    private void InitializeToastTimer()
    {
        _toastTimer.Interval = TimeSpan.FromSeconds(2.4);
        _toastTimer.Tick += ToastTimer_Tick;
    }

    private void ShowClickThroughToastIfNeeded()
    {
        if (_hasShownClickThroughTrayMessage)
        {
            return;
        }

        ShowToastMessage("描画OFF。タスクトレイから戻せます。");
        _hasShownClickThroughTrayMessage = true;
    }

    private void ShowToastMessage(string message)
    {
        ToastTextBlock.Text = message;
        ToastBorder.Visibility = Visibility.Visible;

        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToastMessage()
    {
        _toastTimer.Stop();
        ToastBorder.Visibility = Visibility.Collapsed;
    }

    private void ToastTimer_Tick(object? sender, EventArgs e)
    {
        HideToastMessage();
    }

    private void TrayEnableClickThroughMenuItem_Click(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() => SetClickThrough(true));
    }

    private void TrayDisableClickThroughMenuItem_Click(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() => SetClickThrough(false));
    }

    private void TrayExitMenuItem_Click(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            CommitActiveTextInput();
            EndTextElementDrag();
            Close();
        });
    }

    private void NotifyIcon_DoubleClick(object? sender, EventArgs e)
    {
        if (_isClickThroughEnabled)
        {
            Dispatcher.Invoke(() => SetClickThrough(false));
        }
    }

    private void InitializeButtonStyles()
    {
        foreach (var button in new[]
                 {
                     PenButton, RectangleButton, TextButton, EraserButton, ColorButton,
                     ClearButton, ClickThroughButton, ExitButton
                 })
        {
            button.Background = _normalButtonBackground;
            button.Foreground = _normalButtonForeground;
            button.BorderBrush = Brushes.DimGray;
        }
    }

    private string GetColorFilePath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, fileName);
    }

    private void LoadAppSettings()
    {
        string filePath = GetColorFilePath(AppSettingsFileName);

        try
        {
            if (!File.Exists(filePath))
            {
                _presetColors = new List<Color>(GetDefaultPresetColors());
                _recentColors = new List<Color>();
                _customColorValues = new List<int>();
                _currentPenWidth = 4.0;
                _currentTextFontFamilyName = DefaultTextFontFamilyName;
                _currentTextFontSize = DefaultTextFontSize;
                _currentTextFontStyle = FontStyles.Normal;
                _currentTextFontWeight = FontWeights.Normal;

                SaveAppSettings();
                return;
            }

            string json = File.ReadAllText(filePath);
            AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json);

            if (settings == null)
            {
                _presetColors = new List<Color>(GetDefaultPresetColors());
                _recentColors = new List<Color>();
                _customColorValues = new List<int>();
                _currentPenWidth = 4.0;
                _currentTextFontFamilyName = DefaultTextFontFamilyName;
                _currentTextFontSize = DefaultTextFontSize;
                _currentTextFontStyle = FontStyles.Normal;
                _currentTextFontWeight = FontWeights.Normal;
                SaveAppSettings();
                return;
            }

            _presetColors = ParseColorList(settings.PresetColors, GetDefaultPresetColors());
            _recentColors = ParseColorList(settings.RecentColors, new List<Color>());
            _customColorValues = NormalizeCustomColors(settings.CustomColors);
            _currentPenWidth = NormalizePenWidth(settings.PenWidth);
            _currentTextFontFamilyName = NormalizeTextFontFamilyName(settings.TextFontFamily);
            _currentTextFontSize = NormalizeTextFontSize(settings.TextFontSize);
            _currentTextFontStyle = settings.TextItalic ? FontStyles.Italic : FontStyles.Normal;
            _currentTextFontWeight = settings.TextBold ? FontWeights.Bold : FontWeights.Normal;

            if (!string.IsNullOrWhiteSpace(settings.CurrentColor))
            {
                try
                {
                    object? converted = ColorConverter.ConvertFromString(settings.CurrentColor);
                    if (converted is Color color)
                    {
                        _currentPenColor = color;
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
            _presetColors = new List<Color>(GetDefaultPresetColors());
            _recentColors = new List<Color>();
            _customColorValues = new List<int>();
            _currentPenWidth = 4.0;
            _currentTextFontFamilyName = DefaultTextFontFamilyName;
            _currentTextFontSize = DefaultTextFontSize;
            _currentTextFontStyle = FontStyles.Normal;
            _currentTextFontWeight = FontWeights.Normal;
        }
    }

    private void SaveAppSettings()
    {
        string filePath = GetColorFilePath(AppSettingsFileName);

        var settings = new AppSettings
        {
            PresetColors = ToHexColorList(_presetColors),
            RecentColors = ToHexColorList(_recentColors),
            CustomColors = new List<int>(_customColorValues),
            PenWidth = NormalizePenWidth(_currentPenWidth),
            CurrentColor = _currentPenColor.ToString(),
            TextFontFamily = _currentTextFontFamilyName,
            TextFontSize = NormalizeTextFontSize(_currentTextFontSize),
            TextBold = _currentTextFontWeight == FontWeights.Bold,
            TextItalic = _currentTextFontStyle == FontStyles.Italic
        };

        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(filePath, json);
    }

    private static List<Color> ParseColorList(List<string>? hexColors, List<Color> fallbackColors)
    {
        if (hexColors == null)
        {
            return new List<Color>(fallbackColors);
        }

        var colors = new List<Color>();

        foreach (string hex in hexColors)
        {
            try
            {
                object? converted = ColorConverter.ConvertFromString(hex);
                if (converted is Color color)
                {
                    colors.Add(color);
                }
            }
            catch
            {
            }
        }

        return colors.Count > 0 || fallbackColors.Count == 0
            ? colors
            : new List<Color>(fallbackColors);
    }

    private static List<int> NormalizeCustomColors(List<int>? customColors)
    {
        if (customColors == null)
        {
            return new List<int>();
        }

        var result = new List<int>();

        foreach (int value in customColors)
        {
            result.Add(value & 0x00FFFFFF);

            if (result.Count >= MaxCustomColors)
            {
                break;
            }
        }

        return result;
    }

    private static string NormalizeTextFontFamilyName(string? fontFamilyName)
    {
        return string.IsNullOrWhiteSpace(fontFamilyName)
            ? DefaultTextFontFamilyName
            : fontFamilyName.Trim();
    }

    private static FontFamily CreateFontFamilySafe(string? fontFamilyName)
    {
        string normalized = NormalizeTextFontFamilyName(fontFamilyName);

        try
        {
            return new FontFamily(normalized);
        }
        catch
        {
            return new FontFamily(DefaultTextFontFamilyName);
        }
    }

    private static Drawing.FontStyle ToDrawingFontStyle(FontWeight fontWeight, FontStyle fontStyle)
    {
        Drawing.FontStyle drawingFontStyle = Drawing.FontStyle.Regular;

        if (fontWeight == FontWeights.Bold)
        {
            drawingFontStyle |= Drawing.FontStyle.Bold;
        }

        if (fontStyle == FontStyles.Italic)
        {
            drawingFontStyle |= Drawing.FontStyle.Italic;
        }

        return drawingFontStyle;
    }

    private static List<string> ToHexColorList(List<Color> colors)
    {
        var hexColors = new List<string>();

        foreach (Color color in colors)
        {
            hexColors.Add(color.ToString());
        }

        return hexColors;
    }

    private static double NormalizePenWidth(double width)
    {
        if (width < 0.5)
        {
            return 0.5;
        }

        if (width > 30)
        {
            return 30;
        }

        return Math.Round(width * 2) / 2.0;
    }

    private static double NormalizeTextFontSize(double fontSize)
    {
        if (fontSize < MinTextFontSize)
        {
            return MinTextFontSize;
        }

        if (fontSize > MaxTextFontSize)
        {
            return MaxTextFontSize;
        }

        return Math.Round(fontSize);
    }

    private static int GetWheelDirection(int delta)
    {
        if (delta > 0)
        {
            return 1;
        }

        if (delta < 0)
        {
            return -1;
        }

        return 0;
    }

    private List<Color> GetDefaultPresetColors()
    {
        return new List<Color>
        {
            Color.FromArgb(255, 255, 0, 0),
            Color.FromArgb(255, 0, 191, 255),
            Color.FromArgb(255, 255, 255, 0),
            Color.FromArgb(255, 50, 205, 50),
            Color.FromArgb(255, 255, 165, 0),
            Color.FromArgb(255, 255, 0, 255),
            Color.FromArgb(255, 255, 255, 255),
            Color.FromArgb(255, 0, 0, 0),

            Color.FromArgb(128, 255, 0, 0),
            Color.FromArgb(128, 0, 191, 255),
            Color.FromArgb(96, 255, 255, 0),
            Color.FromArgb(128, 50, 205, 50),
            Color.FromArgb(128, 255, 165, 0),
            Color.FromArgb(128, 255, 0, 255),
            Color.FromArgb(110, 255, 255, 255),
            Color.FromArgb(110, 0, 0, 0)
        };
    }

    private void BuildPresetColorButtons()
    {
        PresetColorGrid.Children.Clear();

        for (int i = 0; i < _presetColors.Count; i++)
        {
            Color color = _presetColors[i];

            var button = CreateColorSwatchButton(color);
            button.Tag = new PresetColorSlot
            {
                Index = i,
                Color = color
            };

            button.ToolTip = $"{GetColorDisplayText(color)}  (クリック: 選択 / ダブルクリック: 編集)";
            button.PreviewMouseLeftButtonDown += PresetColorButton_PreviewMouseLeftButtonDown;

            PresetColorGrid.Children.Add(button);
        }
    }

    private void BuildRecentColorButtons()
    {
        RecentColorGrid.Children.Clear();

        foreach (Color color in _recentColors)
        {
            var button = CreateColorSwatchButton(color);
            button.Click += RecentColorButton_Click;
            RecentColorGrid.Children.Add(button);
        }
    }

    private Button CreateColorSwatchButton(Color color)
    {
        return new Button
        {
            Style = (Style)FindResource("ColorSwatchButtonStyle"),
            Background = new SolidColorBrush(color),
            Tag = color,
            ToolTip = GetColorDisplayText(color)
        };
    }

    private static string GetColorDisplayText(Color color)
    {
        int alphaPercent = (int)Math.Round(color.A * 100.0 / 255.0);
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2} ({alphaPercent}%)";
    }

    private void PresetColorButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button || button.Tag is not PresetColorSlot slot)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            e.Handled = true;
            EditPresetColor(slot.Index);
            return;
        }

        ApplyPenColor(slot.Color, addToRecent: true);
    }

    private void EditPresetColor(int index)
    {
        if (index < 0 || index >= _presetColors.Count)
        {
            return;
        }

        Color original = _presetColors[index];

        var dialog = new ColorPickerDialog(original, BuildCustomColors())
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        CaptureCustomColors(dialog.CustomColors);

        Color updated = dialog.SelectedColor;
        _presetColors[index] = updated;
        SaveAppSettings();
        BuildPresetColorButtons();

        ApplyPenColor(updated, addToRecent: true);
    }

    private void RecentColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not Color color)
        {
            return;
        }

        ApplyPenColor(color, addToRecent: true);
        ColorPopup.IsOpen = false;
    }

    private void ApplyPenColor(Color color, bool addToRecent)
    {
        _currentPenColor = color;
        DrawingCanvas.DefaultDrawingAttributes = CreatePenAttributes(_currentPenColor, _currentPenWidth);

        ColorButton.Foreground = new SolidColorBrush(_currentPenColor);
        ColorButton.FontWeight = FontWeights.Bold;

        if (_activeTextBox != null)
        {
            _activeTextBox.Foreground = new SolidColorBrush(_editingTextOriginalColor ?? _currentPenColor);
            _activeTextBox.CaretBrush = new SolidColorBrush(_editingTextOriginalColor ?? _currentPenColor);
        }

        if (addToRecent)
        {
            AddRecentColor(color);
        }

        SaveAppSettings();
    }

    private void AddRecentColor(Color color)
    {
        _recentColors.RemoveAll(c => c == color);
        _recentColors.Insert(0, color);

        if (_recentColors.Count > MaxRecentColors)
        {
            _recentColors.RemoveRange(MaxRecentColors, _recentColors.Count - MaxRecentColors);
        }

        SaveAppSettings();
        BuildRecentColorButtons();
    }

    private static DrawingAttributes CreatePenAttributes(Color color, double width)
    {
        return new DrawingAttributes
        {
            Color = color,
            Width = width,
            Height = width,
            FitToCurve = false,
            IgnorePressure = true,
            StylusTip = StylusTip.Ellipse,
            IsHighlighter = false
        };
    }

    private void DrawingCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThroughEnabled)
        {
            return;
        }

        if (_currentTool == ToolMode.Pen && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            _currentInteractionState = InteractionState.DrawingPen;
        }

        if (_currentTool == ToolMode.Text)
        {
            if (IsClickOnCommittedTextElement(e.OriginalSource))
            {
                return;
            }

            Point startPoint = e.GetPosition(DrawingCanvas);

            if (_activeTextBox != null)
            {
                CommitActiveTextInput();
            }

            BeginTextInput(startPoint);
            e.Handled = true;
            return;
        }

        if (_currentTool == ToolMode.Rectangle)
        {
            CommitActiveTextInput();

            _isRectangleDrawing = true;
            _currentInteractionState = InteractionState.DrawingRect;
            _rectangleStartPoint = e.GetPosition(DrawingCanvas);

            CancelRectanglePreview();

            DrawingCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (_currentTool == ToolMode.Eraser)
        {
            CommitActiveTextInput();
            BeginEraserGesture();
            _currentInteractionState = InteractionState.Erasing;
            return;
        }

        if (_currentTool != ToolMode.Pen)
        {
            CommitActiveTextInput();
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            return;
        }

        CommitActiveTextInput();

        _isStraightLineDrawing = true;
        _currentInteractionState = ShouldDrawArrowForCurrentGesture()
            ? InteractionState.DrawingArrow
            : InteractionState.DrawingLine;
        _straightLineStartPoint = e.GetPosition(DrawingCanvas);

        CancelStraightLinePreview();

        DrawingCanvas.CaptureMouse();
        e.Handled = true;
    }

    private bool IsClickOnCommittedTextElement(object originalSource)
    {
        DependencyObject? current = originalSource as DependencyObject;

        while (current != null)
        {
            if (current is Border border && _textElements.Contains(border))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void DrawingCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isRectangleDrawing)
        {
            Point currentPoint = e.GetPosition(DrawingCanvas);
            UpdateRectanglePreview(_rectangleStartPoint, currentPoint);
            e.Handled = true;
            return;
        }

        if (_currentTool == ToolMode.Eraser && _isEraserGestureActive)
        {
            return;
        }

        if (!_isStraightLineDrawing)
        {
            return;
        }

        Point currentPointLine = e.GetPosition(DrawingCanvas);
        UpdateStraightLinePreview(_straightLineStartPoint, currentPointLine);
        e.Handled = true;
    }

    private void DrawingCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isRectangleDrawing)
        {
            Point endPoint = e.GetPosition(DrawingCanvas);
            CommitRectangle(_rectangleStartPoint, endPoint);

            _isRectangleDrawing = false;
            _currentInteractionState = InteractionState.None;
            DrawingCanvas.ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

        if (_currentTool == ToolMode.Eraser && _isEraserGestureActive)
        {
            CompleteEraserGestureDeferred();
            return;
        }

        if (!_isStraightLineDrawing)
        {
            return;
        }

        Point endPointLine = e.GetPosition(DrawingCanvas);
        CommitStraightLine(_straightLineStartPoint, endPointLine);

        _isStraightLineDrawing = false;
        _currentInteractionState = InteractionState.None;
        DrawingCanvas.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void DrawingCanvas_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_isClickThroughEnabled || _currentTool != ToolMode.Text)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        int direction = GetWheelDirection(e.Delta);
        if (direction == 0)
        {
            return;
        }

        if (TryAdjustTextFontSizeFromWheel(e.OriginalSource, direction))
        {
            e.Handled = true;
        }
    }

    private void DrawingCanvas_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isRectangleDrawing)
        {
            CancelRectanglePreview();
            _isRectangleDrawing = false;
            _currentInteractionState = InteractionState.None;
        }

        if (_isStraightLineDrawing)
        {
            CancelStraightLinePreview();
            _isStraightLineDrawing = false;
            _currentInteractionState = InteractionState.None;
        }

        if (_isEraserGestureActive)
        {
            CompleteEraserGestureDeferred();
        }
    }

    private void UpdateStraightLinePreview(Point startPoint, Point endPoint)
    {
        CancelStraightLinePreview();

        _straightLinePreviewStroke = CreateStraightLineStroke(startPoint, endPoint);
        ExecuteWithoutStrokeHistory(() => DrawingCanvas.Strokes.Add(_straightLinePreviewStroke));

        if (ShouldDrawArrowForCurrentGesture())
        {
            _straightLinePreviewArrowHeadStroke = CreateArrowHeadStroke(startPoint, endPoint);
            if (_straightLinePreviewArrowHeadStroke != null)
            {
                ExecuteWithoutStrokeHistory(() => DrawingCanvas.Strokes.Add(_straightLinePreviewArrowHeadStroke));
            }
        }
    }

    private void CommitStraightLine(Point startPoint, Point endPoint)
    {
        CancelStraightLinePreview();

        Stroke finalStroke = CreateStraightLineStroke(startPoint, endPoint);

        if (!ShouldDrawArrowForCurrentGesture())
        {
            DrawingCanvas.Strokes.Add(finalStroke);
            return;
        }

        Stroke? arrowHeadStroke = CreateArrowHeadStroke(startPoint, endPoint);

        if (arrowHeadStroke == null)
        {
            DrawingCanvas.Strokes.Add(finalStroke);
            return;
        }

        ExecuteWithoutStrokeHistory(() =>
        {
            DrawingCanvas.Strokes.Add(finalStroke);
            DrawingCanvas.Strokes.Add(arrowHeadStroke);
        });

        PushHistory(new StrokeCollectionAction(
            new[] { finalStroke, arrowHeadStroke },
            Array.Empty<Stroke>()));
    }

    private void CancelStraightLinePreview()
    {
        if (_straightLinePreviewStroke != null)
        {
            Stroke previewStroke = _straightLinePreviewStroke;
            ExecuteWithoutStrokeHistory(() => DrawingCanvas.Strokes.Remove(previewStroke));
            _straightLinePreviewStroke = null;
        }

        if (_straightLinePreviewArrowHeadStroke != null)
        {
            Stroke previewArrowHeadStroke = _straightLinePreviewArrowHeadStroke;
            ExecuteWithoutStrokeHistory(() => DrawingCanvas.Strokes.Remove(previewArrowHeadStroke));
            _straightLinePreviewArrowHeadStroke = null;
        }
    }

    private Stroke CreateStraightLineStroke(Point startPoint, Point endPoint)
    {
        var stylusPoints = new StylusPointCollection
        {
            new StylusPoint(startPoint.X, startPoint.Y),
            new StylusPoint(endPoint.X, endPoint.Y)
        };

        var stroke = new Stroke(stylusPoints)
        {
            DrawingAttributes = CreatePenAttributes(_currentPenColor, _currentPenWidth)
        };

        return stroke;
    }

    private bool ShouldDrawArrowForCurrentGesture()
    {
        return (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
    }

    private Stroke? CreateArrowHeadStroke(Point startPoint, Point endPoint)
    {
        Vector direction = startPoint - endPoint;
        double length = direction.Length;
        if (length < 6.0)
        {
            return null;
        }

        direction.Normalize();
        Vector perpendicular = new(-direction.Y, direction.X);

        double size = Math.Max(8.0, _currentPenWidth * 3.0);
        Point p1 = endPoint + (direction * size) + (perpendicular * size * 0.5);
        Point p2 = endPoint + (direction * size) - (perpendicular * size * 0.5);

        var stylusPoints = new StylusPointCollection
        {
            new StylusPoint(p1.X, p1.Y),
            new StylusPoint(endPoint.X, endPoint.Y),
            new StylusPoint(p2.X, p2.Y)
        };

        var stroke = new Stroke(stylusPoints)
        {
            DrawingAttributes = CreatePenAttributes(_currentPenColor, _currentPenWidth)
        };

        return stroke;
    }

    private void UpdateRectanglePreview(Point startPoint, Point endPoint)
    {
        CancelRectanglePreview();

        _rectanglePreviewStroke = CreateRectangleStroke(startPoint, endPoint);
        ExecuteWithoutStrokeHistory(() => DrawingCanvas.Strokes.Add(_rectanglePreviewStroke));
    }

    private void CommitRectangle(Point startPoint, Point endPoint)
    {
        CancelRectanglePreview();

        if (Math.Abs(endPoint.X - startPoint.X) < 1 && Math.Abs(endPoint.Y - startPoint.Y) < 1)
        {
            return;
        }

        Stroke finalStroke = CreateRectangleStroke(startPoint, endPoint);
        DrawingCanvas.Strokes.Add(finalStroke);
    }

    private void CancelRectanglePreview()
    {
        if (_rectanglePreviewStroke != null)
        {
            Stroke previewStroke = _rectanglePreviewStroke;
            ExecuteWithoutStrokeHistory(() => DrawingCanvas.Strokes.Remove(previewStroke));
            _rectanglePreviewStroke = null;
        }
    }

    private Stroke CreateRectangleStroke(Point startPoint, Point endPoint)
    {
        double left = Math.Min(startPoint.X, endPoint.X);
        double top = Math.Min(startPoint.Y, endPoint.Y);
        double right = Math.Max(startPoint.X, endPoint.X);
        double bottom = Math.Max(startPoint.Y, endPoint.Y);

        var stylusPoints = new StylusPointCollection
        {
            new StylusPoint(left, top),
            new StylusPoint(right, top),
            new StylusPoint(right, bottom),
            new StylusPoint(left, bottom),
            new StylusPoint(left, top)
        };

        var stroke = new Stroke(stylusPoints)
        {
            DrawingAttributes = CreatePenAttributes(_currentPenColor, _currentPenWidth)
        };

        return stroke;
    }

    private void BeginTextInput(Point startPoint)
    {
        CancelActiveTextInput();
        EndTextElementDrag();

        _activeTextStartPoint = startPoint;

        var textBox = new TextBox
        {
            MinWidth = TextMinWidth,
            MaxWidth = TextMaxWidth,
            FontFamily = CreateFontFamilySafe(_editingTextOriginalFontFamilyName ?? _currentTextFontFamilyName),
            FontSize = _editingTextOriginalFontSize ?? _currentTextFontSize,
            FontStyle = _editingTextOriginalFontStyle ?? _currentTextFontStyle,
            FontWeight = _editingTextOriginalFontWeight ?? _currentTextFontWeight,
            Foreground = new SolidColorBrush(_editingTextOriginalColor ?? _currentPenColor),
            CaretBrush = new SolidColorBrush(_editingTextOriginalColor ?? _currentPenColor),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.DimGray,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(TextPaddingX, TextPaddingY, TextPaddingX, TextPaddingY),
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        textBox.KeyDown += ActiveTextBox_KeyDown;
        textBox.LostKeyboardFocus += ActiveTextBox_LostKeyboardFocus;
        textBox.TextChanged += ActiveTextBox_TextChanged;

        _activeTextBox = textBox;
        _currentInteractionState = InteractionState.EditingText;

        DrawingCanvas.Children.Add(textBox);
        InkCanvas.SetLeft(textBox, startPoint.X);
        InkCanvas.SetTop(textBox, startPoint.Y);

        UpdateActiveTextBoxSize(textBox);

        textBox.Focus();
        textBox.SelectAll();
    }

    private void ActiveTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                return;
            }

            CommitActiveTextInput();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            CancelActiveTextInput();
            e.Handled = true;
        }
    }

    private void ActiveTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_activeTextBox == null)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_activeTextBox != null)
            {
                CommitActiveTextInput();
            }
        }), DispatcherPriority.Background);
    }

    private void ActiveTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        UpdateActiveTextBoxSize(textBox);
    }

    private void UpdateActiveTextBoxSize(TextBox textBox)
    {
        textBox.Width = MeasureTextBoxWidth(textBox);
        textBox.Height = double.NaN;
        textBox.UpdateLayout();
    }

    private double MeasureTextBoxWidth(TextBox textBox)
    {
        string[] lines = (textBox.Text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0)
        {
            lines = new[] { " " };
        }

        double maxLineWidth = 0.0;

        foreach (string rawLine in lines)
        {
            string line = string.IsNullOrEmpty(rawLine) ? " " : rawLine;

            var formattedText = new FormattedText(
                line,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(textBox.FontFamily, textBox.FontStyle, textBox.FontWeight, textBox.FontStretch),
                textBox.FontSize,
                textBox.Foreground,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            if (formattedText.WidthIncludingTrailingWhitespace > maxLineWidth)
            {
                maxLineWidth = formattedText.WidthIncludingTrailingWhitespace;
            }
        }

        double width = maxLineWidth + 16.0;
        width = Math.Max(TextMinWidth, width);
        width = Math.Min(TextMaxWidth, width);

        return width;
    }

    private void CommitActiveTextInput()
    {
        if (_activeTextBox == null)
        {
            return;
        }

        string text = _activeTextBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            text = string.Empty;
        }
        else
        {
            text = text.Replace("\r\n", "\n").TrimEnd();
        }

        TextBox textBox = _activeTextBox;
        _activeTextBox = null;

        Color commitColor = _currentPenColor;
        if (textBox.Foreground is SolidColorBrush foregroundBrush)
        {
            commitColor = foregroundBrush.Color;
        }

        string commitFontFamilyName = textBox.FontFamily.Source;
        double commitFontSize = NormalizeTextFontSize(textBox.FontSize);
        FontStyle commitFontStyle = textBox.FontStyle;
        FontWeight commitFontWeight = textBox.FontWeight;

        textBox.KeyDown -= ActiveTextBox_KeyDown;
        textBox.LostKeyboardFocus -= ActiveTextBox_LostKeyboardFocus;
        textBox.TextChanged -= ActiveTextBox_TextChanged;

        DrawingCanvas.Children.Remove(textBox);

        Border? originalElement = _editingTextOriginalElement;

        if (text.Length == 0)
        {
            if (originalElement != null)
            {
                PushHistory(new TextRemoveAction(originalElement));
            }

            _editingTextOriginalElement = null;
            _editingTextOriginalColor = null;
            _editingTextOriginalFontFamilyName = null;
            _editingTextOriginalFontSize = null;
            _editingTextOriginalFontStyle = null;
            _editingTextOriginalFontWeight = null;
            _currentInteractionState = InteractionState.None;
            return;
        }

        Border committed = CreateCommittedTextElement(
            text,
            _activeTextStartPoint,
            commitColor,
            commitFontFamilyName,
            commitFontSize,
            commitFontStyle,
            commitFontWeight);

        AddCommittedTextElement(committed);

        if (originalElement != null)
        {
            PushHistory(new TextReplaceAction(originalElement, committed));
        }
        else
        {
            PushHistory(new TextAddAction(committed));
        }

        _editingTextOriginalElement = null;
        _editingTextOriginalColor = null;
        _editingTextOriginalFontFamilyName = null;
        _editingTextOriginalFontSize = null;
        _editingTextOriginalFontStyle = null;
        _editingTextOriginalFontWeight = null;
        _currentInteractionState = InteractionState.None;
    }

    private void CancelActiveTextInput()
    {
        if (_activeTextBox == null)
        {
            return;
        }

        TextBox textBox = _activeTextBox;
        _activeTextBox = null;

        textBox.KeyDown -= ActiveTextBox_KeyDown;
        textBox.LostKeyboardFocus -= ActiveTextBox_LostKeyboardFocus;
        textBox.TextChanged -= ActiveTextBox_TextChanged;

        DrawingCanvas.Children.Remove(textBox);

        if (_editingTextOriginalElement != null)
        {
            AddCommittedTextElement(_editingTextOriginalElement);
            _editingTextOriginalElement = null;
        }

        _editingTextOriginalColor = null;
        _editingTextOriginalFontFamilyName = null;
        _editingTextOriginalFontSize = null;
        _editingTextOriginalFontStyle = null;
        _editingTextOriginalFontWeight = null;
        _currentInteractionState = InteractionState.None;
    }

    private Border CreateCommittedTextElement(
        string text,
        Point startPoint,
        Color color,
        string fontFamilyName,
        double fontSize,
        FontStyle fontStyle,
        FontWeight fontWeight)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(color),
            FontFamily = CreateFontFamilySafe(fontFamilyName),
            FontSize = fontSize,
            FontStyle = fontStyle,
            FontWeight = fontWeight,
            Background = Brushes.Transparent,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = TextMaxWidth
        };

        var host = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(TextPaddingX, TextPaddingY, TextPaddingX, TextPaddingY),
            Child = textBlock,
            Focusable = false,
            IsHitTestVisible = true,
            Cursor = Cursors.SizeAll,
            MaxWidth = TextMaxWidth + (TextPaddingX * 2.0)
        };

        AttachTextElementHandlers(host);

        InkCanvas.SetLeft(host, startPoint.X);
        InkCanvas.SetTop(host, startPoint.Y);

        return host;
    }

    private void AttachTextElementHandlers(Border host)
    {
        host.MouseLeftButtonDown += TextElement_MouseLeftButtonDown;
        host.MouseMove += TextElement_MouseMove;
        host.MouseLeftButtonUp += TextElement_MouseLeftButtonUp;
        host.LostMouseCapture += TextElement_LostMouseCapture;
    }

    private void DetachTextElementHandlers(Border host)
    {
        host.MouseLeftButtonDown -= TextElement_MouseLeftButtonDown;
        host.MouseMove -= TextElement_MouseMove;
        host.MouseLeftButtonUp -= TextElement_MouseLeftButtonUp;
        host.LostMouseCapture -= TextElement_LostMouseCapture;
    }

    private void TextElement_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThroughEnabled || _currentTool != ToolMode.Text)
        {
            return;
        }

        if (sender is not Border host)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            BeginTextEdit(host);
            e.Handled = true;
            return;
        }

        if (_activeTextBox != null)
        {
            CommitActiveTextInput();
        }

        _draggingTextElement = host;
        _isDraggingTextElement = true;
        _textDragStartMousePoint = e.GetPosition(DrawingCanvas);
        _textDragStartElementPoint = new Point(
            InkCanvas.GetLeft(host),
            InkCanvas.GetTop(host));

        if (double.IsNaN(_textDragStartElementPoint.X))
        {
            _textDragStartElementPoint.X = 0;
        }

        if (double.IsNaN(_textDragStartElementPoint.Y))
        {
            _textDragStartElementPoint.Y = 0;
        }

        _textDragCommittedStartPoint = _textDragStartElementPoint;
        _currentInteractionState = InteractionState.MovingText;

        host.CaptureMouse();
        e.Handled = true;
    }

    private void TextElement_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingTextElement || _draggingTextElement == null)
        {
            return;
        }

        if (sender != _draggingTextElement)
        {
            return;
        }

        Point currentMousePoint = e.GetPosition(DrawingCanvas);
        Vector delta = currentMousePoint - _textDragStartMousePoint;

        double newLeft = _textDragStartElementPoint.X + delta.X;
        double newTop = _textDragStartElementPoint.Y + delta.Y;

        InkCanvas.SetLeft(_draggingTextElement, newLeft);
        InkCanvas.SetTop(_draggingTextElement, newTop);

        e.Handled = true;
    }

    private void TextElement_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingTextElement || _draggingTextElement == null)
        {
            return;
        }

        if (sender is not Border host || host != _draggingTextElement)
        {
            return;
        }

        Point endPoint = GetTextElementPosition(host);

        host.ReleaseMouseCapture();
        EndTextElementDrag();

        if (!ArePointsClose(_textDragCommittedStartPoint, endPoint))
        {
            PushHistory(new TextMoveAction(host, _textDragCommittedStartPoint, endPoint));
        }

        e.Handled = true;
    }

    private void TextElement_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (sender is Border host && host == _draggingTextElement)
        {
            EndTextElementDrag();
        }
    }

    private void CancelTextElementDrag()
    {
        if (_draggingTextElement != null)
        {
            SetTextElementPosition(_draggingTextElement, _textDragCommittedStartPoint);
        }

        EndTextElementDrag();
    }

    private void EndTextElementDrag()
    {
        if (_draggingTextElement != null && _draggingTextElement.IsMouseCaptured)
        {
            _draggingTextElement.ReleaseMouseCapture();
        }

        _draggingTextElement = null;
        _isDraggingTextElement = false;

        if (_currentInteractionState == InteractionState.MovingText)
        {
            _currentInteractionState = InteractionState.None;
        }
    }

    private void BeginTextEdit(Border host)
    {
        CancelActiveTextInput();
        EndTextElementDrag();

        if (host.Child is not TextBlock textBlock)
        {
            return;
        }

        string text = textBlock.Text;
        Color color = Colors.Red;

        if (textBlock.Foreground is SolidColorBrush brush)
        {
            color = brush.Color;
        }

        string fontFamilyName = textBlock.FontFamily.Source;
        double fontSize = textBlock.FontSize;
        FontStyle fontStyle = textBlock.FontStyle;
        FontWeight fontWeight = textBlock.FontWeight;

        double left = InkCanvas.GetLeft(host);
        double top = InkCanvas.GetTop(host);

        _editingTextOriginalElement = host;
        _editingTextOriginalColor = color;
        _editingTextOriginalFontFamilyName = fontFamilyName;
        _editingTextOriginalFontSize = fontSize;
        _editingTextOriginalFontStyle = fontStyle;
        _editingTextOriginalFontWeight = fontWeight;

        RemoveCommittedTextElement(host);

        BeginTextInput(new Point(left, top));

        if (_activeTextBox != null)
        {
            _activeTextBox.Text = text;
            _activeTextBox.CaretIndex = _activeTextBox.Text.Length;
            _activeTextBox.Select(_activeTextBox.Text.Length, 0);
            UpdateActiveTextBoxSize(_activeTextBox);
        }
    }

    private bool TryAdjustTextFontSizeFromWheel(object originalSource, int direction)
    {
        if (direction == 0)
        {
            return false;
        }

        if (_activeTextBox != null && IsDescendantOfElement(originalSource, _activeTextBox))
        {
            double nextFontSize = NormalizeTextFontSize(_activeTextBox.FontSize + direction);
            _activeTextBox.FontSize = nextFontSize;
            UpdateActiveTextBoxSize(_activeTextBox);

            _currentTextFontSize = nextFontSize;
            SaveAppSettings();

            return true;
        }

        Border? host = FindCommittedTextHost(originalSource);
        if (host?.Child is TextBlock textBlock)
        {
            double nextFontSize = NormalizeTextFontSize(textBlock.FontSize + direction);
            textBlock.FontSize = nextFontSize;
            SelectTextFontSize(nextFontSize);
            return true;
        }

        SelectTextFontSize(_currentTextFontSize + direction);
        return true;
    }

    private Border? FindCommittedTextHost(object originalSource)
    {
        DependencyObject? current = originalSource as DependencyObject;

        while (current != null)
        {
            if (current is Border border && _textElements.Contains(border))
            {
                return border;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static bool IsDescendantOfElement(object originalSource, UIElement element)
    {
        DependencyObject? current = originalSource as DependencyObject;

        while (current != null)
        {
            if (ReferenceEquals(current, element))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void AddCommittedTextElement(Border host)
    {
        if (_textElements.Contains(host))
        {
            return;
        }

        DrawingCanvas.Children.Add(host);
        _textElements.Add(host);
    }

    private void RemoveCommittedTextElement(Border host)
    {
        if (_draggingTextElement == host)
        {
            EndTextElementDrag();
        }

        DrawingCanvas.Children.Remove(host);
        _textElements.Remove(host);
    }

    private List<Border> GetCommittedTextElementsSnapshot()
    {
        return new List<Border>(_textElements);
    }

    private void RemoveCommittedTextElements()
    {
        EndTextElementDrag();

        foreach (Border element in GetCommittedTextElementsSnapshot())
        {
            RemoveCommittedTextElement(element);
        }
    }

    private static bool ArePointsClose(Point a, Point b)
    {
        return Math.Abs(a.X - b.X) < 0.1 && Math.Abs(a.Y - b.Y) < 0.1;
    }

    private static Point GetTextElementPosition(Border host)
    {
        double left = InkCanvas.GetLeft(host);
        double top = InkCanvas.GetTop(host);

        if (double.IsNaN(left))
        {
            left = 0;
        }

        if (double.IsNaN(top))
        {
            top = 0;
        }

        return new Point(left, top);
    }

    private void SetTextElementPosition(Border host, Point point)
    {
        InkCanvas.SetLeft(host, point.X);
        InkCanvas.SetTop(host, point.Y);
    }

    private void SelectPenWidth(double width)
    {
        _currentPenWidth = NormalizePenWidth(width);
        DrawingCanvas.DefaultDrawingAttributes = CreatePenAttributes(_currentPenColor, _currentPenWidth);
        SaveAppSettings();
    }

    private void SelectTextFont(
        string fontFamilyName,
        double fontSize,
        FontStyle fontStyle,
        FontWeight fontWeight)
    {
        _currentTextFontFamilyName = NormalizeTextFontFamilyName(fontFamilyName);
        _currentTextFontSize = NormalizeTextFontSize(fontSize);
        _currentTextFontStyle = fontStyle;
        _currentTextFontWeight = fontWeight;

        if (_activeTextBox != null)
        {
            _activeTextBox.FontFamily = CreateFontFamilySafe(_currentTextFontFamilyName);
            _activeTextBox.FontSize = _currentTextFontSize;
            _activeTextBox.FontStyle = _currentTextFontStyle;
            _activeTextBox.FontWeight = _currentTextFontWeight;
            UpdateActiveTextBoxSize(_activeTextBox);
        }

        SaveAppSettings();
    }

    private void SelectTextFontSize(double fontSize)
    {
        SelectTextFont(
            _currentTextFontFamilyName,
            fontSize,
            _currentTextFontStyle,
            _currentTextFontWeight);
    }

    private void SetButtonSelected(Button selectedButton, params Button[] group)
    {
        foreach (var button in group)
        {
            button.Background = _normalButtonBackground;
            button.Foreground = _normalButtonForeground;
            button.FontWeight = FontWeights.Normal;
        }

        selectedButton.Background = _selectedButtonBackground;
        selectedButton.Foreground = _selectedButtonForeground;
        selectedButton.FontWeight = FontWeights.Bold;
    }

    private void UpdateToolHighlight()
    {
        Button selectedButton = _currentTool switch
        {
            ToolMode.Pen => PenButton,
            ToolMode.Rectangle => RectangleButton,
            ToolMode.Text => TextButton,
            _ => EraserButton
        };

        SetButtonSelected(selectedButton, PenButton, RectangleButton, TextButton, EraserButton);
    }

    private void PenButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
        {
            return;
        }

        FinalizeOrCancelCurrentOperation();

        _isStraightLineDrawing = false;
        _isRectangleDrawing = false;

        _currentTool = ToolMode.Pen;
        DrawingCanvas.EditingMode = InkCanvasEditingMode.Ink;
        UpdateToolHighlight();

        var dialog = new PenWidthDialog(_currentPenWidth, _currentPenColor)
        {
            Owner = this
        };

        bool? result = dialog.ShowDialog();
        if (result == true)
        {
            SelectPenWidth(dialog.SelectedWidth);
        }

        e.Handled = true;
    }

    private void PenButton_Click(object sender, RoutedEventArgs e)
    {
        FinalizeOrCancelCurrentOperation();

        _isStraightLineDrawing = false;
        _isRectangleDrawing = false;

        _currentTool = ToolMode.Pen;
        DrawingCanvas.EditingMode = InkCanvasEditingMode.Ink;
        UpdateToolHighlight();
    }

    private void RectangleButton_Click(object sender, RoutedEventArgs e)
    {
        FinalizeOrCancelCurrentOperation();

        _isStraightLineDrawing = false;
        _isRectangleDrawing = false;

        _currentTool = ToolMode.Rectangle;
        DrawingCanvas.EditingMode = InkCanvasEditingMode.None;
        UpdateToolHighlight();
    }

    private void TextButton_Click(object sender, RoutedEventArgs e)
    {
        FinalizeOrCancelCurrentOperation();

        _isStraightLineDrawing = false;
        _isRectangleDrawing = false;

        _currentTool = ToolMode.Text;
        DrawingCanvas.EditingMode = InkCanvasEditingMode.None;
        UpdateToolHighlight();
    }

    private void TextButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
        {
            return;
        }

        _currentTool = ToolMode.Text;
        DrawingCanvas.EditingMode = InkCanvasEditingMode.None;
        UpdateToolHighlight();

        ShowFontDialog();
        e.Handled = true;
    }

    private void EraserButton_Click(object sender, RoutedEventArgs e)
    {
        FinalizeOrCancelCurrentOperation();

        _isStraightLineDrawing = false;
        _isRectangleDrawing = false;

        _currentTool = ToolMode.Eraser;
        DrawingCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
        UpdateToolHighlight();
    }

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        ColorPopup.IsOpen = true;
    }

    private void ColorButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
        {
            return;
        }

        e.Handled = true;
        OpenCurrentColorEditor();
    }

    private void OpenCurrentColorEditor()
    {
        var dialog = new ColorPickerDialog(_currentPenColor, BuildCustomColors())
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        CaptureCustomColors(dialog.CustomColors);
        ApplyPenColor(dialog.SelectedColor, addToRecent: true);
        ColorPopup.IsOpen = false;
    }

    private void ShowFontDialog()
    {
        Drawing.FontStyle drawingFontStyle = ToDrawingFontStyle(_currentTextFontWeight, _currentTextFontStyle);

        using var dialog = new Forms.FontDialog
        {
            ShowColor = false,
            ShowEffects = false,
            FontMustExist = true,
            AllowVerticalFonts = false,
            MinSize = (int)Math.Round(MinTextFontSize),
            MaxSize = (int)Math.Round(MaxTextFontSize),
            Font = new Drawing.Font(
                NormalizeTextFontFamilyName(_currentTextFontFamilyName),
                (float)_currentTextFontSize,
                drawingFontStyle)
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            FontWeight selectedWeight = dialog.Font.Bold ? FontWeights.Bold : FontWeights.Normal;
            FontStyle selectedStyle = dialog.Font.Italic ? FontStyles.Italic : FontStyles.Normal;

            SelectTextFont(
                dialog.Font.FontFamily.Name,
                dialog.Font.Size,
                selectedStyle,
                selectedWeight);
        }
    }

    private int[] BuildCustomColors()
    {
        int[] customColors = new int[MaxCustomColors];

        for (int i = 0; i < Math.Min(_customColorValues.Count, MaxCustomColors); i++)
        {
            customColors[i] = _customColorValues[i];
        }

        return customColors;
    }

    private void CaptureCustomColors(int[] customColors)
    {
        _customColorValues.Clear();

        foreach (int value in customColors)
        {
            int normalized = value & 0x00FFFFFF;
            if (normalized == 0)
            {
                continue;
            }

            _customColorValues.Add(normalized);

            if (_customColorValues.Count >= MaxCustomColors)
            {
                break;
            }
        }

        SaveAppSettings();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        FinalizeOrCancelCurrentOperation();

        List<Stroke> removedStrokes = ToStrokeList(DrawingCanvas.Strokes);
        List<Border> removedTextElements = GetCommittedTextElementsSnapshot();

        if (removedStrokes.Count == 0 && removedTextElements.Count == 0)
        {
            return;
        }

        ExecuteWithoutStrokeHistory(() => DrawingCanvas.Strokes.Clear());
        RemoveCommittedTextElements();

        PushHistory(new ClearAction(removedStrokes, removedTextElements));
    }

    private void ClickThroughButton_Click(object sender, RoutedEventArgs e)
    {
        SetClickThrough(!_isClickThroughEnabled);
    }

    private void SetClickThrough(bool enabled)
    {
        FinalizeOrCancelCurrentOperation();

        _isStraightLineDrawing = false;
        _isRectangleDrawing = false;

        _isClickThroughEnabled = enabled;

        ToolbarPanel.Visibility = enabled
            ? Visibility.Collapsed
            : Visibility.Visible;

        ColorPopup.IsOpen = false;

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();

            if (enabled)
            {
                exStyle |= WS_EX_LAYERED;
                exStyle |= WS_EX_TRANSPARENT;

                DrawingCanvas.IsHitTestVisible = false;
                DrawingCanvas.EditingMode = InkCanvasEditingMode.None;
                ClickThroughButton.Content = "CT: ON";
            }
            else
            {
                exStyle |= WS_EX_LAYERED;
                exStyle &= ~WS_EX_TRANSPARENT;

                DrawingCanvas.IsHitTestVisible = true;
                DrawingCanvas.EditingMode = _currentTool switch
                {
                    ToolMode.Pen => InkCanvasEditingMode.Ink,
                    ToolMode.Rectangle => InkCanvasEditingMode.None,
                    ToolMode.Text => InkCanvasEditingMode.None,
                    _ => InkCanvasEditingMode.EraseByPoint
                };
                ClickThroughButton.Content = "CT: OFF";
            }

            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
        }

        UpdateNotifyIconMenu();

        if (enabled)
        {
            ShowClickThroughToastIfNeeded();
        }
        else
        {
            HideToastMessage();
        }
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        CommitActiveTextInput();
        EndTextElementDrag();
        Close();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID_TOGGLE_CLICKTHROUGH)
        {
            SetClickThrough(!_isClickThroughEnabled);
            handled = true;
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex)
            : GetWindowLongPtr32(hWnd, nIndex);
    }

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : SetWindowLongPtr32(hWnd, nIndex, dwNewLong);
    }
}
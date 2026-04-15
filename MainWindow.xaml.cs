using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
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
    private readonly DispatcherTimer _colorButtonClickTimer = new();
    private readonly DispatcherTimer _presetColorClickTimer = new();
    private readonly DispatcherTimer _penButtonClickTimer = new();
    private readonly DispatcherTimer _penWidthPresetClickTimer = new();
    private readonly DispatcherTimer _clickThroughHoverTimer = new();
    private Forms.ToolStripMenuItem? _trayEnableClickThroughMenuItem;
    private Forms.ToolStripMenuItem? _trayDisableClickThroughMenuItem;
    private ToolMode _currentTool = ToolMode.Pen;
    private InteractionState _currentInteractionState = InteractionState.None;

    private readonly Cursor _penCursor = Cursors.Cross;
    private uint _clickThroughHotkeyModifiers = DefaultHotkeyModifiers;
    private Forms.Keys _clickThroughHotkeyKey = DefaultHotkeyKey;
    private bool _isHotKeyRegistered;
    private bool _isInitializing = true;

    private Color _currentPenColor = Color.FromArgb(255, 255, 0, 0);
    private double _currentPenWidth = 4;
    private string _currentTextFontFamilyName = DefaultTextFontFamilyName;
    private double _currentTextFontSize = DefaultTextFontSize;
    private FontStyle _currentTextFontStyle = FontStyles.Normal;
    private FontWeight _currentTextFontWeight = FontWeights.Normal;

    private List<Color> _presetColors = new();
    private List<Color> _recentColors = new();
    private List<int> _customColorValues = new();
    private List<double> _penWidthPresets = new();

    private bool _pendingPenButtonSingleClick;
    private int? _pendingPresetColorIndex;
    private int? _pendingPenWidthPresetIndex;

    private bool _isStraightLineDrawing;
    private Point _straightLineStartPoint;
    private Stroke? _straightLinePreviewStroke;
    private Stroke? _straightLinePreviewArrowHeadStroke;

    private bool _isRectangleDrawing;
    private Point _rectangleStartPoint;
    private Stroke? _rectanglePreviewStroke;
    private List<Stroke>? _rectanglePreviewFillStrokes;
    private bool _isCircleDrawing;
    private Point _circleStartPoint;
    private Stroke? _circlePreviewStroke;
    private List<Stroke>? _circlePreviewFillStrokes;
    private bool _isRectangleFilled;
    private int _rectangleFillOpacityPercent = 35;

    private TextBox? _activeTextBox;
    private Point _activeTextStartPoint;
    private readonly List<Border> _textElements = new();
    private Border? _selectedTextElement;

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

    private bool _isToolbarDragging;
    private Point _toolbarDragStartMousePoint;
    private Point _toolbarDragStartPanelPoint;
    private bool _hasPendingToolbarPosition;
    private double _toolbarLeft;
    private double _toolbarTop;

    private Border? _editingTextOriginalElement;
    private int? _editingTextOriginalStoredIndex;
    private Color? _editingTextOriginalColor;
    private string? _editingTextOriginalFontFamilyName;
    private double? _editingTextOriginalFontSize;
    private FontStyle? _editingTextOriginalFontStyle;
    private FontWeight? _editingTextOriginalFontWeight;

    private const int MaxRecentColors = 8;
    private const int MaxHistory = 200;
    private const int MaxCustomColors = 16;
    private const int PenWidthPresetCount = 5;

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
    private const int WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1;
    private const int HTTRANSPARENT = -1;
    private const int HOTKEY_ID_TOGGLE_CLICKTHROUGH = 1;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    private const uint DefaultHotkeyModifiers = MOD_CONTROL | MOD_ALT;
    private const Forms.Keys DefaultHotkeyKey = Forms.Keys.T;

    private const string AppSettingsFileName = "app-settings.json";
    private const string AppDataFolderName = "FlowInk";
    private const string ClickThroughOffIconPath = "Assets/mouse-pointer-2.png";
    private const string ClickThroughOnIconPath = "Assets/mouse-pointer-2-off.png";

    private const double ToolbarViewportMargin = 0.0;

    private enum ToolMode
    {
        Pen,
        Rectangle,
        Circle,
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
        DrawingCircle,
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
        private readonly int _index;

        public TextAddAction(Border element, int index)
        {
            _element = element;
            _index = index;
        }

        public void Undo(MainWindow window)
        {
            window.RemoveCommittedTextElement(_element);
        }

        public void Redo(MainWindow window)
        {
            window.AddCommittedTextElement(_element, _index);
        }
    }

    private sealed class TextRemoveAction : IUndoableAction
    {
        private readonly Border _element;
        private readonly int _index;

        public TextRemoveAction(Border element, int index)
        {
            _element = element;
            _index = index;
        }

        public void Undo(MainWindow window)
        {
            window.AddCommittedTextElement(_element, _index);
        }

        public void Redo(MainWindow window)
        {
            window.RemoveCommittedTextElement(_element);
        }
    }

    private sealed class TextReplaceAction : IUndoableAction
    {
        private readonly Border _before;
        private readonly int _beforeIndex;
        private readonly Border _after;
        private readonly int _afterIndex;

        public TextReplaceAction(Border before, int beforeIndex, Border after, int afterIndex)
        {
            _before = before;
            _beforeIndex = beforeIndex;
            _after = after;
            _afterIndex = afterIndex;
        }

        public void Undo(MainWindow window)
        {
            window.RemoveCommittedTextElement(_after);
            window.AddCommittedTextElement(_before, _beforeIndex);
        }

        public void Redo(MainWindow window)
        {
            window.RemoveCommittedTextElement(_before);
            window.AddCommittedTextElement(_after, _afterIndex);
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

    private sealed class ClearTextEntry
    {
        public ClearTextEntry(Border element, int index)
        {
            Element = element;
            Index = index;
        }

        public Border Element { get; }
        public int Index { get; }
    }

    private sealed class ClearAction : IUndoableAction
    {
        private readonly List<Stroke> _removedStrokes;
        private readonly List<ClearTextEntry> _removedTextEntries;

        public ClearAction(IEnumerable<Stroke> removedStrokes, IEnumerable<ClearTextEntry> removedTextEntries)
        {
            _removedStrokes = new List<Stroke>(removedStrokes);
            _removedTextEntries = new List<ClearTextEntry>(removedTextEntries);
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

            foreach (ClearTextEntry entry in _removedTextEntries)
            {
                window.AddCommittedTextElement(entry.Element, entry.Index);
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

            foreach (ClearTextEntry entry in _removedTextEntries)
            {
                window.RemoveCommittedTextElement(entry.Element);
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
        public List<double> PenWidthPresets { get; set; } = new();
        public string? CurrentColor { get; set; }
        public string? TextFontFamily { get; set; }
        public double TextFontSize { get; set; } = DefaultTextFontSize;
        public bool TextBold { get; set; }
        public bool TextItalic { get; set; }
        public bool RectangleFillEnabled { get; set; }
        public int RectangleFillOpacity { get; set; } = 35;
        public double? ToolbarLeft { get; set; }
        public double? ToolbarTop { get; set; }
        public bool HotkeyCtrl { get; set; } = true;
        public bool HotkeyAlt { get; set; } = true;
        public bool HotkeyShift { get; set; }
        public bool HotkeyWin { get; set; }
        public string? HotkeyKey { get; set; } = DefaultHotkeyKey.ToString();
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
        UpdateShapeButtonToolTips();
        UpdateRectangleSettingsUi();
        _penWidthPresets = NormalizePenWidthPresets(_penWidthPresets);

        BuildPresetColorButtons();
        BuildRecentColorButtons();
        BuildPenWidthPresetButtons();
        UpdateToolbarForCT();
        UpdateClickThroughButtonIcons();

        ApplyPenColor(_currentPenColor, addToRecent: false);
        _isInitializing = false;

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
        UpdateCursor();
        InitializeNotifyIcon();
        InitializeToastTimer();
        InitializeColorButtonClickTimer();
        InitializePresetColorClickTimer();
        InitializePenButtonClickTimer();
        InitializePenWidthPresetClickTimer();
        InitializeClickThroughHoverTimer();
        InitializeHotkeySettingsControls();

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        SizeChanged += MainWindow_SizeChanged;
    }


    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyInitialToolbarPosition();
        SetClickThrough(false);

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        HwndSource? source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);

        RegisterCurrentHotKey(showFailureMessage: true);
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        ClampToolbarPositionToViewport(saveSettings: false);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _toastTimer.Stop();
        _colorButtonClickTimer.Stop();
        _presetColorClickTimer.Stop();
        _clickThroughHoverTimer.Stop();
        EndToolbarDrag(saveSettings: false);
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();

        UnregisterCurrentHotKey();
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

        if (e.Key == Key.Delete && _currentTool == ToolMode.Text)
        {
            if (DeleteSelectedTextElement())
            {
                e.Handled = true;
            }

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
            ClearSelectedTextElement();
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
            ClearSelectedTextElement();
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

            case InteractionState.DrawingCircle:
                CancelCircleInteraction();
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

        _colorButtonClickTimer.Stop();
        _presetColorClickTimer.Stop();
        _pendingPresetColorIndex = null;
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

    private void CancelCircleInteraction()
    {
        CancelCirclePreview();
        _isCircleDrawing = false;

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


    private void ApplyInitialToolbarPosition()
    {
        if (_hasPendingToolbarPosition)
        {
            ClampToolbarPositionToViewport(saveSettings: false);
            return;
        }

        PositionToolbarAtDefault(saveSettings: false);
    }

    private void PositionToolbarAtDefault(bool saveSettings)
    {
        if (!EnsureToolbarReadyForPositioning())
        {
            return;
        }

        double defaultLeft = Math.Max(ToolbarViewportMargin, ActualWidth - ToolbarPanel.ActualWidth - ToolbarViewportMargin);
        double defaultTop = Math.Max(ToolbarViewportMargin, (ActualHeight - ToolbarPanel.ActualHeight) / 2.0);

        SetToolbarPosition(defaultLeft, defaultTop, saveSettings);
    }

    private bool EnsureToolbarReadyForPositioning()
    {
        if (ToolbarPanel == null)
        {
            return false;
        }

        ToolbarPanel.HorizontalAlignment = HorizontalAlignment.Left;
        ToolbarPanel.VerticalAlignment = VerticalAlignment.Top;

        if (ToolbarPanel.ActualWidth <= 0 || ToolbarPanel.ActualHeight <= 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return false;
        }

        return true;
    }

    private void ClampToolbarPositionToViewport(bool saveSettings)
    {
        if (!EnsureToolbarReadyForPositioning())
        {
            return;
        }

        double maxLeft = Math.Max(ToolbarViewportMargin, ActualWidth - ToolbarPanel.ActualWidth - ToolbarViewportMargin);
        double maxTop = Math.Max(ToolbarViewportMargin, ActualHeight - ToolbarPanel.ActualHeight - ToolbarViewportMargin);

        double clampedLeft = Math.Min(Math.Max(_toolbarLeft, ToolbarViewportMargin), maxLeft);
        double clampedTop = Math.Min(Math.Max(_toolbarTop, ToolbarViewportMargin), maxTop);

        SetToolbarPosition(clampedLeft, clampedTop, saveSettings);
    }

    private void SetToolbarPosition(double left, double top, bool saveSettings)
    {
        if (ToolbarPanel == null)
        {
            return;
        }

        _toolbarLeft = left;
        _toolbarTop = top;
        _hasPendingToolbarPosition = true;

        ToolbarPanel.Margin = new Thickness(left, top, 0, 0);

        if (saveSettings)
        {
            SaveAppSettings();
        }
    }

    private void ToolbarPanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThroughEnabled)
        {
            return;
        }

        if (!IsToolbarDragHandleHit(e.OriginalSource))
        {
            return;
        }

        _isToolbarDragging = true;
        _toolbarDragStartMousePoint = e.GetPosition(this);
        _toolbarDragStartPanelPoint = new Point(_toolbarLeft, _toolbarTop);

        ToolbarPanel.CaptureMouse();
        e.Handled = true;
    }

    private void ToolbarPanel_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isToolbarDragging)
        {
            return;
        }

        Point currentPoint = e.GetPosition(this);
        Vector delta = currentPoint - _toolbarDragStartMousePoint;

        double nextLeft = _toolbarDragStartPanelPoint.X + delta.X;
        double nextTop = _toolbarDragStartPanelPoint.Y + delta.Y;

        if (!EnsureToolbarReadyForPositioning())
        {
            return;
        }

        double maxLeft = Math.Max(ToolbarViewportMargin, ActualWidth - ToolbarPanel.ActualWidth - ToolbarViewportMargin);
        double maxTop = Math.Max(ToolbarViewportMargin, ActualHeight - ToolbarPanel.ActualHeight - ToolbarViewportMargin);

        nextLeft = Math.Min(Math.Max(nextLeft, ToolbarViewportMargin), maxLeft);
        nextTop = Math.Min(Math.Max(nextTop, ToolbarViewportMargin), maxTop);

        SetToolbarPosition(nextLeft, nextTop, saveSettings: false);
        e.Handled = true;
    }

    private void ToolbarPanel_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isToolbarDragging)
        {
            return;
        }

        EndToolbarDrag(saveSettings: true);
        e.Handled = true;
    }

    private void ToolbarPanel_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isToolbarDragging)
        {
            EndToolbarDrag(saveSettings: true);
        }
    }

    private void EndToolbarDrag(bool saveSettings)
    {
        if (ToolbarPanel.IsMouseCaptured)
        {
            ToolbarPanel.ReleaseMouseCapture();
        }

        _isToolbarDragging = false;
        ClampToolbarPositionToViewport(saveSettings);
    }

    private void UpdateClickThroughButtonIcons()
    {
        SetImageSource(ClickThroughButtonIcon, _isClickThroughEnabled ? ClickThroughOnIconPath : ClickThroughOffIconPath);
        SetImageSource(CtReturnButtonIcon, ClickThroughOnIconPath);
    }

    private static void SetImageSource(Image? image, string relativePath)
    {
        if (image == null)
        {
            return;
        }

        image.Source = new BitmapImage(new Uri(relativePath, UriKind.Relative));
    }

    private void UpdateToolbarForCT()
    {
        if (FullToolbarPanel == null || CtMiniPanel == null)
        {
            return;
        }

        FullToolbarPanel.Visibility = _isClickThroughEnabled
            ? Visibility.Collapsed
            : Visibility.Visible;

        CtMiniPanel.Visibility = _isClickThroughEnabled
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private bool IsPointInsideToolbarPanelScreenBounds(IntPtr lParam)
    {
        if (!_isClickThroughEnabled || ToolbarPanel == null || !ToolbarPanel.IsVisible)
        {
            return false;
        }

        int raw = lParam.ToInt32();
        int screenX = unchecked((short)(raw & 0xFFFF));
        int screenY = unchecked((short)((raw >> 16) & 0xFFFF));

        Point topLeft = ToolbarPanel.PointToScreen(new Point(0, 0));
        Rect bounds = new(topLeft.X, topLeft.Y, ToolbarPanel.ActualWidth, ToolbarPanel.ActualHeight);

        return bounds.Contains(new Point(screenX, screenY));
    }

    private static bool IsToolbarDragHandleHit(object originalSource)
    {
        DependencyObject? current = originalSource as DependencyObject;

        while (current != null)
        {
            if (current is Button || current is Popup || current is ContextMenu || current is MenuItem || current is Slider || current is CheckBox)
            {
                return false;
            }

            if (current is Border border && string.Equals(border.Name, "ToolbarPanel", StringComparison.Ordinal))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
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

    private void InitializeColorButtonClickTimer()
    {
        _colorButtonClickTimer.Interval = TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime + 50);
        _colorButtonClickTimer.Tick += ColorButtonClickTimer_Tick;
    }

    private void InitializePresetColorClickTimer()
    {
        _presetColorClickTimer.Interval = TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime + 50);
        _presetColorClickTimer.Tick += PresetColorClickTimer_Tick;
    }

    private void InitializePenButtonClickTimer()
    {
        _penButtonClickTimer.Interval = TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime + 50);
        _penButtonClickTimer.Tick += PenButtonClickTimer_Tick;
    }

    private void InitializePenWidthPresetClickTimer()
    {
        _penWidthPresetClickTimer.Interval = TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime + 50);
        _penWidthPresetClickTimer.Tick += PenWidthPresetClickTimer_Tick;
    }

    private void InitializeClickThroughHoverTimer()
    {
        _clickThroughHoverTimer.Interval = TimeSpan.FromMilliseconds(50);
        _clickThroughHoverTimer.Tick += ClickThroughHoverTimer_Tick;
    }

    private void ClickThroughHoverTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isClickThroughEnabled)
        {
            return;
        }

        UpdateClickThroughTransparentState();
    }

    private void UpdateClickThroughTransparentState()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        exStyle |= WS_EX_LAYERED;

        bool shouldBeTransparent = _isClickThroughEnabled && !IsCursorInsideToolbarPanel();

        if (shouldBeTransparent)
        {
            exStyle |= WS_EX_TRANSPARENT;
        }
        else
        {
            exStyle &= ~WS_EX_TRANSPARENT;
        }

        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
    }

    private bool IsCursorInsideToolbarPanel()
    {
        if (ToolbarPanel == null || !ToolbarPanel.IsVisible || ToolbarPanel.ActualWidth <= 0 || ToolbarPanel.ActualHeight <= 0)
        {
            return false;
        }

        var cursorPosition = Forms.Cursor.Position;
        Point localPoint = ToolbarPanel.PointFromScreen(new Point(cursorPosition.X, cursorPosition.Y));

        return localPoint.X >= 0
            && localPoint.Y >= 0
            && localPoint.X <= ToolbarPanel.ActualWidth
            && localPoint.Y <= ToolbarPanel.ActualHeight;
    }

    private void ShowClickThroughToastIfNeeded()
    {
        if (_hasShownClickThroughTrayMessage)
        {
            return;
        }

        ShowToastMessage("描画OFF。右の戻るかタスクトレイから戻せます。");
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

    private void ColorButtonClickTimer_Tick(object? sender, EventArgs e)
    {
        _colorButtonClickTimer.Stop();

        if (_isClickThroughEnabled)
        {
            return;
        }

        ColorPopup.IsOpen = true;
    }

    private void PresetColorClickTimer_Tick(object? sender, EventArgs e)
    {
        _presetColorClickTimer.Stop();

        if (_pendingPresetColorIndex == null)
        {
            return;
        }

        int index = _pendingPresetColorIndex.Value;
        _pendingPresetColorIndex = null;

        if (index < 0 || index >= _presetColors.Count)
        {
            return;
        }

        ApplyPenColor(_presetColors[index], addToRecent: true);
        ColorPopup.IsOpen = false;
    }

    private void PenButtonClickTimer_Tick(object? sender, EventArgs e)
    {
        _penButtonClickTimer.Stop();

        if (!_pendingPenButtonSingleClick || _isClickThroughEnabled)
        {
            _pendingPenButtonSingleClick = false;
            return;
        }

        _pendingPenButtonSingleClick = false;
        ActivatePenTool();
    }

    private void PenWidthPresetClickTimer_Tick(object? sender, EventArgs e)
    {
        _penWidthPresetClickTimer.Stop();

        if (_pendingPenWidthPresetIndex == null)
        {
            return;
        }

        int index = _pendingPenWidthPresetIndex.Value;
        _pendingPenWidthPresetIndex = null;

        ApplyPenWidthPreset(index);
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
                     PenButton, RectangleButton, CircleButton, TextButton, EraserButton, ColorButton,
                     ClearButton, ClickThroughButton, SettingsButton, ExitButton
                 })
        {
            button.Background = _normalButtonBackground;
            button.Foreground = _normalButtonForeground;
            button.BorderBrush = Brushes.DimGray;
        }
    }


    private void InitializeHotkeySettingsControls()
    {
        if (HotkeyKeyComboBox == null)
        {
            return;
        }

        HotkeyKeyComboBox.ItemsSource = GetAvailableHotkeyKeys();
        UpdateHotkeySettingsUi();
    }

    private static List<string> GetAvailableHotkeyKeys()
    {
        var keys = new List<string>();

        for (char c = 'A'; c <= 'Z'; c++)
        {
            keys.Add(c.ToString());
        }

        for (char c = '0'; c <= '9'; c++)
        {
            keys.Add(c.ToString());
        }

        for (int i = 1; i <= 12; i++)
        {
            keys.Add($"F{i}");
        }

        return keys;
    }

    private static Forms.Keys NormalizeHotkeyKey(string? keyText)
    {
        if (string.IsNullOrWhiteSpace(keyText))
        {
            return DefaultHotkeyKey;
        }

        if (Enum.TryParse(keyText.Trim(), true, out Forms.Keys parsed))
        {
            parsed &= Forms.Keys.KeyCode;
            if (parsed != Forms.Keys.None)
            {
                return parsed;
            }
        }

        return DefaultHotkeyKey;
    }

    private static uint BuildHotkeyModifiers(bool ctrl, bool alt, bool shift, bool win)
    {
        uint modifiers = 0;

        if (ctrl)
        {
            modifiers |= MOD_CONTROL;
        }

        if (alt)
        {
            modifiers |= MOD_ALT;
        }

        if (shift)
        {
            modifiers |= MOD_SHIFT;
        }

        if (win)
        {
            modifiers |= MOD_WIN;
        }

        return modifiers;
    }

    private string GetCurrentHotkeyDisplayText()
    {
        return BuildHotkeyDisplayText(_clickThroughHotkeyModifiers, _clickThroughHotkeyKey);
    }

    private static string BuildHotkeyDisplayText(uint modifiers, Forms.Keys key)
    {
        var parts = new List<string>();

        if ((modifiers & MOD_CONTROL) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & MOD_ALT) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & MOD_SHIFT) != 0)
        {
            parts.Add("Shift");
        }

        if ((modifiers & MOD_WIN) != 0)
        {
            parts.Add("Win");
        }

        parts.Add(key.ToString());
        return string.Join(" + ", parts);
    }

    private void UpdateHotkeySettingsUi()
    {
        if (HotkeyCtrlCheckBox == null || HotkeyAltCheckBox == null || HotkeyShiftCheckBox == null || HotkeyWinCheckBox == null || HotkeyKeyComboBox == null)
        {
            return;
        }

        HotkeyCtrlCheckBox.IsChecked = (_clickThroughHotkeyModifiers & MOD_CONTROL) != 0;
        HotkeyAltCheckBox.IsChecked = (_clickThroughHotkeyModifiers & MOD_ALT) != 0;
        HotkeyShiftCheckBox.IsChecked = (_clickThroughHotkeyModifiers & MOD_SHIFT) != 0;
        HotkeyWinCheckBox.IsChecked = (_clickThroughHotkeyModifiers & MOD_WIN) != 0;
        HotkeyKeyComboBox.SelectedItem = _clickThroughHotkeyKey.ToString();

        UpdateHotkeyPreviewFromEditor();
    }

    private void UpdateHotkeyPreviewFromEditor()
    {
        if (HotkeyPreviewTextBlock == null || HotkeyCtrlCheckBox == null || HotkeyAltCheckBox == null || HotkeyShiftCheckBox == null || HotkeyWinCheckBox == null || HotkeyKeyComboBox == null)
        {
            return;
        }

        uint modifiers = BuildHotkeyModifiers(
            HotkeyCtrlCheckBox.IsChecked == true,
            HotkeyAltCheckBox.IsChecked == true,
            HotkeyShiftCheckBox.IsChecked == true,
            HotkeyWinCheckBox.IsChecked == true);

        Forms.Keys key = NormalizeHotkeyKey(HotkeyKeyComboBox.SelectedItem as string);
        HotkeyPreviewTextBlock.Text = BuildHotkeyDisplayText(modifiers, key);
    }

    private bool RegisterCurrentHotKey(bool showFailureMessage)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        UnregisterCurrentHotKey();

        bool registered = RegisterHotKey(
            hwnd,
            HOTKEY_ID_TOGGLE_CLICKTHROUGH,
            _clickThroughHotkeyModifiers,
            (uint)_clickThroughHotkeyKey);

        _isHotKeyRegistered = registered;

        if (!registered && showFailureMessage)
        {
            MessageBox.Show(
                $"グローバルホットキー {GetCurrentHotkeyDisplayText()} の登録に失敗しました。\n他のアプリで使われている可能性があります。",
                "FlowInk",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        return registered;
    }

    private void UnregisterCurrentHotKey()
    {
        if (!_isHotKeyRegistered)
        {
            return;
        }

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            _isHotKeyRegistered = false;
            return;
        }

        UnregisterHotKey(hwnd, HOTKEY_ID_TOGGLE_CLICKTHROUGH);
        _isHotKeyRegistered = false;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isClickThroughEnabled)
        {
            return;
        }

        UpdateHotkeySettingsUi();
        HotkeySettingsPopup.IsOpen = false;
        HotkeySettingsPopup.IsOpen = true;
    }

    private void HotkeySettingControl_Changed(object sender, RoutedEventArgs e)
    {
        UpdateHotkeyPreviewFromEditor();
    }

    private void HotkeyKeyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateHotkeyPreviewFromEditor();
    }

    private void HotkeySaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (HotkeyCtrlCheckBox == null || HotkeyAltCheckBox == null || HotkeyShiftCheckBox == null || HotkeyWinCheckBox == null || HotkeyKeyComboBox == null)
        {
            return;
        }

        uint newModifiers = BuildHotkeyModifiers(
            HotkeyCtrlCheckBox.IsChecked == true,
            HotkeyAltCheckBox.IsChecked == true,
            HotkeyShiftCheckBox.IsChecked == true,
            HotkeyWinCheckBox.IsChecked == true);

        if (newModifiers == 0)
        {
            MessageBox.Show(
                "修飾キーを1つ以上選択してください。",
                "FlowInk",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Forms.Keys newKey = NormalizeHotkeyKey(HotkeyKeyComboBox.SelectedItem as string);

        uint previousModifiers = _clickThroughHotkeyModifiers;
        Forms.Keys previousKey = _clickThroughHotkeyKey;

        _clickThroughHotkeyModifiers = newModifiers;
        _clickThroughHotkeyKey = newKey;

        if (!RegisterCurrentHotKey(showFailureMessage: true))
        {
            _clickThroughHotkeyModifiers = previousModifiers;
            _clickThroughHotkeyKey = previousKey;
            RegisterCurrentHotKey(showFailureMessage: false);
            UpdateHotkeySettingsUi();
            return;
        }

        SaveAppSettings();
        HotkeySettingsPopup.IsOpen = false;
        ShowToastMessage($"CTホットキーを {GetCurrentHotkeyDisplayText()} に変更しました。");
    }

    private void HotkeyCancelButton_Click(object sender, RoutedEventArgs e)
    {
        HotkeySettingsPopup.IsOpen = false;
        UpdateHotkeySettingsUi();
    }


    private static string GetAppDataDirectoryPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppDataFolderName);
    }

    private static string GetInstalledFilePath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, fileName);
    }

    private static string GetAppDataFilePath(string fileName)
    {
        return Path.Combine(GetAppDataDirectoryPath(), fileName);
    }

    private static void EnsureAppSettingsFileReady()
    {
        string appDataDirectoryPath = GetAppDataDirectoryPath();
        Directory.CreateDirectory(appDataDirectoryPath);

        string appDataFilePath = GetAppDataFilePath(AppSettingsFileName);
        if (File.Exists(appDataFilePath))
        {
            return;
        }

        string installedFilePath = GetInstalledFilePath(AppSettingsFileName);
        if (File.Exists(installedFilePath))
        {
            File.Copy(installedFilePath, appDataFilePath);
        }
    }

    private void LoadAppSettings()
    {
        EnsureAppSettingsFileReady();
        string filePath = GetAppDataFilePath(AppSettingsFileName);

        try
        {
            if (!File.Exists(filePath))
            {
                _presetColors = new List<Color>(GetDefaultPresetColors());
                _recentColors = new List<Color>();
                _customColorValues = new List<int>();
                _currentPenWidth = 4.0;
                _penWidthPresets = new List<double>(GetDefaultPenWidthPresets());
                _currentTextFontFamilyName = DefaultTextFontFamilyName;
                _currentTextFontSize = DefaultTextFontSize;
                _currentTextFontStyle = FontStyles.Normal;
                _currentTextFontWeight = FontWeights.Normal;
                _isRectangleFilled = false;
                _rectangleFillOpacityPercent = 35;
                _hasPendingToolbarPosition = false;
                _clickThroughHotkeyModifiers = DefaultHotkeyModifiers;
                _clickThroughHotkeyKey = DefaultHotkeyKey;

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
                _penWidthPresets = new List<double>(GetDefaultPenWidthPresets());
                _currentTextFontFamilyName = DefaultTextFontFamilyName;
                _currentTextFontSize = DefaultTextFontSize;
                _currentTextFontStyle = FontStyles.Normal;
                _currentTextFontWeight = FontWeights.Normal;
                _isRectangleFilled = false;
                _rectangleFillOpacityPercent = 35;
                _hasPendingToolbarPosition = false;
                _clickThroughHotkeyModifiers = DefaultHotkeyModifiers;
                _clickThroughHotkeyKey = DefaultHotkeyKey;
                SaveAppSettings();
                return;
            }

            _presetColors = ParseColorList(settings.PresetColors, GetDefaultPresetColors());
            _recentColors = ParseColorList(settings.RecentColors, new List<Color>());
            _customColorValues = NormalizeCustomColors(settings.CustomColors);
            _currentPenWidth = NormalizePenWidth(settings.PenWidth);
            _penWidthPresets = NormalizePenWidthPresets(settings.PenWidthPresets);
            _currentTextFontFamilyName = NormalizeTextFontFamilyName(settings.TextFontFamily);
            _currentTextFontSize = NormalizeTextFontSize(settings.TextFontSize);
            _currentTextFontStyle = settings.TextItalic ? FontStyles.Italic : FontStyles.Normal;
            _currentTextFontWeight = settings.TextBold ? FontWeights.Bold : FontWeights.Normal;
            _isRectangleFilled = settings.RectangleFillEnabled;
            _rectangleFillOpacityPercent = NormalizeRectangleFillOpacity(settings.RectangleFillOpacity);
            _hasPendingToolbarPosition = settings.ToolbarLeft.HasValue && settings.ToolbarTop.HasValue;
            if (_hasPendingToolbarPosition)
            {
                _toolbarLeft = settings.ToolbarLeft!.Value;
                _toolbarTop = settings.ToolbarTop!.Value;
            }

            _clickThroughHotkeyModifiers = BuildHotkeyModifiers(
                settings.HotkeyCtrl,
                settings.HotkeyAlt,
                settings.HotkeyShift,
                settings.HotkeyWin);

            _clickThroughHotkeyKey = NormalizeHotkeyKey(settings.HotkeyKey);

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
            _penWidthPresets = new List<double>(GetDefaultPenWidthPresets());
            _currentTextFontFamilyName = DefaultTextFontFamilyName;
            _currentTextFontSize = DefaultTextFontSize;
            _currentTextFontStyle = FontStyles.Normal;
            _currentTextFontWeight = FontWeights.Normal;
            _isRectangleFilled = false;
            _rectangleFillOpacityPercent = 35;
            _hasPendingToolbarPosition = false;
            _clickThroughHotkeyModifiers = DefaultHotkeyModifiers;
            _clickThroughHotkeyKey = DefaultHotkeyKey;
        }
    }

    private void SaveAppSettings()
    {
        if (_isInitializing)
        {
            return;
        }

        EnsureAppSettingsFileReady();
        string filePath = GetAppDataFilePath(AppSettingsFileName);

        _penWidthPresets = NormalizePenWidthPresets(_penWidthPresets);

        var settings = new AppSettings
        {
            PresetColors = ToHexColorList(_presetColors),
            RecentColors = ToHexColorList(_recentColors),
            CustomColors = new List<int>(_customColorValues),
            PenWidth = NormalizePenWidth(_currentPenWidth),
            PenWidthPresets = new List<double>(_penWidthPresets),
            CurrentColor = ToColorHexString(_currentPenColor),
            TextFontFamily = _currentTextFontFamilyName,
            TextFontSize = NormalizeTextFontSize(_currentTextFontSize),
            TextBold = _currentTextFontWeight == FontWeights.Bold,
            TextItalic = _currentTextFontStyle == FontStyles.Italic,
            RectangleFillEnabled = _isRectangleFilled,
            RectangleFillOpacity = NormalizeRectangleFillOpacity(_rectangleFillOpacityPercent),
            ToolbarLeft = _hasPendingToolbarPosition ? _toolbarLeft : null,
            ToolbarTop = _hasPendingToolbarPosition ? _toolbarTop : null,
            HotkeyCtrl = (_clickThroughHotkeyModifiers & MOD_CONTROL) != 0,
            HotkeyAlt = (_clickThroughHotkeyModifiers & MOD_ALT) != 0,
            HotkeyShift = (_clickThroughHotkeyModifiers & MOD_SHIFT) != 0,
            HotkeyWin = (_clickThroughHotkeyModifiers & MOD_WIN) != 0,
            HotkeyKey = _clickThroughHotkeyKey.ToString()
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

    private static List<double> GetDefaultPenWidthPresets()
    {
        return new List<double> { 1.0, 2.0, 4.0, 6.0, 10.0 };
    }

    private static List<double> NormalizePenWidthPresets(List<double>? presets)
    {
        var normalized = new List<double>();

        if (presets != null)
        {
            foreach (double value in presets)
            {
                double normalizedValue = NormalizePenWidth(value);

                if (normalized.Contains(normalizedValue))
                {
                    continue;
                }

                normalized.Add(normalizedValue);

                if (normalized.Count >= PenWidthPresetCount)
                {
                    break;
                }
            }
        }

        foreach (double fallback in GetDefaultPenWidthPresets())
        {
            double normalizedFallback = NormalizePenWidth(fallback);

            if (normalized.Contains(normalizedFallback))
            {
                continue;
            }

            normalized.Add(normalizedFallback);

            if (normalized.Count >= PenWidthPresetCount)
            {
                break;
            }
        }

        return normalized;
    }

    private static string FormatPenWidthText(double width)
    {
        return width.ToString("0.#");
    }

    private static bool ArePenWidthsEqual(double left, double right)
    {
        return Math.Abs(left - right) < 0.001;
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
            hexColors.Add(ToColorHexString(color));
        }

        return hexColors;
    }

    private static string ToColorHexString(Color color)
    {
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
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

    private static int NormalizeRectangleFillOpacity(int opacityPercent)
    {
        if (opacityPercent < 0)
        {
            return 0;
        }

        if (opacityPercent > 100)
        {
            return 100;
        }

        return opacityPercent;
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

    private void BuildPenWidthPresetButtons()
    {
        _penWidthPresets = NormalizePenWidthPresets(_penWidthPresets);

        PenWidthPresetGrid.Children.Clear();

        for (int i = 0; i < PenWidthPresetCount; i++)
        {
            double width = _penWidthPresets[i];
            var button = CreatePenWidthPresetButton(i, width);
            PenWidthPresetGrid.Children.Add(button);
        }

        UpdatePenWidthPresetButtonHighlight();
    }

    private Button CreatePenWidthPresetButton(int index, double width)
    {
        var previewLine = new Border
        {
            Width = 34,
            Height = Math.Max(2.0, width),
            Background = Brushes.White,
            CornerRadius = new CornerRadius(Math.Max(1.0, width / 2.0)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };

        var widthText = new TextBlock
        {
            Text = FormatPenWidthText(width),
            Foreground = Brushes.White,
            FontSize = 11,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var contentGrid = new Grid
        {
            Margin = new Thickness(8, 0, 8, 0)
        };
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(previewLine, 0);
        Grid.SetColumn(widthText, 2);
        contentGrid.Children.Add(previewLine);
        contentGrid.Children.Add(widthText);

        var button = new Button
        {
            Style = (Style)FindResource("PenWidthPresetButtonStyle"),
            Content = contentGrid,
            Tag = index,
            ToolTip = $"{FormatPenWidthText(width)}  (クリック: 選択 / ダブルクリック: 編集)"
        };

        button.PreviewMouseLeftButtonDown += PenWidthPresetButton_PreviewMouseLeftButtonDown;
        return button;
    }

    private void UpdatePenWidthPresetButtonHighlight()
    {
        foreach (object child in PenWidthPresetGrid.Children)
        {
            if (child is not Button button || button.Tag is not int index || index < 0 || index >= _penWidthPresets.Count)
            {
                continue;
            }

            bool isSelected = ArePenWidthsEqual(_penWidthPresets[index], _currentPenWidth);
            button.BorderBrush = isSelected ? Brushes.White : new SolidColorBrush(Color.FromRgb(102, 102, 102));
            button.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1);
            button.FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal;
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
            _presetColorClickTimer.Stop();
            _pendingPresetColorIndex = null;
            e.Handled = true;
            EditPresetColor(slot.Index);
            return;
        }

        _presetColorClickTimer.Stop();
        _pendingPresetColorIndex = slot.Index;
        e.Handled = true;
        _presetColorClickTimer.Start();
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

    private void PenWidthPresetButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button || button.Tag is not int index)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            _penWidthPresetClickTimer.Stop();
            _pendingPenWidthPresetIndex = null;
            e.Handled = true;
            EditPenWidthPreset(index);
            return;
        }

        _penWidthPresetClickTimer.Stop();
        _pendingPenWidthPresetIndex = index;
        _penWidthPresetClickTimer.Start();
        e.Handled = true;
    }

    private void OpenPenWidthPresetPopup()
    {
        OpenPopupDeferred(PenWidthPopup, () =>
        {
            _colorButtonClickTimer.Stop();
            ColorPopup.IsOpen = false;
            RectangleSettingsPopup.IsOpen = false;
            BuildPenWidthPresetButtons();
        });
    }

    private void ApplyPenWidthPreset(int index)
    {
        if (index < 0 || index >= _penWidthPresets.Count)
        {
            return;
        }

        SelectPenWidth(_penWidthPresets[index]);
        PenWidthPopup.IsOpen = false;
    }

    private void EditPenWidthPreset(int index)
    {
        _penWidthPresets = NormalizePenWidthPresets(_penWidthPresets);

        if (index < 0 || index >= PenWidthPresetCount)
        {
            return;
        }

        ActivatePenTool();

        var dialog = new PenWidthDialog(_penWidthPresets[index], _currentPenColor)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        double updated = NormalizePenWidth(dialog.SelectedWidth);

        var nextPresets = new List<double>(_penWidthPresets);
        nextPresets[index] = updated;
        _penWidthPresets = NormalizePenWidthPresets(nextPresets);

        SelectPenWidth(updated);
        SaveAppSettings();
        BuildPenWidthPresetButtons();
        PenWidthPopup.IsOpen = false;
    }

    private void ActivatePenTool()
    {
        FinalizeOrCancelCurrentOperation();
        ClearSelectedTextElement();

        _isStraightLineDrawing = false;
        _isRectangleDrawing = false;

        _currentTool = ToolMode.Pen;
        DrawingCanvas.EditingMode = InkCanvasEditingMode.Ink;
        UpdateToolHighlight();
        UpdateCursor();
    }

    private void OpenCurrentPenWidthEditor()
    {
        ActivatePenTool();

        var dialog = new PenWidthDialog(_currentPenWidth, _currentPenColor)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        SelectPenWidth(dialog.SelectedWidth);
        BuildPenWidthPresetButtons();
    }

    private void ApplyPenColor(Color color, bool addToRecent)
    {
        _currentPenColor = color;
        DrawingCanvas.DefaultDrawingAttributes = CreatePenAttributes(_currentPenColor, _currentPenWidth);

        if (CurrentColorPreviewEllipse != null)
        {
            CurrentColorPreviewEllipse.Fill = new SolidColorBrush(_currentPenColor);
        }

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

    private void UpdateShapeButtonToolTips()
    {
        string rectangleText = _isRectangleFilled
            ? $"Rectangle（塗りつぶしON {NormalizeRectangleFillOpacity(_rectangleFillOpacityPercent)}% / 右クリックで設定）"
            : "Rectangle（塗りつぶしOFF / 右クリックで設定）";

        string circleText = _isRectangleFilled
            ? $"Circle（塗りつぶしON {NormalizeRectangleFillOpacity(_rectangleFillOpacityPercent)}% / 右クリックで設定）"
            : "Circle（塗りつぶしOFF / 右クリックで設定）";

        RectangleButton.ToolTip = rectangleText;
        CircleButton.ToolTip = circleText;
    }

    private void UpdateRectangleSettingsUi()
    {
        if (RectangleFillCheckBox != null)
        {
            RectangleFillCheckBox.IsChecked = _isRectangleFilled;
        }

        if (RectangleOpacitySlider != null)
        {
            RectangleOpacitySlider.Value = NormalizeRectangleFillOpacity(_rectangleFillOpacityPercent);
            RectangleOpacitySlider.IsEnabled = _isRectangleFilled;
        }

        if (RectangleOpacityLabel != null)
        {
            RectangleOpacityLabel.Text = $"透明度: {NormalizeRectangleFillOpacity(_rectangleFillOpacityPercent)}%";
            RectangleOpacityLabel.Opacity = _isRectangleFilled ? 1.0 : 0.55;
        }
    }

    private void OpenShapeSettingsPopup(Button placementTarget)
    {
        OpenPopupDeferred(RectangleSettingsPopup, () =>
        {
            RectangleSettingsPopup.PlacementTarget = placementTarget;
            PenWidthPopup.IsOpen = false;
            ColorPopup.IsOpen = false;
            UpdateRectangleSettingsUi();
        });
    }

    private void OpenPopupDeferred(Popup popup, Action prepare)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            prepare();
            popup.IsOpen = false;
            popup.IsOpen = true;
        }), DispatcherPriority.Background);
    }

    private DrawingAttributes CreateRectangleFillAttributes()
    {
        Color fillColor = Color.FromArgb(
            (byte)Math.Round(255.0 * NormalizeRectangleFillOpacity(_rectangleFillOpacityPercent) / 100.0),
            _currentPenColor.R,
            _currentPenColor.G,
            _currentPenColor.B);

        return CreatePenAttributes(fillColor, _currentPenWidth);
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

            ClearSelectedTextElement();
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

        if (_currentTool == ToolMode.Circle)
        {
            CommitActiveTextInput();

            _isCircleDrawing = true;
            _currentInteractionState = InteractionState.DrawingCircle;
            _circleStartPoint = e.GetPosition(DrawingCanvas);

            CancelCirclePreview();

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

        if (_isCircleDrawing)
        {
            Point currentPoint = e.GetPosition(DrawingCanvas);
            UpdateCirclePreview(_circleStartPoint, currentPoint);
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

        if (_isCircleDrawing)
        {
            Point endPoint = e.GetPosition(DrawingCanvas);
            CommitCircle(_circleStartPoint, endPoint);

            _isCircleDrawing = false;
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

        if (_isCircleDrawing)
        {
            CancelCirclePreview();
            _isCircleDrawing = false;
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

        Stroke outlineStroke = CreateRectangleOutlineStroke(startPoint, endPoint);
        _rectanglePreviewStroke = outlineStroke;
        ExecuteWithoutStrokeHistory(() => DrawingCanvas.Strokes.Add(outlineStroke));

        if (_isRectangleFilled)
        {
            List<Stroke> fillStrokes = CreateFilledRectangleStrokes(startPoint, endPoint);
            _rectanglePreviewFillStrokes = fillStrokes;

            ExecuteWithoutStrokeHistory(() =>
            {
                foreach (Stroke fillStroke in fillStrokes)
                {
                    DrawingCanvas.Strokes.Add(fillStroke);
                }
            });
        }
    }

    private void CommitRectangle(Point startPoint, Point endPoint)
    {
        CancelRectanglePreview();

        if (Math.Abs(endPoint.X - startPoint.X) < 1 && Math.Abs(endPoint.Y - startPoint.Y) < 1)
        {
            return;
        }

        Stroke outlineStroke = CreateRectangleOutlineStroke(startPoint, endPoint);

        if (!_isRectangleFilled)
        {
            DrawingCanvas.Strokes.Add(outlineStroke);
            return;
        }

        List<Stroke> fillStrokes = CreateFilledRectangleStrokes(startPoint, endPoint);

        ExecuteWithoutStrokeHistory(() =>
        {
            foreach (Stroke fillStroke in fillStrokes)
            {
                DrawingCanvas.Strokes.Add(fillStroke);
            }

            DrawingCanvas.Strokes.Add(outlineStroke);
        });

        var addedStrokes = new List<Stroke>(fillStrokes)
        {
            outlineStroke
        };

        PushHistory(new StrokeCollectionAction(
            addedStrokes,
            Array.Empty<Stroke>()));
    }

    private void CancelRectanglePreview()
    {
        if (_rectanglePreviewStroke != null)
        {
            Stroke previewStroke = _rectanglePreviewStroke;
            ExecuteWithoutStrokeHistory(() => DrawingCanvas.Strokes.Remove(previewStroke));
            _rectanglePreviewStroke = null;
        }

        if (_rectanglePreviewFillStrokes != null)
        {
            List<Stroke> previewFillStrokes = _rectanglePreviewFillStrokes;
            ExecuteWithoutStrokeHistory(() =>
            {
                foreach (Stroke previewFillStroke in previewFillStrokes)
                {
                    DrawingCanvas.Strokes.Remove(previewFillStroke);
                }
            });

            _rectanglePreviewFillStrokes = null;
        }
    }


    private void UpdateCirclePreview(Point startPoint, Point endPoint)
    {
        CancelCirclePreview();

        Stroke outlineStroke = CreateEllipseOutlineStroke(startPoint, endPoint);
        _circlePreviewStroke = outlineStroke;
        ExecuteWithoutStrokeHistory(() => DrawingCanvas.Strokes.Add(outlineStroke));

        if (_isRectangleFilled)
        {
            List<Stroke> fillStrokes = CreateFilledEllipseStrokes(startPoint, endPoint);
            _circlePreviewFillStrokes = fillStrokes;

            ExecuteWithoutStrokeHistory(() =>
            {
                foreach (Stroke fillStroke in fillStrokes)
                {
                    DrawingCanvas.Strokes.Add(fillStroke);
                }
            });
        }
    }

    private void CommitCircle(Point startPoint, Point endPoint)
    {
        CancelCirclePreview();

        if (Math.Abs(endPoint.X - startPoint.X) < 1 && Math.Abs(endPoint.Y - startPoint.Y) < 1)
        {
            return;
        }

        Stroke outlineStroke = CreateEllipseOutlineStroke(startPoint, endPoint);

        if (!_isRectangleFilled)
        {
            DrawingCanvas.Strokes.Add(outlineStroke);
            return;
        }

        List<Stroke> fillStrokes = CreateFilledEllipseStrokes(startPoint, endPoint);

        ExecuteWithoutStrokeHistory(() =>
        {
            foreach (Stroke fillStroke in fillStrokes)
            {
                DrawingCanvas.Strokes.Add(fillStroke);
            }

            DrawingCanvas.Strokes.Add(outlineStroke);
        });

        var addedStrokes = new List<Stroke>(fillStrokes)
        {
            outlineStroke
        };

        PushHistory(new StrokeCollectionAction(
            addedStrokes,
            Array.Empty<Stroke>()));
    }

    private void CancelCirclePreview()
    {
        if (_circlePreviewStroke != null)
        {
            Stroke previewStroke = _circlePreviewStroke;
            ExecuteWithoutStrokeHistory(() => DrawingCanvas.Strokes.Remove(previewStroke));
            _circlePreviewStroke = null;
        }

        if (_circlePreviewFillStrokes != null)
        {
            List<Stroke> previewFillStrokes = _circlePreviewFillStrokes;
            ExecuteWithoutStrokeHistory(() =>
            {
                foreach (Stroke previewFillStroke in previewFillStrokes)
                {
                    DrawingCanvas.Strokes.Remove(previewFillStroke);
                }
            });

            _circlePreviewFillStrokes = null;
        }
    }

    private List<Stroke> CreateFilledRectangleStrokes(Point startPoint, Point endPoint)
    {
        double left = Math.Min(startPoint.X, endPoint.X);
        double top = Math.Min(startPoint.Y, endPoint.Y);
        double right = Math.Max(startPoint.X, endPoint.X);
        double bottom = Math.Max(startPoint.Y, endPoint.Y);

        double width = right - left;
        double height = bottom - top;

        if (width < 1.0 || height < 1.0)
        {
            return new List<Stroke>();
        }

        double step = Math.Max(1.0, _currentPenWidth * 0.6);
        var strokes = new List<Stroke>();

        for (double y = top; y <= bottom; y += step)
        {
            var stylusPoints = new StylusPointCollection
            {
                new StylusPoint(left, y),
                new StylusPoint(right, y)
            };

            strokes.Add(new Stroke(stylusPoints)
            {
                DrawingAttributes = CreateRectangleFillAttributes()
            });
        }

        double lastY = strokes.Count > 0
            ? strokes[^1].StylusPoints[0].Y
            : top;

        if (bottom - lastY > 0.1)
        {
            var stylusPoints = new StylusPointCollection
            {
                new StylusPoint(left, bottom),
                new StylusPoint(right, bottom)
            };

            strokes.Add(new Stroke(stylusPoints)
            {
                DrawingAttributes = CreateRectangleFillAttributes()
            });
        }

        return strokes;
    }

    private Stroke CreateRectangleOutlineStroke(Point startPoint, Point endPoint)
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

        return new Stroke(stylusPoints)
        {
            DrawingAttributes = CreatePenAttributes(_currentPenColor, _currentPenWidth)
        };
    }


    private Stroke CreateEllipseOutlineStroke(Point startPoint, Point endPoint)
    {
        double left = Math.Min(startPoint.X, endPoint.X);
        double top = Math.Min(startPoint.Y, endPoint.Y);
        double right = Math.Max(startPoint.X, endPoint.X);
        double bottom = Math.Max(startPoint.Y, endPoint.Y);

        double width = right - left;
        double height = bottom - top;
        if (width < 1.0 || height < 1.0)
        {
            return CreateRectangleOutlineStroke(startPoint, endPoint);
        }

        double centerX = left + (width / 2.0);
        double centerY = top + (height / 2.0);
        double radiusX = width / 2.0;
        double radiusY = height / 2.0;

        int segmentCount = Math.Max(24, (int)Math.Ceiling((radiusX + radiusY) * 0.35));
        var stylusPoints = new StylusPointCollection();

        for (int i = 0; i <= segmentCount; i++)
        {
            double angle = (Math.PI * 2.0 * i) / segmentCount;
            double x = centerX + (Math.Cos(angle) * radiusX);
            double y = centerY + (Math.Sin(angle) * radiusY);
            stylusPoints.Add(new StylusPoint(x, y));
        }

        return new Stroke(stylusPoints)
        {
            DrawingAttributes = CreatePenAttributes(_currentPenColor, _currentPenWidth)
        };
    }

    private List<Stroke> CreateFilledEllipseStrokes(Point startPoint, Point endPoint)
    {
        double left = Math.Min(startPoint.X, endPoint.X);
        double top = Math.Min(startPoint.Y, endPoint.Y);
        double right = Math.Max(startPoint.X, endPoint.X);
        double bottom = Math.Max(startPoint.Y, endPoint.Y);

        double width = right - left;
        double height = bottom - top;
        if (width < 1.0 || height < 1.0)
        {
            return new List<Stroke>();
        }

        double centerX = left + (width / 2.0);
        double centerY = top + (height / 2.0);
        double radiusX = width / 2.0;
        double radiusY = height / 2.0;
        double step = Math.Max(1.0, _currentPenWidth * 0.6);
        var strokes = new List<Stroke>();

        for (double y = top; y <= bottom; y += step)
        {
            double normalizedY = (y - centerY) / radiusY;
            double inside = 1.0 - (normalizedY * normalizedY);
            if (inside <= 0.0)
            {
                continue;
            }

            double horizontalRadius = radiusX * Math.Sqrt(inside);
            double x1 = centerX - horizontalRadius;
            double x2 = centerX + horizontalRadius;

            var stylusPoints = new StylusPointCollection
            {
                new StylusPoint(x1, y),
                new StylusPoint(x2, y)
            };

            strokes.Add(new Stroke(stylusPoints)
            {
                DrawingAttributes = CreateRectangleFillAttributes()
            });
        }

        return strokes;
    }

    private void BeginTextInput(Point startPoint)
    {
        CancelActiveTextInput();
        ClearSelectedTextElement();
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

        textBox.PreviewKeyDown += ActiveTextBox_KeyDown;
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

        textBox.PreviewKeyDown -= ActiveTextBox_KeyDown;
        textBox.LostKeyboardFocus -= ActiveTextBox_LostKeyboardFocus;
        textBox.TextChanged -= ActiveTextBox_TextChanged;

        DrawingCanvas.Children.Remove(textBox);

        Border? originalElement = _editingTextOriginalElement;
        int originalIndex = originalElement != null ? GetStoredEditingOriginalIndex() : -1;

        if (text.Length == 0)
        {
            if (originalElement != null)
            {
                PushHistory(new TextRemoveAction(originalElement, originalIndex));
            }

            _editingTextOriginalElement = null;
            _editingTextOriginalStoredIndex = null;
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

        int committedIndex = originalIndex >= 0 ? originalIndex : _textElements.Count;
        AddCommittedTextElement(committed, committedIndex);

        if (originalElement != null)
        {
            PushHistory(new TextReplaceAction(originalElement, originalIndex, committed, committedIndex));
        }
        else
        {
            PushHistory(new TextAddAction(committed, committedIndex));
        }

        _editingTextOriginalElement = null;
        _editingTextOriginalStoredIndex = null;
        _editingTextOriginalColor = null;
        _editingTextOriginalFontFamilyName = null;
        _editingTextOriginalFontSize = null;
        _editingTextOriginalFontStyle = null;
        _editingTextOriginalFontWeight = null;
        _currentInteractionState = InteractionState.None;
    }

    private int GetStoredEditingOriginalIndex()
    {
        if (!_editingTextOriginalStoredIndex.HasValue)
        {
            return _textElements.Count;
        }

        int index = _editingTextOriginalStoredIndex.Value;
        if (index < 0)
        {
            return 0;
        }

        if (index > _textElements.Count)
        {
            return _textElements.Count;
        }

        return index;
    }

    private void CancelActiveTextInput()
    {
        if (_activeTextBox == null)
        {
            return;
        }

        TextBox textBox = _activeTextBox;
        _activeTextBox = null;

        textBox.PreviewKeyDown -= ActiveTextBox_KeyDown;
        textBox.LostKeyboardFocus -= ActiveTextBox_LostKeyboardFocus;
        textBox.TextChanged -= ActiveTextBox_TextChanged;

        DrawingCanvas.Children.Remove(textBox);

        if (_editingTextOriginalElement != null)
        {
            int restoreIndex = GetStoredEditingOriginalIndex();
            AddCommittedTextElement(_editingTextOriginalElement, restoreIndex);
            _editingTextOriginalElement = null;
        }

        _editingTextOriginalStoredIndex = null;
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
        host.MouseRightButtonDown += TextElement_MouseRightButtonDown;
    }

    private void DetachTextElementHandlers(Border host)
    {
        host.MouseLeftButtonDown -= TextElement_MouseLeftButtonDown;
        host.MouseMove -= TextElement_MouseMove;
        host.MouseLeftButtonUp -= TextElement_MouseLeftButtonUp;
        host.LostMouseCapture -= TextElement_LostMouseCapture;
        host.MouseRightButtonDown -= TextElement_MouseRightButtonDown;
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

        if (!ReferenceEquals(_selectedTextElement, host))
        {
            SelectTextElement(host);
            e.Handled = true;
            return;
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

    private void TextElement_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThroughEnabled || _currentTool != ToolMode.Text)
        {
            return;
        }

        if (sender is not Border host)
        {
            return;
        }

        if (_activeTextBox != null)
        {
            CommitActiveTextInput();
        }

        SelectTextElement(host);

        var menu = new ContextMenu();

        var editItem = new MenuItem
        {
            Header = "編集"
        };
        editItem.Click += (_, _) =>
        {
            BeginTextEdit(host);
        };

        var deleteItem = new MenuItem
        {
            Header = "削除"
        };
        deleteItem.Click += (_, _) =>
        {
            if (ReferenceEquals(_selectedTextElement, host))
            {
                DeleteSelectedTextElement();
            }
        };

        menu.Items.Add(editItem);
        menu.Items.Add(deleteItem);

        host.ContextMenu = menu;
        menu.IsOpen = true;
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
        SelectTextElement(host);

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
            SelectTextElement(_draggingTextElement);
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
        ClearSelectedTextElement();
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
        _editingTextOriginalStoredIndex = GetCommittedTextElementIndex(host);
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

    private void SelectTextElement(Border host)
    {
        if (ReferenceEquals(_selectedTextElement, host))
        {
            UpdateSelectedTextVisual(host, true);
            return;
        }

        ClearSelectedTextElement();

        _selectedTextElement = host;
        UpdateSelectedTextVisual(host, true);
    }

    private void ClearSelectedTextElement()
    {
        if (_selectedTextElement != null)
        {
            UpdateSelectedTextVisual(_selectedTextElement, false);
            _selectedTextElement = null;
        }
    }

    private bool DeleteSelectedTextElement()
    {
        if (_selectedTextElement == null)
        {
            return false;
        }

        Border target = _selectedTextElement;
        int index = GetCommittedTextElementIndex(target);

        ClearSelectedTextElement();
        RemoveCommittedTextElement(target);
        PushHistory(new TextRemoveAction(target, index));
        return true;
    }

    private void UpdateSelectedTextVisual(Border host, bool isSelected)
    {
        if (isSelected)
        {
            host.BorderBrush = Brushes.White;
            host.BorderThickness = new Thickness(1);
            host.Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
            host.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 0,
                ShadowDepth = 0,
                Opacity = 1
            };
            return;
        }

        host.BorderBrush = Brushes.Transparent;
        host.BorderThickness = new Thickness(0);
        host.Background = Brushes.Transparent;
        host.Effect = null;
    }

    private int GetCommittedTextElementIndex(Border host)
    {
        int index = _textElements.IndexOf(host);
        return index >= 0 ? index : _textElements.Count;
    }

    private void AddCommittedTextElement(Border host)
    {
        AddCommittedTextElement(host, _textElements.Count);
    }

    private void AddCommittedTextElement(Border host, int index)
    {
        if (_textElements.Contains(host))
        {
            return;
        }

        UpdateSelectedTextVisual(host, false);

        int normalizedIndex = index;
        if (normalizedIndex < 0)
        {
            normalizedIndex = 0;
        }

        if (normalizedIndex > _textElements.Count)
        {
            normalizedIndex = _textElements.Count;
        }

        int childIndex = GetTextCanvasInsertIndex(normalizedIndex);
        DrawingCanvas.Children.Insert(childIndex, host);
        _textElements.Insert(normalizedIndex, host);
    }

    private int GetTextCanvasInsertIndex(int textIndex)
    {
        int normalized = textIndex;
        if (normalized < 0)
        {
            normalized = 0;
        }

        if (normalized > _textElements.Count)
        {
            normalized = _textElements.Count;
        }

        if (normalized == _textElements.Count)
        {
            return DrawingCanvas.Children.Count;
        }

        Border nextTextElement = _textElements[normalized];
        int childIndex = DrawingCanvas.Children.IndexOf(nextTextElement);

        if (childIndex < 0)
        {
            return DrawingCanvas.Children.Count;
        }

        return childIndex;
    }

    private void RemoveCommittedTextElement(Border host)
    {
        if (_draggingTextElement == host)
        {
            EndTextElementDrag();
        }

        if (ReferenceEquals(_selectedTextElement, host))
        {
            ClearSelectedTextElement();
        }

        DrawingCanvas.Children.Remove(host);
        _textElements.Remove(host);
    }

    private List<ClearTextEntry> GetCommittedTextEntriesSnapshot()
    {
        var result = new List<ClearTextEntry>(_textElements.Count);

        for (int i = 0; i < _textElements.Count; i++)
        {
            result.Add(new ClearTextEntry(_textElements[i], i));
        }

        return result;
    }

    private void RemoveCommittedTextElements()
    {
        ClearSelectedTextElement();
        EndTextElementDrag();

        foreach (Border element in new List<Border>(_textElements))
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
        UpdatePenWidthPresetButtonHighlight();
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
            ToolMode.Circle => CircleButton,
            ToolMode.Text => TextButton,
            _ => EraserButton
        };

        SetButtonSelected(selectedButton, PenButton, RectangleButton, CircleButton, TextButton, EraserButton);
    }

    private void UpdateCursor()
    {
        Cursor nextCursor;

        if (_isClickThroughEnabled)
        {
            nextCursor = Cursors.Arrow;
        }
        else
        {
            nextCursor = _currentTool switch
            {
                ToolMode.Pen => _penCursor,
                ToolMode.Rectangle => _penCursor,
                ToolMode.Circle => _penCursor,
                ToolMode.Text => Cursors.IBeam,
                ToolMode.Eraser => _penCursor,
                _ => Cursors.Arrow
            };
        }

        Cursor = nextCursor;
        DrawingCanvas.Cursor = nextCursor;
    }


    private void PenButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThroughEnabled)
        {
            return;
        }

        _penButtonClickTimer.Stop();
        _pendingPenButtonSingleClick = true;
        e.Handled = true;
        _penButtonClickTimer.Start();
    }

    private void PenButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThroughEnabled)
        {
            return;
        }

        _penButtonClickTimer.Stop();
        _pendingPenButtonSingleClick = false;
        e.Handled = true;

        ActivatePenTool();
        OpenPenWidthPresetPopup();
    }

    private void RectangleButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThroughEnabled)
        {
            return;
        }
    }

    private void RectangleButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThroughEnabled)
        {
            return;
        }

        e.Handled = true;

        _currentTool = ToolMode.Rectangle;
        DrawingCanvas.EditingMode = InkCanvasEditingMode.None;
        UpdateToolHighlight();
        UpdateCursor();

        OpenShapeSettingsPopup(RectangleButton);
    }

    private void CircleButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThroughEnabled)
        {
            return;
        }
    }

    private void CircleButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThroughEnabled)
        {
            return;
        }

        e.Handled = true;

        _currentTool = ToolMode.Circle;
        DrawingCanvas.EditingMode = InkCanvasEditingMode.None;
        UpdateToolHighlight();
        UpdateCursor();

        OpenShapeSettingsPopup(CircleButton);
    }

    private void RectangleFillCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _isRectangleFilled = RectangleFillCheckBox.IsChecked == true;
        UpdateShapeButtonToolTips();
        UpdateRectangleSettingsUi();
        SaveAppSettings();
    }

    private void RectangleOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _rectangleFillOpacityPercent = NormalizeRectangleFillOpacity((int)Math.Round(e.NewValue));
        UpdateShapeButtonToolTips();
        UpdateRectangleSettingsUi();
        SaveAppSettings();
    }

    private void RectangleButton_Click(object sender, RoutedEventArgs e)
    {
        PenWidthPopup.IsOpen = false;
        FinalizeOrCancelCurrentOperation();
        ClearSelectedTextElement();

        _isStraightLineDrawing = false;
        _isRectangleDrawing = false;
        _isCircleDrawing = false;

        _currentTool = ToolMode.Rectangle;
        DrawingCanvas.EditingMode = InkCanvasEditingMode.None;
        UpdateToolHighlight();
        UpdateCursor();
    }

    private void CircleButton_Click(object sender, RoutedEventArgs e)
    {
        PenWidthPopup.IsOpen = false;
        FinalizeOrCancelCurrentOperation();
        ClearSelectedTextElement();

        _isStraightLineDrawing = false;
        _isRectangleDrawing = false;
        _isCircleDrawing = false;

        _currentTool = ToolMode.Circle;
        DrawingCanvas.EditingMode = InkCanvasEditingMode.None;
        UpdateToolHighlight();
        UpdateCursor();
    }

    private void TextButton_Click(object sender, RoutedEventArgs e)
    {
        PenWidthPopup.IsOpen = false;
        FinalizeOrCancelCurrentOperation();
        ClearSelectedTextElement();

        _isStraightLineDrawing = false;
        _isRectangleDrawing = false;
        _isCircleDrawing = false;

        _currentTool = ToolMode.Text;
        DrawingCanvas.EditingMode = InkCanvasEditingMode.None;
        UpdateToolHighlight();
        UpdateCursor();
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
        UpdateCursor();

        ShowFontDialog();
        e.Handled = true;
    }

    private void EraserButton_Click(object sender, RoutedEventArgs e)
    {
        PenWidthPopup.IsOpen = false;
        FinalizeOrCancelCurrentOperation();
        ClearSelectedTextElement();

        _isStraightLineDrawing = false;
        _isRectangleDrawing = false;
        _isCircleDrawing = false;

        _currentTool = ToolMode.Eraser;
        DrawingCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
        UpdateToolHighlight();
        UpdateCursor();
    }

    private void ColorButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThroughEnabled)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            _colorButtonClickTimer.Stop();
            _presetColorClickTimer.Stop();
            _pendingPresetColorIndex = null;
            PenWidthPopup.IsOpen = false;
            e.Handled = true;
            OpenCurrentColorEditor();
            return;
        }

        PenWidthPopup.IsOpen = false;
        _colorButtonClickTimer.Stop();
        _colorButtonClickTimer.Start();
        e.Handled = true;
    }

    private void OpenCurrentColorEditor()
    {
        _presetColorClickTimer.Stop();
        _pendingPresetColorIndex = null;

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
        ClearSelectedTextElement();

        List<Stroke> removedStrokes = ToStrokeList(DrawingCanvas.Strokes);
        List<ClearTextEntry> removedTextEntries = GetCommittedTextEntriesSnapshot();

        if (removedStrokes.Count == 0 && removedTextEntries.Count == 0)
        {
            return;
        }

        ExecuteWithoutStrokeHistory(() => DrawingCanvas.Strokes.Clear());
        RemoveCommittedTextElements();

        PushHistory(new ClearAction(removedStrokes, removedTextEntries));
    }

    private void ClickThroughButton_Click(object sender, RoutedEventArgs e)
    {
        SetClickThrough(!_isClickThroughEnabled);
    }

    private void CtReturnButton_Click(object sender, RoutedEventArgs e)
    {
        SetClickThrough(false);
    }

    private void SetClickThrough(bool enabled)
    {
        FinalizeOrCancelCurrentOperation();
        ClearSelectedTextElement();

        _isStraightLineDrawing = false;
        _isRectangleDrawing = false;
        _isCircleDrawing = false;

        _isClickThroughEnabled = enabled;

        ColorPopup.IsOpen = false;
        PenWidthPopup.IsOpen = false;
        RectangleSettingsPopup.IsOpen = false;
        _colorButtonClickTimer.Stop();
        _presetColorClickTimer.Stop();
        _pendingPresetColorIndex = null;
        _penButtonClickTimer.Stop();
        _penWidthPresetClickTimer.Stop();
        _pendingPenButtonSingleClick = false;
        _pendingPenWidthPresetIndex = null;

        UpdateToolbarForCT();
        UpdateClickThroughButtonIcons();

        if (enabled)
        {
            DrawingCanvas.IsHitTestVisible = false;
            DrawingCanvas.EditingMode = InkCanvasEditingMode.None;
            _clickThroughHoverTimer.Start();
        }
        else
        {
            DrawingCanvas.IsHitTestVisible = true;
            DrawingCanvas.EditingMode = _currentTool switch
            {
                ToolMode.Pen => InkCanvasEditingMode.Ink,
                ToolMode.Rectangle => InkCanvasEditingMode.None,
                ToolMode.Circle => InkCanvasEditingMode.None,
                ToolMode.Text => InkCanvasEditingMode.None,
                _ => InkCanvasEditingMode.EraseByPoint
            };
            _clickThroughHoverTimer.Stop();
        }

        UpdateCursor();
        UpdateClickThroughTransparentState();
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
        if (msg == WM_NCHITTEST && _isClickThroughEnabled)
        {
            if (IsPointInsideToolbarPanelScreenBounds(lParam))
            {
                handled = true;
                return new IntPtr(HTCLIENT);
            }

            handled = true;
            return new IntPtr(HTTRANSPARENT);
        }

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
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
using WpfShape = System.Windows.Shapes.Shape;
using SR = FlowInk.Properties.Resources;
using WpfEllipse = System.Windows.Shapes.Ellipse;
using WpfRectangle = System.Windows.Shapes.Rectangle;
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
    private LineStyleKind _currentLineStyle = LineStyleKind.Solid;
    private double _currentEraserWidth = 4;
    private string _currentTextFontFamilyName = DefaultTextFontFamilyName;
    private double _currentTextFontSize = DefaultTextFontSize;
    private FontStyle _currentTextFontStyle = FontStyles.Normal;
    private FontWeight _currentTextFontWeight = FontWeights.Normal;

    private List<Color> _presetColors = new();
    private List<Color> _recentColors = new();
    private List<int> _customColorValues = new();
    private List<double> _penWidthPresets = new();
    private List<PenPreset> _penPresets = new();
    private List<double> _eraserWidthPresets = new();


    private bool _isStraightLineDrawing;
    private Point _straightLineStartPoint;
    private Stroke? _straightLinePreviewStroke;
    private Stroke? _straightLinePreviewArrowHeadStroke;

    private bool _isRectangleDrawing;
    private Point _rectangleStartPoint;
    private Stroke? _rectanglePreviewStroke;
    private WpfShape? _rectanglePreviewFillShape;
    private bool _isCircleDrawing;
    private Point _circleStartPoint;
    private Stroke? _circlePreviewStroke;
    private WpfShape? _circlePreviewFillShape;
    private bool _isRectangleFilled;
    private int _rectangleFillOpacityPercent = 35;

    private TextBox? _activeTextBox;
    private Point _activeTextStartPoint;
    private readonly List<Border> _textElements = new();
    private Border? _selectedTextElement;
    private readonly List<Border> _selectedTextElements = new();

    private readonly UndoHistoryManager<IUndoableAction, MainWindow> _history;
    private bool _isApplyingHistory;
    private bool _suppressStrokeHistory;
    private bool _isEraserGestureActive;
    private readonly List<Stroke> _eraserGestureAddedStrokes = new();
    private readonly List<Stroke> _eraserGestureRemovedStrokes = new();
    private readonly List<ShapeClipChangeEntry> _eraserGestureShapeClipChanges = new();
    private readonly Dictionary<WpfShape, Point> _lastFillEraserClipPoints = new();
    private readonly Dictionary<WpfShape, Stroke> _filledShapeOutlineStrokes = new();
    private readonly Dictionary<Stroke, WpfShape> _outlineStrokeFilledShapes = new();
    private readonly Dictionary<Stroke, ShapeKind> _shapeOutlineKinds = new();
    private readonly Dictionary<Stroke, int> _shapeOutlineGroupIds = new();
    private int _nextShapeOutlineGroupId = 1;
    private WpfShape? _selectedFillShape;
    private readonly List<WpfShape> _selectedFillShapes = new();
    private Stroke? _selectedShapeOutlineStroke;
    private readonly List<Stroke> _selectedShapeOutlineStrokes = new();
    private WpfRectangle? _shapeSelectionAdorner;
    private bool _isDraggingSelectedShape;
    private bool _hasSelectedShapeDragMoved;
    private Point _shapeDragStartMousePoint;
    private readonly Dictionary<WpfShape, Rect> _shapeDragStartFillBounds = new();
    private readonly Dictionary<Stroke, StylusPointCollection> _shapeDragStartOutlineStrokePoints = new();
    private readonly Dictionary<Border, Point> _selectionDragStartTextPositions = new();

    private WpfRectangle? _rangeSelectionAdorner;
    private bool _isRangeSelecting;
    private bool _hasRangeSelectionDragMoved;
    private bool _rangeSelectionAddsToExisting;
    private Point _rangeSelectionStartPoint;
    private const double RangeSelectionMinimumDragDistance = 3.0;

    private Border? _draggingTextElement;
    private bool _isDraggingTextElement;

    private bool _isToolbarDragging;
    private ToolbarOrientationKind _toolbarOrientation = ToolbarOrientationKind.Vertical;
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
    private const int PenWidthPresetCount = 4;
    private const int EraserWidthPresetCount = 4;
    private const int PenPresetCount = 16;

    private const string DefaultTextFontFamilyName = "Segoe UI";
    private const double DefaultTextFontSize = 28.0;
    private const double MinTextFontSize = 8.0;
    private const double MaxTextFontSize = 144.0;
    private const double TextMinWidth = 80.0;
    private const double TextPaddingX = 4.0;
    private const double TextPaddingY = 2.0;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

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

    private const double ToolbarViewportMargin = 0.0;

    private enum ToolMode
    {
        Pen,
        Select,
        Rectangle,
        Circle,
        Text,
        Eraser
    }

    private enum ShapeKind
    {
        Rectangle,
        Ellipse
    }

    private enum ToolbarOrientationKind
    {
        Vertical,
        Horizontal
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
        MovingText,
        MovingShape,
        SelectingRange
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

    private sealed class ClearCanvasElementEntry
    {
        public ClearCanvasElementEntry(UIElement element, int index)
        {
            Element = element;
            Index = index;
        }

        public UIElement Element { get; }
        public int Index { get; }
    }

    private sealed class CanvasElementAddAction : IUndoableAction
    {
        private readonly UIElement _element;
        private readonly int _index;

        public CanvasElementAddAction(UIElement element, int index)
        {
            _element = element;
            _index = index;
        }

        public void Undo(MainWindow window)
        {
            window.RemoveCanvasElementIfPresent(_element);
        }

        public void Redo(MainWindow window)
        {
            window.AddCanvasElement(_element, _index);
        }
    }

    private sealed class CanvasElementRemoveAction : IUndoableAction
    {
        private readonly UIElement _element;
        private readonly int _index;

        public CanvasElementRemoveAction(UIElement element, int index)
        {
            _element = element;
            _index = index;
        }

        public void Undo(MainWindow window)
        {
            window.AddCanvasElement(_element, _index);
        }

        public void Redo(MainWindow window)
        {
            window.RemoveCanvasElementIfPresent(_element);
        }
    }

    private sealed class ShapeClipChangeEntry
    {
        public ShapeClipChangeEntry(WpfShape shape, Geometry? originalClip)
        {
            Shape = shape;
            OriginalClip = CloneGeometry(originalClip);
        }

        public WpfShape Shape { get; }
        public Geometry? OriginalClip { get; }
        public Geometry? LatestClip { get; set; }
    }

    private sealed class ShapeClipChangeAction : IUndoableAction
    {
        private readonly WpfShape _shape;
        private readonly Geometry? _before;
        private readonly Geometry? _after;

        public ShapeClipChangeAction(WpfShape shape, Geometry? before, Geometry? after)
        {
            _shape = shape;
            _before = CloneGeometry(before);
            _after = CloneGeometry(after);
        }

        public void Undo(MainWindow window)
        {
            _shape.Clip = CloneGeometry(_before);
        }

        public void Redo(MainWindow window)
        {
            _shape.Clip = CloneGeometry(_after);
        }
    }

    private sealed class RemovedShapeOutlineInfo
    {
        public RemovedShapeOutlineInfo(Stroke stroke, ShapeKind kind, WpfShape? fillShape, int outlineGroupId)
        {
            Stroke = stroke;
            Kind = kind;
            FillShape = fillShape;
            OutlineGroupId = outlineGroupId;
            Bounds = GetStrokeBounds(stroke);
            Tolerance = GetStrokeHitTolerance(stroke);
        }

        public Stroke Stroke { get; }
        public ShapeKind Kind { get; }
        public WpfShape? FillShape { get; }
        public int OutlineGroupId { get; }
        public Rect Bounds { get; }
        public double Tolerance { get; }
    }

    private sealed class DrawableSelectionCandidate
    {
        public DrawableSelectionCandidate(WpfShape? fillShape, IEnumerable<Stroke> outlineStrokes)
        {
            FillShape = fillShape;
            OutlineStrokes = new List<Stroke>(outlineStrokes);
        }

        public WpfShape? FillShape { get; }
        public List<Stroke> OutlineStrokes { get; }
    }

    private sealed class ShapeStrokeMoveEntry
    {
        public ShapeStrokeMoveEntry(Stroke stroke, StylusPointCollection? beforePoints, StylusPointCollection? afterPoints)
        {
            Stroke = stroke;
            BeforePoints = CloneStylusPoints(beforePoints);
            AfterPoints = CloneStylusPoints(afterPoints);
        }

        public Stroke Stroke { get; }
        public StylusPointCollection? BeforePoints { get; }
        public StylusPointCollection? AfterPoints { get; }
    }

    private sealed class ShapeStrokeStyleEntry
    {
        public ShapeStrokeStyleEntry(
            Stroke stroke,
            DrawingAttributes? beforeAttributes,
            DrawingAttributes? afterAttributes,
            LineStyleKind? beforeLineStyle,
            LineStyleKind? afterLineStyle)
        {
            Stroke = stroke;
            BeforeAttributes = beforeAttributes?.Clone();
            AfterAttributes = afterAttributes?.Clone();
            BeforeLineStyle = beforeLineStyle;
            AfterLineStyle = afterLineStyle;
        }

        public Stroke Stroke { get; }
        public DrawingAttributes? BeforeAttributes { get; }
        public DrawingAttributes? AfterAttributes { get; }
        public LineStyleKind? BeforeLineStyle { get; }
        public LineStyleKind? AfterLineStyle { get; }
    }

    private sealed class ShapeMoveAction : IUndoableAction
    {
        private readonly WpfShape? _fillShape;
        private readonly Rect? _beforeFillBounds;
        private readonly Rect? _afterFillBounds;
        private readonly List<ShapeStrokeMoveEntry> _outlineStrokeMoves;

        public ShapeMoveAction(
            WpfShape? fillShape,
            Rect? beforeFillBounds,
            Rect? afterFillBounds,
            IEnumerable<ShapeStrokeMoveEntry> outlineStrokeMoves)
        {
            _fillShape = fillShape;
            _beforeFillBounds = beforeFillBounds;
            _afterFillBounds = afterFillBounds;
            _outlineStrokeMoves = new List<ShapeStrokeMoveEntry>(outlineStrokeMoves);
        }

        public void Undo(MainWindow window)
        {
            if (_fillShape != null && _beforeFillBounds.HasValue)
            {
                SetShapeBounds(_fillShape, _beforeFillBounds.Value);
            }

            foreach (ShapeStrokeMoveEntry entry in _outlineStrokeMoves)
            {
                if (entry.BeforePoints != null)
                {
                    SetStrokeStylusPoints(entry.Stroke, entry.BeforePoints);
                }
            }

            window.UpdateShapeSelectionAdorner();
        }

        public void Redo(MainWindow window)
        {
            if (_fillShape != null && _afterFillBounds.HasValue)
            {
                SetShapeBounds(_fillShape, _afterFillBounds.Value);
            }

            foreach (ShapeStrokeMoveEntry entry in _outlineStrokeMoves)
            {
                if (entry.AfterPoints != null)
                {
                    SetStrokeStylusPoints(entry.Stroke, entry.AfterPoints);
                }
            }

            window.UpdateShapeSelectionAdorner();
        }
    }

    private sealed class ShapeStyleAction : IUndoableAction
    {
        private readonly WpfShape? _fillShape;
        private readonly Brush? _beforeFill;
        private readonly Brush? _afterFill;
        private readonly List<ShapeStrokeStyleEntry> _outlineStrokeStyles;

        public ShapeStyleAction(
            WpfShape? fillShape,
            Brush? beforeFill,
            Brush? afterFill,
            IEnumerable<ShapeStrokeStyleEntry> outlineStrokeStyles)
        {
            _fillShape = fillShape;
            _beforeFill = CloneBrush(beforeFill);
            _afterFill = CloneBrush(afterFill);
            _outlineStrokeStyles = new List<ShapeStrokeStyleEntry>(outlineStrokeStyles);
        }

        public void Undo(MainWindow window)
        {
            if (_fillShape != null)
            {
                _fillShape.Fill = CloneBrush(_beforeFill);
            }

            foreach (ShapeStrokeStyleEntry entry in _outlineStrokeStyles)
            {
                if (entry.BeforeAttributes != null)
                {
                    entry.Stroke.DrawingAttributes = entry.BeforeAttributes.Clone();
                }

                if (entry.Stroke is StyledStroke styledStroke && entry.BeforeLineStyle.HasValue)
                {
                    styledStroke.LineStyle = entry.BeforeLineStyle.Value;
                }
            }

            window.UpdateShapeSelectionAdorner();
        }

        public void Redo(MainWindow window)
        {
            if (_fillShape != null)
            {
                _fillShape.Fill = CloneBrush(_afterFill);
            }

            foreach (ShapeStrokeStyleEntry entry in _outlineStrokeStyles)
            {
                if (entry.AfterAttributes != null)
                {
                    entry.Stroke.DrawingAttributes = entry.AfterAttributes.Clone();
                }

                if (entry.Stroke is StyledStroke styledStroke && entry.AfterLineStyle.HasValue)
                {
                    styledStroke.LineStyle = entry.AfterLineStyle.Value;
                }
            }

            window.UpdateShapeSelectionAdorner();
        }
    }

    private sealed class CompositeAction : IUndoableAction
    {
        private readonly List<IUndoableAction> _actions;

        public CompositeAction(IEnumerable<IUndoableAction> actions)
        {
            _actions = new List<IUndoableAction>(actions);
        }

        public void Undo(MainWindow window)
        {
            for (int i = _actions.Count - 1; i >= 0; i--)
            {
                _actions[i].Undo(window);
            }
        }

        public void Redo(MainWindow window)
        {
            foreach (IUndoableAction action in _actions)
            {
                action.Redo(window);
            }
        }
    }

    private sealed class ClearAction : IUndoableAction
    {
        private readonly List<Stroke> _removedStrokes;
        private readonly List<ClearCanvasElementEntry> _removedCanvasElements;
        private readonly List<ClearTextEntry> _removedTextEntries;

        public ClearAction(
            IEnumerable<Stroke> removedStrokes,
            IEnumerable<ClearCanvasElementEntry> removedCanvasElements,
            IEnumerable<ClearTextEntry> removedTextEntries)
        {
            _removedStrokes = new List<Stroke>(removedStrokes);
            _removedCanvasElements = new List<ClearCanvasElementEntry>(removedCanvasElements);
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

            foreach (ClearCanvasElementEntry entry in _removedCanvasElements)
            {
                window.AddCanvasElement(entry.Element, entry.Index);
            }

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

            foreach (ClearCanvasElementEntry entry in _removedCanvasElements)
            {
                window.RemoveCanvasElementIfPresent(entry.Element);
            }

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

    private sealed class PenPreset
    {
        public Color Color { get; set; }
        public double Width { get; set; }
        public int OpacityPercent { get; set; }
    }

    private sealed class PenPresetSetting
    {
        public string? Color { get; set; }
        public double Width { get; set; } = 4.0;
        public int Opacity { get; set; } = 100;
    }

    private sealed class AppSettings
    {
        public List<string> PresetColors { get; set; } = new();
        public List<string> RecentColors { get; set; } = new();
        public List<int> CustomColors { get; set; } = new();
        public double PenWidth { get; set; } = 4.0;
        public string? LineStyle { get; set; }
        public List<double> PenWidthPresets { get; set; } = new();
        public List<PenPresetSetting> PenPresets { get; set; } = new();
        public double EraserWidth { get; set; } = 4.0;
        public List<double> EraserWidthPresets { get; set; } = new();
        public string? CurrentColor { get; set; }
        public string? TextFontFamily { get; set; }
        public double TextFontSize { get; set; } = DefaultTextFontSize;
        public bool TextBold { get; set; }
        public bool TextItalic { get; set; }
        public bool RectangleFillEnabled { get; set; }
        public int RectangleFillOpacity { get; set; } = 35;
        public string? ToolbarOrientation { get; set; }
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
        ApplyToolbarOrientation(_toolbarOrientation, saveSettings: false);
        UpdateShapeButtonToolTips();
        UpdateRectangleSettingsUi();
        _penWidthPresets = NormalizePenWidthPresets(_penWidthPresets);
        _penPresets = NormalizePenPresets(_penPresets);
        _eraserWidthPresets = NormalizeEraserWidthPresets(_eraserWidthPresets);

        BuildPresetColorButtons();
        BuildRecentColorButtons();
        BuildPenPresetButtons();
        BuildPenWidthPresetButtons();
        BuildLineStyleButtons();
        BuildEraserWidthPresetButtons();
        UpdateToolbarForCT();
        UpdateClickThroughButtonLabel();

        ApplyPenColor(_currentPenColor, addToRecent: false);
        ApplyLineStyleToCanvas();
        ApplyEraserWidthToCanvas();
        _isInitializing = false;

        DrawingCanvas.EditingMode = InkCanvasEditingMode.Ink;
        DrawingCanvas.UseCustomCursor = true;
        DrawingCanvas.IsHitTestVisible = true;
        DrawingCanvas.Focusable = true;

        DrawingCanvas.PreviewMouseLeftButtonDown += DrawingCanvas_PreviewMouseLeftButtonDown;
        DrawingCanvas.PreviewMouseRightButtonDown += DrawingCanvas_PreviewMouseRightButtonDown;
        DrawingCanvas.PreviewMouseMove += DrawingCanvas_PreviewMouseMove;
        DrawingCanvas.PreviewMouseLeftButtonUp += DrawingCanvas_PreviewMouseLeftButtonUp;
        DrawingCanvas.PreviewMouseWheel += DrawingCanvas_PreviewMouseWheel;
        DrawingCanvas.LostMouseCapture += DrawingCanvas_LostMouseCapture;
        DrawingCanvas.Strokes.StrokesChanged += DrawingCanvas_StrokesChanged;
        DrawingCanvas.LineStyleStrokeCollected += DrawingCanvas_LineStyleStrokeCollected;

        PreviewKeyDown += MainWindow_PreviewKeyDown;

        UpdateToolHighlight();
        UpdateCursor();
        InitializeNotifyIcon();
        InitializeToastTimer();
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

        if (e.Key == Key.Delete && _currentTool == ToolMode.Select)
        {
            if (DeleteSelectedObjects())
            {
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Delete && DeleteSelectedShape())
        {
            e.Handled = true;
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
        if (_isApplyingHistory || _suppressStrokeHistory || DrawingCanvas.IsReplacingCollectedStroke)
        {
            return;
        }

        // A non-solid freehand stroke is replaced by LineStyleInkCanvas after collection.
        // Ignore the temporary standard Stroke; the replacement is recorded below.
        if (_currentInteractionState == InteractionState.DrawingPen
            && _currentLineStyle != LineStyleKind.Solid
            && e.Added.Count == 1
            && e.Removed.Count == 0
            && e.Added[0] is not StyledStroke)
        {
            return;
        }

        if (e.Added.Count == 0 && e.Removed.Count == 0)
        {
            return;
        }

        UpdateShapeOutlineMappingsForStrokeChanges(e.Added, e.Removed);

        if (_isEraserGestureActive)
        {
            AccumulateStrokeDelta(_eraserGestureAddedStrokes, _eraserGestureRemovedStrokes, e.Added, e.Removed);
            return;
        }

        PushHistory(new StrokeCollectionAction(ToStrokeList(e.Added), ToStrokeList(e.Removed)));
    }

    private void DrawingCanvas_LineStyleStrokeCollected(object? sender, LineStyleStrokeCollectedEventArgs e)
    {
        if (_isApplyingHistory || _suppressStrokeHistory)
        {
            return;
        }

        PushHistory(new StrokeCollectionAction(new[] { e.Stroke }, Array.Empty<Stroke>()));
        _currentInteractionState = InteractionState.None;
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
        _eraserGestureShapeClipChanges.Clear();
        _lastFillEraserClipPoints.Clear();
    }

    private void CompleteEraserGesture()
    {
        if (!_isEraserGestureActive)
        {
            return;
        }

        _isEraserGestureActive = false;

        if (_eraserGestureAddedStrokes.Count == 0
            && _eraserGestureRemovedStrokes.Count == 0
            && _eraserGestureShapeClipChanges.Count == 0)
        {
            _eraserGestureAddedStrokes.Clear();
            _eraserGestureRemovedStrokes.Clear();
            _eraserGestureShapeClipChanges.Clear();
            _lastFillEraserClipPoints.Clear();
            _currentInteractionState = InteractionState.None;
            return;
        }

        var actions = new List<IUndoableAction>();

        if (_eraserGestureAddedStrokes.Count > 0 || _eraserGestureRemovedStrokes.Count > 0)
        {
            actions.Add(new StrokeCollectionAction(_eraserGestureAddedStrokes, _eraserGestureRemovedStrokes));
        }

        foreach (ShapeClipChangeEntry entry in _eraserGestureShapeClipChanges)
        {
            actions.Add(new ShapeClipChangeAction(entry.Shape, entry.OriginalClip, entry.LatestClip));
        }

        if (actions.Count == 1)
        {
            PushHistory(actions[0]);
        }
        else
        {
            PushHistory(new CompositeAction(actions));
        }

        _eraserGestureAddedStrokes.Clear();
        _eraserGestureRemovedStrokes.Clear();
        _eraserGestureShapeClipChanges.Clear();
        _lastFillEraserClipPoints.Clear();
        _currentInteractionState = InteractionState.None;
    }

    private void CompleteEraserGestureDeferred()
    {
        Dispatcher.InvokeAsync(CompleteEraserGesture, DispatcherPriority.Background);
    }

    private void EraseCanvasElementsAtPoint(Point point)
    {
        double radius = GetFillEraserRadius();

        for (int i = DrawingCanvas.Children.Count - 1; i >= 0; i--)
        {
            UIElement child = DrawingCanvas.Children[i];

            if (child is not WpfShape shape || !IsErasableFillShapeNearPoint(shape, point, radius))
            {
                continue;
            }

            ApplyFillShapeEraserAtPoint(shape, point, radius);
        }
    }

    private double GetFillEraserRadius()
    {
        return Math.Max(2.0, _currentEraserWidth / 2.0);
    }

    private void ApplyFillShapeEraserAtPoint(WpfShape shape, Point point, double radius)
    {
        if (ShouldSkipFillEraserClip(shape, point, radius))
        {
            return;
        }

        ShapeClipChangeEntry entry = GetOrCreateShapeClipChangeEntry(shape);
        Geometry visibleGeometry = CreateCurrentVisibleGeometry(shape);
        Point localPoint = ToShapeLocalPoint(shape, point);
        var eraserGeometry = new EllipseGeometry(localPoint, radius, radius);
        var clippedGeometry = new CombinedGeometry(GeometryCombineMode.Exclude, visibleGeometry, eraserGeometry);

        shape.Clip = clippedGeometry;
        entry.LatestClip = CloneGeometry(clippedGeometry);
        _lastFillEraserClipPoints[shape] = point;
    }

    private bool ShouldSkipFillEraserClip(WpfShape shape, Point point, double radius)
    {
        if (!_lastFillEraserClipPoints.TryGetValue(shape, out Point previousPoint))
        {
            return false;
        }

        double minDistance = Math.Max(1.0, radius * 0.35);
        return (point - previousPoint).Length < minDistance;
    }

    private ShapeClipChangeEntry GetOrCreateShapeClipChangeEntry(WpfShape shape)
    {
        foreach (ShapeClipChangeEntry entry in _eraserGestureShapeClipChanges)
        {
            if (ReferenceEquals(entry.Shape, shape))
            {
                return entry;
            }
        }

        var newEntry = new ShapeClipChangeEntry(shape, shape.Clip);
        _eraserGestureShapeClipChanges.Add(newEntry);
        return newEntry;
    }

    private Geometry CreateCurrentVisibleGeometry(WpfShape shape)
    {
        if (shape.Clip != null)
        {
            return shape.Clip.CloneCurrentValue();
        }

        return CreateFullShapeGeometry(shape);
    }

    private static Geometry CreateFullShapeGeometry(WpfShape shape)
    {
        double width = Math.Max(0.0, shape.Width);
        double height = Math.Max(0.0, shape.Height);

        if (shape is WpfEllipse)
        {
            return new EllipseGeometry(new Rect(0, 0, width, height));
        }

        return new RectangleGeometry(new Rect(0, 0, width, height));
    }

    private static Geometry? CloneGeometry(Geometry? geometry)
    {
        return geometry?.CloneCurrentValue();
    }

    private static bool IsErasableFillShapeNearPoint(WpfShape shape, Point point, double radius)
    {
        if (shape.Fill == null || shape.StrokeThickness != 0)
        {
            return false;
        }

        Rect bounds = GetShapeBounds(shape);
        bounds.Inflate(radius, radius);

        if (!bounds.Contains(point))
        {
            return false;
        }

        if (shape is WpfEllipse ellipse)
        {
            return IsPointNearEllipse(ellipse, point, radius);
        }

        return true;
    }

    private static Rect GetShapeBounds(WpfShape shape)
    {
        double left = InkCanvas.GetLeft(shape);
        double top = InkCanvas.GetTop(shape);

        if (double.IsNaN(left))
        {
            left = 0;
        }

        if (double.IsNaN(top))
        {
            top = 0;
        }

        return new Rect(left, top, Math.Max(0.0, shape.Width), Math.Max(0.0, shape.Height));
    }

    private static Point ToShapeLocalPoint(WpfShape shape, Point point)
    {
        Rect bounds = GetShapeBounds(shape);
        return new Point(point.X - bounds.Left, point.Y - bounds.Top);
    }

    private static bool IsPointNearEllipse(WpfEllipse ellipse, Point point, double radius)
    {
        Rect bounds = GetShapeBounds(ellipse);
        double width = bounds.Width;
        double height = bounds.Height;

        if (width <= 0 || height <= 0)
        {
            return false;
        }

        double radiusX = (width / 2.0) + radius;
        double radiusY = (height / 2.0) + radius;
        double centerX = bounds.Left + (width / 2.0);
        double centerY = bounds.Top + (height / 2.0);
        double normalizedX = (point.X - centerX) / radiusX;
        double normalizedY = (point.Y - centerY) / radiusY;

        return (normalizedX * normalizedX) + (normalizedY * normalizedY) <= 1.0;
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
            ClearSelectedShape();
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
            ClearSelectedShape();
        }
        finally
        {
            _isApplyingHistory = false;
        }
    }

    private void UndoRedoButton_Click(object sender, RoutedEventArgs e)
    {
        UndoHistory();
    }

    private void UndoRedoButton_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        RedoHistory();
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

            case InteractionState.MovingShape:
                CancelSelectedShapeDrag();
                break;

            case InteractionState.SelectingRange:
                CancelRangeSelection();
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

    private void CancelSelectedShapeMoveInteraction()
    {
        CancelSelectedShapeDrag();
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


    private void ApplyToolbarOrientation(ToolbarOrientationKind orientation, bool saveSettings)
    {
        _toolbarOrientation = orientation;

        bool isHorizontal = orientation == ToolbarOrientationKind.Horizontal;

        FullToolbarPanel.Orientation = isHorizontal
            ? Orientation.Horizontal
            : Orientation.Vertical;

        CtMiniPanel.Orientation = isHorizontal
            ? Orientation.Horizontal
            : Orientation.Vertical;

        CtMiniPanel.Width = isHorizontal ? double.NaN : 38.0;
        CtMiniPanel.Height = isHorizontal ? 38.0 : double.NaN;

        UpdateToolbarSpacerForOrientation(FullToolbarLeadingSpacer, isHorizontal);
        UpdateToolbarSpacerForOrientation(FullToolbarTrailingSpacer, isHorizontal);
        UpdateToolbarSpacerForOrientation(CtMiniLeadingSpacer, isHorizontal);
        UpdateToolbarSpacerForOrientation(CtMiniTrailingSpacer, isHorizontal);

        ToolbarPanel.UpdateLayout();

        if (IsLoaded)
        {
            ClampToolbarPositionToViewport(saveSettings: false);
        }

        UpdateToolbarOrientationSettingsUi();

        if (saveSettings)
        {
            SaveAppSettings();
        }
    }

    private static void UpdateToolbarSpacerForOrientation(Border spacer, bool isHorizontal)
    {
        spacer.Width = isHorizontal ? 8.0 : double.NaN;
        spacer.Height = isHorizontal ? double.NaN : 8.0;
    }

    private void UpdateToolbarOrientationSettingsUi()
    {
        if (ToolbarVerticalRadioButton == null || ToolbarHorizontalRadioButton == null)
        {
            return;
        }

        ToolbarVerticalRadioButton.IsChecked = _toolbarOrientation == ToolbarOrientationKind.Vertical;
        ToolbarHorizontalRadioButton.IsChecked = _toolbarOrientation == ToolbarOrientationKind.Horizontal;
    }

    private static ToolbarOrientationKind ParseToolbarOrientation(string? value)
    {
        return Enum.TryParse(value, ignoreCase: true, out ToolbarOrientationKind parsed)
            && Enum.IsDefined(parsed)
            ? parsed
            : ToolbarOrientationKind.Vertical;
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
        if (_isClickThroughEnabled && !IsCursorInsideToolbarPanel())
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

    private void UpdateClickThroughButtonLabel()
    {
        if (ClickThroughButtonLabel != null)
        {
            ClickThroughButtonLabel.Text = _isClickThroughEnabled ? SR.Off : SR.On;
        }

        if (CtReturnButtonLabel != null)
        {
            CtReturnButtonLabel.Text = _isClickThroughEnabled ? SR.Off : SR.On;
        }
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
        if (!_isClickThroughEnabled)
        {
            return false;
        }

        int raw = lParam.ToInt32();
        int screenX = unchecked((short)(raw & 0xFFFF));
        int screenY = unchecked((short)((raw >> 16) & 0xFFFF));

        return IsScreenPointInsideToolbarPanel(screenX, screenY);
    }

    private bool IsScreenPointInsideToolbarPanel(double screenX, double screenY)
    {
        if (ToolbarPanel == null || !ToolbarPanel.IsVisible || ToolbarPanel.ActualWidth <= 0 || ToolbarPanel.ActualHeight <= 0)
        {
            return false;
        }

        Point topLeft = ToolbarPanel.PointToScreen(new Point(0, 0));
        Point bottomRight = ToolbarPanel.PointToScreen(new Point(ToolbarPanel.ActualWidth, ToolbarPanel.ActualHeight));

        double left = Math.Min(topLeft.X, bottomRight.X);
        double top = Math.Min(topLeft.Y, bottomRight.Y);
        double right = Math.Max(topLeft.X, bottomRight.X);
        double bottom = Math.Max(topLeft.Y, bottomRight.Y);

        Rect bounds = new(left, top, right - left, bottom - top);
        bounds.Inflate(1.0, 1.0);

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
        _trayEnableClickThroughMenuItem = new Forms.ToolStripMenuItem(SR.TrayMenuTurnDrawingOff);
        _trayDisableClickThroughMenuItem = new Forms.ToolStripMenuItem(SR.TrayMenuTurnDrawingOn);
        var trayAboutMenuItem = new Forms.ToolStripMenuItem(SR.About);
        var trayExitMenuItem = new Forms.ToolStripMenuItem(SR.Exit);

        _trayEnableClickThroughMenuItem.Click += TrayEnableClickThroughMenuItem_Click;
        _trayDisableClickThroughMenuItem.Click += TrayDisableClickThroughMenuItem_Click;
        trayAboutMenuItem.Click += TrayAboutMenuItem_Click;
        trayExitMenuItem.Click += TrayExitMenuItem_Click;

        _notifyIcon.Text = "FlowInk";
        _notifyIcon.Icon = new Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "FlowInk.ico"));
        _notifyIcon.Visible = true;
        _notifyIcon.ContextMenuStrip = new Forms.ContextMenuStrip();
        _notifyIcon.ContextMenuStrip.Items.Add(_trayEnableClickThroughMenuItem);
        _notifyIcon.ContextMenuStrip.Items.Add(_trayDisableClickThroughMenuItem);
        _notifyIcon.ContextMenuStrip.Items.Add(new Forms.ToolStripSeparator());
        _notifyIcon.ContextMenuStrip.Items.Add(trayAboutMenuItem);
        _notifyIcon.ContextMenuStrip.Items.Add(trayExitMenuItem);
        _notifyIcon.DoubleClick += NotifyIcon_DoubleClick;

        UpdateNotifyIconMenu();
    }

    private void UpdateNotifyIconMenu()
    {
        _notifyIcon.Text = _isClickThroughEnabled
            ? SR.NotifyIconStatusOff
            : SR.NotifyIconStatusOn;

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
        EnsureTopmost(hwnd);
    }

    private void EnsureTopmost(IntPtr hwnd)
    {
        Topmost = true;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
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

        ShowToastMessage(string.Format(SR.DrawingDisabledToast, GetCurrentHotkeyDisplayText()));
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

    private void TrayAboutMenuItem_Click(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var dialog = new AboutDialog
            {
                Owner = this
            };

            dialog.ShowDialog();
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
                     PenButton, LineStyleButton, SelectButton, RectangleButton, CircleButton, TextButton, EraserButton, ColorButton,
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
            parts.Add(SR.HotkeyModifierCtrl);
        }

        if ((modifiers & MOD_ALT) != 0)
        {
            parts.Add(SR.HotkeyModifierAlt);
        }

        if ((modifiers & MOD_SHIFT) != 0)
        {
            parts.Add(SR.HotkeyModifierShift);
        }

        if ((modifiers & MOD_WIN) != 0)
        {
            parts.Add(SR.HotkeyModifierWin);
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
                string.Format(SR.GlobalHotkeyRegisterFailedFormat, GetCurrentHotkeyDisplayText()),
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
        UpdateToolbarOrientationSettingsUi();
        HotkeySettingsPopup.IsOpen = false;
        HotkeySettingsPopup.IsOpen = true;
    }

    private void ToolbarVerticalRadioButton_Click(object sender, RoutedEventArgs e)
    {
        if (_toolbarOrientation == ToolbarOrientationKind.Vertical)
        {
            return;
        }

        ApplyToolbarOrientation(ToolbarOrientationKind.Vertical, saveSettings: true);
        HotkeySettingsPopup.IsOpen = false;
    }

    private void ToolbarHorizontalRadioButton_Click(object sender, RoutedEventArgs e)
    {
        if (_toolbarOrientation == ToolbarOrientationKind.Horizontal)
        {
            return;
        }

        ApplyToolbarOrientation(ToolbarOrientationKind.Horizontal, saveSettings: true);
        HotkeySettingsPopup.IsOpen = false;
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
                SR.SelectAtLeastOneModifier,
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
        ShowToastMessage(string.Format(SR.GlobalHotkeyChangedFormat, GetCurrentHotkeyDisplayText()));
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
                _currentLineStyle = LineStyleKind.Solid;
                _penWidthPresets = new List<double>(GetDefaultPenWidthPresets());
                _penPresets = GetDefaultPenPresets();
                _currentEraserWidth = 4.0;
                _eraserWidthPresets = new List<double>(GetDefaultEraserWidthPresets());
                _currentTextFontFamilyName = DefaultTextFontFamilyName;
                _currentTextFontSize = DefaultTextFontSize;
                _currentTextFontStyle = FontStyles.Normal;
                _currentTextFontWeight = FontWeights.Normal;
                _isRectangleFilled = false;
                _rectangleFillOpacityPercent = 35;
                _toolbarOrientation = ToolbarOrientationKind.Vertical;
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
                _currentLineStyle = LineStyleKind.Solid;
                _penWidthPresets = new List<double>(GetDefaultPenWidthPresets());
                _penPresets = GetDefaultPenPresets();
                _currentEraserWidth = 4.0;
                _eraserWidthPresets = new List<double>(GetDefaultEraserWidthPresets());
                _currentTextFontFamilyName = DefaultTextFontFamilyName;
                _currentTextFontSize = DefaultTextFontSize;
                _currentTextFontStyle = FontStyles.Normal;
                _currentTextFontWeight = FontWeights.Normal;
                _isRectangleFilled = false;
                _rectangleFillOpacityPercent = 35;
                _toolbarOrientation = ToolbarOrientationKind.Vertical;
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
            _currentLineStyle = ParseLineStyle(settings.LineStyle);
            _penWidthPresets = NormalizePenWidthPresets(settings.PenWidthPresets);
            _penPresets = NormalizePenPresets(settings.PenPresets);
            _currentEraserWidth = NormalizePenWidth(settings.EraserWidth);
            _eraserWidthPresets = NormalizeEraserWidthPresets(settings.EraserWidthPresets);
            _currentTextFontFamilyName = NormalizeTextFontFamilyName(settings.TextFontFamily);
            _currentTextFontSize = NormalizeTextFontSize(settings.TextFontSize);
            _currentTextFontStyle = settings.TextItalic ? FontStyles.Italic : FontStyles.Normal;
            _currentTextFontWeight = settings.TextBold ? FontWeights.Bold : FontWeights.Normal;
            _isRectangleFilled = settings.RectangleFillEnabled;
            _rectangleFillOpacityPercent = NormalizeRectangleFillOpacity(settings.RectangleFillOpacity);
            _toolbarOrientation = ParseToolbarOrientation(settings.ToolbarOrientation);
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
            _currentLineStyle = LineStyleKind.Solid;
            _penWidthPresets = new List<double>(GetDefaultPenWidthPresets());
            _currentEraserWidth = 4.0;
            _eraserWidthPresets = new List<double>(GetDefaultEraserWidthPresets());
            _currentTextFontFamilyName = DefaultTextFontFamilyName;
            _currentTextFontSize = DefaultTextFontSize;
            _currentTextFontStyle = FontStyles.Normal;
            _currentTextFontWeight = FontWeights.Normal;
            _isRectangleFilled = false;
            _rectangleFillOpacityPercent = 35;
            _toolbarOrientation = ToolbarOrientationKind.Vertical;
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
        _penPresets = NormalizePenPresets(_penPresets);
        _eraserWidthPresets = NormalizeEraserWidthPresets(_eraserWidthPresets);

        var settings = new AppSettings
        {
            PresetColors = ToHexColorList(_presetColors),
            RecentColors = ToHexColorList(_recentColors),
            CustomColors = new List<int>(_customColorValues),
            PenWidth = NormalizePenWidth(_currentPenWidth),
            LineStyle = _currentLineStyle.ToString(),
            PenWidthPresets = new List<double>(_penWidthPresets),
            PenPresets = ToPenPresetSettings(_penPresets),
            EraserWidth = NormalizePenWidth(_currentEraserWidth),
            EraserWidthPresets = new List<double>(_eraserWidthPresets),
            CurrentColor = ToColorHexString(_currentPenColor),
            TextFontFamily = _currentTextFontFamilyName,
            TextFontSize = NormalizeTextFontSize(_currentTextFontSize),
            TextBold = _currentTextFontWeight == FontWeights.Bold,
            TextItalic = _currentTextFontStyle == FontStyles.Italic,
            RectangleFillEnabled = _isRectangleFilled,
            RectangleFillOpacity = NormalizeRectangleFillOpacity(_rectangleFillOpacityPercent),
            ToolbarOrientation = _toolbarOrientation.ToString(),
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

    private static List<PenPreset> GetDefaultPenPresets()
    {
        return new List<PenPreset>
        {
            new() { Color = Color.FromArgb(255, 255, 0, 0), Width = 1.0, OpacityPercent = 100 },
            new() { Color = Color.FromArgb(255, 255, 0, 0), Width = 5.0, OpacityPercent = 70 },
            new() { Color = Color.FromArgb(255, 255, 0, 0), Width = 11.0, OpacityPercent = 50 },
            new() { Color = Color.FromArgb(255, 255, 0, 0), Width = 20.0, OpacityPercent = 30 },

            new() { Color = Color.FromArgb(255, 0, 255, 0), Width = 1.0, OpacityPercent = 100 },
            new() { Color = Color.FromArgb(255, 0, 255, 0), Width = 5.0, OpacityPercent = 70 },
            new() { Color = Color.FromArgb(255, 0, 255, 0), Width = 11.0, OpacityPercent = 50 },
            new() { Color = Color.FromArgb(255, 0, 255, 0), Width = 20.0, OpacityPercent = 30 },

            new() { Color = Color.FromArgb(255, 0, 0, 255), Width = 1.0, OpacityPercent = 100 },
            new() { Color = Color.FromArgb(255, 0, 0, 255), Width = 5.0, OpacityPercent = 70 },
            new() { Color = Color.FromArgb(255, 0, 0, 255), Width = 11.0, OpacityPercent = 50 },
            new() { Color = Color.FromArgb(255, 0, 0, 255), Width = 20.0, OpacityPercent = 30 },

            new() { Color = Color.FromArgb(255, 255, 255, 0), Width = 1.0, OpacityPercent = 100 },
            new() { Color = Color.FromArgb(255, 255, 255, 0), Width = 5.0, OpacityPercent = 70 },
            new() { Color = Color.FromArgb(255, 255, 255, 0), Width = 11.0, OpacityPercent = 50 },
            new() { Color = Color.FromArgb(255, 255, 255, 0), Width = 20.0, OpacityPercent = 30 }

        };
    }

    private static List<PenPreset> GetPreviousDefaultPenPresets()
    {
        return new List<PenPreset>
        {
            new() { Color = Color.FromArgb(255, 255, 0, 0), Width = 2.0, OpacityPercent = 100 },
            new() { Color = Color.FromArgb(255, 0, 191, 255), Width = 2.0, OpacityPercent = 100 },
            new() { Color = Color.FromArgb(255, 255, 255, 0), Width = 2.0, OpacityPercent = 100 },
            new() { Color = Color.FromArgb(255, 50, 205, 50), Width = 2.0, OpacityPercent = 100 },
            new() { Color = Color.FromArgb(255, 255, 0, 0), Width = 4.0, OpacityPercent = 80 },
            new() { Color = Color.FromArgb(255, 0, 191, 255), Width = 4.0, OpacityPercent = 80 },
            new() { Color = Color.FromArgb(255, 255, 255, 0), Width = 6.0, OpacityPercent = 60 },
            new() { Color = Color.FromArgb(255, 50, 205, 50), Width = 6.0, OpacityPercent = 60 },
            new() { Color = Color.FromArgb(255, 255, 165, 0), Width = 8.0, OpacityPercent = 60 },
            new() { Color = Color.FromArgb(255, 255, 0, 255), Width = 8.0, OpacityPercent = 60 },
            new() { Color = Color.FromArgb(255, 255, 255, 255), Width = 10.0, OpacityPercent = 40 },
            new() { Color = Color.FromArgb(255, 0, 0, 0), Width = 10.0, OpacityPercent = 40 }
        };
    }

    private static List<PenPreset> NormalizePenPresets(List<PenPreset>? presets)
    {
        var normalized = new List<PenPreset>();

        if (presets != null)
        {
            foreach (PenPreset preset in presets)
            {
                normalized.Add(NormalizePenPreset(preset));

                if (normalized.Count >= PenPresetCount)
                {
                    break;
                }
            }
        }

        if (normalized.Count == 0)
        {
            foreach (PenPreset defaultPreset in GetDefaultPenPresets())
            {
                normalized.Add(NormalizePenPreset(defaultPreset));
            }

            return normalized;
        }

        if (IsPreviousDefaultPenPresetSet(normalized))
        {
            return NormalizePenPresets(GetDefaultPenPresets());
        }

        foreach (PenPreset fallback in GetDefaultPenPresets())
        {
            if (normalized.Count >= PenPresetCount)
            {
                break;
            }

            normalized.Add(NormalizePenPreset(fallback));
        }

        return normalized;
    }

    private static List<PenPreset> NormalizePenPresets(List<PenPresetSetting>? settings)
    {
        var presets = new List<PenPreset>();

        if (settings != null)
        {
            foreach (PenPresetSetting setting in settings)
            {
                Color color = Color.FromArgb(255, 255, 0, 0);
                int opacity = NormalizeOpacityPercent(setting.Opacity);

                if (!string.IsNullOrWhiteSpace(setting.Color))
                {
                    try
                    {
                        object? converted = ColorConverter.ConvertFromString(setting.Color);
                        if (converted is Color parsedColor)
                        {
                            color = Color.FromArgb(255, parsedColor.R, parsedColor.G, parsedColor.B);

                            if (setting.Opacity < 0 || setting.Opacity > 100)
                            {
                                opacity = GetOpacityPercent(parsedColor);
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                presets.Add(NormalizePenPreset(new PenPreset
                {
                    Color = color,
                    Width = setting.Width,
                    OpacityPercent = opacity
                }));

                if (presets.Count >= PenPresetCount)
                {
                    break;
                }
            }
        }

        return NormalizePenPresets(presets);
    }

    private static bool IsPreviousDefaultPenPresetSet(List<PenPreset> presets)
    {
        List<PenPreset> previousDefaults = GetPreviousDefaultPenPresets();

        if (presets.Count != previousDefaults.Count)
        {
            return false;
        }

        for (int i = 0; i < previousDefaults.Count; i++)
        {
            PenPreset preset = NormalizePenPreset(presets[i]);
            PenPreset previousDefault = NormalizePenPreset(previousDefaults[i]);

            if (preset.Color.R != previousDefault.Color.R
                || preset.Color.G != previousDefault.Color.G
                || preset.Color.B != previousDefault.Color.B
                || Math.Abs(preset.Width - previousDefault.Width) > 0.001
                || preset.OpacityPercent != previousDefault.OpacityPercent)
            {
                return false;
            }
        }

        return true;
    }

    private static PenPreset NormalizePenPreset(PenPreset preset)
    {
        int opacity = NormalizeOpacityPercent(preset.OpacityPercent);

        if (preset.Color.A != 255 && preset.OpacityPercent == 100)
        {
            opacity = GetOpacityPercent(preset.Color);
        }

        return new PenPreset
        {
            Color = Color.FromArgb(255, preset.Color.R, preset.Color.G, preset.Color.B),
            Width = NormalizePenWidth(preset.Width),
            OpacityPercent = opacity
        };
    }

    private static List<double> GetDefaultPenWidthPresets()
    {
        return new List<double> { 1.0, 2.0, 5.0, 11.0 };
    }

    private static List<double> GetDefaultEraserWidthPresets()
    {
        return new List<double> { 1.0, 2.0, 4.0, 10.0 };
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

    private static List<double> NormalizeEraserWidthPresets(List<double>? presets)
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

                if (normalized.Count >= EraserWidthPresetCount)
                {
                    break;
                }
            }
        }

        foreach (double fallback in GetDefaultEraserWidthPresets())
        {
            double normalizedFallback = NormalizePenWidth(fallback);

            if (normalized.Contains(normalizedFallback))
            {
                continue;
            }

            normalized.Add(normalizedFallback);

            if (normalized.Count >= EraserWidthPresetCount)
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

    private static List<PenPresetSetting> ToPenPresetSettings(List<PenPreset> presets)
    {
        var settings = new List<PenPresetSetting>();

        foreach (PenPreset preset in NormalizePenPresets(presets))
        {
            settings.Add(new PenPresetSetting
            {
                Color = ToColorHexString(Color.FromArgb(255, preset.Color.R, preset.Color.G, preset.Color.B)),
                Width = NormalizePenWidth(preset.Width),
                Opacity = NormalizeOpacityPercent(preset.OpacityPercent)
            });
        }

        return settings;
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

    private static int NormalizeOpacityPercent(int opacityPercent)
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

    private static int GetOpacityPercent(Color color)
    {
        return NormalizeOpacityPercent((int)Math.Round(color.A * 100.0 / 255.0));
    }

    private static int GetTransparencyPercentFromOpacity(int opacityPercent)
    {
        return 100 - NormalizeOpacityPercent(opacityPercent);
    }

    private static int GetTransparencyPercent(Color color)
    {
        return 100 - GetOpacityPercent(color);
    }

    private static Color CreateColorWithOpacity(Color color, int opacityPercent)
    {
        byte alpha = (byte)Math.Round(255.0 * NormalizeOpacityPercent(opacityPercent) / 100.0);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static bool ArePenColorsEqual(Color left, Color right)
    {
        return left.R == right.R && left.G == right.G && left.B == right.B;
    }

    private bool IsCurrentPenPreset(PenPreset preset)
    {
        PenPreset normalized = NormalizePenPreset(preset);
        return ArePenWidthsEqual(normalized.Width, _currentPenWidth)
            && NormalizeOpacityPercent(normalized.OpacityPercent) == GetOpacityPercent(_currentPenColor)
            && ArePenColorsEqual(normalized.Color, _currentPenColor);
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

            button.ToolTip = $"{GetColorDisplayText(color)}  {SR.PresetColorItemToolTipSuffix}";
            button.PreviewMouseLeftButtonDown += PresetColorButton_PreviewMouseLeftButtonDown;
            button.PreviewMouseRightButtonUp += PresetColorButton_PreviewMouseRightButtonUp;

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

    private void BuildPenPresetButtons()
    {
        _penPresets = NormalizePenPresets(_penPresets);

        PenPresetGrid.Children.Clear();

        for (int i = 0; i < PenPresetCount; i++)
        {
            PenPreset preset = _penPresets[i];
            var button = CreatePenPresetButton(i, preset);
            PenPresetGrid.Children.Add(button);
        }

        UpdatePenPresetButtonHighlight();
    }

    private Button CreatePenPresetButton(int index, PenPreset preset)
    {
        Color effectiveColor = CreateColorWithOpacity(preset.Color, preset.OpacityPercent);
        double previewDiameter = GetPenPresetPreviewDiameter(preset.Width);

        var checkerCircle = new WpfEllipse
        {
            Width = previewDiameter,
            Height = previewDiameter,
            Fill = (Brush)FindResource("CheckerBrush"),
            Stroke = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)),
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        var previewCircle = new WpfEllipse
        {
            Width = previewDiameter,
            Height = previewDiameter,
            Fill = new SolidColorBrush(effectiveColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        var outlineCircle = new WpfEllipse
        {
            Width = previewDiameter,
            Height = previewDiameter,
            Fill = Brushes.Transparent,
            Stroke = new SolidColorBrush(Color.FromArgb(96, 0, 0, 0)),
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        var circleHost = new Grid
        {
            Width = 34,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = false,
            IsHitTestVisible = false
        };

        circleHost.Children.Add(checkerCircle);
        circleHost.Children.Add(previewCircle);
        circleHost.Children.Add(outlineCircle);

        var button = new Button
        {
            Style = (Style)FindResource("PenPresetButtonStyle"),
            Content = circleHost,
            Tag = index,
            ToolTip = string.Format(
                SR.PenPresetItemToolTipFormat,
                FormatPenWidthText(preset.Width),
                GetTransparencyPercentFromOpacity(preset.OpacityPercent),
                GetColorHexDisplayText(effectiveColor))
        };

        button.PreviewMouseLeftButtonDown += PenPresetButton_PreviewMouseLeftButtonDown;
        button.PreviewMouseRightButtonUp += PenPresetButton_PreviewMouseRightButtonUp;

        return button;
    }

    private static double GetPenPresetPreviewDiameter(double width)
    {
        double normalized = NormalizePenWidth(width);
        double diameter = normalized <= 10.0
            ? 6.0 + normalized * 1.8
            : 24.0 + (normalized - 10.0) * 0.4;

        if (diameter < 8.0)
        {
            return 8.0;
        }

        if (diameter > 32.0)
        {
            return 32.0;
        }

        return diameter;
    }

    private void UpdatePenPresetButtonHighlight()
    {
        foreach (object child in PenPresetGrid.Children)
        {
            if (child is not Button button || button.Tag is not int index || index < 0 || index >= _penPresets.Count)
            {
                continue;
            }

            bool isSelected = IsCurrentPenPreset(_penPresets[index]);
            button.BorderBrush = isSelected ? Brushes.White : new SolidColorBrush(Color.FromRgb(102, 102, 102));
            button.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1);
        }
    }

    private void BuildPenWidthPresetButtons()
    {
        _penWidthPresets = NormalizePenWidthPresets(_penWidthPresets);

        PenWidthPresetGrid.Children.Clear();
        PenWidthPresetGrid.Columns = PenWidthPresetCount;
        PenWidthPresetGrid.Rows = 1;

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
        double previewDiameter = GetPenPresetPreviewDiameter(width);

        var checkerCircle = new WpfEllipse
        {
            Width = previewDiameter,
            Height = previewDiameter,
            Fill = (Brush)FindResource("CheckerBrush"),
            Stroke = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)),
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        var previewCircle = new WpfEllipse
        {
            Width = previewDiameter,
            Height = previewDiameter,
            Fill = new SolidColorBrush(_currentPenColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        var outlineCircle = new WpfEllipse
        {
            Width = previewDiameter,
            Height = previewDiameter,
            Fill = Brushes.Transparent,
            Stroke = new SolidColorBrush(Color.FromArgb(96, 0, 0, 0)),
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        var circleHost = new Grid
        {
            Width = 34,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = false,
            IsHitTestVisible = false
        };

        circleHost.Children.Add(checkerCircle);
        circleHost.Children.Add(previewCircle);
        circleHost.Children.Add(outlineCircle);

        var button = new Button
        {
            Style = (Style)FindResource("PenWidthPresetButtonStyle"),
            Content = circleHost,
            Tag = index,
            ToolTip = $"{FormatPenWidthText(width)}  {SR.PenWidthPresetItemToolTipSuffix}"
        };

        button.PreviewMouseLeftButtonDown += PenWidthPresetButton_PreviewMouseLeftButtonDown;
        button.PreviewMouseRightButtonUp += PenWidthPresetButton_PreviewMouseRightButtonUp;

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
        }
    }

    private void BuildEraserWidthPresetButtons()
    {
        _eraserWidthPresets = NormalizeEraserWidthPresets(_eraserWidthPresets);

        EraserWidthPresetGrid.Children.Clear();
        EraserWidthPresetGrid.Columns = EraserWidthPresetCount;
        EraserWidthPresetGrid.Rows = 1;

        for (int i = 0; i < EraserWidthPresetCount; i++)
        {
            double width = _eraserWidthPresets[i];
            var button = CreateEraserWidthPresetButton(i, width);
            EraserWidthPresetGrid.Children.Add(button);
        }

        UpdateEraserWidthPresetButtonHighlight();
    }

    private Button CreateEraserWidthPresetButton(int index, double width)
    {
        double previewDiameter = GetPenPresetPreviewDiameter(width);

        var previewCircle = new WpfEllipse
        {
            Width = previewDiameter,
            Height = previewDiameter,
            Fill = Brushes.White,
            Stroke = new SolidColorBrush(Color.FromArgb(96, 0, 0, 0)),
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        var circleHost = new Grid
        {
            Width = 34,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = false,
            IsHitTestVisible = false
        };

        circleHost.Children.Add(previewCircle);

        var button = new Button
        {
            Style = (Style)FindResource("PenWidthPresetButtonStyle"),
            Content = circleHost,
            Tag = index,
            ToolTip = $"{FormatPenWidthText(width)}  {SR.PenWidthPresetItemToolTipSuffix}"
        };

        button.PreviewMouseLeftButtonDown += EraserWidthPresetButton_PreviewMouseLeftButtonDown;
        button.PreviewMouseRightButtonUp += EraserWidthPresetButton_PreviewMouseRightButtonUp;
        return button;
    }

    private void UpdateEraserWidthPresetButtonHighlight()
    {
        foreach (object child in EraserWidthPresetGrid.Children)
        {
            if (child is not Button button || button.Tag is not int index || index < 0 || index >= _eraserWidthPresets.Count)
            {
                continue;
            }

            bool isSelected = ArePenWidthsEqual(_eraserWidthPresets[index], _currentEraserWidth);
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

    private static string GetColorHexDisplayText(Color color)
    {
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static string GetColorDisplayText(Color color)
    {
        int transparencyPercent = GetTransparencyPercent(color);
        return string.Format(SR.ColorDisplayWithAlphaFormat, color.A.ToString("X2"), color.R.ToString("X2"), color.G.ToString("X2"), color.B.ToString("X2"), transparencyPercent);
    }

    private void PresetColorButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button || button.Tag is not PresetColorSlot slot)
        {
            return;
        }

        e.Handled = true;

        if (slot.Index < 0 || slot.Index >= _presetColors.Count)
        {
            return;
        }

        ApplyPenColor(_presetColors[slot.Index], addToRecent: true);
        ColorPopup.IsOpen = false;
    }

    private void PresetColorButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button || button.Tag is not PresetColorSlot slot)
        {
            return;
        }

        e.Handled = true;

        if (slot.Index < 0 || slot.Index >= _presetColors.Count)
        {
            return;
        }

        ShowPresetColorContextMenu(button, slot.Index);
    }

    private void ShowPresetColorContextMenu(Button placementTarget, int presetIndex)
    {
        var editPresetColorItem = new MenuItem
        {
            Header = SR.EditPresetColor
        };
        editPresetColorItem.Click += (_, _) =>
        {
            ColorPopup.IsOpen = false;
            EditPresetColor(presetIndex);
        };

        var menu = new ContextMenu
        {
            PlacementTarget = placementTarget,
            Placement = PlacementMode.MousePoint
        };
        menu.Items.Add(editPresetColorItem);
        menu.IsOpen = true;
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

    private void PenPresetButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button || button.Tag is not int index)
        {
            return;
        }

        e.Handled = true;
        ApplyPenPreset(index);
    }

    private void PenPresetButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button || button.Tag is not int index)
        {
            return;
        }

        e.Handled = true;

        if (index < 0 || index >= _penPresets.Count)
        {
            return;
        }

        ShowPenPresetContextMenu(button, index);
    }

    private void ShowPenPresetContextMenu(Button placementTarget, int index)
    {
        var editPresetItem = new MenuItem
        {
            Header = SR.EditPenPreset
        };
        editPresetItem.Click += (_, _) =>
        {
            PenPresetPopup.IsOpen = false;
            EditPenPreset(index);
        };

        ShowToolbarContextMenu(placementTarget, editPresetItem);
    }

    private void OpenPenPresetPopup()
    {
        OpenPopupDeferred(PenPresetPopup, () =>
        {
            ColorPopup.IsOpen = false;
            LineStylePopup.IsOpen = false;
            EraserWidthPopup.IsOpen = false;
            RectangleSettingsPopup.IsOpen = false;
            HotkeySettingsPopup.IsOpen = false;
            BuildPenPresetButtons();
            BuildPenWidthPresetButtons();
        });
    }

    private void ApplyPenPreset(int index)
    {
        if (index < 0 || index >= _penPresets.Count)
        {
            return;
        }

        ActivatePenTool();
        ApplyPenPreset(_penPresets[index], addToRecent: true);
        PenPresetPopup.IsOpen = false;
    }

    private void ApplyPenPreset(PenPreset preset, bool addToRecent)
    {
        PenPreset normalized = NormalizePenPreset(preset);
        Color effectiveColor = CreateColorWithOpacity(normalized.Color, normalized.OpacityPercent);

        _currentPenWidth = NormalizePenWidth(normalized.Width);
        _currentPenColor = effectiveColor;
        DrawingCanvas.DefaultDrawingAttributes = CreatePenAttributes(_currentPenColor, _currentPenWidth);
        DrawingCanvas.SyncDynamicRendererDrawingAttributes();

        if (CurrentColorPreviewEllipse != null)
        {
            CurrentColorPreviewEllipse.Fill = new SolidColorBrush(_currentPenColor);
        }

        PenButton.FontWeight = FontWeights.Bold;
        ColorButton.FontWeight = FontWeights.Bold;

        if (addToRecent)
        {
            AddRecentColor(_currentPenColor);
        }

        UpdatePenPresetButtonHighlight();
        BuildPenWidthPresetButtons();
        SaveAppSettings();
    }

    private void EditPenPreset(int index)
    {
        _penPresets = NormalizePenPresets(_penPresets);

        if (index < 0 || index >= PenPresetCount)
        {
            return;
        }

        ActivatePenTool();

        PenPreset original = _penPresets[index];
        var dialog = new PenPresetDialog(
            CreateColorWithOpacity(original.Color, original.OpacityPercent),
            original.Width,
            original.OpacityPercent,
            BuildCustomColors())
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        CaptureCustomColors(dialog.CustomColors);

        var updatedPreset = NormalizePenPreset(new PenPreset
        {
            Color = dialog.SelectedColor,
            Width = dialog.SelectedWidth,
            OpacityPercent = dialog.SelectedOpacityPercent
        });

        var nextPresets = new List<PenPreset>(_penPresets);
        nextPresets[index] = updatedPreset;
        _penPresets = NormalizePenPresets(nextPresets);

        ApplyPenPreset(updatedPreset, addToRecent: true);
        SaveAppSettings();
        BuildPenPresetButtons();
        BuildPenWidthPresetButtons();
        PenPresetPopup.IsOpen = false;
    }

    private void PenWidthPresetButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button || button.Tag is not int index)
        {
            return;
        }

        e.Handled = true;
        ApplyPenWidthPreset(index);
    }

    private void PenWidthPresetButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button || button.Tag is not int index)
        {
            return;
        }

        e.Handled = true;

        if (index < 0 || index >= _penWidthPresets.Count)
        {
            return;
        }

        ShowPenWidthPresetContextMenu(button, index);
    }

    private void ShowPenWidthPresetContextMenu(Button placementTarget, int index)
    {
        var editPresetValueItem = new MenuItem
        {
            Header = SR.EditPresetValue
        };
        editPresetValueItem.Click += (_, _) =>
        {
            PenPresetPopup.IsOpen = false;
            EditPenWidthPreset(index);
        };

        ShowToolbarContextMenu(placementTarget, editPresetValueItem);
    }

    private void ApplyPenWidthPreset(int index)
    {
        if (index < 0 || index >= _penWidthPresets.Count)
        {
            return;
        }

        ActivatePenTool();
        SelectPenWidth(_penWidthPresets[index]);
        PenPresetPopup.IsOpen = false;
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
        PenPresetPopup.IsOpen = false;
    }

    private void EraserWidthPresetButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button || button.Tag is not int index)
        {
            return;
        }

        e.Handled = true;

        ApplyEraserWidthPreset(index);
    }

    private void EraserWidthPresetButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button || button.Tag is not int index)
        {
            return;
        }

        e.Handled = true;

        if (index < 0 || index >= _eraserWidthPresets.Count)
        {
            return;
        }

        ShowEraserWidthPresetContextMenu(button, index);
    }

    private void ShowEraserWidthPresetContextMenu(Button placementTarget, int index)
    {
        var editPresetValueItem = new MenuItem
        {
            Header = SR.EditPresetValue
        };
        editPresetValueItem.Click += (_, _) =>
        {
            EraserWidthPopup.IsOpen = false;
            EditEraserWidthPreset(index);
        };

        ShowToolbarContextMenu(placementTarget, editPresetValueItem);
    }

    private void OpenEraserWidthPresetPopup()
    {
        OpenPopupDeferred(EraserWidthPopup, () =>
        {
            ColorPopup.IsOpen = false;
            PenPresetPopup.IsOpen = false;
            RectangleSettingsPopup.IsOpen = false;
            HotkeySettingsPopup.IsOpen = false;
            BuildEraserWidthPresetButtons();
        });
    }

    private void ApplyEraserWidthPreset(int index)
    {
        if (index < 0 || index >= _eraserWidthPresets.Count)
        {
            return;
        }

        SelectEraserWidth(_eraserWidthPresets[index]);
        EraserWidthPopup.IsOpen = false;
    }

    private void EditEraserWidthPreset(int index)
    {
        _eraserWidthPresets = NormalizeEraserWidthPresets(_eraserWidthPresets);

        if (index < 0 || index >= EraserWidthPresetCount)
        {
            return;
        }

        ActivateEraserTool();

        var dialog = new EraserWidthDialog(_eraserWidthPresets[index])
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        double updated = NormalizePenWidth(dialog.SelectedWidth);

        var nextPresets = new List<double>(_eraserWidthPresets);
        nextPresets[index] = updated;
        _eraserWidthPresets = NormalizeEraserWidthPresets(nextPresets);

        SelectEraserWidth(updated);
        SaveAppSettings();
        BuildEraserWidthPresetButtons();
        EraserWidthPopup.IsOpen = false;
    }


    private void BuildLineStyleButtons()
    {
        if (LineStyleGrid == null)
        {
            return;
        }

        LineStyleGrid.Children.Clear();

        foreach (LineStyleKind lineStyle in new[] { LineStyleKind.Solid, LineStyleKind.Dotted })
        {
            var preview = new TextBlock
            {
                Text = GetLineStylePreviewText(lineStyle),
                FontSize = 14,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var label = new TextBlock
            {
                Text = GetLineStyleDisplayName(lineStyle),
                Foreground = Brushes.White,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(6, 2, 6, 2)
            };
            content.Children.Add(preview);
            content.Children.Add(label);

            var button = new Button
            {
                Tag = lineStyle,
                Content = content,
                MinWidth = 135,
                Height = 32,
                Margin = new Thickness(2),
                Padding = new Thickness(4),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = lineStyle == _currentLineStyle
                    ? _selectedButtonBackground
                    : _normalButtonBackground,
                Foreground = Brushes.White,
                BorderBrush = Brushes.DimGray,
                BorderThickness = new Thickness(1)
            };
            button.Click += LineStyleChoiceButton_Click;
            LineStyleGrid.Children.Add(button);
        }

        UpdateLineStyleButton();
    }

    private void LineStyleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isClickThroughEnabled)
        {
            return;
        }

        e.Handled = true;
        OpenLineStylePopup();
    }

    private void OpenLineStylePopup()
    {
        OpenPopupDeferred(LineStylePopup, () =>
        {
            ColorPopup.IsOpen = false;
            PenPresetPopup.IsOpen = false;
            EraserWidthPopup.IsOpen = false;
            RectangleSettingsPopup.IsOpen = false;
            HotkeySettingsPopup.IsOpen = false;
            BuildLineStyleButtons();
        });
    }

    private void LineStyleChoiceButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not LineStyleKind lineStyle)
        {
            return;
        }

        ApplyLineStyle(lineStyle);
    }

    private void ApplyLineStyle(LineStyleKind lineStyle)
    {
        _currentLineStyle = lineStyle;
        ApplyLineStyleToCanvas();
        LineStylePopup.IsOpen = false;
        SaveAppSettings();

        // Do not remove and rebuild the clicked Button while its routed Click event is running.
        Dispatcher.BeginInvoke(
            new Action(BuildLineStyleButtons),
            DispatcherPriority.Background);
    }

    private void ApplyLineStyleToCanvas()
    {
        DrawingCanvas.CurrentLineStyle = _currentLineStyle;
        UpdateLineStyleButton();
    }

    private void UpdateLineStyleButton()
    {
        if (LineStyleButtonPreview != null)
        {
            LineStyleButtonPreview.StrokeDashArray = GetLineStylePreviewDashArray(_currentLineStyle);
        }

        if (LineStyleButton != null)
        {
            LineStyleButton.ToolTip = $"{SR.LineStyle}: {GetLineStyleDisplayName(_currentLineStyle)}";
        }
    }

    private static DoubleCollection GetLineStylePreviewDashArray(LineStyleKind lineStyle)
    {
        return lineStyle switch
        {
            LineStyleKind.Dotted => new DoubleCollection { 0.1, 2.0 },
            LineStyleKind.Dashed => new DoubleCollection { 3.0, 2.0 },
            LineStyleKind.DashDot => new DoubleCollection { 3.0, 2.0, 0.1, 2.0 },
            _ => new DoubleCollection()
        };
    }

    private static string GetLineStylePreviewText(LineStyleKind lineStyle)
    {
        return lineStyle switch
        {
            LineStyleKind.Dotted => "• • • •",
            LineStyleKind.Dashed => "━ ━ ━",
            LineStyleKind.DashDot => "━ • ━ •",
            _ => "━━━━"
        };
    }

    private static string GetLineStyleDisplayName(LineStyleKind lineStyle)
    {
        return lineStyle switch
        {
            LineStyleKind.Dotted => SR.DottedLine,
            LineStyleKind.Solid => SR.SolidLine,
            _ => lineStyle.ToString()
        };
    }

    private static LineStyleKind ParseLineStyle(string? value)
    {
        return Enum.TryParse(value, ignoreCase: true, out LineStyleKind parsed)
            && Enum.IsDefined(parsed)
            ? parsed
            : LineStyleKind.Solid;
    }

    private void ActivatePenTool()
    {
        FinalizeOrCancelCurrentOperation();
        ClearSelectedTextElement();
        ClearSelectedShape();

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
        BuildPenPresetButtons();
    }

    private void ApplyPenColor(Color color, bool addToRecent)
    {
        _currentPenColor = color;
        DrawingCanvas.DefaultDrawingAttributes = CreatePenAttributes(_currentPenColor, _currentPenWidth);
        DrawingCanvas.SyncDynamicRendererDrawingAttributes();

        if (CurrentColorPreviewEllipse != null)
        {
            CurrentColorPreviewEllipse.Fill = new SolidColorBrush(_currentPenColor);
        }

        ColorButton.FontWeight = FontWeights.Bold;
        UpdatePenPresetButtonHighlight();
        BuildPenWidthPresetButtons();

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



    private Stroke CreateStyledStroke(StylusPointCollection stylusPoints)
    {
        return new StyledStroke(
            stylusPoints,
            CreatePenAttributes(_currentPenColor, _currentPenWidth),
            _currentLineStyle);
    }

    private void ApplyEraserWidthToCanvas()
    {
        DrawingCanvas.EraserShape = new EllipseStylusShape(_currentEraserWidth, _currentEraserWidth);
    }

    private void UpdateShapeButtonToolTips()
    {
        int rectangleFillTransparencyPercent = GetTransparencyPercentFromOpacity(_rectangleFillOpacityPercent);

        string rectangleText = _isRectangleFilled
            ? string.Format(SR.RectangleFilledToolTipFormat, rectangleFillTransparencyPercent)
            : SR.RectangleNotFilledToolTip;

        string circleText = _isRectangleFilled
            ? string.Format(SR.CircleFilledToolTipFormat, rectangleFillTransparencyPercent)
            : SR.CircleNotFilledToolTip;

        RectangleButton.ToolTip = rectangleText;
        CircleButton.ToolTip = circleText;
    }

    private void UpdateRectangleSettingsUi()
    {
        int rectangleFillTransparencyPercent = GetTransparencyPercentFromOpacity(_rectangleFillOpacityPercent);

        if (RectangleFillCheckBox != null)
        {
            RectangleFillCheckBox.IsChecked = _isRectangleFilled;
        }

        if (RectangleOpacitySlider != null)
        {
            RectangleOpacitySlider.Value = rectangleFillTransparencyPercent;
            RectangleOpacitySlider.IsEnabled = _isRectangleFilled;
        }

        if (RectangleOpacityLabel != null)
        {
            RectangleOpacityLabel.Text = string.Format(SR.TransparencyFormat, rectangleFillTransparencyPercent);
            RectangleOpacityLabel.Opacity = _isRectangleFilled ? 1.0 : 0.55;
        }
    }

    private void OpenShapeSettingsPopup(Button placementTarget)
    {
        OpenPopupDeferred(RectangleSettingsPopup, () =>
        {
            RectangleSettingsPopup.PlacementTarget = placementTarget;
            PenPresetPopup.IsOpen = false;
            LineStylePopup.IsOpen = false;
            EraserWidthPopup.IsOpen = false;
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

        Point mousePoint = e.GetPosition(DrawingCanvas);

        if (_currentTool == ToolMode.Select)
        {
            bool isShiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            Border? clickedTextElement = FindCommittedTextHost(e.OriginalSource);

            CommitActiveTextInput();
            DrawingCanvas.Focus();

            if (clickedTextElement != null)
            {
                if (isShiftPressed)
                {
                    ToggleTextElementSelection(clickedTextElement);
                }
                else if (IsTextElementSelected(clickedTextElement))
                {
                    BeginTextElementDrag(clickedTextElement, mousePoint);
                }
                else
                {
                    ClearAllSelectedObjects();
                    AddTextElementToSelection(clickedTextElement);
                }

                e.Handled = true;
                return;
            }

            DrawableSelectionCandidate? candidate = FindDrawableSelectionAtPoint(mousePoint);
            if (candidate != null)
            {
                if (isShiftPressed)
                {
                    ToggleDrawableSelection(candidate);
                }
                else
                {
                    if (!IsDrawableSelectionSelected(candidate))
                    {
                        ClearAllSelectedObjects();
                        AddDrawableSelection(candidate);
                    }

                    BeginSelectedShapeDrag(mousePoint);
                }

                e.Handled = true;
                return;
            }

            BeginRangeSelection(mousePoint, isShiftPressed);
            e.Handled = true;
            return;
        }

        if (HasSelectedShape())
        {
            if (IsPointOnSelectedShape(mousePoint))
            {
                CommitActiveTextInput();
                BeginSelectedShapeDrag(mousePoint);
                e.Handled = true;
                return;
            }

            if (!IsClickOnActiveTextBox(e.OriginalSource) && !IsClickOnCommittedTextElement(e.OriginalSource))
            {
                ClearSelectedShape();
            }
        }

        if (_currentTool == ToolMode.Pen && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            _currentInteractionState = InteractionState.DrawingPen;
        }

        if (_currentTool == ToolMode.Text)
        {
            if (IsClickOnActiveTextBox(e.OriginalSource))
            {
                return;
            }

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
            EraseCanvasElementsAtPoint(e.GetPosition(DrawingCanvas));
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

    private bool IsClickOnActiveTextBox(object originalSource)
    {
        if (_activeTextBox == null)
        {
            return false;
        }

        DependencyObject? current = originalSource as DependencyObject;

        while (current != null)
        {
            if (ReferenceEquals(current, _activeTextBox))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
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

    private void DrawingCanvas_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThroughEnabled)
        {
            return;
        }

        if (IsClickOnActiveTextBox(e.OriginalSource) || IsClickOnCommittedTextElement(e.OriginalSource))
        {
            return;
        }

        FinalizeOrCancelCurrentOperation();
        CommitActiveTextInput();

        Point point = e.GetPosition(DrawingCanvas);
        DrawableSelectionCandidate? candidate = FindDrawableSelectionAtPoint(point);

        if (candidate != null)
        {
            if (!IsDrawableSelectionSelected(candidate))
            {
                ClearAllSelectedObjects();
                AddDrawableSelection(candidate);
            }

            ShowSelectedShapeContextMenu();
            e.Handled = true;
            return;
        }

        ClearAllSelectedObjects();
    }

    private void DrawingCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isRangeSelecting)
        {
            UpdateRangeSelection(e.GetPosition(DrawingCanvas));
            e.Handled = true;
            return;
        }

        if (_isDraggingSelectedShape)
        {
            UpdateSelectedShapeDrag(e.GetPosition(DrawingCanvas));
            e.Handled = true;
            return;
        }

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
            EraseCanvasElementsAtPoint(e.GetPosition(DrawingCanvas));
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
        if (_isRangeSelecting)
        {
            CompleteRangeSelection(e.GetPosition(DrawingCanvas));
            e.Handled = true;
            return;
        }

        if (_isDraggingSelectedShape)
        {
            EndSelectedShapeDrag();
            e.Handled = true;
            return;
        }

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
        if (_isRangeSelecting)
        {
            CancelRangeSelection();
        }

        if (_isDraggingSelectedShape)
        {
            CancelSelectedShapeDrag();
        }

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

        RegisterStrokeSelectionGroup(finalStroke, arrowHeadStroke);

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

        return CreateStyledStroke(stylusPoints);
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

        return CreateStyledStroke(stylusPoints);
    }

    private void UpdateRectanglePreview(Point startPoint, Point endPoint)
    {
        CancelRectanglePreview();

        Stroke outlineStroke = CreateRectangleOutlineStroke(startPoint, endPoint);
        _rectanglePreviewStroke = outlineStroke;
        ExecuteWithoutStrokeHistory(() => DrawingCanvas.Strokes.Add(outlineStroke));

        if (_isRectangleFilled)
        {
            WpfShape fillShape = CreateFilledRectangleShape(startPoint, endPoint);
            _rectanglePreviewFillShape = fillShape;
            AddCanvasElement(fillShape);
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
            RegisterShapeOutline(outlineStroke, ShapeKind.Rectangle);
            DrawingCanvas.Strokes.Add(outlineStroke);
            return;
        }

        WpfShape fillShape = CreateFilledRectangleShape(startPoint, endPoint);
        RegisterFilledShape(fillShape, outlineStroke, ShapeKind.Rectangle);
        int fillShapeIndex = GetCanvasElementInsertIndex();

        ExecuteWithoutStrokeHistory(() =>
        {
            AddCanvasElement(fillShape, fillShapeIndex);
            DrawingCanvas.Strokes.Add(outlineStroke);
        });

        PushHistory(new CompositeAction(new IUndoableAction[]
        {
            new CanvasElementAddAction(fillShape, fillShapeIndex),
            new StrokeCollectionAction(new[] { outlineStroke }, Array.Empty<Stroke>())
        }));
    }

    private void CancelRectanglePreview()
    {
        if (_rectanglePreviewStroke != null)
        {
            Stroke previewStroke = _rectanglePreviewStroke;
            ExecuteWithoutStrokeHistory(() => DrawingCanvas.Strokes.Remove(previewStroke));
            _rectanglePreviewStroke = null;
        }

        if (_rectanglePreviewFillShape != null)
        {
            WpfShape previewFillShape = _rectanglePreviewFillShape;
            RemoveCanvasElementIfPresent(previewFillShape);
            _rectanglePreviewFillShape = null;
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
            WpfShape fillShape = CreateFilledEllipseShape(startPoint, endPoint);
            _circlePreviewFillShape = fillShape;
            AddCanvasElement(fillShape);
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
            RegisterShapeOutline(outlineStroke, ShapeKind.Ellipse);
            DrawingCanvas.Strokes.Add(outlineStroke);
            return;
        }

        WpfShape fillShape = CreateFilledEllipseShape(startPoint, endPoint);
        RegisterFilledShape(fillShape, outlineStroke, ShapeKind.Ellipse);
        int fillShapeIndex = GetCanvasElementInsertIndex();

        ExecuteWithoutStrokeHistory(() =>
        {
            AddCanvasElement(fillShape, fillShapeIndex);
            DrawingCanvas.Strokes.Add(outlineStroke);
        });

        PushHistory(new CompositeAction(new IUndoableAction[]
        {
            new CanvasElementAddAction(fillShape, fillShapeIndex),
            new StrokeCollectionAction(new[] { outlineStroke }, Array.Empty<Stroke>())
        }));
    }

    private void CancelCirclePreview()
    {
        if (_circlePreviewStroke != null)
        {
            Stroke previewStroke = _circlePreviewStroke;
            ExecuteWithoutStrokeHistory(() => DrawingCanvas.Strokes.Remove(previewStroke));
            _circlePreviewStroke = null;
        }

        if (_circlePreviewFillShape != null)
        {
            WpfShape previewFillShape = _circlePreviewFillShape;
            RemoveCanvasElementIfPresent(previewFillShape);
            _circlePreviewFillShape = null;
        }
    }

    private WpfShape CreateFilledRectangleShape(Point startPoint, Point endPoint)
    {
        double left = Math.Min(startPoint.X, endPoint.X);
        double top = Math.Min(startPoint.Y, endPoint.Y);
        double width = Math.Abs(endPoint.X - startPoint.X);
        double height = Math.Abs(endPoint.Y - startPoint.Y);

        var shape = new WpfRectangle
        {
            Width = width,
            Height = height,
            Fill = CreateShapeFillBrush(),
            StrokeThickness = 0,
            IsHitTestVisible = false
        };

        InkCanvas.SetLeft(shape, left);
        InkCanvas.SetTop(shape, top);

        return shape;
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

        return CreateStyledStroke(stylusPoints);
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

        return CreateStyledStroke(stylusPoints);
    }

    private WpfShape CreateFilledEllipseShape(Point startPoint, Point endPoint)
    {
        double left = Math.Min(startPoint.X, endPoint.X);
        double top = Math.Min(startPoint.Y, endPoint.Y);
        double width = Math.Abs(endPoint.X - startPoint.X);
        double height = Math.Abs(endPoint.Y - startPoint.Y);

        var shape = new WpfEllipse
        {
            Width = width,
            Height = height,
            Fill = CreateShapeFillBrush(),
            StrokeThickness = 0,
            IsHitTestVisible = false
        };

        InkCanvas.SetLeft(shape, left);
        InkCanvas.SetTop(shape, top);

        return shape;
    }

    private SolidColorBrush CreateShapeFillBrush()
    {
        Color fillColor = Color.FromArgb(
            (byte)Math.Round(255.0 * NormalizeRectangleFillOpacity(_rectangleFillOpacityPercent) / 100.0),
            _currentPenColor.R,
            _currentPenColor.G,
            _currentPenColor.B);

        var brush = new SolidColorBrush(fillColor);
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }

    private void UpdateShapeOutlineMappingsForStrokeChanges(IEnumerable<Stroke> addedStrokes, IEnumerable<Stroke> removedStrokes)
    {
        var removedShapeOutlines = new List<RemovedShapeOutlineInfo>();

        foreach (Stroke removedStroke in removedStrokes)
        {
            ShapeKind? kind = GetKnownOrInferredShapeKind(removedStroke);
            if (!kind.HasValue)
            {
                continue;
            }

            _outlineStrokeFilledShapes.TryGetValue(removedStroke, out WpfShape? fillShape);
            int outlineGroupId = GetOrCreateShapeOutlineGroupId(removedStroke);
            removedShapeOutlines.Add(new RemovedShapeOutlineInfo(removedStroke, kind.Value, fillShape, outlineGroupId));
        }

        if (removedShapeOutlines.Count == 0)
        {
            return;
        }

        foreach (Stroke addedStroke in addedStrokes)
        {
            foreach (RemovedShapeOutlineInfo removedInfo in removedShapeOutlines)
            {
                if (!IsStrokeDerivedFromRemovedOutline(addedStroke, removedInfo))
                {
                    continue;
                }

                if (removedInfo.FillShape != null
                    && DrawingCanvas.Children.Contains(removedInfo.FillShape)
                    && !IsStrokeOnFillShapeBoundary(addedStroke, removedInfo.FillShape, removedInfo.Kind))
                {
                    continue;
                }

                RegisterShapeOutline(addedStroke, removedInfo.Kind, removedInfo.OutlineGroupId);
                if (removedInfo.FillShape != null && DrawingCanvas.Children.Contains(removedInfo.FillShape))
                {
                    RegisterFilledShapeOutlineFragment(removedInfo.FillShape, addedStroke, removedInfo.Kind, removedInfo.OutlineGroupId);
                }

                break;
            }
        }
    }

    private static bool IsStrokeDerivedFromRemovedOutline(Stroke addedStroke, RemovedShapeOutlineInfo removedInfo)
    {
        Rect addedBounds = GetStrokeBounds(addedStroke);
        Rect expandedRemovedBounds = removedInfo.Bounds;
        double tolerance = Math.Max(4.0, removedInfo.Tolerance + 2.0);
        expandedRemovedBounds.Inflate(tolerance, tolerance);

        if (!expandedRemovedBounds.Contains(addedBounds.TopLeft)
            || !expandedRemovedBounds.Contains(addedBounds.BottomRight))
        {
            return false;
        }

        StylusPointCollection points = addedStroke.StylusPoints;
        if (points.Count == 0)
        {
            return false;
        }

        int insideCount = 0;
        foreach (StylusPoint point in points)
        {
            if (expandedRemovedBounds.Contains(ToPoint(point)))
            {
                insideCount++;
            }
        }

        int requiredCount = Math.Max(1, (int)Math.Ceiling(points.Count * 0.75));
        return insideCount >= requiredCount;
    }

    private void RegisterShapeOutline(Stroke outlineStroke, ShapeKind kind)
    {
        RegisterShapeOutline(outlineStroke, kind, null);
    }

    private void RegisterShapeOutline(Stroke outlineStroke, ShapeKind kind, int? outlineGroupId)
    {
        _shapeOutlineKinds[outlineStroke] = kind;

        if (outlineGroupId.HasValue)
        {
            _shapeOutlineGroupIds[outlineStroke] = outlineGroupId.Value;
            return;
        }

        GetOrCreateShapeOutlineGroupId(outlineStroke);
    }

    private void RegisterStrokeSelectionGroup(params Stroke[] strokes)
    {
        int groupId = _nextShapeOutlineGroupId++;

        foreach (Stroke stroke in strokes)
        {
            _shapeOutlineGroupIds[stroke] = groupId;
        }
    }

    private int GetOrCreateShapeOutlineGroupId(Stroke outlineStroke)
    {
        if (_shapeOutlineGroupIds.TryGetValue(outlineStroke, out int outlineGroupId))
        {
            return outlineGroupId;
        }

        int newGroupId = _nextShapeOutlineGroupId++;
        _shapeOutlineGroupIds[outlineStroke] = newGroupId;
        return newGroupId;
    }

    private void RegisterFilledShape(WpfShape fillShape, Stroke outlineStroke, ShapeKind kind)
    {
        _filledShapeOutlineStrokes[fillShape] = outlineStroke;
        RegisterFilledShapeOutlineFragment(fillShape, outlineStroke, kind);
    }

    private void RegisterFilledShapeOutlineFragment(WpfShape fillShape, Stroke outlineStroke, ShapeKind kind)
    {
        RegisterFilledShapeOutlineFragment(fillShape, outlineStroke, kind, null);
    }

    private void RegisterFilledShapeOutlineFragment(WpfShape fillShape, Stroke outlineStroke, ShapeKind kind, int? outlineGroupId)
    {
        _outlineStrokeFilledShapes[outlineStroke] = fillShape;
        RegisterShapeOutline(outlineStroke, kind, outlineGroupId);

        if (!_filledShapeOutlineStrokes.TryGetValue(fillShape, out Stroke? primaryStroke)
            || !DrawingCanvas.Strokes.Contains(primaryStroke))
        {
            _filledShapeOutlineStrokes[fillShape] = outlineStroke;
        }
    }

    private bool HasSelectedShape()
    {
        return GetSelectedFillShapesSnapshot().Count > 0
            || GetSelectedShapeOutlineStrokesSnapshot().Count > 0;
    }





    private void ClearAllSelectedObjects()
    {
        ClearSelectedShape();
        ClearSelectedTextElement();
    }

    private void BeginRangeSelection(Point startPoint, bool addToExisting)
    {
        _isRangeSelecting = true;
        _hasRangeSelectionDragMoved = false;
        _rangeSelectionAddsToExisting = addToExisting;
        _rangeSelectionStartPoint = startPoint;
        _currentInteractionState = InteractionState.SelectingRange;

        RemoveRangeSelectionAdorner();

        if (DrawingCanvas.IsMouseCaptured)
        {
            DrawingCanvas.ReleaseMouseCapture();
        }

        DrawingCanvas.CaptureMouse();
    }

    private void UpdateRangeSelection(Point currentPoint)
    {
        if (!_isRangeSelecting)
        {
            return;
        }

        Rect bounds = CreateNormalizedRect(_rangeSelectionStartPoint, currentPoint);
        if (bounds.Width >= RangeSelectionMinimumDragDistance || bounds.Height >= RangeSelectionMinimumDragDistance)
        {
            _hasRangeSelectionDragMoved = true;
        }

        if (!_hasRangeSelectionDragMoved)
        {
            return;
        }

        UpdateRangeSelectionAdorner(bounds);
    }

    private void CompleteRangeSelection(Point endPoint)
    {
        if (!_isRangeSelecting)
        {
            return;
        }

        Rect bounds = CreateNormalizedRect(_rangeSelectionStartPoint, endPoint);
        bool shouldSelectRange = _hasRangeSelectionDragMoved
            && bounds.Width >= RangeSelectionMinimumDragDistance
            && bounds.Height >= RangeSelectionMinimumDragDistance;

        bool addToExisting = _rangeSelectionAddsToExisting;

        _isRangeSelecting = false;
        _hasRangeSelectionDragMoved = false;
        _rangeSelectionAddsToExisting = false;
        _currentInteractionState = InteractionState.None;

        if (DrawingCanvas.IsMouseCaptured)
        {
            DrawingCanvas.ReleaseMouseCapture();
        }

        RemoveRangeSelectionAdorner();

        if (!shouldSelectRange)
        {
            if (!addToExisting)
            {
                ClearAllSelectedObjects();
            }

            return;
        }

        if (!addToExisting)
        {
            ClearAllSelectedObjects();
        }

        AddObjectsInsideSelectionRange(bounds);
        UpdateShapeSelectionAdorner();
    }

    private void CancelRangeSelection()
    {
        if (!_isRangeSelecting)
        {
            return;
        }

        _isRangeSelecting = false;
        _hasRangeSelectionDragMoved = false;
        _rangeSelectionAddsToExisting = false;

        if (_currentInteractionState == InteractionState.SelectingRange)
        {
            _currentInteractionState = InteractionState.None;
        }

        if (DrawingCanvas.IsMouseCaptured)
        {
            DrawingCanvas.ReleaseMouseCapture();
        }

        RemoveRangeSelectionAdorner();
    }

    private void UpdateRangeSelectionAdorner(Rect bounds)
    {
        if (_rangeSelectionAdorner == null)
        {
            _rangeSelectionAdorner = new WpfRectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(24, 0, 191, 255)),
                Stroke = Brushes.DeepSkyBlue,
                StrokeThickness = 1.0,
                StrokeDashArray = new DoubleCollection { 4.0, 3.0 },
                IsHitTestVisible = false
            };
        }

        _rangeSelectionAdorner.Width = Math.Max(1.0, bounds.Width);
        _rangeSelectionAdorner.Height = Math.Max(1.0, bounds.Height);
        InkCanvas.SetLeft(_rangeSelectionAdorner, bounds.Left);
        InkCanvas.SetTop(_rangeSelectionAdorner, bounds.Top);

        if (DrawingCanvas.Children.Contains(_rangeSelectionAdorner))
        {
            DrawingCanvas.Children.Remove(_rangeSelectionAdorner);
        }

        DrawingCanvas.Children.Add(_rangeSelectionAdorner);
    }

    private void RemoveRangeSelectionAdorner()
    {
        if (_rangeSelectionAdorner == null)
        {
            return;
        }

        RemoveCanvasElementIfPresent(_rangeSelectionAdorner);
        _rangeSelectionAdorner = null;
    }

    private void AddObjectsInsideSelectionRange(Rect selectionBounds)
    {
        foreach (WpfShape fillShape in GetFillShapesInsideRange(selectionBounds))
        {
            AddDrawableSelection(new DrawableSelectionCandidate(fillShape, FindOutlineStrokesForFillShape(fillShape)));
        }

        foreach (Stroke stroke in GetStrokesInsideRange(selectionBounds))
        {
            WpfShape? pairedFillShape = null;
            if (_outlineStrokeFilledShapes.TryGetValue(stroke, out WpfShape? registeredFill)
                && DrawingCanvas.Children.Contains(registeredFill))
            {
                pairedFillShape = registeredFill;
            }

            List<Stroke> outlineStrokes = pairedFillShape != null
                ? FindOutlineStrokesForFillShape(pairedFillShape)
                : FindOutlineStrokesInSameShape(stroke);

            AddDrawableSelection(new DrawableSelectionCandidate(pairedFillShape, outlineStrokes));
        }

        foreach (Border textElement in GetTextElementsInsideRange(selectionBounds))
        {
            AddTextElementToSelection(textElement);
        }
    }

    private List<WpfShape> GetFillShapesInsideRange(Rect selectionBounds)
    {
        var result = new List<WpfShape>();

        foreach (UIElement child in DrawingCanvas.Children)
        {
            if (ReferenceEquals(child, _shapeSelectionAdorner)
                || ReferenceEquals(child, _rangeSelectionAdorner))
            {
                continue;
            }

            if (child is WpfShape shape
                && shape.Fill != null
                && shape.StrokeThickness == 0
                && IsRectInsideRect(selectionBounds, GetShapeBounds(shape)))
            {
                result.Add(shape);
            }
        }

        return result;
    }

    private List<Stroke> GetStrokesInsideRange(Rect selectionBounds)
    {
        var result = new List<Stroke>();

        foreach (Stroke stroke in DrawingCanvas.Strokes)
        {
            Rect strokeBounds = GetStrokeBounds(stroke);
            if (strokeBounds.IsEmpty)
            {
                continue;
            }

            double visibleMargin = Math.Max(stroke.DrawingAttributes.Width, stroke.DrawingAttributes.Height) / 2.0;
            strokeBounds.Inflate(visibleMargin, visibleMargin);

            if (IsRectInsideRect(selectionBounds, strokeBounds))
            {
                AddStrokeReferenceIfMissing(result, stroke);
            }
        }

        return result;
    }

    private List<Border> GetTextElementsInsideRange(Rect selectionBounds)
    {
        var result = new List<Border>();

        foreach (Border textElement in _textElements)
        {
            if (IsRectInsideRect(selectionBounds, GetTextElementBounds(textElement)))
            {
                result.Add(textElement);
            }
        }

        return result;
    }

    private static Rect CreateNormalizedRect(Point first, Point second)
    {
        double left = Math.Min(first.X, second.X);
        double top = Math.Min(first.Y, second.Y);
        double right = Math.Max(first.X, second.X);
        double bottom = Math.Max(first.Y, second.Y);

        return new Rect(left, top, Math.Max(0.0, right - left), Math.Max(0.0, bottom - top));
    }

    private static bool IsRectInsideRect(Rect outer, Rect inner)
    {
        if (outer.IsEmpty || inner.IsEmpty)
        {
            return false;
        }

        return outer.Contains(inner.TopLeft)
            && outer.Contains(inner.BottomRight);
    }

    private DrawableSelectionCandidate? FindDrawableSelectionAtPoint(Point point)
    {
        WpfShape? fillShape = FindFillShapeAtPoint(point);
        if (fillShape != null)
        {
            return new DrawableSelectionCandidate(fillShape, FindOutlineStrokesForFillShape(fillShape));
        }

        Stroke? shapeStroke = FindShapeOutlineStrokeAtPoint(point);
        if (shapeStroke != null)
        {
            WpfShape? pairedFillShape = null;
            if (_outlineStrokeFilledShapes.TryGetValue(shapeStroke, out WpfShape? registeredFill)
                && DrawingCanvas.Children.Contains(registeredFill))
            {
                pairedFillShape = registeredFill;
            }

            List<Stroke> outlineStrokes = pairedFillShape != null
                ? FindOutlineStrokesForFillShape(pairedFillShape)
                : FindOutlineStrokesInSameShape(shapeStroke);

            return new DrawableSelectionCandidate(pairedFillShape, outlineStrokes);
        }

        Stroke? stroke = FindSelectableStrokeAtPoint(point);
        if (stroke == null)
        {
            return null;
        }

        return new DrawableSelectionCandidate(null, FindOutlineStrokesInSameShape(stroke));
    }

    private bool IsDrawableSelectionSelected(DrawableSelectionCandidate candidate)
    {
        if (candidate.FillShape != null && ContainsShapeReference(_selectedFillShapes, candidate.FillShape))
        {
            return true;
        }

        foreach (Stroke stroke in candidate.OutlineStrokes)
        {
            if (ContainsStrokeReference(_selectedShapeOutlineStrokes, stroke))
            {
                return true;
            }
        }

        return false;
    }

    private void AddDrawableSelection(DrawableSelectionCandidate candidate)
    {
        if (candidate.FillShape != null
            && DrawingCanvas.Children.Contains(candidate.FillShape)
            && !ContainsShapeReference(_selectedFillShapes, candidate.FillShape))
        {
            _selectedFillShapes.Add(candidate.FillShape);
            _selectedFillShape = candidate.FillShape;
        }

        foreach (Stroke stroke in candidate.OutlineStrokes)
        {
            AddSelectedShapeOutlineStroke(stroke);
        }

        _selectedShapeOutlineStroke = _selectedShapeOutlineStrokes.Count > 0
            ? _selectedShapeOutlineStrokes[0]
            : null;

        UpdateShapeSelectionAdorner();
    }

    private void RemoveDrawableSelection(DrawableSelectionCandidate candidate)
    {
        if (candidate.FillShape != null)
        {
            RemoveShapeReference(_selectedFillShapes, candidate.FillShape);
        }

        foreach (Stroke stroke in candidate.OutlineStrokes)
        {
            RemoveStrokeReference(_selectedShapeOutlineStrokes, stroke);
        }

        _selectedFillShape = _selectedFillShapes.Count > 0
            ? _selectedFillShapes[_selectedFillShapes.Count - 1]
            : null;
        _selectedShapeOutlineStroke = _selectedShapeOutlineStrokes.Count > 0
            ? _selectedShapeOutlineStrokes[0]
            : null;

        UpdateShapeSelectionAdorner();
    }

    private void ToggleDrawableSelection(DrawableSelectionCandidate candidate)
    {
        if (IsDrawableSelectionSelected(candidate))
        {
            RemoveDrawableSelection(candidate);
        }
        else
        {
            AddDrawableSelection(candidate);
        }
    }





    private Stroke? FindSelectableStrokeAtPoint(Point point)
    {
        for (int i = DrawingCanvas.Strokes.Count - 1; i >= 0; i--)
        {
            Stroke stroke = DrawingCanvas.Strokes[i];
            if (stroke.HitTest(point, GetStrokeHitTolerance(stroke)))
            {
                return stroke;
            }
        }

        return null;
    }




    private WpfShape? FindFillShapeAtPoint(Point point)
    {
        for (int i = DrawingCanvas.Children.Count - 1; i >= 0; i--)
        {
            UIElement child = DrawingCanvas.Children[i];
            if (ReferenceEquals(child, _shapeSelectionAdorner)
                || ReferenceEquals(child, _rangeSelectionAdorner))
            {
                continue;
            }

            if (child is WpfShape shape
                && shape.Fill != null
                && shape.StrokeThickness == 0
                && IsPointInsideVisibleFillShape(shape, point))
            {
                return shape;
            }
        }

        return null;
    }

    private bool IsPointInsideVisibleFillShape(WpfShape shape, Point point)
    {
        Rect bounds = GetShapeBounds(shape);
        if (!bounds.Contains(point))
        {
            return false;
        }

        if (shape is WpfEllipse ellipse && !IsPointNearEllipse(ellipse, point, 0.0))
        {
            return false;
        }

        if (shape.Clip == null)
        {
            return true;
        }

        Point localPoint = ToShapeLocalPoint(shape, point);
        return shape.Clip.FillContains(localPoint);
    }

    private Stroke? FindShapeOutlineStrokeAtPoint(Point point)
    {
        for (int i = DrawingCanvas.Strokes.Count - 1; i >= 0; i--)
        {
            Stroke stroke = DrawingCanvas.Strokes[i];
            ShapeKind? kind = GetKnownOrInferredShapeKind(stroke);
            if (!kind.HasValue)
            {
                continue;
            }

            double tolerance = GetStrokeHitTolerance(stroke);
            if (stroke.HitTest(point, tolerance))
            {
                RegisterShapeOutline(stroke, kind.Value);
                return stroke;
            }
        }

        return null;
    }

    private bool IsShapeOutlineStroke(Stroke stroke)
    {
        return GetKnownOrInferredShapeKind(stroke).HasValue;
    }

    private ShapeKind? GetKnownOrInferredShapeKind(Stroke stroke)
    {
        if (_shapeOutlineKinds.TryGetValue(stroke, out ShapeKind knownKind))
        {
            return knownKind;
        }

        if (TryInferShapeKindFromStroke(stroke, out ShapeKind inferredKind))
        {
            return inferredKind;
        }

        return null;
    }

    private static bool TryInferShapeKindFromStroke(Stroke stroke, out ShapeKind kind)
    {
        kind = ShapeKind.Rectangle;
        StylusPointCollection points = stroke.StylusPoints;
        if (points.Count < 5)
        {
            return false;
        }

        Point first = ToPoint(points[0]);
        Point last = ToPoint(points[points.Count - 1]);
        if (!ArePointsClose(first, last))
        {
            return false;
        }

        if (points.Count == 5 && IsAxisAlignedRectangleStroke(points))
        {
            kind = ShapeKind.Rectangle;
            return true;
        }

        if (points.Count >= 24)
        {
            Rect bounds = GetStrokeBounds(stroke);
            if (bounds.Width >= 1.0 && bounds.Height >= 1.0)
            {
                kind = ShapeKind.Ellipse;
                return true;
            }
        }

        return false;
    }

    private static bool IsAxisAlignedRectangleStroke(StylusPointCollection points)
    {
        if (points.Count != 5)
        {
            return false;
        }

        Point p0 = ToPoint(points[0]);
        Point p1 = ToPoint(points[1]);
        Point p2 = ToPoint(points[2]);
        Point p3 = ToPoint(points[3]);
        Point p4 = ToPoint(points[4]);

        return ArePointsClose(p0, p4)
            && Math.Abs(p0.Y - p1.Y) < 0.1
            && Math.Abs(p1.X - p2.X) < 0.1
            && Math.Abs(p2.Y - p3.Y) < 0.1
            && Math.Abs(p3.X - p0.X) < 0.1;
    }

    private Stroke? FindOutlineStrokeForFillShape(WpfShape fillShape)
    {
        List<Stroke> outlineStrokes = FindOutlineStrokesForFillShape(fillShape);
        return outlineStrokes.Count > 0 ? outlineStrokes[0] : null;
    }

    private List<Stroke> FindOutlineStrokesForFillShape(WpfShape fillShape)
    {
        var outlineStrokes = new List<Stroke>();
        ShapeKind expectedKind = fillShape is WpfEllipse ? ShapeKind.Ellipse : ShapeKind.Rectangle;

        if (_filledShapeOutlineStrokes.TryGetValue(fillShape, out Stroke? primaryStroke)
            && DrawingCanvas.Strokes.Contains(primaryStroke))
        {
            AddStrokeReferenceIfMissing(outlineStrokes, primaryStroke);
        }

        foreach (KeyValuePair<Stroke, WpfShape> pair in _outlineStrokeFilledShapes)
        {
            if (ReferenceEquals(pair.Value, fillShape) && DrawingCanvas.Strokes.Contains(pair.Key))
            {
                AddStrokeReferenceIfMissing(outlineStrokes, pair.Key);
            }
        }

        foreach (Stroke stroke in DrawingCanvas.Strokes)
        {
            if (ContainsStrokeReference(outlineStrokes, stroke))
            {
                continue;
            }

            if (IsStrokeOnFillShapeBoundary(stroke, fillShape, expectedKind))
            {
                RegisterFilledShapeOutlineFragment(fillShape, stroke, expectedKind);
                AddStrokeReferenceIfMissing(outlineStrokes, stroke);
            }
        }

        return outlineStrokes;
    }

    private List<Stroke> FindOutlineStrokesInSameShape(Stroke outlineStroke)
    {
        var outlineStrokes = new List<Stroke>();

        if (!_shapeOutlineGroupIds.TryGetValue(outlineStroke, out int outlineGroupId))
        {
            if (DrawingCanvas.Strokes.Contains(outlineStroke))
            {
                AddStrokeReferenceIfMissing(outlineStrokes, outlineStroke);
            }

            return outlineStrokes;
        }

        foreach (Stroke stroke in DrawingCanvas.Strokes)
        {
            if (_shapeOutlineGroupIds.TryGetValue(stroke, out int currentGroupId)
                && currentGroupId == outlineGroupId)
            {
                AddStrokeReferenceIfMissing(outlineStrokes, stroke);
            }
        }

        if (outlineStrokes.Count == 0 && DrawingCanvas.Strokes.Contains(outlineStroke))
        {
            AddStrokeReferenceIfMissing(outlineStrokes, outlineStroke);
        }

        return outlineStrokes;
    }

    private bool IsStrokeOnFillShapeBoundary(Stroke stroke, WpfShape fillShape, ShapeKind expectedKind)
    {
        if (_shapeOutlineKinds.TryGetValue(stroke, out ShapeKind knownKind) && knownKind != expectedKind)
        {
            return false;
        }

        Rect fillBounds = GetShapeBounds(fillShape);
        Rect strokeBounds = GetStrokeBounds(stroke);
        double tolerance = Math.Max(4.0, GetStrokeHitTolerance(stroke));
        Rect expandedFillBounds = fillBounds;
        expandedFillBounds.Inflate(tolerance, tolerance);

        if (!expandedFillBounds.IntersectsWith(strokeBounds))
        {
            return false;
        }

        StylusPointCollection points = stroke.StylusPoints;
        if (points.Count == 0)
        {
            return false;
        }

        int nearCount = 0;
        foreach (StylusPoint stylusPoint in points)
        {
            if (IsPointNearFillShapeBoundary(fillShape, ToPoint(stylusPoint), tolerance))
            {
                nearCount++;
            }
        }

        int requiredCount = Math.Max(1, (int)Math.Ceiling(points.Count * 0.75));
        return nearCount >= requiredCount;
    }

    private static bool IsPointNearFillShapeBoundary(WpfShape fillShape, Point point, double tolerance)
    {
        Rect bounds = GetShapeBounds(fillShape);
        Rect expandedBounds = bounds;
        expandedBounds.Inflate(tolerance, tolerance);
        if (!expandedBounds.Contains(point))
        {
            return false;
        }

        if (fillShape is WpfEllipse)
        {
            double radiusX = bounds.Width / 2.0;
            double radiusY = bounds.Height / 2.0;
            if (radiusX <= 0.0 || radiusY <= 0.0)
            {
                return false;
            }

            double centerX = bounds.Left + radiusX;
            double centerY = bounds.Top + radiusY;
            double normalizedX = (point.X - centerX) / radiusX;
            double normalizedY = (point.Y - centerY) / radiusY;
            double normalizedDistance = Math.Sqrt((normalizedX * normalizedX) + (normalizedY * normalizedY));
            double normalizedTolerance = tolerance / Math.Max(radiusX, radiusY);
            return Math.Abs(normalizedDistance - 1.0) <= normalizedTolerance;
        }

        return Math.Abs(point.X - bounds.Left) <= tolerance
            || Math.Abs(point.X - bounds.Right) <= tolerance
            || Math.Abs(point.Y - bounds.Top) <= tolerance
            || Math.Abs(point.Y - bounds.Bottom) <= tolerance;
    }

    private static bool AreRectsClose(Rect a, Rect b, double tolerance)
    {
        return Math.Abs(a.Left - b.Left) <= tolerance
            && Math.Abs(a.Top - b.Top) <= tolerance
            && Math.Abs(a.Width - b.Width) <= tolerance * 2.0
            && Math.Abs(a.Height - b.Height) <= tolerance * 2.0;
    }

    private static double GetStrokeHitTolerance(Stroke stroke)
    {
        double width = Math.Max(stroke.DrawingAttributes.Width, stroke.DrawingAttributes.Height);
        return Math.Max(4.0, (width / 2.0) + 4.0);
    }



    private void AddSelectedShapeOutlineStroke(Stroke stroke)
    {
        if (DrawingCanvas.Strokes.Contains(stroke))
        {
            AddStrokeReferenceIfMissing(_selectedShapeOutlineStrokes, stroke);
        }
    }

    private List<WpfShape> GetSelectedFillShapesSnapshot()
    {
        for (int i = _selectedFillShapes.Count - 1; i >= 0; i--)
        {
            if (!DrawingCanvas.Children.Contains(_selectedFillShapes[i]))
            {
                _selectedFillShapes.RemoveAt(i);
            }
        }

        _selectedFillShape = _selectedFillShapes.Count > 0
            ? _selectedFillShapes[_selectedFillShapes.Count - 1]
            : null;

        return new List<WpfShape>(_selectedFillShapes);
    }

    private List<Stroke> GetSelectedShapeOutlineStrokesSnapshot()
    {
        for (int i = _selectedShapeOutlineStrokes.Count - 1; i >= 0; i--)
        {
            if (!DrawingCanvas.Strokes.Contains(_selectedShapeOutlineStrokes[i]))
            {
                _selectedShapeOutlineStrokes.RemoveAt(i);
            }
        }

        _selectedShapeOutlineStroke = _selectedShapeOutlineStrokes.Count > 0
            ? _selectedShapeOutlineStrokes[0]
            : null;

        return new List<Stroke>(_selectedShapeOutlineStrokes);
    }

    private static void AddStrokeReferenceIfMissing(List<Stroke> strokes, Stroke stroke)
    {
        if (!ContainsStrokeReference(strokes, stroke))
        {
            strokes.Add(stroke);
        }
    }

    private static bool ContainsStrokeReference(List<Stroke> strokes, Stroke target)
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

    private static void RemoveStrokeReference(List<Stroke> strokes, Stroke target)
    {
        for (int i = strokes.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(strokes[i], target))
            {
                strokes.RemoveAt(i);
            }
        }
    }

    private static bool ContainsShapeReference(List<WpfShape> shapes, WpfShape target)
    {
        foreach (WpfShape shape in shapes)
        {
            if (ReferenceEquals(shape, target))
            {
                return true;
            }
        }

        return false;
    }

    private static void RemoveShapeReference(List<WpfShape> shapes, WpfShape target)
    {
        for (int i = shapes.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(shapes[i], target))
            {
                shapes.RemoveAt(i);
            }
        }
    }

    private void ClearSelectedShape()
    {
        _selectedFillShape = null;
        _selectedFillShapes.Clear();
        _selectedShapeOutlineStroke = null;
        _selectedShapeOutlineStrokes.Clear();
        _isDraggingSelectedShape = false;
        _hasSelectedShapeDragMoved = false;
        ClearSelectionDragStartState();

        if (_shapeSelectionAdorner != null)
        {
            RemoveCanvasElementIfPresent(_shapeSelectionAdorner);
            _shapeSelectionAdorner = null;
        }
    }

    private void UpdateShapeSelectionAdorner()
    {
        if (!HasSelectedShape())
        {
            if (_shapeSelectionAdorner != null)
            {
                RemoveCanvasElementIfPresent(_shapeSelectionAdorner);
                _shapeSelectionAdorner = null;
            }

            return;
        }

        Rect bounds = GetSelectedShapeBounds();
        if (bounds.IsEmpty)
        {
            if (_shapeSelectionAdorner != null)
            {
                RemoveCanvasElementIfPresent(_shapeSelectionAdorner);
                _shapeSelectionAdorner = null;
            }

            return;
        }

        double margin = Math.Max(4.0, GetSelectedShapeOutlineMaxWidth());
        bounds.Inflate(margin, margin);

        if (_shapeSelectionAdorner == null)
        {
            _shapeSelectionAdorner = new WpfRectangle
            {
                Fill = Brushes.Transparent,
                Stroke = Brushes.DeepSkyBlue,
                StrokeThickness = 1.0,
                StrokeDashArray = new DoubleCollection { 4.0, 3.0 },
                IsHitTestVisible = false
            };
        }

        _shapeSelectionAdorner.Width = Math.Max(1.0, bounds.Width);
        _shapeSelectionAdorner.Height = Math.Max(1.0, bounds.Height);
        InkCanvas.SetLeft(_shapeSelectionAdorner, bounds.Left);
        InkCanvas.SetTop(_shapeSelectionAdorner, bounds.Top);

        if (DrawingCanvas.Children.Contains(_shapeSelectionAdorner))
        {
            DrawingCanvas.Children.Remove(_shapeSelectionAdorner);
        }

        DrawingCanvas.Children.Add(_shapeSelectionAdorner);
    }

    private double GetSelectedShapeOutlineMaxWidth()
    {
        double maxWidth = _currentPenWidth;
        foreach (Stroke stroke in GetSelectedShapeOutlineStrokesSnapshot())
        {
            maxWidth = Math.Max(maxWidth, Math.Max(stroke.DrawingAttributes.Width, stroke.DrawingAttributes.Height));
        }

        return maxWidth;
    }

    private Rect GetSelectedShapeBounds()
    {
        Rect? bounds = null;

        foreach (WpfShape fillShape in GetSelectedFillShapesSnapshot())
        {
            Rect fillBounds = GetShapeBounds(fillShape);
            bounds = bounds.HasValue ? Rect.Union(bounds.Value, fillBounds) : fillBounds;
        }

        foreach (Stroke stroke in GetSelectedShapeOutlineStrokesSnapshot())
        {
            Rect strokeBounds = GetStrokeBounds(stroke);
            bounds = bounds.HasValue ? Rect.Union(bounds.Value, strokeBounds) : strokeBounds;
        }

        return bounds ?? Rect.Empty;
    }

    private bool IsPointOnSelectedShape(Point point)
    {
        foreach (WpfShape fillShape in GetSelectedFillShapesSnapshot())
        {
            if (IsPointInsideVisibleFillShape(fillShape, point))
            {
                return true;
            }
        }

        foreach (Stroke stroke in GetSelectedShapeOutlineStrokesSnapshot())
        {
            if (stroke.HitTest(point, GetStrokeHitTolerance(stroke)))
            {
                return true;
            }
        }

        return false;
    }

    private void BeginSelectedShapeDrag(Point point)
    {
        if (!HasSelectedShape())
        {
            return;
        }

        _isDraggingSelectedShape = true;
        _hasSelectedShapeDragMoved = false;
        _currentInteractionState = InteractionState.MovingShape;
        CaptureSelectionDragStart(point);
        DrawingCanvas.CaptureMouse();
    }

    private void CaptureSelectionDragStart(Point point)
    {
        _shapeDragStartMousePoint = point;
        _shapeDragStartFillBounds.Clear();
        _shapeDragStartOutlineStrokePoints.Clear();
        _selectionDragStartTextPositions.Clear();

        foreach (WpfShape fillShape in GetSelectedFillShapesSnapshot())
        {
            _shapeDragStartFillBounds[fillShape] = GetShapeBounds(fillShape);
        }

        foreach (Stroke stroke in GetSelectedShapeOutlineStrokesSnapshot())
        {
            StylusPointCollection? points = CloneStylusPoints(stroke.StylusPoints);
            if (points != null)
            {
                _shapeDragStartOutlineStrokePoints[stroke] = points;
            }
        }

        foreach (Border textElement in GetSelectedTextElementsSnapshot())
        {
            _selectionDragStartTextPositions[textElement] = GetTextElementPosition(textElement);
        }
    }

    private void ApplySelectionDrag(Point point)
    {
        Vector offset = point - _shapeDragStartMousePoint;
        if (Math.Abs(offset.X) > 0.1 || Math.Abs(offset.Y) > 0.1)
        {
            _hasSelectedShapeDragMoved = true;
        }

        foreach (KeyValuePair<WpfShape, Rect> pair in _shapeDragStartFillBounds)
        {
            if (!DrawingCanvas.Children.Contains(pair.Key))
            {
                continue;
            }

            Rect start = pair.Value;
            SetShapeBounds(
                pair.Key,
                new Rect(start.Left + offset.X, start.Top + offset.Y, start.Width, start.Height));
        }

        foreach (KeyValuePair<Stroke, StylusPointCollection> pair in _shapeDragStartOutlineStrokePoints)
        {
            if (DrawingCanvas.Strokes.Contains(pair.Key))
            {
                SetStrokeStylusPoints(pair.Key, OffsetStylusPoints(pair.Value, offset.X, offset.Y));
            }
        }

        foreach (KeyValuePair<Border, Point> pair in _selectionDragStartTextPositions)
        {
            if (_textElements.Contains(pair.Key))
            {
                SetTextElementPosition(
                    pair.Key,
                    new Point(pair.Value.X + offset.X, pair.Value.Y + offset.Y));
            }
        }

        UpdateShapeSelectionAdorner();
    }

    private List<IUndoableAction> CreateSelectionMoveActions()
    {
        var actions = new List<IUndoableAction>();

        foreach (KeyValuePair<WpfShape, Rect> pair in _shapeDragStartFillBounds)
        {
            if (!DrawingCanvas.Children.Contains(pair.Key))
            {
                continue;
            }

            Rect afterBounds = GetShapeBounds(pair.Key);
            if (!AreRectsClose(pair.Value, afterBounds, 0.1))
            {
                actions.Add(new ShapeMoveAction(
                    pair.Key,
                    pair.Value,
                    afterBounds,
                    Array.Empty<ShapeStrokeMoveEntry>()));
            }
        }

        var strokeMoveEntries = new List<ShapeStrokeMoveEntry>();
        foreach (KeyValuePair<Stroke, StylusPointCollection> pair in _shapeDragStartOutlineStrokePoints)
        {
            if (!DrawingCanvas.Strokes.Contains(pair.Key))
            {
                continue;
            }

            strokeMoveEntries.Add(new ShapeStrokeMoveEntry(
                pair.Key,
                pair.Value,
                CloneStylusPoints(pair.Key.StylusPoints)));
        }

        if (strokeMoveEntries.Count > 0 && _hasSelectedShapeDragMoved)
        {
            actions.Add(new ShapeMoveAction(
                null,
                null,
                null,
                strokeMoveEntries));
        }

        foreach (KeyValuePair<Border, Point> pair in _selectionDragStartTextPositions)
        {
            if (!_textElements.Contains(pair.Key))
            {
                continue;
            }

            Point afterPoint = GetTextElementPosition(pair.Key);
            if (!ArePointsClose(pair.Value, afterPoint))
            {
                actions.Add(new TextMoveAction(pair.Key, pair.Value, afterPoint));
            }
        }

        return actions;
    }

    private void RestoreSelectionDragStart()
    {
        foreach (KeyValuePair<WpfShape, Rect> pair in _shapeDragStartFillBounds)
        {
            if (DrawingCanvas.Children.Contains(pair.Key))
            {
                SetShapeBounds(pair.Key, pair.Value);
            }
        }

        foreach (KeyValuePair<Stroke, StylusPointCollection> pair in _shapeDragStartOutlineStrokePoints)
        {
            if (DrawingCanvas.Strokes.Contains(pair.Key))
            {
                SetStrokeStylusPoints(pair.Key, pair.Value);
            }
        }

        foreach (KeyValuePair<Border, Point> pair in _selectionDragStartTextPositions)
        {
            if (_textElements.Contains(pair.Key))
            {
                SetTextElementPosition(pair.Key, pair.Value);
            }
        }

        UpdateShapeSelectionAdorner();
    }

    private void ClearSelectionDragStartState()
    {
        _shapeDragStartFillBounds.Clear();
        _shapeDragStartOutlineStrokePoints.Clear();
        _selectionDragStartTextPositions.Clear();
    }

    private void PushSelectionMoveHistory(List<IUndoableAction> actions)
    {
        if (actions.Count == 0)
        {
            return;
        }

        PushHistory(actions.Count == 1
            ? actions[0]
            : new CompositeAction(actions));
    }

    private void UpdateSelectedShapeDrag(Point point)
    {
        if (!_isDraggingSelectedShape)
        {
            return;
        }

        ApplySelectionDrag(point);
    }

    private void EndSelectedShapeDrag()
    {
        if (!_isDraggingSelectedShape)
        {
            return;
        }

        List<IUndoableAction> actions = CreateSelectionMoveActions();

        _isDraggingSelectedShape = false;
        _currentInteractionState = InteractionState.None;
        ClearSelectionDragStartState();

        if (DrawingCanvas.IsMouseCaptured)
        {
            DrawingCanvas.ReleaseMouseCapture();
        }

        if (_hasSelectedShapeDragMoved)
        {
            PushSelectionMoveHistory(actions);
        }

        _hasSelectedShapeDragMoved = false;
        UpdateShapeSelectionAdorner();
    }

    private void CancelSelectedShapeDrag()
    {
        if (!_isDraggingSelectedShape)
        {
            return;
        }

        RestoreSelectionDragStart();

        _isDraggingSelectedShape = false;
        _hasSelectedShapeDragMoved = false;
        _currentInteractionState = InteractionState.None;
        ClearSelectionDragStartState();

        if (DrawingCanvas.IsMouseCaptured)
        {
            DrawingCanvas.ReleaseMouseCapture();
        }

        UpdateShapeSelectionAdorner();
    }

    private void ShowSelectedShapeContextMenu()
    {
        if (!HasSelectedShape())
        {
            return;
        }

        var selectItem = new MenuItem
        {
            Header = SR.Select
        };
        selectItem.Click += (_, _) => UpdateShapeSelectionAdorner();

        var applyCurrentStyleItem = new MenuItem
        {
            Header = SR.ApplyCurrentStyle
        };
        applyCurrentStyleItem.Click += (_, _) => ApplyCurrentStyleToSelectedShape();

        var restoreFillItem = new MenuItem
        {
            Header = SR.RestoreFill,
            IsEnabled = CanRestoreSelectedShapeFill()
        };
        restoreFillItem.Click += (_, _) => RestoreSelectedShapeFill();

        var deleteItem = new MenuItem { Header = SR.Delete };
        deleteItem.Click += (_, _) =>
        {
            if (_currentTool == ToolMode.Select)
            {
                DeleteSelectedObjects();
            }
            else
            {
                DeleteSelectedShape();
            }
        };

        var menu = new ContextMenu
        {
            PlacementTarget = DrawingCanvas,
            Placement = PlacementMode.MousePoint
        };
        menu.Items.Add(selectItem);
        menu.Items.Add(applyCurrentStyleItem);
        menu.Items.Add(restoreFillItem);
        menu.Items.Add(deleteItem);
        menu.IsOpen = true;
    }

    private bool ApplyCurrentStyleToSelectedShape()
    {
        List<WpfShape> fillShapes = GetSelectedFillShapesSnapshot();
        List<Stroke> outlineStrokes = GetSelectedShapeOutlineStrokesSnapshot();

        if (fillShapes.Count == 0 && outlineStrokes.Count == 0)
        {
            return false;
        }

        var actions = new List<IUndoableAction>();

        foreach (WpfShape fillShape in fillShapes)
        {
            Brush? beforeFill = fillShape.Fill;
            Brush? afterFill = CreateShapeFillBrush();
            fillShape.Fill = CloneBrush(afterFill);

            actions.Add(new ShapeStyleAction(
                fillShape,
                beforeFill,
                afterFill,
                Array.Empty<ShapeStrokeStyleEntry>()));
        }

        var outlineStyleEntries = new List<ShapeStrokeStyleEntry>();
        foreach (Stroke stroke in outlineStrokes)
        {
            DrawingAttributes? beforeAttributes = stroke.DrawingAttributes.Clone();
            DrawingAttributes afterAttributes = CreatePenAttributes(_currentPenColor, _currentPenWidth);
            LineStyleKind? beforeLineStyle = (stroke as StyledStroke)?.LineStyle;
            LineStyleKind? afterLineStyle = stroke is StyledStroke ? _currentLineStyle : null;

            stroke.DrawingAttributes = afterAttributes.Clone();
            if (stroke is StyledStroke styledStroke)
            {
                styledStroke.LineStyle = _currentLineStyle;
            }

            outlineStyleEntries.Add(new ShapeStrokeStyleEntry(
                stroke,
                beforeAttributes,
                afterAttributes,
                beforeLineStyle,
                afterLineStyle));
        }

        if (outlineStyleEntries.Count > 0)
        {
            actions.Add(new ShapeStyleAction(
                null,
                null,
                null,
                outlineStyleEntries));
        }

        PushHistory(actions.Count == 1
            ? actions[0]
            : new CompositeAction(actions));

        UpdateShapeSelectionAdorner();
        return true;
    }

    private bool CanRestoreSelectedShapeFill()
    {
        foreach (WpfShape fillShape in GetSelectedFillShapesSnapshot())
        {
            if (fillShape.Clip != null)
            {
                return true;
            }
        }

        return false;
    }

    private bool RestoreSelectedShapeFill()
    {
        var actions = new List<IUndoableAction>();

        foreach (WpfShape fillShape in GetSelectedFillShapesSnapshot())
        {
            if (fillShape.Clip == null)
            {
                continue;
            }

            Geometry? beforeClip = CloneGeometry(fillShape.Clip);
            fillShape.Clip = null;
            actions.Add(new ShapeClipChangeAction(fillShape, beforeClip, null));
        }

        if (actions.Count == 0)
        {
            return false;
        }

        PushHistory(actions.Count == 1
            ? actions[0]
            : new CompositeAction(actions));

        UpdateShapeSelectionAdorner();
        return true;
    }


    private bool DeleteSelectedShape()
    {
        List<WpfShape> fillShapes = GetSelectedFillShapesSnapshot();
        List<Stroke> outlineStrokes = GetSelectedShapeOutlineStrokesSnapshot();

        if (fillShapes.Count == 0 && outlineStrokes.Count == 0)
        {
            return false;
        }

        var actions = new List<IUndoableAction>();

        foreach (WpfShape fillShape in fillShapes)
        {
            if (DrawingCanvas.Children.Contains(fillShape))
            {
                actions.Add(new CanvasElementRemoveAction(
                    fillShape,
                    DrawingCanvas.Children.IndexOf(fillShape)));
            }
        }

        if (outlineStrokes.Count > 0)
        {
            actions.Add(new StrokeCollectionAction(
                Array.Empty<Stroke>(),
                outlineStrokes));
        }

        ExecuteWithoutStrokeHistory(() =>
        {
            foreach (WpfShape fillShape in fillShapes)
            {
                RemoveCanvasElementIfPresent(fillShape);
            }

            foreach (Stroke stroke in outlineStrokes)
            {
                RemoveStrokeIfPresent(stroke);
            }
        });

        PushHistory(actions.Count == 1
            ? actions[0]
            : new CompositeAction(actions));

        ClearSelectedShape();
        return true;
    }

    private bool DeleteSelectedObjects()
    {
        List<WpfShape> fillShapes = GetSelectedFillShapesSnapshot();
        List<Stroke> outlineStrokes = GetSelectedShapeOutlineStrokesSnapshot();
        List<Border> textElements = GetSelectedTextElementsSnapshot();

        if (fillShapes.Count == 0 && outlineStrokes.Count == 0 && textElements.Count == 0)
        {
            return false;
        }

        var actions = new List<IUndoableAction>();

        foreach (WpfShape fillShape in fillShapes)
        {
            if (DrawingCanvas.Children.Contains(fillShape))
            {
                actions.Add(new CanvasElementRemoveAction(
                    fillShape,
                    DrawingCanvas.Children.IndexOf(fillShape)));
            }
        }

        if (outlineStrokes.Count > 0)
        {
            actions.Add(new StrokeCollectionAction(
                Array.Empty<Stroke>(),
                outlineStrokes));
        }

        foreach (Border textElement in textElements)
        {
            actions.Add(new TextRemoveAction(
                textElement,
                GetCommittedTextElementIndex(textElement)));
        }

        ClearAllSelectedObjects();

        ExecuteWithoutStrokeHistory(() =>
        {
            foreach (WpfShape fillShape in fillShapes)
            {
                RemoveCanvasElementIfPresent(fillShape);
            }

            foreach (Stroke stroke in outlineStrokes)
            {
                RemoveStrokeIfPresent(stroke);
            }
        });

        foreach (Border textElement in textElements)
        {
            RemoveCommittedTextElement(textElement);
        }

        PushHistory(actions.Count == 1
            ? actions[0]
            : new CompositeAction(actions));

        return true;
    }

    private static Rect GetStrokeBounds(Stroke stroke)
    {
        StylusPointCollection points = stroke.StylusPoints;
        if (points.Count == 0)
        {
            return Rect.Empty;
        }

        double left = points[0].X;
        double right = points[0].X;
        double top = points[0].Y;
        double bottom = points[0].Y;

        for (int i = 1; i < points.Count; i++)
        {
            StylusPoint point = points[i];
            left = Math.Min(left, point.X);
            right = Math.Max(right, point.X);
            top = Math.Min(top, point.Y);
            bottom = Math.Max(bottom, point.Y);
        }

        return new Rect(left, top, Math.Max(0.0, right - left), Math.Max(0.0, bottom - top));
    }

    private static Point ToPoint(StylusPoint point)
    {
        return new Point(point.X, point.Y);
    }

    private static StylusPointCollection? CloneStylusPoints(StylusPointCollection? points)
    {
        if (points == null)
        {
            return null;
        }

        var clone = new StylusPointCollection();
        foreach (StylusPoint point in points)
        {
            clone.Add(new StylusPoint(point.X, point.Y));
        }

        return clone;
    }

    private static StylusPointCollection OffsetStylusPoints(StylusPointCollection points, double offsetX, double offsetY)
    {
        var offsetPoints = new StylusPointCollection();
        foreach (StylusPoint point in points)
        {
            offsetPoints.Add(new StylusPoint(point.X + offsetX, point.Y + offsetY));
        }

        return offsetPoints;
    }

    private static void SetStrokeStylusPoints(Stroke stroke, StylusPointCollection points)
    {
        stroke.StylusPoints = CloneStylusPoints(points) ?? new StylusPointCollection();
    }

    private static void SetShapeBounds(WpfShape shape, Rect bounds)
    {
        shape.Width = Math.Max(0.0, bounds.Width);
        shape.Height = Math.Max(0.0, bounds.Height);
        InkCanvas.SetLeft(shape, bounds.Left);
        InkCanvas.SetTop(shape, bounds.Top);
    }

    private static Brush? CloneBrush(Brush? brush)
    {
        return brush?.CloneCurrentValue();
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
            TextWrapping = TextWrapping.NoWrap,
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
            TextWrapping = TextWrapping.NoWrap
        };

        var host = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(TextPaddingX, TextPaddingY, TextPaddingX, TextPaddingY),
            Child = textBlock,
            Focusable = false,
            IsHitTestVisible = true,
            Cursor = Cursors.SizeAll
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

    private void BeginTextElementDrag(Border host, Point mousePoint)
    {
        if (!IsTextElementSelected(host))
        {
            ClearAllSelectedObjects();
            AddTextElementToSelection(host);
        }

        _draggingTextElement = host;
        _isDraggingTextElement = true;
        _hasSelectedShapeDragMoved = false;
        _currentInteractionState = InteractionState.MovingText;
        CaptureSelectionDragStart(mousePoint);
        host.CaptureMouse();
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

        if (!IsTextElementSelected(host))
        {
            SelectTextElement(host);
            e.Handled = true;
            return;
        }

        BeginTextElementDrag(host, e.GetPosition(DrawingCanvas));
        e.Handled = true;
    }

    private void TextElement_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThroughEnabled
            || (_currentTool != ToolMode.Text && _currentTool != ToolMode.Select))
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

        if (_currentTool == ToolMode.Select)
        {
            if (!IsTextElementSelected(host))
            {
                ClearAllSelectedObjects();
                AddTextElementToSelection(host);
            }
        }
        else
        {
            SelectTextElement(host);
        }

        var menu = new ContextMenu();

        var selectItem = new MenuItem
        {
            Header = SR.Select
        };
        selectItem.Click += (_, _) =>
        {
            if (_currentTool == ToolMode.Select)
            {
                if (!IsTextElementSelected(host))
                {
                    AddTextElementToSelection(host);
                }
            }
            else
            {
                SelectTextElement(host);
            }
        };

        var editItem = new MenuItem
        {
            Header = SR.Edit
        };
        editItem.Click += (_, _) =>
        {
            BeginTextEdit(host);
        };

        var deleteItem = new MenuItem
        {
            Header = SR.Delete
        };
        deleteItem.Click += (_, _) =>
        {
            if (_currentTool == ToolMode.Select)
            {
                DeleteSelectedObjects();
            }
            else if (IsTextElementSelected(host))
            {
                DeleteSelectedTextElement();
            }
        };

        menu.Items.Add(selectItem);
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

        ApplySelectionDrag(e.GetPosition(DrawingCanvas));
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

        CompleteTextElementDrag();
        e.Handled = true;
    }

    private void CompleteTextElementDrag()
    {
        if (!_isDraggingTextElement)
        {
            return;
        }

        Border? capturedHost = _draggingTextElement;
        List<IUndoableAction> actions = CreateSelectionMoveActions();
        bool hasMoved = _hasSelectedShapeDragMoved;

        _draggingTextElement = null;
        _isDraggingTextElement = false;
        _hasSelectedShapeDragMoved = false;

        if (_currentInteractionState == InteractionState.MovingText)
        {
            _currentInteractionState = InteractionState.None;
        }

        ClearSelectionDragStartState();

        if (capturedHost != null && capturedHost.IsMouseCaptured)
        {
            capturedHost.ReleaseMouseCapture();
        }

        if (hasMoved)
        {
            PushSelectionMoveHistory(actions);
        }

        UpdateShapeSelectionAdorner();
    }

    private void TextElement_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isDraggingTextElement
            && sender is Border host
            && ReferenceEquals(host, _draggingTextElement))
        {
            CancelTextElementDrag();
        }
    }

    private void CancelTextElementDrag()
    {
        if (!_isDraggingTextElement)
        {
            return;
        }

        RestoreSelectionDragStart();
        EndTextElementDrag();
    }

    private void EndTextElementDrag()
    {
        Border? capturedHost = _draggingTextElement;

        _draggingTextElement = null;
        _isDraggingTextElement = false;
        _hasSelectedShapeDragMoved = false;

        if (_currentInteractionState == InteractionState.MovingText)
        {
            _currentInteractionState = InteractionState.None;
        }

        ClearSelectionDragStartState();

        if (capturedHost != null && capturedHost.IsMouseCaptured)
        {
            capturedHost.ReleaseMouseCapture();
        }
    }

    private void BeginTextEdit(Border host)
    {
        CancelActiveTextInput();
        ClearAllSelectedObjects();
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
        ClearAllSelectedObjects();
        AddTextElementToSelection(host);
    }

    private bool IsTextElementSelected(Border host)
    {
        foreach (Border selected in _selectedTextElements)
        {
            if (ReferenceEquals(selected, host))
            {
                return true;
            }
        }

        return false;
    }

    private void AddTextElementToSelection(Border host)
    {
        if (!_textElements.Contains(host))
        {
            return;
        }

        if (!IsTextElementSelected(host))
        {
            _selectedTextElements.Add(host);
        }

        _selectedTextElement = host;
        UpdateSelectedTextVisual(host, true);
    }

    private void RemoveTextElementFromSelection(Border host)
    {
        for (int i = _selectedTextElements.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_selectedTextElements[i], host))
            {
                _selectedTextElements.RemoveAt(i);
            }
        }

        UpdateSelectedTextVisual(host, false);

        _selectedTextElement = _selectedTextElements.Count > 0
            ? _selectedTextElements[_selectedTextElements.Count - 1]
            : null;
    }

    private void ToggleTextElementSelection(Border host)
    {
        if (IsTextElementSelected(host))
        {
            RemoveTextElementFromSelection(host);
        }
        else
        {
            AddTextElementToSelection(host);
        }
    }

    private List<Border> GetSelectedTextElementsSnapshot()
    {
        for (int i = _selectedTextElements.Count - 1; i >= 0; i--)
        {
            if (!_textElements.Contains(_selectedTextElements[i]))
            {
                _selectedTextElements.RemoveAt(i);
            }
        }

        _selectedTextElement = _selectedTextElements.Count > 0
            ? _selectedTextElements[_selectedTextElements.Count - 1]
            : null;

        return new List<Border>(_selectedTextElements);
    }

    private void ClearSelectedTextElement()
    {
        foreach (Border host in new List<Border>(_selectedTextElements))
        {
            UpdateSelectedTextVisual(host, false);
        }

        _selectedTextElements.Clear();
        _selectedTextElement = null;
    }

    private bool DeleteSelectedTextElement()
    {
        List<Border> targets = GetSelectedTextElementsSnapshot();
        if (targets.Count == 0)
        {
            return false;
        }

        var actions = new List<IUndoableAction>();
        foreach (Border target in targets)
        {
            actions.Add(new TextRemoveAction(
                target,
                GetCommittedTextElementIndex(target)));
        }

        ClearSelectedTextElement();

        foreach (Border target in targets)
        {
            RemoveCommittedTextElement(target);
        }

        PushHistory(actions.Count == 1
            ? actions[0]
            : new CompositeAction(actions));

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
        if (ReferenceEquals(_draggingTextElement, host))
        {
            EndTextElementDrag();
        }

        if (IsTextElementSelected(host))
        {
            RemoveTextElementFromSelection(host);
        }

        DrawingCanvas.Children.Remove(host);
        _textElements.Remove(host);
    }

    private int GetCanvasElementInsertIndex()
    {
        for (int i = 0; i < DrawingCanvas.Children.Count; i++)
        {
            UIElement child = DrawingCanvas.Children[i];

            if (child is TextBox)
            {
                return i;
            }

            if (child is Border border && _textElements.Contains(border))
            {
                return i;
            }
        }

        return DrawingCanvas.Children.Count;
    }

    private void AddCanvasElement(UIElement element)
    {
        AddCanvasElement(element, GetCanvasElementInsertIndex());
    }

    private void AddCanvasElement(UIElement element, int index)
    {
        if (DrawingCanvas.Children.Contains(element))
        {
            return;
        }

        int normalizedIndex = index;
        if (normalizedIndex < 0)
        {
            normalizedIndex = 0;
        }

        if (normalizedIndex > DrawingCanvas.Children.Count)
        {
            normalizedIndex = DrawingCanvas.Children.Count;
        }

        DrawingCanvas.Children.Insert(normalizedIndex, element);
    }

    private void RemoveCanvasElementIfPresent(UIElement element)
    {
        if (DrawingCanvas.Children.Contains(element))
        {
            DrawingCanvas.Children.Remove(element);
        }
    }

    private List<ClearCanvasElementEntry> GetCommittedCanvasElementEntriesSnapshot()
    {
        var result = new List<ClearCanvasElementEntry>();

        for (int i = 0; i < DrawingCanvas.Children.Count; i++)
        {
            UIElement child = DrawingCanvas.Children[i];

            if (child is TextBox)
            {
                continue;
            }

            if (ReferenceEquals(child, _shapeSelectionAdorner)
                || ReferenceEquals(child, _rangeSelectionAdorner))
            {
                continue;
            }

            if (child is Border border && _textElements.Contains(border))
            {
                continue;
            }

            result.Add(new ClearCanvasElementEntry(child, i));
        }

        return result;
    }

    private void RemoveCommittedCanvasElements()
    {
        foreach (ClearCanvasElementEntry entry in GetCommittedCanvasElementEntriesSnapshot())
        {
            RemoveCanvasElementIfPresent(entry.Element);
        }
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

    private static Rect GetTextElementBounds(Border host)
    {
        Point position = GetTextElementPosition(host);
        double width = host.ActualWidth > 0.0 ? host.ActualWidth : host.RenderSize.Width;
        double height = host.ActualHeight > 0.0 ? host.ActualHeight : host.RenderSize.Height;

        if (width <= 0.0)
        {
            width = Math.Max(1.0, host.DesiredSize.Width);
        }

        if (height <= 0.0)
        {
            height = Math.Max(1.0, host.DesiredSize.Height);
        }

        return new Rect(position.X, position.Y, width, height);
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
        DrawingCanvas.SyncDynamicRendererDrawingAttributes();
        UpdatePenPresetButtonHighlight();
        UpdatePenWidthPresetButtonHighlight();
        SaveAppSettings();
    }

    private void SelectEraserWidth(double width)
    {
        _currentEraserWidth = NormalizePenWidth(width);
        ApplyEraserWidthToCanvas();
        UpdateEraserWidthPresetButtonHighlight();
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
            ToolMode.Select => SelectButton,
            ToolMode.Rectangle => RectangleButton,
            ToolMode.Circle => CircleButton,
            ToolMode.Text => TextButton,
            ToolMode.Eraser => EraserButton,
            _ => PenButton
        };

        SetButtonSelected(selectedButton, PenButton, SelectButton, RectangleButton, CircleButton, TextButton, EraserButton);
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
                ToolMode.Select => Cursors.Arrow,
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

        e.Handled = true;
        ActivatePenTool();
    }

    private void PenButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThroughEnabled)
        {
            return;
        }

        e.Handled = true;

        ActivatePenTool();
        OpenPenPresetPopup();
    }

    private void SelectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isClickThroughEnabled)
        {
            return;
        }

        CloseToolbarPopupsAndPendingClicks();
        FinalizeOrCancelCurrentOperation();
        CommitActiveTextInput();
        ClearSelectedTextElement();

        _isStraightLineDrawing = false;
        _isRectangleDrawing = false;
        _isCircleDrawing = false;

        _currentTool = ToolMode.Select;
        DrawingCanvas.EditingMode = InkCanvasEditingMode.None;
        DrawingCanvas.Focus();

        UpdateToolHighlight();
        UpdateCursor();
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
        int rectangleFillTransparencyPercent = NormalizeRectangleFillOpacity((int)Math.Round(e.NewValue));
        _rectangleFillOpacityPercent = NormalizeRectangleFillOpacity(100 - rectangleFillTransparencyPercent);
        UpdateShapeButtonToolTips();
        UpdateRectangleSettingsUi();
        SaveAppSettings();
    }

    private void RectangleButton_Click(object sender, RoutedEventArgs e)
    {
        PenPresetPopup.IsOpen = false;
        EraserWidthPopup.IsOpen = false;
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
        PenPresetPopup.IsOpen = false;
        EraserWidthPopup.IsOpen = false;
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
        PenPresetPopup.IsOpen = false;
        EraserWidthPopup.IsOpen = false;
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

    private void TextButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThroughEnabled)
        {
            return;
        }

        e.Handled = true;

        CloseToolbarPopupsAndPendingClicks();

        FinalizeOrCancelCurrentOperation();
        ClearSelectedTextElement();
        ClearSelectedShape();

        _isStraightLineDrawing = false;
        _isRectangleDrawing = false;
        _isCircleDrawing = false;

        _currentTool = ToolMode.Text;
        DrawingCanvas.EditingMode = InkCanvasEditingMode.None;
        UpdateToolHighlight();
        UpdateCursor();

        ShowTextButtonContextMenu();
    }

    private void ActivateEraserTool()
    {
        FinalizeOrCancelCurrentOperation();
        ClearSelectedTextElement();

        _isStraightLineDrawing = false;
        _isRectangleDrawing = false;
        _isCircleDrawing = false;

        _currentTool = ToolMode.Eraser;
        DrawingCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
        ApplyEraserWidthToCanvas();
        UpdateToolHighlight();
        UpdateCursor();
    }

    private void EraserButton_Click(object sender, RoutedEventArgs e)
    {
        PenPresetPopup.IsOpen = false;
        EraserWidthPopup.IsOpen = false;
        ActivateEraserTool();
    }

    private void EraserButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThroughEnabled)
        {
            return;
        }

        e.Handled = true;

        CloseToolbarPopupsAndPendingClicks();
        ActivateEraserTool();
        OpenEraserWidthPresetPopup();
    }

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isClickThroughEnabled)
        {
            return;
        }

        e.Handled = true;

        PenPresetPopup.IsOpen = false;
        EraserWidthPopup.IsOpen = false;
        RectangleSettingsPopup.IsOpen = false;
        HotkeySettingsPopup.IsOpen = false;

        OpenColorPopup();
    }

    private void ColorButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThroughEnabled)
        {
            return;
        }

        e.Handled = true;

        CloseToolbarPopupsAndPendingClicks();
        ShowColorButtonContextMenu();
    }

    private void OpenColorPopup()
    {
        OpenPopupDeferred(ColorPopup, () =>
        {
            PenPresetPopup.IsOpen = false;
            EraserWidthPopup.IsOpen = false;
            RectangleSettingsPopup.IsOpen = false;
            HotkeySettingsPopup.IsOpen = false;
            BuildRecentColorButtons();
            BuildPresetColorButtons();
        });
    }

    private void ShowTextButtonContextMenu()
    {
        var fontSettingsItem = new MenuItem
        {
            Header = SR.FontSettings
        };
        fontSettingsItem.Click += (_, _) => ShowFontDialog();

        ShowToolbarContextMenu(TextButton, fontSettingsItem);
    }

    private void ShowColorButtonContextMenu()
    {
        var colorSettingsItem = new MenuItem
        {
            Header = SR.ColorSettings
        };
        colorSettingsItem.Click += (_, _) => OpenCurrentColorEditor();

        ShowToolbarContextMenu(ColorButton, colorSettingsItem);
    }

    private void ShowToolbarContextMenu(Button placementTarget, params MenuItem[] menuItems)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = placementTarget,
            Placement = PlacementMode.MousePoint
        };

        foreach (MenuItem menuItem in menuItems)
        {
            menu.Items.Add(menuItem);
        }

        menu.IsOpen = true;
    }

    private void CloseToolbarPopupsAndPendingClicks()
    {
        ColorPopup.IsOpen = false;
        PenPresetPopup.IsOpen = false;
        LineStylePopup.IsOpen = false;
        EraserWidthPopup.IsOpen = false;
        RectangleSettingsPopup.IsOpen = false;
        HotkeySettingsPopup.IsOpen = false;
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
        ClearSelectedTextElement();
        ClearSelectedShape();

        List<Stroke> removedStrokes = ToStrokeList(DrawingCanvas.Strokes);
        List<ClearCanvasElementEntry> removedCanvasElements = GetCommittedCanvasElementEntriesSnapshot();
        List<ClearTextEntry> removedTextEntries = GetCommittedTextEntriesSnapshot();

        if (removedStrokes.Count == 0 && removedCanvasElements.Count == 0 && removedTextEntries.Count == 0)
        {
            return;
        }

        ExecuteWithoutStrokeHistory(() => DrawingCanvas.Strokes.Clear());
        RemoveCommittedCanvasElements();
        RemoveCommittedTextElements();

        PushHistory(new ClearAction(removedStrokes, removedCanvasElements, removedTextEntries));
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
        ClearSelectedShape();

        _isStraightLineDrawing = false;
        _isRectangleDrawing = false;
        _isCircleDrawing = false;

        _isClickThroughEnabled = enabled;

        ColorPopup.IsOpen = false;
        PenPresetPopup.IsOpen = false;
        LineStylePopup.IsOpen = false;
        EraserWidthPopup.IsOpen = false;
        RectangleSettingsPopup.IsOpen = false;

        UpdateToolbarForCT();
        UpdateClickThroughButtonLabel();

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
            ApplyEraserWidthToCanvas();
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

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
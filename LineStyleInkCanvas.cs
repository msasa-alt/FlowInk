using System;
using System.Windows.Controls;
using System.Windows.Ink;

namespace FlowInk;

public sealed class LineStyleStrokeCollectedEventArgs : EventArgs
{
    public LineStyleStrokeCollectedEventArgs(StyledStroke stroke)
    {
        Stroke = stroke;
    }

    public StyledStroke Stroke { get; }
}

public sealed class LineStyleInkCanvas : InkCanvas
{
    private readonly StyledDynamicRenderer _styledDynamicRenderer = new();
    private LineStyleKind _currentLineStyle = LineStyleKind.Solid;

    public LineStyleInkCanvas()
    {
        DynamicRenderer = _styledDynamicRenderer;
        _styledDynamicRenderer.UpdateDrawingAttributes(DefaultDrawingAttributes);
    }

    public event EventHandler<LineStyleStrokeCollectedEventArgs>? LineStyleStrokeCollected;

    public bool IsReplacingCollectedStroke { get; private set; }

    public LineStyleKind CurrentLineStyle
    {
        get => _currentLineStyle;
        set
        {
            _currentLineStyle = value;
            _styledDynamicRenderer.LineStyle = value;
        }
    }

    public void SyncDynamicRendererDrawingAttributes()
    {
        _styledDynamicRenderer.UpdateDrawingAttributes(DefaultDrawingAttributes);
    }

    protected override void OnStrokeCollected(InkCanvasStrokeCollectedEventArgs e)
    {
        if (_currentLineStyle == LineStyleKind.Solid)
        {
            base.OnStrokeCollected(e);
            return;
        }

        var styledStroke = new StyledStroke(
            e.Stroke.StylusPoints.Clone(),
            e.Stroke.DrawingAttributes.Clone(),
            _currentLineStyle);

        IsReplacingCollectedStroke = true;
        try
        {
            Strokes.Remove(e.Stroke);
            Strokes.Add(styledStroke);
        }
        finally
        {
            IsReplacingCollectedStroke = false;
        }

        LineStyleStrokeCollected?.Invoke(
            this,
            new LineStyleStrokeCollectedEventArgs(styledStroke));

        base.OnStrokeCollected(new InkCanvasStrokeCollectedEventArgs(styledStroke));
    }
}

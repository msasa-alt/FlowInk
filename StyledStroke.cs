using System;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace FlowInk;

public sealed class StyledStroke : Stroke
{
    private LineStyleKind _lineStyle;

    public StyledStroke(
        StylusPointCollection stylusPoints,
        DrawingAttributes drawingAttributes,
        LineStyleKind lineStyle)
        : base(stylusPoints, drawingAttributes)
    {
        _lineStyle = lineStyle;
    }

    public LineStyleKind LineStyle
    {
        get => _lineStyle;
        set
        {
            if (_lineStyle == value)
            {
                return;
            }

            _lineStyle = value;
            OnInvalidated(EventArgs.Empty);
        }
    }

    protected override void DrawCore(
        DrawingContext drawingContext,
        DrawingAttributes drawingAttributes)
    {
        if (_lineStyle == LineStyleKind.Solid || StylusPoints.Count == 0)
        {
            base.DrawCore(drawingContext, drawingAttributes);
            return;
        }

        Brush brush = new SolidColorBrush(drawingAttributes.Color);
        double thickness = Math.Max(0.1, (drawingAttributes.Width + drawingAttributes.Height) / 2.0);

        if (StylusPoints.Count == 1)
        {
            Point point = (Point)StylusPoints[0];
            drawingContext.DrawEllipse(brush, null, point, thickness / 2.0, thickness / 2.0);
            return;
        }

        StreamGeometry geometry = CreatePolylineGeometry(StylusPoints);

        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            DashCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
            DashStyle = LineStyleDrawing.CreateDashStyle(_lineStyle)
        };

        drawingContext.DrawGeometry(null, pen, geometry);
    }

    internal static StreamGeometry CreatePolylineGeometry(StylusPointCollection stylusPoints)
    {
        var geometry = new StreamGeometry();

        using (StreamGeometryContext context = geometry.Open())
        {
            Point first = (Point)stylusPoints[0];
            context.BeginFigure(first, isFilled: false, isClosed: false);

            for (int i = 1; i < stylusPoints.Count; i++)
            {
                context.LineTo((Point)stylusPoints[i], isStroked: true, isSmoothJoin: true);
            }
        }

        geometry.Freeze();
        return geometry;
    }
}

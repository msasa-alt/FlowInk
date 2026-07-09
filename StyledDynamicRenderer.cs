using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Input.StylusPlugIns;
using System.Windows.Media;

namespace FlowInk;

public sealed class StyledDynamicRenderer : DynamicRenderer
{
    [ThreadStatic]
    private static bool _hasPreviousPoint;

    [ThreadStatic]
    private static Point _previousPoint;

    private volatile LineStyleKind _lineStyle = LineStyleKind.Solid;
    private int _argb = unchecked((int)0xFFFF0000);
    private double _width = 4.0;
    private double _height = 4.0;

    public LineStyleKind LineStyle
    {
        get => _lineStyle;
        set => _lineStyle = value;
    }

    public void UpdateDrawingAttributes(DrawingAttributes drawingAttributes)
    {
        ArgumentNullException.ThrowIfNull(drawingAttributes);

        // DynamicRenderer internally also needs its own attributes.
        DrawingAttributes = drawingAttributes.Clone();

        Color color = drawingAttributes.Color;
        int argb = color.A << 24 | color.R << 16 | color.G << 8 | color.B;
        Volatile.Write(ref _argb, argb);
        Volatile.Write(ref _width, drawingAttributes.Width);
        Volatile.Write(ref _height, drawingAttributes.Height);
    }

    protected override void OnStylusDown(RawStylusInput rawStylusInput)
    {
        _hasPreviousPoint = false;
        base.OnStylusDown(rawStylusInput);
    }

    protected override void OnStylusUp(RawStylusInput rawStylusInput)
    {
        base.OnStylusUp(rawStylusInput);
        _hasPreviousPoint = false;
    }

    protected override void OnDraw(
        DrawingContext drawingContext,
        StylusPointCollection stylusPoints,
        Geometry geometry,
        Brush fillBrush)
    {
        LineStyleKind lineStyle = LineStyle;
        if (lineStyle == LineStyleKind.Solid)
        {
            drawingContext.DrawGeometry(fillBrush, null, geometry);
            return;
        }

        if (stylusPoints.Count == 0)
        {
            return;
        }

        var points = new List<Point>(stylusPoints.Count + 1);
        if (_hasPreviousPoint)
        {
            points.Add(_previousPoint);
        }

        foreach (StylusPoint point in stylusPoints)
        {
            points.Add(new Point(point.X, point.Y));
        }

        int argb = Volatile.Read(ref _argb);
        Color color = Color.FromArgb(
            (byte)((argb >> 24) & 0xFF),
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF));

        double width = Volatile.Read(ref _width);
        double height = Volatile.Read(ref _height);
        double thickness = Math.Max(0.1, (width + height) / 2.0);

        var brush = new SolidColorBrush(color);
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        if (points.Count == 1)
        {
            Point point = points[0];
            drawingContext.DrawEllipse(brush, null, point, thickness / 2.0, thickness / 2.0);
            _previousPoint = point;
            _hasPreviousPoint = true;
            return;
        }

        DashStyle? dashStyle = LineStyleDrawing.CreateDashStyle(lineStyle);
        if (dashStyle == null)
        {
            drawingContext.DrawGeometry(fillBrush, null, geometry);
            return;
        }

        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            DashCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
            DashStyle = dashStyle
        };

        if (pen.CanFreeze)
        {
            pen.Freeze();
        }

        drawingContext.DrawGeometry(null, pen, CreatePolylineGeometry(points));

        StylusPoint lastPoint = stylusPoints[stylusPoints.Count - 1];
        _previousPoint = new Point(lastPoint.X, lastPoint.Y);
        _hasPreviousPoint = true;
    }

    private static Geometry CreatePolylineGeometry(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();

        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(points[0], isFilled: false, isClosed: false);

            for (int index = 1; index < points.Count; index++)
            {
                context.LineTo(points[index], isStroked: true, isSmoothJoin: false);
            }
        }

        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }

        return geometry;
    }
}

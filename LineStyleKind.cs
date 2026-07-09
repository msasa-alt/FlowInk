using System.Windows.Media;

namespace FlowInk;

public enum LineStyleKind
{
    Solid,
    Dotted,
    Dashed,
    DashDot
}

internal static class LineStyleDrawing
{
    public static DashStyle? CreateDashStyle(LineStyleKind lineStyle)
    {
        return lineStyle switch
        {
            LineStyleKind.Dotted => DashStyles.Dot,
            LineStyleKind.Dashed => DashStyles.Dash,
            LineStyleKind.DashDot => DashStyles.DashDot,
            _ => null
        };
    }
}

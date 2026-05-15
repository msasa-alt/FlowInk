using System;
using System.Windows;
using System.Windows.Media;
using SR = FlowInk.Properties.Resources;

namespace FlowInk;

public partial class PenPresetDialog : Window
{
    private bool _isUpdating;
    private Color _selectedBaseColor;

    public Color SelectedColor { get; private set; }
    public double SelectedWidth { get; private set; }
    public int SelectedOpacityPercent { get; private set; }
    public int[] CustomColors { get; private set; }

    public PenPresetDialog(Color initialColor, double initialWidth, int initialOpacityPercent, int[] customColors)
    {
        InitializeComponent();

        _selectedBaseColor = Color.FromArgb(255, initialColor.R, initialColor.G, initialColor.B);
        SelectedWidth = NormalizeWidth(initialWidth);
        SelectedOpacityPercent = NormalizeOpacity(initialOpacityPercent);
        CustomColors = customColors ?? Array.Empty<int>();

        if (initialColor.A != 255 && initialOpacityPercent == 100)
        {
            SelectedOpacityPercent = GetOpacityPercent(initialColor);
        }

        SelectedColor = CreateColorWithOpacity(_selectedBaseColor, SelectedOpacityPercent);

        _isUpdating = true;
        try
        {
            WidthSlider.Value = SelectedWidth;
            OpacitySlider.Value = SelectedOpacityPercent;
        }
        finally
        {
            _isUpdating = false;
        }

        UpdatePreview();
    }

    private static double NormalizeWidth(double width)
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

    private static int NormalizeOpacity(int opacityPercent)
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
        return NormalizeOpacity((int)Math.Round(color.A * 100.0 / 255.0));
    }

    private static Color CreateColorWithOpacity(Color color, int opacityPercent)
    {
        byte alpha = (byte)Math.Round(255.0 * NormalizeOpacity(opacityPercent) / 100.0);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private void WidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || !IsLoaded)
        {
            return;
        }

        SelectedWidth = NormalizeWidth(WidthSlider.Value);
        UpdatePreview();
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || !IsLoaded)
        {
            return;
        }

        SelectedOpacityPercent = NormalizeOpacity((int)Math.Round(OpacitySlider.Value));
        UpdatePreview();
    }

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ColorPickerDialog(CreateColorWithOpacity(_selectedBaseColor, SelectedOpacityPercent), CustomColors)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        CustomColors = dialog.CustomColors;
        _selectedBaseColor = Color.FromArgb(255, dialog.SelectedColor.R, dialog.SelectedColor.G, dialog.SelectedColor.B);
        SelectedOpacityPercent = GetOpacityPercent(dialog.SelectedColor);

        _isUpdating = true;
        try
        {
            OpacitySlider.Value = SelectedOpacityPercent;
        }
        finally
        {
            _isUpdating = false;
        }

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        SelectedWidth = NormalizeWidth(SelectedWidth);
        SelectedOpacityPercent = NormalizeOpacity(SelectedOpacityPercent);
        SelectedColor = CreateColorWithOpacity(_selectedBaseColor, SelectedOpacityPercent);

        WidthValueTextBlock.Text = string.Format(SR.CurrentValueFormat, SelectedWidth.ToString("0.#"));
        OpacityValueTextBlock.Text = string.Format(SR.OpacityValueFormat, SelectedOpacityPercent);

        ColorPreviewRectangle.Fill = new SolidColorBrush(SelectedColor);
        PreviewLine.Stroke = new SolidColorBrush(SelectedColor);
        PreviewLine.StrokeThickness = SelectedWidth;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedWidth = NormalizeWidth(WidthSlider.Value);
        SelectedOpacityPercent = NormalizeOpacity((int)Math.Round(OpacitySlider.Value));
        SelectedColor = CreateColorWithOpacity(_selectedBaseColor, SelectedOpacityPercent);
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

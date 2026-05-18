using System;
using System.Windows;
using System.Windows.Media;
using SR = FlowInk.Properties.Resources;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace FlowInk;

public partial class ColorPickerDialog : Window
{
    private Color _selectedColor;
    private readonly int[] _customColors;

    public Color SelectedColor => _selectedColor;
    public int[] CustomColors => _customColors;

    public ColorPickerDialog(Color initialColor, int[]? customColors = null)
    {
        InitializeComponent();

        _selectedColor = initialColor;
        _customColors = customColors != null ? (int[])customColors.Clone() : new int[16];

        AlphaSlider.Value = GetTransparencyPercent(initialColor);
        UpdatePreview();
    }

    private void ChooseBaseColorButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.ColorDialog
        {
            FullOpen = true,
            AllowFullOpen = true,
            AnyColor = true,
            SolidColorOnly = false,
            Color = Drawing.Color.FromArgb(
                255,
                _selectedColor.R,
                _selectedColor.G,
                _selectedColor.B),
            CustomColors = (int[])_customColors.Clone()
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        Array.Copy(dialog.CustomColors, _customColors, Math.Min(dialog.CustomColors.Length, _customColors.Length));

        _selectedColor = Color.FromArgb(
            _selectedColor.A,
            dialog.Color.R,
            dialog.Color.G,
            dialog.Color.B);

        UpdatePreview();
    }

    private void AlphaSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _selectedColor = Color.FromArgb(
            GetAlphaFromTransparencySlider(),
            _selectedColor.R,
            _selectedColor.G,
            _selectedColor.B);

        UpdatePreview();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedColor = Color.FromArgb(
            GetAlphaFromTransparencySlider(),
            _selectedColor.R,
            _selectedColor.G,
            _selectedColor.B);

        DialogResult = true;
    }

    private void UpdatePreview()
    {
        PreviewBrush.Color = _selectedColor;
        ColorValueTextBlock.Text = $"#{_selectedColor.A:X2}{_selectedColor.R:X2}{_selectedColor.G:X2}{_selectedColor.B:X2}";

        AlphaValueTextBlock.Text = string.Format(SR.TransparencyFormat, GetTransparencyPercent(_selectedColor));
    }

    private static int GetTransparencyPercent(Color color)
    {
        int opacityPercent = (int)Math.Round(color.A * 100.0 / 255.0);
        return 100 - Math.Clamp(opacityPercent, 0, 100);
    }

    private byte GetAlphaFromTransparencySlider()
    {
        int transparencyPercent = Math.Clamp((int)Math.Round(AlphaSlider.Value), 0, 100);
        int opacityPercent = 100 - transparencyPercent;
        return (byte)Math.Round(255.0 * opacityPercent / 100.0);
    }
}
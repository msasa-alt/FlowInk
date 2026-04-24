using System;
using System.Windows;
using System.Windows.Media;
using SR = FlowInk.Properties.Resources;

namespace FlowInk;

public partial class PenWidthDialog : Window
{
    public double SelectedWidth { get; private set; }

    public PenWidthDialog(double initialWidth, Color previewColor)
    {
        InitializeComponent();

        SelectedWidth = NormalizeWidth(initialWidth);

        WidthSlider.Value = SelectedWidth;
        PreviewLine.Stroke = new SolidColorBrush(previewColor);

        UpdatePreviewText();
        UpdatePreviewLine();
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

    private void WidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
        {
            return;
        }

        SelectedWidth = Math.Round(WidthSlider.Value * 2) / 2.0;
        UpdatePreviewText();
        UpdatePreviewLine();
    }

    private void UpdatePreviewText()
    {
        PreviewTextBlock.Text = string.Format(SR.CurrentValueFormat, SelectedWidth.ToString("0.#"));
    }

    private void UpdatePreviewLine()
    {
        PreviewLine.StrokeThickness = SelectedWidth;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedWidth = Math.Round(WidthSlider.Value * 2) / 2.0;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

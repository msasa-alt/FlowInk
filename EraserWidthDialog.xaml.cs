using System;
using System.Windows;
using SR = FlowInk.Properties.Resources;

namespace FlowInk;

public partial class EraserWidthDialog : Window
{
    public double SelectedWidth { get; private set; }

    public EraserWidthDialog(double initialWidth)
    {
        InitializeComponent();

        SelectedWidth = NormalizeWidth(initialWidth);

        WidthSlider.Value = SelectedWidth;

        UpdatePreviewText();
        UpdatePreviewLine();
    }

    private static double NormalizeWidth(double width)
    {
        if (double.IsNaN(width) || double.IsInfinity(width))
        {
            return 4.0;
        }

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

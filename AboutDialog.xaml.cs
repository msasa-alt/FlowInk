using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace FlowInk;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();

        string versionText = "1.0.0";

        Assembly? assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        if (assembly != null)
        {
            System.Version? version = assembly.GetName().Version;
            if (version != null)
            {
                versionText = $"{version.Major}.{version.Minor}.{version.Build}";
            }
        }

        VersionTextBlock.Text = $"Version: {versionText}";
    }

    private void GitHubHyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            };

            System.Diagnostics.Process.Start(psi);
        }
        catch
        {
            MessageBox.Show(
                "ブラウザを開けませんでした。",
                "FlowInk",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        e.Handled = true;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}

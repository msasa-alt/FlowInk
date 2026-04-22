using System.Reflection;
using System.Windows;
using SR = FlowInk.Properties.Resources;

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

        VersionTextBlock.Text = string.Format(SR.VersionFormat, versionText);
    }
}

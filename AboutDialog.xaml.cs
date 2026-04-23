using System.Reflection;
using System.Windows;
using SR = FlowInk.Properties.Resources;

namespace FlowInk;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();

        Assembly? assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        string versionText = GetDisplayVersion(assembly);

        VersionTextBlock.Text = string.Format(SR.VersionFormat, versionText);
    }

    private static string GetDisplayVersion(Assembly? assembly)
    {
        if (assembly == null)
        {
            return "Unknown";
        }

        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        System.Version? version = assembly.GetName().Version;
        if (version != null)
        {
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }

        return "Unknown";
    }
}

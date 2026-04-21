using System.Windows;
#if false // trueにすると英語表記
using System.Windows.Controls;
using System.Globalization;
using System.Threading;
#endif


namespace FlowInk;

public partial class App : Application
{
#if false // trueにすると英語表記
    protected override void OnStartup(StartupEventArgs e)
    {
        Thread.CurrentThread.CurrentUICulture = new CultureInfo("en");
        base.OnStartup(e);
    }
#endif
}

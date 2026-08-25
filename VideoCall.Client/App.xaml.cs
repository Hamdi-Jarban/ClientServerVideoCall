using System.Globalization;
using System.Windows;

namespace VideoCall.Client;

/// <summary>
/// Application entry point. Forces Arabic UI culture and RTL flow so
/// every window opens right-to-left by default without each window
/// needing to set it individually.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var arabicCulture = new CultureInfo("ar-SA");
        CultureInfo.DefaultThreadCurrentCulture = arabicCulture;
        CultureInfo.DefaultThreadCurrentUICulture = arabicCulture;
        Thread.CurrentThread.CurrentCulture = arabicCulture;
        Thread.CurrentThread.CurrentUICulture = arabicCulture;

        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(arabicCulture.IetfLanguageTag)));

        base.OnStartup(e);
    }
}

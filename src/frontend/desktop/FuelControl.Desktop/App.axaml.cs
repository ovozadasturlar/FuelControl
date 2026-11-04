using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FuelControl.Desktop.ViewModels;
using FuelControl.Desktop.Views;

namespace FuelControl.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        Controls.KesilganMatn.Yoqish();
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Namuna (demo) rejimi: server o'rniga dizayndagi ma'lumot (FUELCONTROL_NAMUNA=1).
        Services.XatoJurnali.UiniUlash();
        if (System.Environment.GetEnvironmentVariable("FUELCONTROL_NAMUNA") == "1") Services.ApiMijoz.Namuna = new Services.NamunaServer();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow { DataContext = new MainViewModel() };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

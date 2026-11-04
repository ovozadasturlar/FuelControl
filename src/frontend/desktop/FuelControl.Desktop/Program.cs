using System;
using Avalonia;

namespace FuelControl.Desktop;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Sana va oy nomlari o'zbek (lotin) tilida chiqishi uchun.
        var uz = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.GetCultureInfo("uz-Latn-UZ").Clone();
        uz.NumberFormat.NumberDecimalSeparator = ".";
        uz.NumberFormat.NumberGroupSeparator = " ";
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = uz;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = uz;
        System.Threading.Thread.CurrentThread.CurrentCulture = uz;

        Services.XatoJurnali.JarayonniUlash();
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        catch (Exception e) { Services.XatoJurnali.Yoz("Main (dastur yopildi)", e); throw; }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}

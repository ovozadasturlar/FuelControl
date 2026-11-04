using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace FuelControl.Desktop.Services;

/// <summary>
/// Ushlanmagan istisnolar: faylga yoziladi (%LOCALAPPDATA%\FuelControl\logs\desktop-YYYYMMDD.log) va UI oqimidagisi
/// dasturni yopmaydi — foydalanuvchiga xato xabari ko'rsatiladi. AppDomain darajasidagisini to'xtatib bo'lmaydi, faqat yoziladi.
/// Sinovda (FUELCONTROL_SOZLAMA_PAPKA berilgan) log o'sha papkadagi logs\ ga yoziladi.
/// </summary>
public static class XatoJurnali
{
    private static readonly object Qulf = new();
    private static DateTime _oxirgiXabar = DateTime.MinValue;
    private static bool _korsatilmoqda;

    public static string Papka => Path.Combine(
        Environment.GetEnvironmentVariable(Sozlama.PapkaOzgaruvchisi) is { Length: > 0 } p
            ? p
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuelControl"),
        "logs");

    /// <summary>Program.Main boshida: jarayon darajasidagi ushlagichlar.</summary>
    public static void JarayonniUlash()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Yoz("AppDomain" + (e.IsTerminating ? " (dastur yopiladi)" : ""), e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Yoz("Task", e.Exception); e.SetObserved(); };
    }

    /// <summary>Avalonia ishga tushgach: UI oqimidagi istisno — yoziladi, dastur ishlashda davom etadi.</summary>
    public static void UiniUlash() => Dispatcher.UIThread.UnhandledException += (_, e) =>
    {
        Yoz("UI", e.Exception);
        e.Handled = true;
        Xabar();
    };

    public static void Yoz(string manba, Exception? e)
    {
        try
        {
            lock (Qulf)
            {
                Directory.CreateDirectory(Papka);
                File.AppendAllText(Path.Combine(Papka, $"desktop-{DateTime.Now:yyyyMMdd}.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {manba}: {e}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch (Exception) { /* log yozilmasa ham dastur yiqilmasin */ }
    }

    /// <summary>Xabar ko'pi bilan 5 soniyada bir marta (chizish siklidagi takroriy xato dialog bo'roni bermasin).</summary>
    private static void Xabar()
    {
        if (_korsatilmoqda || DateTime.Now - _oxirgiXabar < TimeSpan.FromSeconds(5)) return;
        _oxirgiXabar = DateTime.Now;
        _korsatilmoqda = true;
        try { Bildirish.Xato(Til.F("Xato_Kutilmagan", Papka)); }
        catch (Exception e) { Yoz("Xabar", e); }
        finally { _korsatilmoqda = false; }
    }
}

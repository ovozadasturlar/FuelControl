using System.IO.Compression;
using System.Net.Http.Json;
using FuelControl.Api.Xizmatlar;
using FuelControl.Contracts.Dto;
using FuelControl.Core.Xizmatlar;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FuelControl.Api.Tests;

/// <summary>
/// Zaxira/Telegram testlari asosi: papkalar test papkasi ichida (zaxira, standart), soxta Telegram, to'liq (Trace) log yig'ish. Bugungi soxta nusxa
/// <see cref="NavbatniKut"/> fon ishlari (smena yopilgandagi nusxa, Telegram) tugashini kutadi.
/// </summary>
public abstract class ZaxiraTestBaza : ApiBaza
{
    protected const string Token = "123456789:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw";
    protected const string Chat = "-1001234567890";

    protected readonly SoxtaTelegram Telegram = new();
    protected readonly LogYigish Loglar = new();

    protected string ZaxiraPapkasi => Path.Combine(Papka, "zaxira");
    protected string StandartPapka => Path.Combine(Papka, "standart");
    protected virtual string? SaqlashSozlamasi => null;
    protected virtual string? TelegramMaxBayt => null;
    protected virtual string SozlanganPapka => ZaxiraPapkasi;
    protected virtual string StandartPapkaYoli => StandartPapka;

    protected override void Sozla(IWebHostBuilder b)
    {
        b.UseSetting("Zaxira:Papka", SozlanganPapka);
        b.UseSetting("Zaxira:StandartPapka", StandartPapkaYoli);
        b.UseSetting("Zaxira:Shoxobcha", "Sinov shoxobcha");
        if (SaqlashSozlamasi is { } s) b.UseSetting("Zaxira:Saqlash", s);
        if (TelegramMaxBayt is { } m) b.UseSetting("Zaxira:TelegramMaxBayt", m);
        b.ConfigureLogging(l => { l.SetMinimumLevel(LogLevel.Trace); l.AddProvider(Loglar); });
        b.ConfigureServices(x => x.AddHttpClient(TelegramYuboruvchi.MijozNomi).ConfigurePrimaryHttpMessageHandler(() => Telegram));
    }

    /// <summary>Bugungi sanali eski "kunlik" fayl (0.1.4 va undan eskilarida shunday nusxalar bor edi); endi avtomatik kunlik nusxa olinmaydi.</summary>
    protected string BugungiNusxa(string? papka = null)
    {
        papka ??= ZaxiraPapkasi;
        Directory.CreateDirectory(papka);
        var yol = Path.Combine(papka, ZaxiraQoidalari.Nom(DateTime.UtcNow.Date, ZaxiraQoidalari.Kunlik));
        File.WriteAllText(yol, "soxta");
        return yol;
    }

    /// <summary>Fon navbati bo'shashini kutadi (smena yopish, qo'lda nusxa, soatlik qayta urinish).</summary>
    protected async Task NavbatniKut()
    {
        var navbat = Ilova.Services.GetRequiredService<ZaxiraNavbati>();
        for (var i = 0; i < 600 && !navbat.Bosh; i++) await Task.Delay(25);
        Assert.True(navbat.Bosh, "zaxira navbati 15 soniyada bo'shamadi");
    }

    /// <summary>Bugungi soxta nusxa, Telegram token va chat bilan sozlangan admin mijoz (fon ishlari tugaguncha kutiladi).</summary>
    protected async Task<HttpClient> Sozlangan(bool yoqilgan = true)
    {
        var admin = await Admin();
        await NavbatniKut();                                                              // ishga tushishdagi fon ishi (sozlama hali yo'q) tugasin
        await Oqi<ZaxiraHolatiDto>(await admin.PutAsJsonAsync("/zaxira/telegram", new ZaxiraTelegramTahrirDto(Token, Chat, yoqilgan), Json));
        return admin;
    }

    protected static string GzipMazmun(byte[] gz)
    {
        using var oqim = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress);
        var bosh = new byte[16];
        oqim.ReadExactly(bosh);
        return System.Text.Encoding.ASCII.GetString(bosh);
    }

    protected static string[] Fayllar(string papka) =>
        Directory.Exists(papka) ? [.. Directory.EnumerateFiles(papka, "fuelcontrol-*.db").Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)] : [];
}

using System.Net.Http.Json;
using FuelControl.Api.Xizmatlar;
using FuelControl.Contracts.Dto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FuelControl.Api.Tests;

/// <summary>Telegram hajm chegarasi (50 MB; testda kichik chegara bilan) va token logda/javobda yo'qligi.</summary>
public sealed class ZaxiraKattaFaylTests : ZaxiraTestBaza
{
    protected override string? TelegramMaxBayt => "200";                                  // bayt: har qanday SQLite bazasi gzip'dan keyin ham undan katta

    [Fact]
    public async Task ChegaradanKattaNusxa_Yuborilmaydi_HolatdaKorinadi_QaytaUrinilmaydi()
    {
        var admin = await Sozlangan();

        var birinchi = await Oqi<ZaxiraJavobiDto>(await admin.PostAsync("/zaxira", null));
        await NavbatniKut();

        Assert.Empty(Telegram.Sorovlar);                                                  // tarmoqqa chiqmadi
        var t = (await Oqi<ZaxiraHolatiDto>(await admin.GetAsync("/zaxira/holat"))).Telegram;
        Assert.False(t.OxirgiMuvaffaqiyat);
        Assert.Contains(birinchi.FaylNomi, t.OxirgiXabar);
        Assert.Contains("chegarasidan", t.OxirgiXabar);

        await Oqi<ZaxiraJavobiDto>(await admin.PostAsync("/zaxira", null));
        await NavbatniKut();
        Assert.Empty(Telegram.Sorovlar);                                                  // birinchi nusxa "yuborilmagan" bo'lib qolmadi: qayta urinilmaydi
    }
}

public sealed class ZaxiraTokenXavfsizligiTests : ZaxiraTestBaza
{
    [Fact]
    public async Task Token_LogdaVaHolatdaYoq_ManzildagiTokenHamLogga_Tushmaydi()
    {
        var admin = await Sozlangan();
        Telegram.Javoblar.Enqueue(SoxtaTelegram.Istisno(new HttpRequestException($"Ulanib bo'lmadi: https://api.telegram.org/bot{Token}/sendDocument")));
        await Oqi<ZaxiraJavobiDto>(await admin.PostAsync("/zaxira", null));               // xato yuborish
        await NavbatniKut();
        Telegram.Javoblar.Enqueue(SoxtaTelegram.Istisno(new TaskCanceledException($"vaqt: bot{Uri.EscapeDataString(Token)}")));
        await admin.PostAsync("/zaxira/telegram/sinov", null);                           // xato sinov
        await Oqi<ZaxiraJavobiDto>(await admin.PostAsync("/zaxira", null));               // muvaffaqiyatli yuborish
        await NavbatniKut();

        var log = Loglar.Hammasi;
        Assert.Contains("Zaxira nusxa Telegram'ga yuborildi", log);                       // yuborish loglanadi (sinov bo'sh emas)
        Assert.Contains("Telegram so'rovi bajarilmadi", log);
        Assert.DoesNotContain(Token, log);
        Assert.DoesNotContain("AAHdqTc", log);
        Assert.DoesNotContain(Uri.EscapeDataString(Token), log);
        Assert.DoesNotMatch(@"api\.telegram\.org/bot\d", log);                               // HttpClient'ning standart "so'rov manzili" logi o'chirilgan
        var holat = await admin.GetStringAsync("/zaxira/holat");
        Assert.DoesNotContain("AAHdq", holat);
        Assert.Contains(Telegram.Sorovlar, s => s.Yol.StartsWith("/bot" + Token));        // so'rov haqiqatan token bilan ketgan edi
    }
}

/// <summary>Sir shifrlovchi: qayta ishga tushganda ochiladi (DPAPI/kalit fayli), buzilgan yoki begona shifr ochilmaydi (token "yo'q").</summary>
public sealed class SirShifrlovchiTests : IDisposable
{
    private const string Sir = "123456789:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw";
    private readonly string _papka = Path.Combine(Path.GetTempPath(), "fc-sir-" + Guid.NewGuid().ToString("N"));

    public SirShifrlovchiTests() => Directory.CreateDirectory(_papka);

    public void Dispose() { try { Directory.Delete(_papka, true); } catch (IOException) { } }

    private SirShifrlovchi Yangi(bool faylKaliti) => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Baza"] = $"Data Source={Path.Combine(_papka, "fc.db")}",
        ["Zaxira:FaylKalitiMajburiy"] = faylKaliti ? "true" : "false",
    }).Build(), NullLogger<SirShifrlovchi>.Instance);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shifr_OchiqMatnniSaqlamaydi_YangiNusxaOchadi(bool faylKaliti)
    {
        var shifr = Yangi(faylKaliti).Shifrla(Sir);
        Assert.DoesNotContain("AAHdq", shifr);
        Assert.DoesNotContain("123456789", shifr);
        Assert.Equal(Sir, Yangi(faylKaliti).Och(shifr));                                  // boshqa nusxa (qayta ishga tushish) ham ochadi
        Assert.NotEqual(shifr, Yangi(faylKaliti).Shifrla(Sir));                           // har safar boshqa (tasodifiy nonce / DPAPI tuzi)
    }

    [Fact]
    public void KalitFayli_BazaPapkasidaYaratiladi_VaFaylYoqolsaOchilmaydi()
    {
        var shifr = Yangi(true).Shifrla(Sir);
        var kalit = Path.Combine(_papka, "zaxira-telegram.kalit");
        Assert.True(File.Exists(kalit));
        File.Delete(kalit);
        Assert.Null(Yangi(true).Och(shifr));                                              // kalit boshqa: "token yo'q", istisno emas
    }

    [Theory]
    [InlineData("")]
    [InlineData("aes1:")]
    [InlineData("aes1:!!!nomalum")]
    [InlineData("dpapi1:AAAA")]
    [InlineData("begona:abc")]
    [InlineData(Sir)]
    public void BuzilganYokiBegonaShifr_Null_IstisnoYoq(string shifr)
    {
        Yangi(true).Shifrla(Sir);
        Assert.Null(Yangi(true).Och(shifr));
        Assert.Null(Yangi(false).Och(shifr));
    }
}

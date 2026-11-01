using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using FuelControl.Api.Xizmatlar;
using FuelControl.Contracts.Dto;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelControl.Api.Tests;

/// <summary>Telegram'ga yuborish (docs 8.17): soxta HTTP handler bilan muvaffaqiyat, xato, qayta urinish va hajm chegarasi.</summary>
public sealed class ZaxiraTelegramTests : ZaxiraTestBaza
{
    [Fact]
    public async Task QoldaNusxa_GzipHujjatIzohBilanYuboriladi_HolatdaMuvaffaqiyat()
    {
        var admin = await Sozlangan();

        var javob = await Oqi<ZaxiraJavobiDto>(await admin.PostAsync("/zaxira", null));
        await NavbatniKut();

        Assert.True(javob.TelegramYuboriladi);
        var hujjat = Assert.Single(Telegram.Hujjatlar);
        Assert.Equal($"/bot{Token}/sendDocument", hujjat.Yol);
        Assert.Equal("https://api.telegram.org", hujjat.Mezbon);                          // "bot123:AAH.." nisbiy manzil sxema deb o'qilmaydi
        Assert.Equal(Chat, hujjat.Maydonlar["chat_id"]);
        Assert.Equal(javob.FaylNomi + ".gz", hujjat.HujjatNomi);
        Assert.Equal("SQLite format 3\0", GzipMazmun(hujjat.Hujjat!));                    // gzip ichida haqiqiy baza
        var izoh = hujjat.Maydonlar["caption"];
        Assert.StartsWith("Sinov shoxobcha · ", izoh);
        Assert.Contains("qo'lda", izoh);
        Assert.Matches(@"· \d+(\.\d)? (B|KB|MB)$", izoh);                                    // hajm birligi bor
        Assert.Matches(@"\d{2}\.\d{2}\.\d{4} \d{2}:\d{2}", izoh);                         // sana va vaqt

        var t = (await Oqi<ZaxiraHolatiDto>(await admin.GetAsync("/zaxira/holat"))).Telegram;
        Assert.True(t.OxirgiMuvaffaqiyat);
        Assert.Equal("nusxa", t.OxirgiTuri);
        Assert.Contains(javob.FaylNomi + ".gz", t.OxirgiXabar);
        Assert.NotNull(t.OxirgiVaqt);
    }

    [Fact]
    public async Task SmenaYopilgach_NusxaTelegramgaYuboriladi_IzohdaSmenaRaqami()
    {
        var admin = await Sozlangan();
        var smena = await Och(admin);

        await Oqi<SmenaDto>(await Yop(admin, smena.Id, Oxirgi(await Aparatlar(admin), 10m), naqd: 0));
        await NavbatniKut();

        var hujjat = Assert.Single(Telegram.Hujjatlar);
        Assert.EndsWith($"-smena{smena.Id}.db.gz", hujjat.HujjatNomi);
        Assert.Contains($"smena #{smena.Id} yopildi", hujjat.Maydonlar["caption"]);
    }

    [Fact]
    public async Task Xato_HolatgaYoziladi_KeyingiNusxadaYuborilmaganEskisiHamKetadi()
    {
        var admin = await Sozlangan();
        Telegram.Javoblar.Enqueue(() => SoxtaTelegram.Xato(502, "Bad Gateway"));

        var birinchi = await Oqi<ZaxiraJavobiDto>(await admin.PostAsync("/zaxira", null));
        await NavbatniKut();
        var xato = (await Oqi<ZaxiraHolatiDto>(await admin.GetAsync("/zaxira/holat"))).Telegram;
        Assert.False(xato.OxirgiMuvaffaqiyat);
        Assert.Contains("Bad Gateway", xato.OxirgiXabar);
        Assert.DoesNotContain(Token, xato.OxirgiXabar);
        Assert.Single(Telegram.Hujjatlar);

        var ikkinchi = await Oqi<ZaxiraJavobiDto>(await admin.PostAsync("/zaxira", null));
        await NavbatniKut();

        Assert.Equal([birinchi.FaylNomi + ".gz", birinchi.FaylNomi + ".gz", ikkinchi.FaylNomi + ".gz"], Telegram.Hujjatlar.Select(h => h.HujjatNomi));   // eski qayta, keyin yangi
        var t = (await Oqi<ZaxiraHolatiDto>(await admin.GetAsync("/zaxira/holat"))).Telegram;
        Assert.True(t.OxirgiMuvaffaqiyat);
        Assert.Contains(ikkinchi.FaylNomi, t.OxirgiXabar);
    }

    [Fact]
    public async Task SoatlikQaytaUrinish_NavbatYuborish_YuborilmaganNusxaniYuboradi()
    {
        var admin = await Sozlangan();
        Telegram.Javoblar.Enqueue(SoxtaTelegram.Istisno(new HttpRequestException($"Internet yo'q: https://api.telegram.org/bot{Token}/sendDocument")));
        var nusxa = await Oqi<ZaxiraJavobiDto>(await admin.PostAsync("/zaxira", null));
        await NavbatniKut();
        var xato = (await Oqi<ZaxiraHolatiDto>(await admin.GetAsync("/zaxira/holat"))).Telegram;
        Assert.False(xato.OxirgiMuvaffaqiyat);
        Assert.DoesNotContain(Token, xato.OxirgiXabar);                                   // istisno matnidagi manzildan token olib tashlangan

        Ilova.Services.GetRequiredService<ZaxiraNavbati>().Yuborish();                    // KunlikIshlarFonXizmati har soatda shuni chaqiradi
        await NavbatniKut();

        Assert.Equal(nusxa.FaylNomi + ".gz", Telegram.Hujjatlar[^1].HujjatNomi);
        Assert.True((await Oqi<ZaxiraHolatiDto>(await admin.GetAsync("/zaxira/holat"))).Telegram.OxirgiMuvaffaqiyat);
        Ilova.Services.GetRequiredService<ZaxiraNavbati>().Yuborish();                    // yuborilgan nusxa qayta ketmaydi
        await NavbatniKut();
        Assert.Equal(2, Telegram.Hujjatlar.Length);
    }

    [Fact]
    public async Task TokenYoki_Yoqilmagan_HechNarsaYuborilmaydi()
    {
        var admin = await Admin();
        await NavbatniKut();
        var tokensiz = await Oqi<ZaxiraJavobiDto>(await admin.PostAsync("/zaxira", null));
        await NavbatniKut();
        Assert.False(tokensiz.TelegramYuboriladi);
        Assert.Empty(Telegram.Sorovlar);

        await admin.PutAsJsonAsync("/zaxira/telegram", new ZaxiraTelegramTahrirDto(Token, Chat, false), Json);                // token bor, lekin o'chirilgan
        var ochiq = await Oqi<ZaxiraJavobiDto>(await admin.PostAsync("/zaxira", null));
        await NavbatniKut();
        Assert.False(ochiq.TelegramYuboriladi);
        Assert.Empty(Telegram.Sorovlar);
    }
}

using System.Net;
using System.Net.Http.Json;
using FuelControl.Api.Data;
using FuelControl.Contracts.Dto;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelControl.Api.Tests;

/// <summary>Telegram sozlamalari (docs 8.17): token maskalanadi va shifrlanadi, null/""/qiymat ma'nolari, audit, sinov xabari, ruxsat va qayta o'rnatishda token saqlanishi.</summary>
public sealed class ZaxiraSozlamaTests : ZaxiraTestBaza
{
    private static Task<HttpResponseMessage> Saqla(HttpClient m, string? token, string? chat, bool? yoqilgan = null) =>
        m.PutAsJsonAsync("/zaxira/telegram", new ZaxiraTelegramTahrirDto(token, chat, yoqilgan), Json);

    private static async Task<ZaxiraTelegramDto> Holat(HttpClient m) => (await Oqi<ZaxiraHolatiDto>(await m.GetAsync("/zaxira/holat"))).Telegram;

    [Fact]
    public async Task Token_JavobdaFaqatMaska_BazadaShifrlangan()
    {
        var admin = await Admin();
        var javob = await Saqla(admin, Token, Chat);
        var matn = await javob.Content.ReadAsStringAsync();

        var t = (await Oqi<ZaxiraHolatiDto>(javob)).Telegram;
        Assert.Equal(("…Dsaw", Chat, true, true), (t.TokenMaska, t.ChatId, t.Yoqilgan, t.Faol));
        Assert.Null(t.OxirgiVaqt);
        foreach (var yoz in new[] { matn, await (await admin.GetAsync("/zaxira/holat")).Content.ReadAsStringAsync() })
        {
            Assert.DoesNotContain(Token, yoz);
            Assert.DoesNotContain("AAHdq", yoz);                                          // tokenning boshi ham chiqmaydi
        }
        using var scope = Ilova.Services.CreateScope();
        var qator = await scope.ServiceProvider.GetRequiredService<FuelControlDbContext>().ZaxiraSozlamalari.AsNoTracking().SingleAsync();
        Assert.Equal(1, qator.Id);
        Assert.DoesNotContain("AAHdq", qator.TelegramTokenShifr);                                                    // bazada ochiq emas
        Assert.DoesNotContain("123456789", qator.TelegramTokenShifr);
        if (OperatingSystem.IsWindows()) Assert.StartsWith("dpapi1:", qator.TelegramTokenShifr);
    }

    [Fact]
    public async Task Maydonlar_NullOzgarmaydi_BoshMatnOchiradi_AlohidaYoqilgan()
    {
        var admin = await Admin();
        var bosh = await Holat(admin);                                                    // hech narsa saqlanmagan: yoqilgan (standart), lekin faol emas
        Assert.Equal((true, false, null, null), (bosh.Yoqilgan, bosh.Faol, bosh.TokenMaska, bosh.ChatId));

        await Saqla(admin, Token, null);
        var faqatToken = await Holat(admin);
        Assert.Equal(("…Dsaw", null, false), (faqatToken.TokenMaska, faqatToken.ChatId, faqatToken.Faol));        // chat yo'q: faol emas
        await Saqla(admin, null, Chat);                                                   // token null - o'zgarmadi
        var ikkala = await Holat(admin);
        Assert.Equal(("…Dsaw", Chat, true), (ikkala.TokenMaska, ikkala.ChatId, ikkala.Faol));
        await Saqla(admin, null, null, false);                                            // faqat belgi
        var o = await Holat(admin);
        Assert.Equal((false, false, "…Dsaw", Chat), (o.Yoqilgan, o.Faol, o.TokenMaska, o.ChatId));
        await Saqla(admin, null, null, true);
        Assert.True((await Holat(admin)).Faol);
        await Saqla(admin, "", null);                                                     // token o'chdi
        var tokensiz = await Holat(admin);
        Assert.Equal((null, Chat, false, true), (tokensiz.TokenMaska, tokensiz.ChatId, tokensiz.Faol, tokensiz.Yoqilgan));
        await Saqla(admin, null, "");                                                     // chat o'chdi
        Assert.Null((await Holat(admin)).ChatId);
    }

    [Theory]
    [InlineData("yomon-token-qiymati-123", null)]
    [InlineData("123456789:qisqa", null)]
    [InlineData(null, "chat-emas")]
    [InlineData(null, "@ab")]
    public async Task NotogriFormat_400_XatoMatniDaKiritilganQiymatYoq(string? token, string? chat)
    {
        var admin = await Admin();
        var javob = await Saqla(admin, token, chat);
        await Kut(HttpStatusCode.BadRequest, javob);
        var detail = await Detail(javob);
        if (token is not null) Assert.DoesNotContain(token, detail);
        if (chat is not null) Assert.DoesNotContain(chat, detail);
        var t = await Holat(admin);
        Assert.Equal((null, null), (t.TokenMaska, t.ChatId));                             // hech narsa saqlanmadi
        var log = Loglar.Hammasi;                                                         // kutilgan xato: ogohlantirish, istisno va stek yo'q
        Assert.Contains("[Warning]", log);
        Assert.Contains("Telegram sozlamasi saqlanmadi", log);
        Assert.DoesNotContain("ArgumentException", log);
        Assert.DoesNotContain("unhandled exception", log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at FuelControl", log);
        Assert.DoesNotContain("123456789:AAH", log);                                      // namuna matni ham logga tushmaydi
        if (token is not null) Assert.DoesNotContain(token, log);
        if (chat is not null) Assert.DoesNotContain(chat, log);
    }

    [Fact]
    public async Task Audit_SozlamaYoziladi_TokenningOzi_YoziladiMas()
    {
        var admin = await Admin();
        await Saqla(admin, Token, Chat);
        await Saqla(admin, null, Chat);                                                   // o'zgarmagan: audit yozmaydi
        await Saqla(admin, "", null, false);

        var royxat = (await admin.GetFromJsonAsync<AuditYozuviDto[]>("/audit?limit=100", Json))!;
        var yozuvlar = royxat.Where(a => a.Amal == "Telegram sozlandi").OrderBy(a => a.Vaqt).ToArray();
        Assert.Equal(["token yangilandi · chat ID " + Chat, "token o'chirildi · o'chirilgan"], yozuvlar.Select(a => a.Tafsilot));
        Assert.All(yozuvlar, a => { Assert.Equal("sozlama", a.Tur); Assert.Equal("Administrator", a.Kim); });
        var hammasi = System.Text.Json.JsonSerializer.Serialize(royxat);
        Assert.DoesNotContain("AAHdq", hammasi);
        Assert.DoesNotContain("Dsaw", hammasi);                                           // maskaning o'zi ham auditda yo'q
    }

    [Fact]
    public async Task Sinov_Muvaffaqiyat_Va_Telegramga_SinovXabari()
    {
        var admin = await Sozlangan();
        var holat = await Oqi<ZaxiraHolatiDto>(await admin.PostAsync("/zaxira/telegram/sinov", null));

        var xabar = Assert.Single(Telegram.Xabarlar);
        Assert.Equal($"/bot{Token}/sendMessage", xabar.Yol);
        Assert.Equal(Chat, xabar.Maydonlar["chat_id"]);
        Assert.Contains("sinov xabari", xabar.Maydonlar["text"]);
        Assert.Contains("Sinov shoxobcha", xabar.Maydonlar["text"]);
        Assert.Equal((true, "sinov"), (holat.Telegram.OxirgiMuvaffaqiyat, holat.Telegram.OxirgiTuri));
        Assert.NotNull(holat.Telegram.OxirgiVaqt);
    }

    [Fact]
    public async Task Sinov_TelegramXatosi_TashqiXato_200_HolatdaXatoMatni()
    {
        var admin = await Sozlangan();
        Telegram.Javoblar.Enqueue(() => SoxtaTelegram.Xato(401, "Unauthorized"));
        Telegram.Javoblar.Enqueue(SoxtaTelegram.Istisno(new HttpRequestException($"Tarmoq yo'q: https://api.telegram.org/bot{Token}/sendMessage")));

        var birinchi = await admin.PostAsync("/zaxira/telegram/sinov", null);
        await Kut(HttpStatusCode.OK, birinchi);
        var t1 = (await Oqi<ZaxiraHolatiDto>(birinchi)).Telegram;
        Assert.False(t1.OxirgiMuvaffaqiyat);
        Assert.Contains("Unauthorized", t1.OxirgiXabar);

        var ikkinchi = await admin.PostAsync("/zaxira/telegram/sinov", null);
        await Kut(HttpStatusCode.OK, ikkinchi);
        var t2 = (await Oqi<ZaxiraHolatiDto>(ikkinchi)).Telegram;
        Assert.False(t2.OxirgiMuvaffaqiyat);
        Assert.Contains("Tarmoq yo'q", t2.OxirgiXabar);
        Assert.DoesNotContain("AAHdq", t2.OxirgiXabar);
        Assert.DoesNotContain("AAHdq", await ikkinchi.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Sinov_TokenYokiChatYoq_400_YoqilmaganBolsaHamIshlaydi()
    {
        var admin = await Admin();
        await Kut(HttpStatusCode.BadRequest, await admin.PostAsync("/zaxira/telegram/sinov", null));
        await Saqla(admin, Token, null);
        await Kut(HttpStatusCode.BadRequest, await admin.PostAsync("/zaxira/telegram/sinov", null));
        await Saqla(admin, null, Chat, false);                                            // o'chirilgan, lekin token va chat bor
        await Kut(HttpStatusCode.OK, await admin.PostAsync("/zaxira/telegram/sinov", null));
        Assert.Single(Telegram.Xabarlar);
    }

    [Fact]
    public async Task Ruxsatsiz_403_Tokensiz_401()
    {
        var (_, operatorMijoz) = await Yarat("ali");                                      // standart Operator: "Sozlamalar" ruxsati yo'q
        foreach (var javob in new[]
        {
            await operatorMijoz.GetAsync("/zaxira/holat"),
            await Saqla(operatorMijoz, Token, Chat),
            await operatorMijoz.PostAsync("/zaxira/telegram/sinov", null),
            await operatorMijoz.PostAsync("/zaxira", null),
        }) await Kut(HttpStatusCode.Forbidden, javob);
        var anonim = Ilova.CreateClient();
        await Kut(HttpStatusCode.Unauthorized, await anonim.GetAsync("/zaxira/holat"));
        await Kut(HttpStatusCode.Unauthorized, await anonim.PostAsync("/zaxira/telegram/sinov", null));
        Assert.Empty(Telegram.Sorovlar);
    }

    [Fact]
    public async Task Token_YangiJwtKalitibilanQaytaIshgaTushganda_Saqlanadi()
    {
        var admin = await Admin();
        await Saqla(admin, Token, Chat);
        Assert.Equal("…Dsaw", (await Holat(admin)).TokenMaska);

        // Qayta o'rnatish: o'sha baza, yangi JWT kaliti (o'rnatuvchi har safar yangisini yaratadi). Token shifri JWT kalitiga bog'liq emas.
        using var qayta = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(Muhit);
            b.UseSetting("ConnectionStrings:Baza", $"Data Source={Path.Combine(Papka, "fc.db")}");
            b.UseSetting("Jwt:Kalit", "yangi-boshqa-kalit-0123456789abcdef0123456789abcdef");
            b.UseSetting("Seed:AdminParol", "admin1234");
            b.UseSetting("Zaxira:Papka", ZaxiraPapkasi);
            b.UseSetting("Zaxira:StandartPapka", StandartPapka);
        });
        var mijoz = qayta.CreateClient();
        var kirish = await mijoz.PostAsJsonAsync("/auth/login", new LoginSoroviDto("admin", "admin1234"), Json);
        mijoz.DefaultRequestHeaders.Authorization = new("Bearer", (await Oqi<LoginJavobiDto>(kirish)).Token);

        var t = (await Oqi<ZaxiraHolatiDto>(await mijoz.GetAsync("/zaxira/holat"))).Telegram;
        Assert.Equal(("…Dsaw", Chat, true), (t.TokenMaska, t.ChatId, t.Faol));
    }
}

using FuelControl.Core.Xizmatlar;
using Xunit;

namespace FuelControl.Core.Tests;

/// <summary>Zaxira nusxa fayl nomi, saqlash soni, Telegram token/chat tekshiruvi, maska va izoh matni (docs 8.17).</summary>
public sealed class ZaxiraQoidalariTests
{
    private const string Token = "123456789:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw";
    private static readonly DateTime Vaqt = new(2026, 11, 1, 9, 30, 5, DateTimeKind.Utc);

    [Theory]
    [InlineData(ZaxiraQoidalari.Kunlik, null, 0, "fuelcontrol-20261101-093005-kunlik.db")]
    [InlineData(ZaxiraQoidalari.Qolda, null, 0, "fuelcontrol-20261101-093005-qolda.db")]
    [InlineData(ZaxiraQoidalari.Smena, 42, 0, "fuelcontrol-20261101-093005-smena42.db")]
    [InlineData(ZaxiraQoidalari.Qolda, null, 1, "fuelcontrol-20261101-093005-qolda-2.db")]
    [InlineData(ZaxiraQoidalari.Smena, 7, 2, "fuelcontrol-20261101-093005-smena7-3.db")]
    public void Nom_SababVaTakror(string sabab, int? smena, int takror, string kutilgan)
    {
        var nom = ZaxiraQoidalari.Nom(Vaqt, sabab, smena, takror);
        Assert.Equal(kutilgan, nom);
        var t = ZaxiraQoidalari.Tahlil(nom)!;                              // nom -> tahlil: qaytadan o'qiladi
        Assert.Equal(Vaqt, t.Vaqt);
        Assert.Equal(DateTimeKind.Utc, t.Vaqt.Kind);
        Assert.Equal(sabab, t.Sabab);
        Assert.Equal(smena, t.SmenaId);
    }

    [Fact]
    public void Tahlil_EskiNomSababsiz_VaYaroqsizNomlar()
    {
        var eski = ZaxiraQoidalari.Tahlil(@"C:\ProgramData\FuelControl\zaxira\fuelcontrol-20261031-050000.db")!;   // 0.1.4 gacha nomlar
        Assert.Equal("", eski.Sabab);
        Assert.Null(eski.SmenaId);
        Assert.Equal(new DateTime(2026, 10, 31, 5, 0, 0, DateTimeKind.Utc), eski.Vaqt);
        Assert.Null(ZaxiraQoidalari.Tahlil("fuelcontrol-20261301-093005.db"));        // 13-oy
        Assert.Null(ZaxiraQoidalari.Tahlil("fuelcontrol-foo.db"));
        Assert.Null(ZaxiraQoidalari.Tahlil("fuelcontrol-20261101-093005-boshqa.db"));
        Assert.Null(ZaxiraQoidalari.Tahlil("fuelcontrol-20261101-093005-kunlik.db.gz"));
        Assert.Null(ZaxiraQoidalari.Tahlil("fc.db"));
    }

    [Theory]
    [InlineData(null, 30)]
    [InlineData("", 30)]
    [InlineData("abc", 30)]
    [InlineData("0", 30)]
    [InlineData("-5", 30)]
    [InlineData("1", 1)]
    [InlineData("30", 30)]
    [InlineData("90", 90)]
    public void Saqlash_Sozlamasi(string? sozlama, int kutilgan) => Assert.Equal(kutilgan, ZaxiraQoidalari.Saqlash(sozlama));

    [Fact]
    public void Ochirish_31NusxadaEngEskisi_Faqat30QolishiKerak()
    {
        var nomlar = Enumerable.Range(0, 31).Select(i => ZaxiraQoidalari.Nom(Vaqt.AddHours(-i), ZaxiraQoidalari.Kunlik)).ToList();
        var ochirish = ZaxiraQoidalari.OchiriladiganlarNomi(nomlar, 30);
        Assert.Equal([ZaxiraQoidalari.Nom(Vaqt.AddHours(-30), ZaxiraQoidalari.Kunlik)], ochirish);                // faqat eng eskisi
        Assert.Empty(ZaxiraQoidalari.OchiriladiganlarNomi(nomlar.Take(30), 30));                                    // 30 tada hech narsa
    }

    [Fact]
    public void Ochirish_VaqtNomdaVaSababAralash_YangilariQoladi_BegonaFaylTegilmaydi()
    {
        var kunlik = ZaxiraQoidalari.Nom(Vaqt, ZaxiraQoidalari.Kunlik);
        var smena = ZaxiraQoidalari.Nom(Vaqt.AddMinutes(5), ZaxiraQoidalari.Smena, 9);
        var eski = ZaxiraQoidalari.Nom(Vaqt.AddDays(-2), ZaxiraQoidalari.Qolda);
        var eskiNomsiz = "fuelcontrol-20261001-120000.db";
        var begona = "fuelcontrol-foo.db";
        var ochirish = ZaxiraQoidalari.OchiriladiganlarNomi([kunlik, smena, eski, eskiNomsiz, begona], 2);
        Assert.Equal([eskiNomsiz, eski], ochirish);                                                                 // eskisi birinchi; begona fayl ro'yxatda yo'q
        Assert.Empty(ZaxiraQoidalari.OchiriladiganlarNomi([kunlik], 0));                                            // Saqlash kamida 1: eng yangisi o'chmaydi
    }

    [Theory]
    [InlineData(Token, true)]
    [InlineData("1234567:AbCdEfGhIjKlMnOpQrStUvWxYz-_0123", true)]
    [InlineData("", false)]
    [InlineData("hammasi-xato", false)]
    [InlineData("123456789:qisqa", false)]
    [InlineData("abc:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw", false)]
    [InlineData("123456789:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw uchun", false)]
    public void Token_Tekshiruvi(string token, bool kutilgan) => Assert.Equal(kutilgan, ZaxiraQoidalari.TokenYaroqli(token));

    [Theory]
    [InlineData("123456789", true)]
    [InlineData("-1001234567890", true)]
    [InlineData("@kanal_nomi", true)]
    [InlineData("@ab", false)]
    [InlineData("kanal", false)]
    [InlineData("12-34", false)]
    [InlineData("", false)]
    public void Chat_Tekshiruvi(string chat, bool kutilgan) => Assert.Equal(kutilgan, ZaxiraQoidalari.ChatYaroqli(chat));

    [Fact]
    public void Maska_FaqatOxirgi4Belgi_VaTokenniYashirish()
    {
        Assert.Equal("…Dsaw", ZaxiraQoidalari.Maska(Token));
        Assert.Null(ZaxiraQoidalari.Maska(null));
        Assert.Null(ZaxiraQoidalari.Maska(""));
        Assert.Equal("…", ZaxiraQoidalari.Maska("qisqa"));                                                           // qisqa qiymatdan hech narsa ochilmaydi
        var url = $"https://api.telegram.org/bot{Token}/sendDocument";
        Assert.DoesNotContain(Token, ZaxiraQoidalari.TokenniYashir($"xato: {url}", Token));
        Assert.DoesNotContain("AAHdq", ZaxiraQoidalari.TokenniYashir($"xato: {Uri.EscapeDataString(Token)}", Token));
        Assert.Equal("matn", ZaxiraQoidalari.TokenniYashir("matn", null));
    }

    [Fact]
    public void Izoh_ShoxobchaToshkentVaqti_SababVaHajm()
    {
        Assert.Equal("Markaz · 01.11.2026 14:30 · smena #42 yopildi · 1.5 MB", ZaxiraQoidalari.Izoh("Markaz", Vaqt, ZaxiraQoidalari.Smena, 42, 1_572_864));
        Assert.Equal("Markaz · 01.11.2026 14:30 · kunlik · 10 KB", ZaxiraQoidalari.Izoh("Markaz", Vaqt, ZaxiraQoidalari.Kunlik, null, 10_240));
        Assert.Equal("Markaz · 01.11.2026 14:30 · qo'lda · 512 B", ZaxiraQoidalari.Izoh("Markaz", Vaqt, ZaxiraQoidalari.Qolda, null, 512));
        Assert.True(ZaxiraQoidalari.Izoh(new string('x', 3000), Vaqt, ZaxiraQoidalari.Qolda, null, 1).Length <= 1024);   // Bot API izohi 1024 belgigacha
    }
}

using System.Net;
using System.Net.Http.Json;
using FuelControl.Api.Xizmatlar;
using FuelControl.Contracts.Dto;
using FuelControl.Core.Xizmatlar;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelControl.Api.Tests;

/// <summary>Zaxira nusxa (docs 8.17): son bo'yicha saqlash, 31-nusxada eng eskisi o'chadi, smena yopilganda nusxa, ishlamaydigan papka -> standart papka.</summary>
public sealed class ZaxiraNusxaTests : ZaxiraTestBaza
{
    private static readonly DateTime Eski = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private void EskiNusxalar(int soni)
    {
        Directory.CreateDirectory(ZaxiraPapkasi);
        for (var i = 0; i < soni; i++)
            File.WriteAllText(Path.Combine(ZaxiraPapkasi, ZaxiraQoidalari.Nom(Eski.AddDays(i), ZaxiraQoidalari.Kunlik)), "soxta");
    }

    [Fact]
    public async Task Nusxa31_EngEskisiOchadi_30QolAdi()
    {
        EskiNusxalar(29);
        BugungiNusxa();                                                                   // 30-nusxa (eski kunlik fayl, bugungi sana)
        var admin = await Admin();
        Assert.Equal(30, Fayllar(ZaxiraPapkasi).Length);

        var javob = await Oqi<ZaxiraJavobiDto>(await admin.PostAsync("/zaxira", null));  // 31-nusxa

        var fayllar = Fayllar(ZaxiraPapkasi);
        Assert.Equal(30, fayllar.Length);
        Assert.DoesNotContain(ZaxiraQoidalari.Nom(Eski, ZaxiraQoidalari.Kunlik), fayllar);                          // eng eskisi o'chdi
        Assert.Contains(ZaxiraQoidalari.Nom(Eski.AddDays(1), ZaxiraQoidalari.Kunlik), fayllar);                      // ikkinchisi turibdi
        Assert.Contains(javob.FaylNomi, fayllar);
        Assert.EndsWith("-qolda.db", javob.FaylNomi);
        Assert.Equal(("qolda", false, ZaxiraPapkasi), (javob.Sabab, javob.StandartPapkaga, javob.Papka));
        Assert.True(javob.HajmBayt > 0);
    }

    [Fact]
    public async Task AvtomatikKunlikNusxaYoq_IshgaTushganda_VaSoatlikTekshiruvda_Nusxa_Olinmaydi()
    {
        var admin = await Admin();                                                        // xizmatlar ishga tushdi: soatlik sikl birinchi aylanishi ham shu paytda
        await Sozlangan();
        await NavbatniKut();
        Assert.Empty(Fayllar(ZaxiraPapkasi));
        Ilova.Services.GetRequiredService<FuelControl.Api.Xizmatlar.ZaxiraNavbati>().Yuborish();       // soatlik qayta yuborish ham nusxa olmaydi
        await NavbatniKut();
        Assert.Empty(Fayllar(ZaxiraPapkasi));
        Assert.Empty(Telegram.Sorovlar);                                                  // yuborilmagan nusxa ham yo'q
        Assert.Empty((await Oqi<ZaxiraHolatiDto>(await admin.GetAsync("/zaxira/holat"))).Nusxalar);
    }

    [Fact]
    public async Task Nusxa_SQLiteBazasiFaylidir()
    {
        var admin = await Admin();
        var javob = await Oqi<ZaxiraJavobiDto>(await admin.PostAsync("/zaxira", null));
        var bosh = new byte[16];
        await using (var f = File.OpenRead(Path.Combine(ZaxiraPapkasi, javob.FaylNomi))) await f.ReadExactlyAsync(bosh);
        Assert.Equal("SQLite format 3\0", System.Text.Encoding.ASCII.GetString(bosh));
    }

    [Fact]
    public async Task Holat_RoyxatYangidanEskiga_SababVaSmenaId()
    {
        BugungiNusxa();
        var admin = await Admin();
        await Oqi<ZaxiraJavobiDto>(await admin.PostAsync("/zaxira", null));
        var smena = await Och(admin);
        await Oqi<SmenaDto>(await Yop(admin, smena.Id, Oxirgi(await Aparatlar(admin), 10m), naqd: 0));
        await NavbatniKut();

        var holat = await Oqi<ZaxiraHolatiDto>(await admin.GetAsync("/zaxira/holat"));

        Assert.Equal((ZaxiraPapkasi, ZaxiraPapkasi, false, null, 30), (holat.Papka, holat.SozlanganPapka, holat.StandartPapkaga, holat.StandartPapkaSababi, holat.Saqlash));
        Assert.Equal(holat.Nusxalar.OrderByDescending(n => n.Nomi, StringComparer.Ordinal).Select(n => n.Nomi), holat.Nusxalar.Select(n => n.Nomi));   // yangidan eskiga
        var smenaNusxa = Assert.Single(holat.Nusxalar, n => n.Sabab == "smena");
        Assert.Equal(smena.Id, smenaNusxa.SmenaId);
        Assert.Contains(holat.Nusxalar, n => n.Sabab == "qolda" && n.SmenaId is null);
        Assert.Contains(holat.Nusxalar, n => n.Sabab == "kunlik");
        Assert.All(holat.Nusxalar, n => { Assert.True(n.HajmBayt > 0); Assert.False(n.StandartPapkada); });
    }

    [Fact]
    public async Task SmenaYopilgach_FondaNusxaOlinadi_YopishJavobiTegilmaydi()
    {
        var admin = await Admin();
        var smena = await Och(admin);
        var avval = Fayllar(ZaxiraPapkasi);

        var yopish = await Yop(admin, smena.Id, Oxirgi(await Aparatlar(admin), 10m), naqd: 0);
        var yopilgan = await Oqi<SmenaDto>(yopish);                                       // javob nusxa tayyor bo'lishini kutmaydi
        await NavbatniKut();

        Assert.NotNull(yopilgan.Tugadi);
        var yangi = Fayllar(ZaxiraPapkasi).Except(avval).ToArray();
        var nusxa = Assert.Single(yangi);
        Assert.EndsWith($"-smena{smena.Id}.db", nusxa);
        Assert.Equal(smena.Id, ZaxiraQoidalari.Tahlil(nusxa)!.SmenaId);
        Assert.True(new FileInfo(Path.Combine(ZaxiraPapkasi, nusxa)).Length > 0);
    }

    [Fact]
    public async Task SozlanganPapkaBuzuq_SmenaYopilgach_NusxaStandartPapkagaTushadi()
    {
        BuzukPapkaYarat();
        var admin = await Admin();
        var smena = await Och(admin);

        var yopilgan = await Oqi<SmenaDto>(await Yop(admin, smena.Id, Oxirgi(await Aparatlar(admin), 10m), naqd: 0));
        await NavbatniKut();

        Assert.NotNull(yopilgan.Tugadi);
        Assert.Contains(Fayllar(StandartPapka), n => n.EndsWith($"-smena{smena.Id}.db"));                          // standart papkaga tushdi, yopish buzilmadi
        Assert.Contains("Zaxira papkasi ishlamayapti", Loglar.Hammasi);
    }

    [Fact]
    public async Task IkkalaPapkaBuzuq_SmenaYopish_BaribirMuvaffaqiyatli_XatoLogda()
    {
        BuzukPapkaYarat();
        BuzukStandart = true;
        var admin = await Admin();
        var smena = await Och(admin);

        var yopish = await Yop(admin, smena.Id, Oxirgi(await Aparatlar(admin), 10m), naqd: 0);
        await NavbatniKut();

        Assert.Equal(HttpStatusCode.OK, yopish.StatusCode);
        Assert.NotNull((await Oqi<SmenaDto>(yopish)).Tugadi);
        Assert.Contains("[Error]", Loglar.Hammasi);                                                                   // zaxira xatosi logga yozildi
        var holat = await Oqi<ZaxiraHolatiDto>(await admin.GetAsync("/zaxira/holat"));                               // holat so'rovi ham yiqilmaydi, ogohlantirish bor
        Assert.False(holat.StandartPapkaga);
        Assert.NotNull(holat.StandartPapkaSababi);
    }

    [Fact]
    public async Task PapkaMavjudEmas_QoldaNusxa_StandartPapkagaTushadi_HolatdaOgohlantirish()
    {
        BuzukPapkaYarat();
        var admin = await Admin();

        var javob = await Oqi<ZaxiraJavobiDto>(await admin.PostAsync("/zaxira", null));
        var holat = await Oqi<ZaxiraHolatiDto>(await admin.GetAsync("/zaxira/holat"));

        Assert.Equal((true, StandartPapka), (javob.StandartPapkaga, javob.Papka));
        Assert.True(File.Exists(Path.Combine(StandartPapka, javob.FaylNomi)));
        Assert.True(holat.StandartPapkaga);
        Assert.Equal((StandartPapka, BuzukPapka), (holat.Papka, holat.SozlanganPapka));
        Assert.Contains(BuzukPapka!, holat.StandartPapkaSababi);                                                       // qaysi papka va nega
        Assert.Matches(OzbekSabab, holat.StandartPapkaSababi!);                                                         // sabab o'zbekcha, inglizcha istisno matni yo'q
        Assert.Contains(holat.Nusxalar, n => n.Nomi == javob.FaylNomi && n.StandartPapkada);
        Assert.Contains("Zaxira papkasi ishlamayapti", Loglar.Hammasi);
    }

    [Fact]
    public async Task SozlanganPapkaYaratiladi_DiskUlanganBolsa()
    {
        var yangiPapka = Path.Combine(Papka, "E-baza", "ichki");                                                      // hali yo'q, lekin yaratish mumkin
        Yangi = yangiPapka;
        var admin = await Admin();
        var javob = await Oqi<ZaxiraJavobiDto>(await admin.PostAsync("/zaxira", null));
        Assert.Equal((false, yangiPapka), (javob.StandartPapkaga, javob.Papka));
        Assert.True(File.Exists(Path.Combine(yangiPapka, javob.FaylNomi)));
    }

    private static readonly System.Text.RegularExpressions.Regex OzbekSabab =
        new(@"ga yozib bo'lmadi: (disk yoki papka topilmadi \(disk ulanmaganmi\?\)|papkaga yozish uchun ruxsat yo'q|diskda joy yo'q|yozib bo'lmadi)$");

    [Fact]
    public void YozolmaslikSababi_IstisnoTuriOzbekchaMatnga()
    {
        Assert.Equal("disk yoki papka topilmadi (disk ulanmaganmi?)", ZaxiraXizmati.YozolmaslikSababi(new DirectoryNotFoundException("Could not find a part of the path 'Z:\\yoq'.")));
        Assert.Equal("disk yoki papka topilmadi (disk ulanmaganmi?)", ZaxiraXizmati.YozolmaslikSababi(new DriveNotFoundException("x")));
        Assert.Equal("papkaga yozish uchun ruxsat yo'q", ZaxiraXizmati.YozolmaslikSababi(new UnauthorizedAccessException("Access to the path is denied.")));
        Assert.Equal("diskda joy yo'q", ZaxiraXizmati.YozolmaslikSababi(new IOException("There is not enough space on the disk.", unchecked((int)0x80070070))));
        Assert.Equal("diskda joy yo'q", ZaxiraXizmati.YozolmaslikSababi(new IOException("x", unchecked((int)0x80070027))));
        Assert.Equal("yozib bo'lmadi", ZaxiraXizmati.YozolmaslikSababi(new IOException("boshqa xato", unchecked((int)0x80070005))));
        Assert.Equal("yozib bo'lmadi", ZaxiraXizmati.YozolmaslikSababi(new ArgumentException("x")));
    }

    [Fact]
    public async Task UlanmaganDisk_HolatdaOzbekchaSabab_AslMatnFaqatLogda()
    {
        if (!OperatingSystem.IsWindows()) return;                                         // disk harfi - Windows tushunchasi
        var bosh = Enumerable.Range('D', 'Z' - 'D' + 1).Select(c => (char)c).FirstOrDefault(c => !DriveInfo.GetDrives().Any(d => d.Name[0] == c));
        if (bosh == default) return;
        BuzukPapka = bosh + @":\yoq\baza";
        var admin = await Admin();

        var holat = await Oqi<ZaxiraHolatiDto>(await admin.GetAsync("/zaxira/holat"));
        var javob = await Oqi<ZaxiraJavobiDto>(await admin.PostAsync("/zaxira", null));

        Assert.True(holat.StandartPapkaga);
        Assert.Equal(BuzukPapka + " ga yozib bo'lmadi: disk yoki papka topilmadi (disk ulanmaganmi?)", holat.StandartPapkaSababi);
        Assert.DoesNotContain("Could not find", holat.StandartPapkaSababi);               // inglizcha asl matn klientga chiqmaydi
        Assert.True(javob.StandartPapkaga);
        var log = Loglar.Hammasi;
        Assert.Contains("Zaxira papkasiga yozib bo'lmadi", log);                           // asl matn logda (yo'l bilan)
        Assert.Contains(BuzukPapka, log);
    }

    private string? BuzukPapka { get; set; }
    private string? Yangi { get; set; }
    private bool BuzukStandart { get; set; }

    /// <summary>Fayl ichidagi "papka": yaratib bo'lmaydi (disk ulanmagan / yozib bo'lmaydigan joyning o'rniga, har tizimda bir xil).</summary>
    private void BuzukPapkaYarat()
    {
        var fayl = Path.Combine(Papka, "fayl.txt");
        File.WriteAllText(fayl, "x");
        BuzukPapka = Path.Combine(fayl, "baza");
    }

    protected override string SozlanganPapka => BuzukPapka ?? Yangi ?? ZaxiraPapkasi;
    protected override string StandartPapkaYoli => BuzukStandart ? Path.Combine(Papka, "fayl.txt", "standart") : StandartPapka;
}

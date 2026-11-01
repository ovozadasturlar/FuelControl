using System.Net.Http.Json;
using FuelControl.Contracts;
using FuelControl.Contracts.Dto;
using Xunit;

namespace FuelControl.Api.Tests;

/// <summary>
/// Bo'sh bazada (Development + Seed:DemoMalumot) yoziladigan demo ma'lumot dizayn (docs/dizayn) bilan aynan mos:
/// smena #28-#41 yopilgan, #42 ochiq; hisobot qatorlari, aparat litrlari, bak jadvali, nasiyalar, operator hisoblari.
/// </summary>
public sealed class DemoMalumotApiTests : ApiBaza
{
    protected override string Muhit => "Development";
    protected override bool DemoSozlamasi => true;

    private static DateOnly Bugun => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5));

    [Fact]
    public async Task Hisobot_01_04_Oktabr_SmenaQatorlari_Jami_BakJadvali_DizaynBilanMos()
    {
        var admin = await Admin();
        var h = await Oqi<HisobotDto>(await admin.GetAsync("/hisobot?dan=2026-10-01&gacha=2026-10-04&guruh=smena"));

        // Hisobotlar.dc.html: har smena qatori.
        var q = h.Qatorlar.ToDictionary(x => x.Guruh);
        Assert.Equal(["41", "40", "39"], h.Qatorlar.Select(x => x.Guruh));
        void Tekshir(string smena, string operatorIsmi, decimal litr, long savdo, long plastik, long depozit, long nasiya, long qaytgan, long xarajat, int xarajatSoni, long naqd, long kamomat, long ortiqcha)
        {
            var x = q[smena];
            Assert.Equal((operatorIsmi, litr, savdo, plastik, depozit, nasiya, qaytgan, xarajat, xarajatSoni, naqd, kamomat, ortiqcha),
                (x.OperatorIsmi, x.Litr, x.Savdo, x.Plastik, x.Depozit, x.Nasiya, x.QaytganNasiya, x.Xarajat, x.XarajatSoni, x.NaqdSavdo, x.Kamomat, x.Ortiqcha));
        }
        Tekshir("39", "Dilshod Rahimov", 1205.60m, 16_098_210, 7_290_000, 3_365_900, 615_000, 0, 310_000, 2, 4_827_310, 0, 20_000);
        Tekshir("40", "Alisher Karimov", 1236.10m, 16_496_940, 7_410_000, 2_100_000, 1_200_000, 450_000, 1_620_000, 2, 5_786_940, 120_000, 0);
        Tekshir("41", "Dilshod Rahimov", 1198.40m, 15_995_270, 7_330_000, 270_000, 95_000, 0, 50_000, 1, 8_300_270, 0, 0);

        var j = h.Jami;
        Assert.Equal((3, 3640.10m, 48_590_420L), (j.SmenaSoni, j.Litr, j.Savdo));
        Assert.Equal((22_030_000L, 5_735_900L, 1_910_000L, 450_000L, 1_980_000L, 5), (j.Plastik, j.Depozit, j.Nasiya, j.QaytganNasiya, j.Xarajat, j.XarajatSoni));
        Assert.Equal((18_914_520L, 120_000L, 20_000L, 1_000_000L), (j.NaqdSavdo, j.Kamomat, j.Ortiqcha, h.Avans));

        // Aparatlar va baklar: bak boshida + kirim - sotildi = bak oxirida (litrda).
        Assert.Equal(
            [(1, 3_017.70m, 5_000m, 1_177.70m, 6_840m, 14_367_940L), (2, 3_915.00m, 0m, 700.00m, 3_215m, 8_540_000L), (3, 1_641.80m, 3_000m, 521.80m, 4_120m, 8_087_900L),
             (4, 2_239.00m, 0m, 279.00m, 1_960m, 4_324_500L), (5, 2_441.60m, 8_000m, 961.60m, 9_480m, 13_270_080L)],
            h.Aparatlar.Select(a => (a.Raqam, a.BakBoshida, a.Kirim, a.Sotildi, a.BakOxirida, a.Savdo)));
        Assert.Equal((13_255.10m, 16_000m, 3_640.10m, 25_615m), (h.Aparatlar.Sum(a => a.BakBoshida), h.Aparatlar.Sum(a => a.Kirim), h.Aparatlar.Sum(a => a.Sotildi), h.Aparatlar.Sum(a => a.BakOxirida)));
    }

    [Fact]
    public async Task Smenalar_AparatLitrlari_JoriySmena42_VaOperatorHisoblari()
    {
        var admin = await Admin();
        // Har aparat litri (Hisobotlar.dc.html va Smenalar.dc.html), har segment Boshi = oldingi smena Oxiri.
        var kutilgan = new Dictionary<int, decimal[]>
        {
            [39] = [380.40m, 235.00m, 170.10m, 92.00m, 328.10m],
            [40] = [405.20m, 235.00m, 180.40m, 92.00m, 323.50m],
            [41] = [392.10m, 230.00m, 171.30m, 95.00m, 310.00m],
        };
        foreach (var (id, litrlar) in kutilgan)
        {
            var t = await Oqi<SmenaTafsilotDto>(await admin.GetAsync($"/smenalar/{id}"));
            Assert.Equal(litrlar, t.Korsatkichlar.Select(k => k.Litr));
            Assert.All(t.Korsatkichlar, k => Assert.False(k.NarxOzgarishida));
        }
        var t41 = await Oqi<SmenaTafsilotDto>(await admin.GetAsync("/smenalar/41"));
        Assert.Equal([183_838.40m, 97_180.00m, 62_946.90m, 41_807.75m, 120_250.00m], t41.Korsatkichlar.Select(k => k.Boshi));
        Assert.Equal([184_230.50m, 97_410.00m, 63_118.20m, 41_902.75m, 120_560.00m], t41.Korsatkichlar.Select(k => k.Oxiri));
        Assert.Equal([4_783_620L, 2_806_000L, 2_655_150L, 1_472_500L, 4_278_000L], t41.Korsatkichlar.Select(k => k.Summa));
        var s = t41.Smena;
        Assert.Equal((100_000L, 150_000L, 980_000L, 7_480_000L, 1_250_000L), (s.OchishQaytim, s.OchishTerminal, s.OchishDepozit, s.YopishTerminal, s.YopishDepozit));
        Assert.Equal((15_995_270L, 7_330_000L, 270_000L, 95_000L, 50_000L, 8_350_270L, 8_350_270L, 0L),
            (s.Savdo, s.Plastik, s.DepozitFarqi, s.NasiyaJami, s.XarajatJami, s.Kutilgan, s.SanalganNaqd, s.Farq));

        // #42 ochiq: nasiya 570 000 (2 ta), qaytgan 300 000 (Bobur Aliyev), xarajat 1 585 000 (2 ta); savdo yopilganda hisoblanadi.
        var joriy = await Oqi<SmenaTafsilotDto>(await admin.GetAsync("/smenalar/joriy"));
        Assert.Equal((42, "Alisher Karimov", 100_000L, 200_000L, 1_250_000L), (joriy.Smena.Id, joriy.Smena.OperatorIsmi, joriy.Smena.OchishQaytim, joriy.Smena.OchishTerminal, joriy.Smena.OchishDepozit));
        Assert.Equal((570_000L, 300_000L, 1_585_000L, 0L), (joriy.Smena.NasiyaJami, joriy.Smena.QaytganNasiya, joriy.Smena.XarajatJami, joriy.Smena.Savdo));
        Assert.Equal((2, 1, 2, 0), (joriy.Nasiyalar.Length, joriy.Qaytishlar.Length, joriy.Xarajatlar.Length, joriy.Korsatkichlar.Length));
        Assert.Equal("Bobur Aliyev", joriy.Qaytishlar[0].MijozIsmi);
        Assert.Equal(41, (await Oqi<SmenaTafsilotDto>(await admin.GetAsync("/smenalar/oxirgi"))).Smena.Id);

        // Operatorlar hisobi (Operatorlar.dc.html): Alisher 4 500 000 - 1 000 000 - 120 000; Dilshod 4 500 000 + 20 000.
        var operatorlar = (await admin.GetFromJsonAsync<FoydalanuvchiDto[]>("/operatorlar", Json))!.ToDictionary(f => f.Login);
        // Demo ma'lumot oktyabr uchun; API ishga tushganda MaoshYozuvchi joriy oy maoshini ham yozadi (noyabrdan boshlab) - u hisobga olinmaydi.
        static async Task<long> OktyabrQoldigi(HttpClient admin, int id)
        {
            var h = await Oqi<OperatorHisobDto>(await admin.GetAsync($"/operatorlar/{id}/hisob"));
            return h.Qoldiq - h.Harakatlar.Where(x => x.Turi == HarakatTuri.Maosh && x.Sana >= new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)).Sum(x => x.Summa);
        }
        Assert.Equal(3_380_000, await OktyabrQoldigi(admin, operatorlar["alisher"].Id));
        Assert.Equal(4_520_000, await OktyabrQoldigi(admin, operatorlar["dilshod"].Id));
    }

    [Fact]
    public async Task Nasiyalar_8TaDizayndagiSmenalargaBogLangan_Xulosa2510000va7ta()
    {
        var admin = await Admin();
        var n = await Oqi<NasiyalarDto>(await admin.GetAsync("/nasiyalar"));

        // Nasiyalar.dc.html: ism, yozilgan smena (#29 Alisher, #31 Dilshod, #41 Dilshod, #42 Alisher, ...), mashina, summa, qaytgan, muddat.
        Assert.Equal(
            [("Sherzod Qodirov", 29, "Alisher Karimov", "40 C 919 DA", 180_000L, 0L, "2026-09-28"),
             ("Bobur Aliyev", 31, "Dilshod Rahimov", "01 H 202 MA", 600_000L, 300_000L, "2026-09-30"),
             ("Komil Saidov", 41, "Dilshod Rahimov", "01 M 345 OA", 95_000L, 0L, "2026-10-05"),
             ("Farhod Ismoilov", 42, "Alisher Karimov", "30 B 456 CA", 220_000L, 0L, "2026-10-07"),
             ("Jasur To'xtayev", 42, "Alisher Karimov", "01 A 777 BC", 350_000L, 0L, "2026-10-11"),
             ("Nodir Xasanov", 39, "Dilshod Rahimov", "01 D 128 EA", 165_000L, 0L, "2026-10-15"),
             ("Rustam Ergashev", 40, "Alisher Karimov", "01 345 KBA", 1_200_000L, 0L, "2026-11-02"),
             ("Ulug'bek Nazarov", 39, "Dilshod Rahimov", "01 K 515 KA", 450_000L, 450_000L, "2026-10-08")],
            n.Royxat.Select(x => (x.MijozIsmi, x.SmenaId, x.OperatorIsmi, x.MashinaRaqami, x.Summa, x.Qaytgan, x.Muddat.ToString("yyyy-MM-dd"))));
        Assert.Equal("+998 97 700 80 90", n.Royxat[1].Telefon);
        Assert.Equal("Neksiya, oylikdan keyin", n.Royxat[1].Izoh);
        Assert.Equal(NasiyaHolati.Yopilgan, n.Royxat[7].Holati);
        Assert.NotNull(n.Royxat[7].Yopildi);

        // Faol qarz 2 510 000 (7 ta) - sanaga bog'liq emas; muddati o'tganlar esa "bugun"ga: ro'yxatdan hisoblangan qiymat bilan mos bo'lishi shart.
        Assert.Equal((2_510_000L, 7), (n.Xulosa.FaolQarz, n.Xulosa.FaolSoni));
        var otgan = n.Royxat.Where(x => x.Holati == NasiyaHolati.MuddatiOtgan).ToList();
        Assert.Equal((otgan.Sum(x => x.Qoldiq), otgan.Count), (n.Xulosa.MuddatiOtgan, n.Xulosa.MuddatiOtganSoni));
        if (Bugun <= new DateOnly(2026, 10, 5)) Assert.Equal((480_000L, 2), (n.Xulosa.MuddatiOtgan, n.Xulosa.MuddatiOtganSoni));    // Sherzod 180 000 + Bobur 300 000
        if (Bugun >= new DateOnly(2026, 10, 1) && Bugun < new DateOnly(2026, 11, 1))
            Assert.Equal((2_480_000L, 6, 750_000L, 2), (n.Xulosa.OyBerilgan, n.Xulosa.OyBerilganSoni, n.Xulosa.OyQaytgan, n.Xulosa.OyQaytganSoni));
        Assert.Single((await Oqi<NasiyalarDto>(await admin.GetAsync("/nasiyalar?holat=yopilgan"))).Royxat);
        Assert.Equal(7, (await Oqi<NasiyalarDto>(await admin.GetAsync("/nasiyalar?holat=faol"))).Royxat.Length);
    }

    [Fact]
    public async Task Boshqaruv_GrafikAparatlarVaOyKorsatkichlari()
    {
        var admin = await Admin();
        var b = await Oqi<BoshqaruvDto>(await admin.GetAsync("/boshqaruv"));
        Assert.Equal(42, b.JoriySmena!.Id);
        Assert.Equal((570_000L, 300_000L, 1_585_000L), (b.JoriySmena.NasiyaJami, b.JoriySmena.QaytganNasiya, b.JoriySmena.XarajatJami));
        Assert.Equal((41, 15_995_270L, 1198.40m, 0L), (b.OxirgiYopilgan!.Id, b.OxirgiYopilgan.Savdo, b.OxirgiYopilgan.JamiLitr, b.OxirgiYopilgan.Farq));
        Assert.Equal([41, 40, 39], b.OxirgiYopilganlar.Select(x => x.Id));
        Assert.Equal(Enumerable.Range(28, 14), b.OxirgiSmenalar.Select(x => x.Id));                                    // eskisidan yangisiga
        Assert.Equal([15.2, 16.8, 14.9, 15.6, 17.1, 16.2, 15.4, 16.9, 15.8, 16.4, 15.1, 16.1, 16.5, 16.0],
            b.OxirgiSmenalar.Select(x => Math.Round(x.Savdo / 1_000_000.0, 1)));                                        // dizayndagi grafik (mln so'm)
        Assert.Equal([6_840m, 3_215m, 4_120m, 1_960m, 9_480m], b.Aparatlar.Select(a => a.BakQoldiq));
        Assert.Equal([5_000m, 4_000m, 3_000m, 3_000m, 8_000m], b.Aparatlar.Select(a => a.OxirgiKirimLitr!.Value));

        if (Bugun >= new DateOnly(2026, 10, 1) && Bugun < new DateOnly(2026, 11, 1))
        {
            Assert.Equal((48_590_420L, 3640.10m, 3, 120_000L, 20_000L), (b.OySavdo, b.OyLitr, b.OySmenaSoni, b.OyKamomat, b.OyOrtiqcha));
            Assert.Equal((18_914_520L, 22_030_000L, 5_735_900L, 1_910_000L), (b.OyTolovlar.Naqd, b.OyTolovlar.Plastik, b.OyTolovlar.Depozit, b.OyTolovlar.Nasiya));
        }
    }

    [Fact]
    public async Task Tarix_HammaYopilganSmenadaSegment_ZanjirIzchil_BakHechQachonManfiyEmas()
    {
        var admin = await Admin();
        var smenalar = (await admin.GetFromJsonAsync<SmenaDto[]>("/smenalar", Json))!.OrderBy(s => s.Id).ToList();
        Assert.Equal(Enumerable.Range(28, 15), smenalar.Select(s => s.Id));
        Assert.Equal([42], smenalar.Where(s => s.Tugadi is null).Select(s => s.Id));                                    // bitta ochiq smena

        var aparatlar = await Aparatlar(admin);
        var tafsilotlar = new Dictionary<int, SmenaTafsilotDto>();
        foreach (var s in smenalar.Where(s => s.Tugadi is not null)) tafsilotlar[s.Id] = await Oqi<SmenaTafsilotDto>(await admin.GetAsync($"/smenalar/{s.Id}"));
        Assert.All(tafsilotlar.Values, t =>
        {
            Assert.Equal(5, t.Korsatkichlar.Length);                                                                     // har yopilgan smenada 5 aparat segmenti
            Assert.Equal((t.Smena.Savdo, t.Smena.JamiLitr), (t.Korsatkichlar.Sum(k => k.Summa), t.Korsatkichlar.Sum(k => k.Litr)));
            Assert.Equal(t.Smena.Kutilgan - t.Smena.SanalganNaqd!.Value, -t.Smena.Farq);
        });
        // Har segmentdagi Boshi = oldingi smena Oxiri; oxirgisi (#41) Oxiri = aparatning hozirgi pult ko'rsatkichi.
        for (var id = 29; id <= 41; id++)
            Assert.Equal(tafsilotlar[id - 1].Korsatkichlar.Select(k => k.Oxiri), tafsilotlar[id].Korsatkichlar.Select(k => k.Boshi));
        Assert.Equal(aparatlar.Select(a => a.TotalLitr), tafsilotlar[41].Korsatkichlar.Select(k => k.Oxiri));

        // Bak tarixi: yopilishlar (sotilgan litr) va kirimlar vaqt bo'yicha; hozirgi bakdan orqaga qaytarilgan boshlang'ich ham, har qadam ham >= 0.
        var kirimlar = (await admin.GetFromJsonAsync<BakKirimDto[]>("/bak-kirimlar", Json))!;
        Assert.Equal(5, kirimlar.Length);
        for (var i = 0; i < 5; i++)
        {
            var hodisalar = tafsilotlar.Values.Select(t => (Vaqt: t.Smena.Tugadi!.Value, Delta: -t.Korsatkichlar[i].Litr))
                .Concat(kirimlar.Where(k => k.AparatId == aparatlar[i].Id).Select(k => (k.Vaqt, Delta: k.Litr))).OrderBy(h => h.Vaqt).ToList();
            var daraja = aparatlar[i].BakQoldiq - hodisalar.Sum(h => h.Delta);
            Assert.True(daraja >= 0, $"{i + 1}-aparat boshlang'ich baki manfiy: {daraja}");
            foreach (var h in hodisalar)
            {
                daraja += h.Delta;
                Assert.True(daraja >= 0, $"{i + 1}-aparat baki {h.Vaqt:u} da manfiy: {daraja}");
            }
            Assert.Equal(aparatlar[i].BakQoldiq, daraja);
        }
        var dizel = kirimlar.Single(k => k.AparatRaqam == 5);
        Assert.Equal((8_000m, 1_480m, 9_480m, "yuk xati 1176"), (dizel.Litr, dizel.QoldiqOldin, dizel.QoldiqKeyin, dizel.Hujjat));   // 04.10 yozilgan: "bak 9 480 L bo'ldi"
    }
}

/// <summary>Demo ma'lumot faqat dev/web muhitida yoziladi (Seed:DemoMalumot=true bo'lsa ham "Testing"da - yo'q).</summary>
public sealed class WebMuhitiDemoTests : ApiBaza
{
    protected override string Muhit => "Web";
    protected override bool DemoSozlamasi => true;

    [Fact]
    public async Task WebMuhitida_DemoYoziladi()
    {
        var admin = await Admin();
        var joriy = await Oqi<SmenaTafsilotDto>(await admin.GetAsync("/smenalar/joriy"));
        Assert.Equal((42, 2, 2), (joriy.Smena.Id, joriy.Nasiyalar.Length, joriy.Xarajatlar.Length));
        Assert.Equal(15, (await admin.GetFromJsonAsync<SmenaDto[]>("/smenalar", Json))!.Length);
    }
}

public sealed class TestingMuhitiDemoSozlamasiTests : ApiBaza
{
    protected override bool DemoSozlamasi => true;                            // muhit "Testing": demo yozilmaydi

    [Fact]
    public async Task TestingMuhitida_DemoYozilmaydi()
    {
        var admin = await Admin();
        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await admin.GetAsync("/smenalar/joriy")).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<SmenaDto[]>("/smenalar", Json))!);
    }
}

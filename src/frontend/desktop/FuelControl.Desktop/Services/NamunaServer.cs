using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using FuelControl.Contracts.Dto;
using FuelControl.Desktop.Models;

namespace FuelControl.Desktop.Services;

/// <summary>
/// Namuna (demo) server: dizayndagi raqamlar bilan (smena #39–#42, 5 aparat, nasiyalar, xarajatlar). Server tayyor bo'lmaganda
/// va sinov harness'ida ishlatiladi: FUELCONTROL_NAMUNA=1 yoki ApiMijoz.Namuna = new NamunaServer().
/// Yozuvchi so'rovlar xotirada bajariladi (smena yopish formulasi server bilan bir xil — SmenaHisobi).
/// </summary>
public sealed class NamunaServer : INamunaServer
{
    private static DateTime Utc(DateOnly kun, int soat, int daqiqa) =>
        kun.ToDateTime(new TimeOnly(soat, daqiqa), DateTimeKind.Local).ToUniversalTime();

    private readonly DateOnly _bugun = DateOnly.FromDateTime(DateTime.Today);
    private readonly List<FoydalanuvchiDto> _foydalanuvchilar;
    private readonly List<YoqilgiTuriDto> _yoqilgilar;
    private readonly List<AparatDto> _aparatlar;
    private readonly List<SmenaDto> _smenalar = new();
    private readonly Dictionary<int, List<SmenaKorsatkichDto>> _korsatkichlar = new();
    private readonly List<NasiyaDto> _nasiyalar = new();
    private readonly List<NasiyaQaytishiDto> _qaytishlar = new();
    private readonly List<XarajatDto> _xarajatlar = new();
    private readonly List<AuditYozuviDto> _audit = new();
    private readonly List<HisobHarakatiDto> _harakatlar = new();
    private int _keyingiId = 1000;
    private int _joriyFoydalanuvchi = 2;

    public NamunaServer()
    {
        var b = _bugun;
        Ruxsat[] operatorR = [.. Ruxsatlar.Standart(Rol.Operator)];
        _foydalanuvchilar =
        [
            new(1, "Administrator", "admin", Rol.Admin, true, 0, [.. Ruxsatlar.Standart(Rol.Admin)]),
            new(2, "Alisher Karimov", "alisher", Rol.Operator, true, 4_000_000, operatorR),
            new(3, "Dilshod Rahimov", "dilshod", Rol.Operator, true, 4_000_000, operatorR),
        ];
        _yoqilgilar =
        [
            new(1, "AI-92", 12_200, "#2563EB", true),
            new(2, "AI-95", 15_500, "#7C3AED", true),
            new(3, "Dizel", 13_800, "#CA8A04", true),
        ];
        _aparatlar =
        [
            new(1, 1, 1, "AI-92", 184_230.50m, 6_840m, Utc(b.AddDays(-2), 9, 30), 5_000m, null),
            new(2, 2, 1, "AI-92", 97_410.00m, 3_215m, Utc(b.AddDays(-6), 10, 0), 4_000m, null),
            new(3, 3, 2, "AI-95", 63_118.20m, 4_120m, Utc(b.AddDays(-3), 11, 0), 3_000m, null),
            new(4, 4, 2, "AI-95", 41_902.75m, 1_960m, Utc(b.AddDays(-9), 14, 0), 3_000m, null),
            new(5, 5, 3, "Dizel", 120_560.00m, 9_480m, Utc(b.AddDays(-1), 16, 30), 8_000m, null),
        ];

        // Yopilgan smenalar #30–#41 (eskisidan yangisiga), har biri bir sutka.
        var rnd = new Random(42);
        for (int i = 30; i <= 41; i++)
        {
            var kun = b.AddDays(i - 42);
            var op = i % 2 == 0 ? 2 : 3;
            long savdo = i == 41 ? 15_995_270 : 14_000_000 + rnd.Next(0, 30) * 100_000 + rnd.Next(0, 999) * 10;
            decimal litr = i == 41 ? 1_198.40m : Math.Round(savdo / 13_400m, 2);
            long farq = i switch { 41 => 0, 40 => -32_000, 37 => -18_500, 35 => 12_000, _ => 0 };
            long plastik = savdo * 45 / 100 / 1000 * 1000, depFarq = 1_800_000, nasiya = 300_000, qaytgan = 100_000, xarajat = 250_000;
            long kutilgan = 100_000 + savdo + qaytgan - plastik - depFarq - nasiya - xarajat;
            _smenalar.Add(new SmenaDto(i, op, Ism(op), Utc(kun, 8, 2), Utc(kun.AddDays(1), 8, 1),
                100_000, 200_000, 1_250_000, 200_000 + plastik, 1_250_000 + depFarq, kutilgan + farq,
                litr, savdo, plastik, depFarq, nasiya, qaytgan, xarajat, kutilgan, farq, null, []));
        }

        // Ochiq smena #42 — dizayndagi Main / SmenaYopish holati.
        _smenalar.Add(new SmenaDto(42, 2, "Alisher Karimov", Utc(b, 8, 2), null, 100_000, 200_000, 1_250_000,
            null, null, null, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, []));

        // Nasiyalar.dc.html dagi 8 ta mijoz (bugun = dizayndagi 04.10).
        _nasiyalar.Add(Nasiya(88, 29, 2, "Sherzod Qodirov", "+998 99 410 22 11", "40 C 919 DA", 180_000, 0, b.AddDays(-6), Utc(b.AddDays(-13), 18, 40), null));
        _nasiyalar.Add(Nasiya(90, 31, 3, "Bobur Aliyev", "+998 97 700 80 90", "01 H 202 MA", 600_000, 300_000, b.AddDays(-4), Utc(b.AddDays(-11), 15, 10), "Neksiya, oylikdan keyin"));
        _nasiyalar.Add(Nasiya(97, 41, 3, "Komil Saidov", "+998 94 222 31 31", "01 M 345 OA", 95_000, 0, b.AddDays(1), Utc(b.AddDays(-1), 19, 0), null));
        _nasiyalar.Add(Nasiya(102, 42, 2, "Farhod Ismoilov", "+998 93 555 12 34", "30 B 456 CA", 220_000, 0, b.AddDays(3), Utc(b, 9, 47), null));
        _nasiyalar.Add(Nasiya(101, 42, 2, "Jasur To'xtayev", "+998 90 123 45 67", "01 A 777 BC", 350_000, 0, b.AddDays(7), Utc(b, 9, 5), "Damas, hafta oxirida to'laydi"));
        _nasiyalar.Add(Nasiya(94, 39, 3, "Nodir Xasanov", "+998 88 640 07 07", "01 D 128 EA", 165_000, 0, b.AddDays(11), Utc(b.AddDays(-3), 11, 30), null));
        _nasiyalar.Add(Nasiya(95, 40, 2, "Rustam Ergashev", "+998 90 909 30 30", "01 345 KBA", 1_200_000, 0, b.AddDays(29), Utc(b.AddDays(-2), 16, 0), null));
        _nasiyalar.Add(Nasiya(93, 39, 3, "Ulug'bek Nazarov", "+998 91 333 44 55", "01 K 515 KA", 450_000, 450_000, b.AddDays(4), Utc(b.AddDays(-3), 10, 0), null));
        _qaytishlar.Add(new NasiyaQaytishiDto(201, 90, "Bobur Aliyev", 42, 300_000, TolovTuri.Naqd, Utc(b, 11, 20), "Alisher Karimov", null, 2, false));
        _qaytishlar.Add(new NasiyaQaytishiDto(195, 93, "Ulug'bek Nazarov", 40, 450_000, TolovTuri.Plastik, Utc(b.AddDays(-2), 13, 0), "Alisher Karimov", null, 2, false));
        _xarajatlar.Add(new XarajatDto(301, 42, 85_000, "Lampochka va tozalash vositasi", XarajatManbai.Kassa, Utc(b, 10, 15), "Alisher Karimov", 2));
        _xarajatlar.Add(new XarajatDto(302, 42, 1_500_000, "Boshliq naqd oldi", XarajatManbai.Kassa, Utc(b, 12, 40), "Alisher Karimov", 2));

        Audit("Alisher Karimov", "Xarajat yozildi", "Boshliq naqd oldi · 1 500 000 · kassadan · smena #42", "xarajat", Utc(b, 12, 40));
        Audit("Alisher Karimov", "Qarz qaytdi", "Bobur Aliyev · 300 000 naqd · qolgan qarz 300 000", "nasiya", Utc(b, 11, 20));
        Audit("Alisher Karimov", "Xarajat yozildi", "Lampochka va tozalash vositasi · 85 000 · kassadan", "xarajat", Utc(b, 10, 15));
        Audit("Alisher Karimov", "Nasiya yozildi", "Farhod Ismoilov · 30 B 456 CA · 220 000", "nasiya", Utc(b, 9, 47));
        Audit("Alisher Karimov", "Nasiya yozildi", "Jasur To'xtayev · 01 A 777 BC · 350 000", "nasiya", Utc(b, 9, 5));
        Audit("Alisher Karimov", "Smena ochildi", "#42 · qaytim puli 100 000 · terminal 200 000 · depozit 1 250 000", "smena", Utc(b, 8, 2));
        Audit("Dilshod Rahimov", "Smena yopildi", "#41 · savdo 15 995 270 · 1 198.40 L · farq yo'q", "smena", Utc(b, 8, 1));
        Audit("Administrator", "Bakka kirim", "5-aparat, Dizel · +8 000 L · bak 9 480 L bo'ldi · yuk xati 1176", "bak", Utc(b.AddDays(-1), 16, 30));
        Audit("Administrator", "Ko'rsatkich tuzatildi", "Smena #40 · 3-aparat yangi ko'rsatkich 62 949.60 dan 62 946.90 ga · sabab: yozishda xato", "tuzatish", Utc(b.AddDays(-1), 9, 12));
        Audit("Administrator", "Avans berildi", "Alisher Karimov · 1 000 000 · naqd", "hisob", Utc(b.AddDays(-2), 14, 10));

        var oyBoshi = new DateTime(b.Year, b.Month, 1, 9, 0, 0, DateTimeKind.Local).ToUniversalTime();
        foreach (var op in new[] { 2, 3 })
        {
            _harakatlar.Add(new HisobHarakatiDto(_keyingiId++, op, oyBoshi, HarakatTuri.Maosh, 4_000_000, "Oylik maosh", "Tizim"));
            _harakatlar.Add(new HisobHarakatiDto(_keyingiId++, op, oyBoshi.AddDays(1), HarakatTuri.Avans, -1_000_000, "Naqd", "Administrator"));
        }
    }

    private string Ism(int id) => _foydalanuvchilar.First(f => f.Id == id).ToliqIsm;

    private NasiyaDto Nasiya(int id, int smenaId, int op, string mijoz, string tel, string raqam, long summa, long qaytgan, DateOnly muddat, DateTime yozildi, string? izoh, bool eski = false)
    {
        var qoldiq = summa - qaytgan;
        var kun = muddat.DayNumber - _bugun.DayNumber;
        var holat = qoldiq == 0 ? NasiyaHolati.Yopilgan : kun < 0 ? NasiyaHolati.MuddatiOtgan : NasiyaHolati.Faol;
        return new NasiyaDto(id, smenaId, Ism(op), mijoz, tel, raqam, summa, qaytgan, qoldiq, muddat, holat, kun, yozildi,
            qoldiq == 0 ? yozildi.AddDays(1) : null, izoh, op, eski);
    }

    private void Audit(string kim, string amal, string tafsilot, string tur, DateTime? vaqt = null) =>
        _audit.Insert(0, new AuditYozuviDto(_keyingiId++, vaqt ?? DateTime.UtcNow, kim, amal, tafsilot, tur));

    private SmenaDto? Ochiq => _smenalar.FirstOrDefault(s => s.Tugadi is null);

    /// <summary>Ochiq smenada nasiya/qaytish/xarajat yig'indilari joriy bo'lsin.</summary>
    private SmenaDto Yigindili(SmenaDto s) => s.Tugadi is not null ? s : s with
    {
        NasiyaJami = _nasiyalar.Where(n => n.SmenaId == s.Id && !n.EskiQarz).Sum(n => n.Summa),
        QaytganNasiya = _qaytishlar.Where(q => q.SmenaId == s.Id).Sum(q => q.Summa),
        XarajatJami = _xarajatlar.Where(x => x.SmenaId == s.Id).Sum(x => x.Summa),
    };

    private SmenaTafsilotDto Tafsilot(SmenaDto s)
    {
        s = Yigindili(s);
        var k = _korsatkichlar.TryGetValue(s.Id, out var l) ? l.ToArray()
            : s.Tugadi is null ? [] : _aparatlar.Select(a => Segment(a, a.TotalLitr - s.JamiLitr / 5, a.TotalLitr, false)).ToArray();
        return new SmenaTafsilotDto(s, k,
            _nasiyalar.Where(n => n.SmenaId == s.Id && !n.EskiQarz).ToArray(),
            _qaytishlar.Where(q => q.SmenaId == s.Id).ToArray(),
            _xarajatlar.Where(x => x.SmenaId == s.Id).ToArray());
    }

    private SmenaKorsatkichDto Segment(AparatDto a, decimal boshi, decimal oxiri, bool narxOzgarishida, long? narx = null)
    {
        var n = narx ?? _yoqilgilar.First(y => y.Id == a.YoqilgiTuriId).Narx;
        var litr = SmenaHisobi.Litr(boshi, oxiri);
        return new SmenaKorsatkichDto(a.Id, a.Raqam, a.YoqilgiNomi, boshi, oxiri, n, litr, SmenaHisobi.Summa(litr, n), narxOzgarishida);
    }

    private static Dictionary<string, string> Sorov(string yol)
    {
        var d = new Dictionary<string, string>();
        var i = yol.IndexOf('?');
        if (i < 0) return d;
        foreach (var q in yol[(i + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = q.Split('=', 2);
            d[p[0]] = p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "";
        }
        return d;
    }

    private static ApiXatosi Xato(string matn, int status = 400) => new(matn, status);

    public object? Javob(HttpMethod usul, string yol, object? tana)
    {
        var q = Sorov(yol);
        var y = yol.Split('?')[0].TrimEnd('/');
        var s = y.Split('/');
        var get = usul == HttpMethod.Get;

        switch (s[0])
        {
            case "auth":
                var login = ((LoginSoroviDto)tana!).Login;
                var f = _foydalanuvchilar.FirstOrDefault(x => x.Login == login) ?? throw Xato("Login yoki parol noto'g'ri", 400);
                _joriyFoydalanuvchi = f.Id;
                return new LoginJavobiDto("namuna-token", DateTime.UtcNow.AddHours(12), f);
            case "me":
                return _foydalanuvchilar.First(x => x.Id == _joriyFoydalanuvchi);
            case "foydalanuvchilar" when get:
                return _foydalanuvchilar.ToList();
            case "operatorlar" when s.Length == 1:
                return _foydalanuvchilar.Where(x => x.Rol == Rol.Operator).ToList();
            case "operatorlar" when s.Length == 3 && s[2] == "hisob":
                var opId = int.Parse(s[1]);
                var h = _harakatlar.Where(x => x.OperatorId == opId).OrderByDescending(x => x.Sana).ToArray();
                var oySmena = _smenalar.Where(x => x.OperatorId == opId && x.Tugadi is not null && x.Boshlandi.ToLocalTime().Month == _bugun.Month).ToList();
                return new OperatorHisobDto(opId, Ism(opId), 4_000_000, h.Sum(x => x.Summa), h.Sum(x => x.Summa), oySmena.Sum(x => x.Savdo), oySmena.Count, h);
            case "yoqilgilar" when s.Length == 2 && s[1] == "narx-tarixi":
                return new List<NarxTarixiDto> { new(Utc(_bugun.AddDays(-20), 9, 0), "AI-92", 11_900, 12_200, "Administrator") };
            case "yoqilgilar" when get:
                return _yoqilgilar.ToList();
            case "yoqilgilar" when usul == HttpMethod.Put:
                return NarxOzgartir(int.Parse(s[1]), (YoqilgiTahrirlashDto)tana!);
            case "aparatlar" when get:
                return _aparatlar.ToList();
            case "aparatlar" when usul == HttpMethod.Post && s.Length == 1:
                return AparatYarat((AparatYaratishDto)tana!);
            case "aparatlar" when usul == HttpMethod.Put && s.Length == 2:
                return AparatTahrirla(int.Parse(s[1]), (AparatTahrirlashDto)tana!);
            case "aparatlar" when s.Length == 3 && s[2] == "kirim":
                return BakKirim(int.Parse(s[1]), (BakKirimYaratishDto)tana!);
            case "smenalar" when get && s.Length == 1:
                var dan = q.TryGetValue("dan", out var d) ? DateOnly.Parse(d) : DateOnly.MinValue;
                return _smenalar.Where(x => DateOnly.FromDateTime(x.Boshlandi.ToLocalTime()) >= dan)
                    .Where(x => !q.TryGetValue("operatorId", out var o) || x.OperatorId == int.Parse(o))
                    .OrderByDescending(x => x.Boshlandi).Select(Yigindili).ToList();
            case "smenalar" when get && s[1] == "joriy":
                return Ochiq is { } js ? Tafsilot(js) : null;
            case "smenalar" when get && s[1] == "oxirgi" && s.Length == 3 && s[2] == "topshirish":
                return _smenalar.Where(x => x.Tugadi is not null).MaxBy(x => x.Tugadi) is { } tx
                    ? new SmenaTopshirishDto(tx.Id, tx.OperatorId, tx.OperatorIsmi, tx.Tugadi!.Value, tx.YopishDepozit) : null;
            case "smenalar" when get && s[1] == "oxirgi":
            {
                // §8.10: to'liq natija faqat boshliq (Smenalar ruxsati) yoki smena egasiga
                if (_smenalar.Where(x => x.Tugadi is not null).MaxBy(x => x.Tugadi) is not { } ox) return null;
                var men = _foydalanuvchilar.First(f => f.Id == _joriyFoydalanuvchi);
                if (!men.Ruxsatlar.Contains(Ruxsat.Smenalar) && ox.OperatorId != men.Id) throw Xato("Ruxsat yo'q", 403);
                return Tafsilot(ox);
            }
            case "smenalar" when get:
                return Tafsilot(_smenalar.First(x => x.Id == int.Parse(s[1])));
            case "smenalar" when s[1] == "och":
                return SmenaOch((SmenaOchishDto)tana!);
            case "smenalar" when s.Length == 3 && s[2] == "yop":
            {
                var yopildi = SmenaYop(int.Parse(s[1]), (SmenaYopishDto)tana!);
                ZaxiraOl("smena", yopildi.Id);   // §8.17: smena yopilganda nusxa
                return yopildi;
            }
            case "smenalar" when usul == HttpMethod.Put && s.Length == 3 && s[2] == "narx-korsatkichi":
                return NarxKorsatkichiniTuzat(int.Parse(s[1]), (KorsatkichTuzatishDto)tana!);
            case "nasiyalar" when get && s.Length == 2 && s[1] == "mijozlar":
                return MijozTakliflari(q.GetValueOrDefault("q"));
            case "nasiyalar" when get && s.Length == 1:
                return Nasiyalar(q.GetValueOrDefault("holat"), q.GetValueOrDefault("q"), q.TryGetValue("operatorId", out var opq) ? int.Parse(opq) : null);
            case "nasiyalar" when get:
                var nid = int.Parse(s[1]);
                return new NasiyaTafsilotDto(_nasiyalar.First(x => x.Id == nid), _qaytishlar.Where(x => x.NasiyaId == nid).ToArray());
            case "nasiyalar" when usul == HttpMethod.Post && s.Length == 1:
                return NasiyaYoz((NasiyaYaratishDto)tana!);
            case "nasiyalar" when usul == HttpMethod.Post && s.Length == 3:
                return QarzQaytdi(int.Parse(s[1]), (NasiyaQaytishiYaratishDto)tana!);
            case "nasiyalar" when usul == HttpMethod.Post && s.Length == 2 && s[1] == "eski-qarz":
                return EskiQarz((EskiQarzYaratishDto)tana!);
            case "nasiyalar" when usul == HttpMethod.Delete:
            {
                // §8.11: eski qarz juftligi (nasiya + qaytishi) qaysi tomondan o'chirilsa ham birga o'chadi
                var nId = s[1] == "qaytishlar" ? _qaytishlar.FirstOrDefault(x => x.Id == int.Parse(s[2]))?.NasiyaId : int.Parse(s[1]);
                var eski = _nasiyalar.Any(x => x.Id == nId && x.EskiQarz);
                if (s[1] == "qaytishlar") _qaytishlar.RemoveAll(x => x.Id == int.Parse(s[2]));
                if (s[1] != "qaytishlar" || eski)
                {
                    if (!eski && _qaytishlar.Any(x => x.NasiyaId == nId)) throw Xato("Qaytishlari bor nasiyani o'chirib bo'lmaydi", 409);
                    _qaytishlar.RemoveAll(x => x.NasiyaId == nId);
                    _nasiyalar.RemoveAll(x => x.Id == nId);
                }
                return null;
            }
            case "xarajatlar" when get:
                return _xarajatlar.Where(x => !q.TryGetValue("smenaId", out var sid) || x.SmenaId == int.Parse(sid)).ToList();
            case "xarajatlar" when usul == HttpMethod.Post:
                var xd = (XarajatYaratishDto)tana!;
                var smena = Ochiq ?? throw Xato("Ochiq smena yo'q");
                var x = new XarajatDto(_keyingiId++, smena.Id, xd.Summa, xd.Sabab, xd.Manba, DateTime.UtcNow, Ism(_joriyFoydalanuvchi), _joriyFoydalanuvchi);
                _xarajatlar.Add(x);
                Audit(Ism(_joriyFoydalanuvchi), "Xarajat yozildi", $"{xd.Sabab} · {Format.Pul(xd.Summa)}", "xarajat");
                return x;
            case "xarajatlar" when usul == HttpMethod.Delete:
                _xarajatlar.RemoveAll(x2 => x2.Id == int.Parse(s[1]));
                return null;
            case "bak-kirimlar":
                return new List<BakKirimDto>();
            case "audit" when s.Length == 1:
                return _audit.Where(a => !q.TryGetValue("tur", out var t) || a.Tur == t).ToList();
            case "zaxira" when usul == HttpMethod.Post && s.Length == 1:
                return ZaxiraOl("qolda", null);
            case "zaxira" when get && s.Length == 2 && s[1] == "holat":
                return ZaxiraHolati();
            case "zaxira" when usul == HttpMethod.Put && s.Length == 2 && s[1] == "telegram":
                return TelegramTahrir((ZaxiraTelegramTahrirDto)tana!);
            case "zaxira" when usul == HttpMethod.Post && s.Length == 3 && s[1] == "telegram" && s[2] == "sinov":
                return TelegramSinov();
            case "boshqaruv":
                return Boshqaruv();
            case "hisobot":
                return Hisobot(q);
            default:
                return null;
        }
    }

    // ================= §8.17 Zaxira nusxa =================
    /// <summary>Sinov uchun: true bo'lsa sozlangan papka (E:\baza) "ishlamaydi" — nusxalar standart papkaga tushadi.</summary>
    public bool ZaxiraPapkaIshlamaydi { get; set; }
    private const string SozlanganPapka = @"E:\baza", StandartPapka = @"C:\ProgramData\FuelControl\zaxira";
    private readonly List<ZaxiraNusxasiDto> _nusxalar = new();
    private string? _tgToken, _tgChat;
    private bool _tgYoqilgan;
    private (DateTime Vaqt, bool Ok, string Xabar, string Turi)? _tgOxirgi;
    private bool TgFaol => _tgYoqilgan && _tgToken is not null && _tgChat is not null;

    private void ZaxiraNamunasi()
    {
        var b = _bugun;
        foreach (var (kun, soat, sabab, smena, hajm) in new (int, int, string, int?, long)[]
                 { (0, 8, "smena", 41, 2_318_336), (-1, 3, "kunlik", null, 2_301_952), (-1, 8, "smena", 40, 2_297_856), (-2, 3, "kunlik", null, 2_289_664), (-9, 3, "", null, 1_912_832) })
        {
            var v = Utc(b.AddDays(kun), soat, 5);
            _nusxalar.Add(new ZaxiraNusxasiDto($"fuelcontrol-{v.ToLocalTime():yyyyMMdd-HHmmss}{(sabab.Length > 0 ? "-" + sabab : "")}{(smena is { } n ? "-" + n : "")}.db",
                v, hajm, sabab, smena, sabab.Length == 0));
        }
    }

    private ZaxiraJavobiDto ZaxiraOl(string sabab, int? smenaId)
    {
        if (_nusxalar.Count == 0) ZaxiraNamunasi();
        var v = DateTime.UtcNow;
        var nomi = $"fuelcontrol-{v.ToLocalTime():yyyyMMdd-HHmmss}-{sabab}{(smenaId is { } n ? "-" + n : "")}.db";
        var hajm = 2_318_336L + _nusxalar.Count * 4096;
        _nusxalar.Insert(0, new ZaxiraNusxasiDto(nomi, v, hajm, sabab, smenaId, ZaxiraPapkaIshlamaydi));
        if (_nusxalar.Count > 30) _nusxalar.RemoveRange(30, _nusxalar.Count - 30);
        if (sabab == "qolda") Audit(Ism(_joriyFoydalanuvchi), "Zaxira nusxa olindi", nomi, "zaxira");
        var yuboriladi = TgFaol;
        if (yuboriladi) _tgOxirgi = _tgChat == "-1"
            ? (v, false, "Bad Request: chat not found", "nusxa")
            : (v, true, $"{nomi}.gz · {hajm / 3 / 1024} KB", "nusxa");
        return new ZaxiraJavobiDto(nomi, hajm, v, sabab, ZaxiraPapkaIshlamaydi ? StandartPapka : SozlanganPapka, ZaxiraPapkaIshlamaydi, yuboriladi);
    }

    private ZaxiraHolatiDto ZaxiraHolati()
    {
        if (_nusxalar.Count == 0) ZaxiraNamunasi();
        var o = _tgOxirgi;
        var maska = _tgToken is { } t ? "…" + t[^4..] : null;
        return new ZaxiraHolatiDto(ZaxiraPapkaIshlamaydi ? StandartPapka : SozlanganPapka, SozlanganPapka, ZaxiraPapkaIshlamaydi,
            ZaxiraPapkaIshlamaydi ? "disk ulanmagan" : null, 30, _nusxalar.ToArray(),
            new ZaxiraTelegramDto(_tgYoqilgan, TgFaol, maska, _tgChat, o?.Vaqt, o?.Ok, o?.Xabar, o?.Turi));
    }

    private ZaxiraHolatiDto TelegramTahrir(ZaxiraTelegramTahrirDto d)
    {
        if (d.Token is { Length: > 0 } t && !System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d{5,}:[A-Za-z0-9_-]{20,}$"))
            throw Xato("Bot tokeni noto'g'ri formatda (masalan 123456789:AAH...)");
        if (d.ChatId is { Length: > 0 } c && !System.Text.RegularExpressions.Regex.IsMatch(c, @"^(-?\d+|@[A-Za-z0-9_]{4,})$"))
            throw Xato("Chat ID noto'g'ri: son (guruhda manfiy) yoki @kanal bo'lishi kerak");
        if (d.Token is not null)
        {
            Audit(Ism(_joriyFoydalanuvchi), d.Token.Length == 0 ? "Telegram tokeni o'chirildi" : "Telegram tokeni o'zgartirildi", "", "sozlama");
            _tgToken = d.Token.Length == 0 ? null : d.Token;
        }
        if (d.ChatId is not null) _tgChat = d.ChatId.Length == 0 ? null : d.ChatId;
        if (d.Yoqilgan is { } y) _tgYoqilgan = y;
        return ZaxiraHolati();
    }

    private ZaxiraHolatiDto TelegramSinov()
    {
        if (_tgToken is null) throw Xato("Bot tokeni kiritilmagan");
        if (_tgChat is null) throw Xato("Chat ID kiritilmagan");
        _tgOxirgi = _tgChat == "-1"
            ? (DateTime.UtcNow, false, "Bad Request: chat not found", "sinov")
            : (DateTime.UtcNow, true, "Sinov xabari", "sinov");
        return ZaxiraHolati();
    }

    private SmenaDto SmenaOch(SmenaOchishDto d)
    {
        if (Ochiq is not null) throw Xato("Ochiq smena bor", 409);
        var smena = new SmenaDto(_smenalar.Max(x => x.Id) + 1, _joriyFoydalanuvchi, Ism(_joriyFoydalanuvchi), DateTime.UtcNow, null,
            d.Qaytim, d.Terminal, d.Depozit, null, null, null, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, []);
        _smenalar.Add(smena);
        Audit(Ism(_joriyFoydalanuvchi), "Smena ochildi", $"#{smena.Id}", "smena");
        return smena;
    }

    private SmenaDto SmenaYop(int id, SmenaYopishDto d)
    {
        var s = Yigindili(_smenalar.First(x => x.Id == id));
        var oldingi = _korsatkichlar.TryGetValue(id, out var qayd) ? qayd : new List<SmenaKorsatkichDto>();
        var segmentlar = new List<SmenaKorsatkichDto>(oldingi);
        foreach (var a in _aparatlar)
        {
            var k = d.Korsatkichlar.FirstOrDefault(x => x.AparatId == a.Id) ?? throw Xato($"{a.Raqam}-aparat ko'rsatkichi yo'q");
            var boshi = SmenaHisobi.Oldingi(a, oldingi);
            if (k.Qiymat < boshi) throw Xato($"{a.Raqam}-aparat: yangi ko'rsatkich oldingisidan kichik");
            segmentlar.Add(Segment(a, boshi, k.Qiymat, false));
        }
        var natija = SmenaHisobi.Hisobla(s.OchishQaytim, s.OchishTerminal, s.OchishDepozit, segmentlar.Sum(x => x.Summa),
            s.QaytganNasiya, s.NasiyaJami, s.XarajatJami, d.Terminal, d.Depozit, d.SanalganNaqd);
        var yopilgan = s with
        {
            Tugadi = DateTime.UtcNow, YopishTerminal = d.Terminal, YopishDepozit = d.Depozit, SanalganNaqd = d.SanalganNaqd,
            JamiLitr = segmentlar.Sum(x => x.Litr), Savdo = natija.Savdo, Plastik = natija.Plastik, DepozitFarqi = natija.DepozitFarqi,
            Kutilgan = natija.Kutilgan, Farq = natija.Farq, Izoh = d.Izoh, PlastikSummalari = d.PlastikSummalari ?? [],
        };
        _smenalar[_smenalar.FindIndex(x => x.Id == id)] = yopilgan;
        _korsatkichlar[id] = segmentlar;
        for (int i = 0; i < _aparatlar.Count; i++)
        {
            var a = _aparatlar[i];
            var sotildi = segmentlar.Where(x => x.AparatId == a.Id).Sum(x => x.Litr);
            _aparatlar[i] = a with { TotalLitr = d.Korsatkichlar.First(x => x.AparatId == a.Id).Qiymat, BakQoldiq = a.BakQoldiq - sotildi };
        }
        if (natija.Farq != 0)
            _harakatlar.Add(new HisobHarakatiDto(_keyingiId++, s.OperatorId, DateTime.UtcNow,
                natija.Farq < 0 ? HarakatTuri.Kamomat : HarakatTuri.Ortiqcha, natija.Farq, $"Smena #{id}", "Tizim"));
        Audit(Ism(_joriyFoydalanuvchi), "Smena yopildi", $"#{id} · savdo {Format.Pul(natija.Savdo)} · farq {Format.Farq(natija.Farq)}", "smena");
        return yopilgan;
    }

    /// <summary>§8.16: KrugHajmi — yaratishda null/0 = krug yo'q; tahrirda null = o'zgarmaydi, 0 = o'chiriladi, &gt; 0 = o'rnatiladi; manfiy — 400.</summary>
    private AparatDto AparatYarat(AparatYaratishDto d)
    {
        if (d.KrugHajmi < 0) throw Xato("Krug hajmi manfiy bo'lishi mumkin emas");
        if (_aparatlar.Any(a => a.Raqam == d.Raqam)) throw Xato("Bu raqamli aparat allaqachon bor", 409);
        var y = _yoqilgilar.First(x => x.Id == d.YoqilgiTuriId);
        var a = new AparatDto(_aparatlar.Max(x => x.Id) + 1, d.Raqam, y.Id, y.Nomi, d.BoshlangichTotalLitr, d.BoshlangichBakQoldiq, null, null,
            d.KrugHajmi is > 0 ? d.KrugHajmi : null);
        _aparatlar.Add(a);
        Audit(Ism(_joriyFoydalanuvchi), "Aparat qo'shildi", $"{a.Raqam}-aparat · {y.Nomi}{KrugAudit(a.KrugHajmi)}", "sozlama");
        return a;
    }

    private AparatDto AparatTahrirla(int id, AparatTahrirlashDto d)
    {
        if (d.KrugHajmi < 0) throw Xato("Krug hajmi manfiy bo'lishi mumkin emas");
        var i = _aparatlar.FindIndex(x => x.Id == id);
        if (i < 0) throw Xato("Aparat topilmadi", 404);
        var eski = _aparatlar[i];
        var pultOzgardi = d.TotalLitr is { } t && t != eski.TotalLitr;
        if (pultOzgardi && Ochiq is not null) throw Xato("Ochiq smenada pult ko'rsatkichini tuzatib bo'lmaydi", 409);
        if ((pultOzgardi || d.BakQoldiq is { } b0 && b0 != eski.BakQoldiq) && string.IsNullOrWhiteSpace(d.Sabab)) throw Xato("Sabab majburiy");
        var y = _yoqilgilar.First(x => x.Id == d.YoqilgiTuriId);
        var krug = d.KrugHajmi switch { null => eski.KrugHajmi, 0 => null, var h => h };
        var yangi = eski with
        {
            Raqam = d.Raqam, YoqilgiTuriId = y.Id, YoqilgiNomi = y.Nomi,
            TotalLitr = d.TotalLitr ?? eski.TotalLitr, BakQoldiq = d.BakQoldiq ?? eski.BakQoldiq, KrugHajmi = krug,
        };
        _aparatlar[i] = yangi;
        Audit(Ism(_joriyFoydalanuvchi), "Aparat o'zgartirildi", $"{yangi.Raqam}-aparat{KrugAudit(krug)}{(d.Sabab is { } sb ? " · sabab: " + sb : "")}", "sozlama");
        return yangi;
    }

    private static string KrugAudit(decimal? h) => h is { } v ? $" · krug {Format.ButunLitr(v)} L" : " · krug yo'q";

    /// <summary>§8.14: ochiq smenada aparatning oxirgi narx o'zgarishi segmentini tuzatish (server qoidalari bilan bir xil).</summary>
    private SmenaTafsilotDto NarxKorsatkichiniTuzat(int smenaId, KorsatkichTuzatishDto d)
    {
        if (Ochiq is not { } s || s.Id != smenaId) throw Xato("Smena ochiq emas", 409);
        var a = _aparatlar.FirstOrDefault(x => x.Id == d.AparatId) ?? throw Xato("Aparat topilmadi", 404);
        if (string.IsNullOrWhiteSpace(d.Sabab)) throw Xato("Sabab majburiy");
        var l = _korsatkichlar.TryGetValue(s.Id, out var x0) ? x0 : new List<SmenaKorsatkichDto>();
        var i = l.FindLastIndex(k => k.AparatId == a.Id && k.NarxOzgarishida);
        if (i < 0) throw Xato("Bu aparatda ochiq smenada narx o'zgarishi ko'rsatkichi yo'q.", 409);
        var seg = l[i];
        var q = Math.Round(d.Qiymat, 2, MidpointRounding.AwayFromZero);
        if (q < seg.Boshi) throw Xato($"Yangi ko'rsatkich oldingi ko'rsatkichdan ({Format.Son(seg.Boshi)}) kichik bo'lishi mumkin emas");
        if (q == seg.Oxiri) throw Xato("Yangi ko'rsatkich joriy ko'rsatkichdan farq qilishi kerak.");
        if (q == seg.Boshi) l.RemoveAt(i);
        else l[i] = Segment(a, seg.Boshi, q, true, seg.Narx);
        Audit(Ism(_joriyFoydalanuvchi), "Ko'rsatkich tuzatildi",
            $"Smena #{s.Id} · {a.Raqam}-aparat narx o'zgarishidagi ko'rsatkich {Format.Son(seg.Oxiri)} dan {Format.Son(q)} ga · sabab: {d.Sabab.Trim()}", "tuzatish");
        return Tafsilot(s);
    }

    private YoqilgiTuriDto NarxOzgartir(int id, YoqilgiTahrirlashDto d)
    {
        var i = _yoqilgilar.FindIndex(x => x.Id == id);
        var eski = _yoqilgilar[i];
        if (Ochiq is { } s && d.Narx != eski.Narx)
        {
            var kerakli = _aparatlar.Where(a => a.YoqilgiTuriId == id).Select(a => a.Id).ToArray();
            if (d.Korsatkichlar is null || kerakli.Any(a => d.Korsatkichlar.All(k => k.AparatId != a)))
                throw new ApiXatosi("Ochiq smenada narx o'zgarsa, aparat ko'rsatkichlari majburiy", 400, kerakli);
            if (!_korsatkichlar.TryGetValue(s.Id, out var l)) _korsatkichlar[s.Id] = l = new();
            foreach (var k in d.Korsatkichlar)
            {
                var a = _aparatlar.First(x => x.Id == k.AparatId);
                var boshi = SmenaHisobi.Oldingi(a, l);
                if (k.Qiymat > boshi) l.Add(Segment(a, boshi, k.Qiymat, true, eski.Narx));
            }
        }
        _yoqilgilar[i] = eski with { Nomi = d.Nomi, Narx = d.Narx, Rang = d.Rang };
        Audit(Ism(_joriyFoydalanuvchi), "Narx o'zgardi", $"{d.Nomi} · {Format.Pul(eski.Narx)} → {Format.Pul(d.Narx)}", "sozlama");
        return _yoqilgilar[i];
    }

    private AparatDto BakKirim(int id, BakKirimYaratishDto d)
    {
        var i = _aparatlar.FindIndex(x => x.Id == id);
        _aparatlar[i] = _aparatlar[i] with { BakQoldiq = _aparatlar[i].BakQoldiq + d.Litr, OxirgiKirimVaqti = d.Vaqt ?? DateTime.UtcNow, OxirgiKirimLitr = d.Litr };
        Audit(Ism(_joriyFoydalanuvchi), "Bakka kirim", $"{_aparatlar[i].Raqam}-aparat · +{Format.ButunLitr(d.Litr)} L", "bak");
        return _aparatlar[i];
    }

    private NasiyaDto NasiyaYoz(NasiyaYaratishDto d)
    {
        var s = Ochiq ?? throw Xato("Ochiq smena yo'q");
        var n = Nasiya(_keyingiId++, s.Id, _joriyFoydalanuvchi, d.MijozIsmi, d.Telefon, d.MashinaRaqami, d.Summa, 0, d.Muddat, DateTime.UtcNow, d.Izoh);
        _nasiyalar.Add(n);
        Audit(Ism(_joriyFoydalanuvchi), "Nasiya yozildi", $"{d.MijozIsmi} · {Format.Pul(d.Summa)}", "nasiya");
        return n;
    }

    private NasiyaDto QarzQaytdi(int id, NasiyaQaytishiYaratishDto d)
    {
        var i = _nasiyalar.FindIndex(x => x.Id == id);
        var n = _nasiyalar[i];
        if (d.Summa <= 0 || d.Summa > n.Qoldiq) throw Xato("Summa qoldiqdan katta");
        var smenaId = d.SmenaHisobiga ? (Ochiq ?? throw Xato("Ochiq smena yo'q")).Id : (int?)null;
        _qaytishlar.Add(new NasiyaQaytishiDto(_keyingiId++, id, n.MijozIsmi, smenaId, d.Summa, d.Usul, DateTime.UtcNow, Ism(_joriyFoydalanuvchi), d.Izoh, _joriyFoydalanuvchi, false));
        var op = _foydalanuvchilar.First(f => f.ToliqIsm == n.OperatorIsmi).Id;
        _nasiyalar[i] = Nasiya(n.Id, n.SmenaId, op, n.MijozIsmi, n.Telefon, n.MashinaRaqami, n.Summa, n.Qaytgan + d.Summa, n.Muddat, n.Yozildi, n.Izoh);
        Audit(Ism(_joriyFoydalanuvchi), "Qarz qaytdi", $"{n.MijozIsmi} · {Format.Pul(d.Summa)}", "nasiya");
        return _nasiyalar[i];
    }

    /// <summary>§8.11: eski qarz — ochiq smenaga bog'langan, darhol yopilgan nasiya + shu summadagi qaytish (smena hisobiga).</summary>
    private NasiyaTafsilotDto EskiQarz(EskiQarzYaratishDto d)
    {
        var s = Ochiq ?? throw Xato("Eski qarzni yozish uchun ochiq smena kerak.", 409);
        if (string.IsNullOrWhiteSpace(d.MijozIsmi) || d.Summa <= 0) throw Xato("Ism va summa majburiy");
        var hozir = DateTime.UtcNow;
        var n = Nasiya(_keyingiId++, s.Id, _joriyFoydalanuvchi, d.MijozIsmi, d.Telefon, d.MashinaRaqami, d.Summa, d.Summa, _bugun, hozir, d.Izoh, eski: true) with { Yopildi = hozir };
        var qt = new NasiyaQaytishiDto(_keyingiId++, n.Id, n.MijozIsmi, s.Id, d.Summa, d.Usul, hozir, Ism(_joriyFoydalanuvchi), d.Izoh, _joriyFoydalanuvchi, true);
        _nasiyalar.Add(n);
        _qaytishlar.Add(qt);
        Audit(Ism(_joriyFoydalanuvchi), "Eski qarz qaytdi", $"{d.MijozIsmi} · {Format.Pul(d.Summa)}", "nasiya");
        return new NasiyaTafsilotDto(n, [qt]);
    }

    private NasiyalarDto Nasiyalar(string? holat, string? q, int? operatorId = null)
    {
        var oy = _bugun.AddDays(1 - _bugun.Day);
        var faol = _nasiyalar.Where(n => n.Qoldiq > 0).ToList();
        var otgan = faol.Where(n => n.Holati == NasiyaHolati.MuddatiOtgan).ToList();
        var oyB = _nasiyalar.Where(n => !n.EskiQarz && DateOnly.FromDateTime(n.Yozildi.ToLocalTime()) >= oy).ToList();
        var oyQ = _qaytishlar.Where(x => DateOnly.FromDateTime(x.Vaqt.ToLocalTime()) >= oy).ToList();
        var xulosa = new NasiyalarXulosaDto(faol.Sum(n => n.Qoldiq), faol.Count, otgan.Sum(n => n.Qoldiq), otgan.Count,
            oyB.Sum(n => n.Summa), oyB.Count, oyQ.Sum(x => x.Summa), oyQ.Count);
        var royxat = _nasiyalar.Where(n => operatorId is null || n.MuallifId == operatorId).Where(n => holat switch
            {
                "faol" => n.Qoldiq > 0,
                "otgan" => n.Holati == NasiyaHolati.MuddatiOtgan,
                "yopilgan" => n.Qoldiq == 0,
                _ => true,
            })
            .ToArray();
        // Server bilan bir xil qidiruv qoidasi (§8.3): q bo'lsa — moslik va yangilik tartibida, aks holda odatiy tartib.
        royxat = string.IsNullOrWhiteSpace(q)
            ? royxat.OrderBy(n => n.Qoldiq > 0 ? 0 : 1).ThenBy(n => n.Muddat).ToArray()
            : NasiyaQidiruv.Filtrla(royxat, q).ToArray();
        NasiyaOperatorXulosaDto? opX = null;
        if (operatorId is { } oid)
        {
            var l = _nasiyalar.Where(n => n.MuallifId == oid && !n.EskiQarz).ToList();
            opX = new NasiyaOperatorXulosaDto(oid, Ism(oid), l.Count, l.Sum(n => n.Summa), l.Sum(n => n.Qaytgan),
                l.Sum(n => n.Qoldiq), l.Count(n => n.Qoldiq > 0), _nasiyalar.Where(n => n.MuallifId == oid && n.EskiQarz).Sum(n => n.Summa));
        }
        return new NasiyalarDto(xulosa, royxat, opX);
    }

    /// <summary>§8.2: mijozlar nasiyalardan — telefon bo'yicha, telefon yo'q bo'lsa ism + mashina raqami bo'yicha guruhlanadi.</summary>
    private List<MijozTaklifDto> MijozTakliflari(string? q)
    {
        var mijozlar = _nasiyalar
            .GroupBy(n => Format.TelefonRaqamlari(n.Telefon) is { Length: > 0 } t ? "t:" + t : "i:" + NasiyaQidiruv.Ism(n.MijozIsmi) + "|" + NasiyaQidiruv.Raqam(n.MashinaRaqami))
            .Select(g =>
            {
                var oxirgi = g.MaxBy(n => n.Yozildi)!;
                return new MijozTaklifDto(oxirgi.MijozIsmi, Format.Telefon(oxirgi.Telefon), oxirgi.MashinaRaqami, g.Count(), g.Sum(n => n.Qoldiq),
                    DateOnly.FromDateTime(oxirgi.Yozildi.ToLocalTime()));
            });
        if (string.IsNullOrWhiteSpace(q)) return mijozlar.OrderByDescending(m => m.OxirgiNasiya).Take(8).ToList();
        return mijozlar.Select(m => (m, k: NasiyaQidiruv.Moslik(m, q))).Where(x => x.k > 0)
            .OrderByDescending(x => x.k).ThenByDescending(x => x.m.OxirgiNasiya).Select(x => x.m).Take(8).ToList();
    }

    private BoshqaruvDto Boshqaruv()
    {
        var yopilgan = _smenalar.Where(x => x.Tugadi is not null).OrderBy(x => x.Boshlandi).ToList();
        var oy = yopilgan.Where(x => x.Boshlandi.ToLocalTime().Month == _bugun.Month && x.Boshlandi.ToLocalTime().Year == _bugun.Year).ToList();
        var savdo = oy.Sum(x => x.Savdo);
        var plastik = oy.Sum(x => x.Plastik);
        var dep = oy.Sum(x => x.DepozitFarqi);
        var nas = oy.Sum(x => x.NasiyaJami);
        return new BoshqaruvDto(Ochiq is { } o ? Yigindili(o) : null, yopilgan.LastOrDefault(),
            savdo, oy.Sum(x => x.JamiLitr), oy.Count, oy.Where(x => x.Farq < 0).Sum(x => -x.Farq), oy.Where(x => x.Farq > 0).Sum(x => x.Farq),
            Nasiyalar("faol", null).Xulosa, _aparatlar.ToArray(),
            yopilgan.TakeLast(14).Select(x => new SmenaQisqaDto(x.Id, DateOnly.FromDateTime(x.Boshlandi.ToLocalTime()), x.OperatorIsmi, x.Savdo, x.JamiLitr, x.Farq)).ToArray(),
            new TolovTaqsimotiDto(savdo - plastik - dep - nas, plastik, dep, nas),
            yopilgan.TakeLast(3).Reverse().ToArray());
    }

    private HisobotDto Hisobot(Dictionary<string, string> q)
    {
        var dan = DateOnly.Parse(q["dan"]);
        var gacha = DateOnly.Parse(q["gacha"]);
        var guruh = q.GetValueOrDefault("guruh") ?? "smena";
        var sm = _smenalar.Where(x => x.Tugadi is not null)
            .Where(x => { var k = DateOnly.FromDateTime(x.Boshlandi.ToLocalTime()); return k >= dan && k <= gacha; })
            .Where(x => !q.TryGetValue("operatorId", out var o) || x.OperatorId == int.Parse(o))
            .OrderBy(x => x.Boshlandi).ToList();
        HisobotQatoriDto Qator(string g, DateOnly? sana, string? op, IList<SmenaDto> l, bool jami) => new(g, sana, op, l.Count, l.Sum(x => x.JamiLitr),
            l.Sum(x => x.Savdo), l.Sum(x => x.Plastik), l.Sum(x => x.DepozitFarqi), l.Sum(x => x.NasiyaJami), l.Sum(x => x.QaytganNasiya),
            l.Sum(x => x.XarajatJami), l.Sum(x => x.Savdo - x.Plastik - x.DepozitFarqi - x.NasiyaJami),
            l.Where(x => x.Farq < 0).Sum(x => -x.Farq), l.Where(x => x.Farq > 0).Sum(x => x.Farq), jami, l.Count * 2);
        DateOnly Kun(SmenaDto x) => DateOnly.FromDateTime(x.Boshlandi.ToLocalTime());
        var qatorlar = guruh switch
        {
            "kun" => sm.GroupBy(Kun).Select(g => Qator(g.Key.ToString("yyyy-MM-dd"), g.Key, null, g.ToList(), false)),
            "oy" => sm.GroupBy(x => Kun(x).ToString("yyyy-MM")).Select(g => Qator(g.Key, null, null, g.ToList(), false)),
            "operator" => sm.GroupBy(x => x.OperatorIsmi).Select(g => Qator(g.Key, null, null, g.ToList(), false)),
            _ => sm.Select(x => Qator(x.Id.ToString(), Kun(x), x.OperatorIsmi, [x], false)),
        };
        var aparat = _aparatlar.Select(a =>
        {
            var sotildi = Math.Round(sm.Sum(x => x.JamiLitr) / 5, 2);
            return new HisobotAparatDto(a.Id, a.Raqam, a.YoqilgiNomi, a.BakQoldiq + sotildi, 0, sotildi, a.BakQoldiq,
                SmenaHisobi.Summa(sotildi, _yoqilgilar.First(y => y.Id == a.YoqilgiTuriId).Narx));
        }).ToArray();
        var smIdlar = sm.Select(x => x.Id).ToHashSet();
        var eskiQ = _qaytishlar.Where(x => x.EskiQarz && x.SmenaId is { } si && smIdlar.Contains(si)).ToList();
        return new HisobotDto(qatorlar.ToArray(), Qator("Jami", null, null, sm, true), aparat, 2_000_000, eskiQ.Sum(x => x.Summa), eskiQ.Count);
    }
}

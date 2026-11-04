using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia.Threading;
using FuelControl.Contracts.Dto;
using FuelControl.Desktop.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace FuelControl.Desktop.Services;

[Flags]
public enum Bolim
{
    Foydalanuvchilar = 1,
    Yoqilgilar = 2,
    Aparatlar = 4,
    Smenalar = 8,
    JoriySmena = 16,
    Nasiyalar = 32,
    Harakatlar = 64,
    Audit = 128,
    Hammasi = 255,
}

/// <summary>
/// Serverdagi ma'lumotning lokal keshi. Ro'yxatlar login'dan keyin API'dan yuklanadi va SignalR (/hub) xabarlari
/// kelganda tegishli bo'lim qayta yuklanadi. Yozuvchi amallar faqat API orqali — javobdan kesh yangilanadi.
/// Obyektlar Id bo'yicha joyida yangilanadi, shuning uchun ViewModel'lardagi tanlovlar (aparat, smena) saqlanib qoladi.
/// Hamma o'zgarishlar UI oqimida bajariladi.
/// </summary>
public static class Malumot
{
    public static Foydalanuvchi JoriyFoydalanuvchi { get; private set; } = new();
    public static readonly List<Foydalanuvchi> Foydalanuvchilar = new();
    public static readonly List<YoqilgiTuri> Yoqilgilar = new();
    public static readonly List<Aparat> Aparatlar = new();
    public static readonly List<Smena> Smenalar = new();
    public static readonly List<HisobHarakati> Harakatlar = new();
    public static readonly List<AuditYozuvi> Audit = new();
    public static readonly List<NarxTarixi> NarxTarixi = new();

    /// <summary>Butun shoxobchadagi ochiq smena tafsiloti (GET /smenalar/joriy); yo'q bo'lsa null.</summary>
    public static SmenaTafsilotDto? Joriy { get; private set; }
    /// <summary>Ochiq smena (Joriy.Smena ning modeli) yoki null.</summary>
    public static Smena? JoriySmena { get; private set; }
    /// <summary>Oxirgi yopilgan smena (GET /smenalar/oxirgi) — Savdo ochish formasidagi "Oxirgi smena" uchun.</summary>
    public static Smena? OxirgiYopilgan { get; private set; }
    /// <summary>§8.10: oxirgi yopilgan smena topshirish ma'lumoti (natijasiz) — to'liq natija ko'rish huquqi bo'lmaganlar uchun; aks holda null.</summary>
    public static SmenaTopshirishDto? OxirgiTopshirish { get; private set; }

    /// <summary>Qarzi bor nasiyalar (holat=faol, muddati o'tganlar ham) va umumiy xulosa. Nasiyalar ruxsati bo'lmasa null.</summary>
    public static NasiyalarDto? FaolNasiyalar { get; private set; }
    public static int MuddatiOtganSoni => FaolNasiyalar?.Xulosa.MuddatiOtganSoni ?? 0;

    // Keshda faqat yaqin davr: oxirgi 60 kun smenalari, oxirgi 500 audit.
    // Boshqaruv, hisobot, smena tafsiloti, nasiyalar ro'yxati (filtr bilan) va "Hammasi" filtri serverdan alohida so'raladi.
    public const int SmenaKunlari = 60;
    public const int AuditSoni = 500;

    /// <summary>Operatorning joriy oy savdosi va smenalar soni (operator hisobi so'rovidan).</summary>
    public static readonly Dictionary<int, (long Savdo, int Smenalar)> OyStatistikasi = new();

    public static bool Kirilgan => _kirilgan;
    public static DateOnly Bugun => DateOnly.FromDateTime(DateTime.Today);

    public static IEnumerable<Foydalanuvchi> Operatorlar => Foydalanuvchilar.Where(f => f.Rol == Rol.Operator);

    /// <summary>Kesh o'zgardi — sahifalar qayta hisoblanadi.</summary>
    public static event Action? Ozgardi;
    public static void OzgardiXabar() => Ozgardi?.Invoke();

    public static ApiMijoz Api { get; private set; } = YangiMijoz(Sozlama.Joriy.ServerManzili);

    /// <summary>SignalR ulanishi bor-yo'qligi. Yo'q bo'lsa yozuvchi tugmalar o'chiriladi (1-versiyada navbat yo'q).</summary>
    /// <summary>SignalR holati: Ulanmoqda (birinchi ulanish yoki qayta ulanish), Bor, Yoq (ulanmadi yoki bir necha soniyada tiklanmadi).</summary>
    public enum AloqaHolati { Ulanmoqda, Bor, Yoq }
    public static AloqaHolati Holat { get; private set; } = AloqaHolati.Yoq;

    /// <summary>Yozuvchi amallar mumkinmi: yozish REST orqali, shuning uchun ulanayotganda ham ruxsat; faqat aloqa haqiqatan yo'q bo'lsa o'chadi.</summary>
    public static bool AloqaBor => Holat != AloqaHolati.Yoq;
    private const int UlanishKutish = 6; // soniya — shundan keyin ham ulanmasa "Aloqa yo'q"
    private static int _ulanishVersiyasi;
    public static event Action? AloqaOzgardi;

    /// <summary>Sessiya tugadi yoki o'z ruxsatlari o'zgardi — qayta kirish kerak. Parametr — sabab matni.</summary>
    public static event Action<string>? MajburiyChiqish;

    private static HubConnection? _hub;
    private static bool _kirilgan;
    private static Bolim _kutilayotgan;
    private static int _rejaVersiyasi;

    // Ro'yxatda yo'q, lekin sotuv/smenada uchraydigan foydalanuvchilar (masalan, sotuv qilgan boshliq).
    private static readonly Dictionary<int, Foydalanuvchi> Begonalar = new();

    private static ApiMijoz YangiMijoz(string manzil)
    {
        var m = new ApiMijoz(manzil);
        m.SessiyaTugadi += () => Dispatcher.UIThread.Post(() => MajburiyChiqish?.Invoke(Til.T("SessiyaTugadi")));
        // §8.18: 401 — kirish oynasiga otilmaydi, joriy ekran ustida parol/PIN so'raladi (MainViewModel)
        // Uzaytirilgan token bilan rol va ruxsatlar bazadan keladi — /me orqali joyida yangilanadi
        m.TokenYangilandi += () => Dispatcher.UIThread.Post(() => { if (_kirilgan) Rejala(Bolim.Foydalanuvchilar); });
        m.QaytaKirishSorovi = () => Dispatcher.UIThread.InvokeAsync(() => QaytaKirishSorovi?.Invoke() ?? Task.FromResult(false));
        return m;
    }

    // ================= §8.18 Sessiya: uzaytirish va qayta kirish =================

    /// <summary>UI: "Sessiya tugadi" oynasi. true — qayta kirildi, false — to'liq chiqish.</summary>
    public static Func<Task<bool>>? QaytaKirishSorovi { get; set; }

    /// <summary>Fon tekshiruvi oralig'i (sinovda qisqartiriladi).</summary>
    public static TimeSpan YangilashTekshiruvi { get; set; } = TimeSpan.FromMinutes(10);
    private static int _sessiyaVersiyasi;

    /// <summary>Har 10 daqiqada: muddat oz qolgan va foydalanuvchi faol bo'lsa token uzaytiriladi (Task.Delay — DispatcherTimer emas).</summary>
    private static async Task YangilashSikli(int v)
    {
        while (_kirilgan && v == _sessiyaVersiyasi)
        {
            await Task.Delay(YangilashTekshiruvi);
            if (!_kirilgan || v != _sessiyaVersiyasi) return;
            try { await Api.YangilaKerakBolsa(); } catch (Exception) { /* keyingi tekshiruvda */ }
        }
    }

    /// <summary>Sessiya tugaganda shu foydalanuvchi bilan qayta kirish (login o'zgarmaydi). Ma'lumot va ochiq oynalar o'z joyida qoladi.</summary>
    public static async Task QaytaKir(string parolYokiPin)
    {
        await Api.Kirish(JoriyFoydalanuvchi.Login, parolYokiPin);
        // Token tugagan paytda o'tkazib yuborilgan o'zgarishlar — fonda yangilanadi
        Rejala(Bolim.Hammasi);
    }

    // ================= Kirish / chiqish =================

    public static async Task<Foydalanuvchi> Kirish(string manzil, string login, string parol)
    {
        manzil = manzil.Trim().TrimEnd('/');
        if (!Uri.TryCreate(manzil, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new ApiXatosi(Til.T("Xato_ServerManzil"), -1);
        if (Api.Manzil != manzil) Api = YangiMijoz(manzil);

        var javob = await Api.Kirish(login.Trim(), parol);
        Tozala();
        JoriyFoydalanuvchi = Yarat(javob.Foydalanuvchi);
        _kirilgan = true;
        _ = YangilashSikli(++_sessiyaVersiyasi);
        try
        {
            await Yukla(Bolim.Hammasi);
        }
        catch
        {
            await Chiqish();
            throw;
        }
        // Namuna rejimida SignalR yo'q — aloqa bor deb hisoblaymiz.
        if (ApiMijoz.Namuna is not null) HolatniOrnat(AloqaHolati.Bor);
        else { HolatniOrnat(AloqaHolati.Ulanmoqda); _ = HubniBoshla(); }
        return JoriyFoydalanuvchi;
    }

    public static async Task Chiqish()
    {
        _kirilgan = false;
        _sessiyaVersiyasi++;   // uzaytirish sikli to'xtaydi
        _rejaVersiyasi++;   // rejadagi yuklash bekor
        _kutilayotgan = 0;
        var hub = _hub;
        _hub = null;
        if (hub is not null)
        {
            try { await hub.DisposeAsync(); } catch (Exception) { /* yopilayotgan ulanish */ }
        }
        Api.Chiqish();
        HolatniOrnat(AloqaHolati.Yoq);
        Tozala();
        OzgardiXabar();
    }

    private static void Tozala()
    {
        Foydalanuvchilar.Clear(); Yoqilgilar.Clear(); Aparatlar.Clear(); Smenalar.Clear();
        Harakatlar.Clear(); Audit.Clear(); NarxTarixi.Clear(); Begonalar.Clear(); OyStatistikasi.Clear();
        Joriy = null; JoriySmena = null; OxirgiYopilgan = null; OxirgiTopshirish = null; FaolNasiyalar = null;
        JoriyFoydalanuvchi = new Foydalanuvchi();
    }

    // ================= Yuklash =================

    /// <summary>Ko'rsatilgan bo'limlarni serverdan qayta yuklaydi (ruxsatga qarab — server baribir o'zinikini qaytaradi).</summary>
    public static async Task Yukla(Bolim b)
    {
        var j = JoriyFoydalanuvchi;

        if (b.HasFlag(Bolim.Foydalanuvchilar))
        {
            // Server ruxsatni har so'rovda bazadan tekshiradi — rol/ruxsat o'zgarsa joyida yangilaymiz (menyu Ozgardi orqali qayta quriladi).
            // Nofaol qilingan foydalanuvchi /me da 401 oladi → SessiyaTugadi → chiqish.
            var men = (await Api.Men())!;
            if (!men.Faol)
            {
                MajburiyChiqish?.Invoke(Til.T("SessiyaTugadi"));
                return;
            }
            j.ToliqIsm = men.ToliqIsm; j.Login = men.Login; j.OylikMaosh = men.OylikMaosh;
            j.Rol = men.Rol;
            if (!j.Ruxsatlar.SetEquals(men.Ruxsatlar)) j.Ruxsatlar = men.Ruxsatlar.ToHashSet();
            var royxat = j.Bor(Ruxsat.Sozlamalar) ? await Api.Foydalanuvchilar() : await Api.Operatorlar();
            Mosla(Foydalanuvchilar, royxat!, d => d.Id, FoydalanuvchiOl, (f, d) =>
            {
                f.ToliqIsm = d.ToliqIsm; f.Login = d.Login; f.Rol = d.Rol; f.Faol = d.Faol; f.OylikMaosh = d.OylikMaosh;
                if (f != j) f.Ruxsatlar = d.Ruxsatlar.ToHashSet();
            });
        }

        if (b.HasFlag(Bolim.Yoqilgilar))
        {
            Mosla(Yoqilgilar, (await Api.Yoqilgilar())!, d => d.Id, d => new YoqilgiTuri { Id = d.Id },
                (y, d) => { y.Nomi = d.Nomi; y.Narx = d.Narx; y.Rang = d.Rang; });
            var tarix = (await Api.NarxTarixi())!;
            NarxTarixi.Clear();
            NarxTarixi.AddRange(tarix.Select(n => new NarxTarixi
            {
                Vaqt = n.Vaqt.ToLocalTime(), Yoqilgi = n.Yoqilgi, EskiNarx = n.EskiNarx, YangiNarx = n.YangiNarx, Kim = n.Kim,
            }));
        }

        if (b.HasFlag(Bolim.Aparatlar))
            Mosla(Aparatlar, (await Api.Aparatlar())!, d => d.Id, d => new Aparat { Id = d.Id }, (a, d) =>
            {
                a.Raqam = d.Raqam;
                a.Yoqilgi = YoqilgiOl(d.YoqilgiTuriId, d.YoqilgiNomi);
                a.TotalLitr = d.TotalLitr; a.BakQoldiq = d.BakQoldiq;
                a.OxirgiKirimVaqti = d.OxirgiKirimVaqti?.ToLocalTime(); a.OxirgiKirimLitr = d.OxirgiKirimLitr;
                a.KrugHajmi = Krug.Hajm(d.KrugHajmi);
            });

        if (b.HasFlag(Bolim.JoriySmena))
        {
            Joriy = await Api.JoriySmena();
            JoriySmena = Joriy is null ? null : SmenaniQoy(Joriy.Smena, xabar: false);
            await OxirginiYukla();
            // Boshqa joyda yopilgan smena keshda ochiq bo'lib qolmasin.
            if (JoriySmena is null && Smenalar.Any(s => s.Ochiqmi)) b |= Bolim.Smenalar;
        }

        if (b.HasFlag(Bolim.Nasiyalar))
            FaolNasiyalar = j.Bor(Ruxsat.Nasiyalar) ? await Api.Nasiyalar("faol") : null;

        if (b.HasFlag(Bolim.Smenalar))
        {
            Mosla(Smenalar, (await Api.Smenalar(Bugun.AddDays(-SmenaKunlari)))!, d => d.Id, SmenaYarat, SmenaYangila);
            if (JoriySmena is { } js && !Smenalar.Contains(js)) Smenalar.Add(js);
            Smenalar.Sort((x, y) => y.Boshlandi.CompareTo(x.Boshlandi));
        }

        if (b.HasFlag(Bolim.Harakatlar))
        {
            // Operatorlar ruxsati bo'lsa — barcha operatorlar, aks holda faqat o'zi.
            var idlar = j.Bor(Ruxsat.Operatorlar) ? Operatorlar.Select(o => o.Id).ToList() : [j.Id];
            var yangi = new List<HisobHarakati>();
            foreach (var id in idlar)
            {
                var hisob = (await Api.OperatorHisobi(id))!;
                var op = FoydalanuvchiOl(id, hisob.OperatorIsmi);
                OyStatistikasi[id] = (hisob.OySavdo, hisob.OySmenalar);
                yangi.AddRange(hisob.Harakatlar.Select(h => new HisobHarakati
                {
                    Id = h.Id, Operator = op, Sana = h.Sana.ToLocalTime(), Turi = h.Turi, Summa = h.Summa, Izoh = h.Izoh, KimYozdi = h.KimYozdi,
                }));
            }
            Harakatlar.Clear();
            Harakatlar.AddRange(yangi.OrderByDescending(h => h.Sana).ThenByDescending(h => h.Id));
        }

        if (b.HasFlag(Bolim.Audit) && j.Bor(Ruxsat.Audit))
        {
            var audit = (await Api.Audit(AuditSoni))!;
            Audit.Clear();
            Audit.AddRange(audit.Select(a => new AuditYozuvi { Id = a.Id, Vaqt = a.Vaqt.ToLocalTime(), Kim = a.Kim, Amal = a.Amal, Tafsilot = a.Tafsilot, Tur = a.Tur }));
        }

        OzgardiXabar();
    }

    /// <summary>Yozuvchi amaldan keyin qo'shimcha yangilash — xato bo'lsa jim (keyingi SignalR xabari yoki qayta ulanish to'g'rilaydi).</summary>
    private static async Task JimYukla(Bolim b)
    {
        ApiMijoz.FonBelgila();   // SignalR/rejali qayta yuklash — foydalanuvchi faolligi emas (§8.18)
        try { await Yukla(b); }
        catch (ApiXatosi) { }
    }

    /// <summary>
    /// §8.10 klient tartibi: boshliq (Smenalar ruxsati) → /smenalar/oxirgi (to'liq natija). Aks holda → /smenalar/oxirgi/topshirish;
    /// smena o'ziniki bo'lsa (OperatorId == men) → /smenalar/oxirgi (server egasiga beradi). Boshqa operatorning natijasi (kamomat...) so'ralmaydi.
    /// </summary>
    private static async Task OxirginiYukla()
    {
        if (JoriyFoydalanuvchi.Bor(Ruxsat.Smenalar))
        {
            OxirgiTopshirish = null;
            OxirgiYopilgan = await Api.OxirgiSmena() is { } ox ? SmenaKorinishi(ox.Smena) : null;
            return;
        }
        var t = await Api.OxirgiTopshirish();
        if (t is not null && t.OperatorId == JoriyFoydalanuvchi.Id)
        {
            OxirgiTopshirish = null;
            OxirgiYopilgan = await Api.OxirgiSmena() is { } o ? SmenaKorinishi(o.Smena) : null;
        }
        else
        {
            OxirgiYopilgan = null;
            OxirgiTopshirish = t;
        }
    }

    /// <summary>
    /// Bir nechta xabar ketma-ket kelsa (300 ms ichida), bitta yuklashga birlashtiriladi.
    /// DispatcherTimer emas, Task.Delay (KechiktirilganIsh kabi): taymer ishga tushmay qolsa, SignalR xabari
    /// (masalan, admin xarajat yozdi/o'chirdi) ochiq turgan "Smenani yopish" sahifasini yangilamas edi.
    /// </summary>
    private static void Rejala(Bolim b)
    {
        if (!_kirilgan) return;
        _kutilayotgan |= b;
        var r = ++_rejaVersiyasi;
        _ = Task.Delay(300).ContinueWith(_ => Dispatcher.UIThread.Post(async () =>
        {
            if (r != _rejaVersiyasi) return;
            var x = _kutilayotgan;
            _kutilayotgan = 0;
            if (_kirilgan && x != 0) await JimYukla(x);
        }));
    }

    // ================= SignalR =================

    private sealed class DoimiyQaytaUlanish : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext c) => c.PreviousRetryCount switch
        {
            0 => TimeSpan.Zero,
            1 => TimeSpan.FromSeconds(2),
            2 => TimeSpan.FromSeconds(5),
            _ => TimeSpan.FromSeconds(10),
        };
    }

    private static async Task HubniBoshla()
    {
        var hub = new HubConnectionBuilder()
            .WithUrl(Api.Manzil + "/hub", o => o.AccessTokenProvider = () => Task.FromResult(Api.Token))
            .WithAutomaticReconnect(new DoimiyQaytaUlanish())
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();
        _hub = hub;

        void UI(Action a) => Dispatcher.UIThread.Post(() => { if (_hub == hub) a(); });

        // Smena hisobi hodisalari — faqat "qayta yuklash" signali (yuk DTO'si ishlatilmaydi, o'chirishda ham shu keladi).
        void Signal(string nomi, Bolim bolim) => hub.On<JsonElement>(nomi, _ => UI(() => Rejala(bolim)));
        Signal("SmenaOzgardi", Bolim.JoriySmena | Bolim.Smenalar | Bolim.Aparatlar | Bolim.Harakatlar | Bolim.Audit);
        Signal("NasiyaOzgardi", Bolim.JoriySmena | Bolim.Nasiyalar | Bolim.Audit);
        Signal("XarajatOzgardi", Bolim.JoriySmena | Bolim.Audit);
        Signal("AparatOzgardi", Bolim.Aparatlar | Bolim.Audit);
        Signal("NarxOzgardi", Bolim.Yoqilgilar | Bolim.Aparatlar | Bolim.JoriySmena | Bolim.Audit);
        hub.On<string>("Ozgardi", bolim => UI(() => Rejala(Bolim.Audit | bolim switch
        {
            "Foydalanuvchilar" => Bolim.Foydalanuvchilar,
            "Yoqilgilar" => Bolim.Yoqilgilar | Bolim.Aparatlar,
            "Aparatlar" => Bolim.Aparatlar,
            "Harakatlar" => Bolim.Harakatlar,
            _ => Bolim.Hammasi,
        })));

        // Server o'zgargan foydalanuvchining ulanishini uzishdan oldin yuboradi: o'z holatimizni darhol tekshiramiz
        // (nofaol bo'lsak /me → 401 → chiqish; ruxsat o'zgargan bo'lsa — joyida yangilanadi, qayta ulanish yangi guruhlar bilan).
        hub.On("QaytaUlan", () => UI(() => Rejala(Bolim.Foydalanuvchilar)));
        hub.Reconnecting += _ => { UI(() => HolatniOrnat(AloqaHolati.Ulanmoqda)); return Task.CompletedTask; };
        // Uzilish paytida o'tkazib yuborilgan xabarlar — hammasini qayta yuklaymiz.
        hub.Reconnected += _ => { UI(() => { HolatniOrnat(AloqaHolati.Bor); Rejala(Bolim.Hammasi); }); return Task.CompletedTask; };
        hub.Closed += xato => { UI(() => { HolatniOrnat(AloqaHolati.Yoq); _ = Ulan(hub, qaytaYukla: true); }); return Task.CompletedTask; };

        await Ulan(hub, qaytaYukla: false);
    }

    /// <summary>Birinchi ulanish (yoki avtomatik qayta ulanish taslim bo'lgach) — muvaffaqiyatgacha har 5 soniyada urinadi.</summary>
    private static async Task Ulan(HubConnection hub, bool qaytaYukla)
    {
        while (_kirilgan && _hub == hub && hub.State == HubConnectionState.Disconnected)
        {
            try
            {
                await hub.StartAsync();
                HolatniOrnat(AloqaHolati.Bor);
                if (qaytaYukla) Rejala(Bolim.Hammasi);
                return;
            }
            catch (Exception)
            {
                HolatniOrnat(AloqaHolati.Yoq);
                qaytaYukla = true;
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }

    private static void HolatniOrnat(AloqaHolati h)
    {
        var v = ++_ulanishVersiyasi;   // oldingi "cho'zildi" tekshiruvi bekor
        if (h == AloqaHolati.Ulanmoqda)
        {
            // Ulanish cho'zilsa — "Aloqa yo'q". DispatcherTimer emas, Task.Delay: taymer ishga tushmay, holat "Ulanmoqda"da qotib qolardi.
            _ = Task.Delay(TimeSpan.FromSeconds(UlanishKutish)).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
            {
                if (v == _ulanishVersiyasi && Holat == AloqaHolati.Ulanmoqda) HolatniOrnat(AloqaHolati.Yoq);
            }));
        }
        if (Holat == h) return;
        Holat = h;
        AloqaOzgardi?.Invoke();
    }

    // ================= Yozuvchi amallar (API → kesh) =================

    public static async Task<Smena> SmenaOch(SmenaOchishDto d)
    {
        var s = SmenaniQoy((await Api.SmenaOch(d))!);
        await JimYukla(Bolim.JoriySmena | Bolim.Audit);
        return s;
    }

    public static async Task<Smena> SmenaYop(int id, SmenaYopishDto d)
    {
        var s = SmenaniQoy((await Api.SmenaYop(id, d))!);
        await JimYukla(Bolim.JoriySmena | Bolim.Aparatlar | Bolim.Harakatlar | Bolim.Audit);
        return s;
    }

    public static async Task<SmenaTafsilotDto> KorsatkichTuzat(int smenaId, KorsatkichTuzatishDto d)
    {
        var t = (await Api.KorsatkichTuzat(smenaId, d))!;
        SmenaniQoy(t.Smena);
        await JimYukla(Bolim.JoriySmena | Bolim.Aparatlar | Bolim.Harakatlar | Bolim.Audit);
        return t;
    }

    /// <summary>§8.14: ochiq smenada aparatning oxirgi narx o'zgarishi segmentini tuzatish (TotalLitr, BakQoldiq o'zgarmaydi).</summary>
    public static async Task<SmenaTafsilotDto> NarxKorsatkichiniTuzat(int smenaId, KorsatkichTuzatishDto d)
    {
        var t = (await Api.NarxKorsatkichiniTuzat(smenaId, d))!;
        await JimYukla(Bolim.JoriySmena | Bolim.Audit);
        return t;
    }

    public static async Task<NasiyaDto> NasiyaYoz(NasiyaYaratishDto d)
    {
        var n = (await Api.NasiyaYoz(d))!;
        await JimYukla(Bolim.JoriySmena | Bolim.Nasiyalar | Bolim.Audit);
        return n;
    }

    public static async Task<NasiyaDto> QarzQaytdi(int nasiyaId, NasiyaQaytishiYaratishDto d)
    {
        var n = (await Api.QarzQaytdi(nasiyaId, d))!;
        await JimYukla(Bolim.JoriySmena | Bolim.Nasiyalar | Bolim.Audit);
        return n;
    }

    public static async Task<NasiyaTafsilotDto> EskiQarzYoz(EskiQarzYaratishDto d)
    {
        var t = (await Api.EskiQarz(d))!;
        await JimYukla(Bolim.JoriySmena | Bolim.Nasiyalar | Bolim.Audit);
        return t;
    }

    public static async Task NasiyaOchir(int id) { await Api.NasiyaOchir(id); await JimYukla(Bolim.JoriySmena | Bolim.Nasiyalar | Bolim.Audit); }
    public static async Task QaytishOchir(int id) { await Api.QaytishOchir(id); await JimYukla(Bolim.JoriySmena | Bolim.Nasiyalar | Bolim.Audit); }

    public static async Task<XarajatDto> XarajatYoz(XarajatYaratishDto d)
    {
        var x = (await Api.XarajatYoz(d))!;
        await JimYukla(Bolim.JoriySmena | Bolim.Audit);
        return x;
    }

    public static async Task XarajatOchir(int id) { await Api.XarajatOchir(id); await JimYukla(Bolim.JoriySmena | Bolim.Audit); }

    public static async Task BakKirim(int aparatId, BakKirimYaratishDto d) { await Api.BakKirim(aparatId, d); await JimYukla(Bolim.Aparatlar | Bolim.Audit); }

    public static async Task HarakatYoz(int operatorId, HarakatYaratishDto d)
    {
        await Api.HarakatYoz(operatorId, d);
        await JimYukla(Bolim.Harakatlar | Bolim.Audit);
    }

    public static async Task YoqilgiYarat(YoqilgiYaratishDto d) { await Api.YoqilgiYarat(d); await JimYukla(Bolim.Yoqilgilar | Bolim.Audit); }
    public static async Task YoqilgiTahrirla(int id, YoqilgiTahrirlashDto d) { await Api.YoqilgiTahrirla(id, d); await JimYukla(Bolim.Yoqilgilar | Bolim.Aparatlar | Bolim.JoriySmena | Bolim.Audit); }
    public static async Task YoqilgiOchir(int id) { await Api.YoqilgiOchir(id); await JimYukla(Bolim.Yoqilgilar | Bolim.Audit); }

    public static async Task AparatYarat(AparatYaratishDto d) { await Api.AparatYarat(d); await JimYukla(Bolim.Aparatlar | Bolim.Audit); }
    public static async Task AparatTahrirla(int id, AparatTahrirlashDto d) { await Api.AparatTahrirla(id, d); await JimYukla(Bolim.Aparatlar | Bolim.Audit); }

    public static async Task<Foydalanuvchi> FoydalanuvchiYarat(FoydalanuvchiYaratishDto d)
    {
        var f = (await Api.FoydalanuvchiYarat(d))!;
        await JimYukla(Bolim.Foydalanuvchilar | Bolim.Audit);
        return FoydalanuvchiOl(f);
    }

    public static async Task FoydalanuvchiTahrirla(int id, FoydalanuvchiTahrirlashDto d, string? yangiPin)
    {
        await Api.FoydalanuvchiTahrirla(id, d);
        if (!string.IsNullOrWhiteSpace(yangiPin)) await Api.PinOrnat(id, new PinOrnatishDto(yangiPin.Trim()));
        await JimYukla(Bolim.Foydalanuvchilar | Bolim.Audit);
    }

    public static async Task RuxsatlarniOrnat(int id, IEnumerable<Ruxsat> ruxsatlar)
    {
        await Api.RuxsatlarniOrnat(id, new RuxsatlarOrnatishDto(ruxsatlar.ToArray()));
        await JimYukla(Bolim.Foydalanuvchilar | Bolim.Audit);
    }

    public static async Task PinOrnat(int id, string pin)
    {
        await Api.PinOrnat(id, new PinOrnatishDto(pin));
        await JimYukla(Bolim.Audit);
    }

    public static async Task<ZaxiraJavobiDto> Zaxira()
    {
        var z = (await Api.Zaxira())!;
        await JimYukla(Bolim.Audit);
        return z;
    }

    /// <summary>§8.17: zaxira papkasi, oxirgi nusxalar va Telegram holati (keshlanmaydi — Zaxira tabi ochilganda so'raladi).</summary>
    public static async Task<ZaxiraHolatiDto> ZaxiraHolati() => (await Api.ZaxiraHolati())!;

    /// <summary>Telegram sozlamalari: Token null — o'zgarmaydi, "" — o'chiriladi. Token o'zgarishi auditga yoziladi.</summary>
    public static async Task<ZaxiraHolatiDto> ZaxiraTelegram(ZaxiraTelegramTahrirDto d)
    {
        var h = (await Api.ZaxiraTelegram(d))!;
        await JimYukla(Bolim.Audit);
        return h;
    }

    /// <summary>Sinov xabari: yuborilmasa ham 200 — natija Telegram.OxirgiMuvaffaqiyat/OxirgiXabar'da.</summary>
    public static async Task<ZaxiraHolatiDto> ZaxiraTelegramSinov() => (await Api.ZaxiraTelegramSinov())!;

    // ================= DTO → model =================

    /// <summary>Ro'yxatni server javobiga moslaydi: borini joyida yangilaydi, yangisini qo'shadi, yo'qini olib tashlaydi.</summary>
    private static void Mosla<T, TDto>(List<T> royxat, List<TDto> dtolar, Func<TDto, int> id, Func<TDto, T> yarat, Action<T, TDto> yangila)
        where T : class
    {
        var eskilar = royxat.ToDictionary(x => ModelId(x));
        royxat.Clear();
        foreach (var d in dtolar)
        {
            var x = eskilar.TryGetValue(id(d), out var e) ? e : yarat(d);
            yangila(x, d);
            royxat.Add(x);
        }
    }

    private static int ModelId(object x) => x switch
    {
        Foydalanuvchi f => f.Id, YoqilgiTuri y => y.Id, Aparat a => a.Id, Smena s => s.Id,
        _ => throw new ArgumentException(x.GetType().Name),
    };

    private static Foydalanuvchi Yarat(FoydalanuvchiDto d) => new()
    {
        Id = d.Id, ToliqIsm = d.ToliqIsm, Login = d.Login, Rol = d.Rol, Faol = d.Faol, OylikMaosh = d.OylikMaosh,
        Ruxsatlar = d.Ruxsatlar.ToHashSet(),
    };

    private static Foydalanuvchi FoydalanuvchiOl(FoydalanuvchiDto d) => FoydalanuvchiOl(d.Id, d.ToliqIsm);

    private static Foydalanuvchi FoydalanuvchiOl(int id, string ism)
    {
        if (JoriyFoydalanuvchi.Id == id) return JoriyFoydalanuvchi;
        var f = Foydalanuvchilar.FirstOrDefault(x => x.Id == id);
        if (f is not null) return f;
        if (!Begonalar.TryGetValue(id, out f))
            Begonalar[id] = f = new Foydalanuvchi { Id = id, ToliqIsm = ism, Rol = Rol.Boshliq, Faol = false };
        return f;
    }

    private static YoqilgiTuri YoqilgiOl(int id, string nomi) =>
        Yoqilgilar.FirstOrDefault(y => y.Id == id) ?? new YoqilgiTuri { Id = id, Nomi = nomi };

    private static Smena SmenaYarat(SmenaDto d) =>
        new() { Id = d.Id, Operator = FoydalanuvchiOl(d.OperatorId, d.OperatorIsmi) };

    private static void SmenaYangila(Smena s, SmenaDto d)
    {
        s.Boshlandi = d.Boshlandi.ToLocalTime(); s.Tugadi = d.Tugadi?.ToLocalTime();
        s.OchishQaytim = d.OchishQaytim; s.OchishTerminal = d.OchishTerminal; s.OchishDepozit = d.OchishDepozit;
        s.YopishTerminal = d.YopishTerminal; s.YopishDepozit = d.YopishDepozit; s.SanalganNaqd = d.SanalganNaqd;
        s.JamiLitr = d.JamiLitr; s.Savdo = d.Savdo; s.Plastik = d.Plastik; s.DepozitFarqi = d.DepozitFarqi;
        s.NasiyaJami = d.NasiyaJami; s.QaytganNasiya = d.QaytganNasiya; s.XarajatJami = d.XarajatJami;
        s.Kutilgan = d.Kutilgan; s.Farq = d.Farq; s.Izoh = d.Izoh;
    }

    // ================= Serverdan bevosita (keshga kirmaydigan) ma'lumot =================

    /// <summary>DTO → model: keshda bo'lsa o'sha obyekt (yangilanadi), aks holda alohida obyekt.</summary>
    public static Smena SmenaKorinishi(SmenaDto d)
    {
        var s = Smenalar.FirstOrDefault(x => x.Id == d.Id) ?? SmenaYarat(d);
        SmenaYangila(s, d);
        return s;
    }

    public static Foydalanuvchi FoydalanuvchiniOl(int id, string ism) => FoydalanuvchiOl(id, ism);

    /// <summary>Smenalar sahifasidagi "Hammasi" filtri — 60 kundan eskilari ham.</summary>
    public static async Task<List<Smena>> BarchaSmenalar(int? operatorId) =>
        (await Api.Smenalar(operatorId: operatorId))!.Select(SmenaKorinishi).ToList();

    /// <summary>Hamma bo'limni qayta yuklash ("Yangilash" tugmasi).</summary>
    public static Task QaytaYukla() => Yukla(Bolim.Hammasi);

    /// <summary>Klientda qilingan eksportni server auditiga yozadi (xato bo'lsa jim — fayl baribir saqlangan).</summary>
    public static async Task EksportniYoz(string turi, string tafsilot)
    {
        try
        {
            await Api.AuditEksport(new AuditEksportDto(turi, tafsilot));
            await JimYukla(Bolim.Audit);
        }
        catch (ApiXatosi) { }
    }

    private static Smena SmenaniQoy(SmenaDto d, bool xabar = true)
    {
        var s = Smenalar.FirstOrDefault(x => x.Id == d.Id);
        if (s is null)
        {
            s = SmenaYarat(d);
            Smenalar.Insert(0, s);
        }
        SmenaYangila(s, d);
        if (xabar) OzgardiXabar();
        return s;
    }
}

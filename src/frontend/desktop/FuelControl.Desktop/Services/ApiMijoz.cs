using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using FuelControl.Contracts.Dto;

namespace FuelControl.Desktop.Services;

/// <summary>Server rad etgan amal yoki aloqa xatosi. Xabar foydalanuvchiga ko'rsatiladi (server ProblemDetails'dagi o'zbekcha matn).</summary>
public sealed class ApiXatosi(string xabar, int status, int[]? kerakliAparatlar = null) : Exception(xabar)
{
    public int Status { get; } = status;
    public bool AloqaXatosi => Status == 0;
    /// <summary>Narx o'zgarishi 400: ko'rsatkichi kerak bo'lgan aparatlar (ProblemDetails extensions.kerakliAparatlar).</summary>
    public int[]? KerakliAparatlar { get; } = kerakliAparatlar;
}

/// <summary>Namuna (demo) rejimi: server o'rniga javob beradi — UI'ni server tayyor bo'lmaganda va sinov harness'ida ko'rsatish uchun.</summary>
public interface INamunaServer
{
    /// <summary>yol — so'rov qatori bilan ("smenalar?dan=..."); tana — yuborilgan DTO. Javob T ga mos obyekt yoki null.</summary>
    object? Javob(HttpMethod usul, string yol, object? tana);
}

/// <summary>FuelControl API uchun tipli mijoz. Token login'dan keyin saqlanadi va har so'rovga qo'shiladi.</summary>
public sealed class ApiMijoz
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _http;

    public ApiMijoz(string manzil)
    {
        Manzil = manzil.TrimEnd('/');
        _http = new HttpClient { BaseAddress = new Uri(Manzil + "/"), Timeout = TimeSpan.FromSeconds(15) };
    }

    public string Manzil { get; }
    public string? Token { get; private set; }

    /// <summary>O'rnatilsa — barcha so'rovlarga server o'rniga shu javob beradi (namuna rejimi).</summary>
    public static INamunaServer? Namuna { get; set; }

    /// <summary>Sessiyani davom ettirib bo'lmaydi (qayta kirish rad etildi, foydalanuvchi nofaol/bloklangan) — to'liq chiqish kerak.</summary>
    public event Action? SessiyaTugadi;

    // ---------- §8.18: token muddati, fon rejimida uzaytirish, 401 da qayta kirish ----------
    /// <summary>Sinov uchun almashtiriladi (soat siljitish). UTC.</summary>
    public static Func<DateTime> Soat { get; set; } = () => DateTime.UtcNow;
    /// <summary>Muddatgacha min(shu, token umrining yarmi) dan kam qolsa (va foydalanuvchi faol bo'lsa) token uzaytiriladi.</summary>
    public static TimeSpan YangilashChegarasi { get; set; } = TimeSpan.FromHours(6);
    private DateTime _berilgan;
    /// <summary>Oxirgi API so'rovi yoki klaviatura/sichqoncha harakati shu oraliqda bo'lsa — foydalanuvchi faol.</summary>
    public static TimeSpan FaollikOynasi { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Server bergan muddat (UTC).</summary>
    public DateTime? TokenMuddati { get; private set; }
    private DateTime _oxirgiFaollik = DateTime.MinValue;
    private Task? _yangilash;
    private Task<bool>? _qaytaKirish;

    /// <summary>401: joriy ekran ustida parol/PIN so'raladi. true — qayta kirildi (so'rov bir marta qayta yuboriladi), false — to'liq chiqish.</summary>
    public Func<Task<bool>>? QaytaKirishSorovi { get; set; }

    /// <summary>Token almashdi (uzaytirildi yoki qayta kirildi).</summary>
    public event Action? TokenYangilandi;

    public void FaollikBelgila() => _oxirgiFaollik = Soat();

    /// <summary>
    /// Fon so'rovlari (SignalR keltirgan qayta yuklash, taymerlar) faollik hisoblanmaydi — ochiq qoldirilgan kompyuterda token 12 soatda tugaydi.
    /// AsyncLocal: belgilagan async metod va u kutgan chaqiruvlar uchun amal qiladi, chaqiruvchiga qaytmaydi.
    /// </summary>
    private static readonly System.Threading.AsyncLocal<bool> Fon = new();
    public static void FonBelgila() => Fon.Value = true;

    private void TokenniQoy(LoginJavobiDto j)
    {
        Token = j.Token;
        _berilgan = Soat();
        TokenMuddati = j.TokenMuddati.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(j.TokenMuddati, DateTimeKind.Utc) : j.TokenMuddati.ToUniversalTime();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
    }

    /// <summary>Muddati o'tmagan, lekin oz qolgan va foydalanuvchi faol. O'tib ketgan token uzaytirilmaydi — u 401 bilan qayta kirishga o'tadi.</summary>
    public bool YangilashKerak
    {
        get
        {
            if (Token is null || TokenMuddati is not { } m || _qaytaKirish is not null) return false;
            var hozir = Soat();
            var yarmi = (m - _berilgan) / 2;
            var chegara = yarmi < YangilashChegarasi ? yarmi : YangilashChegarasi;   // qisqa (sinov) tokenda ham ishlaydi
            return m > hozir && m - hozir < chegara && hozir - _oxirgiFaollik < FaollikOynasi;
        }
    }

    /// <summary>Kerak bo'lsa fon rejimida /auth/yangila; bir vaqtda bittasi. Aloqa xatosida jim (keyingi tekshiruvda qayta urinadi).</summary>
    public Task YangilaKerakBolsa() => YangilashKerak ? _yangilash ??= Yangila() : Task.CompletedTask;

    private async Task Yangila()
    {
        try
        {
            var j = await Yubor<LoginJavobiDto>(HttpMethod.Post, "auth/yangila", null, ichki: true);
            if (j is not null) { TokenniQoy(j); TokenYangilandi?.Invoke(); }
        }
        catch (ApiXatosi e) when (e.Status is 401 or 403) { SessiyaTugadi?.Invoke(); }   // nofaol yoki bloklangan — to'liq chiqish
        catch (ApiXatosi) { }
        finally { _yangilash = null; }
    }

    /// <summary>Bir nechta so'rov bir vaqtda 401 olsa ham parol bir marta so'raladi.</summary>
    private Task<bool> QaytaKirishKut()
    {
        if (QaytaKirishSorovi is not { } sorov) return Task.FromResult(false);
        return _qaytaKirish ??= Kut();
        async Task<bool> Kut()
        {
            try { return await sorov(); }
            finally { _qaytaKirish = null; }
        }
    }

    // ---------- Auth ----------
    public async Task<LoginJavobiDto> Kirish(string login, string parolYokiPin)
    {
        var javob = await Yubor<LoginJavobiDto>(HttpMethod.Post, "auth/login", new LoginSoroviDto(login, parolYokiPin), tokenli: false)
                    ?? throw new ApiXatosi(Til.F("ServerXatosi", 200), 200);
        TokenniQoy(javob);
        FaollikBelgila();
        TokenYangilandi?.Invoke();
        return javob;
    }

    public void Chiqish()
    {
        Token = null;
        TokenMuddati = null;
        _http.DefaultRequestHeaders.Authorization = null;
    }

    public Task<FoydalanuvchiDto?> Men() => Ol<FoydalanuvchiDto>("me");

    // ---------- Yoqilg'i / aparat ----------
    public Task<List<YoqilgiTuriDto>?> Yoqilgilar() => Ol<List<YoqilgiTuriDto>>("yoqilgilar");
    public Task<List<NarxTarixiDto>?> NarxTarixi() => Ol<List<NarxTarixiDto>>("yoqilgilar/narx-tarixi");
    public Task<YoqilgiTuriDto?> YoqilgiYarat(YoqilgiYaratishDto d) => Yubor<YoqilgiTuriDto>(HttpMethod.Post, "yoqilgilar", d);
    public Task<YoqilgiTuriDto?> YoqilgiTahrirla(int id, YoqilgiTahrirlashDto d) => Yubor<YoqilgiTuriDto>(HttpMethod.Put, $"yoqilgilar/{id}", d);
    public Task YoqilgiOchir(int id) => Yubor<object>(HttpMethod.Delete, $"yoqilgilar/{id}", null);

    public Task<List<AparatDto>?> Aparatlar() => Ol<List<AparatDto>>("aparatlar");
    public Task<AparatDto?> AparatYarat(AparatYaratishDto d) => Yubor<AparatDto>(HttpMethod.Post, "aparatlar", d);
    public Task<AparatDto?> AparatTahrirla(int id, AparatTahrirlashDto d) => Yubor<AparatDto>(HttpMethod.Put, $"aparatlar/{id}", d);

    // ---------- Foydalanuvchilar ----------
    public Task<List<FoydalanuvchiDto>?> Foydalanuvchilar() => Ol<List<FoydalanuvchiDto>>("foydalanuvchilar");
    public Task<List<FoydalanuvchiDto>?> Operatorlar() => Ol<List<FoydalanuvchiDto>>("operatorlar");
    public Task<FoydalanuvchiDto?> FoydalanuvchiYarat(FoydalanuvchiYaratishDto d) => Yubor<FoydalanuvchiDto>(HttpMethod.Post, "foydalanuvchilar", d);
    public Task<FoydalanuvchiDto?> FoydalanuvchiTahrirla(int id, FoydalanuvchiTahrirlashDto d) => Yubor<FoydalanuvchiDto>(HttpMethod.Put, $"foydalanuvchilar/{id}", d);
    public Task<FoydalanuvchiDto?> RuxsatlarniOrnat(int id, RuxsatlarOrnatishDto d) => Yubor<FoydalanuvchiDto>(HttpMethod.Put, $"foydalanuvchilar/{id}/ruxsatlar", d);
    public Task PinOrnat(int id, PinOrnatishDto d) => Yubor<object>(HttpMethod.Post, $"foydalanuvchilar/{id}/pin", d);

    // ---------- Smena ----------
    public Task<List<SmenaDto>?> Smenalar(DateOnly? dan = null, DateOnly? gacha = null, int? operatorId = null) =>
        Ol<List<SmenaDto>>("smenalar" + Sorov(("dan", dan), ("gacha", gacha), ("operatorId", operatorId)));
    /// <summary>Ochiq smena (butun shoxobcha bo'yicha); yo'q bo'lsa null (204).</summary>
    public Task<SmenaTafsilotDto?> JoriySmena() => Ol<SmenaTafsilotDto>("smenalar/joriy");
    /// <summary>Oxirgi yopilgan smena (butun shoxobcha bo'yicha); yo'q bo'lsa null (204).</summary>
    public Task<SmenaTafsilotDto?> OxirgiSmena() => Ol<SmenaTafsilotDto>("smenalar/oxirgi");
    /// <summary>§8.10: natijasiz topshirish ma'lumoti — har kim uchun (to'liq natija faqat boshliq yoki smena egasiga).</summary>
    public Task<SmenaTopshirishDto?> OxirgiTopshirish() => Ol<SmenaTopshirishDto>("smenalar/oxirgi/topshirish");
    public Task<SmenaTafsilotDto?> SmenaTafsiloti(int id) => Ol<SmenaTafsilotDto>($"smenalar/{id}");
    public Task<SmenaDto?> SmenaOch(SmenaOchishDto d) => Yubor<SmenaDto>(HttpMethod.Post, "smenalar/och", d);
    public Task<SmenaDto?> SmenaYop(int id, SmenaYopishDto d) => Yubor<SmenaDto>(HttpMethod.Post, $"smenalar/{id}/yop", d);
    public Task<SmenaTafsilotDto?> KorsatkichTuzat(int id, KorsatkichTuzatishDto d) => Yubor<SmenaTafsilotDto>(HttpMethod.Put, $"smenalar/{id}/korsatkich", d);
    public Task<SmenaTafsilotDto?> NarxKorsatkichiniTuzat(int id, KorsatkichTuzatishDto d) => Yubor<SmenaTafsilotDto>(HttpMethod.Put, $"smenalar/{id}/narx-korsatkichi", d);

    // ---------- Nasiya / xarajat / bak ----------
    /// <summary>operatorId berilsa — faqat shu operator yozgan nasiyalar va javobda Operator xulosasi (§8.12).</summary>
    public Task<NasiyalarDto?> Nasiyalar(string? holat = null, string? q = null, int? operatorId = null) =>
        Ol<NasiyalarDto>("nasiyalar" + Sorov(("holat", holat), ("q", string.IsNullOrWhiteSpace(q) ? null : q.Trim()), ("operatorId", operatorId)));
    /// <summary>Mavjud mijozlar (§8.2): nasiya yozuvlaridan, eng ko'pi 8 ta; q bo'sh bo'lsa — oxirgilari.</summary>
    public Task<List<MijozTaklifDto>?> MijozTakliflari(string? q) =>
        Ol<List<MijozTaklifDto>>("nasiyalar/mijozlar" + Sorov(("q", string.IsNullOrWhiteSpace(q) ? null : q.Trim())));
    public Task<NasiyaTafsilotDto?> NasiyaTafsiloti(int id) => Ol<NasiyaTafsilotDto>($"nasiyalar/{id}");
    public Task<NasiyaDto?> NasiyaYoz(NasiyaYaratishDto d) => Yubor<NasiyaDto>(HttpMethod.Post, "nasiyalar", d);
    public Task<NasiyaDto?> QarzQaytdi(int nasiyaId, NasiyaQaytishiYaratishDto d) => Yubor<NasiyaDto>(HttpMethod.Post, $"nasiyalar/{nasiyaId}/qaytish", d);
    /// <summary>§8.11: dasturdan oldingi qarz to'lovi — ochiq smena hisobiga qaytish sifatida yoziladi.</summary>
    public Task<NasiyaTafsilotDto?> EskiQarz(EskiQarzYaratishDto d) => Yubor<NasiyaTafsilotDto>(HttpMethod.Post, "nasiyalar/eski-qarz", d);
    public Task NasiyaOchir(int id) => Yubor<object>(HttpMethod.Delete, $"nasiyalar/{id}", null);
    public Task QaytishOchir(int id) => Yubor<object>(HttpMethod.Delete, $"nasiyalar/qaytishlar/{id}", null);

    public Task<List<XarajatDto>?> Xarajatlar(int? smenaId = null, DateOnly? dan = null, DateOnly? gacha = null) =>
        Ol<List<XarajatDto>>("xarajatlar" + Sorov(("smenaId", smenaId), ("dan", dan), ("gacha", gacha)));
    public Task<XarajatDto?> XarajatYoz(XarajatYaratishDto d) => Yubor<XarajatDto>(HttpMethod.Post, "xarajatlar", d);
    public Task XarajatOchir(int id) => Yubor<object>(HttpMethod.Delete, $"xarajatlar/{id}", null);

    public Task<AparatDto?> BakKirim(int aparatId, BakKirimYaratishDto d) => Yubor<AparatDto>(HttpMethod.Post, $"aparatlar/{aparatId}/kirim", d);
    public Task<List<BakKirimDto>?> BakKirimlar(int? aparatId = null, DateOnly? dan = null, DateOnly? gacha = null) =>
        Ol<List<BakKirimDto>>("bak-kirimlar" + Sorov(("aparatId", aparatId), ("dan", dan), ("gacha", gacha)));

    // ---------- Operator hisobi / audit / zaxira ----------
    public Task<OperatorHisobDto?> OperatorHisobi(int id, DateOnly? oy = null) => Ol<OperatorHisobDto>($"operatorlar/{id}/hisob" + Sorov(("oy", oy)));
    public Task<HisobHarakatiDto?> HarakatYoz(int id, HarakatYaratishDto d) => Yubor<HisobHarakatiDto>(HttpMethod.Post, $"operatorlar/{id}/harakat", d);
    public Task<List<AuditYozuviDto>?> Audit(int limit, string? tur = null, string? q = null, DateOnly? dan = null, DateOnly? gacha = null) =>
        Ol<List<AuditYozuviDto>>("audit" + Sorov(("limit", limit), ("tur", tur), ("q", string.IsNullOrWhiteSpace(q) ? null : q.Trim()), ("dan", dan), ("gacha", gacha)));
    public Task AuditEksport(AuditEksportDto d) => Yubor<object>(HttpMethod.Post, "audit/eksport", d);
    public Task<ZaxiraJavobiDto?> Zaxira() => Yubor<ZaxiraJavobiDto>(HttpMethod.Post, "zaxira", null);
    public Task<ZaxiraHolatiDto?> ZaxiraHolati() => Ol<ZaxiraHolatiDto>("zaxira/holat");
    public Task<ZaxiraHolatiDto?> ZaxiraTelegram(ZaxiraTelegramTahrirDto d) => Yubor<ZaxiraHolatiDto>(HttpMethod.Put, "zaxira/telegram", d);
    public Task<ZaxiraHolatiDto?> ZaxiraTelegramSinov() => Yubor<ZaxiraHolatiDto>(HttpMethod.Post, "zaxira/telegram/sinov", null);

    // ---------- Boshqaruv / hisobot (serverda hisoblanadi) ----------
    public Task<BoshqaruvDto?> Boshqaruv() => Ol<BoshqaruvDto>("boshqaruv");
    public Task<HisobotDto?> Hisobot(DateOnly dan, DateOnly gacha, int? operatorId, HisobotGuruhi guruh) =>
        Ol<HisobotDto>("hisobot" + Sorov(("dan", dan), ("gacha", gacha), ("operatorId", operatorId), ("guruh", guruh.ToString().ToLowerInvariant())));

    // ---------- Ichki ----------
    private static string Sorov(params (string Nomi, object? Qiymat)[] p)
    {
        var qismlar = p.Where(x => x.Qiymat is not null).Select(x => x.Nomi + "=" + Uri.EscapeDataString(x.Qiymat switch
        {
            DateOnly d => d.ToString("yyyy-MM-dd"),
            _ => Convert.ToString(x.Qiymat, System.Globalization.CultureInfo.InvariantCulture)!,
        })).ToList();
        return qismlar.Count == 0 ? "" : "?" + string.Join("&", qismlar);
    }

    private Task<T?> Ol<T>(string yol) => Yubor<T>(HttpMethod.Get, yol, null);

    /// <summary>
    /// tokenli — 401 qayta kirishga olib boradi. ichki — /auth/yangila: faollik belgilanmaydi, uzaytirish tekshirilmaydi, 401 chaqiruvchiga qaytadi.
    /// 401 da (oddiy so'rov): parol/PIN so'raladi va so'rov bir marta qayta yuboriladi; rad etilsa — SessiyaTugadi.
    /// </summary>
    private async Task<T?> Yubor<T>(HttpMethod usul, string yol, object? tana, bool tokenli = true, bool ichki = false)
    {
        if (tokenli && !ichki)
        {
            if (!Fon.Value) FaollikBelgila();   // faqat foydalanuvchi boshlagan so'rov
            await YangilaKerakBolsa();
        }
        for (var urinish = 0; ; urinish++)
        {
            var yuborilganToken = Token;
            try
            {
                return await BirMarta<T>(usul, yol, tana);
            }
            catch (ApiXatosi e) when (e.Status == 401 && tokenli && !ichki)
            {
                // Boshqa so'rov orqali token allaqachon yangilangan — so'ramasdan qayta yuboriladi
                if (urinish == 0 && Token is not null && Token != yuborilganToken) continue;
                if (urinish == 0 && await QaytaKirishKut()) continue;
                SessiyaTugadi?.Invoke();
                throw new ApiXatosi(Til.T("SessiyaTugadi"), 401);
            }
        }
    }

    private async Task<T?> BirMarta<T>(HttpMethod usul, string yol, object? tana)
    {
        if (Namuna is { } n)
        {
            await Task.Yield();
            var j = n.Javob(usul, yol, tana);
            if (usul == HttpMethod.Post && yol is "auth/login" or "auth/yangila" && j is LoginJavobiDto lj) TokenniQoy(lj);
            return j is T t ? t : default;
        }

        using var sorov = new HttpRequestMessage(usul, yol);
        if (tana is not null) sorov.Content = JsonContent.Create(tana, tana.GetType(), options: Json);

        HttpResponseMessage javob;
        try
        {
            javob = await _http.SendAsync(sorov);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new ApiXatosi(Til.T("AloqaYoq"), 0);
        }

        using (javob)
        {
            if (javob.IsSuccessStatusCode)
            {
                if (javob.StatusCode == HttpStatusCode.NoContent || javob.Content.Headers.ContentLength == 0) return default;
                return await javob.Content.ReadFromJsonAsync<T>(Json);
            }

            // Login (tokensiz) 401 — server matni bilan oddiy xato; boshqalari — Yubor qayta kirishni hal qiladi
            if (javob.StatusCode == HttpStatusCode.Unauthorized && yol != "auth/login")
                throw new ApiXatosi(Til.T("SessiyaTugadi"), 401);

            string xabar;
            int[]? kerakli = null;
            try
            {
                var p = await javob.Content.ReadFromJsonAsync<JsonElement>(Json);
                xabar = p.TryGetProperty("detail", out var d) && d.GetString() is { Length: > 0 } dt ? dt
                      : p.TryGetProperty("title", out var t) && t.GetString() is { Length: > 0 } tt ? tt
                      : Til.F("ServerXatosi", (int)javob.StatusCode);
                if (p.TryGetProperty("kerakliAparatlar", out var ka) && ka.ValueKind == JsonValueKind.Array)
                    kerakli = ka.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()).ToArray();
            }
            catch (Exception)
            {
                xabar = Til.F("ServerXatosi", (int)javob.StatusCode);
            }
            throw new ApiXatosi(xabar, (int)javob.StatusCode, kerakli);
        }
    }
}

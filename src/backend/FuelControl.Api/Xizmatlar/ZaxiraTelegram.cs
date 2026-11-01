using System.IO.Compression;
using FuelControl.Api.Auth;
using FuelControl.Api.Data;
using FuelControl.Contracts.Dto;
using FuelControl.Core.Modellar;
using FuelControl.Core.Xizmatlar;
using Microsoft.EntityFrameworkCore;

namespace FuelControl.Api.Xizmatlar;

/// <summary>
/// Zaxira nusxalarini Telegram'ga yuborish. Yuborilmagan nusxa (oxirgi yuborilganidan yangi) oxirgi 2 tagacha ketadi: yangi nusxa va undan
/// oldingi yuborilmagani. Xatoda to'xtaydi (yangilari ham o'tmaydi) — keyingi urinishda qayta. gzip 50 MB dan katta bo'lsa yuborilmaydi va qayta urinilmaydi.
/// Faqat bitta oqimdan chaqiriladi (<see cref="ZaxiraNavbati"/>); sinov — endpointdan.
/// </summary>
public sealed class ZaxiraTelegram(ZaxiraXizmati zaxira, TelegramYuboruvchi telegram, ISirShifrlovchi sir, IServiceScopeFactory scopes,
    IConfiguration konfiguratsiya, ILogger<ZaxiraTelegram> log)
{
    private long MaxBayt => long.TryParse(konfiguratsiya["Zaxira:TelegramMaxBayt"], out var n) && n > 0 ? n : ZaxiraQoidalari.TelegramMaxBayt;

    public string Shoxobcha => konfiguratsiya["Zaxira:Shoxobcha"] is { Length: > 0 } s ? s : Environment.MachineName;

    /// <summary>Sozlama qatori va ochilgan token (yo'q yoki ochib bo'lmasa — null).</summary>
    public (string? Token, string? ChatId) Sirlar(ZaxiraSozlamasi? q) =>
        q?.TelegramTokenShifr is { Length: > 0 } sh && sir.Och(sh) is { Length: > 0 } t ? (t, q.TelegramChatId) : (null, q?.TelegramChatId);

    public static bool Faol(ZaxiraSozlamasi? q, string? token) =>
        q is { TelegramYoqilgan: true } && !string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(q.TelegramChatId);

    public ZaxiraTelegramDto Dto(ZaxiraSozlamasi? q)
    {
        var (token, _) = Sirlar(q);
        return new ZaxiraTelegramDto(q?.TelegramYoqilgan ?? true, Faol(q, token), ZaxiraQoidalari.Maska(token), q?.TelegramChatId,
            q?.YuborishVaqti, q?.YuborishMuvaffaqiyat, q?.YuborishXabari, q?.YuborishTuri);
    }

    public async Task<bool> FaolmiAsync(CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var q = await scope.ServiceProvider.GetRequiredService<FuelControlDbContext>().ZaxiraSozlamalari.AsNoTracking().FirstOrDefaultAsync(ct);
        return Faol(q, Sirlar(q).Token);
    }

    public async Task NusxalarniYubor(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FuelControlDbContext>();
        var q = await db.ZaxiraSozlamalari.FirstOrDefaultAsync(ct);
        var (token, chat) = Sirlar(q);
        if (q is null || !Faol(q, token)) return;

        var fayllar = zaxira.Fayllar();
        // Belgi (oxirgi qayta ishlangan nusxa) bo'lmasa — hammasi yangi (Telegram yoqilganda belgi eng yangi nusxaga qo'yiladi).
        var belgiNomi = q.OxirgiYuborilganFayl;
        var belgi = belgiNomi is null ? null : ZaxiraQoidalari.Tahlil(belgiNomi);
        var yuboriladigan = fayllar
            .Where(f => belgi is null || ZaxiraNomi.Solishtir(f.Nom, Path.GetFileName(f.ToliqYol), belgi, belgiNomi!) > 0)
            .OrderBy(f => f.Nom.Vaqt).ThenBy(f => f.Nom.Takror).ThenBy(f => Path.GetFileName(f.ToliqYol), StringComparer.Ordinal).TakeLast(2).ToList();

        foreach (var f in yuboriladigan)
        {
            var nom = Path.GetFileName(f.ToliqYol);
            var gz = Path.Combine(Path.GetTempPath(), $"fc-zaxira-{Guid.NewGuid():N}.db.gz");
            try
            {
                var hajm = await Siq(f.ToliqYol, gz, ct);
                if (hajm > MaxBayt)
                {
                    Yoz(q, false, $"{nom}: gzip hajmi {ZaxiraQoidalari.Hajm(hajm)} — Telegram chegarasidan ({ZaxiraQoidalari.Hajm(MaxBayt)}) katta, yuborilmadi.");
                    q.OxirgiYuborilganFayl = nom;
                    log.LogWarning("Zaxira nusxa Telegram'ga yuborilmadi: {Fayl} gzip {Hajm} (chegara {Chegara})", nom, hajm, MaxBayt);
                    await db.SaveChangesAsync(ct);
                    continue;
                }
                var izoh = ZaxiraQoidalari.Izoh(Shoxobcha, f.Nom.Vaqt, f.Nom.Sabab, f.Nom.SmenaId, hajm);
                var natija = await telegram.HujjatYubor(token!, chat!, gz, nom + ".gz", izoh, ct);
                if (natija.Muvaffaqiyat)
                {
                    Yoz(q, true, $"{nom}.gz · {ZaxiraQoidalari.Hajm(hajm)}");
                    q.OxirgiYuborilganFayl = nom;
                    log.LogInformation("Zaxira nusxa Telegram'ga yuborildi: {Fayl} ({Hajm})", nom, ZaxiraQoidalari.Hajm(hajm));
                }
                else
                {
                    Yoz(q, false, $"{nom}: {natija.Xabar}");
                    log.LogWarning("Zaxira nusxa Telegram'ga yuborilmadi: {Fayl}: {Xato}", nom, natija.Xabar);
                }
                await db.SaveChangesAsync(ct);
                if (!natija.Muvaffaqiyat) return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Yoz(q, false, $"{nom}: nusxani tayyorlab bo'lmadi: {e.Message}");
                log.LogWarning("Telegram uchun nusxani tayyorlab bo'lmadi: {Fayl}: {Xato}", nom, e.Message);
                await db.SaveChangesAsync(ct);
                return;
            }
            finally { try { File.Delete(gz); } catch (IOException) { } }
        }
    }

    /// <summary>Sinov xabari. Token yoki chat ID yo'q bo'lsa — 400. Telegram xatosi istisno emas: holatga yoziladi.</summary>
    public async Task Sinov(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FuelControlDbContext>();
        var q = await db.ZaxiraSozlamalari.FirstOrDefaultAsync(ct);
        var (token, chat) = Sirlar(q);
        if (q is null || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chat))
            throw new BiznesXatosi("Avval bot tokeni va chat ID ni saqlang.", 400);

        var matn = $"FuelControl: sinov xabari · {Shoxobcha} · {DateTime.UtcNow.Add(Vaqt.Toshkent):dd.MM.yyyy HH:mm}";
        var natija = await telegram.XabarYubor(token, chat, matn, ct);
        Yoz(q, natija.Muvaffaqiyat, natija.Muvaffaqiyat ? "Sinov xabari yuborildi" : natija.Xabar, "sinov");
        if (!natija.Muvaffaqiyat) log.LogWarning("Telegram sinov xabari yuborilmadi: {Xato}", natija.Xabar);
        await db.SaveChangesAsync(ct);
    }

    private static void Yoz(ZaxiraSozlamasi q, bool ok, string xabar, string tur = "nusxa")
    {
        q.YuborishVaqti = DateTime.UtcNow;
        q.YuborishMuvaffaqiyat = ok;
        q.YuborishXabari = xabar.Length > 500 ? xabar[..500] : xabar;
        q.YuborishTuri = tur;
    }

    private static async Task<long> Siq(string manba, string nishon, CancellationToken ct)
    {
        await using (var kirish = File.OpenRead(manba))
        await using (var chiqish = File.Create(nishon))
        await using (var gzip = new GZipStream(chiqish, CompressionLevel.Optimal))
            await kirish.CopyToAsync(gzip, ct);
        return new FileInfo(nishon).Length;
    }
}

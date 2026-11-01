using FuelControl.Api.Data;
using FuelControl.Contracts.Dto;
using FuelControl.Core.Xizmatlar;
using Microsoft.Data.Sqlite;

namespace FuelControl.Api.Xizmatlar;

/// <summary>Zaxira papkalari: Zaxira:Papka (sozlangan) va standart papka (Zaxira:StandartPapka, bo'lmasa baza faylining papkasi\zaxira).</summary>
public static class ZaxiraYollari
{
    /// <summary>Baza faylining papkasi (o'rnatuvchida %ProgramData%\FuelControl); topilmasa joriy papka.</summary>
    public static string BazaPapkasi(IConfiguration k)
    {
        var ulanish = k.GetConnectionString("Baza") ?? "Data Source=fuelcontrol.db";
        try { return Path.GetDirectoryName(Path.GetFullPath(new SqliteConnectionStringBuilder(ulanish).DataSource)) ?? Directory.GetCurrentDirectory(); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return Directory.GetCurrentDirectory(); }
    }

    public static string Sozlangan(IConfiguration k) => Path.GetFullPath(k["Zaxira:Papka"] is { Length: > 0 } p ? p : "zaxira");

    public static string Standart(IConfiguration k) =>
        Path.GetFullPath(k["Zaxira:StandartPapka"] is { Length: > 0 } p ? p : Path.Combine(BazaPapkasi(k), "zaxira"));

    public static bool Bir(string a, string b) => string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}

/// <summary>Nusxa tushadigan papka: StandartPapkaga = sozlangan papkaga yozib bo'lmadi (Sabab to'ldirilgan). Sabab != null va StandartPapkaga = false — standart papkaning o'zi yozilmaydi.</summary>
public sealed record PapkaHolati(string Papka, string Sozlangan, string Standart, bool StandartPapkaga, string? Sabab);

public sealed record NusxaNatijasi(string FaylNomi, long HajmBayt, DateTime Vaqt, string Sabab, int? SmenaId, string Papka, bool StandartPapkaga)
{
    public string ToliqYol => Path.Combine(Papka, FaylNomi);
}

/// <summary>
/// SQLite `VACUUM INTO` bilan zaxira nusxa (smena yopilganda va qo'lda; kunlik avtomatik nusxa yo'q). Papka ishlamasa — standart papkaga (zaxira to'xtamaydi);
/// eng yangi Zaxira:Saqlash (30) ta nusxa qoladi. Nusxalar bir vaqtda bittadan olinadi.
/// </summary>
public sealed partial class ZaxiraXizmati(IConfiguration konfiguratsiya, IServiceScopeFactory scopes, ILogger<ZaxiraXizmati> log)
{
    private readonly SemaphoreSlim _qulf = new(1, 1);
    private string? _oxirgiBelgi;

    public int Saqlash => ZaxiraQoidalari.Saqlash(konfiguratsiya["Zaxira:Saqlash"]);

    /// <summary>Papkani tekshiradi (yo'q bo'lsa yaratadi, sinov fayli yozadi); ishlamasa standart papkaga o'tadi. Ikkalasi ham yozilmasa — IOException.</summary>
    public PapkaHolati PapkaniAniqla()
    {
        var sozlangan = ZaxiraYollari.Sozlangan(konfiguratsiya);
        var standart = ZaxiraYollari.Standart(konfiguratsiya);
        var xato = Sinash(sozlangan);
        PapkaHolati h;
        if (xato is null) h = new(sozlangan, sozlangan, standart, false, null);
        else if (ZaxiraYollari.Bir(sozlangan, standart)) h = new(standart, sozlangan, standart, false, $"{sozlangan} ga yozib bo'lmadi: {xato}");
        else
        {
            var xato2 = Sinash(standart);
            var sabab = $"{sozlangan} ga yozib bo'lmadi: {xato}";
            if (xato2 is not null) throw new IOException($"{sabab}; standart papka ({standart}) ham yozilmaydi: {xato2}");
            h = new(standart, sozlangan, standart, true, sabab);
        }
        Belgila(h);
        return h;
    }

    /// <summary>Holat so'rovi uchun: ikkala papka ham yozilmasa istisno emas, ogohlantirish matni bilan standart papka qaytadi.</summary>
    public PapkaHolati PapkaniAniqlaYumshoq()
    {
        try { return PapkaniAniqla(); }
        catch (IOException e)
        {
            var standart = ZaxiraYollari.Standart(konfiguratsiya);
            return new PapkaHolati(standart, ZaxiraYollari.Sozlangan(konfiguratsiya), standart, false, e.Message);
        }
    }

    private void Belgila(PapkaHolati h)
    {
        var belgi = $"{h.StandartPapkaga}|{h.Papka}|{h.Sabab}";
        if (belgi == _oxirgiBelgi) return;
        var avval = _oxirgiBelgi;
        _oxirgiBelgi = belgi;
        if (h.Sabab is not null)
            log.LogWarning("Zaxira papkasi ishlamayapti: {Sabab}. Nusxalar {Papka} ga olinmoqda.", h.Sabab, h.Papka);
        else if (avval is not null)
            log.LogInformation("Zaxira papkasi qayta ishlayapti: nusxalar {Papka} ga olinmoqda.", h.Papka);
    }

    /// <summary>
    /// Papkaga yozib bo'lmaslik sababi - foydalanuvchiga tushunarli o'zbekcha qisqa matn (Sozlamalardagi holat). Istisnoning asl matni
    /// (ko'pincha inglizcha, yo'l bilan) faqat logga yoziladi.
    /// </summary>
    public static string YozolmaslikSababi(Exception e) => e switch
    {
        DirectoryNotFoundException or DriveNotFoundException => "disk yoki papka topilmadi (disk ulanmaganmi?)",
        UnauthorizedAccessException => "papkaga yozish uchun ruxsat yo'q",
        IOException io when DiskToldi(io) => "diskda joy yo'q",
        _ => "yozib bo'lmadi",
    };

    // Windows: ERROR_HANDLE_DISK_FULL (0x27) va ERROR_DISK_FULL (0x70); Unix: ENOSPC (28).
    private static bool DiskToldi(IOException e) => (e.HResult & 0xFFFF) is 0x27 or 0x70 || e.HResult == 28;

    private string? Sinash(string papka)
    {
        try
        {
            Directory.CreateDirectory(papka);
            var sinov = Path.Combine(papka, $".yozish-sinovi-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(sinov, "x");
            File.Delete(sinov);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            log.LogWarning("Zaxira papkasiga yozib bo'lmadi: {Papka}: {Xato}", papka, e.Message);       // asl matn - faqat logda
            return YozolmaslikSababi(e);
        }
    }
}

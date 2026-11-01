using System.Globalization;
using System.Text.RegularExpressions;

namespace FuelControl.Core.Xizmatlar;

/// <summary>Nusxa fayl nomi: fuelcontrol-yyyyMMdd-HHmmss[-sabab][-takror].db (UTC). Sabab bo'lmasa (eski nomlar) — "".</summary>
/// <summary>Takror: bir soniyada olingan nusxalar tartibi (1 - birinchisi, "-2" qo'shimchali - ikkinchisi...).</summary>
public sealed record ZaxiraNomi(DateTime Vaqt, string Sabab, int? SmenaId, int Takror = 1)
{
    /// <summary>Vaqt, takror, nom bo'yicha tartib (nom - teng bo'lganda barqaror tartib uchun).</summary>
    public static int Solishtir(ZaxiraNomi a, string nomA, ZaxiraNomi b, string nomB)
    {
        var c = a.Vaqt.CompareTo(b.Vaqt);
        if (c == 0) c = a.Takror.CompareTo(b.Takror);
        return c != 0 ? c : string.CompareOrdinal(nomA, nomB);
    }
}

/// <summary>Zaxira nusxa va Telegram qoidalari (fayl nomi, saqlash soni, token/chat tekshiruvi, izoh matni) — fayl va tarmoqsiz, sinash oson.</summary>
public static class ZaxiraQoidalari
{
    public const string Kunlik = "kunlik";
    public const string Smena = "smena";
    public const string Qolda = "qolda";

    public const int StandartSaqlash = 30;

    /// <summary>Bot API hujjat chegarasi (gzip'dan keyin). Aniq 50 MB emas, kichik zaxira bilan.</summary>
    public const long TelegramMaxBayt = 50_000_000;

    private static readonly Regex NomAndozasi = new(
        @"^fuelcontrol-(?<d>\d{8})-(?<t>\d{6})(?:-(?<s>kunlik|qolda|smena(?<id>\d+)))?(?:-(?<k>\d+))?\.db$", RegexOptions.CultureInvariant);

    private static readonly Regex TokenAndozasi = new(@"^\d{6,12}:[A-Za-z0-9_-]{25,60}$", RegexOptions.CultureInvariant);
    private static readonly Regex ChatAndozasi = new(@"^(-?\d{1,20}|@[A-Za-z][A-Za-z0-9_]{4,31})$", RegexOptions.CultureInvariant);

    public static string Nom(DateTime utc, string sabab, int? smenaId = null, int takror = 0)
    {
        var s = sabab switch
        {
            Smena when smenaId is { } id => $"-smena{id}",
            Kunlik => "-kunlik",
            Qolda => "-qolda",
            _ => "",
        };
        return $"fuelcontrol-{utc:yyyyMMdd-HHmmss}{s}{(takror > 0 ? "-" + (takror + 1) : "")}.db";
    }

    /// <summary>Yaroqsiz nom (zaxira fayli emas) — null.</summary>
    public static ZaxiraNomi? Tahlil(string fayl)
    {
        var m = NomAndozasi.Match(Path.GetFileName(fayl));
        if (!m.Success) return null;
        if (!DateTime.TryParseExact(m.Groups["d"].Value + m.Groups["t"].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var vaqt)) return null;
        var sabab = m.Groups["id"].Success ? Smena : m.Groups["s"].Success ? m.Groups["s"].Value : "";
        int? id = m.Groups["id"].Success && int.TryParse(m.Groups["id"].Value, out var n) ? n : null;
        var takror = m.Groups["k"].Success && int.TryParse(m.Groups["k"].Value, out var k) ? k : 1;
        return new ZaxiraNomi(DateTime.SpecifyKind(vaqt, DateTimeKind.Utc), sabab, id, takror);
    }

    /// <summary>Zaxira Saqlash sozlamasi: bo'sh, son emas yoki 1 dan kichik bo'lsa — 30.</summary>
    public static int Saqlash(string? sozlama) =>
        int.TryParse(sozlama, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1 ? n : StandartSaqlash;

    /// <summary>Eng yangi <paramref name="saqlash"/> ta nusxa qoladi (nom bo'yicha: vaqt nomda), qolganlari — o'chiriladigan ro'yxat (eskisi birinchi).</summary>
    public static IReadOnlyList<string> OchiriladiganlarNomi(IEnumerable<string> nomlar, int saqlash) =>
        nomlar.Where(n => Tahlil(n) is not null).OrderByDescending(n => n, StringComparer.Ordinal).Skip(Math.Max(1, saqlash)).Reverse().ToList();

    /// <summary>Telegram bot tokeni: "123456789:AAH..." ko'rinishi.</summary>
    public static bool TokenYaroqli(string? token) => token is not null && TokenAndozasi.IsMatch(token);

    /// <summary>Chat ID: butun son (guruhlarda manfiy) yoki @kanal_nomi.</summary>
    public static bool ChatYaroqli(string? chat) => chat is not null && ChatAndozasi.IsMatch(chat);

    /// <summary>"…ab12" — tokenning oxirgi 4 belgisi; qisqa/yo'q token uchun null yoki faqat "…".</summary>
    public static string? Maska(string? token) =>
        string.IsNullOrEmpty(token) ? null : token.Length < 12 ? "…" : "…" + token[^4..];

    /// <summary>Matndagi tokenni (ochiq va URL ko'rinishida) "[token]" bilan almashtiradi: log va holatga token tushmasligi uchun.</summary>
    public static string TokenniYashir(string? matn, string? token)
    {
        if (string.IsNullOrEmpty(matn) || string.IsNullOrEmpty(token)) return matn ?? "";
        return matn.Replace(token, "[token]").Replace(Uri.EscapeDataString(token), "[token]");
    }

    public static string Hajm(long bayt) => bayt switch
    {
        < 1024 => $"{bayt} B",
        < 1024 * 1024 => (bayt / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + " KB",
        _ => (bayt / 1048576d).ToString("0.#", CultureInfo.InvariantCulture) + " MB",
    };

    public static string SababMatni(string sabab, int? smenaId) => sabab switch
    {
        Kunlik => "kunlik",
        Smena when smenaId is { } id => $"smena #{id} yopildi",
        Qolda => "qo'lda",
        _ => "—",
    };

    /// <summary>Telegram izohi (hujjat tagida): shoxobcha · Toshkent sanasi va vaqti · sabab · hajm. Bot API izohi — 1024 belgigacha.</summary>
    public static string Izoh(string shoxobcha, DateTime utc, string sabab, int? smenaId, long hajm)
    {
        var mahalliy = utc.Add(Vaqt.Toshkent);
        var matn = $"{shoxobcha} · {mahalliy:dd.MM.yyyy HH:mm} · {SababMatni(sabab, smenaId)} · {Hajm(hajm)}";
        return matn.Length <= 1000 ? matn : matn[..1000];
    }
}

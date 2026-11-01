using System.Security.Cryptography;
using System.Text;

namespace FuelControl.Api.Xizmatlar;

/// <summary>
/// Sirni (Telegram bot tokeni) bazada ochiq yozmaslik uchun shifrlash. Kalit qayta o'rnatishda yo'qolmasligi shart, shuning uchun JWT kaliti emas:
/// Windows'da DPAPI (LocalMachine: xizmat LocalSystem'da ham, foydalanuvchi sifatida ham ochadi; nusxalar boshqa kompyuterda ochilmaydi),
/// boshqa tizimda (Docker) ma'lumot papkasidagi alohida kalit fayli (zaxira nusxaga tushmaydi). Ochib bo'lmasa — null ("sir yo'q").
/// </summary>
public interface ISirShifrlovchi
{
    string Shifrla(string ochiq);
    string? Och(string shifr);
}

public sealed class SirShifrlovchi(IConfiguration konfiguratsiya, ILogger<SirShifrlovchi> log) : ISirShifrlovchi
{
    private const string DpapiBelgisi = "dpapi1:";
    private const string KalitBelgisi = "aes1:";
    private static readonly byte[] Entropiya = Encoding.UTF8.GetBytes("FuelControl.Zaxira.Telegram.v1");
    private readonly object _qulf = new();
    private byte[]? _kalit;

    /// <summary>Windows'da standart — DPAPI; Zaxira:FaylKalitiMajburiy = true bo'lsa (va boshqa tizimlarda) kalit fayli.</summary>
    private bool FaylKalitiMajburiy => konfiguratsiya.GetValue<bool>("Zaxira:FaylKalitiMajburiy");

    public string Shifrla(string ochiq)
    {
        var bayt = Encoding.UTF8.GetBytes(ochiq);
        if (OperatingSystem.IsWindows() && !FaylKalitiMajburiy)
            return DpapiBelgisi + Convert.ToBase64String(ProtectedData.Protect(bayt, Entropiya, DataProtectionScope.LocalMachine));

        var nonce = RandomNumberGenerator.GetBytes(12);
        var shifr = new byte[bayt.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(Kalit(), 16)) aes.Encrypt(nonce, bayt, shifr, tag);
        return KalitBelgisi + Convert.ToBase64String([.. nonce, .. tag, .. shifr]);
    }

    public string? Och(string shifr)
    {
        try
        {
            if (shifr.StartsWith(DpapiBelgisi, StringComparison.Ordinal))
            {
                if (!OperatingSystem.IsWindows()) return null;
                var ochiq = ProtectedData.Unprotect(Convert.FromBase64String(shifr[DpapiBelgisi.Length..]), Entropiya, DataProtectionScope.LocalMachine);
                return Encoding.UTF8.GetString(ochiq);
            }
            if (shifr.StartsWith(KalitBelgisi, StringComparison.Ordinal))
            {
                var hammasi = Convert.FromBase64String(shifr[KalitBelgisi.Length..]);
                if (hammasi.Length < 29) return null;
                var ochiq = new byte[hammasi.Length - 28];
                using var aes = new AesGcm(Kalit(), 16);
                aes.Decrypt(hammasi.AsSpan(0, 12), hammasi.AsSpan(28), hammasi.AsSpan(12, 16), ochiq);
                return Encoding.UTF8.GetString(ochiq);
            }
        }
        catch (Exception e) when (e is CryptographicException or FormatException or IOException)
        {
            // Matn yoki kalit haqida hech narsa yozilmaydi: faqat sabab turi.
            log.LogWarning("Saqlangan Telegram tokenini ochib bo'lmadi ({Tur}); token \"yo'q\" deb hisoblanadi, uni qayta kiriting.", e.GetType().Name);
        }
        return null;
    }

    private byte[] Kalit()
    {
        lock (_qulf)
        {
            if (_kalit is not null) return _kalit;
            var yol = konfiguratsiya["Zaxira:KalitFayli"] ?? Path.Combine(ZaxiraYollari.BazaPapkasi(konfiguratsiya), "zaxira-telegram.kalit");
            if (File.Exists(yol)) return _kalit = Convert.FromBase64String(File.ReadAllText(yol).Trim());
            var yangi = RandomNumberGenerator.GetBytes(32);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(yol))!);
            File.WriteAllText(yol, Convert.ToBase64String(yangi));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(yol, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return _kalit = yangi;
        }
    }
}

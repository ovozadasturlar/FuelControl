using System.Net.Http.Headers;
using System.Text.Json;
using FuelControl.Core.Xizmatlar;

namespace FuelControl.Api.Xizmatlar;

public sealed record TelegramNatijasi(bool Muvaffaqiyat, string Xabar);

/// <summary>
/// Telegram Bot API (sendMessage, sendDocument). HttpClient IHttpClientFactory'dan "telegram" nomi bilan olinadi (testlarda soxta handler).
/// Token so'rov manzilida bo'lgani uchun shu mijozning standart HTTP logi o'chirilgan (Program.cs), xato matnlaridan token olib tashlanadi.
/// Tarmoq/Telegram xatosi istisno emas — <see cref="TelegramNatijasi"/> (Muvaffaqiyat = false).
/// </summary>
public sealed class TelegramYuboruvchi(IHttpClientFactory fabrika, ILogger<TelegramYuboruvchi> log)
{
    public const string MijozNomi = "telegram";

    public Task<TelegramNatijasi> XabarYubor(string token, string chatId, string matn, CancellationToken ct) =>
        Yubor(token, "sendMessage", new FormUrlEncodedContent(new Dictionary<string, string> { ["chat_id"] = chatId, ["text"] = matn }), TimeSpan.FromSeconds(30), ct);

    /// <summary>fayl — yuboriladigan (gzip) fayl yo'li; hujjatNomi — Telegram'da ko'rinadigan nom.</summary>
    public async Task<TelegramNatijasi> HujjatYubor(string token, string chatId, string fayl, string hujjatNomi, string izoh, CancellationToken ct)
    {
        try
        {
            await using var oqim = File.OpenRead(fayl);
            var hujjat = new StreamContent(oqim);
            hujjat.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
            using var shakl = new MultipartFormDataContent
            {
                { new StringContent(chatId), "chat_id" },
                { new StringContent(izoh), "caption" },
                { hujjat, "document", hujjatNomi },
            };
            return await Yubor(token, "sendDocument", shakl, TimeSpan.FromMinutes(5), ct);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(false, $"Yuboriladigan faylni o'qib bo'lmadi: {e.Message}");
        }
    }

    private async Task<TelegramNatijasi> Yubor(string token, string metod, HttpContent kontent, TimeSpan muhlat, CancellationToken ct)
    {
        using var muhlatTokeni = CancellationTokenSource.CreateLinkedTokenSource(ct);
        muhlatTokeni.CancelAfter(muhlat);
        try
        {
            using var mijoz = fabrika.CreateClient(MijozNomi);
            // Boshida "/" shart: "bot123:AAH..." nisbiy manzilni .NET "bot123" sxemali absolyut manzil deb o'qiydi.
            using var javob = await mijoz.PostAsync($"/bot{token}/{metod}", kontent, muhlatTokeni.Token);
            var matn = await javob.Content.ReadAsStringAsync(muhlatTokeni.Token);
            var (ok, tavsif) = Oqi(matn);
            if (javob.IsSuccessStatusCode && ok) return new(true, "");
            return new(false, Yashir(token, $"Telegram: {tavsif ?? $"HTTP {(int)javob.StatusCode}"}"));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, "Telegram javob bermadi (vaqt tugadi).");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            var matn = Yashir(token, $"Telegram'ga ulanib bo'lmadi: {e.Message}");
            log.LogWarning("Telegram so'rovi bajarilmadi ({Metod}): {Xato}", metod, matn);
            return new(false, matn);
        }
    }

    private static (bool Ok, string? Tavsif) Oqi(string matn)
    {
        try
        {
            using var doc = JsonDocument.Parse(matn);
            var ildiz = doc.RootElement;
            var ok = ildiz.TryGetProperty("ok", out var o) && o.ValueKind == JsonValueKind.True;
            var tavsif = ildiz.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            return (ok, tavsif);
        }
        catch (JsonException) { return (false, null); }
    }

    private static string Yashir(string token, string matn) => ZaxiraQoidalari.TokenniYashir(matn, token);
}

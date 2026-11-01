using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace FuelControl.Api.Tests;

/// <summary>Soxta Telegram: har so'rovni yozib oladi (metod, forma maydonlari, hujjat), javobni sinov belgilaydi. Tarmoqqa chiqmaydi.</summary>
public sealed class SoxtaTelegram : HttpMessageHandler
{
    /// <summary>Mezbon - "sxema://host" (haqiqiy HttpClient manzilni to'g'ri yig'ganini tekshirish uchun).</summary>
    public sealed record Sorov(string Yol, string Metod, Dictionary<string, string> Maydonlar, string? HujjatNomi, byte[]? Hujjat, string Mezbon = "");

    private readonly object _qulf = new();
    private readonly List<Sorov> _sorovlar = [];

    /// <summary>Navbatdagi javoblar (bo'sh bo'lsa - muvaffaqiyat). Har so'rov bittasini oladi.</summary>
    public ConcurrentQueue<Func<HttpResponseMessage>> Javoblar { get; } = new();

    public Sorov[] Sorovlar { get { lock (_qulf) return [.. _sorovlar]; } }
    public Sorov[] Hujjatlar => [.. Sorovlar.Where(s => s.Metod == "sendDocument")];
    public Sorov[] Xabarlar => [.. Sorovlar.Where(s => s.Metod == "sendMessage")];

    public static HttpResponseMessage Ok() => Json(HttpStatusCode.OK, """{"ok":true,"result":{}}""");

    public static HttpResponseMessage Xato(int kod, string tavsif) =>
        Json((HttpStatusCode)kod, $$"""{"ok":false,"error_code":{{kod}},"description":"{{tavsif}}"}""");

    public static Func<HttpResponseMessage> Istisno(Exception e) => () => throw e;

    private static HttpResponseMessage Json(HttpStatusCode kod, string matn) => new(kod) { Content = new StringContent(matn, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
    {
        var tana = r.Content is null ? [] : await r.Content.ReadAsByteArrayAsync(ct);
        var sorov = Oqi(r.RequestUri!.AbsolutePath, r.Content?.Headers.ContentType, tana) with { Mezbon = $"{r.RequestUri.Scheme}://{r.RequestUri.Host}" };
        lock (_qulf) _sorovlar.Add(sorov);
        return Javoblar.TryDequeue(out var javob) ? javob() : Ok();
    }

    private static Sorov Oqi(string yol, System.Net.Http.Headers.MediaTypeHeaderValue? turi, byte[] tana)
    {
        var metod = yol[(yol.LastIndexOf('/') + 1)..];
        var maydonlar = new Dictionary<string, string>();
        string? nom = null;
        byte[]? hujjat = null;
        var chegara = turi?.Parameters.FirstOrDefault(p => p.Name == "boundary")?.Value?.Trim('"');
        var matn = Encoding.Latin1.GetString(tana);                                  // 1:1 bayt: bo'laklarni aniq kesish uchun
        if (chegara is null)
        {
            foreach (var juft in matn.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var i = juft.IndexOf('=');
                maydonlar[Uri.UnescapeDataString(juft[..i])] = Uri.UnescapeDataString(juft[(i + 1)..].Replace('+', ' '));
            }
        }
        else
        {
            foreach (var qism in matn.Split("--" + chegara).Skip(1))
            {
                var bosh = qism.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (bosh < 0) continue;
                var sarlavha = qism[..bosh];
                var mazmun = qism[(bosh + 4)..];
                if (mazmun.EndsWith("\r\n", StringComparison.Ordinal)) mazmun = mazmun[..^2];
                var ism = Regex.Match(sarlavha, @"name=""?([^"";\r\n]+)""?").Groups[1].Value;
                var bayt = Encoding.Latin1.GetBytes(mazmun);
                if (ism == "document")
                {
                    hujjat = bayt;
                    nom = Regex.Match(sarlavha, @"filename=""?([^"";\r\n]+)""?").Groups[1].Value;
                }
                else maydonlar[ism] = Encoding.UTF8.GetString(bayt);
            }
        }
        return new Sorov(yol, metod, maydonlar, nom, hujjat);
    }
}

/// <summary>Barcha log yozuvlarini (kategoriya, daraja, matn, istisno) yig'adi: token logga tushmaganini tekshirish uchun.</summary>
public sealed class LogYigish : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _yozuvlar = new();
    public string[] Yozuvlar => [.. _yozuvlar];
    public string Hammasi => string.Join('\n', _yozuvlar);

    public ILogger CreateLogger(string kategoriya) => new Yozuvchi(kategoriya, _yozuvlar);
    public void Dispose() { }

    private sealed class Yozuvchi(string kategoriya, ConcurrentQueue<string> yozuvlar) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel daraja) => true;
        public void Log<TState>(LogLevel daraja, EventId id, TState state, Exception? istisno, Func<TState, Exception?, string> format) =>
            yozuvlar.Enqueue($"[{daraja}] {kategoriya}: {format(state, istisno)}{(istisno is null ? "" : "\n" + istisno)}");
    }
}

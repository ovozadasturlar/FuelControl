using System.Threading.Channels;
using FuelControl.Api.Data;
using FuelControl.Core.Xizmatlar;

namespace FuelControl.Api.Xizmatlar;

/// <summary>
/// Zaxira ishlari navbati (bitta fon oqimi, ketma-ket): smena yopilgandan keyingi nusxa va Telegram'ga yuborish. Chaqiruvchi (masalan, smena yopish)
/// faqat navbatga qo'yadi va kutmaydi; xato yopishni buzmaydi — shu yerda ushlanib logga yoziladi.
/// </summary>
public sealed class ZaxiraNavbati(ZaxiraXizmati zaxira, ZaxiraTelegram telegram, ILogger<ZaxiraNavbati> log) : BackgroundService
{
    private abstract record Ish;
    private sealed record SmenaIshi(int SmenaId) : Ish;
    private sealed record YuborishIshi : Ish;

    private readonly Channel<Ish> _kanal = Channel.CreateUnbounded<Ish>(new UnboundedChannelOptions { SingleReader = true });
    private int _kutilmoqda;

    /// <summary>Navbatda ham, bajarilayotgan ham ish yo'q (testlar natijani kutishi uchun).</summary>
    public bool Bosh => Volatile.Read(ref _kutilmoqda) == 0;

    /// <summary>Smena yopildi: nusxa ("smenaN") olinadi va Telegram'ga yuboriladi. Darhol qaytadi.</summary>
    public void SmenaYopildi(int smenaId) => Qosh(new SmenaIshi(smenaId));

    /// <summary>Yuborilmagan nusxalarni Telegram'ga yuborish (nusxa olingach va soatlik qayta urinishda). Darhol qaytadi.</summary>
    public void Yuborish() => Qosh(new YuborishIshi());

    private void Qosh(Ish ish)
    {
        Interlocked.Increment(ref _kutilmoqda);
        if (!_kanal.Writer.TryWrite(ish)) Interlocked.Decrement(ref _kutilmoqda);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var ish in _kanal.Reader.ReadAllAsync(ct))
        {
            try
            {
                if (ish is SmenaIshi s) await zaxira.NusxaOl(ZaxiraQoidalari.Smena, s.SmenaId, ct);
                await telegram.NusxalarniYubor(ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Zaxira ishi bajarilmadi ({Ish})", ish.GetType().Name);
            }
            finally { Interlocked.Decrement(ref _kutilmoqda); }
        }
    }
}

/// <summary>
/// Soatiga bir: yuborilmagan zaxira nusxalarni Telegram'ga qayta yuboradi va yangi oy uchun maoshlarni yozadi. Avtomatik nusxa ENDI FAQAT smena
/// yopilganda olinadi (docs 8.17), bu yerda nusxa olinmaydi; nomi eskicha — chunki xizmat o'sha.
/// </summary>
public sealed class KunlikIshlarFonXizmati(ZaxiraNavbati navbat, IServiceScopeFactory scopes, ILogger<KunlikIshlarFonXizmati> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var taymer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            navbat.Yuborish();
            try
            {
                using var scope = scopes.CreateScope();
                await MaoshYozuvchi.Yoz(scope.ServiceProvider.GetRequiredService<FuelControlDbContext>());
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Kunlik ish bajarilmadi");
            }
        } while (await taymer.WaitForNextTickAsync(ct));
    }
}

using FuelControl.Api.Auth;
using FuelControl.Api.Data;
using FuelControl.Api.Xizmatlar;
using FuelControl.Contracts;
using FuelControl.Contracts.Dto;
using FuelControl.Core.Modellar;
using FuelControl.Core.Xizmatlar;
using Microsoft.EntityFrameworkCore;

namespace FuelControl.Api.Endpointlar;

/// <summary>Zaxira nusxa va Telegram sozlamalari (docs 8.17). Hammasi "Sozlamalar" ruxsati bilan.</summary>
public static class ZaxiraEndpointlari
{
    public static void ZaxiraUlash(this IEndpointRouteBuilder app)
    {
        var z = app.MapGroup("/zaxira").RequireAuthorization();

        // Hozir nusxa olish (qo'lda). Telegram'ga yuborish fonda; natijasi GET /zaxira/holat da.
        z.MapPost("/", async (HttpContext ctx, FuelControlDbContext db, ZaxiraXizmati zaxira, ZaxiraTelegram telegram, ZaxiraNavbati navbat) =>
        {
            var n = await zaxira.NusxaOl(ZaxiraQoidalari.Qolda, null, ctx.RequestAborted);
            Audit.Yoz(db, ctx.User.Ism(), "Zaxira nusxa olindi", n.FaylNomi, AuditTurlari.Sozlama);
            await db.SaveChangesAsync();
            var yuboriladi = await telegram.FaolmiAsync(ctx.RequestAborted);
            if (yuboriladi) navbat.Yuborish();
            return Results.Ok(new ZaxiraJavobiDto(n.FaylNomi, n.HajmBayt, n.Vaqt, n.Sabab, n.Papka, n.StandartPapkaga, yuboriladi));
        }).RuxsatKerak(Ruxsat.Sozlamalar).Produces<ZaxiraJavobiDto>().ProducesProblem(403);

        z.MapGet("/holat", async (FuelControlDbContext db, ZaxiraXizmati zaxira, ZaxiraTelegram telegram) =>
            Results.Ok(await Holat(db, zaxira, telegram)))
            .RuxsatKerak(Ruxsat.Sozlamalar).Produces<ZaxiraHolatiDto>().ProducesProblem(403);

        // null - o'zgarmaydi; Token/ChatId "" - o'chiriladi. Javob - yangilangan holat (token faqat maskalangan).
        z.MapPut("/telegram", async (ZaxiraTelegramTahrirDto t, HttpContext ctx, FuelControlDbContext db, ZaxiraXizmati zaxira, ZaxiraTelegram telegram, ISirShifrlovchi sir, ILogger<ZaxiraTelegram> log) =>
        {
            // Noto'g'ri format - kutilgan holat: istisno emas (jurnalga stek bilan tushmasin), faqat qisqa ogohlantirish; kiritilgan qiymat va namuna yozilmaydi.
            IResult Rad(string sabab, string xabar)
            {
                log.LogWarning("Telegram sozlamasi saqlanmadi: {Sabab}", sabab);
                return Results.Problem(statusCode: 400, title: "So'rov bajarilmadi", detail: xabar);
            }

            var q = await db.ZaxiraSozlamalari.FirstOrDefaultAsync();
            var yangi = q is null;
            q ??= new ZaxiraSozlamasi();
            var tokenOldin = telegram.Sirlar(q).Token;
            var faolOldin = ZaxiraTelegram.Faol(q, tokenOldin);

            bool? tokenYangilandi = null;
            if (t.Token is not null)
            {
                var token = t.Token.Trim();
                if (token.Length == 0) { tokenYangilandi = q.TelegramTokenShifr is null ? null : false; q.TelegramTokenShifr = null; }
                else
                {
                    // Xato matnida kiritilgan qiymat takrorlanmaydi: token hech qayerga chiqmasin.
                    if (!ZaxiraQoidalari.TokenYaroqli(token)) return Rad("bot tokeni formati noto'g'ri", "Bot tokeni noto'g'ri: 123456789:AAH... ko'rinishida bo'lishi kerak.");
                    q.TelegramTokenShifr = sir.Shifrla(token);
                    tokenYangilandi = true;
                }
            }

            var chatOzgardi = false;
            if (t.ChatId is not null)
            {
                var chat = t.ChatId.Trim();
                if (chat.Length > 0 && !ZaxiraQoidalari.ChatYaroqli(chat))
                    return Rad("chat ID formati noto'g'ri", "Chat ID noto'g'ri: butun son (guruhda manfiy) yoki @kanal_nomi bo'lishi kerak.");
                var qiymat = chat.Length == 0 ? null : chat;
                chatOzgardi = qiymat != q.TelegramChatId;
                q.TelegramChatId = qiymat;
            }

            bool? yoqilganOzgardi = null;
            if (t.Yoqilgan is { } y && (y != q.TelegramYoqilgan || yangi)) { q.TelegramYoqilgan = y; yoqilganOzgardi = y; }

            // Yuborish yoqilgan paytdan boshlab: undan oldingi nusxalar "yuborilmagan" hisoblanmaydi (birinchi nusxa o'tmaguncha eski fayllar oqib ketmasin).
            var tokenEndi = t.Token is null ? tokenOldin : t.Token.Trim().Length == 0 ? null : t.Token.Trim();
            if (!faolOldin && ZaxiraTelegram.Faol(q, tokenEndi))
                q.OxirgiYuborilganFayl = zaxira.Fayllar().Select(f => Path.GetFileName(f.ToliqYol)).FirstOrDefault();

            if (yangi) db.ZaxiraSozlamalari.Add(q);
            var matn = AuditMatnlari.TelegramSozlandi(tokenYangilandi, q.TelegramChatId, chatOzgardi, yoqilganOzgardi);
            if (matn.Length > 0) Audit.Yoz(db, ctx.User.Ism(), "Telegram sozlandi", matn, AuditTurlari.Sozlama);
            await db.SaveChangesAsync();
            return Results.Ok(await Holat(db, zaxira, telegram));
        }).RuxsatKerak(Ruxsat.Sozlamalar).Produces<ZaxiraHolatiDto>().ProducesProblem(400).ProducesProblem(403);

        // Telegram xatosi tashqi xato: 200 + holatda xato matni. 400 - faqat token yoki chat ID yo'q bo'lsa.
        z.MapPost("/telegram/sinov", async (HttpContext ctx, FuelControlDbContext db, ZaxiraXizmati zaxira, ZaxiraTelegram telegram) =>
        {
            await telegram.Sinov(ctx.RequestAborted);
            return Results.Ok(await Holat(db, zaxira, telegram));
        }).RuxsatKerak(Ruxsat.Sozlamalar).Produces<ZaxiraHolatiDto>().ProducesProblem(400).ProducesProblem(403);
    }

    private static async Task<ZaxiraHolatiDto> Holat(FuelControlDbContext db, ZaxiraXizmati zaxira, ZaxiraTelegram telegram)
    {
        var h = zaxira.PapkaniAniqlaYumshoq();
        var q = await db.ZaxiraSozlamalari.AsNoTracking().FirstOrDefaultAsync();
        return new ZaxiraHolatiDto(h.Papka, h.Sozlangan, h.StandartPapkaga, h.Sabab, zaxira.Saqlash, zaxira.Nusxalar(), telegram.Dto(q));
    }
}

using FuelControl.Api.Auth;
using FuelControl.Api.Data;
using FuelControl.Api.Xizmatlar;
using FuelControl.Contracts;
using FuelControl.Contracts.Dto;
using FuelControl.Core.Modellar;
using FuelControl.Core.Xizmatlar;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FuelControl.Api.Endpointlar;

public static class FoydalanuvchiEndpointlari
{
    private static readonly string SoxtaXesh = ParolXeshlash.Xeshla(Guid.NewGuid().ToString());

    public static void Ulash(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/login", async (LoginSoroviDto s, FuelControlDbContext db, TokenXizmati token) =>
        {
            var f = await db.Foydalanuvchilar.FirstOrDefaultAsync(x => x.Login == s.Login);
            var hozir = DateTime.UtcNow;
            if (f is null || !f.Faol)
            {
                // Javob vaqti bo'yicha login mavjudligini bilib bo'lmasin.
                ParolXeshlash.Tekshir(s.ParolYokiPin, SoxtaXesh);
                return Results.Problem(statusCode: 401, title: "Kirish xatosi", detail: "Login yoki parol noto'g'ri.");
            }
            if (KirishXizmati.Blokmi(f, hozir))
                return Results.Problem(statusCode: 429, title: "Bloklangan",
                    detail: $"Ko'p xato urinish. {Math.Ceiling((f.BlokGacha!.Value - hozir).TotalMinutes)} daqiqadan keyin urinib ko'ring.");

            if (!ParolXeshlash.Tekshir(s.ParolYokiPin, f.ParolXeshi))
            {
                KirishXizmati.XatoUrinish(f, hozir);
                await db.SaveChangesAsync();
                return Results.Problem(statusCode: 401, title: "Kirish xatosi", detail: "Login yoki parol noto'g'ri.");
            }

            KirishXizmati.Muvaffaqiyatli(f);
            await db.SaveChangesAsync();
            var (t, muddati) = token.Yarat(f);
            return Results.Ok(new LoginJavobiDto(t, muddati, f.Dto()));
        }).AllowAnonymous().Produces<LoginJavobiDto>().ProducesProblem(401).ProducesProblem(429);

        // Faol foydalanuvchi sessiyasini uzaytirish (docs 8.18): amaldagi (hali tugamagan) token bilan yangi token, xuddi login kabi, parolsiz.
        // Rol va ruxsatlar bazadan olinadi; nofaol yoki bloklangan foydalanuvchi - 401. Muddat 12 soat bo'lib qoladi, audit va hisoblagichlar yozilmaydi.
        app.MapPost("/auth/yangila", async (HttpContext ctx, FuelControlDbContext db, TokenXizmati token) =>
        {
            var f = await db.Foydalanuvchilar.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ctx.User.FoydalanuvchiId());
            if (f is null || !f.Faol || KirishXizmati.Blokmi(f, DateTime.UtcNow))
                return Results.Problem(statusCode: 401, title: "Sessiya yangilanmadi", detail: "Foydalanuvchi faol emas yoki bloklangan.");
            var (t, muddati) = token.Yarat(f);
            return Results.Ok(new LoginJavobiDto(t, muddati, f.Dto()));
        }).RequireAuthorization().Produces<LoginJavobiDto>().ProducesProblem(401);

        app.MapGet("/me", async (HttpContext ctx, FuelControlDbContext db) =>
        {
            var f = await db.Foydalanuvchilar.FindAsync(ctx.User.FoydalanuvchiId());
            return f is null ? Results.Unauthorized() : Results.Ok(f.Dto());
        }).RequireAuthorization().Produces<FoydalanuvchiDto>();

        var g = app.MapGroup("/foydalanuvchilar").RequireAuthorization();

        g.MapGet("/", async (FuelControlDbContext db) =>
            (await db.Foydalanuvchilar.OrderBy(f => f.Id).ToListAsync()).Select(f => f.Dto()))
            .RuxsatKerak(Ruxsat.Sozlamalar);

        // Operatorlar ro'yxati (smena/hisobot filtrlari, operator hisoblari uchun). Sozlamalar ruxsatisiz ham ochiq,
        // lekin boshqalarning ruxsatlar to'plami berilmaydi; ko'rish ruxsati yo'q foydalanuvchi faqat o'zini oladi.
        app.MapGet("/operatorlar", async (HttpContext ctx, FuelControlDbContext db) =>
        {
            var men = ctx.User.FoydalanuvchiId();
            var hammasi = ctx.User.Bor(Ruxsat.Operatorlar) || ctx.User.Bor(Ruxsat.Smenalar)
                          || ctx.User.Bor(Ruxsat.Hisobotlar) || ctx.User.Bor(Ruxsat.Boshqaruv);
            var q = db.Foydalanuvchilar.Where(f => f.Rol == Rol.Operator);
            if (!hammasi) q = q.Where(f => f.Id == men);
            return (await q.OrderBy(f => f.Id).ToListAsync())
                .Select(f => f.Id == men || ctx.User.Bor(Ruxsat.Sozlamalar) ? f.Dto() : f.Dto() with { Ruxsatlar = [] });
        }).RequireAuthorization();

        g.MapPost("/", async (FoydalanuvchiYaratishDto s, HttpContext ctx, FuelControlDbContext db, IHubContext<SotuvHub> hub) =>
        {
            if (string.IsNullOrWhiteSpace(s.ToliqIsm) || string.IsNullOrWhiteSpace(s.Login))
                throw new BiznesXatosi("Ism va login kiritilishi shart.");
            if (s.ParolYokiPin.Length < 4) throw new BiznesXatosi("Parol/PIN kamida 4 belgi bo'lishi kerak.");
            if (await db.Foydalanuvchilar.AnyAsync(f => f.Login == s.Login)) throw new BiznesXatosi("Bu login band.", 409);

            var f = new Foydalanuvchi
            {
                ToliqIsm = s.ToliqIsm.Trim(), Login = s.Login.Trim(), Rol = s.Rol, OylikMaosh = s.OylikMaosh,
                ParolXeshi = ParolXeshlash.Xeshla(s.ParolYokiPin), Ruxsatlar = RuxsatXizmati.Standart(s.Rol).ToList(),
            };
            db.Foydalanuvchilar.Add(f);
            Audit.Yoz(db, ctx.User.Ism(), "Foydalanuvchi yaratildi", $"{f.ToliqIsm} ({f.Login}), rol {f.Rol}", AuditTurlari.Sozlama);
            await db.SaveChangesAsync();
            await hub.Bildir(Bolimlar.Foydalanuvchilar);
            return Results.Created($"/foydalanuvchilar/{f.Id}", f.Dto());
        }).RuxsatKerak(Ruxsat.Sozlamalar).Produces<FoydalanuvchiDto>(201);

        g.MapPut("/{id:int}", async (int id, FoydalanuvchiTahrirlashDto s, HttpContext ctx, FuelControlDbContext db, IHubContext<SotuvHub> hub,
            FoydalanuvchiKeshi kesh, UlanishlarXaritasi ulanishlar) =>
        {
            var f = await db.Foydalanuvchilar.FindAsync(id) ?? throw new BiznesXatosi("Foydalanuvchi topilmadi.", 404);
            if (!s.Faol && f.Id == ctx.User.FoydalanuvchiId()) throw new BiznesXatosi("O'zingizni o'chirib bo'lmaydi.");
            if (string.IsNullOrWhiteSpace(s.ToliqIsm)) throw new BiznesXatosi("Ism kiritilishi shart.");
            if (s.Login is { } yangiLogin && yangiLogin.Trim() != f.Login)
            {
                yangiLogin = yangiLogin.Trim();
                if (yangiLogin.Length == 0) throw new BiznesXatosi("Login kiritilishi shart.");
                if (await db.Foydalanuvchilar.AnyAsync(x => x.Login == yangiLogin && x.Id != id)) throw new BiznesXatosi("Bu login band.", 409);
                f.Login = yangiLogin;
            }
            // Rol o'zgarsa ruxsatlar yangi rolning standart to'plamiga qaytadi (desktop ham shunday qiladi).
            var rolOzgardi = f.Rol != s.Rol;
            var faollikOzgardi = f.Faol != s.Faol;
            if (rolOzgardi) f.Ruxsatlar = RuxsatXizmati.Standart(s.Rol).ToList();
            f.ToliqIsm = s.ToliqIsm.Trim(); f.Rol = s.Rol; f.Faol = s.Faol; f.OylikMaosh = s.OylikMaosh;
            Audit.Yoz(db, ctx.User.Ism(), "Foydalanuvchi o'zgartirildi",
                $"{f.ToliqIsm} ({f.Login}), rol {f.Rol}, faol {f.Faol}, maosh {f.OylikMaosh}" +
                (rolOzgardi ? $"; ruxsatlar standartga qaytarildi: {string.Join(',', f.Ruxsatlar)}" : ""), AuditTurlari.Sozlama);
            await db.SaveChangesAsync();
            kesh.Unut(id);
            // Guruh a'zoligi ulanish paytida belgilangan — rol/faollik o'zgarsa ulanishlarni uzamiz (qayta ulanishda yangilanadi).
            if (rolOzgardi || faollikOzgardi) await ulanishlar.Uz(id);
            await hub.Bildir(Bolimlar.Foydalanuvchilar);
            return Results.Ok(f.Dto());
        }).RuxsatKerak(Ruxsat.Sozlamalar).Produces<FoydalanuvchiDto>();

        g.MapPut("/{id:int}/ruxsatlar", async (int id, RuxsatlarOrnatishDto s, HttpContext ctx, FuelControlDbContext db, IHubContext<SotuvHub> hub,
            FoydalanuvchiKeshi kesh, UlanishlarXaritasi ulanishlar) =>
        {
            var f = await db.Foydalanuvchilar.FindAsync(id) ?? throw new BiznesXatosi("Foydalanuvchi topilmadi.", 404);
            var eski = f.Ruxsatlar.ToList();
            f.Ruxsatlar = s.Ruxsatlar.Distinct().ToList();
            Audit.Yoz(db, ctx.User.Ism(), "Ruxsatlar o'zgartirildi",
                $"{f.ToliqIsm}: {string.Join(',', eski)} → {string.Join(',', f.Ruxsatlar)}", AuditTurlari.Sozlama);
            await db.SaveChangesAsync();
            kesh.Unut(id);
            if (!eski.ToHashSet().SetEquals(f.Ruxsatlar)) await ulanishlar.Uz(id);
            await hub.Bildir(Bolimlar.Foydalanuvchilar);
            return Results.Ok(f.Dto());
        }).RuxsatKerak(Ruxsat.Sozlamalar).Produces<FoydalanuvchiDto>();

        g.MapPost("/{id:int}/pin", async (int id, PinOrnatishDto s, HttpContext ctx, FuelControlDbContext db) =>
        {
            if (s.YangiParolYokiPin.Length < 4) throw new BiznesXatosi("Parol/PIN kamida 4 belgi bo'lishi kerak.");
            var f = await db.Foydalanuvchilar.FindAsync(id) ?? throw new BiznesXatosi("Foydalanuvchi topilmadi.", 404);
            f.ParolXeshi = ParolXeshlash.Xeshla(s.YangiParolYokiPin);
            KirishXizmati.Muvaffaqiyatli(f);
            Audit.Yoz(db, ctx.User.Ism(), "Parol/PIN almashtirildi", f.ToliqIsm, AuditTurlari.Kirish);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RuxsatKerak(Ruxsat.Sozlamalar).Produces(204);
    }
}

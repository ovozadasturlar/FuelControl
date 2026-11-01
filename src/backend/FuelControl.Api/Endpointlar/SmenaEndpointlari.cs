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

public static class SmenaEndpointlari
{
    public static void SmenaUlash(this IEndpointRouteBuilder app)
    {
        var s = app.MapGroup("/smenalar").RequireAuthorization();

        // "Smenalar" ruxsati yo'q foydalanuvchi faqat o'zi ochgan smenalarni ko'radi.
        s.MapGet("/", async (HttpContext ctx, FuelControlDbContext db, DateOnly? dan, DateOnly? gacha, int? operatorId) =>
        {
            if (!ctx.User.Bor(Ruxsat.Smenalar)) operatorId = ctx.User.FoydalanuvchiId();
            var q = db.Smenalar.AsNoTracking().AsQueryable();
            if (Vaqt.Dan(dan) is { } d) q = q.Where(x => x.Boshlandi >= d);
            if (Vaqt.Gacha(gacha) is { } g) q = q.Where(x => x.Boshlandi < g);
            if (operatorId is { } o) q = q.Where(x => x.OperatorId == o);
            return await db.SmenaDtolari(await q.OrderByDescending(x => x.Boshlandi).ThenByDescending(x => x.Id).ToListAsync());
        }).Produces<SmenaDto[]>();

        // Butun shoxobcha bo'yicha bitta ochiq smena; yo'q bo'lsa 204.
        s.MapGet("/joriy", async (FuelControlDbContext db) =>
        {
            var smena = await db.Smenalar.AsNoTracking().FirstOrDefaultAsync(x => x.Tugadi == null);
            return smena is null ? Results.NoContent() : Results.Ok(await db.SmenaTafsiloti(smena));
        }).Produces<SmenaTafsilotDto>().Produces(204);

        // Oxirgi yopilgan smena (Tugadi bo'yicha eng yangisi); yo'q bo'lsa 204. Pul natijasi (savdo, plastik, kamomat...) bo'lgani uchun faqat
        // boshliqqa ("Smenalar" ruxsati) va shu smenaning operatoriga; keyingi operator /oxirgi/topshirish dan foydalanadi (boshqalarga 403).
        s.MapGet("/oxirgi", async (HttpContext ctx, FuelControlDbContext db) =>
        {
            var smena = await OxirgiYopilgan(db);
            if (smena is null) return Results.NoContent();
            if (smena.OperatorId != ctx.User.FoydalanuvchiId() && !ctx.User.Bor(Ruxsat.Smenalar))
                throw new BiznesXatosi("Oxirgi smena natijasini faqat boshliq (\"Smenalar\" ruxsati) yoki shu smenaning operatori ko'ra oladi.", 403);
            return Results.Ok(await db.SmenaTafsiloti(smena));
        }).Produces<SmenaTafsilotDto>().Produces(204).ProducesProblem(403);

        // Keyingi operator uchun topshirish ma'lumoti: kim va qachon yopgan, yopilgandagi depozit qoldig'i - pul natijasisiz. Yopilgan smena
        // yo'q bo'lsa 204. Ruxsat - /smenalar/joriy bilan bir xil (kirgan har kim).
        s.MapGet("/oxirgi/topshirish", async (FuelControlDbContext db) =>
        {
            var smena = await OxirgiYopilgan(db);
            if (smena is null) return Results.NoContent();
            var ism = await db.Foydalanuvchilar.AsNoTracking().Where(f => f.Id == smena.OperatorId).Select(f => f.ToliqIsm).FirstOrDefaultAsync() ?? "";
            return Results.Ok(new SmenaTopshirishDto(smena.Id, smena.OperatorId, ism, smena.Tugadi!.Value, smena.YopishDepozit));
        }).Produces<SmenaTopshirishDto>().Produces(204);

        s.MapGet("/{id:int}", async (int id, HttpContext ctx, FuelControlDbContext db) =>
        {
            var smena = await db.Smenalar.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id) ?? throw new BiznesXatosi("Smena topilmadi.", 404);
            if (smena.OperatorId != ctx.User.FoydalanuvchiId() && !ctx.User.Bor(Ruxsat.Smenalar))
                throw new BiznesXatosi("Bu smenani ko'rish uchun \"Smenalar\" ruxsati kerak.", 403);
            return Results.Ok(await db.SmenaTafsiloti(smena));
        }).Produces<SmenaTafsilotDto>().ProducesProblem(404).ProducesProblem(403);

        // Uchala qoldiqni operator qo'lda kiritadi (oldingi smenadan olinmaydi). Ochiq smena bo'lsa 409 (bazada ham kafolat).
        // Aparat yo'q bo'lsa (yangi Production bazasi) smena ochilmaydi: 400. Xabar (ProblemDetails.detail) foydalanuvchiga so'zma-so'z ko'rsatiladi.
        s.MapPost("/och", async (SmenaOchishDto so, HttpContext ctx, FuelControlDbContext db, IHubContext<SotuvHub> hub) =>
        {
            SmenaDto dto;
            try
            {
                dto = await Tranzaksiya.Bajar(db, async () =>
                {
                    if (!await db.Aparatlar.AnyAsync()) throw new BiznesXatosi("Smenani ochish uchun kamida bitta aparat kerak. Sozlamalar → Aparatlar bo'limida aparat qo'shing.");
                    if (await db.Smenalar.AnyAsync(x => x.Tugadi == null)) throw OchiqSmenaBor();
                    var smena = SmenaHisoblagich.Och(ctx.User.FoydalanuvchiId(), so.Qaytim, so.Terminal, so.Depozit, DateTime.UtcNow);
                    db.Smenalar.Add(smena);
                    await db.SaveChangesAsync();                 // Id kerak: audit matnida
                    Audit.Yoz(db, ctx.User.Ism(), "Smena ochildi", AuditMatnlari.SmenaOchildi(smena), AuditTurlari.Smena);
                    await db.SaveChangesAsync();
                    return await db.SmenaDtosi(smena);
                });
            }
            catch (DbUpdateException e) when (Tranzaksiya.Takror(e)) { throw OchiqSmenaBor(); }
            await hub.Kuzatuvchilarga(Xabarlar.SmenaOzgardi, dto);
            return Results.Created($"/smenalar/{dto.Id}", dto);
        }).RuxsatKerak(Ruxsat.SmenaOchish).Produces<SmenaDto>(201).ProducesProblem(409).ProducesProblem(400);

        // Operator faqat o'zi ochgan smenani yopadi ("Smenalar" ruxsati borlar - istalganini). Formula - Core.SmenaHisoblagich.
        s.MapPost("/{id:int}/yop", async (int id, SmenaYopishDto so, HttpContext ctx, FuelControlDbContext db, IHubContext<SotuvHub> hub, UlanishlarXaritasi ulanishlar, ZaxiraNavbati zaxira) =>
        {
            var (dto, aparatlar) = await Tranzaksiya.Bajar(db, async () =>
            {
                var smena = await db.Smenalar.FirstOrDefaultAsync(x => x.Id == id) ?? throw new BiznesXatosi("Smena topilmadi.", 404);
                if (smena.OperatorId != ctx.User.FoydalanuvchiId() && !ctx.User.Bor(Ruxsat.Smenalar))
                    throw new BiznesXatosi("Faqat o'z smenangizni yopishingiz mumkin.", 403);
                if (!smena.Ochiqmi) throw new BiznesXatosi("Smena allaqachon yopilgan.", 409);

                var aparatlar = await db.Aparatlar.ToListAsync();
                var narxlar = await db.Yoqilgilar.ToDictionaryAsync(y => y.Id, y => y.Narx);
                var mavjud = await db.SmenaKorsatkichlari.Where(x => x.SmenaId == id).ToListAsync();
                var natija = SmenaHisoblagich.Yop(smena, aparatlar, narxlar, mavjud, Korsatkichlar(so.Korsatkichlar),
                    so.Terminal, so.Depozit, so.SanalganNaqd, so.Izoh, await db.Yigindilar(id), DateTime.UtcNow, so.PlastikSummalari);

                db.SmenaKorsatkichlari.AddRange(natija.YangiSegmentlar);
                if (natija.Harakat is { } h) db.Harakatlar.Add(h);
                Audit.Yoz(db, ctx.User.Ism(), "Smena yopildi", AuditMatnlari.SmenaYopildi(smena), AuditTurlari.Smena);
                await db.SaveChangesAsync();
                return (await db.SmenaDtosi(smena), await db.AparatDtolari());
            });
            await ulanishlar.YopilganSmena(dto);                       // to'liq natija faqat Smenalar ruxsatlilarga va egasiga (docs 8.10)
            foreach (var a in aparatlar) await hub.Kuzatuvchilarga(Xabarlar.AparatOzgardi, a);
            await hub.Bildir(Bolimlar.Harakatlar);
            zaxira.SmenaYopildi(id);                                   // zaxira nusxa fonda (docs 8.17): yopishni sekinlashtirmaydi va buzmaydi
            return Results.Ok(dto);
        }).RuxsatKerak(Ruxsat.SmenaYopish).Produces<SmenaDto>().ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        // Faqat oxirgi yopilgan smena, sabab majburiy. Smena qayta hisoblanadi, farq o'zgarishi operator hisobiga tuzatuvchi harakat bo'lib yoziladi.
        s.MapPut("/{id:int}/korsatkich", async (int id, KorsatkichTuzatishDto t, HttpContext ctx, FuelControlDbContext db, IHubContext<SotuvHub> hub, UlanishlarXaritasi ulanishlar) =>
        {
            var (tafsilot, aparat) = await Tranzaksiya.Bajar(db, async () =>
            {
                var smena = await db.Smenalar.FirstOrDefaultAsync(x => x.Id == id) ?? throw new BiznesXatosi("Smena topilmadi.", 404);
                if (smena.Ochiqmi) throw new BiznesXatosi("Ochiq smena ko'rsatkichini tuzatib bo'lmaydi: avval smenani yoping.", 409);
                var oxirgi = await db.Smenalar.Where(x => x.Tugadi != null).OrderByDescending(x => x.Tugadi).ThenByDescending(x => x.Id)
                    .Select(x => x.Id).FirstAsync();
                if (oxirgi != id) throw new BiznesXatosi($"Faqat oxirgi yopilgan smena (#{oxirgi}) ko'rsatkichini tuzatish mumkin.", 409);
                var a = await db.Aparatlar.FirstOrDefaultAsync(x => x.Id == t.AparatId) ?? throw new BiznesXatosi("Aparat topilmadi.", 404);

                var segmentlar = await db.SmenaKorsatkichlari.Where(x => x.SmenaId == id).ToListAsync();
                var ochiqId = await db.Smenalar.Where(x => x.Tugadi == null).Select(x => (int?)x.Id).FirstOrDefaultAsync();
                var keyingi = ochiqId is { } oid
                    ? await db.SmenaKorsatkichlari.Where(x => x.SmenaId == oid && x.AparatId == a.Id).ToListAsync()
                    : [];
                var natija = SmenaHisoblagich.KorsatkichniTuzat(smena, a, segmentlar, keyingi, t.Qiymat, t.Sabab, ctx.User.Ism(), DateTime.UtcNow);

                if (natija.Harakat is { } h) db.Harakatlar.Add(h);
                Audit.Yoz(db, ctx.User.Ism(), "Ko'rsatkich tuzatildi",
                    AuditMatnlari.KorsatkichTuzatildi(id, a.Raqam, natija.EskiOxiri, natija.YangiOxiri, t.Sabab), AuditTurlari.Tuzatish);
                await db.SaveChangesAsync();
                return (await db.SmenaTafsiloti(smena), await db.AparatDtosi(a));
            });
            await ulanishlar.YopilganSmena(tafsilot.Smena);
            await hub.Kuzatuvchilarga(Xabarlar.AparatOzgardi, aparat);
            await hub.Bildir(Bolimlar.Harakatlar);
            return Results.Ok(tafsilot);
        }).RuxsatKerak(Ruxsat.KorsatkichTuzatish).Produces<SmenaTafsilotDto>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);

        // Ochiq smenada narx o'zgarishida noto'g'ri yozilgan ko'rsatkichni tuzatish (yuqoridagi /korsatkich faqat yopilgan smena uchun):
        // aparatning shu smenadagi OXIRGI narx o'zgarishi segmenti. Qiymat segment boshiga teng bo'lsa segment o'chadi. Aparatning pult va bak
        // qoldig'iga tegilmaydi, harakat yozilmaydi (ochiq smenada pul hali hisoblanmagan). Javob - ochiq smenaning yangilangan tafsiloti.
        s.MapPut("/{id:int}/narx-korsatkichi", async (int id, KorsatkichTuzatishDto t, HttpContext ctx, FuelControlDbContext db, IHubContext<SotuvHub> hub) =>
        {
            var tafsilot = await Tranzaksiya.Bajar(db, async () =>
            {
                var smena = await db.Smenalar.FirstOrDefaultAsync(x => x.Id == id) ?? throw new BiznesXatosi("Smena topilmadi.", 404);
                if (!smena.Ochiqmi)
                    throw new BiznesXatosi($"Smena #{id} yopilgan: narx o'zgarishi ko'rsatkichini faqat ochiq smenada tuzatish mumkin.", 409);
                var a = await db.Aparatlar.FirstOrDefaultAsync(x => x.Id == t.AparatId) ?? throw new BiznesXatosi("Aparat topilmadi.", 404);

                var segmentlar = await db.SmenaKorsatkichlari.Where(x => x.SmenaId == id && x.AparatId == a.Id).ToListAsync();
                var natija = SmenaHisoblagich.NarxKorsatkichiniTuzat(smena, a, segmentlar, t.Qiymat, t.Sabab);
                if (natija.Ochirildi) db.SmenaKorsatkichlari.Remove(natija.Segment);

                Audit.Yoz(db, ctx.User.Ism(), "Ko'rsatkich tuzatildi",
                    AuditMatnlari.NarxKorsatkichiTuzatildi(id, a.Raqam, natija.EskiOxiri, natija.YangiOxiri, t.Sabab), AuditTurlari.Tuzatish);
                await db.SaveChangesAsync();
                return await db.SmenaTafsiloti(smena);
            });
            await hub.Kuzatuvchilarga(Xabarlar.SmenaOzgardi, tafsilot.Smena);
            return Results.Ok(tafsilot);
        }).RuxsatKerak(Ruxsat.KorsatkichTuzatish).Produces<SmenaTafsilotDto>().ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
    }

    private static BiznesXatosi OchiqSmenaBor() => new("Smena allaqachon ochiq: avval uni yoping.", 409);

    /// <summary>Oxirgi yopilgan smena (Tugadi bo'yicha eng yangisi) yoki null.</summary>
    private static Task<Smena?> OxirgiYopilgan(FuelControlDbContext db) =>
        db.Smenalar.AsNoTracking().Where(x => x.Tugadi != null).OrderByDescending(x => x.Tugadi).ThenByDescending(x => x.Id).FirstOrDefaultAsync();

    /// <summary>Aparat ko'rsatkichlari ro'yxati -> lug'at; bir aparat ikki marta berilsa 400.</summary>
    internal static Dictionary<int, decimal> Korsatkichlar(AparatKorsatkichDto[]? royxat)
    {
        var d = new Dictionary<int, decimal>();
        foreach (var k in royxat ?? [])
            if (!d.TryAdd(k.AparatId, k.Qiymat)) throw new BiznesXatosi("Bir aparatning ko'rsatkichi ikki marta berilgan.");
        return d;
    }
}

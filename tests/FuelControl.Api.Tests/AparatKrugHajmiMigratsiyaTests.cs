using FuelControl.Api.Data;
using FuelControl.Api.Xizmatlar;
using FuelControl.Core.Modellar;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace FuelControl.Api.Tests;

/// <summary>AparatKrugHajmi migratsiyasi: 0.1.3 bazasi aparatlarini (TotalLitr, bak) tegmasdan bitta nullable KrugHajmi ustunini oladi, hamma qator null.</summary>
public sealed class AparatKrugHajmiMigratsiyaTests : IDisposable
{
    private const string Versiya013 = "20261011065321_NasiyaEskiQarz";
    private readonly SqliteConnection _ulanish = new("Data Source=:memory:");

    public AparatKrugHajmiMigratsiyaTests() => _ulanish.Open();

    public void Dispose() => _ulanish.Dispose();

    private FuelControlDbContext Yangi() => new(new DbContextOptionsBuilder<FuelControlDbContext>().UseSqlite(_ulanish).Options);

    private async Task<string> Skalyar(string sql)
    {
        await using var buyruq = _ulanish.CreateCommand();
        buyruq.CommandText = sql;
        return Convert.ToString(await buyruq.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }

    /// <summary>0.1.3 sxemasidagi baza: ikki yoqilg'i, ikki aparat (mijozdagi qiymatlar), narx o'zgarishi segmenti bor ochiq smena; ustun hali yo'q, shuning uchun SQL bilan.</summary>
    private async Task<FuelControlDbContext> Otkaz()
    {
        var db = Yangi();
        await db.GetService<IMigrator>().MigrateAsync(Versiya013);
        Assert.Equal("0", await Skalyar("SELECT COUNT(*) FROM pragma_table_info('Aparatlar') WHERE name = 'KrugHajmi'"));
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Foydalanuvchilar" ("Id","ToliqIsm","Login","Rol","Faol","OylikMaosh","ParolXeshi","Ruxsatlar","XatoUrinishlar","BlokGacha") VALUES
              (1,'Op','op','Operator',1,0,'x','Savdo',0,NULL);
            INSERT INTO "Yoqilgilar" ("Id","Nomi","Narx","Rang") VALUES (1,'DIZEL ECO',16000,'#2563EB'),(2,'AI-95',14200,'#16A34A');
            INSERT INTO "Aparatlar" ("Id","Raqam","YoqilgiTuriId","TotalLitr","BakQoldiq") VALUES
              (1,4,1,'109797.58','5017.83'),
              (2,5,2,'10560.52','8120.40');
            INSERT INTO "Smenalar" ("Id","OperatorId","Boshlandi","Tugadi","OchishQaytim","OchishTerminal","OchishDepozit","YopishTerminal","YopishDepozit","SanalganNaqd",
                "Izoh","JamiLitr","Savdo","Plastik","DepozitFarqi","NasiyaJami","QaytganNasiya","XarajatJami","Kutilgan","Farq","PlastikSummalari") VALUES
              (1,1,'2026-10-17 03:00:00',NULL,100000,7830000,1500000,NULL,NULL,NULL,NULL,'0',0,0,0,0,0,0,0,0,'');
            INSERT INTO "SmenaKorsatkichlari" ("Id","SmenaId","AparatId","Tartib","Boshi","Oxiri","Narx","Litr","Summa","NarxOzgarishida","Vaqt") VALUES
              (1,1,1,1,'109797.58','1099797.58',15500,'990000.00',15345000000,1,'2026-10-17 05:00:00');
            """);
        await db.Database.MigrateAsync();
        return db;
    }

    [Fact]
    public async Task Eski013Bazasi_BittaNullableUstun_QatorlarNull_BoshqaQiymatlarTegilmaydi()
    {
        await using var db = await Otkaz();

        var qollangan = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.Contains(qollangan, m => m.EndsWith("_AparatKrugHajmi", StringComparison.Ordinal));                     // shu migratsiya qo'llangan (keyingilari o'z testlarida)
        Assert.Equal("1", await Skalyar("SELECT COUNT(*) FROM pragma_table_info('Aparatlar') WHERE name = 'KrugHajmi' AND type = 'TEXT' AND \"notnull\" = 0 AND dflt_value IS NULL"));
        Assert.Equal("2", await Skalyar("SELECT COUNT(*) FROM \"Aparatlar\" WHERE \"KrugHajmi\" IS NULL"));          // hamma eski qator krug yo'q
        Assert.Equal("109797.58|5017.83|10560.52|8120.40", await Skalyar(
            "SELECT group_concat(\"TotalLitr\" || '|' || \"BakQoldiq\", '|') FROM (SELECT * FROM \"Aparatlar\" ORDER BY \"Raqam\")"));   // santimigacha o'zgarmagan

        var aparatlar = await db.Aparatlar.AsNoTracking().OrderBy(a => a.Raqam).ToListAsync();
        Assert.Equal([4, 5], aparatlar.Select(a => a.Raqam));
        Assert.Equal([109_797.58m, 10_560.52m], aparatlar.Select(a => a.TotalLitr));
        Assert.Equal([5_017.83m, 8_120.40m], aparatlar.Select(a => a.BakQoldiq));
        Assert.All(aparatlar, a => Assert.Null(a.KrugHajmi));
        Assert.Null(aparatlar[0].Dto("DIZEL ECO", null).KrugHajmi);

        // Ochiq smena va uning narx o'zgarishi segmenti ham joyida.
        Assert.Equal("1", await Skalyar("SELECT COUNT(*) FROM \"Smenalar\" WHERE \"Id\" = 1 AND \"Tugadi\" IS NULL"));
        var segment = await db.SmenaKorsatkichlari.AsNoTracking().SingleAsync();
        Assert.Equal((109_797.58m, 1_099_797.58m, 15_500L, 990_000.00m, 15_345_000_000L, true), (segment.Boshi, segment.Oxiri, segment.Narx, segment.Litr, segment.Summa, segment.NarxOzgarishida));
    }

    [Fact]
    public async Task MigratsiyadanKeyin_KrugYoziladiQaytaOqiladi_TotalLitrUmumiyLitrdaQoladi()
    {
        await using var db = await Otkaz();
        var a5 = await db.Aparatlar.SingleAsync(a => a.Raqam == 5);
        a5.KrugHajmi = 10_000m;                                                                                         // mijozda o'rnatilgandan keyin: 10 560.52 o'zgarmaydi
        await db.SaveChangesAsync();

        await using var yangi = Yangi();
        var o = await yangi.Aparatlar.AsNoTracking().OrderBy(a => a.Raqam).ToListAsync();
        Assert.Equal([null, 10_000m], o.Select(a => a.KrugHajmi));
        Assert.Equal((10_560.52m, 8_120.40m), (o[1].TotalLitr, o[1].BakQoldiq));
        var dto = o[1].Dto("AI-95", null);
        Assert.Equal((10_000m, 10_560.52m), (dto.KrugHajmi, dto.TotalLitr));
    }
}

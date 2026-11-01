using FuelControl.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace FuelControl.Api.Tests;

/// <summary>ZaxiraSozlamalari migratsiyasi: 0.1.4 bazasiga faqat yangi bo'sh jadval qo'shiladi, eski jadvallar (aparat, smena, segment, foydalanuvchi) tegilmaydi.</summary>
public sealed class ZaxiraSozlamalariMigratsiyaTests : IDisposable
{
    private const string Versiya014 = "20261017053420_AparatKrugHajmi";
    private readonly SqliteConnection _ulanish = new("Data Source=:memory:");

    public ZaxiraSozlamalariMigratsiyaTests() => _ulanish.Open();

    public void Dispose() => _ulanish.Dispose();

    private FuelControlDbContext Yangi() => new(new DbContextOptionsBuilder<FuelControlDbContext>().UseSqlite(_ulanish).Options);

    private async Task<string> Skalyar(string sql)
    {
        await using var buyruq = _ulanish.CreateCommand();
        buyruq.CommandText = sql;
        return Convert.ToString(await buyruq.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }

    [Fact]
    public async Task Eski014Bazasi_YangiJadvalBosh_EskiMalumotTegilmaydi()
    {
        await using var db = Yangi();
        await db.GetService<IMigrator>().MigrateAsync(Versiya014);
        Assert.Equal("0", await Skalyar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ZaxiraSozlamalari'"));
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Foydalanuvchilar" ("Id","ToliqIsm","Login","Rol","Faol","OylikMaosh","ParolXeshi","Ruxsatlar","XatoUrinishlar","BlokGacha") VALUES
              (1,'Op','op','Operator',1,0,'x','Savdo',0,NULL);
            INSERT INTO "Yoqilgilar" ("Id","Nomi","Narx","Rang") VALUES (1,'DIZEL ECO',16000,'#2563EB');
            INSERT INTO "Aparatlar" ("Id","Raqam","YoqilgiTuriId","TotalLitr","BakQoldiq","KrugHajmi") VALUES (1,4,1,'109797.58','5017.83','10000.00');
            INSERT INTO "Smenalar" ("Id","OperatorId","Boshlandi","Tugadi","OchishQaytim","OchishTerminal","OchishDepozit","YopishTerminal","YopishDepozit","SanalganNaqd",
                "Izoh","JamiLitr","Savdo","Plastik","DepozitFarqi","NasiyaJami","QaytganNasiya","XarajatJami","Kutilgan","Farq","PlastikSummalari") VALUES
              (1,1,'2026-10-17 03:00:00',NULL,100000,7830000,1500000,NULL,NULL,NULL,NULL,'0',0,0,0,0,0,0,0,0,'');
            INSERT INTO "SmenaKorsatkichlari" ("Id","SmenaId","AparatId","Tartib","Boshi","Oxiri","Narx","Litr","Summa","NarxOzgarishida","Vaqt") VALUES
              (1,1,1,1,'109797.58','1099797.58',15500,'990000.00',15345000000,1,'2026-10-17 05:00:00');
            """);

        await db.Database.MigrateAsync();

        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("_ZaxiraSozlamalari", StringComparison.Ordinal));
        Assert.Equal("0", await Skalyar("SELECT COUNT(*) FROM \"ZaxiraSozlamalari\""));                                       // jadval bo'sh: qator birinchi saqlashda yaratiladi
        Assert.Equal("Id|INTEGER|1,TelegramTokenShifr|TEXT|0,TelegramChatId|TEXT|0,TelegramYoqilgan|INTEGER|1,YuborishVaqti|TEXT|0,YuborishMuvaffaqiyat|INTEGER|0,YuborishXabari|TEXT|0,YuborishTuri|TEXT|0,OxirgiYuborilganFayl|TEXT|0",
            await Skalyar("SELECT group_concat(name || '|' || type || '|' || \"notnull\", ',') FROM pragma_table_info('ZaxiraSozlamalari')"));
        Assert.Equal("109797.58|5017.83|10000.00", await Skalyar("SELECT \"TotalLitr\" || '|' || \"BakQoldiq\" || '|' || \"KrugHajmi\" FROM \"Aparatlar\""));   // eski ustunlar santimigacha
        Assert.Equal("1|1", await Skalyar("SELECT (SELECT COUNT(*) FROM \"Smenalar\") || '|' || (SELECT COUNT(*) FROM \"SmenaKorsatkichlari\")"));
        Assert.Equal("op", await Skalyar("SELECT \"Login\" FROM \"Foydalanuvchilar\""));
    }
}

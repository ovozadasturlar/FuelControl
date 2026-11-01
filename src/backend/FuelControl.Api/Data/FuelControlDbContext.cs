using FuelControl.Contracts;
using System.Globalization;
using FuelControl.Core.Modellar;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FuelControl.Api.Data;

public sealed class UtcVaqtKonverteri() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

/// <summary>
/// Foydalanuvchi ruxsatlari bazada nomlar bilan, vergul bilan ajratilgan matn. O'qishda noma'lum nomlar (olib tashlangan
/// ruxsatlar) e'tiborga olinmaydi, eski "SotuvKiritish" — "Savdo" ga o'tadi: eski bazadagi foydalanuvchi kirishdan to'xtamasin.
/// </summary>
public static class RuxsatMatni
{
    public static string Yoz(List<Ruxsat> v) => string.Join(',', v);

    public static List<Ruxsat> Oqi(string? v)
    {
        var natija = new List<Ruxsat>();
        foreach (var nom in (v ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var qiymat = nom == "SotuvKiritish" ? Ruxsat.Savdo
                : Enum.TryParse<Ruxsat>(nom, out var r) && Enum.IsDefined(r) && !int.TryParse(nom, out _) ? r
                : (Ruxsat?)null;
            if (qiymat is { } q && !natija.Contains(q)) natija.Add(q);
        }
        return natija;
    }
}

/// <summary>
/// Smenaning plastik qismlari bazada bitta ustunda, vergul bilan ajratilgan sonlar ("7830000,300000,150000"); bo'sh matn - bo'sh massiv
/// (eski smenalar, migratsiyadagi standart qiymat). Ro'yxat har doim smena bilan birga o'qiladi/yoziladi va elementlar bo'yicha so'rov yo'q,
/// shuning uchun alohida jadval ham, JSON ham kerak emas. O'qishda yaroqsiz bo'laklar o'tkazib yuboriladi.
/// </summary>
public static class PlastikMatni
{
    public static string Yoz(long[] v) => string.Join(',', v.Select(x => x.ToString(CultureInfo.InvariantCulture)));

    public static long[] Oqi(string? v) =>
        (v ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => long.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .Select(x => long.Parse(x, NumberStyles.None, CultureInfo.InvariantCulture)).ToArray();
}

public sealed class FuelControlDbContext(DbContextOptions<FuelControlDbContext> options) : DbContext(options)
{
    public DbSet<Foydalanuvchi> Foydalanuvchilar => Set<Foydalanuvchi>();
    public DbSet<YoqilgiTuri> Yoqilgilar => Set<YoqilgiTuri>();
    public DbSet<NarxTarixi> NarxTarixlari => Set<NarxTarixi>();
    public DbSet<Aparat> Aparatlar => Set<Aparat>();
    public DbSet<Smena> Smenalar => Set<Smena>();
    public DbSet<SmenaKorsatkichi> SmenaKorsatkichlari => Set<SmenaKorsatkichi>();
    public DbSet<Nasiya> Nasiyalar => Set<Nasiya>();
    public DbSet<NasiyaQaytishi> NasiyaQaytishlari => Set<NasiyaQaytishi>();
    public DbSet<Xarajat> Xarajatlar => Set<Xarajat>();
    public DbSet<BakKirim> BakKirimlari => Set<BakKirim>();
    public DbSet<BakTuzatishi> BakTuzatishlari => Set<BakTuzatishi>();
    public DbSet<HisobHarakati> Harakatlar => Set<HisobHarakati>();
    public DbSet<AuditYozuvi> Audit => Set<AuditYozuvi>();
    public DbSet<ZaxiraSozlamasi> ZaxiraSozlamalari => Set<ZaxiraSozlamasi>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<DateTime>().HaveConversion<UtcVaqtKonverteri>();
        b.Properties<decimal>().HavePrecision(18, 2);
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        var ruxsatKonverter = new ValueConverter<List<Ruxsat>, string>(
            v => RuxsatMatni.Yoz(v),
            v => RuxsatMatni.Oqi(v));
        var ruxsatTaqqoslagich = new ValueComparer<List<Ruxsat>>(
            (a, b) => a!.SequenceEqual(b!),
            v => v.Aggregate(0, (h, x) => HashCode.Combine(h, (int)x)),
            v => v.ToList());

        var plastikKonverter = new ValueConverter<long[], string>(v => PlastikMatni.Yoz(v), v => PlastikMatni.Oqi(v));
        var plastikTaqqoslagich = new ValueComparer<long[]>(
            (a, b) => a!.SequenceEqual(b!),
            v => v.Aggregate(0, (h, x) => HashCode.Combine(h, x)),
            v => v.ToArray());

        m.Entity<Foydalanuvchi>(e =>
        {
            e.HasIndex(x => x.Login).IsUnique();
            e.Property(x => x.Rol).HasConversion<string>();
            e.Property(x => x.Ruxsatlar).HasConversion(ruxsatKonverter, ruxsatTaqqoslagich);
        });

        m.Entity<YoqilgiTuri>(e => e.HasIndex(x => x.Nomi).IsUnique());

        // Bitta qator (Id = 1): zaxira va Telegram sozlamalari; birinchi saqlashda yaratiladi.
        m.Entity<ZaxiraSozlamasi>(e => e.Property(x => x.Id).ValueGeneratedNever());

        m.Entity<NarxTarixi>(e => e.HasIndex(x => x.Vaqt));

        m.Entity<Aparat>(e =>
        {
            e.HasIndex(x => x.Raqam).IsUnique();
            e.HasOne<YoqilgiTuri>().WithMany().HasForeignKey(x => x.YoqilgiTuriId).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<Smena>(e =>
        {
            e.Ignore(x => x.Ochiqmi);
            e.Property(x => x.PlastikSummalari).HasConversion(plastikKonverter, plastikTaqqoslagich);
            e.HasIndex(x => new { x.OperatorId, x.Tugadi });
            // Butun shoxobchada bir vaqtda faqat bitta ochiq smena. Kafolat — bazada: IX_Smenalar_BittaOchiq
            // (UNIQUE ((1)) WHERE Tugadi IS NULL) migratsiyada SQL bilan yaratiladi (EF modeli ifodali indeksni bilmaydi).
            e.HasIndex(x => x.Boshlandi);
            e.HasOne<Foydalanuvchi>().WithMany().HasForeignKey(x => x.OperatorId).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<SmenaKorsatkichi>(e =>
        {
            e.HasIndex(x => new { x.SmenaId, x.AparatId, x.Tartib }).IsUnique();
            e.HasIndex(x => x.AparatId);
            e.HasOne<Smena>().WithMany().HasForeignKey(x => x.SmenaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Aparat>().WithMany().HasForeignKey(x => x.AparatId).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<Nasiya>(e =>
        {
            e.Ignore(x => x.Qoldiq);
            e.HasIndex(x => x.SmenaId);
            e.HasIndex(x => x.Yozildi);
            e.HasOne<Smena>().WithMany().HasForeignKey(x => x.SmenaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Foydalanuvchi>().WithMany().HasForeignKey(x => x.OperatorId).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<NasiyaQaytishi>(e =>
        {
            e.Property(x => x.Usul).HasConversion<string>();
            e.HasIndex(x => x.NasiyaId);
            e.HasIndex(x => x.SmenaId);
            e.HasIndex(x => x.Vaqt);
            e.HasOne<Nasiya>().WithMany().HasForeignKey(x => x.NasiyaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Smena>().WithMany().HasForeignKey(x => x.SmenaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Foydalanuvchi>().WithMany().HasForeignKey(x => x.OperatorId).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<Xarajat>(e =>
        {
            e.Property(x => x.Manba).HasConversion<string>();
            e.HasIndex(x => x.SmenaId);
            e.HasOne<Smena>().WithMany().HasForeignKey(x => x.SmenaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Foydalanuvchi>().WithMany().HasForeignKey(x => x.OperatorId).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<BakKirim>(e =>
        {
            e.HasIndex(x => new { x.AparatId, x.Vaqt });
            e.HasOne<Aparat>().WithMany().HasForeignKey(x => x.AparatId).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<BakTuzatishi>(e =>
        {
            e.HasIndex(x => new { x.AparatId, x.Vaqt });
            e.HasOne<Aparat>().WithMany().HasForeignKey(x => x.AparatId).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<HisobHarakati>(e =>
        {
            e.Property(x => x.Turi).HasConversion<string>();
            e.HasIndex(x => new { x.OperatorId, x.Sana });
            // Bir operatorga bir oyda bitta maosh — bir nechta API nusxasi/so'rov parallel yozsa ham.
            e.HasIndex(x => new { x.OperatorId, x.MaoshOyi }).IsUnique().HasFilter("\"MaoshOyi\" IS NOT NULL").HasDatabaseName("IX_Harakatlar_MaoshOyi");
            e.HasOne<Foydalanuvchi>().WithMany().HasForeignKey(x => x.OperatorId).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<AuditYozuvi>(e =>
        {
            e.HasIndex(x => x.Vaqt);
            e.HasIndex(x => x.Tur);
        });
    }
}

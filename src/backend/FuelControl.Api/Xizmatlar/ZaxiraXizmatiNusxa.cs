using FuelControl.Api.Data;
using FuelControl.Contracts.Dto;
using FuelControl.Core.Xizmatlar;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FuelControl.Api.Xizmatlar;

public sealed partial class ZaxiraXizmati
{
    public async Task<NusxaNatijasi> NusxaOl(string sabab, int? smenaId = null, CancellationToken ct = default)
    {
        await _qulf.WaitAsync(ct);
        try
        {
            var h = PapkaniAniqla();
            var vaqt = DateTime.UtcNow;
            var nomzodlar = new List<(string Papka, bool Standart)> { (h.Papka, h.StandartPapkaga) };
            if (!ZaxiraYollari.Bir(h.Papka, h.Standart) && Sinash(h.Standart) is null) nomzodlar.Add((h.Standart, true));

            Exception? oxirgiXato = null;
            foreach (var (papka, standart) in nomzodlar)
            {
                var yol = BoshYol(papka, vaqt, sabab, smenaId);
                try
                {
                    await VacuumInto(yol, ct);
                    Tozala(papka);
                    if (!ZaxiraYollari.Bir(papka, h.Standart)) Tozala(h.Standart);
                    return new NusxaNatijasi(Path.GetFileName(yol), new FileInfo(yol).Length, vaqt, sabab, smenaId, papka, standart);
                }
                catch (Exception e) when (e is IOException or SqliteException or UnauthorizedAccessException)
                {
                    oxirgiXato = e;
                    log.LogWarning("Zaxira nusxa {Papka} ga yozilmadi: {Xato}", papka, e.Message);
                    try { File.Delete(yol); } catch (IOException) { }
                }
            }
            throw oxirgiXato!;
        }
        finally { _qulf.Release(); }
    }

    private static string BoshYol(string papka, DateTime vaqt, string sabab, int? smenaId)
    {
        for (var takror = 0; ; takror++)
        {
            var yol = Path.GetFullPath(Path.Combine(papka, ZaxiraQoidalari.Nom(vaqt, sabab, smenaId, takror)));
            if (!File.Exists(yol)) return yol;
        }
    }

    private async Task VacuumInto(string yol, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FuelControlDbContext>();
        var ulanish = (SqliteConnection)db.Database.GetDbConnection();
        await ulanish.OpenAsync(ct);
        try
        {
            // Fayl nomi serverda yaratiladi (foydalanuvchi kiritmaydi); apostrof ekranlanadi.
            await using var buyruq = ulanish.CreateCommand();
            buyruq.CommandText = $"VACUUM INTO '{yol.Replace("'", "''")}'";
            await buyruq.ExecuteNonQueryAsync(ct);
        }
        finally { await ulanish.CloseAsync(); }
    }

    private void Tozala(string papka)
    {
        if (!Directory.Exists(papka)) return;
        var nomlar = Directory.EnumerateFiles(papka, "fuelcontrol-*.db").Select(Path.GetFileName).OfType<string>().ToList();
        foreach (var nom in ZaxiraQoidalari.OchiriladiganlarNomi(nomlar, Saqlash))
        {
            try { File.Delete(Path.Combine(papka, nom)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log.LogWarning("Eski zaxira nusxani o'chirib bo'lmadi: {Fayl} ({Xato})", nom, e.Message);
            }
        }
    }

    /// <summary>Sozlangan va standart papkalardagi nusxalar (yo'q papkalar o'tkazib yuboriladi, yaratilmaydi), yangidan eskiga.</summary>
    public IReadOnlyList<(string ToliqYol, ZaxiraNomi Nom, long Hajm, bool StandartPapkada)> Fayllar()
    {
        var sozlangan = ZaxiraYollari.Sozlangan(konfiguratsiya);
        var standart = ZaxiraYollari.Standart(konfiguratsiya);
        var bitta = ZaxiraYollari.Bir(sozlangan, standart);
        var papkalar = bitta ? [sozlangan] : new[] { sozlangan, standart };
        var natija = new List<(string, ZaxiraNomi, long, bool)>();
        foreach (var papka in papkalar)
        {
            if (!Directory.Exists(papka)) continue;
            try
            {
                foreach (var f in Directory.EnumerateFiles(papka, "fuelcontrol-*.db"))
                    if (ZaxiraQoidalari.Tahlil(f) is { } nom)
                        natija.Add((f, nom, new FileInfo(f).Length, !bitta && ZaxiraYollari.Bir(papka, standart)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log.LogWarning("Zaxira papkasini o'qib bo'lmadi: {Papka} ({Xato})", papka, e.Message);
            }
        }
        natija.Sort((a, b) => ZaxiraNomi.Solishtir(b.Item2, b.Item1, a.Item2, a.Item1));                            // yangidan eskiga
        return natija;
    }

    public ZaxiraNusxasiDto[] Nusxalar(int limit = 200) =>
        Fayllar().Take(limit).Select(f => new ZaxiraNusxasiDto(Path.GetFileName(f.ToliqYol), f.Nom.Vaqt, f.Hajm, f.Nom.Sabab, f.Nom.SmenaId, f.StandartPapkada)).ToArray();
}

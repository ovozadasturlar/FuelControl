using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelControl.Contracts.Dto;
using FuelControl.Desktop.Services;

namespace FuelControl.Desktop.ViewModels;

/// <summary>§8.17: zaxira nusxalari ro'yxatidagi bitta qator (matnlar joriy tilda).</summary>
public sealed record ZaxiraQatori(string Nomi, string Vaqt, string Hajm, string Sabab, bool StandartPapkada)
{
    public string StandartPapkadaMatn => Til.T("Zaxira_StandartPapkada");
}

/// <summary>§8.17 Sozlamalar → Zaxira nusxa: papka, oxirgi nusxalar, Telegram (token serverda, klientga faqat maska keladi).</summary>
public partial class SozlamalarViewModel
{
    private ZaxiraHolatiDto? _zaxira;
    private int _zaxiraSorov;

    [ObservableProperty] private string _oxirgiNusxa = "—";
    [ObservableProperty] private string _zaxiraPapka = "—";
    [ObservableProperty] private string _zaxiraQachon = "";
    [ObservableProperty] private string _zaxiraOgoh = "";
    [ObservableProperty] private string _zaxiraXato = "";
    [ObservableProperty] private string _zaxiraOgohSabab = "";
    public bool ZaxiraOgohSababBor => ZaxiraOgohSabab.Length > 0;
    partial void OnZaxiraOgohSababChanged(string value) => OnPropertyChanged(nameof(ZaxiraOgohSababBor));
    public List<ZaxiraQatori> ZaxiraNusxalari { get; private set; } = new();
    public bool ZaxiraNusxaYoq => _zaxira is not null && ZaxiraNusxalari.Count == 0;
    public bool ZaxiraOgohBor => ZaxiraOgoh.Length > 0;
    public bool ZaxiraXatoBor => ZaxiraXato.Length > 0;

    // ---- Telegram (qoralama: faqat "Saqlash" bilan yuboriladi)
    [ObservableProperty] private string _tgToken = "";
    [ObservableProperty] private string _tgChatId = "";
    [ObservableProperty] private bool _tgYoqilgan;
    [ObservableProperty] private string _tgXato = "";
    [ObservableProperty] private string _tgTokenMaska = "";
    [ObservableProperty] private string _tgHolat = "";
    [ObservableProperty] private bool _tgHolatXato;
    [ObservableProperty] private string _tgEslatma = "";
    /// <summary>Eslatma turi: "izoh" — sozlanmagan (kulrang), "xato" — token yoki chat ID yetishmaydi (qizil), "ogoh" — yuborilmagan nusxa (sariq).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TgEslatmaIzoh), nameof(TgEslatmaXato), nameof(TgEslatmaOgoh))] private string _tgEslatmaTuri = "";
    public bool TgEslatmaIzoh => TgEslatmaTuri == "izoh";
    public bool TgEslatmaXato => TgEslatmaTuri == "xato";
    public bool TgEslatmaOgoh => TgEslatmaTuri == "ogoh";
    public bool TgXatoBor => TgXato.Length > 0;
    public bool TgEslatmaBor => TgEslatma.Length > 0;
    public bool TokenBor => _zaxira?.Telegram.TokenMaska is { Length: > 0 };
    public bool TgSaqlanmagan => _zaxira is not null && (TgToken.Trim().Length > 0 || TgChatId.Trim() != (_zaxira.Telegram.ChatId ?? "")
                                                         || TgYoqilgan != _zaxira.Telegram.Yoqilgan);

    partial void OnTgTokenChanged(string value) => TgQoralamaOzgardi();
    partial void OnTgChatIdChanged(string value) => TgQoralamaOzgardi();
    partial void OnTgYoqilganChanged(bool value) => TgQoralamaOzgardi();
    partial void OnTgXatoChanged(string value) => OnPropertyChanged(nameof(TgXatoBor));
    partial void OnTgEslatmaChanged(string value) => OnPropertyChanged(nameof(TgEslatmaBor));
    partial void OnZaxiraOgohChanged(string value) => OnPropertyChanged(nameof(ZaxiraOgohBor));
    partial void OnZaxiraXatoChanged(string value) => OnPropertyChanged(nameof(ZaxiraXatoBor));
    private void TgQoralamaOzgardi()
    {
        TgXato = "";
        OnPropertyChanged(nameof(TgSaqlanmagan));
        TelegramniSaqlaCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Sozlamalar sahifasiga qaytilganda — Zaxira tabi ochiq bo'lsa holat yangilanadi (smena yopilgandagi nusxa ko'rinsin).</summary>
    public void SahifaOchildi() { if (ZaxiraBolimi) _ = ZaxiraniYukla(); }

    private async Task ZaxiraniYukla()
    {
        if (!Malumot.AloqaBor) return;
        var n = ++_zaxiraSorov;
        try
        {
            var h = await Malumot.ZaxiraHolati();
            if (n == _zaxiraSorov) ZaxiraniQollash(h);
        }
        catch (ApiXatosi e) { if (n == _zaxiraSorov) ZaxiraXato = e.Message; }
    }

    /// <summary>Server holatini ko'rsatadi. Foydalanuvchi tahrirlamagan Telegram maydonlari server qiymatiga tenglanadi.</summary>
    private void ZaxiraniQollash(ZaxiraHolatiDto h, bool qoralamaniTozala = false)
    {
        var oldingi = _zaxira;
        var tahrirlanmagan = oldingi is null || qoralamaniTozala
                             || (TgChatId.Trim() == (oldingi.Telegram.ChatId ?? "") && TgYoqilgan == oldingi.Telegram.Yoqilgan);
        _zaxira = h;
        ZaxiraXato = "";
        if (qoralamaniTozala) TgToken = "";
        if (tahrirlanmagan)
        {
            TgChatId = h.Telegram.ChatId ?? "";
            TgYoqilgan = h.Telegram.Yoqilgan;
        }
        ZaxiraTilYangila();
        TgQoralamaOzgardi();
        TokenniOchirCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Holatdan matnlar (til almashganda ham chaqiriladi).</summary>
    private void ZaxiraTilYangila()
    {
        if (_zaxira is not { } h) return;
        ZaxiraPapka = h.Papka;
        ZaxiraQachon = Til.F("Zaxira_Qachon", h.Saqlash);
        // StandartPapkaga — sozlangan papka ishlamayapti; faqat sabab bo'lsa — papka (standart bo'lsa ham) yozib bo'lmayapti.
        var sabab = string.IsNullOrWhiteSpace(h.StandartPapkaSababi) ? null : h.StandartPapkaSababi.Trim();
        // Sabab (server matni) sariq qutidagi asosiy matn ostida kichik qatorda.
        ZaxiraOgoh = h.StandartPapkaga ? Til.F("Zaxira_PapkaStandart", h.SozlanganPapka) : sabab ?? "";
        ZaxiraOgohSabab = h.StandartPapkaga ? sabab ?? "" : "";
        ZaxiraNusxalari = h.Nusxalar.Select(x => new ZaxiraQatori(x.Nomi, Format.SanaVaqt(Mahalliy(x.Vaqt)), Hajm(x.HajmBayt),
            SababMatn(x.Sabab, x.SmenaId), x.StandartPapkada)).ToList();
        OnPropertyChanged(nameof(ZaxiraNusxalari));
        OnPropertyChanged(nameof(ZaxiraNusxaYoq));
        OxirgiNusxa = ZaxiraNusxalari.FirstOrDefault() is { } o ? $"{o.Vaqt} · {o.Nomi} · {o.Hajm} · {o.Sabab}" : Til.T("Zaxira_NusxaYoq");

        var t = h.Telegram;
        TgTokenMaska = t.TokenMaska is { Length: > 0 } m ? Til.F("Zaxira_TokenSaqlangan", m) : Til.T("Zaxira_TokenYoq");
        if (t.OxirgiVaqt is not { } v)
        {
            TgHolat = Til.T("Zaxira_YuborishYoq");
            TgHolatXato = false;
        }
        else
        {
            var qachon = $"{Format.SanaVaqt(Mahalliy(v))} · {Til.T(t.OxirgiTuri == "sinov" ? "Zaxira_TuriSinov" : "Zaxira_TuriNusxa")}";
            TgHolatXato = t.OxirgiMuvaffaqiyat != true;
            TgHolat = TgHolatXato ? Til.F("Zaxira_YuborishXato", qachon, t.OxirgiXabar ?? "") : Til.F("Zaxira_YuborishOk", qachon);
        }
        // Yoqilgan, lekin token ham, chat ID ham yo'q — neytral "sozlanmagan"; yoqilgan, lekin bittasi yetishmaydi — qizil;
        // nusxa yuborilmay qolgan bo'lsa (keyingi safar qayta yuboriladi) — sariq.
        var tokenBor = t.TokenMaska is { Length: > 0 };
        var chatBor = t.ChatId is { Length: > 0 };
        (TgEslatma, TgEslatmaTuri) =
            t.Yoqilgan && !tokenBor && !chatBor ? (Til.T("Zaxira_TelegramSozlanmagan"), "izoh")
            : t.Yoqilgan && !t.Faol ? (Til.T(!tokenBor ? "Zaxira_XatoToken" : "Zaxira_XatoChatId"), "xato")
            : t.Faol && t.OxirgiVaqt is not null && t.OxirgiMuvaffaqiyat != true && t.OxirgiTuri != "sinov" ? (Til.T("Zaxira_Kutilmoqda"), "ogoh")
            : ("", "");
        OnPropertyChanged(nameof(TokenBor));
    }

    private static DateTime Mahalliy(DateTime v) =>
        (v.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : v).ToLocalTime();

    private static string Hajm(long bayt) => bayt < 1024 * 1024
        ? Til.F("Zaxira_Kb", Math.Max(1, (long)Math.Round(bayt / 1024m)).ToString("#,0", CultureInfo.InvariantCulture).Replace(",", Format.Ajratgich))
        : Til.F("Zaxira_Mb", (bayt / 1048576m).ToString("0.0", CultureInfo.InvariantCulture));

    private static string SababMatn(string sabab, int? smenaId) => sabab switch
    {
        "kunlik" => Til.T("Zaxira_SababKunlik"),
        "smena" => Til.F("Zaxira_SababSmena", smenaId?.ToString() ?? "?"),
        "qolda" => Til.T("Zaxira_SababQolda"),
        _ => "—",
    };

    // ================= Amallar =================
    [RelayCommand(CanExecute = nameof(AloqaBor))]
    private async Task HozirNusxa()
    {
        try
        {
            var z = await Malumot.Zaxira();
            Bildirish.Malumot(Til.F("Zaxira_NusxaOlindi", z.FaylNomi));
            await ZaxiraniYukla();
            // Telegram'ga fon rejimida yuboriladi — natijasi bir necha soniyadan keyin holatda ko'rinadi.
            // Tugma band qolmasin: kechiktirilgan yangilash buyruqdan tashqarida.
            if (z.TelegramYuboriladi) _ = KechiktibYukla();
        }
        catch (ApiXatosi e) { Bildirish.Xato(e.Message); }
    }

    private async Task KechiktibYukla()
    {
        await Task.Delay(5000);
        await ZaxiraniYukla();
    }

    /// <summary>Papka shu kompyuterda bo'lsa (server bilan bir kompyuter) — Explorer'da ochiladi, aks holda serverdagi yo'l aytiladi.</summary>
    [RelayCommand]
    private void PapkaniOch()
    {
        var papka = _zaxira?.Papka;
        if (!string.IsNullOrWhiteSpace(papka) && System.IO.Directory.Exists(papka))
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(papka) { UseShellExecute = true }); return; }
            catch (Exception) { }
        }
        Bildirish.Malumot(Til.F("Zaxira_PapkaServerda", papka ?? "—"));
    }

    /// <summary>Tiklash klientdan qilinmaydi (avvalgidek) — qanday tiklanishi va nusxalar qayerdaligi aytiladi.</summary>
    [RelayCommand]
    private void NusxadanTiklash() => Bildirish.Malumot(Til.F("Zaxira_TiklashIzoh", _zaxira?.Papka ?? "—"));

    private bool TelegramSaqlaMumkin() => Malumot.AloqaBor && TgSaqlanmagan;

    [RelayCommand(CanExecute = nameof(TelegramSaqlaMumkin))]
    private async Task TelegramniSaqla()
    {
        if (await TelegramQoralamaniYubor()) Bildirish.Malumot(Til.T("Zaxira_TelegramSaqlandi"));
    }

    /// <summary>Qoralamani PUT /zaxira/telegram ga yuboradi: o'zgarmagan maydon null (o'zgarmaydi), bo'shatilgan chat ID "" (o'chiriladi).</summary>
    private async Task<bool> TelegramQoralamaniYubor()
    {
        if (_zaxira is not { } h) return false;
        var token = TgToken.Trim();
        var chat = TgChatId.Trim();
        if (TgYoqilgan && token.Length == 0 && !TokenBor) { TgXato = Til.T("Zaxira_XatoToken"); return false; }
        if (TgYoqilgan && chat.Length == 0) { TgXato = Til.T("Zaxira_XatoChatId"); return false; }
        var d = new ZaxiraTelegramTahrirDto(token.Length > 0 ? token : null,
            chat != (h.Telegram.ChatId ?? "") ? chat : null,
            TgYoqilgan != h.Telegram.Yoqilgan ? TgYoqilgan : null);
        try
        {
            ZaxiraniQollash(await Malumot.ZaxiraTelegram(d), qoralamaniTozala: true);
            return true;
        }
        catch (ApiXatosi e) when (e.Status == 400) { TgXato = e.Message; }
        catch (ApiXatosi e) { Bildirish.Xato(e.Message); }
        return false;
    }

    private bool TokenOchirMumkin() => Malumot.AloqaBor && TokenBor;

    [RelayCommand(CanExecute = nameof(TokenOchirMumkin))]
    private async Task TokenniOchir()
    {
        if (!await Bildirish.Tasdiqla(Til.T("Zaxira_TokenOchir"), Til.T("Zaxira_TokenOchirSavol"), Til.T("Ochirish"), xavfli: true)) return;
        try { ZaxiraniQollash(await Malumot.ZaxiraTelegram(new ZaxiraTelegramTahrirDto("", null, null)), qoralamaniTozala: true); }
        catch (ApiXatosi e) { Bildirish.Xato(e.Message); }
    }

    /// <summary>Kichik matnli sinov xabari. Saqlanmagan o'zgarish bo'lsa — avval saqlanadi (sinov serverdagi sozlama bilan yuboriladi).</summary>
    [RelayCommand(CanExecute = nameof(AloqaBor))]
    private async Task SinovYubor()
    {
        if (TgSaqlanmagan && !await TelegramQoralamaniYubor()) return;
        try
        {
            var h = await Malumot.ZaxiraTelegramSinov();
            ZaxiraniQollash(h);
            if (h.Telegram.OxirgiMuvaffaqiyat == true) Bildirish.Malumot(Til.T("Zaxira_SinovYuborildi"));
            else Bildirish.Xato(h.Telegram.OxirgiXabar ?? TgHolat);
        }
        catch (ApiXatosi e) when (e.Status == 400) { TgXato = e.Message; }
        catch (ApiXatosi e) { Bildirish.Xato(e.Message); }
    }
}

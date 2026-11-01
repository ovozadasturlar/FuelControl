using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelControl.Contracts.Dto;
using FuelControl.Desktop.Models;
using FuelControl.Desktop.Services;

namespace FuelControl.Desktop.ViewModels;

public partial class YoqilgiQatoriVm : ObservableObject
{
    public YoqilgiTuri Yoqilgi { get; }
    public string Nomi => Yoqilgi.Nomi;
    public string Rang => Yoqilgi.Rang;
    public string JoriyNarx => Format.Pul(Yoqilgi.Narx);
    public string AparatSoni => Til.F("Sozlama_AparatSoni", Malumot.Aparatlar.Count(a => a.Yoqilgi.Id == Yoqilgi.Id));
    [ObservableProperty] private string _yangiNarx = "";
    public YoqilgiQatoriVm(YoqilgiTuri y)
    {
        Yoqilgi = y;
        Til.Ozgardi += () => OnPropertyChanged(string.Empty);
        // YangiNarx (kiritilayotgan matn) tegilmaydi — faqat ko'rsatiladigan qiymatlar yangilanadi.
        Malumot.Ozgardi += () => { OnPropertyChanged(nameof(Nomi)); OnPropertyChanged(nameof(Rang)); OnPropertyChanged(nameof(JoriyNarx)); OnPropertyChanged(nameof(AparatSoni)); };
    }
    public void NarxYangilandi() => OnPropertyChanged(nameof(JoriyNarx));
}

/// <summary>Bitta ruxsat qatori — qoralama: o'zgarish faqat "Ruxsatlarni saqlash" tugmasi bilan yuboriladi (§7.14).</summary>
public partial class RuxsatElementi : ObservableObject
{
    public Ruxsat Ruxsat { get; }
    public string Nomi => Ruxsatlar.Nomi(Ruxsat);
    /// <summary>Faqat amallar uchun izoh (bo'limlar nomi o'zi tushunarli).</summary>
    public string Izoh => Guruh == "A" ? Ruxsatlar.Izoh(Ruxsat) : "";
    public bool IzohBor => Guruh == "A";
    public bool Yangi => Ruxsatlar.Yangilar.Contains(Ruxsat);
    public string Guruh { get; }
    [ObservableProperty] private bool _tanlangan;

    public RuxsatElementi(Foydalanuvchi f, (Ruxsat Ruxsat, string Guruh) r)
    {
        Ruxsat = r.Ruxsat; Guruh = r.Guruh;
        _tanlangan = f.Bor(r.Ruxsat);
        Til.Ozgardi += () => OnPropertyChanged(string.Empty);
    }
}

/// <summary>Rang tanlash chipi.</summary>
public partial class RangElementi : ObservableObject
{
    public string Rang { get; init; } = "";
    [ObservableProperty] private bool _tanlangan;
}

/// <summary>ComboBox uchun rol elementi (nomi tilga qarab).</summary>
public sealed class RolElementi
{
    public Rol Rol { get; init; }
    public string Nomi => Til.T("Rol_" + Rol);
    public override string ToString() => Nomi;
}

/// <summary>Sozlamalar (Admin): narxlar, aparatlar, foydalanuvchilar, ruxsatlar, zaxira. Hamma o'zgarish server orqali.</summary>
public partial class SozlamalarViewModel : ObservableObject
{
    public ObservableCollection<YoqilgiQatoriVm> Yoqilgilar { get; } = new();
    public ObservableCollection<YoqilgiTuri> YoqilgiTurlari { get; } = new();

    // ---- Yoqilg'i dialogi
    public static readonly string[] Palitra = ["#2F6BFF", "#7C5CFF", "#E8A317", "#1EA66A", "#19B5C9", "#E5484D", "#F0668A", "#5A6B88"];
    public List<RangElementi> Ranglar { get; } = Palitra.Select(r => new RangElementi { Rang = r }).ToList();
    [ObservableProperty] private bool _yoqilgiDialogOchiq;
    [ObservableProperty] private YoqilgiTuri? _yoqilgiTahrir;
    [ObservableProperty] private string _yNomi = "";
    [ObservableProperty] private string _yNarx = "";
    [ObservableProperty] private string _yRang = "#2F6BFF";
    [ObservableProperty] private string _yXato = "";
    public string YoqilgiDialogSarlavha => YoqilgiTahrir is null ? Til.T("YangiYoqilgi") : Til.T("YoqilginiTahrirlash");
    public bool YoqilgiYangi => YoqilgiTahrir is null;
    public bool YoqilginiOchirishMumkin => YoqilgiTahrir is not null && !Malumot.Aparatlar.Any(a => a.Yoqilgi.Id == YoqilgiTahrir.Id);
    public ObservableCollection<Aparat> Aparatlar { get; } = new();
    public List<AparatKartasi> AparatKartalari { get; private set; } = new();
    public ObservableCollection<Foydalanuvchi> Foydalanuvchilar { get; } = new();
    public ObservableCollection<NarxTarixi> NarxTarixi { get; } = new();
    public List<RolElementi> Rollar { get; private set; } = Enum.GetValues<Rol>().Select(r => new RolElementi { Rol = r }).ToList();

    [ObservableProperty] private int _bolim; // 0 narxlar, 1 aparatlar, 2 foydalanuvchilar, 3 ruxsatlar, 4 zaxira
    public bool NarxlarBolimi => Bolim == 0;
    public bool AparatlarBolimi => Bolim == 1;
    public bool FoydalanuvchilarBolimi => Bolim == 2;
    public bool RuxsatlarBolimi => Bolim == 3;
    public bool ZaxiraBolimi => Bolim == 4;

    // ---- Ruxsatlar bo'limi
    [ObservableProperty] private Foydalanuvchi? _ruxsatFoydalanuvchi;
    public ObservableCollection<RuxsatElementi> BolimRuxsatlari { get; } = new();
    public ObservableCollection<RuxsatElementi> AmalRuxsatlari { get; } = new();
    private HashSet<Ruxsat> Qoralama => BolimRuxsatlari.Concat(AmalRuxsatlari).Where(e => e.Tanlangan).Select(e => e.Ruxsat).ToHashSet();
    public string RuxsatIzohi => RuxsatFoydalanuvchi is null ? "" :
        $"{RuxsatFoydalanuvchi.RolNomi} · {Til.F("Sozlama_RuxsatSoni", Qoralama.Count)}";
    /// <summary>Qoralama serverdagidan farq qiladi — "Saqlanmagan o'zgarishlar".</summary>
    public bool RuxsatSaqlanmagan => RuxsatFoydalanuvchi is { } f && !Qoralama.SetEquals(f.Ruxsatlar);

    // ---- Aparat dialogi
    [ObservableProperty] private bool _aparatDialogOchiq;
    [ObservableProperty] private Aparat? _aparatTahrir;
    [ObservableProperty] private string _aparatRaqam = "";
    [ObservableProperty] private YoqilgiTuri? _aparatYoqilgi;
    [ObservableProperty] private string _aparatTotalLitr = "";
    [ObservableProperty] private string _aparatBak = "";
    [ObservableProperty] private string _aparatSabab = "";
    [ObservableProperty] private string _aparatXato = "";
    public string AparatDialogSarlavha => AparatTahrir is null ? Til.T("YangiAparat") : Til.F("Aparat_Tahrirlash", AparatTahrir.Raqam);
    public string AparatDialogIzoh => Til.T(AparatTahrir is null ? "Aparat_YangiIzoh" : "Aparat_AuditgaYoziladi");
    public bool AparatTahrirda => AparatTahrir is not null;
    public string AparatPultYorliq => Til.T(AparatTahrir is null ? "Aparat_BoshlangichPult" : "Aparat_PultLitr").ToUpperInvariant();
    public string AparatBakYorliq => Til.T(AparatTahrir is null ? "Aparat_BoshlangichBak" : "Aparat_BakLitr").ToUpperInvariant();

    // §8.16: pult krugi. Yoqilgan bo'lsa pult ikki maydon bilan kiritiladi: "Pult ko'rsatkichi" (AparatTotalLitr) + "Krug";
    // umumiy = (krug − 1) × H + pult. Belgi almashtirilganda yozilgan qiymat shu qoida bo'yicha o'giriladi (umumiy litr o'zgarmaydi).
    [ObservableProperty] private bool _aparatKrug;
    [ObservableProperty] private string _aparatKrugHajmi = "";
    [ObservableProperty] private string _aparatKrugRaqami = "";
    private bool _aparatYuklanmoqda;
    public const decimal StandartKrug = 10_000m;
    private decimal? AparatH => Format.KasrOl(AparatKrugHajmi) is > 0 and var h ? h : null;
    /// <summary>Kiritilgan maydonlardan umumiy litr (krug o'chiq — pult maydonining o'zi); noto'g'ri bo'lsa null.</summary>
    private decimal? AparatUmumiy
    {
        get
        {
            if (Format.KasrOl(AparatTotalLitr) is not { } p) return null;
            if (!AparatKrug) return p;
            if (AparatH is not { } h || p < 0 || p >= h || !int.TryParse(AparatKrugRaqami.Trim(), out var k) || k < 1) return null;
            return Krug.Umumiy(p, k, h);
        }
    }
    public string AparatUmumiyMatn => AparatKrug && AparatUmumiy is { } u ? Til.F("Krug_Umumiy", Format.Son(u)) : "";
    public bool AparatUmumiyBor => AparatUmumiyMatn.Length > 0;

    partial void OnAparatKrugChanged(bool value)
    {
        if (!_aparatYuklanmoqda)
        {
            var h = AparatH ?? StandartKrug;
            if (AparatH is null) AparatKrugHajmi = Format.ButunLitr(h);
            // Yozilgan qiymat saqlanadi: umumiy → pult + krug yoki aksincha
            if (value && Format.KasrOl(AparatTotalLitr) is { } t && t >= 0)
            {
                AparatTotalLitr = Format.Son(Krug.Pult(t, h));
                AparatKrugRaqami = Krug.Raqam(t, h).ToString();
            }
            else if (!value && Format.KasrOl(AparatTotalLitr) is { } p && int.TryParse(AparatKrugRaqami.Trim(), out var k) && k >= 1)
                AparatTotalLitr = Format.Son(Krug.Umumiy(p, k, h));
        }
        AparatUmumiyYangila();
    }
    partial void OnAparatKrugHajmiChanged(string value) => AparatUmumiyYangila();
    partial void OnAparatKrugRaqamiChanged(string value) => AparatUmumiyYangila();
    partial void OnAparatTotalLitrChanged(string value) => AparatUmumiyYangila();
    private void AparatUmumiyYangila() { OnPropertyChanged(nameof(AparatUmumiyMatn)); OnPropertyChanged(nameof(AparatUmumiyBor)); }

    // ---- Foydalanuvchi dialogi
    [ObservableProperty] private bool _fDialogOchiq;
    [ObservableProperty] private Foydalanuvchi? _fTahrir;
    [ObservableProperty] private string _fIsm = "";
    [ObservableProperty] private string _fLogin = "";
    [ObservableProperty] private RolElementi? _fRol;
    [ObservableProperty] private string _fMaosh = "";
    [ObservableProperty] private bool _fFaol = true;
    [ObservableProperty] private string _fPinParol = "";
    [ObservableProperty] private string _fXato = "";
    public string FDialogSarlavha => FTahrir is null ? Til.T("YangiFoydalanuvchi") : Til.T("FoydalanuvchiniTahrirlash");
    public bool FYangi => FTahrir is null;

    // ---- PIN/parol tiklash dialogi
    [ObservableProperty] private bool _tiklashOchiq;
    [ObservableProperty] private Foydalanuvchi? _tiklashF;
    [ObservableProperty] private string _yangiPin = "";

    private string _yoqilgiImzo = "", _aparatImzo = "", _foydalanuvchiImzo = "", _narxImzo = "";

    public SozlamalarViewModel()
    {
        Sinxronla();
        Malumot.Ozgardi += Sinxronla;
        Navigatsiya.SozlamaBolimSorovi += b => Bolim = b;
        Malumot.AloqaOzgardi += () =>
        {
            foreach (var c in new IRelayCommand[] { NarxniSaqlaCommand, YoqilginiSaqlaCommand, YoqilginiOchirCommand, AparatniSaqlaCommand,
                         FoydalanuvchiniSaqlaCommand, StandartRuxsatlarCommand, TiklashniSaqlaCommand, HozirNusxaCommand,
                         TelegramniSaqlaCommand, TokenniOchirCommand, SinovYuborCommand })
                c.NotifyCanExecuteChanged();
        };
        Til.Ozgardi += () =>
        {
            Rollar = Enum.GetValues<Rol>().Select(r => new RolElementi { Rol = r }).ToList();
            ZaxiraTilYangila();
            OnPropertyChanged(string.Empty);
        };
    }

    private static bool AloqaBor() => Malumot.AloqaBor;

    /// <summary>Bo'sh baza: yoqilg'i yo'q — aparat qo'shib bo'lmaydi (aparat yoqilg'iga bog'lanadi).</summary>
    public bool YoqilgiYoq => Malumot.Yoqilgilar.Count == 0;
    private bool AparatQoshMumkin() => !YoqilgiYoq;
    [RelayCommand] private void YoqilgiBolimigaOt() => Bolim = 0;

    /// <summary>Kesh o'zgarganda ro'yxatlarni yangilaydi — faqat haqiqatan o'zgarganini (kiritilayotgan matn va tanlov saqlanadi).</summary>
    private void Sinxronla()
    {
        OnPropertyChanged(nameof(YoqilgiYoq));
        AparatQoshCommand.NotifyCanExecuteChanged();
        var yImzo = string.Join("|", Malumot.Yoqilgilar.Select(y => $"{y.Id}:{y.Nomi}:{y.Narx}:{y.Rang}"));
        if (yImzo != _yoqilgiImzo)
        {
            var yIdlar = string.Join(",", Malumot.Yoqilgilar.Select(y => y.Id));
            if (yIdlar != string.Join(",", Yoqilgilar.Select(q => q.Yoqilgi.Id)))
            {
                Yoqilgilar.Clear();
                foreach (var y in Malumot.Yoqilgilar) Yoqilgilar.Add(new YoqilgiQatoriVm(y));
            }
            var tanlangan = AparatYoqilgi?.Id;
            YoqilgiTurlari.Clear();
            foreach (var y in Malumot.Yoqilgilar) YoqilgiTurlari.Add(y);
            if (tanlangan is not null) AparatYoqilgi = YoqilgiTurlari.FirstOrDefault(y => y.Id == tanlangan);
            _yoqilgiImzo = yImzo;
        }

        var aImzo = string.Join("|", Malumot.Aparatlar.Select(a => $"{a.Id}:{a.Raqam}:{a.Yoqilgi.Id}:{a.Yoqilgi.Nomi}:{a.TotalLitr}:{a.BakQoldiq}:{a.Yoqilgi.Rang}:{a.KrugHajmi}"));
        if (aImzo != _aparatImzo)
        {
            Aparatlar.Clear();
            foreach (var a in Malumot.Aparatlar.OrderBy(a => a.Raqam)) Aparatlar.Add(a);
            AparatKartalari = Malumot.Aparatlar.OrderBy(a => a.Raqam).Select(a => new AparatKartasi(a, YoqilgiBelgi.Ol(a.Yoqilgi))).ToList();
            OnPropertyChanged(nameof(AparatKartalari));
            _aparatImzo = aImzo;
            OnPropertyChanged(nameof(YoqilginiOchirishMumkin));
        }

        var fImzo = string.Join("|", Malumot.Foydalanuvchilar.Select(f => $"{f.Id}:{f.ToliqIsm}:{f.Login}:{f.Rol}:{f.Faol}:{f.OylikMaosh}:{f.RuxsatSoni}"));
        if (fImzo != _foydalanuvchiImzo)
        {
            Foydalanuvchilar.Clear();
            foreach (var f in Malumot.Foydalanuvchilar) Foydalanuvchilar.Add(f);
            _foydalanuvchiImzo = fImzo;
        }

        var nImzo = $"{Malumot.NarxTarixi.Count}:{Malumot.NarxTarixi.FirstOrDefault()?.Vaqt:O}";
        if (nImzo != _narxImzo)
        {
            NarxTarixi.Clear();
            foreach (var n in Malumot.NarxTarixi) NarxTarixi.Add(n);
            _narxImzo = nImzo;
        }

        // Ruxsatlar bo'limi: tanlangan foydalanuvchini Id bo'yicha saqlab, belgilar serverdagidan farq qilsa qayta quramiz.
        var rf = Foydalanuvchilar.FirstOrDefault(f => f.Id == RuxsatFoydalanuvchi?.Id)
                 ?? Foydalanuvchilar.FirstOrDefault(f => f.Rol == Rol.Operator) ?? Foydalanuvchilar.FirstOrDefault();
        var belgilar = BolimRuxsatlari.Concat(AmalRuxsatlari).Where(e => e.Tanlangan).Select(e => e.Ruxsat).ToHashSet();
        if (rf != RuxsatFoydalanuvchi || (rf is not null && !belgilar.SetEquals(rf.Ruxsatlar) && !_qoralamaTahrirda))
        {
            RuxsatFoydalanuvchi = null;
            RuxsatFoydalanuvchi = rf;
        }
        else OnPropertyChanged(nameof(RuxsatIzohi));
    }

    partial void OnBolimChanged(int value)
    {
        foreach (var n in new[] { nameof(NarxlarBolimi), nameof(AparatlarBolimi), nameof(FoydalanuvchilarBolimi), nameof(RuxsatlarBolimi), nameof(ZaxiraBolimi) })
            OnPropertyChanged(n);
        if (value == 4) _ = ZaxiraniYukla();
    }

    partial void OnRuxsatFoydalanuvchiChanged(Foydalanuvchi? value)
    {
        BolimRuxsatlari.Clear();
        AmalRuxsatlari.Clear();
        if (value is null) return;
        foreach (var r in Ruxsatlar.Royxat)
        {
            var e = new RuxsatElementi(value, r);
            e.PropertyChanged += (_, a) =>
            {
                if (a.PropertyName != nameof(RuxsatElementi.Tanlangan)) return;
                _qoralamaTahrirda = true;
                OnPropertyChanged(nameof(RuxsatIzohi)); OnPropertyChanged(nameof(RuxsatSaqlanmagan));
                RuxsatlarniSaqlaCommand.NotifyCanExecuteChanged();
            };
            (r.Guruh == "B" ? BolimRuxsatlari : AmalRuxsatlari).Add(e);
        }
        _qoralamaTahrirda = false;
        OnPropertyChanged(nameof(RuxsatIzohi)); OnPropertyChanged(nameof(RuxsatSaqlanmagan));
        RuxsatlarniSaqlaCommand.NotifyCanExecuteChanged();
    }

    private bool _qoralamaTahrirda;

    [RelayCommand] private void BolimniTanla(string i) => Bolim = int.Parse(i);
    [RelayCommand] private void RuxsatFoydalanuvchisiniTanla(Foydalanuvchi f) { _qoralamaTahrirda = false; RuxsatFoydalanuvchi = f; }

    /// <summary>Rol bo'yicha standart to'plam qoralamaga qo'yiladi (saqlash — alohida tugma).</summary>
    [RelayCommand(CanExecute = nameof(AloqaBor))]
    private void StandartRuxsatlar()
    {
        if (RuxsatFoydalanuvchi is null) return;
        var st = Ruxsatlar.Standart(RuxsatFoydalanuvchi.Rol);
        foreach (var e in BolimRuxsatlari.Concat(AmalRuxsatlari)) e.Tanlangan = st.Contains(e.Ruxsat);
    }

    private bool RuxsatSaqlashMumkin() => Malumot.AloqaBor && RuxsatSaqlanmagan;

    [RelayCommand(CanExecute = nameof(RuxsatSaqlashMumkin))]
    private async Task RuxsatlarniSaqla()
    {
        if (RuxsatFoydalanuvchi is not { } f) return;
        try
        {
            await Malumot.RuxsatlarniOrnat(f.Id, Qoralama);
            _qoralamaTahrirda = false;
            OnPropertyChanged(nameof(RuxsatSaqlanmagan));
            RuxsatlarniSaqlaCommand.NotifyCanExecuteChanged();
        }
        catch (ApiXatosi e) { Bildirish.Xato(e.Message); }
    }

    // ================= Narxlar =================
    [RelayCommand(CanExecute = nameof(AloqaBor))]
    private async Task NarxniSaqla(YoqilgiQatoriVm q)
    {
        // Narx o'zgarishi dialogi: ochiq smenada shu yoqilg'i aparatlarining hozirgi pult ko'rsatkichi so'raladi (§1.10).
        var yangi = Format.PulOl(q.YangiNarx);
        Dialoglar.Narx.Och(q.Yoqilgi);
        if (yangi is > 0) Dialoglar.Narx.YangiNarx = Format.Pul(yangi.Value);
        q.YangiNarx = "";
        await Task.CompletedTask;
    }

    // ================= Yoqilg'i turlari =================
    private void YoqilgiDialogniTayyorla(YoqilgiTuri? y)
    {
        YoqilgiTahrir = y;
        YNomi = y?.Nomi ?? "";
        YNarx = y is null ? "" : Format.Pul(y.Narx);
        YRangniTanla(y?.Rang ?? Palitra.FirstOrDefault(r => !Malumot.Yoqilgilar.Any(x => x.Rang == r)) ?? Palitra[0]);
        YXato = "";
        OnPropertyChanged(nameof(YoqilgiDialogSarlavha)); OnPropertyChanged(nameof(YoqilgiYangi)); OnPropertyChanged(nameof(YoqilginiOchirishMumkin));
        YoqilgiDialogOchiq = true;
    }

    [RelayCommand] private void YoqilgiQosh() => YoqilgiDialogniTayyorla(null);
    [RelayCommand] private void YoqilginiTahrirla(YoqilgiQatoriVm q) => YoqilgiDialogniTayyorla(q.Yoqilgi);
    [RelayCommand] private void YoqilgiDialogniYop() => YoqilgiDialogOchiq = false;

    [RelayCommand]
    private void YRangniTanla(string rang)
    {
        YRang = rang;
        foreach (var r in Ranglar) r.Tanlangan = r.Rang == rang;
    }

    [RelayCommand(CanExecute = nameof(AloqaBor))]
    private async Task YoqilginiSaqla()
    {
        var nomi = YNomi.Trim();
        var narx = long.TryParse(new string(YNarx.Where(char.IsDigit).ToArray()), out var n) ? n : 0;
        if (nomi.Length == 0 || narx <= 0) { YXato = Til.T("Xato_Maydon"); return; }
        if (Malumot.Yoqilgilar.Any(y => y.Nomi.Equals(nomi, StringComparison.OrdinalIgnoreCase) && y.Id != YoqilgiTahrir?.Id)) { YXato = Til.T("Xato_YoqilgiBand"); return; }

        try
        {
            if (YoqilgiTahrir is null) await Malumot.YoqilgiYarat(new YoqilgiYaratishDto(nomi, narx, YRang));
            else await Malumot.YoqilgiTahrirla(YoqilgiTahrir.Id, new YoqilgiTahrirlashDto(nomi, narx, YRang));
            YoqilgiDialogOchiq = false;
        }
        catch (ApiXatosi e) { YXato = e.Message; }
    }

    [RelayCommand(CanExecute = nameof(AloqaBor))]
    private async Task YoqilginiOchir()
    {
        if (YoqilgiTahrir is null) return;
        if (!YoqilginiOchirishMumkin) { YXato = Til.T("Xato_YoqilgiIshlatilmoqda"); return; }
        try
        {
            await Malumot.YoqilgiOchir(YoqilgiTahrir.Id);
            YoqilgiDialogOchiq = false;
        }
        catch (ApiXatosi e) { YXato = e.Message; }
    }

    // ================= Aparatlar =================
    [RelayCommand(CanExecute = nameof(AparatQoshMumkin))]
    private void AparatQosh()
    {
        AparatTahrir = null;
        AparatRaqam = (Malumot.Aparatlar.Count == 0 ? 1 : Malumot.Aparatlar.Max(a => a.Raqam) + 1).ToString();
        AparatYoqilgi = YoqilgiTurlari.FirstOrDefault();
        _aparatYuklanmoqda = true;
        AparatKrug = false; AparatKrugHajmi = Format.ButunLitr(StandartKrug); AparatKrugRaqami = "1";
        _aparatYuklanmoqda = false;
        AparatTotalLitr = ""; AparatBak = ""; AparatSabab = "";
        AparatXato = "";
        AparatSarlavhalari();
        AparatDialogOchiq = true;
    }

    [RelayCommand]
    private void AparatniTahrirla(Aparat a)
    {
        AparatTahrir = a;
        AparatRaqam = a.Raqam.ToString();
        AparatYoqilgi = YoqilgiTurlari.FirstOrDefault(y => y.Id == a.Yoqilgi.Id);
        _aparatYuklanmoqda = true;
        AparatKrug = a.KrugHajmi is not null;
        AparatKrugHajmi = Format.ButunLitr(a.KrugHajmi ?? StandartKrug);
        AparatKrugRaqami = a.KrugHajmi is { } h ? Krug.Raqam(a.TotalLitr, h).ToString() : "1";
        AparatTotalLitr = Krug.Son(a.TotalLitr, a.KrugHajmi);
        _aparatYuklanmoqda = false;
        AparatBak = Format.Son(a.BakQoldiq);
        AparatSabab = "";
        AparatXato = "";
        AparatSarlavhalari();
        AparatDialogOchiq = true;
    }

    [RelayCommand] private void AparatDialogniYop() => AparatDialogOchiq = false;

    private void AparatSarlavhalari()
    {
        foreach (var n in new[] { nameof(AparatDialogSarlavha), nameof(AparatDialogIzoh), nameof(AparatTahrirda), nameof(AparatPultYorliq), nameof(AparatBakYorliq) })
            OnPropertyChanged(n);
    }

    /// <summary>
    /// Yangi aparat: boshlang'ich pult ko'rsatkichi va bak qoldig'i. Tahrirda pult yoki bak o'zgarsa — sabab majburiy, server auditga yozadi;
    /// bakni qo'lda manfiy qilib bo'lmaydi (§7.11).
    /// </summary>
    [RelayCommand(CanExecute = nameof(AloqaBor))]
    private async Task AparatniSaqla()
    {
        if (!int.TryParse(AparatRaqam.Trim(), out var raqam) || raqam <= 0 || AparatYoqilgi is null) { AparatXato = Til.T("Xato_Maydon"); return; }
        if (Malumot.Aparatlar.Any(a => a.Raqam == raqam && a.Id != AparatTahrir?.Id)) { AparatXato = Til.T("Aparat_XatoRaqamBand"); return; }
        // Yangi aparatda boshlang'ich pult/bak jimgina 0 bo'lib ketmasin — haqiqiy ko'rsatkich kiritilishi shart (0 bo'lsa "0" yoziladi).
        if (AparatTahrir is null && (AparatTotalLitr.Trim().Length == 0 || AparatBak.Trim().Length == 0)) { AparatXato = Til.T("Xato_Maydon"); return; }
        // §8.16: krug yoqilgan bo'lsa — hajm > 0, pult 0 ≤ p < H, krug ≥ 1; umumiy = (krug − 1) × H + pult
        if (AparatKrug)
        {
            if (AparatH is not { } h) { AparatXato = Til.T("Krug_XatoHajm"); return; }
            if (Format.KasrOl(AparatTotalLitr) is { } p0 && (p0 < 0 || p0 >= h)) { AparatXato = Krug.ChegaraXatoMatn(h); return; }
            if (!int.TryParse(AparatKrugRaqami.Trim(), out var kr) || kr < 1) { AparatXato = Til.T("Krug_XatoRaqam"); return; }
        }
        var pult = AparatTotalLitr.Trim().Length == 0 ? 0m : AparatUmumiy;
        decimal? krugHajmi = AparatKrug ? AparatH : null;
        // Tahrirda bak o'zgartirilmagan bo'lsa — joriy qiymat (manfiy qoldiq "−1 234.00" ko'rinishida to'ldiriladi va KasrOl uni o'qimaydi;
        // aks holda manfiy bakli aparatni, masalan krugini yoqish uchun, umuman saqlab bo'lmasdi)
        var bak = AparatTahrir is { } at && AparatBak == Format.Son(at.BakQoldiq) ? at.BakQoldiq
            : AparatBak.Trim().Length == 0 ? 0m : Format.KasrOl(AparatBak);
        if (pult is null or < 0) { AparatXato = Til.T("Aparat_XatoPult"); return; }
        if (bak is null) { AparatXato = Til.T("Xato_Maydon"); return; }
        if (bak < 0 && bak != AparatTahrir?.BakQoldiq) { AparatXato = Til.T("Aparat_XatoBakManfiy"); return; }

        try
        {
            if (AparatTahrir is not { } a) await Malumot.AparatYarat(new AparatYaratishDto(raqam, AparatYoqilgi.Id, pult.Value, bak.Value, krugHajmi));
            else
            {
                var ozgardi = pult != a.TotalLitr || bak != a.BakQoldiq;
                var sabab = AparatSabab.Trim();
                if (ozgardi && sabab.Length == 0) { AparatXato = Til.T("Aparat_XatoSabab"); return; }
                // KrugHajmi: null — o'zgarmaydi, 0 — o'chiriladi, > 0 — o'rnatiladi (faqat ko'rinish, sabab so'ralmaydi)
                decimal? krug = krugHajmi is { } kh ? (kh != a.KrugHajmi ? kh : null) : (a.KrugHajmi is not null ? 0m : null);
                await Malumot.AparatTahrirla(a.Id, new AparatTahrirlashDto(raqam, AparatYoqilgi.Id,
                    pult != a.TotalLitr ? pult : null, bak != a.BakQoldiq ? bak : null, ozgardi ? sabab : null, krug));
            }
            AparatDialogOchiq = false;
        }
        catch (ApiXatosi e) { AparatXato = e.Message; }
    }

    // ================= Foydalanuvchilar =================
    [RelayCommand]
    private void FoydalanuvchiQosh()
    {
        FTahrir = null;
        FIsm = ""; FLogin = ""; FRol = Rollar.First(r => r.Rol == Rol.Operator); FMaosh = ""; FFaol = true; FPinParol = ""; FXato = "";
        OnPropertyChanged(nameof(FDialogSarlavha)); OnPropertyChanged(nameof(FYangi));
        FDialogOchiq = true;
    }

    [RelayCommand]
    private void FoydalanuvchiniTahrirla(Foydalanuvchi f)
    {
        FTahrir = f;
        FIsm = f.ToliqIsm; FLogin = f.Login; FRol = Rollar.First(r => r.Rol == f.Rol);
        FMaosh = f.OylikMaosh > 0 ? Format.Pul(f.OylikMaosh) : ""; FFaol = f.Faol; FPinParol = ""; FXato = "";
        OnPropertyChanged(nameof(FDialogSarlavha)); OnPropertyChanged(nameof(FYangi));
        FDialogOchiq = true;
    }

    [RelayCommand] private void FDialogniYop() => FDialogOchiq = false;

    [RelayCommand(CanExecute = nameof(AloqaBor))]
    private async Task FoydalanuvchiniSaqla()
    {
        var ism = FIsm.Trim(); var login = FLogin.Trim().ToLowerInvariant();
        if (ism.Length == 0 || login.Length == 0 || FRol is null || (FTahrir is null && FPinParol.Trim().Length == 0)) { FXato = Til.T("Xato_Maydon"); return; }
        if (Malumot.Foydalanuvchilar.Any(f => f.Login.Equals(login, StringComparison.OrdinalIgnoreCase) && f.Id != FTahrir?.Id)) { FXato = Til.T("Xato_LoginBand"); return; }
        var maosh = long.TryParse(new string(FMaosh.Where(char.IsDigit).ToArray()), out var m) ? m : 0;

        try
        {
            if (FTahrir is null)
            {
                var yangi = await Malumot.FoydalanuvchiYarat(new FoydalanuvchiYaratishDto(ism, login, FRol.Rol, maosh, FPinParol.Trim()));
                // Yaratish har doim faol foydalanuvchi beradi; nofaol belgilangan bo'lsa — darhol o'chiramiz.
                if (!FFaol) await Malumot.FoydalanuvchiTahrirla(yangi.Id, new FoydalanuvchiTahrirlashDto(ism, FRol.Rol, false, maosh, login), null);
            }
            else
            {
                await Malumot.FoydalanuvchiTahrirla(FTahrir.Id, new FoydalanuvchiTahrirlashDto(ism, FRol.Rol, FFaol, maosh, login), FPinParol);
            }
            FDialogOchiq = false;
        }
        catch (ApiXatosi e) { FXato = e.Message; }
    }

    [RelayCommand]
    private void TiklashniOch(Foydalanuvchi f) { TiklashF = f; YangiPin = ""; TiklashOchiq = true; }

    [RelayCommand] private void TiklashniYop() => TiklashOchiq = false;

    [RelayCommand(CanExecute = nameof(AloqaBor))]
    private async Task TiklashniSaqla()
    {
        if (TiklashF is null || YangiPin.Trim().Length == 0) return;
        try
        {
            await Malumot.PinOrnat(TiklashF.Id, YangiPin.Trim());
            TiklashOchiq = false;
        }
        catch (ApiXatosi e) { Bildirish.Xato(e.Message); }
    }
}

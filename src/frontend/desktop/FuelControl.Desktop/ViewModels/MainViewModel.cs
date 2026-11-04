using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelControl.Desktop.Models;
using FuelControl.Desktop.Services;

namespace FuelControl.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public LoginViewModel Login { get; }
    public BoshqaruvViewModel Boshqaruv { get; } = new();
    public SavdoViewModel Savdo { get; } = new();
    public SmenalarViewModel Smenalar { get; } = new();
    public NasiyalarViewModel Nasiyalar { get; } = new();
    public HisobotViewModel Hisobot { get; } = new();
    public OperatorlarViewModel Operatorlar { get; } = new();
    public SozlamalarViewModel Sozlamalar { get; } = new();
    public AuditViewModel Audit { get; } = new();

    [ObservableProperty] private bool _kirilgan;
    [ObservableProperty] private Foydalanuvchi _joriy = Malumot.JoriyFoydalanuvchi;

    // Umumiy xabar dialogi (Bildirish.Xato / Bildirish.Malumot)
    [ObservableProperty] private bool _bildirishOchiq;
    [ObservableProperty] private string _bildirishSarlavha = "";
    [ObservableProperty] private string _bildirishMatni = "";
    [ObservableProperty] private bool _bildirishXato;

    [RelayCommand] private void BildirishniYop() => BildirishOchiq = false;

    // Tasdiq dialogi (Bildirish.Tasdiqla)
    [ObservableProperty] private bool _tasdiqOchiq;
    [ObservableProperty] private string _tasdiqSarlavha = "";
    [ObservableProperty] private string _tasdiqMatni = "";
    [ObservableProperty] private string _tasdiqTugma = "";
    [ObservableProperty] private bool _tasdiqXavfli;
    [ObservableProperty] private string _tasdiqBekor = "";
    /// <summary>§8.15: fokus bekor tugmasida ("Tekshiraman"), Enter o'zi saqlamaydi.</summary>
    [ObservableProperty] private bool _tasdiqBekorFokus;
    private System.Threading.Tasks.TaskCompletionSource<bool>? _tasdiq;

    [RelayCommand] private void TasdiqJavob(string ha)
    {
        TasdiqOchiq = false;
        var t = _tasdiq; _tasdiq = null;
        t?.TrySetResult(ha == "1");
    }

    // Smena hisobi dialoglari (istalgan sahifadan ochiladi)
    public NasiyaDialogVM NasiyaDialog => Dialoglar.Nasiya;
    public QaytishDialogVM QaytishDialog => Dialoglar.Qaytish;
    public XarajatDialogVM XarajatDialog => Dialoglar.Xarajat;
    public BakKirimDialogVM BakDialog => Dialoglar.Bak;
    public NarxDialogVM NarxDialog => Dialoglar.Narx;
    public NarxTuzatishDialogVM NarxTuzatishDialog => Dialoglar.NarxTuzatish;

    /// <summary>Yon menyudagi indikator: SignalR ulanishi holati.</summary>
    public bool AloqaBor => Malumot.Holat == Malumot.AloqaHolati.Bor;
    public bool AloqaUlanmoqda => Malumot.Holat == Malumot.AloqaHolati.Ulanmoqda;
    public bool AloqaYoq => Malumot.Holat == Malumot.AloqaHolati.Yoq;

    // Yon menyu: yig'ilgan rejimda faqat ikonkalar qoladi
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MenyuKengligi), nameof(MenyuOchiq))]
    private bool _menyuYigilgan;
    public double MenyuKengligi => MenyuYigilgan ? 92 : 264;
    public bool MenyuOchiq => !MenyuYigilgan;

    [ObservableProperty] private bool _qorongiRejim;

    public Til Til => Til.Joriy;
    public string JoriyRolNomi => Joriy.RolNomi;

    /// <summary>Menyudagi "Nasiyalar" belgisi: muddati o'tgan qarzlar soni (0 bo'lsa ko'rinmaydi).</summary>
    public int MuddatiOtganSoni => Malumot.MuddatiOtganSoni;
    public bool MuddatiOtganBor => MuddatiOtganSoni > 0;
    public string MuddatiOtganIzoh => Til.F("MuddatiOtganQarzlarSoni", MuddatiOtganSoni);

    public const int SBoshqaruv = 0, SSavdo = 1, SSmenalar = 2, SHisobot = 3, SOperatorlar = 4, SSozlamalar = 5, SAudit = 6, SNasiyalar = 7;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BoshqaruvSahifasi), nameof(SavdoSahifasi), nameof(SmenalarSahifasi), nameof(NasiyalarSahifasi),
        nameof(HisobotSahifasi), nameof(OperatorlarSahifasi), nameof(SozlamalarSahifasi), nameof(AuditSahifasi))]
    private int _sahifa;

    partial void OnSahifaChanged(int value) { if (value == SSozlamalar) Sozlamalar.SahifaOchildi(); }
    public bool BoshqaruvSahifasi => Sahifa == SBoshqaruv;
    public bool SavdoSahifasi => Sahifa == SSavdo;
    public bool SmenalarSahifasi => Sahifa == SSmenalar;
    public bool NasiyalarSahifasi => Sahifa == SNasiyalar;
    public bool HisobotSahifasi => Sahifa == SHisobot;
    public bool OperatorlarSahifasi => Sahifa == SOperatorlar;
    public bool SozlamalarSahifasi => Sahifa == SSozlamalar;
    public bool AuditSahifasi => Sahifa == SAudit;

    // Ruxsatlarga qarab menyu bo'limlari
    public bool KoradiBoshqaruv => Joriy.Bor(Ruxsat.Boshqaruv);
    public bool KoradiSavdo => Joriy.Bor(Ruxsat.Savdo);
    public bool KoradiSmenalar => Joriy.Bor(Ruxsat.Smenalar);
    public bool KoradiNasiyalar => Joriy.Bor(Ruxsat.Nasiyalar);
    public bool KoradiHisobot => Joriy.Bor(Ruxsat.Hisobotlar);
    public bool KoradiOperatorlar => Joriy.Bor(Ruxsat.Operatorlar);
    public bool KoradiAudit => Joriy.Bor(Ruxsat.Audit);
    public bool KoradiSozlamalar => Joriy.Bor(Ruxsat.Sozlamalar);
    public bool KoradiEksport => Joriy.Bor(Ruxsat.Eksport);
    public bool KoradiAvans => Joriy.Bor(Ruxsat.AvansBerish);

    public MainViewModel()
    {
        Login = new LoginViewModel(f =>
        {
            Joriy = f;
            OnPropertyChanged(nameof(JoriyRolNomi));
            RuxsatlarniYangila();
            Sahifa = BirinchiSahifa();
            Kirilgan = true;
        });
        Malumot.Ozgardi += RuxsatlarniYangila;
        Malumot.AloqaOzgardi += () => { OnPropertyChanged(nameof(AloqaBor)); OnPropertyChanged(nameof(AloqaUlanmoqda)); OnPropertyChanged(nameof(AloqaYoq)); };
        Malumot.MajburiyChiqish += sabab => _ = ChiqishAsync(sabab);
        Malumot.QaytaKirishSorovi = SessiyaniSora;
        Bildirish.Korsatildi += (sarlavha, matn, xato) =>
        {
            BildirishSarlavha = sarlavha; BildirishMatni = matn; BildirishXato = xato;
            BildirishOchiq = true;
        };
        Bildirish.TasdiqSoraldi += s =>
        {
            _tasdiq?.TrySetResult(false);
            _tasdiq = s.Javob;
            TasdiqSarlavha = s.Sarlavha; TasdiqMatni = s.Matn; TasdiqTugma = s.Tugma; TasdiqXavfli = s.Xavfli;
            TasdiqBekor = s.Bekor ?? Til.T("BekorQilish"); TasdiqBekorFokus = s.BekorFokus;
            TasdiqOchiq = true;
        };
        Til.Ozgardi += () => OnPropertyChanged(string.Empty);
        // Boshqa sahifadan (Boshqaruv, Savdo) "Barcha nasiyalar" / "Barcha smenalar" kabi o'tishlar.
        Navigatsiya.Sorov += s => { if (SahifaRuxsatli(s)) Sahifa = s; };
    }

    private async System.Threading.Tasks.Task ChiqishAsync(string? sabab)
    {
        if (!Kirilgan) return;
        if (SessiyaOchiq) SessiyaniYop(false);
        Kirilgan = false;
        foreach (var d in new DialogVM[] { Dialoglar.Nasiya, Dialoglar.Qaytish, Dialoglar.Xarajat, Dialoglar.Bak, Dialoglar.Narx, Dialoglar.NarxTuzatish }) d.Ochiq = false;
        if (TasdiqOchiq) TasdiqJavob("0");
        await Malumot.Chiqish();
        Joriy = Malumot.JoriyFoydalanuvchi;
        Login.Tozala(sabab);
    }

    /// <summary>Ruxsati bor birinchi bo'lim — kirgandan keyin shu ochiladi.</summary>
    private int BirinchiSahifa()
    {
        if (KoradiBoshqaruv) return SBoshqaruv;
        if (KoradiSavdo) return SSavdo;
        if (KoradiSmenalar) return SSmenalar;
        if (KoradiNasiyalar) return SNasiyalar;
        if (KoradiHisobot) return SHisobot;
        if (KoradiOperatorlar) return SOperatorlar;
        if (KoradiAudit) return SAudit;
        return SSozlamalar;
    }

    private void RuxsatlarniYangila()
    {
        foreach (var n in new[] { nameof(KoradiBoshqaruv), nameof(KoradiSavdo), nameof(KoradiSmenalar), nameof(KoradiNasiyalar), nameof(KoradiHisobot),
                     nameof(KoradiOperatorlar), nameof(KoradiAudit), nameof(KoradiSozlamalar), nameof(KoradiEksport), nameof(KoradiAvans),
                     nameof(MuddatiOtganSoni), nameof(MuddatiOtganBor), nameof(MuddatiOtganIzoh) })
            OnPropertyChanged(n);
        OnPropertyChanged(nameof(JoriyRolNomi));
        // Ochiq sahifaga ruxsat olib tashlangan bo'lsa — ruxsati bor birinchi sahifaga o'tamiz.
        if (Kirilgan && !SahifaRuxsatli(Sahifa)) Sahifa = BirinchiSahifa();
    }

    private bool SahifaRuxsatli(int s) => s switch
    {
        SBoshqaruv => KoradiBoshqaruv, SSavdo => KoradiSavdo, SSmenalar => KoradiSmenalar, SNasiyalar => KoradiNasiyalar,
        SHisobot => KoradiHisobot, SOperatorlar => KoradiOperatorlar, SSozlamalar => KoradiSozlamalar, SAudit => KoradiAudit, _ => false,
    };

    [RelayCommand]
    private void SahifaniOch(string indeks) => Sahifa = int.Parse(indeks);

    [RelayCommand]
    private void MenyuniYig() => MenyuYigilgan = !MenyuYigilgan;

    [RelayCommand]
    private void TilniTanla(string kod) => Til.Joriy.Tanla(kod);

    [RelayCommand]
    private void KeyingiTil() => Til.Joriy.Keyingi();

    [RelayCommand]
    private void RejimniAlmashtir()
    {
        QorongiRejim = !QorongiRejim;
        if (Application.Current is { } app)
            app.RequestedThemeVariant = QorongiRejim ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    [RelayCommand]
    private System.Threading.Tasks.Task Chiqish() => ChiqishAsync(null);

    // ================= §8.18 "Sessiya tugadi" — joriy ekran va ochiq dialog ustida =================
    [ObservableProperty] private bool _sessiyaOchiq;
    [ObservableProperty] private string _sessiyaParol = "";
    [ObservableProperty] private string _sessiyaXato = "";
    [ObservableProperty] private bool _sessiyaBand;
    private System.Threading.Tasks.TaskCompletionSource<bool>? _sessiya;
    public bool SessiyaPin => Joriy.Rol == FuelControl.Contracts.Rol.Operator;
    public string SessiyaIzoh => Til.F(SessiyaPin ? "Sessiya_IzohPin" : "Sessiya_IzohParol", Joriy.ToliqIsm, Joriy.Login);
    public string SessiyaYorliq => Til.T(SessiyaPin ? "PinKod" : "Parol").ToUpperInvariant();
    public string SessiyaSarlavha => Til.T("Sessiya_Sarlavha");

    private System.Threading.Tasks.Task<bool> SessiyaniSora()
    {
        if (!Kirilgan) return System.Threading.Tasks.Task.FromResult(false);
        if (_sessiya is { } bor) return bor.Task;
        _sessiya = new System.Threading.Tasks.TaskCompletionSource<bool>();
        SessiyaParol = ""; SessiyaXato = ""; SessiyaBand = false;
        OnPropertyChanged(nameof(SessiyaPin)); OnPropertyChanged(nameof(SessiyaIzoh)); OnPropertyChanged(nameof(SessiyaYorliq));
        SessiyaOchiq = true;
        return _sessiya.Task;
    }

    private void SessiyaniYop(bool natija)
    {
        SessiyaOchiq = false;
        SessiyaParol = "";
        var t = _sessiya;
        _sessiya = null;
        t?.TrySetResult(natija);
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task SessiyaKir()
    {
        if (SessiyaBand) return;
        if (SessiyaPin ? SessiyaParol.Length < 4 : SessiyaParol.Length == 0) { SessiyaXato = Til.T(SessiyaPin ? "Xato_Pin" : "ParolniKiriting"); return; }
        SessiyaBand = true;
        try
        {
            await Malumot.QaytaKir(SessiyaParol);
            SessiyaniYop(true);
        }
        // 403 — to'liq chiqish; 401 (noto'g'ri parol/PIN) va 429 (ko'p xato urinish) — server matni shu oynada, qayta urinish yoki "Chiqish"
        catch (ApiXatosi e) when (e.Status == 403) { SessiyaniYop(false); }
        catch (ApiXatosi e) { SessiyaXato = e.Message; SessiyaParol = ""; }
        finally { SessiyaBand = false; }
    }

    /// <summary>Boshqa odam kirmoqchi yoki parol esda yo'q — to'liq chiqish (kirish oynasi).</summary>
    [RelayCommand] private void SessiyaChiq() => SessiyaniYop(false);

    [RelayCommand]
    private void SessiyaRaqam(string r)
    {
        if (r == "⌫") { if (SessiyaParol.Length > 0) SessiyaParol = SessiyaParol[..^1]; return; }
        if (SessiyaParol.Length < 6) SessiyaParol += r;
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FuelControl.Contracts;
using FuelControl.Contracts.Dto;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelControl.Api.Tests;

/// <summary>
/// To'liq API (vaqtinchalik SQLite fayl, Testing muhiti): admin/admin1234, seeddagi 3 yoqilg'i va 5 aparat. Demo = true bo'lsa muhit Development va
/// dizayndagi demo ma'lumot (smena #28-#42) yoziladi. Yordamchilar: foydalanuvchi yaratish, kirish, smena oqimi.
/// </summary>
public abstract class ApiBaza : IAsyncLifetime
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly string _papka = Path.Combine(Path.GetTempPath(), "fc-test-" + Guid.NewGuid().ToString("N"));
    protected WebApplicationFactory<Program> Ilova = null!;
    /// <summary>Test papkasi (baza fc.db va zaxira shu yerda).</summary>
    protected string Papka => _papka;
    /// <summary>Qo'shimcha sozlamalar/servislar (soxta Telegram handler va h.k.): standart sozlamalardan keyin chaqiriladi.</summary>
    protected virtual void Sozla(IWebHostBuilder b) { }
    /// <summary>ASP.NET Core muhiti: "Testing" (demo yozilmaydi), "Development" yoki "Web" (Seed:DemoMalumot=true bo'lsa demo yoziladi).</summary>
    protected virtual string Muhit => "Testing";
    protected virtual bool DemoSozlamasi => false;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_papka);
        Ilova = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(Muhit);
            b.UseSetting("ConnectionStrings:Baza", $"Data Source={Path.Combine(_papka, "fc.db")}");
            b.UseSetting("Jwt:Kalit", "test-kalit-0123456789abcdef0123456789abcdef");
            b.UseSetting("Seed:AdminParol", "admin1234");
            b.UseSetting("Seed:DemoMalumot", DemoSozlamasi ? "true" : "false");
            b.UseSetting("Zaxira:Papka", Path.Combine(_papka, "zaxira"));
            Sozla(b);
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await Ilova.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_papka, true); } catch (IOException) { }
    }

    protected async Task<HttpClient> Kir(string login, string parol)
    {
        var mijoz = Ilova.CreateClient();
        var javob = await mijoz.PostAsJsonAsync("/auth/login", new LoginSoroviDto(login, parol), Json);
        javob.EnsureSuccessStatusCode();
        mijoz.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await javob.Content.ReadFromJsonAsync<LoginJavobiDto>(Json))!.Token);
        return mijoz;
    }

    protected Task<HttpClient> Admin() => Kir("admin", "admin1234");

    /// <summary>SignalR /hub ulanishi (TestServer, long polling) mijozning JWT tokeni bilan.</summary>
    protected async Task<HubConnection> HubUlan(HttpClient mijoz, Action<HubConnection>? hodisalar = null)
    {
        var server = Ilova.Server;
        var token = mijoz.DefaultRequestHeaders.Authorization!.Parameter!;
        var hub = new HubConnectionBuilder().WithUrl(new Uri(server.BaseAddress, "hub"), o =>
        {
            o.HttpMessageHandlerFactory = _ => server.CreateHandler();
            o.Transports = HttpTransportType.LongPolling;
            o.AccessTokenProvider = () => Task.FromResult<string?>(token);
        }).AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter())).Build();
        hodisalar?.Invoke(hub);
        await hub.StartAsync();
        return hub;
    }

    /// <summary>Yangi foydalanuvchi (standart ruxsatlar yoki berilganlari) va uning nomidan kirgan mijoz. Parol/PIN: 1111.</summary>
    protected async Task<(FoydalanuvchiDto Dto, HttpClient Mijoz)> Yarat(string login, Rol rol = Rol.Operator, Ruxsat[]? ruxsatlar = null)
    {
        var admin = await Admin();
        var javob = await admin.PostAsJsonAsync("/foydalanuvchilar", new FoydalanuvchiYaratishDto(login, login, rol, 4_000_000, "1111"), Json);
        javob.EnsureSuccessStatusCode();
        var dto = (await javob.Content.ReadFromJsonAsync<FoydalanuvchiDto>(Json))!;
        if (ruxsatlar is not null)
            (await admin.PutAsJsonAsync($"/foydalanuvchilar/{dto.Id}/ruxsatlar", new RuxsatlarOrnatishDto(ruxsatlar), Json)).EnsureSuccessStatusCode();
        return (dto, await Kir(login, "1111"));
    }

    protected static async Task<T> Oqi<T>(HttpResponseMessage javob)
    {
        Assert.True(javob.IsSuccessStatusCode, $"{(int)javob.StatusCode}: {await javob.Content.ReadAsStringAsync()}");
        return (await javob.Content.ReadFromJsonAsync<T>(Json))!;
    }

    /// <summary>ProblemDetails.detail (javob matni buferlanadi: bir necha marta o'qish mumkin).</summary>
    protected static async Task<string> Detail(HttpResponseMessage javob) =>
        JsonDocument.Parse(await javob.Content.ReadAsStringAsync()).RootElement.GetProperty("detail").GetString() ?? "";

    protected static async Task Kut(HttpStatusCode kod, HttpResponseMessage javob) =>
        Assert.True(javob.StatusCode == kod, $"kutilgan {(int)kod}, kelgan {(int)javob.StatusCode}: {await javob.Content.ReadAsStringAsync()}");

    protected Task<SmenaDto> Och(HttpClient mijoz, long qaytim = 100_000, long terminal = 50_000, long depozit = 200_000) =>
        mijoz.PostAsJsonAsync("/smenalar/och", new SmenaOchishDto(qaytim, terminal, depozit), Json).ContinueWith(t => Oqi<SmenaDto>(t.Result)).Unwrap();

    protected static Task<AparatDto[]> Aparatlar(HttpClient mijoz) => mijoz.GetFromJsonAsync<AparatDto[]>("/aparatlar", Json)!;

    /// <summary>Hamma aparat uchun: hozirgi TotalLitr + berilgan litr (yoki qo'shilmaydi - 0).</summary>
    protected static AparatKorsatkichDto[] Oxirgi(AparatDto[] aparatlar, params decimal[] litrlar) =>
        aparatlar.Select((a, i) => new AparatKorsatkichDto(a.Id, a.TotalLitr + (i < litrlar.Length ? litrlar[i] : 0))).ToArray();

    protected Task<HttpResponseMessage> Yop(HttpClient mijoz, int smenaId, AparatKorsatkichDto[] korsatkichlar, long terminal = 50_000, long depozit = 200_000, long naqd = 0, string? izoh = null,
        long[]? plastik = null) =>
        mijoz.PostAsJsonAsync($"/smenalar/{smenaId}/yop", new SmenaYopishDto(korsatkichlar, terminal, depozit, naqd, izoh, plastik), Json);
}

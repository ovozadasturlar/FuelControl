using System.Reflection;
using System.Text.Json.Serialization;
using FuelControl.Api.Auth;
using FuelControl.Api.Data;
using FuelControl.Api.Endpointlar;
using FuelControl.Api.Xizmatlar;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // Windows xizmati sifatida joriy papka C:\Windows\System32 bo'ladi: kontent ildizi — dastur papkasi
    // (appsettings*.json va wwwroot shu yerda). Konsol/dev'da odatdagidek joriy papka.
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null,
});
builder.Host.UseWindowsService(o => o.ServiceName = "FuelControl");

// appsettings.Local.json — administrator qo'lda tahrirlaydigan ixtiyoriy fayl (masalan, Cors:Manbalar).
// O'rnatuvchi appsettings.Production.json ni har o'rnatishda qayta yozadi, Local.json ga esa tegmaydi.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

// Fayl jurnali: Log:Papka berilsa (o'rnatuvchi %ProgramData%\FuelControl\logs ni beradi) — kunlik fayllar, 30 kun saqlanadi.
// Windows xizmatida konsol yo'q, shuning uchun xizmat ishga tushmasa sababi shu yerda ko'rinadi.
if (builder.Configuration["Log:Papka"] is { Length: > 0 } logPapka)
{
    builder.Logging.AddSerilog(new LoggerConfiguration()
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
        .MinimumLevel.Override("System", LogEventLevel.Warning)
        .WriteTo.File(Path.Combine(logPapka, "fuelcontrol-.log"),
            rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30, shared: true,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
        .CreateLogger(), dispose: true);
}

builder.Services.AddDbContext<FuelControlDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Baza") ?? "Data Source=fuelcontrol.db"));

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    // Web standarti sonlarni satrdan ham o'qiydi — OpenAPI'da "number | string" bo'lib chiqadi. Faqat son.
    o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Kalit yo'q bo'lsa — har so'rovda emas, ishga tushishdayoq to'xtaymiz.
var jwtKalit = TokenXizmati.Kalit(builder.Configuration);
if (jwtKalit.KeySize < 256) throw new InvalidOperationException("Jwt:Kalit kamida 32 belgi bo'lishi kerak.");

builder.Services.AddSingleton<TokenXizmati>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<FoydalanuvchiKeshi>();
builder.Services.AddSingleton<UlanishlarXaritasi>();
builder.Services.AddSingleton<ZaxiraXizmati>();
builder.Services.AddSingleton<ISirShifrlovchi, SirShifrlovchi>();
builder.Services.AddSingleton<TelegramYuboruvchi>();
builder.Services.AddSingleton<ZaxiraTelegram>();
builder.Services.AddSingleton<ZaxiraNavbati>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ZaxiraNavbati>());
builder.Services.AddHostedService<KunlikIshlarFonXizmati>();
// Bot tokeni so'rov manzilida: shu mijozning standart HTTP logi (manzilni yozadi) o'chiriladi.
builder.Services.AddHttpClient(TelegramYuboruvchi.MijozNomi, c => { c.BaseAddress = new Uri("https://api.telegram.org/"); c.Timeout = TimeSpan.FromMinutes(6); })
    .RemoveAllLoggers();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = TokenXizmati.Emitent,
        ValidAudience = TokenXizmati.Emitent,
        IssuerSigningKey = jwtKalit,
        ClockSkew = TimeSpan.FromMinutes(1),
        RoleClaimType = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role",
    };
    // SignalR (WebSocket) tokenni so'rov satridan yuboradi.
    o.Events = new JwtBearerEvents
    {
        OnMessageReceived = ctx =>
        {
            var token = ctx.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hub"))
                ctx.Token = token;
            return Task.CompletedTask;
        },
        OnTokenValidated = FoydalanuvchiKeshi.TokenniTekshir,
    };
});
builder.Services.AddAuthorization();

// CORS: Cors:Manbalar (string[]) — faqat shu aniq manbalar (AllowAnyOrigin emas). Bo'sh bo'lsa CORS yoqilmaydi.
// Web alohida domenda (masalan, Cloudflare Pages) turganda API va /hub ga brauzer shu manbadan murojaat qiladi.
var corsManbalar = (builder.Configuration.GetSection("Cors:Manbalar").Get<string[]>() ?? [])
    .Select(x => x.Trim().TrimEnd('/')).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
if (corsManbalar.Length > 0)
    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
        .WithOrigins(corsManbalar).AllowAnyHeader().AllowAnyMethod().AllowCredentials()
        .SetPreflightMaxAge(TimeSpan.FromHours(1))));

var app = builder.Build();

app.UseExceptionHandler(h => h.Run(async ctx =>
{
    var xato = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, xabar) = xato switch
    {
        BiznesXatosi b => (b.Status, b.Message),
        BadHttpRequestException bad => (400, "So'rov noto'g'ri: " + bad.Message),
        ArgumentException a => (400, a.Message),
        InvalidOperationException i => (409, i.Message),
        _ => (500, "Serverda kutilmagan xato yuz berdi."),
    };
    if (status == 500) app.Logger.LogError(xato, "Kutilmagan xato");
    ctx.Response.StatusCode = status;
    await Results.Problem(statusCode: status, title: status == 500 ? "Server xatosi" : "So'rov bajarilmadi", detail: xabar)
        .ExecuteAsync(ctx);
}));
app.UseStatusCodePages();

// Autentifikatsiya va /hub dan oldin: preflight (OPTIONS) shu yerda 204 bilan tugaydi.
if (corsManbalar.Length > 0) app.UseCors();

// PWA (src/frontend/web/FuelControl.Web build → wwwroot). Hash marshrutlash — SPA fallback kerak emas.
var turlar = new FileExtensionContentTypeProvider();
turlar.Mappings[".webmanifest"] = "application/manifest+json";
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = turlar });

app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi().AllowAnonymous();
app.MapScalarApiReference().AllowAnonymous();

app.Ulash();
app.YoqilgiAparatUlash();
app.SmenaUlash();
app.NasiyaUlash();
app.XarajatUlash();
app.HisobotUlash();
app.ZaxiraUlash();
app.MapHub<SotuvHub>("/hub").RequireAuthorization();

try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<FuelControlDbContext>();
    db.Database.Migrate();
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    await BazaKafolati.Tikla(db);
    // Production (mijoz o'rnatuvchisi): faqat admin; yoqilg'i va aparatlarni admin Sozlamalar'da o'zi kiritadi.
    await SeedXizmati.Boshlash(db, builder.Configuration, app.Logger, yoqilgiVaAparatlar: !app.Environment.IsProduction());
    // Demo ma'lumot (dizayn namunasi) faqat dev/web muhitida va faqat bo'sh bazada: Seed:DemoMalumot=true.
    if ((app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Web")) && builder.Configuration.GetValue<bool>("Seed:DemoMalumot"))
        await SeedXizmati.DemoMalumot(db, app.Logger);
    await MaoshYozuvchi.Yoz(db);
}
catch (Exception e)
{
    // Xizmat shu yerda to'xtaydi: sababi jurnalda qolsin (Seed:AdminParol yo'qligi, baza fayliga ruxsat yo'qligi va h.k.).
    app.Logger.LogCritical(e, "FuelControl API ishga tushmadi: bazani yoki boshlang'ich ma'lumotni tayyorlashda xato.");
    throw;
}

app.Logger.LogInformation("FuelControl API tayyor. Versiya {Versiya}, muhit {Muhit}, kontent ildizi {Ildiz}",
    typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?",
    app.Environment.EnvironmentName, app.Environment.ContentRootPath);

app.Run();

public partial class Program;

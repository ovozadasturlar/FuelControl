using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using FuelControl.Api.Auth;
using FuelControl.Api.Data;
using FuelControl.Contracts;
using FuelControl.Contracts.Dto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FuelControl.Api.Tests;

/// <summary>POST /auth/yangila (docs 8.18): faol foydalanuvchi sessiyasini uzaytirish. Yangi token, 12 soat, ruxsat bazadan, nofaol/bloklangan - 401, audit yozilmaydi.</summary>
public sealed class AuthYangilaApiTests : ApiBaza
{
    private const string JwtKalit = "test-kalit-0123456789abcdef0123456789abcdef";

    private HttpClient Mijoz(string token)
    {
        var m = Ilova.CreateClient();
        m.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return m;
    }

    private async Task<LoginJavobiDto> Login(string login, string parol) =>
        await Oqi<LoginJavobiDto>(await Ilova.CreateClient().PostAsJsonAsync("/auth/login", new LoginSoroviDto(login, parol), Json));

    private Task<HttpResponseMessage> Yangila(string token) => Mijoz(token).PostAsync("/auth/yangila", null);

    [Fact]
    public async Task Yangila_YangiToken_12Soat_ParolsizVaFoydalanuvchiLoginDagidek()
    {
        var (dto, _) = await Yarat("ali");
        var eski = await Login("ali", "1111");
        await Task.Delay(1100);                                                           // JWT vaqti soniyalarda: yangi token matni boshqacha bo'lishi uchun

        var yangi = await Oqi<LoginJavobiDto>(await Yangila(eski.Token));

        Assert.NotEqual(eski.Token, yangi.Token);
        Assert.True(yangi.TokenMuddati > eski.TokenMuddati);
        Assert.InRange((yangi.TokenMuddati - DateTime.UtcNow).TotalMinutes, 12 * 60 - 2, 12 * 60 + 1);              // muddat o'zgarmagan: 12 soat
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(yangi.Token);
        Assert.InRange(Math.Abs((jwt.ValidTo - yangi.TokenMuddati).TotalSeconds), 0, 2);
        Assert.Equal(dto.Id.ToString(), jwt.Subject);
        Assert.Equal((dto.Id, "ali"), (yangi.Foydalanuvchi.Id, yangi.Foydalanuvchi.Login));
        Assert.Equal(eski.Foydalanuvchi.Ruxsatlar.Order(), yangi.Foydalanuvchi.Ruxsatlar.Order());
        var men = await Oqi<FoydalanuvchiDto>(await Mijoz(yangi.Token).GetAsync("/me"));                             // yangi token ishlaydi
        Assert.Equal(dto.Id, men.Id);
    }

    [Fact]
    public async Task EskiToken_Yangilangandan_Keyin_MuddatiTugaguncha_Ishlayveradi()
    {
        await Yarat("ali");
        var eski = await Login("ali", "1111");
        await Task.Delay(1100);
        await Oqi<LoginJavobiDto>(await Yangila(eski.Token));

        await Kut(HttpStatusCode.OK, await Mijoz(eski.Token).GetAsync("/me"));
        await Kut(HttpStatusCode.OK, await Yangila(eski.Token));                          // eskisi bilan yana yangilash ham mumkin
    }

    [Fact]
    public async Task Ruxsat_VaRol_Bazadan_OlinadiToken_ChiqarilgandanKeyinOzgargan()
    {
        var (dto, _) = await Yarat("ali");
        var eski = await Login("ali", "1111");
        var admin = await Admin();
        var yangiRuxsat = new[] { Ruxsat.Savdo, Ruxsat.Hisobotlar };
        await Kut(HttpStatusCode.OK, await admin.PutAsJsonAsync($"/foydalanuvchilar/{dto.Id}", new FoydalanuvchiTahrirlashDto("Ali", Rol.Boshliq, true, 0), Json));   // rol o'zgarsa standart ruxsatlar qaytadi
        await Kut(HttpStatusCode.OK, await admin.PutAsJsonAsync($"/foydalanuvchilar/{dto.Id}/ruxsatlar", new RuxsatlarOrnatishDto(yangiRuxsat), Json));

        var yangi = await Oqi<LoginJavobiDto>(await Yangila(eski.Token));

        Assert.Equal(yangiRuxsat.Order(), yangi.Foydalanuvchi.Ruxsatlar.Order());                                      // bazadagi joriy holat
        Assert.Equal(Rol.Boshliq, yangi.Foydalanuvchi.Rol);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(yangi.Token);
        Assert.Equal(yangiRuxsat.Select(r => r.ToString()).Order(), jwt.Claims.Where(c => c.Type == "ruxsat").Select(c => c.Value).Order());
        Assert.Contains(jwt.Claims, c => (c.Type is "role" or ClaimTypes.Role) && c.Value == "Boshliq");
    }

    [Fact]
    public async Task NofaolFoydalanuvchi_401()
    {
        var (dto, _) = await Yarat("ali");
        var token = (await Login("ali", "1111")).Token;
        var admin = await Admin();
        await Kut(HttpStatusCode.OK, await admin.PutAsJsonAsync($"/foydalanuvchilar/{dto.Id}", new FoydalanuvchiTahrirlashDto("Ali", Rol.Operator, false, 0), Json));

        await Kut(HttpStatusCode.Unauthorized, await Yangila(token));
    }

    [Fact]
    public async Task BloklanganFoydalanuvchi_401_TokenIshlaydiLekinYangilanmaydi()
    {
        var (dto, _) = await Yarat("ali");
        var token = (await Login("ali", "1111")).Token;
        using (var scope = Ilova.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FuelControlDbContext>();
            var f = await db.Foydalanuvchilar.SingleAsync(x => x.Id == dto.Id);
            f.BlokGacha = DateTime.UtcNow.AddMinutes(10);
            await db.SaveChangesAsync();
        }

        var javob = await Yangila(token);

        await Kut(HttpStatusCode.Unauthorized, javob);
        Assert.Contains("bloklangan", await Detail(javob));
    }

    [Fact]
    public async Task TokenSiz_YaroqsizYokiMuddatiTugagan_401()
    {
        var (dto, _) = await Yarat("ali");
        await Kut(HttpStatusCode.Unauthorized, await Ilova.CreateClient().PostAsync("/auth/yangila", null));
        await Kut(HttpStatusCode.Unauthorized, await Yangila("bu.token.emas"));

        // Muddati tugagan token bilan yangilab bo'lmaydi (klient parol/PIN so'raydi): imzo to'g'ri, lekin 5 daqiqa oldin tugagan.
        var kalit = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(JwtKalit));
        var otgan = new JwtSecurityToken("FuelControl", "FuelControl", [new Claim("sub", dto.Id.ToString())],
            notBefore: DateTime.UtcNow.AddHours(-13), expires: DateTime.UtcNow.AddMinutes(-5), signingCredentials: new SigningCredentials(kalit, SecurityAlgorithms.HmacSha256));
        await Kut(HttpStatusCode.Unauthorized, await Yangila(new JwtSecurityTokenHandler().WriteToken(otgan)));
    }

    [Fact]
    public async Task Yangilash_AuditVaHisoblagichlarniYozmaydi()
    {
        var (dto, _) = await Yarat("ali");
        var eski = await Login("ali", "1111");
        var admin = await Admin();
        var auditAvval = (await admin.GetFromJsonAsync<AuditYozuviDto[]>("/audit?limit=1000", Json))!.Length;

        for (var i = 0; i < 3; i++) await Oqi<LoginJavobiDto>(await Yangila(eski.Token));

        Assert.Equal(auditAvval, (await admin.GetFromJsonAsync<AuditYozuviDto[]>("/audit?limit=1000", Json))!.Length);
        using var scope = Ilova.Services.CreateScope();
        var f = await scope.ServiceProvider.GetRequiredService<FuelControlDbContext>().Foydalanuvchilar.AsNoTracking().SingleAsync(x => x.Id == dto.Id);
        Assert.Equal((0, (DateTime?)null), (f.XatoUrinishlar, f.BlokGacha));
    }
}

/// <summary>Avtorizatsiya:Muddat - token muddati sozlanadi (klientlar qisqa token bilan sinashi uchun); login va /auth/yangila bir xil muddat beradi.</summary>
public sealed class TokenMuddatiApiTests : ApiBaza
{
    protected override void Sozla(Microsoft.AspNetCore.Hosting.IWebHostBuilder b) => b.UseSetting("Avtorizatsiya:Muddat", "00:03:00");

    [Fact]
    public async Task SozlanganMuddat_LoginVaYangila_BirXil_3Daqiqa()
    {
        var (_, _) = await Yarat("ali");
        var login = await Oqi<LoginJavobiDto>(await Ilova.CreateClient().PostAsJsonAsync("/auth/login", new LoginSoroviDto("ali", "1111"), Json));
        Assert.InRange((login.TokenMuddati - DateTime.UtcNow).TotalSeconds, 170, 181);
        Assert.InRange(Math.Abs((new JwtSecurityTokenHandler().ReadJwtToken(login.Token).ValidTo - login.TokenMuddati).TotalSeconds), 0, 2);

        var m = Ilova.CreateClient();
        m.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        var yangi = await Oqi<LoginJavobiDto>(await m.PostAsync("/auth/yangila", null));
        Assert.InRange((yangi.TokenMuddati - DateTime.UtcNow).TotalSeconds, 170, 181);                              // yangilash ham sozlangan muddatni beradi
    }
}

public sealed class TokenMuddatiQoidasiTests
{
    private static TokenXizmati Xizmat(string? muddat) => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Avtorizatsiya:Muddat"] = muddat, ["Jwt:Kalit"] = "test-kalit-0123456789abcdef0123456789abcdef" }).Build());

    [Theory]
    [InlineData(null, 12 * 60)]
    [InlineData("", 12 * 60)]
    [InlineData("12:00:00", 12 * 60)]
    [InlineData("00:03:00", 3)]
    [InlineData("00:02:00", 2)]
    [InlineData("1.00:00:00", 24 * 60)]
    [InlineData("abc", 12 * 60)]
    [InlineData("00:00:00", 12 * 60)]
    [InlineData("-00:05:00", 12 * 60)]
    [InlineData("3", 3 * 24 * 60)]                                                    // "3" - TimeSpan uchun 3 kun (kutilgan, qisqa token uchun "00:03:00" yozing)
    public void Muddat_Sozlamadan_YaroqsizYoki0YokiManfiyBolsa_12Soat(string? sozlama, double kutilganDaqiqa) =>
        Assert.Equal(kutilganDaqiqa, Xizmat(sozlama).Muddat.TotalMinutes);

    [Fact]
    public void Yarat_MuddatiSozlamaBoyicha()
    {
        var f = new FuelControl.Core.Modellar.Foydalanuvchi { Id = 7, ToliqIsm = "Ali", Login = "ali", Rol = FuelControl.Contracts.Rol.Operator };
        var (token, muddati) = Xizmat("00:02:00").Yarat(f);
        Assert.InRange((muddati - DateTime.UtcNow).TotalSeconds, 110, 121);
        Assert.InRange(Math.Abs((new JwtSecurityTokenHandler().ReadJwtToken(token).ValidTo - muddati).TotalSeconds), 0, 2);
    }
}

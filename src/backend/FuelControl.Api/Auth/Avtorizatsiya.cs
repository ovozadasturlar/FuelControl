using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FuelControl.Contracts;
using FuelControl.Core.Modellar;
using Microsoft.IdentityModel.Tokens;

namespace FuelControl.Api.Auth;

/// <summary>Biznes qoidasi buzilganda — ProblemDetails (o'zbekcha xabar) bilan qaytariladi.</summary>
public sealed class BiznesXatosi(string xabar, int status = 400) : Exception(xabar)
{
    public int Status { get; } = status;
}

public static class FoydalanuvchiKengaytmalari
{
    public static int FoydalanuvchiId(this ClaimsPrincipal u) =>
        int.Parse(u.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? u.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static string Ism(this ClaimsPrincipal u) => u.FindFirstValue("ism") ?? "?";

    public static bool Bor(this ClaimsPrincipal u, Ruxsat r) =>
        u.Claims.Any(c => c.Type == "ruxsat" && c.Value == r.ToString());
}

public sealed class RuxsatTalabFilter(Ruxsat kerak) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        if (!ctx.HttpContext.User.Bor(kerak))
            return Results.Problem(statusCode: 403, title: "Ruxsat yo'q", detail: $"Bu amal uchun \"{kerak}\" ruxsati kerak.");
        return await next(ctx);
    }
}

/// <summary>Berilgan ruxsatlardan kamida bittasi bo'lsa o'tkazadi.</summary>
public sealed class RuxsatdanBiriTalabFilter(Ruxsat[] kerak) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        if (!kerak.Any(ctx.HttpContext.User.Bor))
            return Results.Problem(statusCode: 403, title: "Ruxsat yo'q",
                detail: $"Bu amal uchun {string.Join(" yoki ", kerak.Select(r => $"\"{r}\""))} ruxsati kerak.");
        return await next(ctx);
    }
}

public static class RuxsatFilterKengaytmasi
{
    public static RouteHandlerBuilder RuxsatKerak(this RouteHandlerBuilder b, Ruxsat r) =>
        b.AddEndpointFilter(new RuxsatTalabFilter(r));

    public static RouteHandlerBuilder RuxsatdanBiriKerak(this RouteHandlerBuilder b, params Ruxsat[] r) =>
        b.AddEndpointFilter(new RuxsatdanBiriTalabFilter(r));
}

public sealed class TokenXizmati(IConfiguration konfiguratsiya)
{
    public const string Emitent = "FuelControl";
    public static readonly TimeSpan StandartMuddat = TimeSpan.FromHours(12);

    /// <summary>
    /// Token muddati: "Avtorizatsiya:Muddat" (TimeSpan, masalan "00:03:00" - klientlar qisqa token bilan sinashi uchun); bo'sh, yaroqsiz yoki
    /// 0 va undan kichik bo'lsa - 12 soat. Login va /auth/yangila bir xil muddat beradi.
    /// </summary>
    public TimeSpan Muddat =>
        TimeSpan.TryParse(konfiguratsiya["Avtorizatsiya:Muddat"], System.Globalization.CultureInfo.InvariantCulture, out var m) && m > TimeSpan.Zero ? m : StandartMuddat;

    public static SymmetricSecurityKey Kalit(IConfiguration k) =>
        new(Encoding.UTF8.GetBytes(k["Jwt:Kalit"] ?? throw new InvalidOperationException("Jwt:Kalit sozlanmagan.")));

    public (string Token, DateTime Muddati) Yarat(Foydalanuvchi f)
    {
        var muddati = DateTime.UtcNow.Add(Muddat);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, f.Id.ToString()),
            new("ism", f.ToliqIsm),
            new(ClaimTypes.Role, f.Rol.ToString()),
        };
        claims.AddRange(f.Ruxsatlar.Select(r => new Claim("ruxsat", r.ToString())));

        var token = new JwtSecurityToken(Emitent, Emitent, claims, expires: muddati,
            signingCredentials: new SigningCredentials(Kalit(konfiguratsiya), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), muddati);
    }
}

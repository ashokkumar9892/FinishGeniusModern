using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FinishGenius.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace FinishGenius.Api.Infrastructure;

public class JwtOptions
{
    public string Issuer { get; set; } = "FinishGenius";
    public string Audience { get; set; } = "FinishGenius";
    public string Key { get; set; } = "";
    public int ExpiryHours { get; set; } = 12;
    public int RememberMeDays { get; set; } = 30;

    public SymmetricSecurityKey SigningKey()
    {
        if (Key.Length < 32)
            throw new InvalidOperationException("Jwt:Key must be at least 32 characters. Set it in appsettings.Local.json.");
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key));
    }
}

public class TokenService(Microsoft.Extensions.Options.IOptions<JwtOptions> options)
{
    private readonly JwtOptions _o = options.Value;

    public (string token, DateTime expires) Create(User user, bool rememberMe)
    {
        var expires = rememberMe ? DateTime.UtcNow.AddDays(_o.RememberMeDays) : DateTime.UtcNow.AddHours(_o.ExpiryHours);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email),
            new("groupId", user.GroupId.ToString()),
        };
        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r.Role)));
        var token = new JwtSecurityToken(_o.Issuer, _o.Audience, claims, expires: expires,
            signingCredentials: new SigningCredentials(_o.SigningKey(), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

public static class Passwords
{
    private static readonly PasswordHasher<User> Hasher = new();

    public static string Hash(string password) => Hasher.HashPassword(null!, password);

    /// <summary>Accounts imported from the legacy app keep their bcrypt hashes until the next successful login.</summary>
    public static bool IsLegacyBcrypt(string? hash) => hash != null && hash.StartsWith("$2");

    public static bool Verify(string hash, string password)
    {
        if (string.IsNullOrEmpty(hash)) return false;
        if (IsLegacyBcrypt(hash))
        {
            try { return BCrypt.Net.BCrypt.Verify(password, hash); }
            catch (Exception) { return false; }
        }
        try
        {
            return Hasher.VerifyHashedPassword(null!, hash, password) != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static void Validate(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            throw ApiException.Bad("Password must be at least 8 characters.");
    }
}

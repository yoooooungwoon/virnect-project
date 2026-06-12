using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace VirnectMonitor.Auth;

public sealed class TokenService
{
    private readonly AuthOptions _options;

    public TokenService(IOptions<AuthOptions> options)
    {
        _options = options.Value;
    }

    public string CreateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    public string HashToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Token is required.", nameof(token));
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.ServerSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string MaskToken(string token)
    {
        if (token.Length <= 10)
        {
            return "***";
        }

        return $"{token[..4]}...{token[^4..]}";
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}


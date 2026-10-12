using System.Security.Cryptography;
using System.Text;
using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common.Options;
using Microsoft.Extensions.Options;

namespace Deutchezz.Infrastructure.Services;

/// <summary>
/// 6-digit code generator/verifier. The code is stored only as an HMAC-SHA256 keyed with
/// <c>Security:OtpHmacKey</c> and compared in constant time, so a database leak cannot recover
/// the codes in use.
/// </summary>
public sealed class HmacOtpCodeService : IOtpCodeService
{
    private readonly byte[] _key;

    public HmacOtpCodeService(IOptions<SecurityOptions> options)
    {
        _key = Encoding.UTF8.GetBytes(options.Value.OtpHmacKey);
    }

    public string Generate() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public string Hash(string code)
    {
        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(mac);
    }

    public bool Verify(string codeHash, string code)
    {
        var computed = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(code));

        byte[] stored;
        try
        {
            stored = Convert.FromHexString(codeHash);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(computed, stored);
    }
}

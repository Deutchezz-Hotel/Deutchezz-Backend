namespace Deutchezz.Application.Abstractions;

/// <summary>
/// Generates and verifies the 6-digit email codes used for account verification and password
/// reset. Codes are never stored in plain text: only a keyed hash is persisted.
/// </summary>
public interface IOtpCodeService
{
    /// <summary>Generates a cryptographically random, zero-padded 6-digit code.</summary>
    string Generate();

    /// <summary>Returns the keyed hash of <paramref name="code"/> for storage.</summary>
    string Hash(string code);

    /// <summary>Constant-time comparison of <paramref name="code"/> against a stored hash.</summary>
    bool Verify(string codeHash, string code);
}

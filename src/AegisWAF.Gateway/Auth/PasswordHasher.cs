using System.Security.Cryptography;

namespace AegisWAF.Gateway.Auth;

/// <summary>PBKDF2-HMAC-SHA512 password hashing (OWASP 2023 minimum: 210,000 iterations).</summary>
internal static class PasswordHasher
{
    private const string Scheme = "pbkdf2-sha512";
    private const int Iterations = 210_000, SaltSize = 16, HashSize = 32;
    public const int MaxLength = 128;
    public const int MinLength = 12;

    // Verified against when a user does not exist so unknown-user and wrong-password take similar time.
    private static readonly Lazy<string> DummyHash = new(() => Hash("dummy-password-for-timing-equalisation"));

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA512, HashSize);
        return $"{Scheme}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme || !int.TryParse(parts[1], out var iterations) || iterations < 1) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA512, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }

    public static void BurnTime(string password) => Verify(password, DummyHash.Value);

    /// <summary>Production password baseline: length plus multiple character classes.</summary>
    public static string? Validate(string? password, string? username = null)
    {
        if (string.IsNullOrEmpty(password)) return "Password must not be empty";
        if (password.Length < MinLength) return $"Password must be at least {MinLength} characters";
        if (password.Length > MaxLength) return $"Password must be at most {MaxLength} characters";
        if (username is not null && password.Contains(username, StringComparison.OrdinalIgnoreCase)) return "Password must not contain the username";
        if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit) || !password.Any(c => !char.IsLetterOrDigit(c)))
            return "Password must include upper, lower, number and symbol characters";
        return null;
    }
}

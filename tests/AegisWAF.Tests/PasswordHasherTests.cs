using AegisWAF.Gateway.Auth;

namespace AegisWAF.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_then_verify_succeeds_and_wrong_password_fails()
    {
        var h = PasswordHasher.Hash("correct horse battery staple");
        Assert.True(PasswordHasher.Verify("correct horse battery staple", h));
        Assert.False(PasswordHasher.Verify("correct horse battery stapl3", h));
    }

    [Fact]
    public void Hashes_are_salted_and_never_contain_the_password()
    {
        var a = PasswordHasher.Hash("same-password-123");
        var b = PasswordHasher.Hash("same-password-123");
        Assert.NotEqual(a, b);
        Assert.DoesNotContain("same-password-123", a);
        Assert.StartsWith("pbkdf2-sha512$210000$", a);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2-sha512$0$AA==$AA==")]
    [InlineData("pbkdf2-sha512$1000$!!!$!!!")]
    [InlineData("md5$1$AA==$AA==")]
    public void Malformed_stored_hashes_are_rejected_without_throwing(string stored) => Assert.False(PasswordHasher.Verify("whatever-password", stored));

    [Theory]
    [InlineData("a")]
    [InlineData("1234")]
    [InlineData("password")]
    [InlineData("aaaaaaaaaaaaaaaa")]
    [InlineData("admin")]
    [InlineData("my-alice-password-1")]
    [InlineData("   ")]
    [InlineData("รหัสผ่านภาษาไทย")]
    public void Production_password_policy_requires_length_and_character_classes(string pw) => Assert.NotNull(PasswordHasher.Validate(pw));

    [Fact]
    public void Weak_passwords_still_hash_and_verify_normally()
    {
        var h = PasswordHasher.Hash("a");
        Assert.True(PasswordHasher.Verify("a", h));
        Assert.False(PasswordHasher.Verify("b", h));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_password_is_rejected_because_the_login_endpoint_could_never_accept_it(string? pw) => Assert.NotNull(PasswordHasher.Validate(pw));

    [Fact]
    public void Overlong_passwords_are_rejected_to_bound_hashing_cost()
    {
        Assert.NotNull(PasswordHasher.Validate(new string('x', 128)));
        Assert.NotNull(PasswordHasher.Validate(new string('x', 129)));
    }
}

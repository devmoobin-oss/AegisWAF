using AegisWAF.Gateway;
using Microsoft.Extensions.Configuration;

namespace AegisWAF.Tests;

/// <summary>
/// Deterministic robustness checks for the inspection boundary. These are intentionally
/// lightweight property-style tests: malformed Unicode and hostile sizes must never
/// crash the request path or make regex inspection unbounded.
/// </summary>
public sealed class FuzzRegressionTests
{
    private static readonly char[] Alphabet =
    [
        'a', 'Z', '0', '%', '&', '=', '<', '>', '\'', '"', '/', '\\',
        '\0', '\u0001', '\u001f', '\u007f', '\u00a0', '\u200b', '\ufeff',
        '\uff1c', '\uff1e', '\uff0f', '\uff1a', '\ud800', '\udfff'
    ];

    private static string Input(Random random, int maxLength)
    {
        var length = random.Next(maxLength + 1);
        var chars = new char[length];
        for (var i = 0; i < chars.Length; i++) chars[i] = Alphabet[random.Next(Alphabet.Length)];
        return new string(chars);
    }

    private static SecurityRules Rules() => new(new ConfigurationBuilder().Build());

    [Fact]
    public void Normalizer_handles_malformed_unicode_and_never_exceeds_its_work_cap()
    {
        var random = new Random(0xAE915);
        for (var i = 0; i < 1000; i++)
        {
            var variants = Normalizer.Variants(Input(random, i % 2 == 0 ? 512 : 70_000));
            Assert.NotEmpty(variants);
            Assert.All(variants, value => Assert.InRange(value.Length, 0, 65_536));
        }
    }

    [Fact]
    public void Rule_engine_handles_hostile_inputs_without_throwing()
    {
        var rules = Rules();
        var random = new Random(0xAE916);
        for (var i = 0; i < 1000; i++)
        {
            var value = Input(random, i % 3 == 0 ? 10_000 : 1_024);
            var exception = Record.Exception(() => rules.Match(value));
            Assert.Null(exception);
        }
    }

    [Fact]
    public void Response_inspector_handles_large_and_malformed_text_without_throwing()
    {
        var inspector = new ResponseInspector();
        var random = new Random(0xAE917);
        for (var i = 0; i < 200; i++)
        {
            var body = Input(random, i % 2 == 0 ? 2_048 : 300_000);
            var exception = Record.Exception(() => inspector.Inspect(body));
            Assert.Null(exception);
        }

        Assert.True(ResponseInspector.IsInspectable("text/plain; charset=utf-8"));
        Assert.True(ResponseInspector.IsInspectable("application/problem+json"));
        Assert.False(ResponseInspector.IsInspectable("application/octet-stream"));
    }
}

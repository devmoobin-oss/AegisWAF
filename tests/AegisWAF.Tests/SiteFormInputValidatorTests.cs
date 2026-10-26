using AegisWAF.Client;

namespace AegisWAF.Tests;

public sealed class SiteFormInputValidatorTests
{
    [Fact]
    public void Explains_that_a_hostname_must_not_contain_a_port()
    {
        var error = SiteFormInputValidator.Validate(
            "affiliateiq",
            ["localhost:4004"],
            "/",
            "http://localhost:4000/");

        Assert.Contains("Hostname ไม่ต้องใส่ port", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Explains_the_site_id_format_before_sending_the_request()
    {
        var error = SiteFormInputValidator.Validate(
            "Affiliate IQ",
            ["affiliateiq.localhost"],
            "/",
            "http://localhost:4000/");

        Assert.Contains("รหัส Site", error);
        Assert.Contains("ขีดกลาง", error);
    }

    [Fact]
    public void Explains_that_an_upstream_needs_an_http_scheme()
    {
        var error = SiteFormInputValidator.Validate(
            "affiliateiq",
            ["affiliateiq.localhost"],
            "/",
            "localhost:4000");

        Assert.Contains("http:// หรือ https://", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Accepts_a_unique_hostname_and_a_local_http_upstream_for_development()
    {
        var error = SiteFormInputValidator.Validate(
            "affiliateiq",
            ["affiliateiq.localhost"],
            "/",
            "http://localhost:4000/");

        Assert.Null(error);
    }
}

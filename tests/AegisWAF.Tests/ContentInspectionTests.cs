using System.Text;
using AegisWAF.Gateway;

namespace AegisWAF.Tests;

public sealed class ContentInspectionTests
{
    [Fact]
    public void Text_like_opaque_bytes_are_classified_as_inspectable()
    {
        Assert.True(ContentInspection.IsTextLike(Encoding.UTF8.GetBytes("root:x:0:0:root:/root:/bin/bash")));
    }

    [Fact]
    public void Nul_control_bytes_are_classified_as_binary()
    {
        Assert.False(ContentInspection.IsTextLike("PK\0\0\u0001\u0002"u8));
    }

    [Fact]
    public void Invalid_utf8_is_classified_as_binary_without_throwing()
    {
        Assert.False(ContentInspection.IsTextLike(new byte[] { 0xff, 0xfe, 0xfd }));
    }
}

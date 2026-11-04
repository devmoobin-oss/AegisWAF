using System.Text;

namespace AegisWAF.Tests;

public sealed class HandoffLatestStateContractTests
{
    [Fact]
    public void Handoff_ends_with_the_latest_authoritative_pointer()
    {
        var root = FindRepoRoot();
        var handoff = File.ReadAllText(Path.Combine(root, "handoff.md"), Encoding.UTF8);
        const string marker = "### Current handoff pointer — 2026-11-04 (after Module 220)";
        var markerIndex = handoff.LastIndexOf(marker, StringComparison.Ordinal);

        Assert.True(markerIndex >= 0, "handoff.md must end with the Module 216 current pointer.");
        var pointer = handoff[markerIndex..].TrimEnd();
        Assert.True(handoff.TrimEnd().EndsWith(pointer, StringComparison.Ordinal), "The latest handoff pointer must be the final non-whitespace content.");
        Assert.Equal(markerIndex, handoff.LastIndexOf(marker, StringComparison.Ordinal));
        Assert.Equal(markerIndex, handoff.LastIndexOf("### Current handoff pointer", StringComparison.Ordinal));
        Assert.Contains("Module 154 = 100%", pointer);
        Assert.Contains("Module 153 = 100%", pointer);
        Assert.Contains("Module 152 = 100%", pointer);
        Assert.Contains("Module 151 = 100%", pointer);
        Assert.Contains("Module 150 = 100%", pointer);
        Assert.Contains("Module 149 = 100%", pointer);
        Assert.Contains("Module 148 = 100%", pointer);
        Assert.Contains("Module 147 = 100%", pointer);
        Assert.Contains("Module 146 = 100%", pointer);
        Assert.Contains("Module 145 = 100%", pointer);
        Assert.Contains("Module 141 = 100%", pointer);
        Assert.Contains("Module 140 = 100%", pointer);
        Assert.Contains("Module 139 = 100%", pointer);
        Assert.Contains("Module 138 = 100%", pointer);
        Assert.Contains("Module 137 = 100%", pointer);
        Assert.Contains("Module 134 = 100%", pointer);
        Assert.Contains("Module 133 = 100%", pointer);
        Assert.Contains("Module 132 = 100%", pointer);
        Assert.Contains("Module 131 = 100%", pointer);
        Assert.Contains("Module 130 = 100%", pointer);
        Assert.Contains("Module 129 = 100%", pointer);
        Assert.Contains("Module 128 = 100%", pointer);
        Assert.Contains("Module 127 = 100%", pointer);
        Assert.Contains("Module 126 = 100%", pointer);
        Assert.Contains("Module 117 = 100%", pointer);
        Assert.Contains("Module 116 = 100%", pointer);
        Assert.Contains("Module 115 = 100%", pointer);
        Assert.Contains("Module 114 = 100%", pointer);
        Assert.Contains("Module 113 = 100%", pointer);
        Assert.Contains("Module 112 = 100%", pointer);
        Assert.Contains("Module 111 = 100%", pointer);
        Assert.Contains("Module 110 = 100%", pointer);
        Assert.Contains("Module 109 = 100%", pointer);
        Assert.Contains("Module 108 = 100%", pointer);
        Assert.Contains("Module 107 = 100%", pointer);
        Assert.Contains("Module 106 = 100%", pointer);
        Assert.Contains("Module 105 = 100%", pointer);
        Assert.Contains("Module 104 = 100%", pointer);
        Assert.Contains("Module 103 = 100%", pointer);
        Assert.Contains("Module 102 = 100%", pointer);
        Assert.Contains("Module 101 = 100%", pointer);
        Assert.Contains("Module 100 = 100%", pointer);
        Assert.Contains("Module 99 = 100%", pointer);
        Assert.Contains("Module 98 = 100%", pointer);
        Assert.Contains("Module 97 = 100%", pointer);
        Assert.Contains("Module 95 = 100%", pointer);
        Assert.Contains("Module 94 = 100%", pointer);
        Assert.Contains("Module 93 = 100%", pointer);
        Assert.Contains("Module 92 = 100%", pointer);
        Assert.Contains("Module 91 = 100%", pointer);
        Assert.Contains("Module 90 = 100%", pointer);
        Assert.Contains("Module 88 = 100%", pointer);
        Assert.Contains("Module 87 = 100%", pointer);
        Assert.Contains("Module 41 = 100% and Module 60 = 100%", pointer);
        Assert.Contains("Module 163 = 100%", pointer);
        Assert.Contains("Module 164 = 100%", pointer);
        Assert.Contains("Module 165 = 100%", pointer);
        Assert.Contains("Module 166 = 100%", pointer);
        Assert.Contains("Module 167 = 100%", pointer);
        Assert.Contains("Module 168 = 100%", pointer);
        Assert.Contains("Module 169 = 100%", pointer);
        Assert.Contains("Module 170 = 100%", pointer);
        Assert.Contains("Module 171 = 100%", pointer);
        Assert.Contains("Module 172 = 100%", pointer);
        Assert.Contains("Module 173 = 100%", pointer);
        Assert.Contains("Module 174 = 100%", pointer);
        Assert.Contains("Module 175 = 100%", pointer);
        Assert.Contains("Module 180 = 100%", pointer);
        Assert.Contains("Module 181 = 100%", pointer);
        Assert.Contains("Module 182 = 100%", pointer);
        Assert.Contains("Module 183 = 100%", pointer);
        Assert.Contains("Module 184 = 100%", pointer);
        Assert.Contains("Module 185 = 100%", pointer);
        Assert.Contains("Module 186 = 100%", pointer);
        Assert.Contains("Module 187 = 100%", pointer);
        Assert.Contains("Module 188 = 100%", pointer);
        Assert.Contains("Module 189 = 100%", pointer);
        Assert.Contains("Module 190 = 100%", pointer);
        Assert.Contains("Module 191 = 100%", pointer);
        Assert.Contains("Module 192 = 100%", pointer);
        Assert.Contains("Module 193 = 100%", pointer);
        Assert.Contains("Module 194 = 100%", pointer);
        Assert.Contains("Module 195 = 100%", pointer);
        Assert.Contains("Module 196 = 100%", pointer);
        Assert.Contains("Module 197 = 100%", pointer);
        Assert.Contains("Module 198 = 100%", pointer);
        Assert.Contains("Module 199 = 100%", pointer);
        Assert.Contains("Module 200 = 100%", pointer);
        Assert.Contains("Module 201 = 100%", pointer);
        Assert.Contains("Module 202 = 100%", pointer);
        Assert.Contains("Module 203 = 100%", pointer);
        Assert.Contains("Module 204 = 100%", pointer);
        Assert.Contains("Module 205 = 100%", pointer);
        Assert.Contains("Module 206 = 100%", pointer);
        Assert.Contains("Module 207 = 100%", pointer);
        Assert.Contains("Module 208 = 100%", pointer);
        Assert.Contains("Module 209 = 100%", pointer);
        Assert.Contains("Module 210 = 100%", pointer);
        Assert.Contains("Module 211 = 100%", pointer);
        Assert.Contains("Module 212 = 100%", pointer);
        Assert.Contains("Module 213 = 100%", pointer);
        Assert.Contains("Module 214 = 100%", pointer);
        Assert.Contains("Module 179 = 100%", pointer);
        Assert.Contains("Module 178 = 100%", pointer);
        Assert.Contains("Module 177 = 100%", pointer);
        Assert.Contains("Module 176 = 100%", pointer);
        Assert.Contains("Module 215 = 100%", pointer);
        Assert.Contains("Module 216 = 100%", pointer);
        Assert.Contains("Module 217 = 100%", pointer);
        Assert.Contains("Module 218 = 100%", pointer);
        Assert.Contains("Module 219 = 100%", pointer);
        Assert.Contains("Module 220 = 100%", pointer);
        Assert.Contains("DDC2C0A4B4DEBBA3078808795BC0E2CA95BBD6695A01435AFDFEF3078D24D860", pointer);
        Assert.Contains("D:\\AegisWAF\\dist\\AegisWAF-Desktop-CompletedModules\\AegisWAF.exe", pointer);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AegisWAF.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}

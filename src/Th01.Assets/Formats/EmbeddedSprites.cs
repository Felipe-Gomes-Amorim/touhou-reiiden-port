using System.Security.Cryptography;
using Th01.Assets.Model;

namespace Th01.Assets.Formats;

/// <summary>
/// Monochrome sprite tables that the original build compiled into the executables (RECON section 5).
/// They are found at run time by content: for every table the repo only knows its length and two
/// fingerprints (a 64-bit rolling hash and SHA-256), never the bytes themselves. Pixels are 0/1 and
/// Alpha equals the bits (the colour is chosen when drawing, as for .GRC). docs/FORMATS.md.
/// </summary>
public sealed class EmbeddedSprites
{
    private sealed record Table(string Name, int Width, int Height, int Frames, ulong RollingHash, string Sha256)
    {
        public int Length => Width / 8 * Height * Frames;
    }

    // Name = the original symbol: leaf_s = sSPARK, leaf_l = sLEAF_LEFT, leaf_r = sLEAF_RIGHT, ileave_m = sINTERLEAVE_MASKS,
    // laser_s = sSHOOTOUT_LASER, mousecur = sMOUSE_CURSOR, pellet = sPELLET (3 cels x 8 pre-shifts, 16 px wide, cel-major),
    // pellet_c = sPELLET_CLOUD, pillar = sPILLAR, shape8x8 = sSHAPE8X8, bonusbox = sSTAGEBONUS_BOX.
    private static readonly Table[] Tables =
    [
        new("leaf_s", 8, 8, 1, 0x297B738ECE766A91UL, "160ef4a841158fa6b9305535462ed676854debc816079dea6f38ac0e83f62ddc"),
        new("leaf_l", 8, 8, 1, 0xD9059ABA7E824629UL, "f09122d9c2c99df8b5c66e8522e15e2ab8a95e7aef6853fda4fb94d06009a276"),
        new("leaf_r", 8, 8, 1, 0x26FEED21557D63CAUL, "4382ba7468ca054e3a346110868bb66049fb681f33b95d7158b26a8589169039"),
        new("ileave_m", 8, 8, 10, 0xC70C1AE136ECD3D8UL, "45848602dfdf09ec205f6108088806c7307ad8595f385604a3bb500fc8f7ccbb"),
        new("laser_s", 16, 8, 8, 0x9C55EB578DF3A6F8UL, "da20b6c7fc4f0a1f05dc2eaa22f6c318b271ade6fa126ee0d96fe3ee298c10af"),
        new("mousecur", 16, 16, 2, 0xE1C024EB082F23BFUL, "8bb17f01fd74d520b0d793ef82dbb419126402e449e8f0d3dddfc7329a48e660"),
        new("pellet", 16, 8, 24, 0x38C126F3456BEFB4UL, "3f6448b52bca40d373ebe00b057dff7450d94da4e5dd5f09bc15ec005b4dab96"),
        new("pellet_c", 16, 16, 2, 0xE349F35F1BCFFECFUL, "e3f893baab07129101d55f0a84f0c3dc1f421eb2ce57ee180a0d710b7e1c9d54"),
        new("pillar", 32, 8, 2, 0x7FB37B5ECA6C5147UL, "e8ab5be2faa4e258d3103cdcc3b42a7b10e5724413fd7db882c27ac880175f90"),
        new("shape8x8", 8, 8, 7, 0x5575B3C94466734CUL, "887f35f600f209d93062bca726b5d6dd5630ca45de5ba56b43a5ffd8e227b0f6"),
        new("bonusbox", 8, 4, 1, 0x10F3AF0492904791UL, "6380312857b7395c1fe0e4a5a94d81b8d4ef5586eb02abfbd41aa28093ae3667"),
    ];

    private const ulong Base = 1099511628211UL;

    private readonly Dictionary<string, (int Offset, SpriteSheet Sheet)> _found = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Names of all tables the reader knows how to find.</summary>
    public static IReadOnlyList<string> KnownNames => Tables.Select(t => t.Name).ToList();

    /// <summary>Tables that were not found in the executable (e.g. other executables, or a modified build).</summary>
    public IReadOnlyList<string> Missing => Tables.Where(t => !_found.ContainsKey(t.Name)).Select(t => t.Name).ToList();

    public IReadOnlyCollection<string> Found => _found.Keys;

    private EmbeddedSprites() { }

    /// <summary>Scans the executable image and decodes every known table that is present.</summary>
    public static EmbeddedSprites Locate(ReadOnlySpan<byte> exe)
    {
        var result = new EmbeddedSprites();
        foreach (var t in Tables)
        {
            int offset = Find(exe, t);
            if (offset >= 0) result._found[t.Name] = (offset, Decode(exe.Slice(offset, t.Length), t));
        }
        return result;
    }

    public bool Has(string name) => _found.ContainsKey(name);

    /// <summary>Offset of a table inside the executable (diagnostics only).</summary>
    public int OffsetOf(string name) => Lookup(name).Offset;

    public SpriteSheet Get(string name) => Lookup(name).Sheet;

    private (int Offset, SpriteSheet Sheet) Lookup(string name)
    {
        if (_found.TryGetValue(name, out var v)) return v;
        throw Tables.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
            ? new KeyNotFoundException($"Embedded sprite table {name} was not found in this executable.")
            : new KeyNotFoundException($"{name} is not a known embedded sprite table.");
    }

    private static int Find(ReadOnlySpan<byte> exe, Table t)
    {
        int n = t.Length;
        if (exe.Length < n) return -1;
        ulong high = 1; // Base^(n-1)
        for (int i = 1; i < n; i++) high = unchecked(high * Base);

        ulong h = 0;
        for (int i = 0; i < n; i++) h = unchecked(h * Base + exe[i]);
        for (int pos = 0; ; pos++)
        {
            if (h == t.RollingHash && Convert.ToHexString(SHA256.HashData(exe.Slice(pos, n))).Equals(t.Sha256, StringComparison.OrdinalIgnoreCase))
                return pos;
            if (pos + n >= exe.Length) return -1;
            h = unchecked((h - exe[pos] * high) * Base + exe[pos + n]);
        }
    }

    private static SpriteSheet Decode(ReadOnlySpan<byte> data, Table t)
    {
        int rowBytes = t.Width / 8, frameBytes = rowBytes * t.Height;
        var frames = new List<IndexedImageA>(t.Frames);
        for (int f = 0; f < t.Frames; f++)
        {
            byte[] bits = Planar.ToBits(data.Slice(f * frameBytes, frameBytes), rowBytes, t.Height);
            frames.Add(new IndexedImageA(t.Width, t.Height, bits, (byte[])bits.Clone()));
        }
        return new SpriteSheet(frames);
    }
}

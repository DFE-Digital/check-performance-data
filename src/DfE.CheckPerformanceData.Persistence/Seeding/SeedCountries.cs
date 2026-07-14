using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Seeding;

public static class SeedCountries
{
    // Settings-table marker holding a hash of the seed inputs from the last successful seed.
    // Kept out of SettingDefinitions on purpose: it is an internal marker, not an editable
    // setting, so it never appears in the admin settings editor.
    private const string SeedHashSettingKey = "Countries:SeedHash";

    // Bump when the parsing logic or Country shape changes in a way that should force a
    // reseed even though the CSV bytes and hand-written entries are unchanged.
    private const int SeedFormatVersion = 1;

    private static readonly (string Code, string Name, CountryKind Kind)[] HandWrittenEntries =
    [
        ("GB-ENG", "England", CountryKind.HomeNation),
        ("GB-SCT", "Scotland", CountryKind.HomeNation),
        ("GB-WLS", "Wales", CountryKind.HomeNation),
        ("GB-NIR", "Northern Ireland", CountryKind.HomeNation),
        ("IM", "Isle of Man", CountryKind.CrownDependency),
        ("JE", "Jersey", CountryKind.CrownDependency),
        ("GG", "Guernsey", CountryKind.CrownDependency),
    ];

    // Runs unconditionally on startup in every environment (see Program.cs). Idempotent and
    // content-aware: it reseeds only when the table is empty or the seed inputs (embedded CSV
    // bytes, hand-written entries, or SeedFormatVersion) have changed since the last seed,
    // detected via a hash stored in the Settings table. An unchanged database is left alone,
    // so repeated startups never duplicate rows; a changed CSV triggers a full replace.
    public static async Task ExecuteSeed(IPortalDbContext dbContext)
    {
        var desiredHash = ComputeSeedHash();
        var currentHash = await dbContext.Settings
            .Where(s => s.Key == SeedHashSettingKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync();

        // Up to date and populated — nothing to do. The AnyAsync guard reseeds even when the
        // hash matches if the table has somehow been emptied (e.g. manual truncation).
        if (currentHash == desiredHash && await dbContext.Countries.AnyAsync())
            return;

        // Content changed (or first run) — replace the dataset wholesale. ExecuteDeleteAsync
        // runs immediately; if the subsequent insert fails, the marker is not updated, so the
        // next startup re-detects the mismatch and reseeds (self-healing).
        await dbContext.Countries.ExecuteDeleteAsync();

        var countries = new List<Country>();
        countries.AddRange(ReadCsvEntries());
        countries.AddRange(HandWrittenEntries.Select(e => new Country
        {
            Id = Guid.NewGuid(),
            Code = e.Code,
            Name = e.Name,
            OfficialName = e.Name,
            Kind = e.Kind
        }));

        await dbContext.Countries.AddRangeAsync(countries);

        var marker = await dbContext.Settings.FirstOrDefaultAsync(s => s.Key == SeedHashSettingKey);
        if (marker is null)
        {
            marker = new Setting { Key = SeedHashSettingKey };
            await dbContext.Settings.AddAsync(marker);
        }
        marker.Value = desiredHash;

        await dbContext.SaveChangesAsync();
    }

    // A stable fingerprint of everything that determines the seeded rows: a format version,
    // the hand-written entries, and the raw CSV bytes. Any change flips the hash and triggers
    // a reseed on next startup.
    private static string ComputeSeedHash()
    {
        var prefix = new StringBuilder();
        prefix.Append('v').Append(SeedFormatVersion).Append('\n');
        foreach (var e in HandWrittenEntries)
            prefix.Append(e.Code).Append('|').Append(e.Name).Append('|').Append((int)e.Kind).Append('\n');

        var prefixBytes = Encoding.UTF8.GetBytes(prefix.ToString());
        var csvBytes = ReadCsvBytes();

        var combined = new byte[prefixBytes.Length + csvBytes.Length];
        Buffer.BlockCopy(prefixBytes, 0, combined, 0, prefixBytes.Length);
        Buffer.BlockCopy(csvBytes, 0, combined, prefixBytes.Length, csvBytes.Length);

        return Convert.ToHexString(SHA256.HashData(combined));
    }

    private static byte[] ReadCsvBytes()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("FCDO_Geographical_Names_Index.csv"));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static IEnumerable<Country> ReadCsvEntries()
    {
        // File uses Latin-1 encoding (e.g. ü in "Türkiye")
        using var reader = new StreamReader(new MemoryStream(ReadCsvBytes()), Encoding.Latin1);

        reader.ReadLine(); // skip header

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            // Limit to 4 parts so citizen names with internal commas (quoted) stay in the last element
            var parts = line.Split(',', 4);
            if (parts.Length < 3) continue;

            var code = parts[0].Trim();
            var name = parts[1].Trim();
            var officialName = parts[2].Trim();

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
                continue;

            // GB is replaced by the four UK home nations added as HandWrittenEntries
            if (code == "GB") continue;

            yield return new Country
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = name,
                OfficialName = officialName,
                Kind = CountryKind.Sovereign
            };
        }
    }
}

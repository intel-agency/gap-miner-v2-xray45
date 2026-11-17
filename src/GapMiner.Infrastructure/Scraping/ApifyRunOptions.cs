using System.Text.Json.Serialization;

namespace GapMiner.Infrastructure.Scraping;

/// <summary>
/// The <c>options</c> object of an Apify actor run ("Get Actor run", Apify API
/// v2 reference) — the configuration the run was started with. A typed subset;
/// fields Apify adds later are ignored on deserialization.
/// </summary>
public sealed class ApifyRunOptions
{
    /// <summary>The Actor build the run used (tag or number, e.g. <c>latest</c>).</summary>
    [JsonPropertyName("build")]
    public string? Build { get; set; }

    /// <summary>The run timeout, in seconds.</summary>
    [JsonPropertyName("timeoutSecs")]
    public int? TimeoutSecs { get; set; }

    /// <summary>The memory limit of the run, in megabytes.</summary>
    [JsonPropertyName("memoryMbytes")]
    public int? MemoryMbytes { get; set; }

    /// <summary>The disk limit of the run, in megabytes.</summary>
    [JsonPropertyName("diskMbytes")]
    public int? DiskMbytes { get; set; }

    /// <summary>The maximum number of charged dataset items (pay-per-result actors).</summary>
    [JsonPropertyName("maxItems")]
    public int? MaxItems { get; set; }
}

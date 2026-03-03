using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DocAgent.Tests.Performance;

/// <summary>
/// Root deserialization model for baselines.json.
/// </summary>
public record BaselineFile(
    [property: JsonPropertyName("_note")] string? Note,
    [property: JsonPropertyName("benchmarks")] Dictionary<string, BaselineEntry> Benchmarks
);

/// <summary>
/// Per-benchmark baseline thresholds read from baselines.json.
/// </summary>
public record BaselineEntry(
    [property: JsonPropertyName("meanNanoseconds")] double MeanNanoseconds,
    [property: JsonPropertyName("allocatedBytes")] long AllocatedBytes,
    [property: JsonPropertyName("absoluteCeilingMeanNanoseconds")] double? AbsoluteCeilingNanoseconds,
    [property: JsonPropertyName("absoluteCeilingAllocatedBytes")] long? AbsoluteCeilingAllocatedBytes,
    [property: JsonPropertyName("description")] string? Description
);

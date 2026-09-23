using SuggestX.Contracts.Dtos;

namespace SuggestX.Aggregator.Domain;

/// <summary>
/// One S3 object from suggestx-raw-logs, already parsed — the doc's "raw
/// data" the aggregator "retrieves from HDFS and distributes to workers,"
/// made concrete as one object CollectionService flushed.
/// </summary>
public sealed record RawLogBatch(string Key, IReadOnlyList<SearchLogEntry> Entries);

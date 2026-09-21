namespace SuggestX.ServiceDefaults.Configuration;

/// <summary>
/// AWS endpoint configuration. Locally this points at LocalStack; in a real
/// account every value except <see cref="Region"/> is left unset and the SDK's
/// default credential and endpoint resolution takes over. Unlike JameX, no
/// service here ever hands a URL to a browser, so there is no second
/// "public" endpoint to presign for — every AWS call in this system is
/// server-to-server.
/// </summary>
public sealed class AwsOptions
{
    public const string SectionName = "Aws";

    public string? ServiceUrl { get; set; }
    public string Region { get; set; } = "us-east-1";

    /// <summary>Static credentials for LocalStack. Never set these in production.</summary>
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }

    public bool UsesCustomEndpoint => !string.IsNullOrWhiteSpace(ServiceUrl);
}

/// <summary>
/// The two S3 buckets in this system, and the only thing either one holds.
/// See DESIGN.md §1 decision 3 for why trie snapshots are S3 objects, not
/// DynamoDB items or Mongo documents — a serialized trie is a blob, not a
/// queryable row.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// CollectionService's exclusive store. One object per flush per instance
    /// (see CollectionService's own doc comments once it exists) — never
    /// rewritten, only appended-to-the-bucket, mirroring HDFS's own
    /// write-once files.
    /// </summary>
    public string RawLogsBucket { get; set; } = "suggestx-raw-logs";

    /// <summary>
    /// TrieBuilder's exclusive durability store — a full trie snapshot per
    /// partition per version, kept for recovery. The version actually being
    /// served lives in Redis, not here; this bucket exists so a crashed
    /// TrieBuilder can rebuild its in-memory trie from the last snapshot
    /// instead of re-deriving it from every historical DynamoDB frequency.
    /// </summary>
    public string TrieSnapshotsBucket { get; set; } = "suggestx-trie-snapshots";
}

/// <summary>
/// Aggregator's exclusive DynamoDB table — the Cassandra stand-in. One item
/// per phrase, updated with an atomic <c>ADD</c> so a redelivered/replayed
/// log batch increments rather than overwrites.
/// </summary>
public sealed class DynamoOptions
{
    public const string SectionName = "Dynamo";

    public string PhraseFrequenciesTable { get; set; } = "suggestx-phrase-frequencies";
}

/// <summary>
/// ZooKeeper coordination. TrieBuilder is the only writer (it owns
/// <c>/suggestx/**</c>); SuggestionService is a reader watching for version
/// changes. See DESIGN.md §1 decision 4 for why this runs as a real
/// container rather than being flattened into a Redis key.
/// </summary>
public sealed class ZooKeeperOptions
{
    public const string SectionName = "ZooKeeper";

    public string ConnectionString { get; set; } = "zookeeper:2181";
    public string RootPath { get; set; } = "/suggestx";
}

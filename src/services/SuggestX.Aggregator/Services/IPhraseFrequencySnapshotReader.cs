using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using SuggestX.ServiceDefaults.Configuration;

namespace SuggestX.Aggregator.Services;

/// <summary>
/// Read access to Aggregator's own table, for its own debug visibility —
/// `/_debug/frequencies` (the insights panel's phrase-frequency table),
/// not anything on a write/read path. Aggregator otherwise only ever
/// writes this table (<see cref="IPhraseFrequencyWriter"/>); TrieBuilder
/// is the table's one real read-only consumer downstream. A full `Scan`,
/// same shape and same defensiveness as TrieBuilder's own
/// `DynamoPhraseFrequencyReader` — this is a debug view of the whole
/// table, not a bounded query, so there's no cheaper way to answer it.
/// </summary>
public interface IPhraseFrequencySnapshotReader
{
    Task<IReadOnlyList<(string Phrase, long Frequency)>> ReadAllAsync(CancellationToken ct);
}

public sealed class DynamoPhraseFrequencySnapshotReader(
    IAmazonDynamoDB dynamo, IOptions<DynamoOptions> options) : IPhraseFrequencySnapshotReader
{
    public async Task<IReadOnlyList<(string Phrase, long Frequency)>> ReadAllAsync(CancellationToken ct)
    {
        var results = new List<(string, long)>();
        Dictionary<string, AttributeValue>? lastEvaluatedKey = null;

        do
        {
            var response = await dynamo.ScanAsync(new ScanRequest
            {
                TableName = options.Value.PhraseFrequenciesTable,
                ExclusiveStartKey = lastEvaluatedKey
            }, ct);

            // Same AWS SDK habit as everywhere else in this project: Items
            // and LastEvaluatedKey are null, not empty, when there's
            // nothing more — see DESIGN.md decision 12.
            if (response.Items is { Count: > 0 })
            {
                foreach (var item in response.Items)
                {
                    if (item.TryGetValue("phrase", out var phraseAttr)
                        && item.TryGetValue("frequency", out var freqAttr)
                        && long.TryParse(freqAttr.N, out var frequency))
                    {
                        results.Add((phraseAttr.S, frequency));
                    }
                }
            }

            lastEvaluatedKey = response.LastEvaluatedKey is { Count: > 0 }
                ? response.LastEvaluatedKey
                : null;
        } while (lastEvaluatedKey is not null);

        return results;
    }
}

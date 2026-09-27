using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using SuggestX.ServiceDefaults.Configuration;

namespace SuggestX.TrieBuilder.Services;

/// <summary>
/// Read-only access to Aggregator's store — TrieBuilder never writes to
/// suggestx-phrase-frequencies, matching the pipeline's linear,
/// read-the-previous-stage's-store-only design (CLAUDE.md). Unlike
/// Aggregator's own checkpointed, incremental reads of S3, this reads the
/// *entire* table every cycle: a full trie rebuild needs the complete
/// current dataset, not just what changed since last time.
/// </summary>
public interface IPhraseFrequencyReader
{
    Task<IReadOnlyList<(string Phrase, long Frequency)>> ReadAllAsync(CancellationToken ct);
}

public sealed class DynamoPhraseFrequencyReader(
    IAmazonDynamoDB dynamo, IOptions<DynamoOptions> options) : IPhraseFrequencyReader
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

            // Same AWS SDK habit as everywhere else in this project by now:
            // check for null/empty explicitly rather than assume — Items and
            // LastEvaluatedKey have both turned out to be null instead of
            // empty in other response types (see DESIGN.md decision 12).
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

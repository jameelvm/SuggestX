using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using SuggestX.Contracts.Dtos;
using SuggestX.ServiceDefaults.Configuration;

namespace SuggestX.Aggregator.Services;

/// <summary>
/// The doc's "MapReduce job aggregates prefix frequencies" step, made
/// concrete for one batch at a time. "Map": count each normalized phrase's
/// occurrences within the batch. "Reduce": one atomic DynamoDB <c>ADD</c>
/// per unique phrase in the batch — not one ADD per entry — so ten people
/// searching "jazz piano" in the same S3 object costs one write, not ten.
/// </summary>
public interface IPhraseFrequencyWriter
{
    Task ApplyAsync(IReadOnlyList<SearchLogEntry> entries, CancellationToken ct);
}

public sealed class DynamoPhraseFrequencyWriter(
    IAmazonDynamoDB dynamo,
    IOptions<DynamoOptions> options,
    ILogger<DynamoPhraseFrequencyWriter> logger) : IPhraseFrequencyWriter
{
    public async Task ApplyAsync(IReadOnlyList<SearchLogEntry> entries, CancellationToken ct)
    {
        // Map: normalize and count within this batch only. The doc
        // explicitly assumes case-insensitive matching ("Data structure for
        // storing prefixes" chapter) — this is the one place that's
        // enforced. Everything downstream (Aggregator's own table,
        // TrieBuilder reading it later) sees only already-normalized
        // phrases and never has to think about casing again.
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var phrase = Normalize(entry.Query);
            if (phrase.Length == 0) continue;

            counts[phrase] = counts.GetValueOrDefault(phrase) + 1;
        }

        // Reduce: one atomic ADD per unique phrase. ADD both creates the
        // item (initializing frequency to the operand) and increments an
        // existing one — no separate "does this phrase exist yet" check
        // needed, and no read-modify-write race between concurrent writers.
        foreach (var (phrase, count) in counts)
        {
            await dynamo.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = options.Value.PhraseFrequenciesTable,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["phrase"] = new AttributeValue { S = phrase }
                },
                UpdateExpression = "ADD frequency :inc",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":inc"] = new AttributeValue { N = count.ToString(CultureInfo.InvariantCulture) }
                }
            }, ct);
        }

        if (counts.Count > 0)
        {
            logger.LogInformation(
                "Applied {UniquePhrases} phrase count(s) from {EntryCount} entries",
                counts.Count, entries.Count);
        }
    }

    private static string Normalize(string query) => query.Trim().ToLowerInvariant();
}

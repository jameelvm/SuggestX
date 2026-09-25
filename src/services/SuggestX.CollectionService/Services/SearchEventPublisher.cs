using System.Text;
using System.Text.Json;
using Amazon.KinesisFirehose;
using Amazon.KinesisFirehose.Model;
using Microsoft.Extensions.Options;
using SuggestX.Contracts.Dtos;
using SuggestX.ServiceDefaults.Configuration;

namespace SuggestX.CollectionService.Services;

/// <summary>
/// The doc's "collection service logs the phrase, timestamp, and metadata"
/// step. Publishes one record per accepted search event to Kinesis Data
/// Firehose, which buffers records server-side and batch-writes them into
/// suggestx-raw-logs — the managed replacement for the in-memory queue and
/// timer-driven flush worker this service used to own (DESIGN.md decision 11).
/// </summary>
public interface ISearchEventPublisher
{
    Task PublishAsync(SearchLogEntry entry, CancellationToken ct);
}

public sealed class FirehoseSearchEventPublisher(
    IAmazonKinesisFirehose firehose,
    IOptions<FirehoseOptions> options,
    IPublishedEventStats stats) : ISearchEventPublisher
{
    public async Task PublishAsync(SearchLogEntry entry, CancellationToken ct)
    {
        // Firehose concatenates record payloads verbatim when it writes a
        // batch to S3 — it inserts no separator of its own. The trailing
        // newline is what makes the delivered object line-delimited JSON,
        // which is the format Aggregator's reader parses.
        var payload = JsonSerializer.Serialize(entry) + "\n";

        await firehose.PutRecordAsync(new PutRecordRequest
        {
            DeliveryStreamName = options.Value.SearchEventsStream,
            Record = new Record { Data = new MemoryStream(Encoding.UTF8.GetBytes(payload)) }
        }, ct);

        stats.RecordPublished();
    }
}

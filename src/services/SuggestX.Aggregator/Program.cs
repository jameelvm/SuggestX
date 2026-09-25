using SuggestX.Aggregator;
using SuggestX.ServiceDefaults.Hosting;

// Owns suggestx-phrase-frequencies and suggestx-aggregator-checkpoints
// (both DynamoDB) exclusively — the doc's "aggregator" / MapReduce-over-HDFS
// stand-in, now fully assembled. RawLogPollingWorker reads new objects from
// CollectionService's Firehose-delivered S3 objects on a timer (read-only —
// Aggregator never writes to suggestx-raw-logs), maps and reduces each
// batch's phrases into atomic DynamoDB ADDs, then durably advances its
// checkpoint so a restart resumes instead of reprocessing the whole bucket.
// The HTTP surface here is for health/status only — the real work is the
// background worker.
const string ServiceName = "Aggregator";

var builder = WebApplication.CreateBuilder(args);

builder.AddSuggestXServiceDefaults(ServiceName);
builder.AddSuggestXApiDefaults();
builder.Services.AddAggregatorServices(builder.Configuration);

var app = builder.Build();

app.UseCors();
app.MapSuggestXDefaultEndpoints(ServiceName);

app.Run();

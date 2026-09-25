using SuggestX.Aggregator;
using SuggestX.ServiceDefaults.Hosting;

// Owns suggestx-phrase-frequencies (DynamoDB, once Module 2 lands) and
// suggestx-aggregator-checkpoints (DynamoDB) exclusively — the doc's
// "aggregator" / MapReduce-over-HDFS stand-in. RawLogPollingWorker reads new
// objects from CollectionService's Firehose-delivered S3 objects on a timer
// (read-only — Aggregator never writes to suggestx-raw-logs), resuming from
// a durably persisted checkpoint on restart instead of the whole bucket.
// The map-reduce into phrase-frequency counts is still Module 2, not yet
// built. The HTTP surface here is for health/status only — the real work is
// the background worker.
const string ServiceName = "Aggregator";

var builder = WebApplication.CreateBuilder(args);

builder.AddSuggestXServiceDefaults(ServiceName);
builder.AddSuggestXApiDefaults();
builder.Services.AddAggregatorServices(builder.Configuration);

var app = builder.Build();

app.UseCors();
app.MapSuggestXDefaultEndpoints(ServiceName);

app.Run();

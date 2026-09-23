using SuggestX.Aggregator;
using SuggestX.ServiceDefaults.Hosting;

// Owns suggestx-phrase-frequencies (DynamoDB) exclusively — the doc's
// "aggregator" / MapReduce-over-HDFS stand-in. RawLogPollingWorker reads new
// objects from CollectionService's S3 bucket on a timer (read-only —
// Aggregator never writes there); the map-reduce into DynamoDB arrives
// Module 2. The HTTP surface here is for health/status only — the real work
// is the background worker.
const string ServiceName = "Aggregator";

var builder = WebApplication.CreateBuilder(args);

builder.AddSuggestXServiceDefaults(ServiceName);
builder.AddSuggestXApiDefaults();
builder.Services.AddAggregatorServices(builder.Configuration);

var app = builder.Build();

app.UseCors();
app.MapSuggestXDefaultEndpoints(ServiceName);

app.Run();

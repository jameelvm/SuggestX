using SuggestX.ServiceDefaults.Hosting;

// Owns suggestx-phrase-frequencies (DynamoDB) exclusively — the doc's
// "aggregator" / MapReduce-over-HDFS stand-in. Once Phase 3 fills this in: a
// BackgroundService on a timer reads new objects from CollectionService's S3
// bucket (read-only — Aggregator never writes there), does an in-process
// map-reduce over phrases, and atomically ADDs the counts into DynamoDB. It
// keeps a small HTTP surface (this Program.cs) purely for health/status, not
// for serving traffic — the real work is the background worker.
const string ServiceName = "Aggregator";

var builder = WebApplication.CreateBuilder(args);

builder.AddSuggestXServiceDefaults(ServiceName);
builder.AddSuggestXApiDefaults();

var app = builder.Build();

app.UseCors();
app.MapSuggestXDefaultEndpoints(ServiceName);

app.Run();

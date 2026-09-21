using SuggestX.ServiceDefaults.Hosting;

// Owns suggestx-raw-logs (S3) exclusively — the doc's "collection service"
// / HDFS write path. Once Phase 2 fills this in: buffers submitted search
// events in memory per instance and flushes them as line-delimited JSON
// objects on a timer, so concurrent instances never contend on the same S3
// key. Module 1 only proves the process starts and answers a health check.
const string ServiceName = "CollectionService";

var builder = WebApplication.CreateBuilder(args);

builder.AddSuggestXServiceDefaults(ServiceName);
builder.AddSuggestXApiDefaults();

var app = builder.Build();

app.UseCors();
app.MapSuggestXDefaultEndpoints(ServiceName);

app.Run();

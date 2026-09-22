using SuggestX.CollectionService;
using SuggestX.ServiceDefaults.Hosting;

// Owns suggestx-raw-logs (S3) exclusively — the doc's "collection service"
// / HDFS write path. Module 1 (this): POST /search-events buffers submitted
// search events in memory, per instance. Module 2 adds the timed flush to
// S3 as line-delimited JSON, keyed per instance so concurrent instances
// never contend on the same object.
const string ServiceName = "CollectionService";

var builder = WebApplication.CreateBuilder(args);

builder.AddSuggestXServiceDefaults(ServiceName);
builder.AddSuggestXApiDefaults();
builder.Services.AddCollectionServices();

var app = builder.Build();

app.UseCors();
app.MapSuggestXDefaultEndpoints(ServiceName);

app.Run();

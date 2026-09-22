using SuggestX.CollectionService;
using SuggestX.ServiceDefaults.Hosting;

// Owns suggestx-raw-logs (S3) exclusively — the doc's "collection service"
// / HDFS write path. POST /search-events buffers submitted search events in
// memory, per instance; SearchEventFlushWorker drains that buffer on a
// timer and writes it as one line-delimited-JSON object to S3, keyed per
// instance so concurrent instances never contend on the same object.
const string ServiceName = "CollectionService";

var builder = WebApplication.CreateBuilder(args);

builder.AddSuggestXServiceDefaults(ServiceName);
builder.AddSuggestXApiDefaults();
builder.Services.AddCollectionServices(builder.Configuration);

var app = builder.Build();

app.UseCors();
app.MapSuggestXDefaultEndpoints(ServiceName);

app.Run();

using SuggestX.CollectionService;
using SuggestX.ServiceDefaults.Hosting;

// The doc's "collection service" — the write path that turns a submitted
// search into durable evidence. POST /search-events validates the query and
// publishes it to the suggestx-search-events Firehose delivery stream;
// Firehose buffers records server-side and batch-writes them into
// suggestx-raw-logs, where Aggregator reads them.
//
// This service owns no store and holds no state: it previously buffered
// events in memory and flushed them to S3 on a timer, which meant an
// ungraceful kill lost whatever hadn't been flushed yet. See DESIGN.md
// decision 11 for why that was replaced with a managed delivery stream.
const string ServiceName = "CollectionService";

var builder = WebApplication.CreateBuilder(args);

builder.AddSuggestXServiceDefaults(ServiceName);
builder.AddSuggestXApiDefaults();
builder.Services.AddCollectionServices();

var app = builder.Build();

app.UseCors();
app.MapSuggestXDefaultEndpoints(ServiceName);

app.Run();

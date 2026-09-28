using SuggestX.ServiceDefaults.Hosting;
using SuggestX.SuggestionService;

// The hot read path. Owns no durable store of its own — every request is
// one Redis GET against the flattened prefix -> top-N cache TrieBuilder
// publishes, against whichever version CurrentVersionPoller last learned
// from ZooKeeper. No trie traversal, no DynamoDB/S3 access, ever
// (DESIGN.md decision 2, decision 16).
const string ServiceName = "SuggestionService";

var builder = WebApplication.CreateBuilder(args);

builder.AddSuggestXServiceDefaults(ServiceName);
builder.AddSuggestXApiDefaults();
builder.Services.AddSuggestionServiceServices(builder.Configuration);

var app = builder.Build();

app.UseCors();
app.MapSuggestXDefaultEndpoints(ServiceName);

app.Run();

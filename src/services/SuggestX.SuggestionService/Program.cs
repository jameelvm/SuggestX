using SuggestX.ServiceDefaults.Hosting;

// The hot read path. Owns no durable store of its own — every request is,
// once Phase 5 fills this in, a single Redis GET against the flattened
// prefix -> top-N cache TrieBuilder publishes. Module 1 exists only to prove
// this process starts, joins the compose network, and answers a health check
// before any real logic exists — see PROGRESS.md Phase 1.
const string ServiceName = "SuggestionService";

var builder = WebApplication.CreateBuilder(args);

builder.AddSuggestXServiceDefaults(ServiceName);
builder.AddSuggestXApiDefaults();

var app = builder.Build();

app.UseCors();
app.MapSuggestXDefaultEndpoints(ServiceName);

app.Run();

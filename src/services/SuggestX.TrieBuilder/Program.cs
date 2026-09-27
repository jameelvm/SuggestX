using SuggestX.ServiceDefaults.Hosting;
using SuggestX.TrieBuilder;

// Owns suggestx-trie-snapshots (S3), the served trie:* Redis namespace, and
// the ZooKeeper version znodes under /suggestx — exclusively. TrieBuildWorker
// reads Aggregator's DynamoDB table (read-only) on a timer and builds a
// fresh compressed trie in memory (Module 1) — the only place in the whole
// system an actual trie data structure exists (CLAUDE.md). Still to come:
// walking it once to precompute prefix -> top-N and flattening that into
// Redis (Module 2), then a full snapshot to S3 and the ZooKeeper-coordinated
// blue/green version swap from DESIGN.md §1 decision 8 (Module 3) — this is
// the only service that will ever write to ZooKeeper.
const string ServiceName = "TrieBuilder";

var builder = WebApplication.CreateBuilder(args);

builder.AddSuggestXServiceDefaults(ServiceName);
builder.AddSuggestXApiDefaults();
builder.Services.AddTrieBuilderServices(builder.Configuration);

var app = builder.Build();

app.UseCors();
app.MapSuggestXDefaultEndpoints(ServiceName);

app.Run();

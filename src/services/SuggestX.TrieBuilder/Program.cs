using SuggestX.ServiceDefaults.Hosting;
using SuggestX.TrieBuilder;

// Owns suggestx-trie-snapshots (S3), the served trie:* Redis namespace, and
// the ZooKeeper version znodes under /suggestx — exclusively; the only
// service that ever writes to ZooKeeper. TrieBuildWorker reads Aggregator's
// DynamoDB table (read-only) on a timer and builds a fresh compressed trie
// in memory (Module 1) — the only place in the whole system an actual trie
// data structure exists (CLAUDE.md). It walks that trie once to precompute
// prefix -> top-N and flattens the result into a versioned Redis namespace
// (Module 2), then persists the same flattened result to S3 and flips the
// ZooKeeper current_version znode — only after Redis already has it live —
// so a restart resumes from the last published version instead of resetting
// to 1 (Module 3, DESIGN.md §1 decisions 8 and 14).
const string ServiceName = "TrieBuilder";

var builder = WebApplication.CreateBuilder(args);

builder.AddSuggestXServiceDefaults(ServiceName);
builder.AddSuggestXApiDefaults();
builder.Services.AddTrieBuilderServices(builder.Configuration);

var app = builder.Build();

app.UseCors();
app.MapSuggestXDefaultEndpoints(ServiceName);

app.Run();

using SuggestX.ServiceDefaults.Hosting;

// Owns suggestx-trie-snapshots (S3), the served trie:* Redis namespace, and
// the ZooKeeper version znodes under /suggestx — exclusively. Once Phase 4
// fills this in: a BackgroundService on a timer reads Aggregator's DynamoDB
// table (read-only), builds a compressed trie per partition in memory, walks
// it once to precompute prefix -> top-N, writes a new version's worth of
// flattened entries to Redis plus a full snapshot to S3, then flips the
// ZooKeeper current_version znode per partition — the blue/green swap from
// DESIGN.md §1 decision 8. This is the only service that ever writes to
// ZooKeeper.
const string ServiceName = "TrieBuilder";

var builder = WebApplication.CreateBuilder(args);

builder.AddSuggestXServiceDefaults(ServiceName);
builder.AddSuggestXApiDefaults();

var app = builder.Build();

app.UseCors();
app.MapSuggestXDefaultEndpoints(ServiceName);

app.Run();

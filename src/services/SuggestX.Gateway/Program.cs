using SuggestX.ServiceDefaults.Hosting;

// The one address the frontend knows. Unlike JameX's Gateway, this one does
// no authentication (the design doc has no identity concept at all — every
// request is anonymous) and, for now, no BFF aggregation either. It exists
// for the same first reason JameX's did: one origin so services can move
// without the frontend's CORS configuration changing. A small admin/insights
// aggregation endpoint (trie version, partition map, aggregation lag) is
// planned for Phase 6, once there is something on the other end to aggregate.
// Routing is table-driven from appsettings, same as JameX.
const string ServiceName = "Gateway";

var builder = WebApplication.CreateBuilder(args);

builder.AddSuggestXServiceDefaults(ServiceName);
builder.AddSuggestXApiDefaults();

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseCors();
app.MapSuggestXDefaultEndpoints(ServiceName);

app.MapReverseProxy();

app.Run();

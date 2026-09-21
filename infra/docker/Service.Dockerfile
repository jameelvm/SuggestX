# One image definition for every HTTP service. The service is selected by the
# SERVICE build argument, so adding a service means adding a compose entry,
# not another Dockerfile. Same convention as the JameX project.
#
#   docker build -f infra/docker/Service.Dockerfile --build-arg SERVICE=SuggestionService .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG SERVICE
WORKDIR /src

# Restore from project files alone, so editing source does not invalidate the
# (slow) package restore layer.
COPY SuggestX.slnx ./
COPY src/shared/SuggestX.Contracts/SuggestX.Contracts.csproj             src/shared/SuggestX.Contracts/
COPY src/shared/SuggestX.ServiceDefaults/SuggestX.ServiceDefaults.csproj src/shared/SuggestX.ServiceDefaults/
COPY src/services/SuggestX.Gateway/SuggestX.Gateway.csproj               src/services/SuggestX.Gateway/
COPY src/services/SuggestX.SuggestionService/SuggestX.SuggestionService.csproj src/services/SuggestX.SuggestionService/
COPY src/services/SuggestX.CollectionService/SuggestX.CollectionService.csproj src/services/SuggestX.CollectionService/
COPY src/services/SuggestX.Aggregator/SuggestX.Aggregator.csproj         src/services/SuggestX.Aggregator/
COPY src/services/SuggestX.TrieBuilder/SuggestX.TrieBuilder.csproj       src/services/SuggestX.TrieBuilder/
RUN dotnet restore "src/services/SuggestX.${SERVICE}/SuggestX.${SERVICE}.csproj"

COPY src/ src/
RUN dotnet publish "src/services/SuggestX.${SERVICE}/SuggestX.${SERVICE}.csproj" \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG SERVICE
WORKDIR /app
COPY --from=build /app ./

# ARG values do not survive into the running container, so carry it in an ENV.
ENV SERVICE_DLL="SuggestX.${SERVICE}.dll"
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# exec keeps dotnet as PID 1 so SIGTERM reaches it and shutdown is graceful —
# matters for Aggregator/TrieBuilder, which must finish an in-flight batch.
ENTRYPOINT ["sh", "-c", "exec dotnet $SERVICE_DLL"]

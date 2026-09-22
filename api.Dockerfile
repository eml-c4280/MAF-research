# Self-contained multi-stage build: restores, builds and publishes inside the image, so
# `docker compose up --build` is all you need - no prior `dotnet publish` on the host.
#
# The certs/ step matters on networks doing TLS inspection (Zscaler and friends): the proxy
# re-signs HTTPS with a corporate root CA that the host trusts but a stock container image does
# not, which makes `dotnet restore` fail with NU1301 as if the network were blocked. Dropping
# that CA into certs/ fixes it. On a normal network certs/ is empty and this is a no-op.
# See certs/README.md.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY certs/ /usr/local/share/ca-certificates/
RUN update-ca-certificates

# Restore as its own layer, keyed only on the project files, so code edits don't re-download
# packages on every rebuild. Restores just AgentCore.Api's own project graph, not the whole
# .sln - the .sln also lists mcp/ClaimsToolsServer and tests/, which this image doesn't need
# and shouldn't have to copy into its build context.
COPY src/AgentCore.Domain/AgentCore.Domain.csproj src/AgentCore.Domain/
COPY src/AgentCore.Application/AgentCore.Application.csproj src/AgentCore.Application/
COPY src/AgentCore.Infrastructure/AgentCore.Infrastructure.csproj src/AgentCore.Infrastructure/
COPY src/AgentCore.Agents/AgentCore.Agents.csproj src/AgentCore.Agents/
COPY src/AgentCore.Api/AgentCore.Api.csproj src/AgentCore.Api/
RUN dotnet restore src/AgentCore.Api/AgentCore.Api.csproj

COPY src/ src/
RUN dotnet publish src/AgentCore.Api/AgentCore.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Same CAs at runtime, so outbound HTTPS (e.g. a hosted LLM provider) works through the proxy too.
COPY certs/ /usr/local/share/ca-certificates/
RUN update-ca-certificates

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "AgentCore.Api.dll"]

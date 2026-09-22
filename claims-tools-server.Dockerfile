# Same shape as api.Dockerfile - see that file's header comment for why certs/ is here.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY certs/ /usr/local/share/ca-certificates/
RUN update-ca-certificates

# Restore just ClaimsToolsServer's own project graph (Domain + Infrastructure), not the whole
# .sln - the .sln also lists src/AgentCore.Api/Agents/Application and tests/, which this image
# doesn't need.
COPY src/AgentCore.Domain/AgentCore.Domain.csproj src/AgentCore.Domain/
COPY src/AgentCore.Infrastructure/AgentCore.Infrastructure.csproj src/AgentCore.Infrastructure/
COPY mcp/ClaimsToolsServer/ClaimsToolsServer.csproj mcp/ClaimsToolsServer/
RUN dotnet restore mcp/ClaimsToolsServer/ClaimsToolsServer.csproj

COPY src/AgentCore.Domain/ src/AgentCore.Domain/
COPY src/AgentCore.Infrastructure/ src/AgentCore.Infrastructure/
COPY mcp/ClaimsToolsServer/ mcp/ClaimsToolsServer/
RUN dotnet publish mcp/ClaimsToolsServer/ClaimsToolsServer.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

COPY certs/ /usr/local/share/ca-certificates/
RUN update-ca-certificates

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8081
EXPOSE 8081

ENTRYPOINT ["dotnet", "ClaimsToolsServer.dll"]

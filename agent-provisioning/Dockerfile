FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["agent-provisioning/src/CohereInventoryAndTrend.AgentProvisioning/CohereInventoryAndTrend.AgentProvisioning.csproj", "agent-provisioning/src/CohereInventoryAndTrend.AgentProvisioning/"]
RUN dotnet restore "agent-provisioning/src/CohereInventoryAndTrend.AgentProvisioning/CohereInventoryAndTrend.AgentProvisioning.csproj"
COPY agent-provisioning/ agent-provisioning/
WORKDIR /src/agent-provisioning/src/CohereInventoryAndTrend.AgentProvisioning
RUN dotnet publish "CohereInventoryAndTrend.AgentProvisioning.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /src/agent-provisioning/src/CohereInventoryAndTrend.AgentProvisioning/agents /app/agents
COPY --from=build /src/agent-provisioning/src/CohereInventoryAndTrend.AgentProvisioning/shared /app/shared
ENTRYPOINT ["dotnet", "CohereInventoryAndTrend.AgentProvisioning.dll"]

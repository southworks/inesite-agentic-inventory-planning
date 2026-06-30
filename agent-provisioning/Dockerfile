FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["agent-provisioning/src/GrokInventoryAndTrend.AgentProvisioning/GrokInventoryAndTrend.AgentProvisioning.csproj", "agent-provisioning/src/GrokInventoryAndTrend.AgentProvisioning/"]
RUN dotnet restore "agent-provisioning/src/GrokInventoryAndTrend.AgentProvisioning/GrokInventoryAndTrend.AgentProvisioning.csproj"
COPY agent-provisioning/ agent-provisioning/
WORKDIR /src/agent-provisioning/src/GrokInventoryAndTrend.AgentProvisioning
RUN dotnet publish "GrokInventoryAndTrend.AgentProvisioning.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "GrokInventoryAndTrend.AgentProvisioning.dll"]

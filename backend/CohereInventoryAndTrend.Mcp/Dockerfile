FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["backend/src/CohereInventoryAndTrend.Mcp/CohereInventoryAndTrend.Mcp.csproj", "backend/src/CohereInventoryAndTrend.Mcp/"]
RUN dotnet restore "backend/src/CohereInventoryAndTrend.Mcp/CohereInventoryAndTrend.Mcp.csproj"
COPY backend/src/CohereInventoryAndTrend.Mcp/ backend/src/CohereInventoryAndTrend.Mcp/
COPY dataset-seed/ dataset-seed/
WORKDIR /src/backend/src/CohereInventoryAndTrend.Mcp
RUN dotnet publish "CohereInventoryAndTrend.Mcp.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /src/dataset-seed /app/dataset-seed
ENV Dataset__RootPath=/app/dataset-seed
ENV Dataset__PromotionsFilePath=/app/dataset-seed/promotions-price-rag/promotions_price_calendar.txt
ENV Dataset__SignalQualityFilePath=/app/dataset-seed/signal-quality-rag/signal_quality_rules.txt
ENV Dataset__TrendPatternsFilePath=/app/dataset-seed/trend-patterns-rag/trend_patterns.txt
ENV Dataset__PlanningConstraintsFilePath=/app/dataset-seed/planning-constraints-rag/planning_constraints.txt
ENTRYPOINT ["dotnet", "CohereInventoryAndTrend.Mcp.dll"]

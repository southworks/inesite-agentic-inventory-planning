FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["backend/GrokInventoryAndTrend.Api/GrokInventoryAndTrend.Api.csproj", "backend/GrokInventoryAndTrend.Api/"]
RUN dotnet restore "backend/GrokInventoryAndTrend.Api/GrokInventoryAndTrend.Api.csproj"
COPY backend/GrokInventoryAndTrend.Api/ backend/GrokInventoryAndTrend.Api/
COPY dataset-seed/ dataset-seed/
WORKDIR /src/backend/GrokInventoryAndTrend.Api
RUN dotnet publish "GrokInventoryAndTrend.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /src/dataset-seed /app/dataset-seed
ENV Dataset__RootPath=/app/dataset-seed
ENTRYPOINT ["dotnet", "GrokInventoryAndTrend.Api.dll"]

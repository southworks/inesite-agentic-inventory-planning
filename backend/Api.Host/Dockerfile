FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["backend/Api.Host/InventoryPlanning.Api.Host.csproj", "backend/Api.Host/"]
RUN dotnet restore "backend/Api.Host/InventoryPlanning.Api.Host.csproj"
COPY backend/Api.Host/ backend/Api.Host/
WORKDIR /src/backend/Api.Host
RUN dotnet publish "InventoryPlanning.Api.Host.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "InventoryPlanning.Api.Host.dll"]

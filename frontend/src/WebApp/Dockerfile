FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY frontend/src/WebApp/InventoryAndTrend.WebApp.csproj frontend/src/WebApp/
RUN dotnet restore frontend/src/WebApp/InventoryAndTrend.WebApp.csproj
COPY frontend/src/WebApp/ frontend/src/WebApp/
COPY dataset-seed/ dataset-seed/
WORKDIR /src/frontend/src/WebApp
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /src/dataset-seed /app/dataset-seed
ENV DatasetSeed__RootPath=/app/dataset-seed
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "InventoryAndTrend.WebApp.dll"]

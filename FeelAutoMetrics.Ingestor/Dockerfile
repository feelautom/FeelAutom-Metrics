# Dockerfile pour FeelAutoMetrics.Ingestor
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["FeelAutoMetrics.Ingestor/FeelAutoMetrics.Ingestor.csproj", "FeelAutoMetrics.Ingestor/"]
COPY ["FeelAutoMetrics.Infrastructure/FeelAutoMetrics.Infrastructure.csproj", "FeelAutoMetrics.Infrastructure/"]
COPY ["FeelAutoMetrics.Shared/FeelAutoMetrics.Shared.csproj", "FeelAutoMetrics.Shared/"]
RUN dotnet restore "FeelAutoMetrics.Ingestor/FeelAutoMetrics.Ingestor.csproj"
COPY . .
WORKDIR "/src/FeelAutoMetrics.Ingestor"
RUN dotnet build "FeelAutoMetrics.Ingestor.csproj" -c Release -o /app/build
RUN dotnet publish "FeelAutoMetrics.Ingestor.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "FeelAutoMetrics.Ingestor.dll"]

# Dockerfile pour FeelAutoMetrics.Dashboard
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["FeelAutoMetrics.Dashboard/FeelAutoMetrics.Dashboard.csproj", "FeelAutoMetrics.Dashboard/"]
COPY ["FeelAutoMetrics.Infrastructure/FeelAutoMetrics.Infrastructure.csproj", "FeelAutoMetrics.Infrastructure/"]
COPY ["FeelAutoMetrics.Shared/FeelAutoMetrics.Shared.csproj", "FeelAutoMetrics.Shared/"]
RUN dotnet restore "FeelAutoMetrics.Dashboard/FeelAutoMetrics.Dashboard.csproj"
COPY . .
WORKDIR "/src/FeelAutoMetrics.Dashboard"
RUN dotnet build "FeelAutoMetrics.Dashboard.csproj" -c Release -o /app/build
RUN dotnet publish "FeelAutoMetrics.Dashboard.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "FeelAutoMetrics.Dashboard.dll"]

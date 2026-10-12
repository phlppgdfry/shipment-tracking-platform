FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["ShipmentTracking.sln", "./"]
COPY ["NuGet.Config", "./"]
COPY ["src/Domain/Domain.csproj", "src/Domain/"]
COPY ["src/Application/Application.csproj", "src/Application/"]
COPY ["src/Infrastructure/Infrastructure.csproj", "src/Infrastructure/"]
COPY ["src/WebAPI/WebAPI.csproj", "src/WebAPI/"]
COPY ["artifacts/feed/", "artifacts/feed/"]
RUN dotnet restore "src/WebAPI/WebAPI.csproj"
COPY . .
RUN dotnet publish "src/WebAPI/WebAPI.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
# The official .NET runtime image ships the unprivileged `app` user.
RUN mkdir -p /tmp/shipment-tracking/documents && chown -R app:app /tmp/shipment-tracking
USER app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=90s --retries=5 CMD curl --fail --silent http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "WebAPI.dll"]

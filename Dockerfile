FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY . .
RUN dotnet restore Alxarafe.sln
RUN dotnet publish src/Alxarafe.Host/Alxarafe.Host.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Alxarafe.Host.dll"]

FROM build AS development
WORKDIR /workspace
CMD ["dotnet", "watch", "--project", "src/Alxarafe.Host/Alxarafe.Host.csproj", "run", "--urls", "http://0.0.0.0:8080"]

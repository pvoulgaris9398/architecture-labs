FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-env
WORKDIR /app

# Copy the project file first and restore NuGet packages (allows Docker layer caching)
COPY *.csproj ./
RUN dotnet restore

# Copy the rest of the application files and compile the production release
COPY . ./
RUN dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build-env /app/out .
# Copy the database seed script so the API can run it on startup
COPY --from=build-env /app/seed.sql ./seed.sql

# Expose ports for handling traffic and exporting metrics
EXPOSE 8080

# https://andrewlock.net/exploring-the-dotnet-8-preview-updates-to-docker-images-in-dotnet-8/
# 👇 set to use the non-root USER here
USER $APP_UID

# Launch the compiled API entrypoint
ENTRYPOINT ["dotnet", "PoolMonitoringApi.dll"]


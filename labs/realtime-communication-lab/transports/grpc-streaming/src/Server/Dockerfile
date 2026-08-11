FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Protos/realtime.proto Protos/realtime.proto
COPY src/Server/Server.csproj src/Server/Server.csproj
RUN dotnet restore src/Server/Server.csproj
COPY src/Server src/Server
RUN dotnet publish src/Server/Server.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
EXPOSE 8081
ENTRYPOINT ["dotnet", "Server.dll"]

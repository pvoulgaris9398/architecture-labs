FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Protos/", "./Protos/"]
COPY ["DotnetApiGateway/DotnetApiGateway.csproj", "./"]
RUN dotnet restore "DotnetApiGateway.csproj"
COPY . .
RUN dotnet publish "DotnetApiGateway/DotnetApiGateway.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DotnetApiGateway.dll"]

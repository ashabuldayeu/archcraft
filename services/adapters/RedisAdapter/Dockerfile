FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Adapters.Contracts/Adapters.Contracts.csproj Adapters.Contracts/
COPY RedisAdapter/RedisAdapter.csproj RedisAdapter/
RUN dotnet restore RedisAdapter/RedisAdapter.csproj

COPY Adapters.Contracts/ Adapters.Contracts/
COPY RedisAdapter/ RedisAdapter/
RUN dotnet publish RedisAdapter/RedisAdapter.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "RedisAdapter.dll"]

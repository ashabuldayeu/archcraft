FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Adapters.Contracts/Adapters.Contracts.csproj Adapters.Contracts/
COPY PgAdapter/PgAdapter.csproj PgAdapter/
RUN dotnet restore PgAdapter/PgAdapter.csproj

COPY Adapters.Contracts/ Adapters.Contracts/
COPY PgAdapter/ PgAdapter/
RUN dotnet publish PgAdapter/PgAdapter.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "PgAdapter.dll"]

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY adapters/Adapters.Contracts/Adapters.Contracts.csproj adapters/Adapters.Contracts/
COPY synthetic/SynteticApi.csproj synthetic/
RUN dotnet restore synthetic/SynteticApi.csproj

COPY adapters/Adapters.Contracts/ adapters/Adapters.Contracts/
COPY synthetic/ synthetic/
RUN dotnet publish synthetic/SynteticApi.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "SynteticApi.dll"]

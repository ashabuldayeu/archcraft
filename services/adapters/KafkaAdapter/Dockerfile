FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Adapters.Contracts/Adapters.Contracts.csproj Adapters.Contracts/
COPY KafkaAdapter/KafkaAdapter.csproj KafkaAdapter/
RUN dotnet restore KafkaAdapter/KafkaAdapter.csproj

COPY Adapters.Contracts/ Adapters.Contracts/
COPY KafkaAdapter/ KafkaAdapter/
RUN dotnet publish KafkaAdapter/KafkaAdapter.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "KafkaAdapter.dll"]

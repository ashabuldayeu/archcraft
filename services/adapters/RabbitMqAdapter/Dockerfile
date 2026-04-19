FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Adapters.Contracts/Adapters.Contracts.csproj Adapters.Contracts/
COPY RabbitMqAdapter/RabbitMqAdapter.csproj RabbitMqAdapter/
RUN dotnet restore RabbitMqAdapter/RabbitMqAdapter.csproj

COPY Adapters.Contracts/ Adapters.Contracts/
COPY RabbitMqAdapter/ RabbitMqAdapter/
RUN dotnet publish RabbitMqAdapter/RabbitMqAdapter.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "RabbitMqAdapter.dll"]

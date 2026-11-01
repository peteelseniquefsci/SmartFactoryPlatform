# Smart Factory Platform web app. Build: docker compose up --build (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY SmartFactoryPlatform.sln ./
COPY src/SmartFactory.Core/SmartFactory.Core.csproj src/SmartFactory.Core/
COPY src/SmartFactory.Performance/SmartFactory.Performance.csproj src/SmartFactory.Performance/
COPY src/SmartFactory.Infrastructure/SmartFactory.Infrastructure.csproj src/SmartFactory.Infrastructure/
COPY src/SmartFactory.Web/SmartFactory.Web.csproj src/SmartFactory.Web/
RUN dotnet restore src/SmartFactory.Web/SmartFactory.Web.csproj
COPY src/ src/
RUN dotnet publish src/SmartFactory.Web/SmartFactory.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "SmartFactory.Web.dll"]

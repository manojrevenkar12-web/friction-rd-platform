FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props nuget.config ./
COPY src ./src
RUN dotnet publish src/Rdo.Api/Rdo.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
ENV RDO_ROOT=/app ASPNETCORE_URLS=http://+:8080
COPY --from=build /app .
COPY config ./config
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Rdo.Api.dll"]

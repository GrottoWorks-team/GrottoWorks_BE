FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER app
WORKDIR /app
EXPOSE 8080
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Services/NotificationAudit/NotificationAudit.Api/NotificationAudit.Api.csproj", "Services/NotificationAudit/NotificationAudit.Api/"]
COPY ["Services/NotificationAudit/NotificationAudit.Application/NotificationAudit.Application.csproj", "Services/NotificationAudit/NotificationAudit.Application/"]
COPY ["Services/NotificationAudit/NotificationAudit.Infrastructure/NotificationAudit.Infrastructure.csproj", "Services/NotificationAudit/NotificationAudit.Infrastructure/"]
COPY ["Services/NotificationAudit/NotificationAudit.Domain/NotificationAudit.Domain.csproj", "Services/NotificationAudit/NotificationAudit.Domain/"]
COPY ["Services/NotificationAudit/NotificationAudit.Contracts/NotificationAudit.Contracts.csproj", "Services/NotificationAudit/NotificationAudit.Contracts/"]
COPY ["BuildingBlocks/BuildingBlocks.Web/BuildingBlocks.Web.csproj", "BuildingBlocks/BuildingBlocks.Web/"]
COPY ["BuildingBlocks/BuildingBlocks.Security/BuildingBlocks.Security.csproj", "BuildingBlocks/BuildingBlocks.Security/"]
RUN dotnet restore "./Services/NotificationAudit/NotificationAudit.Api/NotificationAudit.Api.csproj"
COPY . .
WORKDIR "/src/Services/NotificationAudit/NotificationAudit.Api"
RUN dotnet build "./NotificationAudit.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./NotificationAudit.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "NotificationAudit.Api.dll"]

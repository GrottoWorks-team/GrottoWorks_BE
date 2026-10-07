#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER app
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Services/ParishCoordination/ParishCoordination.Api/ParishCoordination.Api.csproj", "Services/ParishCoordination/ParishCoordination.Api/"]
COPY ["Services/ParishCoordination/ParishCoordination.Application/ParishCoordination.Application.csproj", "Services/ParishCoordination/ParishCoordination.Application/"]
COPY ["Services/ParishCoordination/ParishCoordination.Infrastructure/ParishCoordination.Infrastructure.csproj", "Services/ParishCoordination/ParishCoordination.Infrastructure/"]
COPY ["Services/ParishCoordination/ParishCoordination.Domain/ParishCoordination.Domain.csproj", "Services/ParishCoordination/ParishCoordination.Domain/"]
COPY ["BuildingBlocks/BuildingBlocks.Web/BuildingBlocks.Web.csproj", "BuildingBlocks/BuildingBlocks.Web/"]
RUN dotnet restore "./Services/ParishCoordination/ParishCoordination.Api/./ParishCoordination.Api.csproj"
COPY . .
WORKDIR "/src/Services/ParishCoordination/ParishCoordination.Api"
RUN dotnet build "./ParishCoordination.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./ParishCoordination.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ParishCoordination.Api.dll"]

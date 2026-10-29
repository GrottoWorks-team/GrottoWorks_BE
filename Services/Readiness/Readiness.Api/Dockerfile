#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER app
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Services/Readiness/Readiness.Api/Readiness.Api.csproj", "Services/Readiness/Readiness.Api/"]
COPY ["Services/Readiness/Readiness.Application/Readiness.Application.csproj", "Services/Readiness/Readiness.Application/"]
COPY ["Services/Readiness/Readiness.Infrastructure/Readiness.Infrastructure.csproj", "Services/Readiness/Readiness.Infrastructure/"]
COPY ["Services/Readiness/Readiness.Domain/Readiness.Domain.csproj", "Services/Readiness/Readiness.Domain/"]
COPY ["Services/Readiness/Readiness.Contracts/Readiness.Contracts.csproj", "Services/Readiness/Readiness.Contracts/"]
COPY ["BuildingBlocks/BuildingBlocks.Web/BuildingBlocks.Web.csproj", "BuildingBlocks/BuildingBlocks.Web/"]
COPY ["BuildingBlocks/BuildingBlocks.Security/BuildingBlocks.Security.csproj", "BuildingBlocks/BuildingBlocks.Security/"]
RUN dotnet restore "./Services/Readiness/Readiness.Api/./Readiness.Api.csproj"
COPY . .
WORKDIR "/src/Services/Readiness/Readiness.Api"
RUN dotnet build "./Readiness.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Readiness.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Readiness.Api.dll"]

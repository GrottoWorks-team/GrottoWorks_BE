#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER app
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Services/Reporting/Reporting.Api/Reporting.Api.csproj", "Services/Reporting/Reporting.Api/"]
COPY ["Services/Reporting/Reporting.Application/Reporting.Application.csproj", "Services/Reporting/Reporting.Application/"]
COPY ["Services/Reporting/Reporting.Infrastructure/Reporting.Infrastructure.csproj", "Services/Reporting/Reporting.Infrastructure/"]
COPY ["Services/Reporting/Reporting.Domain/Reporting.Domain.csproj", "Services/Reporting/Reporting.Domain/"]
COPY ["Services/Reporting/Reporting.Contracts/Reporting.Contracts.csproj", "Services/Reporting/Reporting.Contracts/"]
COPY ["BuildingBlocks/BuildingBlocks.Web/BuildingBlocks.Web.csproj", "BuildingBlocks/BuildingBlocks.Web/"]
COPY ["BuildingBlocks/BuildingBlocks.Security/BuildingBlocks.Security.csproj", "BuildingBlocks/BuildingBlocks.Security/"]
RUN dotnet restore "./Services/Reporting/Reporting.Api/./Reporting.Api.csproj"
COPY . .
WORKDIR "/src/Services/Reporting/Reporting.Api"
RUN dotnet build "./Reporting.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Reporting.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Reporting.Api.dll"]

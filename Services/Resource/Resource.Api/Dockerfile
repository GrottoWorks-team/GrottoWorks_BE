#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER app
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Services/Resource/Resource.Api/Resource.Api.csproj", "Services/Resource/Resource.Api/"]
COPY ["Services/Resource/Resource.Application/Resource.Application.csproj", "Services/Resource/Resource.Application/"]
COPY ["Services/Resource/Resource.Infrastructure/Resource.Infrastructure.csproj", "Services/Resource/Resource.Infrastructure/"]
COPY ["Services/Resource/Resource.Domain/Resource.Domain.csproj", "Services/Resource/Resource.Domain/"]
COPY ["Services/Resource/Resource.Contracts/Resource.Contracts.csproj", "Services/Resource/Resource.Contracts/"]
COPY ["BuildingBlocks/BuildingBlocks.Web/BuildingBlocks.Web.csproj", "BuildingBlocks/BuildingBlocks.Web/"]
COPY ["BuildingBlocks/BuildingBlocks.Security/BuildingBlocks.Security.csproj", "BuildingBlocks/BuildingBlocks.Security/"]
RUN dotnet restore "./Services/Resource/Resource.Api/./Resource.Api.csproj"
COPY . .
WORKDIR "/src/Services/Resource/Resource.Api"
RUN dotnet build "./Resource.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Resource.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Resource.Api.dll"]

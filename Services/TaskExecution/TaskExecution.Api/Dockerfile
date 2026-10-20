FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER app
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Services/TaskExecution/TaskExecution.Api/TaskExecution.Api.csproj", "Services/TaskExecution/TaskExecution.Api/"]
COPY ["Services/TaskExecution/TaskExecution.Application/TaskExecution.Application.csproj", "Services/TaskExecution/TaskExecution.Application/"]
COPY ["Services/TaskExecution/TaskExecution.Infrastructure/TaskExecution.Infrastructure.csproj", "Services/TaskExecution/TaskExecution.Infrastructure/"]
COPY ["Services/TaskExecution/TaskExecution.Domain/TaskExecution.Domain.csproj", "Services/TaskExecution/TaskExecution.Domain/"]
COPY ["Services/TaskExecution/TaskExecution.Contracts/TaskExecution.Contracts.csproj", "Services/TaskExecution/TaskExecution.Contracts/"]
COPY ["BuildingBlocks/BuildingBlocks.Web/BuildingBlocks.Web.csproj", "BuildingBlocks/BuildingBlocks.Web/"]
COPY ["BuildingBlocks/BuildingBlocks.Security/BuildingBlocks.Security.csproj", "BuildingBlocks/BuildingBlocks.Security/"]
RUN dotnet restore "./Services/TaskExecution/TaskExecution.Api/TaskExecution.Api.csproj"
COPY . .
WORKDIR "/src/Services/TaskExecution/TaskExecution.Api"
RUN dotnet build "./TaskExecution.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./TaskExecution.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TaskExecution.Api.dll"]

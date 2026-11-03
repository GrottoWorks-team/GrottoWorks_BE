FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore Services/NotificationAudit/NotificationAudit.BackupWorker/NotificationAudit.BackupWorker.csproj
RUN dotnet publish Services/NotificationAudit/NotificationAudit.BackupWorker/NotificationAudit.BackupWorker.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
USER root
RUN apt-get update && apt-get install -y --no-install-recommends postgresql-client-17 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
ENTRYPOINT ["dotnet", "NotificationAudit.BackupWorker.dll"]

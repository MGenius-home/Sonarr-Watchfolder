# STAGE 1: Build Frontend
FROM node:20 AS frontend-build
WORKDIR /src
COPY package.json yarn.lock ./
RUN yarn install --frozen-lockfile
COPY . .
RUN yarn build

# STAGE 2: Build Backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src
COPY . .
RUN dotnet publish src/NzbDrone.Console/Sonarr.Console.csproj -f net10.0 -c Release -o /app/out -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false

# STAGE 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Sonarr runtime dependencies
RUN apt-get update && apt-get install -y libsqlite3-0 sqlite3 curl tzdata mediainfo
WORKDIR /app
COPY --from=backend-build /app/out .
COPY --from=frontend-build /src/_output/UI ./UI

# Volume configuration
VOLUME ["/config", "/tv", "/watch"]
EXPOSE 8989

ENTRYPOINT ["dotnet", "Sonarr.dll", "-nobrowser", "-data=/config"]

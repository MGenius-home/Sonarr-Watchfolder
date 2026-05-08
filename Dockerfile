# STAGE 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the frontend and build it (assuming node/yarn is needed if we wanted to build it properly, 
# but for this we'll just build the dotnet project. Note that in a real scenario we'd build the yarn frontend first.
# For simplicity and given the spec, we will just publish the .NET app)
COPY . .

# Publish the .NET application
RUN dotnet publish src/NzbDrone.Console/NzbDrone.Console.csproj -c Release -o /app/out

# STAGE 2: Runtime
FROM ubuntu:24.04
RUN apt-get update && apt-get install -y libicu-dev libsqlite3-0 curl
WORKDIR /app
COPY --from=build /app/out .

# Volume configuration
VOLUME ["/config", "/tv", "/watch"]
EXPOSE 8989

ENTRYPOINT ["dotnet", "Sonarr.dll", "-nobrowser", "-data=/config"]

# Stage 1: Build frontend
FROM node:20-alpine AS ui-build
WORKDIR /src
COPY package.json yarn.lock ./
RUN yarn install --frozen-lockfile
COPY . .
RUN yarn build

# Stage 2: Build backend
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend-build
WORKDIR /src
COPY global.json .editorconfig ./
COPY Logo/ ./Logo/
COPY src/ ./src/
RUN dotnet publish src/NzbDrone.Console/Lidarr.Console.csproj \
        --configuration Release \
        --framework net8.0 \
        --runtime linux-x64 \
        --self-contained false \
        --output /app \
        "-p:NoWarn=NU1902;CS1591" \
        -p:TreatWarningsAsErrors=false && \
    dotnet publish src/NzbDrone.Mono/Lidarr.Mono.csproj \
        --configuration Release \
        --framework net8.0 \
        --runtime linux-x64 \
        --self-contained false \
        --output /app \
        "-p:NoWarn=NU1902;CS1591" \
        -p:TreatWarningsAsErrors=false

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=backend-build /app ./
COPY --from=ui-build /src/_output/UI ./UI

RUN groupadd -g 1000 lidarr && \
    useradd -u 1000 -g lidarr -m lidarr && \
    mkdir -p /config /data && \
    chown -R lidarr:lidarr /config /data /app

USER lidarr

EXPOSE 8686
VOLUME ["/config", "/data"]

ENTRYPOINT ["/app/Lidarr", "-nobrowser", "-data=/config"]

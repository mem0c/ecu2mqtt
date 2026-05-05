
##############
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Install clang/zlib1g-dev dependencies for publishing to native
RUN apk add clang build-base zlib-dev

COPY . .

RUN dotnet publish "./ecu2mqtt/ecu2mqtt.csproj" \
    -c Release \
    -r linux-musl-x64 \
    -o /app/publish \
    /p:PublishAot=true \
    /p:SelfContained=true \
    /p:InvariantGlobalization=true \
    /p:StripSymbols=true \
    /p:OptimizationPreference=Size

##############    
# Runtime stage (small)
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["./ecu2mqtt"]


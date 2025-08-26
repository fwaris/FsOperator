# syntax=docker/dockerfile:1.7
FROM --platform=amd64 mcr.microsoft.com/dotnet/sdk:9.0.304-noble AS base
ENV DEBIAN_FRONTEND=noninteractive

# Tools + system CA store
RUN apt-get update && apt-get install -y --no-install-recommends \
      ca-certificates curl gnupg \
 && update-ca-certificates \
 && rm -rf /var/lib/apt/lists/*

COPY --from=ext cacerts.pem /usr/local/share/ca-certificates/cacerts.crt
RUN update-ca-certificates

# Add Microsoft GPG key
RUN curl https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor > microsoft.gpg && \
install -o root -g root -m 644 microsoft.gpg /etc/apt/trusted.gpg.d/ && \
rm microsoft.gpg

# Add the Microsoft Edge repository
RUN sh -c 'echo "deb [arch=amd64] https://packages.microsoft.com/repos/edge stable main" > /etc/apt/sources.list.d/microsoft-edge.list'

# Update and install Microsoft Edge stable
RUN apt-get update && apt-get install -y --no-install-recommends \
    microsoft-edge-stable && \
rm -rf /var/lib/apt/lists/*

FROM base AS build
WORKDIR /src
COPY src .
WORKDIR /src/RTTest

# RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
#     --mount=type=secret,id=nugetconfig \
# 	dotnet restore -r linux-x64
# RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
#     --mount=type=secret,id=nugetconfig \
# 	dotnet publish -c Release -o /app --no-restore

RUN dotnet publish -c:Release -o:/app 

FROM build AS final
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "RTTest.dll"]

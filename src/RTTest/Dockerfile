# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:9.0.304-noble AS base
ENV DEBIAN_FRONTEND=noninteractive

# Tools + system CA store
RUN apt-get update && apt-get install -y --no-install-recommends \
      ca-certificates curl gnupg \
 && update-ca-certificates \
 && rm -rf /var/lib/apt/lists/*

# copy zscaler certs - use 'docker ---build-context ext=<folder> ...' to build the container
COPY --from=ext *.crt /usr/local/share/ca-certificates/
RUN update-ca-certificates

# Add Microsoft GPG key
RUN curl https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor > microsoft.gpg && \
install -o root -g root -m 644 microsoft.gpg /etc/apt/trusted.gpg.d/ && \
rm microsoft.gpg

# Add the Microsoft Edge repository
RUN sh -c 'echo "deb [arch=amd64] https://packages.microsoft.com/repos/edge stable main" > /etc/apt/sources.list.d/microsoft-edge.list'

# Update and install Microsoft Edge stable and related tools
RUN apt-get update && apt-get install -y --no-install-recommends \
    tzdata libnss3-tools openssl \
    microsoft-edge-stable && \
rm -rf /var/lib/apt/lists/*

ENV HOME=/home

# Create a persistent Edge profile for Playwright and initialize NSS DB
RUN mkdir -p $HOME/.pki/nssdb && \
    certutil -d sql:$HOME/.pki/nssdb -N --empty-password && \
    \
    # Import every cert from /usr/local/share/ca-certificates into NSS
    # Trust flags: "C,," = trusted CA for TLS server auth
    for f in /usr/local/share/ca-certificates/*; do \
      # Only import files that actually contain a cert
      if grep -q "BEGIN CERTIFICATE" "$f" 2>/dev/null; then \
        nick="$(openssl x509 -in "$f" -noout -subject 2>/dev/null \
               | sed -e "s/^subject= *//" -e "s,/,_,g" -e "s, ,_,g")"; \
        [ -z "$nick" ] && nick="CorpCA-$(basename "$f")"; \
        # Add or update (-A adds; if duplicate, delete then add)
        certutil -d sql:$HOME/.pki/nssdb -A -t "C,," -n "$nick" -i "$f" || \
        (certutil -d sql:$HOME/.pki/nssdb -D -n "$nick" 2>/dev/null || true; \
         certutil -d sql:$HOME/.pki/nssdb -A -t "C,," -n "$nick" -i "$f"); \
      fi; \
    done && \
    \
    # Optional: show what’s in the NSS DB for debugging
    certutil -d sql:$HOME/.pki/nssdb -L


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

RUN dotnet publish -c:Release -o:/app -f net9.0




FROM build AS final
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "RTTest.dll"]

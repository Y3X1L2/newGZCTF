FROM docker:29-cli AS docker-cli

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine

ARG PUBLISH_PATH
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    LC_ALL=en_US.UTF-8
WORKDIR /app
RUN apk add --no-cache ca-certificates icu-libs iptables libgdiplus libpcap socat tzdata wget
COPY --from=docker-cli /usr/local/bin/docker /usr/local/bin/docker
COPY ${PUBLISH_PATH}/ ./
COPY scripts/validation/teamlab/docker/api-start.sh /opt/teamlab-p1/api-start.sh
RUN chmod 755 /opt/teamlab-p1/api-start.sh
ENTRYPOINT ["/bin/sh", "/opt/teamlab-p1/api-start.sh"]

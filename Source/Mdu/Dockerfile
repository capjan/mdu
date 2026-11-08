FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine-aot AS build

ARG TARGETARCH

RUN apk add --no-cache openssl-dev zlib-dev

WORKDIR /source
COPY . .

RUN case "$TARGETARCH" in \
        amd64) rid=linux-musl-x64 ;; \
        arm64) rid=linux-musl-arm64 ;; \
        *) echo "Unsupported architecture: $TARGETARCH" >&2; exit 1 ;; \
    esac \
    && dotnet publish Source/Mdu/Mdu.csproj \
        --configuration Release \
        --runtime "$rid" \
        --self-contained true \
        -p:PublishAot=true \
        -p:InvariantGlobalization=true \
        -p:OptimizationPreference=Size \
        --output /app/publish

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine AS runtime-base

RUN apk add --no-cache bash ca-certificates git libcrypto3 libssl3 \
    && mkdir -p /work \
    && chmod 1777 /work /tmp

ENV HOME=/tmp

COPY --from=build /app/publish/mdu /usr/local/bin/mdu
RUN ln -s /usr/local/bin/mdu /mdu
COPY --from=build /source/LICENSE /licenses/LICENSE
COPY --from=build /source/Source/StaticCode.Icons/THIRD-PARTY-NOTICES.md /licenses/THIRD-PARTY-NOTICES.md
COPY --from=build /source/Source/StaticCode.Icons/Res/dashboard-icons.LICENSE /licenses/dashboard-icons.LICENSE

WORKDIR /work

FROM runtime-base AS ci
ENTRYPOINT []
CMD ["/bin/sh"]

FROM runtime-base AS cli
ENTRYPOINT ["/usr/local/bin/mdu"]

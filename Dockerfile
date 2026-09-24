# syntax=docker/dockerfile:1
# check=skip=CopyIgnoredFile
#
# CopyIgnoredFile is skipped deliberately: .dockerignore re-excludes bin/ and obj/ beneath the
# deps/** allow-list that Dockerfile.Debug relies on, so the broad COPY instructions below
# legitimately step over ignored files.
#
# Multi-architecture image, built from a single Dockerfile. Structure follows
# https://github.com/f2calv/multi-arch-container-dotnet
#
# One image serves every role; the enabled feature set (CasCap__FeatureConfig__EnabledFeatures)
# selects which services are registered and started at runtime.
#
# ------------------------------------------------------------------------------
# Stage 1 of 2: build
#
# Pinned to $BUILDPLATFORM and CROSS-COMPILES to $TARGETPLATFORM; emulating the
# target under QEMU instead is typically 10-50x slower.
# ------------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY ["Directory.Build.props", "Directory.Packages.props", "global.json", "appsettings.json", "./"]

ARG WORKLOAD=CasCap.App.Server
ARG CONFIGURATION=Release

# -- Dependency layer ----------------------------------------------------------
# Cached until a csproj/props or package version changes. Copy every project manifest first
# (--parents preserves directory structure) so editing source (.cs) files reuses the cached
# restore. Restore is platform-agnostic, so keep it before ARG TARGETARCH to share it across
# architectures.
COPY --parents src/**/*.csproj ./
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet restore "src/$WORKLOAD/$WORKLOAD.csproj"

# -- Compile layer -------------------------------------------------------------
COPY . .

# buildx injects TARGETARCH/TARGETVARIANT automatically:
#   linux/amd64 -> amd64, linux/arm64 -> arm64, linux/arm/v7 -> arm + v7
# Concatenating the two gives a single flat token to switch on.
ARG TARGETARCH
ARG TARGETVARIANT
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked <<EOF
set -eux
# https://learn.microsoft.com/dotnet/core/rid-catalog
case "${TARGETARCH}${TARGETVARIANT}" in
    amd64) RID=linux-x64   ;;
    arm64) RID=linux-arm64 ;;
    armv7) RID=linux-arm   ;;
    *) echo "unsupported platform: linux/${TARGETARCH}/${TARGETVARIANT}" >&2; exit 1 ;;
esac
dotnet publish "src/$WORKLOAD/$WORKLOAD.csproj" -c "$CONFIGURATION" -o /app/publish -r "$RID" --self-contained false
mkdir -p /app/state
touch /app/state/.keep
EOF

# ------------------------------------------------------------------------------
# Stage 2 of 2: final
#
# No --platform override here, so buildx resolves the base image for
# $TARGETPLATFORM and the resulting image is genuinely native to the target.
#
# Chiseled is the smallest option and runs as a non-root user by default. It has no shell and
# no package manager, which is affordable here because the gateway needs no native runtime
# dependency. Reaching for a fuller base image means one has been introduced - say why.
# ------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app

COPY --link --from=build /app/publish .
COPY --link --from=build --chown=$APP_UID:$APP_UID /app/state /var/lib/signalizr

# -- Provenance ----------------------------------------------------------------
# Supplied by the CI workflow (.github/workflows/ci.yml) or by build.ps1/build.sh.
ARG GIT_REPOSITORY=n/a
ENV GIT_REPOSITORY=$GIT_REPOSITORY
ARG GIT_BRANCH=n/a
ENV GIT_BRANCH=$GIT_BRANCH
ARG GIT_COMMIT=n/a
ENV GIT_COMMIT=$GIT_COMMIT
ARG GIT_TAG=n/a
ENV GIT_TAG=$GIT_TAG

ARG GITHUB_WORKFLOW=n/a
ENV GITHUB_WORKFLOW=$GITHUB_WORKFLOW
ARG GITHUB_RUN_ID=0
ENV GITHUB_RUN_ID=$GITHUB_RUN_ID
ARG GITHUB_RUN_NUMBER=0
ENV GITHUB_RUN_NUMBER=$GITHUB_RUN_NUMBER

EXPOSE 8080

# https://github.com/opencontainers/image-spec/blob/main/annotations.md
LABEL org.opencontainers.image.title="signalizr" \
    org.opencontainers.image.description="Signal Messenger gateway - one owned account, named channels, REST send and gRPC inbound fan-out" \
    org.opencontainers.image.source="https://github.com/f2calv/signalizr" \
    org.opencontainers.image.licenses="Unlicense" \
    org.opencontainers.image.version="$GIT_TAG" \
    org.opencontainers.image.revision="$GIT_COMMIT"

USER $APP_UID

# The exec form makes dotnet PID 1 so it receives SIGTERM for a clean shutdown. The assembly is
# named literally because the chiseled image has no shell to expand a variable, and because this
# repository publishes exactly one workload.
ENTRYPOINT ["dotnet", "CasCap.App.Server.dll"]

# 0003. Chiseled, non-root runtime images with a read-only root file system

- **Status:** Accepted
- **Date:** 2026-11-23

## Context

Container images are the unit we deploy and the unit security scanners judge us by. A full Debian-based
`aspnet` image carries a shell, a package manager and dozens of packages the service never uses; each one shows
up in vulnerability reports and each one is something an attacker could use after a remote code execution bug.

Kubernetes clusters we deploy to enforce the *restricted* Pod Security Standard more and more often: non-root,
no privilege escalation, dropped capabilities, seccomp.

## Decision

- Multi-stage `Dockerfile`: the SDK image builds and publishes; the runtime stage is
  `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`. It has no shell and no package manager and runs as the
  non-root `app` user (uid 1654) by default.
- The restore step is its own layer, keyed on project and props files, so code changes do not re-download
  packages.
- The service is built with `InvariantGlobalization`, because chiseled images ship without ICU. Services that
  need culture data use the `-extra` variant and say so in an ADR.
- Containers run with a **read-only root file system**; `/tmp` is the only writable path (an `emptyDir` in the
  Helm chart, a `tmpfs` in compose). Capabilities are dropped, privilege escalation is off, seccomp is
  `RuntimeDefault`.
- `appsettings.Development.json` is not published, so local-only settings never end up in an image.
- CI builds the image for every deep combination and runs the compose stack with the same restrictions.

## Consequences

- Smaller image and a much shorter CVE list; nothing to `apt-get` in production.
- No `docker exec ... sh` for debugging. Use `kubectl debug` with an ephemeral container, logs, traces and
  metrics instead. This is the point, but it surprises people the first time.
- Health checks cannot use `curl` inside the container; the orchestrator probes HTTP endpoints from outside.
- Anything that writes to disk must write to `/tmp` (or a mounted volume). Libraries that assume a writable home
  directory need configuration.

## Alternatives considered

- **Regular `aspnet:10.0` image as non-root.** Works, but keeps the shell and packages. Rejected as the default.
- **Native AOT on `runtime-deps` chiseled.** Smallest and fastest start, but EF Core, the configuration binder
  and parts of OpenTelemetry still need trimming work. Revisit when the default dependencies are AOT-clean.
- **Distroless images from other vendors.** Comparable, but the Microsoft chiseled images are patched on the
  .NET servicing cadence we already follow.

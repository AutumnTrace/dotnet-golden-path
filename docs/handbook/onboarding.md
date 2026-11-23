# Onboarding: your first week

The goal of week one is to ship a small change to production through the normal process, not to understand
everything. You will learn the rest by doing.

## Day 1: tools and access

- Install the .NET 10 SDK, Docker (or Podman with the Docker CLI), `kubectl`, `helm` and your editor of choice.
  Rider, Visual Studio and VS Code with C# Dev Kit all work; the repos carry `.editorconfig`, so formatting is the
  same everywhere.
- Ask your onboarding buddy for access to: the GitHub organisation, the container registry, the observability
  backend (traces, metrics, logs) and the on-call tool. You need read access to production telemetry from day one.
- Read the [handbook index](README.md) and the five [ADRs](../adr). It takes an hour and saves weeks.

## Day 2: generate a service and run it

```bash
dotnet new install GoldenPath.Templates.<version>.nupkg   # from the latest release
dotnet new goldenpath-api -n Sandbox
cd Sandbox
dotnet run --project src/Sandbox.Api
```

No Docker needed: the database and the message bus have in-memory stand-ins in Development. Then:

1. `curl -X POST localhost:5080/dev/tokens` and place an order with the token (see the service README).
2. Run `dotnet test --filter "Category!=Integration"` and read the test names: they describe the behaviour.
3. Run `docker compose up --build`, open the Aspire dashboard on port 18888 and find the trace of the order you
   just placed. Follow it from the HTTP span to the database span.

## Day 3: read a real service

Pick one service your team owns. With the [service anatomy](service-anatomy.md) page open:

- Find its slices in `Features/`. For one use case, trace the request from the endpoint mapping to the handler,
  the domain rule and the store.
- Open its dashboard and find the same use case in production traces. Note p50 and p99 latency.
- Read its last three merged pull requests and their reviews. That is the fastest way to learn the team's taste.

## Day 4 to 5: ship something

Take a starter issue (labelled `good first issue`): usually a validation rule, an error message, a missing test
or a dashboard panel.

1. Branch, change, add or update tests. Run the full test suite locally.
2. Open a pull request using the template. Fill in **Testing** with what you actually ran.
3. Respond to review, merge, and watch the deployment: CI, then the rollout, then the service's dashboard.

## What good looks like after a month

- You can explain what each of your team's services does, what it depends on, and how it fails.
- You have reviewed other people's pull requests, not just had yours reviewed.
- You have been on call shadowing at least once and know where the runbooks are.
- You have proposed one improvement to this handbook. Everyone who joins finds something we got used to.

## Ask early

Nobody expects you to know the codebase. Everybody expects you to ask after 30 minutes of being stuck. Ask in the
team channel rather than in private messages, so the answer helps the next person too.

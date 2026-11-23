# Contributing

Thanks for helping improve the golden path. Changes here end up in every service generated from the
template, so the bar is "would we want this in all of them?".

## Development loop

```bash
# Fast: instantiate every option combination and check the generated files (no build).
dotnet test

# Thorough: pack, install into a throwaway hive, build and unit-test each combination.
eng/verify-combinations.sh            # all combinations
eng/verify-combinations.sh both       # only combinations whose arguments contain "both"
```

Neither command touches your real `dotnet new` configuration. To try the template by hand:

```bash
dotnet new install ./templates/goldenpath-api --debug:custom-hive .scratch/hive
dotnet new goldenpath-api --debug:custom-hive .scratch/hive -n Orders -o .scratch/Orders
```

`.scratch/` is git-ignored.

## Editing the template

- Template content lives in `templates/goldenpath-api`. Placeholder names are `Company.Service` (projects and
  namespaces) and `company-service` (the kebab-case slug used for the service name, image and chart).
- Option-specific content uses template-engine conditionals: `#if (UseOtlp)` in C#, `<!--#if (UseOtlp) -->` in
  XML and Markdown, `//#if` in JSON and `#if` in YAML. Whole files are excluded in `template.json` `sources.modifiers`.
- A new option or choice must be added to `template.json`, `tests/GoldenPath.Templates.Tests/TemplateOptions.cs`,
  `eng/verify-combinations.sh`, the CI matrix and the README options table. The template tests fail until it is.
- The generated code builds with `TreatWarningsAsErrors` and the `latest-recommended` analyzers. Fix warnings;
  suppress only with a comment that says why.

## Pull requests

- One coherent change per PR, Conventional Commit titles (`feat:`, `fix:`, `docs:`, `test:`, `ci:`, `chore:`).
- Update `CHANGELOG.md` under **Unreleased**.
- If the change alters a convention, add or update an ADR in `docs/adr` and the relevant handbook page.

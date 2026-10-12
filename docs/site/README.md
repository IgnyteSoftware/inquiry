# Inquiry docs site

DocFX project that renders the user-facing documentation for Inquiry. The site is published to **GitHub Pages** at <https://ignytesoftware.github.io/inquiry/> by [`docs.yml`](../../.github/workflows/docs.yml) on every push to `main`; the steps below preview it locally.

## One-time setup

Install DocFX as a global .NET tool:

```bash
dotnet tool install -g docfx
```

## Build + preview locally

From the repo root:

```bash
docfx docs/site/docfx.json --serve
```

DocFX builds the site to `docs/site/_site/`, then serves it at <http://localhost:8080>. Edits to any markdown trigger a rebuild on the next page load — keep the command running and refresh your browser.

## Build only (no server)

```bash
docfx docs/site/docfx.json
```

Output lands in `docs/site/_site/`. Open `docs/site/_site/index.html` directly in a browser, or serve the directory with any static-file server.

The required CI `docs` job runs `docfx docs/site/docfx.json --warningsAsErrors` and an offline link
check on every pull request, so any DocFX warning or broken local link blocks the merge.

## Executable recipes

A `<!-- recipe: name/File.cs -->` comment directly above a code fence marks that fence as a file of an
executable recipe. The package consumer runner extracts these files, builds them against the
candidate packages, and runs them; see [Contributing](develop/contributing.md#clean-package-consumers).
Keep each comment on the line immediately above its fence.

## What's in here

- **`docfx.json`** — the build config. Lists the public packages whose XML doc comments become the API reference.
- **`index.md`** — site landing page.
- **`toc.yml`** — top-level navigation.
- **`articles/`** — the hand-written conceptual content (getting started, how-it-works, features, providers, architecture).
- **`api/`** — DocFX's auto-generated reference metadata. `api/index.md` is hand-written and tracked; all `api/*.yml` files are regenerated each build and are gitignored.
- **`_site/`** — build output. Gitignored.

## Hosting

The site deploys to GitHub Pages via the `docs.yml` workflow (build → link-check → deploy). The
build invocation is host-agnostic static output, so the deploy mechanism can change without
touching the build.

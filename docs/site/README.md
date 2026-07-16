# Inquiry docs site

DocFX project that renders the user-facing documentation for Inquiry. While the repo is private and we haven't picked a hosting target, **the site is local-preview only** — run DocFX yourself to view it.

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

## What's in here

- **`docfx.json`** — the build config. Lists the public packages whose XML doc comments become the API reference.
- **`index.md`** — site landing page.
- **`toc.yml`** — top-level navigation.
- **`articles/`** — the hand-written conceptual content (getting started, how-it-works, features, providers, architecture).
- **`api/`** — DocFX's auto-generated reference metadata. `api/index.md` is hand-written and tracked; all `api/*.yml` files are regenerated each build and are gitignored.
- **`_site/`** — build output. Gitignored.

## Hosting options (when we're ready)

The site is a vanilla static-file output, so any static host works. Realistic paths for a private repo:

- **Cloudflare Pages** — free, supports private GitHub repos, has Cloudflare Access for SSO/PIN gating (free for ≤50 users). Best free option.
- **Azure Static Web Apps** — free tier, built-in AAD/Microsoft/GitHub auth, fits the .NET ecosystem.
- **GitHub Pages** — requires GitHub Pro/Team/Enterprise for private-repo Pages.
- **Public mirror repo** — push the built `_site/` to a separate public repo and host it via GitHub Pages there. Docs become public; source stays private.

When we pick one, the build invocation stays the same — only the deploy mechanism changes.

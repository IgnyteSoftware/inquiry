# Security policy

## Supported versions

| Version         | Supported                                        |
|-----------------|--------------------------------------------------|
| 1.0.0-preview.x | Fixes ship only in the latest preview            |
| 1.0.x           | Supported once the stable release ships          |

No stable release has shipped yet; `1.0.0-preview` packages are published on nuget.org and receive fixes only in the latest preview.

Once released, only the latest patch of each supported major.minor receives security fixes.

## Reporting a vulnerability

**Do not open a public issue for security vulnerabilities.**

Report vulnerabilities through [GitHub private vulnerability reporting](https://github.com/IgnyteSoftware/inquiry/security/advisories/new). You will receive an acknowledgement within 72 hours and a resolution timeline within 14 days.

If private advisory reporting is unavailable, email the maintainer directly at jake.overstreet@icloud.com with the subject line `[SECURITY] Inquiry vulnerability report`.

Include:

- A description of the vulnerability and its impact.
- Steps to reproduce or a minimal proof of concept.
- The affected version(s) and component(s).

## Disclosure

Inquiry follows coordinated disclosure. Fixes are released as patch versions with a GitHub security advisory. The advisory credits the reporter unless they request otherwise.

## Scope

Inquiry is a compile-time SQL micro-ORM. Security-relevant areas include:

- **SQL injection** — the source generator emits parameterized queries; any path that interpolates user input into SQL text is a vulnerability.
- **Supply chain** — NuGet package integrity, dependency provenance, and analyzer DLL authenticity.
- **Deserialization** — JSON column mapping and value converter pipelines.
- **Information disclosure** — connection strings, parameter values, or internal state leaked through diagnostics, logs, or error messages.

# Security policy

PulseWatch sends HTTP requests to URLs chosen by its users, so we take security reports seriously, especially
anything that lets a user reach addresses they shouldn't (SSRF), read another user's data, or take over accounts.

## Supported versions

| Version | Supported |
|---|---|
| Latest release (`main`) | ✅ |
| Older releases | ❌ Upgrade to the latest release |

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Report privately through GitHub: open the repository's **Security** tab and choose **Report a vulnerability**
([GitHub's guide](https://docs.github.com/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability)).

Please include:

- what an attacker can do, and what access they need (anonymous, signed-in user, owner of a monitored site);
- steps to reproduce, ideally against a local `docker compose` setup;
- the version or commit you tested;
- any ideas for a fix.

What to expect:

- an acknowledgement within **5 working days**;
- an assessment and a plan within **14 days**;
- a fix released as soon as practical, and credit in the release notes if you'd like it.

## Scope

In scope: the API, worker, web app, Docker images and the deployment files in this repository.

Out of scope:

- findings that need an attacker who already has shell or Docker access on the server;
- the residual risks listed in [docs/threat-model.md](docs/threat-model.md#6-residual-risks), unless you find a
  way around a control described there;
- volume-based denial of service against a self-hosted instance;
- missing security headers on pages that aren't served by PulseWatch (for example your reverse proxy's own error pages).

## Safe testing

Test against your own local instance. Don't run scans or SSRF payloads against instances you don't own, and never
point monitors at third-party internal networks.

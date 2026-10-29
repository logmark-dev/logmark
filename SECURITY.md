# Security

## Reporting a vulnerability

Report security issues privately to **security@logmark.dev**. Do not open a public issue for a suspected vulnerability - give the maintainer a chance to assess and, if needed, ship a fix before details are public.

Include what you found, how to reproduce it, and the affected platform and version (`logmark --version`). Expect an acknowledgment within the same weekly triage cadence described in [CONTRIBUTING.md](CONTRIBUTING.md); this is a solo-maintained project, not a security team with an SLA.

## Daemon network posture

`logmark serve` binds to loopback only (`127.0.0.1`, plus `::1` on macOS, where `localhost` resolves to IPv6 first) - it is never exposed to the network by default, and nothing in this codebase opens it to a non-loopback interface. It also refuses any request whose `Host` header is not `127.0.0.1:<port>` or `localhost:<port>`, so a web page cannot use DNS rebinding (pointing its own name at 127.0.0.1) to read your vault through the browser. Browsers cannot send the API's writes (`PUT`) cross-origin, because the daemon answers no CORS preflight. In the current API version (v0), that is the whole of the protection: any process on the same machine that can reach `127.0.0.1` can call the API. A bearer-token check (`Authorization: Bearer <token>`, gated by a `LOGMARK_DAEMON_TOKEN` environment variable) is reserved in the contract but not yet enforced - treat the daemon as trusting anything running locally until that lands. There is no built-in mechanism for exposing the daemon beyond localhost; doing so (a reverse proxy, a tunnel, a changed bind address) is an explicit choice you would have to make yourself, and is not a configuration this project recommends or supports out of the box.

## No telemetry

This build sends nothing anywhere on its own. No analytics, no crash reporting, no update check, no phone-home of any kind. The only network activity `logmark` initiates is what you explicitly ask for (for example, an MCP client's own transport, or a future release-channel update check you opt into). Your vault stays on your disk unless you move it there yourself.

## Scope

This policy covers the code in this repository: the kernel, daemon, CLI, MCP server, and cockpit. Vulnerabilities in the LogMark desktop app or LogMark's cloud services should also be reported to security@logmark.dev, but are triaged separately since they live in a different codebase.

# LogMark Engine

The memory substrate a person and their agents share - you own it, they compound it.

## What this build guarantees

- **Your vault is plain markdown you own.** Every entry is a dated, typed, attributable claim in a folder you control. No database, no export step.
- **Provenance by absence.** An entry with no `source:` line was written by a human. Every agent-written entry carries `source: agent:<name>`. You can audit every claim an agent added, in the raw file, with no tool.
- **Loopback only.** `logmark serve` and `logmark mcp` listen on 127.0.0.1 and stdio. There is no server that receives your data, and no telemetry. The daemon refuses requests whose Host header is not loopback (DNS rebinding); the test is `DaemonHostHeaderTests` in this repository's daemon test suite. See SECURITY.md.
- **The open tool list never shrinks.** 23 MCP tools and the matching CLI verbs ship here today; tools join this set over time and are never pulled back out.
- **Coexistence and exit.** Install the LogMark desktop app beside this build, or remove it, and your vault and agent wiring survive both moves. This is a tested contract, not a promise of intent. The open half lives in this repository; the install-order tests run in the desktop suite (details below).
- **AGPL-3.0 code, CC-BY-4.0 spec.** The vault format is [lm-vault](https://github.com/logmark-dev/lm-vault), specified in its own repository. The format is the contract; any tool may read or write it.

LogMark Engine is a local-first, markdown-native memory engine for people who work with AI agents. Everything you or an agent capture becomes a typed, dated, attributable markdown entry in a folder you control. No database to export, no vendor lock-in, no cloud dependency: the format is the product.

This repository is the open substrate: a kernel that parses, routes, stores, searches, lints, organizes, and exports vault entries; a headless daemon (`logmark serve`) that hosts a vault and exposes it over a local HTTP/SSE API; an MCP server (`logmark mcp`) so any MCP-capable agent can read and write your vault as a first-class tool; a CLI for scripting and terminal use; and a browser cockpit (embedded in the same binary) for viewing, reviewing, and configuring a vault by hand. Kernel, daemon, CLI, MCP server, and cockpit ship as one self-contained binary named `logmark`.

## Quickstart

Download the archive for your platform from the [releases page](https://github.com/logmark-dev/logmark/releases): `logmark-<version>-win-x64.zip`, `logmark-<version>-osx-arm64.tar.gz`, or `logmark-<version>-linux-x64.tar.gz`, plus the `SHA256SUMS` file. Verify the archive against its line in `SHA256SUMS`:

```
sha256sum -c SHA256SUMS --ignore-missing          # Linux
shasum -a 256 logmark-<version>-osx-arm64.tar.gz  # macOS: compare by eye
certutil -hashfile logmark-<version>-win-x64.zip SHA256   # Windows: compare by eye
```

The archive has no top-level folder: its files sit at the root, so extract it into a folder of its own. The binary is self-contained, with no runtime to install and no setup step:

```
mkdir logmark && tar -xzf logmark-<version>-linux-x64.tar.gz -C logmark && cd logmark   # Linux (osx-arm64 on macOS)
Expand-Archive logmark-<version>-win-x64.zip -DestinationPath logmark; cd logmark      # Windows PowerShell

./logmark --version       # macOS / Linux
.\logmark.exe --version   # Windows (PowerShell or cmd)
```

It prints `LogMark Engine <version>`. The commands below call it `logmark`; put the extracted folder on your PATH or use the full path to the binary. If the LogMark desktop app is installed on this machine, read [If the desktop app is installed](#if-the-desktop-app-is-installed) before you connect an agent.

Initialize a vault and start the daemon:

```
logmark vault init ./my-vault
logmark serve --vault ./my-vault
```

`logmark serve` hosts the vault, watches it for changes, and serves the cockpit and its HTTP/SSE API on loopback (`http://127.0.0.1:4747` by default) - open that address in a browser to view, ratify, and configure the vault.

Connect an agent over MCP with the built-in wiring command - it writes the client config for you, pointing at the right binary:

```
logmark connect claude-code --all --vault ./my-vault
```

(Frameworks: `claude-code`, `codex`, `gemini-cli`, `cursor`, `copilot-cli`.) `--all` installs the MCP wiring, the nine agent skills and the session hook; use `--mcp` alone to wire only the server. The skills teach the agent the loop: orient at the start of a session, capture as it works, checkpoint at the end.

Or wire a client by hand. Claude Code and Codex take a one-liner:

```
claude mcp add logmark -- /absolute/path/to/logmark mcp --vault-override /path/to/my-vault
codex mcp add logmark -- /absolute/path/to/logmark mcp --vault-override /path/to/my-vault
```

For any client, point its stdio MCP config at the `mcp` verb, using the **absolute path** to the binary. MCP clients spawn `command` directly with no shell, so a bare name relies on PATH resolution that many clients skip - and on Windows the npm-installed `logmark` is a `.cmd` shim that some clients cannot spawn at all:

```json
{
  "mcpServers": {
    "logmark": {
      "command": "/absolute/path/to/logmark",
      "args": ["mcp", "--vault-override", "/path/to/my-vault"]
    }
  }
}
```

The agent now has 23 read and write tools over your vault (the list is under [The tool-list guarantee](#the-tool-list-guarantee)).

### If the desktop app is installed

The desktop app includes the Engine, and LogMark agents share one LogMark memory: with the desktop app installed, agents use the desktop app's server for the one shared `logmark` connection, so connect agents from the desktop app. The Engine never creates the shared `logmark` entry on that machine: `connect --mcp` changes nothing, says why, and exits 3. Where the desktop app already connected an agent to the same vault, `connect` records the Engine as a fallback and exits 0, and a later `disconnect` removes only the Engine's ownership and leaves the connection in place. With a different `--vault` it exits 3 and prints the alternative with its flag (`--repoint` points every LogMark product at `./my-vault` by writing a `--vault-override` pin). To try the Engine on its own vault, use a machine or user account without the desktop app. On this machine, run the daemon and the CLI directly against that vault instead, and start the daemon with `--no-activevault` so it leaves the desktop app's active vault alone:

```
logmark serve --no-activevault --vault ./my-vault
```

## If you are an agent

You have been wired to a vault over MCP. Three calls orient you: `vault_summary` (what is here), `get_project_context` (one route's decisions, tasks, blocks), `get_routes` (where things go). Every result carries stable entry ids; `read_entry(id)` returns full detail any time.

Write etiquette: one claim per entry. Type it (`t:` task, `i:` idea, `b:` block, `d:` decision, `q:` question, `g:` gap). Route it with `+route` when it belongs to a project. Never rewrite yesterday's entry to change its meaning; add a new dated entry, and use `update_entry` only to advance lifecycle (resolve, freeze, dismiss). Your entries carry `source:` automatically. The vault's `vault.md` has a "How agents should help" section; read it first and follow it.

**Wiring details.** `--vault` is accepted before or after the verb, and the MCP server accepts `--vault` as an alias of `--vault-override`. To cohost with the LogMark desktop app, run `logmark serve --no-activevault`. To wire Claude Code at local scope for one project, run `logmark connect claude-code --mcp --scope local --project <dir>`. `connect` and `disconnect` exit 0 when they made the change or had nothing to do, 2 when a capability failed, 3 when they changed nothing because a choice is needed - for example when the requested vault differs from the one the shared entry already uses, or the entry belongs to another LogMark product (each alternative is printed with its flag, and `--json` lists them as `choices`) - and 64 on a usage error such as an unknown flag. `disconnect --all-products` removes a connection for every LogMark product. `disconnect --dry-run` shows what it would remove and writes nothing.

## Other ways to install

**npm** - The release candidate is on npm under the `rc` tag: `npm i -g @logmark/cli@rc`. From v0.1.0, `npm i -g @logmark/cli` installs the stable release and gives you the plain `logmark` command:

```
npm i -g @logmark/cli
```

Or run it without installing anything (one-shot form):

```
npx @logmark/cli vault init ./my-vault
```

Note the scope on that one-shot form: it is always `npx @logmark/cli`. Do not drop the scope - the unscoped `logmark` package on npm belongs to an unrelated third-party logging library, not to this project.

**Package managers** - winget (`LogmarkDev.LogmarkCLI`), a scoop bucket, and a Homebrew tap carry the same archives. Availability per release is listed on the releases page.

**Build from source** - requires the .NET SDK version pinned in `global.json` (the latest .NET 9 SDK satisfies it), plus Node 20+ if you want the cockpit UI baked in:

```
dotnet build logmark.sln          # engine + CLI + daemon + MCP server
npm install && npm run build      # cockpit SPA (optional)
dotnet build src/LogMark.Daemon   # re-embed the daemon with the cockpit
```

The binary lands at `src/LogMark.Daemon/bin/Debug/net8.0/logmark` (`logmark.exe` on Windows). Without the npm step it runs API-only - every verb works, `serve` just has no cockpit UI. See [CONTRIBUTING.md](CONTRIBUTING.md) for the development workflow.

## What is in this repo, and what is not

This repo is the kernel, the daemon, the CLI, the MCP server, and the cockpit - the sovereignty layer: format, parsing, routing, storage, and access. It is everything a stranger needs to audit and everything an agent needs to read and write a vault without asking anyone's permission.

Out of scope here, and consumers of this substrate rather than part of it: the [LogMark desktop app](https://logmark.dev/download) (closed source and freemium: free forever, with a superset of advanced but not essential tools and features, built on this same kernel - capture surface, presence, judgment features, cloud services), Ink (a hosted renderer for published vault content), the Telegram relay, and the mobile app. Issues about any of those belong at [logmark.dev](https://logmark.dev), not in this repository.

## The tool-list guarantee

The MCP and CLI surface in this repo is the kernel tool set: the tools that read or write vault markdown with no judgment attached - create, read, search, backfill, organize, route, tag, and session-track. Nothing paid or proprietary sits behind a flag in this code; features that require judgment (synthesis, digests, agent behaviors, private routes) live in closed producers that are simply absent from this build.

The commitment: the open build's tool list never shrinks. Tools join the kernel set as capabilities graduate from closed to open; none are ever pulled back out once shipped here. As of this release that set is these 23 MCP tools plus the matching CLI verbs (`capture`, `recent`, `search`, `tasks`, `blocks`, `decisions`, `routes`, `lint`, `vault init`, `connect`, `page write`/`page link`, and more):

| Group                  | Tools                                                                                                                   |
| ---------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| Orient                 | `vault_summary`, `get_project_context`, `get_routes`, `get_constitution`                                                |
| Read                   | `read_entry`, `list_recent`, `search`, `get_tasks`, `get_blocks`, `get_decisions`, `get_sessions`                       |
| Write                  | `create_entry`, `create_entry_with_subpage`, `write_page`, `backfill_entry`, `backfill_batch`                           |
| Lifecycle and organize | `update_entry`, `organize_entry`, `update_tags`, `update_routes`, `update_constitution`, `start_session`, `end_session` |

No tool in this set edits the words of an entry. `update_entry` advances lifecycle (complete, resolve, freeze, dismiss, deadline); `organize_entry` retypes, moves and retags, and records the change in the file.

## The coexistence guarantee

If you later install the LogMark desktop app alongside this build - or remove it again - your vault and your agent wiring survive both moves. Installing the desktop never re-seeds or migrates a vault this build created without honoring its format stamp, and never makes your agents double-write: both products register as providers of one shared `logmark` identity, and the most capable installed provider serves it. Uninstalling the desktop falls wiring back to this build without touching your wired configs. This is not a promise of intent; the OSS-first-then-desktop scenario and the fallback are part of a tested cross-product contract. Its open half (provider manifests, the hook shim, vault resolution, the format stamp) lives in this repository; the install-order and uninstall-fallback tests run in the LogMark desktop test suite, which is where the closed half lives.

## Naming

This open artifact is **LogMark Engine**. Its binary and CLI verb stay `logmark`, and so do the repository, the release archive names, and the `logmark` MCP server key agents are wired with. "LogMark" on its own is the desktop product - the flagship app, unqualified wherever users see it (its executable, its Store listing, its title bar). The LogMark desktop app includes the Engine. "Desktop app" is a plain descriptor used only where the two need to be told apart ("the LogMark desktop app"), never a product name on its own. Engine issues (this repo) are filed here; app issues go to logmark.dev.

## License

LogMark Engine is AGPL-3.0-only. See [LICENSE](LICENSE) and [NOTICE](NOTICE). Use it, change it, and run it for yourself or inside your company, at no cost. If you distribute a modified version, or let others use one over a network, the AGPL asks you to publish that version's source under the same license. If that does not fit (for example, shipping the Engine inside a closed product or service), a commercial license is available: contact legal@logmark.dev.

The LogMark name, the LogMark Engine name, and the LogMark emblem are trademarks of Denis Darkin. The AGPL-3.0 license covers the code and does not grant trademark rights; you may use the names to say where the software came from, and a modified version should carry its own name so users can tell the two apart.

The vault format specification, [lm-vault](https://github.com/logmark-dev/lm-vault), is CC-BY-4.0, so any tool may implement it. Contributions are accepted under the [Contributor License Agreement](CLA.md); see [CONTRIBUTING.md](CONTRIBUTING.md). Third-party components are inventoried in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

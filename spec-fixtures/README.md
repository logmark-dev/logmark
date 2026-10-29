# Conformance fixtures

Four tiny sample vaults, each with an `expected.json` capturing exactly what the reference kernel parser emits when it reads that vault. A conformant reader of the lm-vault format should reproduce the same result. These fixtures are meant to be embedded in other implementations' test suites.

## The fixtures

| Fixture | Exercises |
|---|---|
| `valid-minimal/` | The smallest compliant vault: a `vault.md` manifest and one untyped log entry. |
| `valid-full/` | Every entry type (task, idea, block, decision, question, gap, analogy, log), routed and inbox entries, tags, faceted-tag leaf collapse, full provenance (source, session, refs, task metadata), a `## Scope` constitution, and a route registry. |
| `invalid-frontmatter/` | Malformed manifest YAML frontmatter (body still parses), an empty `source:` line (retained as `""`), and a `refs:` line mixing a malformed and a valid reference (malformed dropped, valid kept). |
| `invalid-notation/` | An out-of-range timestamp header (entry dropped), a file with no header (zero entries), and false-positive notation guards (`+5`, `c++`, `#2` not extracted; unknown `taskfoo:` prefix degrades to a log). |

Each fixture directory is a vault root: its content lives under `logmark/`, so a reader resolves the content root, manifest, routes, and entry files exactly as in production.

## Ground truth: `expected.json`

`expected.json` is generated from the reference parser, never hand-authored. It is the output of running the kernel over the fixture vault, serialized as:

```
{
  "vault": "<fixture name>",
  "entryFiles": [                       // sorted by path; only entry files
    {
      "path": "logmark/<route>/_<type>/<year>.md",
      "route": "<folder-derived route, or null for _inbox>",
      "typeName": "<type folder name>",
      "entries": [
        {
          "type": "Task|Idea|Block|Decision|Question|Gap|Analogy|Log",
          "timestamp": "YYYY-MM-DDTHH:mm:ss",
          "context": "<from | context, or null>",
          "body": "<entry body, prefix preserved>",
          "inlineRoutes": ["<+route tokens in the body>"],
          "tags": ["<leaf-collapsed #tags>"],
          "source": "<agent attribution, null=human, ''=empty>",
          "session": "<session id or null>",
          "refs": ["<type:id>"],
          "shortId": "<8-hex id or null>",
          "isCompleted": false,
          "resolution": "<resolved: note or null>",
          "taskDeadline": "<ISO datetime or null>",
          "postponedCount": <int or null>,
          "extendedMetadata": { "<key>": "<value>" }
        }
      ]
    }
  ],
  "constitution": {                     // from vault.md (or the empty projection)
    "vaultName", "purpose", "hasConventions", "hasScope",
    "scopeIncludedRoutes", "scopeExcludedRoutes",
    "scopeIncludedTags", "scopeExcludedTags"
  },
  "routes": [                           // from _system/routes.md (empty if absent)
    { "name", "section", "path", "status", "tags" }
  ]
}
```

The `entryFiles` model is a composition of kernel APIs: `EntryParser.Parse` with the type hint inferred from the folder, plus `NotationParser.ExtractProjects` / `ExtractTags` over each entry body, plus the folder-derived route from `PathConstants.ParsePath`. This mirrors how the reference search index builds an entry record (tags via `ExtractTags(Content)`, route from the file's folder). The `constitution` and `routes` sections come from `VaultConstitutionParser` and `RouteParser`.

Two fields on the same entry make the notation-vs-storage distinction explicit: `inlineRoutes` is the `+route` tokens still present in the body text, while `route` is where the entry actually lives on disk. A stored entry's route is its folder (see spec chapter 4); the inline token is only a capture-time routing instruction.

## Regenerating

The generator and the verifier are the same test: `LogMark.Daemon.Tests/VaultSpecFixtureTests`. To regenerate ground truth after an intentional parser change:

```
LOGMARK_WRITE_EXPECTED=1 dotnet test src/LogMark.Daemon.Tests --filter VaultSpecFixtureTests
```

Without the environment variable the test diffs the live parser output against the committed `expected.json` and fails on any drift, so the reference implementation stays conformance-bound to its own published format.

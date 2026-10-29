# Third-Party Notices

LogMark redistributes the third-party components listed below. This file is the
authoritative inventory for the open `logmark` binary and its embedded cockpit.
It is generated from what the public tree actually ships, not inherited from any
other distribution.

- The NuGet section covers packages that flow into the self-contained .NET
  binary (the kernel, daemon, CLI, and MCP server compose one executable).
- The npm section covers packages whose code is bundled into the cockpit static
  assets embedded in that same binary. Build-only tooling that produces the
  bundle but contributes no code to it (Vite, Rollup, esbuild, TypeScript,
  Vitest, Playwright) is not redistributed and is not listed.

The full text of each license family referenced below is reproduced at the end
of this file.

## NuGet packages (.NET runtime)

### MIT

| Package | Version |
|---|---|
| Microsoft.Data.Sqlite | 10.0.0 |
| Microsoft.Data.Sqlite.Core | 10.0.0 |
| Microsoft.Recognizers.Text | 1.8.9 |
| Microsoft.Recognizers.Text.DateTime | 1.8.9 |
| Microsoft.Recognizers.Text.Number | 1.8.9 |
| Microsoft.Recognizers.Text.NumberWithUnit | 1.8.9 |
| Microsoft.Recognizers.Text.DataTypes.TimexExpression | 1.8.9 |
| Microsoft.Recognizers.Definitions | 1.8.9 |
| YamlDotNet | 16.3.0 |
| System.Collections.Immutable | 1.7.1 |
| System.ComponentModel.Annotations | 4.5.0 |
| System.Memory | 4.5.3 |
| System.Runtime.CompilerServices.Unsafe | 4.5.1 |
| System.ValueTuple | 4.5.0 |

### Apache-2.0

| Package | Version |
|---|---|
| SQLitePCLRaw.core | 2.1.11 |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.11 |
| SQLitePCLRaw.lib.e_sqlite3 | 2.1.11 |
| SQLitePCLRaw.provider.e_sqlite3 | 2.1.11 |
| Microsoft.Extensions.Caching.Abstractions | 2.2.0 |
| Microsoft.Extensions.Caching.Memory | 2.2.0 |
| Microsoft.Extensions.DependencyInjection.Abstractions | 2.2.0 |
| Microsoft.Extensions.Options | 2.2.0 |
| Microsoft.Extensions.Primitives | 2.2.0 |

### BSD-2-Clause

| Package | Version |
|---|---|
| Markdig | 0.37.0 |

### Public Domain

| Component | Notes |
|---|---|
| SQLite | The native SQLite engine, bundled by SQLitePCLRaw.lib.e_sqlite3. SQLite is released into the public domain (the SQLite blessing, reproduced below). |

Build-time only, not distributed in the shipped binary (listed here for
completeness): NuGet.CommandLine 5.11.5 (Apache-2.0) and vswhere 2.8.4 (MIT)
are pulled in transitively as build tooling and contribute no code to the
runtime.

## npm packages (cockpit bundle)

Every third-party package bundled into the cockpit is MIT-licensed. These are
the markdown-parsing runtime dependencies (`remark-parse` and `unified` plus
their transitive `micromark`, `mdast-util`, and `vfile` trees). The
`@logmark/lblocks` primitive library is a first-party component (Apache-2.0)
and is not third-party.

### MIT

| Package | Version |
|---|---|
| remark-parse | 11.0.0 |
| unified | 11.0.5 |
| bail | 2.0.2 |
| character-entities | 2.0.2 |
| debug | 4.4.3 |
| decode-named-character-reference | 1.3.0 |
| dequal | 2.0.3 |
| devlop | 1.1.0 |
| extend | 3.0.2 |
| is-plain-obj | 4.1.0 |
| mdast-util-from-markdown | 2.0.3 |
| mdast-util-to-string | 4.0.0 |
| micromark | 4.0.2 |
| micromark-core-commonmark | 2.0.3 |
| micromark-factory-destination | 2.0.1 |
| micromark-factory-label | 2.0.1 |
| micromark-factory-space | 2.0.1 |
| micromark-factory-title | 2.0.1 |
| micromark-factory-whitespace | 2.0.1 |
| micromark-util-character | 2.1.1 |
| micromark-util-chunked | 2.0.1 |
| micromark-util-classify-character | 2.0.1 |
| micromark-util-combine-extensions | 2.0.1 |
| micromark-util-decode-numeric-character-reference | 2.0.2 |
| micromark-util-decode-string | 2.0.1 |
| micromark-util-encode | 2.0.1 |
| micromark-util-html-tag-name | 2.0.1 |
| micromark-util-normalize-identifier | 2.0.1 |
| micromark-util-resolve-all | 2.0.1 |
| micromark-util-sanitize-uri | 2.0.1 |
| micromark-util-subtokenize | 2.1.0 |
| micromark-util-symbol | 2.0.1 |
| micromark-util-types | 2.0.2 |
| ms | 2.1.3 |
| trough | 2.2.0 |
| unist-util-stringify-position | 4.0.0 |
| vfile | 6.0.3 |
| vfile-message | 4.0.3 |
| @types/debug | 4.1.13 |
| @types/mdast | 4.0.4 |
| @types/ms | 2.1.0 |
| @types/unist | 3.0.3 |

## License texts

### MIT License

```
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

Copyright holders of the MIT-licensed components above include the .NET
Foundation and Microsoft (Microsoft.Data.Sqlite, Microsoft.Recognizers.Text,
System.\* packages), Antoine Aubry (YamlDotNet), and Titus Wormer and
contributors (the remark, unified, micromark, mdast-util, and vfile packages).
Each component retains its own copyright notice in its source distribution.

### BSD-2-Clause License (Markdig, Copyright Alexandre Mutel)

```
Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

### Apache License 2.0

The Apache-2.0 packages above (the SQLitePCLRaw family and the
Microsoft.Extensions family) are licensed under the Apache License, Version 2.0.
The full text of that license is in the LICENSE file distributed alongside this
document (LogMark itself is also Apache-2.0), and at:

    http://www.apache.org/licenses/LICENSE-2.0

### SQLite (Public Domain)

```
The author disclaims copyright to this source code. In place of a legal
notice, here is a blessing:

    May you do good and not evil.
    May you find forgiveness for yourself and forgive others.
    May you share freely, never taking more than you give.
```

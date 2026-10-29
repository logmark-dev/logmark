#!/usr/bin/env node
// Shared OSS dist build script - lands at build/dist.mjs in the public
// `logmark` tree (authored here as an overlay file, see
// scripts/oss/manifest.logmark.json's rebase for scripts/oss/overlay/logmark).
//
// This is the SINGLE place the tree-local build steps live: cockpit
// (packages/lblocks tsc, then the cockpit `vite build`), copying the built
// SPA into the daemon's cockpit-dist/, `dotnet publish` for one RID, and
// archiving the result. Both scripts/oss/publish-logmark.ps1 (local,
// multi-RID orchestration) and .github/workflows/release.yml (CI, one RID
// per matrix job) call this script per RID, so the two never drift apart -
// there is exactly one implementation of "how logmark gets built."
//
// Plain Node, no dependencies. Runs on Windows, Linux, and macOS.
//
// Usage:
//   node build/dist.mjs cockpit [--force]
//       Build packages/lblocks (npm ci + npm run build) then the cockpit
//       SPA (npm ci + npm run build, i.e. vite build) at the repo root.
//       Idempotent: skips both steps if dist/ui/index.html already exists,
//       unless --force. This is the CI "node 20" smoke job's TS build/test
//       step, and it is also the first phase of `build` below.
//
//   node build/dist.mjs build --rid <rid> [--version <version>] [--out <dir>] [--force-cockpit]
//       Full per-RID pipeline: cockpit() [skipped if already built], copy
//       dist/ui into src/LogMark.Daemon/cockpit-dist, `dotnet publish` the
//       daemon self-contained for <rid> with OssBuild=true and the given
//       version (default: the VERSION file at the repo root), prune the
//       publish output to one launchable binary (no lm / logmark-mcp
//       apphosts or their launch sidecars, no .pdb), archive it (zip for
//       win-*, tar.gz otherwise) into <out> (default: dist-out next to the
//       repo root).
//       Prints "RESULT: OK" and the archive path on success.
//
//   node build/dist.mjs sums --dir <dir> [--out <file>]
//       Compute a SHA256SUMS file (LF-only, ASCII, no BOM - matches
//       `sha256sum -c`) over every archive (*.zip, *.tar.gz) in <dir>,
//       sorted by filename. Default --out is <dir>/SHA256SUMS. Used once
//       after all RID archives exist, whether they were produced by a
//       local multi-RID loop or gathered from several CI matrix jobs.
//
// Every step fails closed: a missing expected file or non-zero exit code
// from a child process aborts the whole run (process.exit(1)) rather than
// continuing with a partial or stale artifact.

import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, '..');

function fail(message) {
  console.error(`FAILED: ${message}`);
  process.exit(1);
}

function log(message) {
  console.log(message);
}

function run(command, args, options = {}) {
  log(`--- ${command} ${args.join(' ')} ${options.cwd ? `(cwd: ${options.cwd})` : ''} ---`);
  // npm ships as npm.cmd on Windows, which spawnSync can only resolve
  // through the shell. Every other command here (dotnet, tar, powershell)
  // is a real executable, so shell stays off for those - narrower
  // surface, no args-escaping deprecation warning.
  const needsShell = process.platform === 'win32' && command === 'npm';
  const result = spawnSync(command, args, {
    stdio: 'inherit',
    shell: needsShell,
    ...options,
  });
  if (result.error) {
    fail(`${command} ${args.join(' ')} - ${result.error.message}`);
  }
  if (result.status !== 0) {
    fail(`${command} ${args.join(' ')} exited ${result.status}`);
  }
}

function parseArgs(argv) {
  const flags = {};
  const positional = [];
  for (let i = 0; i < argv.length; i++) {
    const token = argv[i];
    if (token.startsWith('--')) {
      const name = token.slice(2);
      const next = argv[i + 1];
      if (next === undefined || next.startsWith('--')) {
        flags[name] = true;
      } else {
        flags[name] = next;
        i++;
      }
    } else {
      positional.push(token);
    }
  }
  return { flags, positional };
}

// ---------------------------------------------------------------------------
// cockpit: packages/lblocks (tsc) then the cockpit SPA (vite build)
// ---------------------------------------------------------------------------

function cockpitDistIndex() {
  return path.join(repoRoot, 'dist', 'ui', 'index.html');
}

function buildCockpit({ force = false } = {}) {
  if (!force && fs.existsSync(cockpitDistIndex())) {
    log(`=== cockpit: already built (${cockpitDistIndex()} exists) - skipping. Pass --force to rebuild. ===`);
    return;
  }

  log('=== cockpit: packages/lblocks ===');
  const lblocksDir = path.join(repoRoot, 'packages', 'lblocks');
  if (!fs.existsSync(path.join(lblocksDir, 'package.json'))) {
    fail(`packages/lblocks not found at ${lblocksDir} - manifest missing 'packages/lblocks/**'?`);
  }
  // lblocks has no vite/rollup dependency (tsc + vitest only), so its
  // committed package-lock.json has no platform-specific optional-native-
  // dependency problem - npm ci stays deterministic here.
  run('npm', ['ci', '--no-audit', '--no-fund'], { cwd: lblocksDir });
  run('npm', ['run', 'build'], { cwd: lblocksDir });

  log('=== cockpit: SPA (vite build) ===');
  if (!fs.existsSync(path.join(repoRoot, 'package.json'))) {
    fail(`Cockpit package.json not found at ${repoRoot} - overlay package.json missing from the mirror?`);
  }
  // No committed package-lock.json for this package.json (deliberately -
  // see the overlay directory): `npm install` against package.json alone
  // resolves the dependency tree fresh every run, including the correct
  // platform-specific optional native rollup binary for whatever OS it is
  // actually running on. A lockfile authored on one dev platform (Windows)
  // only records that platform's optional native rollup/esbuild binary (a
  // longstanding npm behavior, https://github.com/npm/cli/issues/4828 -
  // "Cannot find module @rollup/rollup-<other-platform>"); `npm ci` refuses
  // to deviate from such a lockfile and fails outright on every other OS.
  // This is a small, leaf devDependency set for a build tool, not a shipped
  // artifact - version drift between runs is an acceptable tradeoff for
  // working `vite build` on Linux, macOS, and Windows CI runners alike.
  run('npm', ['install', '--no-audit', '--no-fund'], { cwd: repoRoot });
  // build:ui (vite only): lblocks was just built above with its own locked
  // toolchain, and populateCockpitDist below does this pipeline's embed copy.
  // The root `npm run build` chain (lblocks + ui + embed) is the MANUAL
  // source-build path documented in README/CONTRIBUTING, not this pipeline's.
  run('npm', ['run', 'build:ui'], { cwd: repoRoot });

  if (!fs.existsSync(cockpitDistIndex())) {
    fail(`vite build did not produce ${cockpitDistIndex()} - cockpit build is not usable.`);
  }
  log(`cockpit built: ${cockpitDistIndex()}`);
}

// ---------------------------------------------------------------------------
// populate src/LogMark.Daemon/cockpit-dist from the vite build output
// ---------------------------------------------------------------------------

function copyDirRecursive(src, dest) {
  fs.mkdirSync(dest, { recursive: true });
  for (const entry of fs.readdirSync(src, { withFileTypes: true })) {
    const s = path.join(src, entry.name);
    const d = path.join(dest, entry.name);
    if (entry.isDirectory()) {
      copyDirRecursive(s, d);
    } else {
      fs.copyFileSync(s, d);
    }
  }
}

function populateCockpitDist() {
  log('=== populate cockpit-dist ===');
  const daemonDir = path.join(repoRoot, 'src', 'LogMark.Daemon');
  const daemonCsproj = path.join(daemonDir, 'LogMark.Daemon.csproj');
  if (!fs.existsSync(daemonCsproj)) {
    fail(`LogMark.Daemon.csproj not found at ${daemonCsproj} - manifest missing 'src/LogMark.Daemon/**'?`);
  }

  const cockpitDist = path.join(daemonDir, 'cockpit-dist');
  if (fs.existsSync(cockpitDist)) {
    fs.rmSync(cockpitDist, { recursive: true, force: true });
  }
  const viteOut = path.join(repoRoot, 'dist', 'ui');
  copyDirRecursive(viteOut, cockpitDist);

  if (!fs.existsSync(path.join(cockpitDist, 'index.html'))) {
    fail(`cockpit-dist population failed - no index.html at ${cockpitDist}`);
  }
  log(`cockpit-dist populated at ${cockpitDist}`);
  return daemonCsproj;
}

// ---------------------------------------------------------------------------
// dotnet publish
// ---------------------------------------------------------------------------

function dotnetPublish(daemonCsproj, rid, version, outDir) {
  log(`=== dotnet publish ${rid} ===`);
  if (fs.existsSync(outDir)) {
    fs.rmSync(outDir, { recursive: true, force: true });
  }
  fs.mkdirSync(outDir, { recursive: true });

  run('dotnet', [
    'publish', daemonCsproj,
    '-c', 'Release',
    '--self-contained', 'true',
    '-r', rid,
    '-p:OssBuild=true',
    `-p:Version=${version}`,
    '-o', outDir,
  ]);

  const exeName = rid.startsWith('win-') ? 'logmark.exe' : 'logmark';
  if (!fs.existsSync(path.join(outDir, exeName))) {
    fail(`dotnet publish for ${rid} did not produce ${exeName} at ${outDir}`);
  }
  return outDir;
}

// ---------------------------------------------------------------------------
// prune: one launchable binary per archive
// ---------------------------------------------------------------------------

// dotnet publish copies the referenced CLI and MCP projects' apphosts (lm,
// logmark-mcp) and their launch sidecars next to logmark, plus every .pdb.
// With the extracted folder on PATH those names collide with the LogMark
// desktop app's executables. Their managed assemblies (lm.dll,
// logmark-mcp.dll) stay: logmark loads them.
const PRUNED_APPHOSTS = ['lm', 'logmark-mcp'];
const PRUNED_SIDECARS = ['deps.json', 'runtimeconfig.json'];

// relPaths are '/'-separated paths relative to the publish dir. Returns the
// ones to delete, sorted.
export function filesToPrune(relPaths, rid) {
  const exe = rid.startsWith('win-') ? '.exe' : '';
  const topLevel = new Set(PRUNED_APPHOSTS.flatMap((host) => [
    `${host}${exe}`,
    ...PRUNED_SIDECARS.map((sidecar) => `${host}.${sidecar}`),
  ]));
  return relPaths.filter((p) => topLevel.has(p) || /\.pdb$/i.test(p)).sort();
}

function listFilesRelative(dir, base = dir, out = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) listFilesRelative(full, base, out);
    else out.push(path.relative(base, full).split(path.sep).join('/'));
  }
  return out;
}

export function pruneArchiveInputs(publishDir, rid) {
  const removed = filesToPrune(listFilesRelative(publishDir), rid);
  for (const rel of removed) fs.rmSync(path.join(publishDir, ...rel.split('/')));
  return removed;
}

// ---------------------------------------------------------------------------
// legal files: the license travels with the binary
// ---------------------------------------------------------------------------

// AGPL-3.0 section 4 and 6: a binary is conveyed with the license text, and
// NOTICE carries the copyright line and where the source lives.
export const LEGAL_FILES = Object.freeze(['LICENSE', 'NOTICE', 'THIRD-PARTY-NOTICES.md']);

export function copyLegalFiles(repoDir, publishDir) {
  for (const name of LEGAL_FILES) {
    const src = path.join(repoDir, name);
    if (!fs.existsSync(src)) throw new Error(`legal file missing from the repo root: ${name}`);
  }
  fs.mkdirSync(publishDir, { recursive: true });
  for (const name of LEGAL_FILES) {
    const dest = path.join(publishDir, name);
    fs.copyFileSync(path.join(repoDir, name), dest);
    // copyFileSync keeps the source mode, and a checkout on a drvfs mount
    // reads as 0777: the archive then lists LICENSE and NOTICE as executables.
    fs.chmodSync(dest, 0o644);
  }
  return [...LEGAL_FILES];
}

// ---------------------------------------------------------------------------
// archive: zip for win-*, tar.gz otherwise
// ---------------------------------------------------------------------------

// True when the file opens with a zip local-file header (PK\x03\x04).
export function isZipFile(filePath) {
  const fd = fs.openSync(filePath, 'r');
  try {
    const head = Buffer.alloc(4);
    const read = fs.readSync(fd, head, 0, 4, 0);
    return read === 4 && head.equals(Buffer.from([0x50, 0x4b, 0x03, 0x04]));
  } finally {
    fs.closeSync(fd);
  }
}

function archive(rid, srcDir, distDir, version) {
  log(`=== archive ${rid} ===`);
  fs.mkdirSync(distDir, { recursive: true });

  let archivePath;
  if (rid.startsWith('win-')) {
    archivePath = path.join(distDir, `logmark-${version}-${rid}.zip`);
    if (fs.existsSync(archivePath)) fs.rmSync(archivePath);
    // bsdtar ships with Windows 10+ at System32\tar.exe and writes a zip for
    // -a with a .zip name. Use the absolute path: a GNU tar earlier on PATH
    // (Git for Windows) cannot create zips. No PowerShell is involved.
    const bsdtar = path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'tar.exe');
    run(fs.existsSync(bsdtar) ? bsdtar : 'tar', ['-a', '-cf', archivePath, '-C', srcDir, '.']);
    // GNU tar (Linux, WSL, Homebrew) ignores -a and writes a plain tar under
    // the .zip name, which every Windows unzip then rejects (lmk-vfng).
    if (fs.existsSync(archivePath) && !isZipFile(archivePath)) {
      fs.rmSync(archivePath);
      fail(`${path.basename(archivePath)} is not a zip: this tar cannot write zips. Build win-* archives on Windows, which ships System32\\tar.exe.`);
    }
  } else {
    archivePath = path.join(distDir, `logmark-${version}-${rid}.tar.gz`);
    if (fs.existsSync(archivePath)) fs.rmSync(archivePath);
    run('tar', ['-czf', archivePath, '-C', srcDir, '.']);
  }

  if (!fs.existsSync(archivePath)) {
    fail(`archive step did not produce ${archivePath}`);
  }
  const size = fs.statSync(archivePath).size;
  log(`archive: ${archivePath} (${(size / (1024 * 1024)).toFixed(2)} MB)`);
  return archivePath;
}

// ---------------------------------------------------------------------------
// SHA256SUMS - LF-only, ASCII, no BOM (sha256sum -c compatible). Shared byte
// discipline with the pre-refactor publish-logmark.ps1 write.
// ---------------------------------------------------------------------------

function sha256File(filePath) {
  const buf = fs.readFileSync(filePath);
  return crypto.createHash('sha256').update(buf).digest('hex');
}

function writeSums(dir, outFile) {
  const entries = fs.readdirSync(dir)
    .filter((name) => name.endsWith('.zip') || name.endsWith('.tar.gz'))
    .sort();
  if (entries.length === 0) {
    fail(`no archives (*.zip, *.tar.gz) found in ${dir} to sum`);
  }
  const lines = entries.map((name) => `${sha256File(path.join(dir, name))}  ${name}`);
  const content = lines.join('\n') + '\n';
  // Node's fs.writeFileSync writes plain bytes - no BOM is ever added for
  // 'ascii'/'utf8' output, and '\n' is always LF regardless of platform.
  fs.writeFileSync(outFile, content, { encoding: 'ascii' });
  log(`wrote ${outFile}:`);
  for (const line of lines) log(`  ${line}`);
  return outFile;
}

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------

function cmdCockpit(flags) {
  buildCockpit({ force: Boolean(flags.force) });
  log('RESULT: OK');
}

const VERSION_PATTERN = /^[A-Za-z0-9][A-Za-z0-9.+-]*$/;

function cmdBuild(flags) {
  const rid = flags.rid;
  if (!rid) fail('build requires --rid <rid>');
  let version = flags.version;
  if (!version) {
    const versionFile = path.join(repoRoot, 'VERSION');
    if (!fs.existsSync(versionFile)) {
      fail('build requires --version <version> or a VERSION file at the repo root');
    }
    version = fs.readFileSync(versionFile, 'utf8').trim();
    log(`version: ${version} (from VERSION)`);
  }
  if (!VERSION_PATTERN.test(String(version))) {
    fail(
      `--version '${version}' is not a valid version string (expected to match ` +
      `${VERSION_PATTERN}) - refusing to pass it into the archive step.`
    );
  }
  const outRoot = path.resolve(flags.out ? String(flags.out) : path.join(repoRoot, 'dist-out'));

  buildCockpit({ force: Boolean(flags['force-cockpit']) });
  const daemonCsproj = populateCockpitDist();

  const publishDir = path.join(outRoot, 'publish', rid);
  dotnetPublish(daemonCsproj, rid, version, publishDir);
  const pruned = pruneArchiveInputs(publishDir, rid);
  log(`pruned ${pruned.length} file(s) from the archive: ${pruned.join(', ') || 'none'}`);
  try {
    log(`legal files: ${copyLegalFiles(repoRoot, publishDir).join(', ')}`);
  } catch (err) {
    fail(err.message);
  }

  const archivePath = archive(rid, publishDir, outRoot, version);

  log('');
  log(`RID:     ${rid}`);
  log(`Version: ${version}`);
  log(`Archive: ${archivePath}`);
  log('RESULT: OK');
}

function cmdSums(flags) {
  const dir = flags.dir ? path.resolve(String(flags.dir)) : fail('sums requires --dir <dir>');
  const outFile = flags.out ? path.resolve(String(flags.out)) : path.join(dir, 'SHA256SUMS');
  writeSums(dir, outFile);
  log('RESULT: OK');
}

function main() {
  const [, , subcommand, ...rest] = process.argv;
  const { flags } = parseArgs(rest);

  switch (subcommand) {
    case 'cockpit':
      cmdCockpit(flags);
      break;
    case 'build':
      cmdBuild(flags);
      break;
    case 'sums':
      cmdSums(flags);
      break;
    default:
      fail(`unknown subcommand '${subcommand ?? ''}' - expected one of: cockpit, build, sums`);
  }
}

// Run the CLI only when invoked as a script, so tests can import the pure
// helpers above without starting a build.
const invokedPath = process.argv[1] ? path.resolve(process.argv[1]).toLowerCase() : '';
if (invokedPath === fileURLToPath(import.meta.url).toLowerCase()) {
  main();
}

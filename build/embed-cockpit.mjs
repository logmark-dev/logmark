#!/usr/bin/env node
// Copies the vite build output (dist/ui) into src/LogMark.Daemon/cockpit-dist
// so the next `dotnet build`/`dotnet publish` of the daemon embeds the cockpit
// SPA (the csproj glob-embeds cockpit-dist/** when the folder exists).
//
// This is the last step of the root `npm run build` chain, so a source build
// of the daemon carries the cockpit with no extra steps:
//
//   npm install && npm run build && dotnet build src/LogMark.Daemon
//
// build/dist.mjs (the release pipeline) performs the same copy through its
// own populateCockpitDist step; this script exists so the MANUAL build path
// documented in README/CONTRIBUTING reaches the same end state.
//
// Plain Node, no dependencies. Runs on Windows, Linux, and macOS.

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const src = path.join(repoRoot, 'dist', 'ui');
const dest = path.join(repoRoot, 'src', 'LogMark.Daemon', 'cockpit-dist');

if (!fs.existsSync(path.join(src, 'index.html'))) {
  console.error(`embed-cockpit: ${src} has no index.html - run 'npm run build:ui' (vite build) first.`);
  process.exit(1);
}

fs.rmSync(dest, { recursive: true, force: true });
fs.cpSync(src, dest, { recursive: true });
console.log(`embed-cockpit: copied ${src} -> ${dest}`);
console.log('embed-cockpit: the next dotnet build of src/LogMark.Daemon embeds the cockpit.');

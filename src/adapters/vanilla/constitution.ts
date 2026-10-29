import { readFile, writeFile, stat } from 'node:fs/promises'
import { join } from 'node:path'
import { resolveVaultContentRoot } from './vault-root.js'

async function exists(path: string): Promise<boolean> {
  try {
    await stat(path)
    return true
  } catch {
    return false
  }
}

/** vault.md is canonical (lm-vault manifest); constitution.md is the legacy fallback. */
async function constitutionPath(vault: string): Promise<string> {
  const root = await resolveVaultContentRoot(vault)
  const vaultMd = join(root, 'vault.md')
  if (await exists(vaultMd)) return vaultMd
  const legacy = join(root, 'constitution.md')
  if (await exists(legacy)) return legacy
  return vaultMd
}

export async function writeConstitutionFs(vault: string, body: string): Promise<void> {
  await writeFile(await constitutionPath(vault), body, 'utf8')
}

export async function readConstitutionFs(vault: string): Promise<string | null> {
  try {
    return await readFile(await constitutionPath(vault), 'utf8')
  } catch (err: unknown) {
    if ((err as NodeJS.ErrnoException).code === 'ENOENT') return null
    throw err
  }
}

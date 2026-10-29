import { readFile } from 'node:fs/promises'
import { homedir } from 'node:os'
import { join } from 'node:path'

export interface BootstrapInfo {
  vaultPath: string
  installationToken?: string
}

export async function readBootstrap(customPath?: string): Promise<BootstrapInfo | null> {
  const path = customPath ?? defaultBootstrapPath()
  try {
    const src = await readFile(path, 'utf8')
    const data = JSON.parse(src) as {
      vaultPath?: string
      VaultPath?: string
      installationToken?: string
      InstallationToken?: string
    }
    const vaultPath = data.vaultPath ?? data.VaultPath
    if (!vaultPath) return null
    return {
      vaultPath,
      installationToken: data.installationToken ?? data.InstallationToken,
    }
  } catch (err: unknown) {
    if ((err as NodeJS.ErrnoException).code === 'ENOENT') return null
    throw err
  }
}

export function defaultBootstrapPath(): string {
  if (process.platform === 'win32') {
    const appData = process.env.APPDATA ?? join(homedir(), 'AppData', 'Roaming')
    return join(appData, 'LogMark', 'bootstrap.json')
  }
  return join(homedir(), '.logmark', 'bootstrap.json')
}

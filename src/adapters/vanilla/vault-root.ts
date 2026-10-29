import { stat } from 'node:fs/promises'
import { join } from 'node:path'

export async function resolveVaultContentRoot(vault: string): Promise<string> {
  const wrapped = join(vault, 'logmark')
  try {
    const stats = await stat(wrapped)
    return stats.isDirectory() ? wrapped : vault
  } catch (err: unknown) {
    if ((err as NodeJS.ErrnoException).code === 'ENOENT') return vault
    throw err
  }
}

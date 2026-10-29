import { VaultFileWatcher } from '../services/file-watcher.js'
import { eventsForPaths } from '../services/vault-event-mapping.js'
import type { VaultEvent } from '../types/vault-events.js'

/**
 * Shared watcher-backed vault-event source for the process-local adapters
 * (vanilla, logmark-mcp). Lazily starts one chokidar watcher on first
 * subscribe; adapters stop it on disconnect.
 */
export class VaultEventHub {
  private watcher: VaultFileWatcher | null = null
  private starting: Promise<void> | null = null
  private readonly listeners = new Set<(ev: VaultEvent) => void>()

  constructor(private readonly vaultRoot: string) {}

  subscribe(listener: (ev: VaultEvent) => void): () => void {
    this.listeners.add(listener)
    if (!this.watcher) {
      this.watcher = new VaultFileWatcher(this.vaultRoot)
      this.watcher.onChange((paths) => {
        for (const ev of eventsForPaths(this.vaultRoot, paths)) {
          for (const l of this.listeners) l(ev)
        }
      })
      this.starting = this.watcher.start()
      this.starting.catch((err) =>
        console.warn(`Vault watcher failed to start: ${(err as Error).message}`)
      )
    }
    return () => {
      this.listeners.delete(listener)
    }
  }

  async stop(): Promise<void> {
    this.listeners.clear()
    const watcher = this.watcher
    this.watcher = null
    if (this.starting) await this.starting.catch(() => {})
    this.starting = null
    await watcher?.stop()
  }
}

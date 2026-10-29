export type VaultEventKind = 'entries-changed' | 'concepts-changed' | 'constitution-changed'

export interface VaultEvent {
  kind: VaultEventKind
  /** Vault-root-relative changed files, when known. */
  paths?: string[]
}

import type { IVaultAdapter } from '../adapters/adapter.js'

export class ConstitutionService {
  private cached: string | null | undefined = undefined
  constructor(private readonly adapter: IVaultAdapter) {}

  async read(): Promise<string | null> {
    if (this.cached !== undefined) return this.cached
    this.cached = await this.adapter.readConstitution()
    return this.cached
  }

  invalidate(): void {
    this.cached = undefined
  }
}

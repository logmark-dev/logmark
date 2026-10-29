import { readFile, writeFile, mkdir } from 'node:fs/promises'
import { join } from 'node:path'
import { homedir } from 'node:os'
import { DEFAULT_SETTINGS, type KnowledgeSettings } from '../types/settings.js'

const FILE = 'settings.json'

export function defaultSettingsDir(): string {
  return process.env.LK_SETTINGS_DIR ?? join(homedir(), '.logmark-knowledge')
}

export async function readSettingsFs(dir: string): Promise<KnowledgeSettings> {
  try {
    const raw = await readFile(join(dir, FILE), 'utf8')
    const parsed = JSON.parse(raw) as Partial<KnowledgeSettings>
    return {
      providerOrder: parsed.providerOrder ?? [...DEFAULT_SETTINGS.providerOrder],
      routing: { ...DEFAULT_SETTINGS.routing, ...parsed.routing },
    }
  } catch {
    return structuredClone(DEFAULT_SETTINGS)
  }
}

export async function writeSettingsFs(dir: string, settings: KnowledgeSettings): Promise<void> {
  await mkdir(dir, { recursive: true })
  await writeFile(join(dir, FILE), JSON.stringify(settings, null, 2), 'utf8')
}

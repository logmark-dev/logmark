import { readdir, readFile, writeFile, mkdir, copyFile, rename, unlink, rm, stat } from 'node:fs/promises'
import { randomUUID } from 'node:crypto'
import { extname, join } from 'node:path'
import { parseFrontmatter, serializeWithFrontmatter } from '../../lib/frontmatter.js'
import type { ConceptPage, ConceptPageSummary } from '../../types/concept-page.js'
import { resolveVaultContentRoot } from './vault-root.js'

async function conceptsDir(vault: string): Promise<string> {
  return join(await resolveVaultContentRoot(vault), 'concepts')
}

async function listSlugs(vault: string): Promise<string[]> {
  try {
    const files = await readdir(await conceptsDir(vault))
    return files
      .filter((f) => extname(f) === '.md')
      .map((f) => f.replace(/\.md$/, ''))
  } catch (err: unknown) {
    if ((err as NodeJS.ErrnoException).code === 'ENOENT') return []
    throw err
  }
}

function dataToConceptPage(slug: string, data: Record<string, unknown>, body: string): ConceptPage {
  return {
    slug,
    title: (data.title as string) ?? slug,
    body,
    status: (data.status as ConceptPage['status']) ?? 'draft',
    draft: typeof data.draft === 'boolean' ? data.draft : undefined,
    density: typeof data.density === 'number' ? (data.density as number) : undefined,
    tags: Array.isArray(data.tags) ? (data.tags as string[]) : [],
    sources: Array.isArray(data.sources) ? (data.sources as ConceptPage['sources']) : [],
    related_concepts: Array.isArray(data.related_concepts)
      ? (data.related_concepts as string[])
      : [],
    created: (data.created as string) ?? new Date().toISOString(),
    last_distilled: (data.last_distilled as string) ?? new Date().toISOString(),
    distillation_version:
      typeof data.distillation_version === 'number'
        ? (data.distillation_version as number)
        : 1,
  }
}

export async function listConceptPagesFs(vault: string): Promise<ConceptPageSummary[]> {
  const slugs = await listSlugs(vault)
  const out: ConceptPageSummary[] = []
  for (const slug of slugs) {
    const page = await readConceptPageFs(vault, slug)
    if (!page) continue
    out.push({
      slug: page.slug,
      title: page.title,
      status: page.status,
      tags: page.tags,
      last_distilled: page.last_distilled,
      source_count: page.sources.length,
      distillation_version: page.distillation_version,
      density: page.density,
      draft: page.draft,
      related_count: page.related_concepts.length,
      related_concepts: page.related_concepts,
    })
  }
  return out
}

export async function readConceptPageFs(
  vault: string,
  slug: string
): Promise<ConceptPage | null> {
  const path = join(await conceptsDir(vault), `${slug}.md`)
  let src: string
  try {
    src = await readFile(path, 'utf8')
  } catch (err: unknown) {
    if ((err as NodeJS.ErrnoException).code === 'ENOENT') return null
    throw err
  }
  const { data, body } = parseFrontmatter(src)
  return dataToConceptPage(slug, data, body)
}

export async function writeConceptPageFs(
  vault: string,
  slug: string,
  page: ConceptPage
): Promise<void> {
  const dir = await conceptsDir(vault)
  await mkdir(dir, { recursive: true })
  const data: Record<string, unknown> = {
    slug: page.slug,
    title: page.title,
    created: page.created,
    last_distilled: page.last_distilled,
    distillation_version: page.distillation_version,
    status: page.status,
    tags: page.tags,
    sources: page.sources,
    related_concepts: page.related_concepts,
  }
  if (page.draft !== undefined) data.draft = page.draft
  if (page.density !== undefined) data.density = page.density
  const md = serializeWithFrontmatter(data, page.body)
  await writeFile(join(dir, `${slug}.md`), md, 'utf8')
}

export interface AtomicWriteOptions {
  __testFailOnRename?: string
}

export async function writeConceptPagesAtomicFs(
  vault: string,
  pages: ConceptPage[],
  opts: AtomicWriteOptions = {}
): Promise<void> {
  const root = await resolveVaultContentRoot(vault)
  const conceptsRoot = await conceptsDir(vault)
  await mkdir(conceptsRoot, { recursive: true })
  const txId = randomUUID()
  const txDir = join(root, '.knowledge', '.write-tx', txId)
  await mkdir(txDir, { recursive: true })

  const tempFiles: { tempPath: string; finalPath: string; slug: string }[] = []
  const snapshots: { snapPath: string; finalPath: string }[] = []

  try {
    for (const page of pages) {
      const finalPath = join(conceptsRoot, `${page.slug}.md`)
      const tempPath = `${finalPath}.tmp.${txId}`

      try {
        await stat(finalPath)
        const snapPath = join(txDir, `${page.slug}.md.snapshot`)
        await copyFile(finalPath, snapPath)
        snapshots.push({ snapPath, finalPath })
      } catch (err: unknown) {
        if ((err as NodeJS.ErrnoException).code !== 'ENOENT') throw err
      }

      const data: Record<string, unknown> = {
        slug: page.slug,
        title: page.title,
        created: page.created,
        last_distilled: page.last_distilled,
        distillation_version: page.distillation_version,
        status: page.status,
        tags: page.tags,
        sources: page.sources,
        related_concepts: page.related_concepts,
      }
      if (page.density !== undefined) data.density = page.density
      if (page.draft !== undefined) data.draft = page.draft
      const md = serializeWithFrontmatter(data, page.body)
      await writeFile(tempPath, md, 'utf8')
      tempFiles.push({ tempPath, finalPath, slug: page.slug })
    }

    for (const t of tempFiles) {
      if (opts.__testFailOnRename === t.slug) {
        throw new Error(`simulated rename failure on ${t.slug}`)
      }
      await rename(t.tempPath, t.finalPath)
    }

    await rm(txDir, { recursive: true, force: true })
    // Single-tenant assumption: Phase 1 has no concurrent distillation, so the
    // parent .write-tx dir contains only this run's txId subdir at cleanup time.
    // If concurrent writeConceptPagesAtomicFs calls are ever introduced, this
    // parent rm must be removed or guarded -- rm({ recursive: true, force: true })
    // would silently wipe sibling runs' snapshots and break their rollback.
    await rm(join(root, '.knowledge', '.write-tx'), { recursive: true, force: true }).catch(() => undefined)
  } catch (err) {
    for (const t of tempFiles) {
      try { await unlink(t.tempPath) } catch { /* may already be renamed */ }
    }
    for (const s of snapshots) {
      try { await copyFile(s.snapPath, s.finalPath) } catch { /* original missing now */ }
    }
    await rm(txDir, { recursive: true, force: true }).catch(() => undefined)
    // Same single-tenant assumption as above; see note at success path.
    await rm(join(root, '.knowledge', '.write-tx'), { recursive: true, force: true }).catch(() => undefined)
    throw new Error(`atomic write failed: ${(err as Error).message}`)
  }
}

import type { ConceptPage } from './concept-page.js'

export interface UncitedClaim {
  sentence: string
  lineNumber: number
}

export interface ParsedPage {
  slug: string
  body: string
}

export interface ParsedDistillOutput {
  pages: ParsedPage[]
  skipped: string[]
}

export interface DistillResult {
  pages: ConceptPage[]
  skipped: string[]
  runLog: {
    promptCharCount: number
    responseCharCount: number
    uncitedByPage: Record<string, number>
  }
}

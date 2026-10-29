import type {
  ChatTranscript,
  ChatTranscriptBlock,
  ChatTranscriptMeta,
  ChatTurn,
  ChatTurnKind,
} from '../types/chat-transcript.js'
import type { MarkdownBlock, MarkdownRenderOptions } from '../types/markdown-doc.js'
import { parseMarkdown } from '../engine/markdown-parser.js'
import { renderHeading } from './markdown-heading.js'
import { renderParagraph } from './markdown-paragraph.js'
import { renderList } from './markdown-list.js'
import { renderCodeblock } from './markdown-codeblock.js'
import { renderBlockquote } from './markdown-blockquote.js'
import { renderTable } from './markdown-table.js'
import { renderImage } from './markdown-image.js'
import { renderDivider } from './markdown-divider.js'

const KNOWN_KINDS: Record<string, ChatTurnKind> = {
  user: 'user',
  assistant: 'assistant',
  tool_call: 'tool_call',
  tool_result: 'tool_result',
  write_confirmed: 'write_confirmed',
  write_rejected: 'write_rejected',
  system: 'system',
}

// Matches known turn headers: ## user, ## assistant, ## tool_call: name, etc.
const KNOWN_KIND_TURN_RE =
  /^## (user|assistant|tool_call|tool_result|write_confirmed|write_rejected|system)(?::\s+(\S.*))?$/

// Matches any ## header with a single non-whitespace word (optionally followed by ": rest").
// Lines like "## My H2 heading" do NOT match because "My" is followed by " H2 heading",
// which fails the $ anchor -- the regex requires the word to extend to end of line
// or be followed only by the optional ": rest" suffix.
const ANY_TURN_HEADER_RE = /^## (\S+)(?::\s+(\S.*))?$/

const TIMESTAMP_RE = /^### (\S.*)$/
const METADATA_RE = /^_metadata:\s*(.+)_$/

export function parseChatTranscript(raw: string): ChatTranscript {
  const lines = raw.replace(/\r\n/g, '\n').split('\n')
  let i = 0

  const meta: ChatTranscriptMeta = {}

  // Parse frontmatter block if present
  if (i < lines.length && lines[i] === '---') {
    i++
    while (i < lines.length && lines[i] !== '---') {
      const line = lines[i]
      const colonIdx = line.indexOf(':')
      if (colonIdx > 0) {
        const key = line.substring(0, colonIdx).trim()
        const value = line.substring(colonIdx + 1).trim()
        switch (key) {
          case 'chat_id': meta.chatId = value; break
          case 'started': meta.started = value; break
          case 'transport': meta.transport = value; break
          case 'route_scope': meta.routeScope = value; break
          case 'entry_scope': meta.entryScope = value; break
        }
      }
      i++
    }
    if (i < lines.length) i++ // skip closing ---
  }

  const turns: ChatTurn[] = []
  let currentTurn: ChatTurn | null = null
  let contentBuffer: string[] = []

  const flushTurn = () => {
    if (currentTurn === null) return
    let content = contentBuffer.join('\n').trim()
    // Check last non-empty line for _metadata: ..._ annotation
    const cLines = content.split('\n')
    if (cLines.length > 0) {
      const last = cLines[cLines.length - 1].trim()
      const metaMatch = last.match(METADATA_RE)
      if (metaMatch) {
        currentTurn.metadata = metaMatch[1].trim()
        cLines.pop()
        content = cLines.join('\n').trim()
      }
    }
    currentTurn.content = content
    turns.push(currentTurn)
    currentTurn = null
    contentBuffer = []
  }

  while (i < lines.length) {
    const line = lines[i]

    // Check for a known kind turn header first
    const knownMatch = line.match(KNOWN_KIND_TURN_RE)
    if (knownMatch) {
      flushTurn()
      currentTurn = {
        kind: KNOWN_KINDS[knownMatch[1]] ?? 'unknown',
        content: '',
      }
      if (knownMatch[2]) currentTurn.toolName = knownMatch[2].trim()
      i++
      // Skip blank lines after header
      while (i < lines.length && lines[i].trim() === '') i++
      // Check for optional ### timestamp line
      if (i < lines.length) {
        const tsMatch = lines[i].match(TIMESTAMP_RE)
        if (tsMatch) {
          currentTurn.timestamp = tsMatch[1].trim()
          i++
        }
      }
      continue
    }

    // Check for an unknown single-word ## header (creates an unknown-kind turn).
    // Only single-word headers (no spaces in the word) are treated as turn boundaries.
    // Multi-word ## lines like "## My H2 heading" do NOT match ANY_TURN_HEADER_RE
    // because the \S+ anchor forces the word to end at whitespace or line end.
    const anyMatch = line.match(ANY_TURN_HEADER_RE)
    if (anyMatch && !KNOWN_KINDS[anyMatch[1]]) {
      flushTurn()
      currentTurn = {
        kind: 'unknown',
        content: '',
      }
      i++
      // Skip blank lines after header
      while (i < lines.length && lines[i].trim() === '') i++
      // Check for optional ### timestamp line
      if (i < lines.length) {
        const tsMatch = lines[i].match(TIMESTAMP_RE)
        if (tsMatch) {
          currentTurn.timestamp = tsMatch[1].trim()
          i++
        }
      }
      continue
    }

    // Accumulate content into the current turn
    if (currentTurn !== null) {
      contentBuffer.push(line)
    }
    i++
  }
  flushTurn()

  return { meta, turns }
}

export function renderChatTranscript(
  block: ChatTranscriptBlock,
  options: MarkdownRenderOptions
): HTMLElement {
  const transcript = parseChatTranscript(block.body)
  const root = document.createElement('div')
  root.className = 'lb-chat-transcript'
  root.style.fontFamily = 'var(--lb-font-family)'
  root.style.color = 'var(--lb-text-primary)'
  root.style.padding = '20px 24px'
  root.style.maxWidth = '760px'
  root.style.margin = '0 auto'

  if (Object.keys(transcript.meta).length > 0) {
    root.appendChild(renderMetaStrip(transcript.meta))
  }

  if (transcript.turns.length === 0) {
    const empty = document.createElement('div')
    empty.className = 'lb-chat-empty'
    empty.style.color = 'var(--lb-text-muted)'
    empty.style.textAlign = 'center'
    empty.style.padding = '40px 0'
    empty.textContent = '(no messages)'
    root.appendChild(empty)
    return root
  }

  for (const turn of transcript.turns) {
    root.appendChild(renderTurn(turn, options))
  }
  return root
}

function renderMetaStrip(meta: ChatTranscriptMeta): HTMLElement {
  const strip = document.createElement('div')
  strip.className = 'lb-chat-meta'
  strip.style.color = 'var(--lb-text-muted)'
  strip.style.fontSize = '11px'
  strip.style.marginBottom = '24px'
  strip.style.fontVariantNumeric = 'tabular-nums'
  const parts: string[] = []
  if (meta.chatId) parts.push(`chat ${meta.chatId}`)
  if (meta.started) parts.push(`started ${meta.started}`)
  if (meta.transport) parts.push(meta.transport)
  if (meta.routeScope) parts.push(`+${meta.routeScope.replace(/^\+/, '')}`)
  if (meta.entryScope) parts.push(meta.entryScope)
  strip.textContent = parts.join(' · ')
  return strip
}

function renderTurn(turn: ChatTurn, options: MarkdownRenderOptions): HTMLElement {
  const wrapper = document.createElement('div')
  wrapper.className = `lb-chat-turn lb-chat-turn--${turn.kind}`
  wrapper.style.marginBottom = '16px'

  const header = document.createElement('div')
  header.className = 'lb-chat-turn-header'
  header.style.display = 'flex'
  header.style.justifyContent = 'space-between'
  header.style.alignItems = 'baseline'
  header.style.marginBottom = '6px'
  header.appendChild(renderPill(turn))
  if (turn.timestamp) {
    const ts = document.createElement('span')
    ts.className = 'lb-chat-timestamp'
    ts.style.fontSize = '11px'
    ts.style.color = 'var(--lb-text-dim, var(--lb-text-muted))'
    ts.style.fontVariantNumeric = 'tabular-nums'
    ts.textContent = turn.timestamp
    header.appendChild(ts)
  }
  wrapper.appendChild(header)

  const body = document.createElement('div')
  body.className = 'lb-chat-turn-body'
  switch (turn.kind) {
    case 'user':
    case 'assistant':
      body.appendChild(renderMarkdownInto(turn.content, options))
      break
    case 'tool_call':
    case 'tool_result':
      body.style.fontFamily = 'var(--lb-font-mono, monospace)'
      body.style.fontSize = '85%'
      body.style.color = 'var(--lb-text-muted)'
      body.style.whiteSpace = 'pre-wrap'
      body.textContent = turn.content
      break
    case 'write_confirmed':
      wrapper.style.borderLeft = '3px solid var(--lb-signal-positive)'
      wrapper.style.paddingLeft = '12px'
      body.style.fontFamily = 'var(--lb-font-mono, monospace)'
      body.style.fontSize = '90%'
      body.style.whiteSpace = 'pre-wrap'
      body.textContent = turn.content
      break
    case 'write_rejected':
      wrapper.style.borderLeft = '3px solid var(--lb-signal-negative)'
      wrapper.style.paddingLeft = '12px'
      body.style.fontFamily = 'var(--lb-font-mono, monospace)'
      body.style.fontSize = '90%'
      body.style.whiteSpace = 'pre-wrap'
      body.textContent = turn.content
      break
    case 'system':
    case 'unknown':
    default:
      body.style.fontStyle = 'italic'
      body.style.color = 'var(--lb-text-muted)'
      body.style.fontSize = '90%'
      body.textContent = turn.content
      break
  }
  wrapper.appendChild(body)

  if (turn.metadata) {
    const metaEl = document.createElement('div')
    metaEl.style.fontSize = '11px'
    metaEl.style.color = 'var(--lb-text-muted)'
    metaEl.style.fontStyle = 'italic'
    metaEl.style.marginTop = '4px'
    metaEl.textContent = `metadata: ${turn.metadata}`
    wrapper.appendChild(metaEl)
  }

  return wrapper
}

function renderPill(turn: ChatTurn): HTMLElement {
  const pill = document.createElement('span')
  pill.className = `lb-chat-pill lb-chat-pill--${turn.kind}`
  pill.style.display = 'inline-block'
  pill.style.padding = '2px 8px'
  pill.style.borderRadius = '10px'
  pill.style.fontSize = '11px'
  pill.style.fontWeight = '600'
  pill.style.textTransform = 'lowercase'
  switch (turn.kind) {
    case 'user':
      pill.style.background = 'var(--lb-bg-secondary)'
      pill.style.border = '1px solid var(--lb-border)'
      pill.style.color = 'var(--lb-text-secondary)'
      pill.textContent = 'you'
      break
    case 'assistant':
      pill.style.background = 'var(--lb-signal-positive)'
      pill.style.color = 'var(--lb-text-on-accent)'
      pill.textContent = 'logmark'
      break
    case 'tool_call':
      pill.style.background = 'transparent'
      pill.style.color = 'var(--lb-text-muted)'
      pill.style.fontWeight = '400'
      pill.textContent = `-> ${turn.toolName ?? '?'}`
      break
    case 'tool_result':
      pill.style.background = 'transparent'
      pill.style.color = 'var(--lb-text-muted)'
      pill.style.fontWeight = '400'
      pill.textContent = `<- ${turn.toolName ?? '?'}`
      break
    case 'write_confirmed':
      pill.style.background = 'var(--lb-signal-positive)'
      pill.style.color = 'var(--lb-text-on-accent)'
      pill.textContent = 'saved'
      break
    case 'write_rejected':
      pill.style.background = 'var(--lb-signal-negative)'
      pill.style.color = 'var(--lb-text-on-accent)'
      pill.textContent = 'discarded'
      break
    case 'system':
    case 'unknown':
    default:
      pill.style.background = 'transparent'
      pill.style.color = 'var(--lb-text-muted)'
      pill.style.fontWeight = '400'
      pill.textContent = 'system'
      break
  }
  return pill
}

// Renders the body of a chat turn as parsed markdown. Uses the lblocks parseMarkdown
// engine to tokenize source text into blocks, then dispatches each block through the
// matching markdown primitive renderer. Mirrors the dispatch shape used by composeMarkdown.
function renderMarkdownInto(content: string, options: MarkdownRenderOptions): HTMLElement {
  const wrapper = document.createElement('div')
  const blocks = parseMarkdown(content)
  for (const block of blocks) {
    wrapper.appendChild(dispatchBlock(block, options))
  }
  return wrapper
}

function dispatchBlock(block: MarkdownBlock, options: MarkdownRenderOptions): HTMLElement {
  switch (block.kind) {
    case 'heading':
      return renderHeading(block, options)
    case 'paragraph':
      return renderParagraph(block, options)
    case 'list':
      return renderList(block, options, (b) => dispatchBlock(b, options))
    case 'codeblock':
      return renderCodeblock(block, options)
    case 'blockquote':
      return renderBlockquote(block, options, (b) => dispatchBlock(b, options))
    case 'table':
      return renderTable(block, options)
    case 'image':
      return renderImage(block, options)
    case 'divider':
      return renderDivider(block, options)
    case 'frontmatter':
    case 'chat-transcript':
    default: {
      // These shouldn't appear inside a chat turn body. Fall back to a span with literal text.
      const span = document.createElement('span')
      span.textContent = `(unsupported block: ${(block as { kind: string }).kind})`
      return span
    }
  }
}

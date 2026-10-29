// Chat transcript types: parsed structure of a vault/logmark/_chats/*.md file.

export type ChatTranscriptMeta = {
  chatId?: string
  started?: string      // raw frontmatter "2026-05-05T14:30:00"
  transport?: string
  routeScope?: string
  entryScope?: string
}

export type ChatTurnKind =
  | 'user'
  | 'assistant'
  | 'tool_call'
  | 'tool_result'
  | 'write_confirmed'
  | 'write_rejected'
  | 'system'
  | 'unknown'

export type ChatTurn = {
  kind: ChatTurnKind
  timestamp?: string
  toolName?: string
  metadata?: string
  content: string
}

export type ChatTranscript = {
  meta: ChatTranscriptMeta
  turns: ChatTurn[]
}

export type ChatTranscriptBlock = {
  kind: 'chat-transcript'
  body: string
}

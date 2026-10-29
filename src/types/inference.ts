export interface PromptMessage {
  role: 'system' | 'user' | 'assistant'
  content: string
}

export interface Prompt {
  messages: PromptMessage[]
}

export interface InferenceResponse {
  body: string                  // raw text response
  usage?: {
    input_tokens?: number
    output_tokens?: number
  }
  finish_reason?: string
}

export interface InferenceOpts {
  model?: string
  maxTokens?: number
  temperature?: number
  timeoutMs?: number
}

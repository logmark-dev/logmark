export type ProviderName = 'claude-cli' | 'ollama' | 'lm-studio'

export type RoutingMode = 'default' | 'disabled'

export interface KnowledgeSettings {
  /** Preference order for inference provider detection. */
  providerOrder: ProviderName[]
  /** Per-task routing: 'default' uses the resolved provider, 'disabled' turns the task off. */
  routing: {
    summarization: RoutingMode
    distillation: RoutingMode
    searchEnrichment: RoutingMode
  }
}

export const DEFAULT_SETTINGS: KnowledgeSettings = {
  providerOrder: ['claude-cli', 'ollama', 'lm-studio'],
  routing: {
    summarization: 'default',
    distillation: 'default',
    searchEnrichment: 'disabled',
  },
}

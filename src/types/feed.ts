export interface FeedItem {
  id: string
  kind: string                  // detector type (e.g. 'depth-proposal', 'cluster')
  payload: Record<string, unknown>
  created_at: string
  status: 'pending' | 'actioned' | 'dismissed'
}

export interface Decision {
  id: string
  title: string
  status: 'frozen' | 'active' | 'superseded'
  scope?: string
  rationale?: string
  createdAt: string
}

// One /api/events stream per browser, not per tab (bead lmk-9ozu).
//
// Each EventSource holds one of the browser's 6 HTTP/1.1 connections to the
// host for as long as the tab is open, so about 6 cockpit tabs starved every
// other request (pages, API calls, fonts) of a connection. Instead, the tab
// that holds a Web Lock owns the stream and relays its events and live state
// to the other tabs over a BroadcastChannel. When that tab closes, its lock is
// released and the next waiting tab opens the stream. Where either API is
// missing, each tab falls back to its own stream.

export interface EventSourceLike {
  addEventListener(name: string, listener: (e: { data?: string }) => void): void
  close(): void
}

export interface ChannelLike {
  onmessage: ((e: { data: unknown }) => void) | null
  postMessage(data: unknown): void
  close(): void
}

export interface LocksLike {
  request(name: string, callback: () => Promise<void>): Promise<unknown>
}

export interface LiveEventDeps {
  createSource: (url: string) => EventSourceLike
  locks?: LocksLike
  createChannel?: (name: string) => ChannelLike
}

type Message =
  | { type: 'event'; data: string }
  | { type: 'state'; live: boolean }
  | { type: 'hello' }

const EVENTS_URL = '/api/events'
const SHARED_NAME = 'logmark-cockpit-events'

/**
 * Connect to vault events. `onEvent` receives the raw `vault-event` data;
 * `onState` the stream's live state. Returns a function that disconnects.
 */
export function connectLiveEvents(
  onEvent: (data: string) => void,
  onState: (live: boolean) => void,
  deps: LiveEventDeps,
): () => void {
  if (!deps.locks || !deps.createChannel) return openStream(deps.createSource, onEvent, onState)

  const channel = deps.createChannel(SHARED_NAME)
  let closed = false
  let ownsStream = false
  let live: boolean | undefined
  let release: (() => void) | undefined

  const setLive = (value: boolean) => {
    live = value
    onState(value)
  }

  channel.onmessage = (e) => {
    const message = e.data as Message
    if (message.type === 'event') onEvent(message.data)
    else if (message.type === 'state') setLive(message.live)
    else if (message.type === 'hello' && ownsStream && live !== undefined) channel.postMessage({ type: 'state', live })
  }
  // Ask the stream owner, if there is one yet, for its current live state.
  channel.postMessage({ type: 'hello' })

  void deps.locks.request(SHARED_NAME, () => {
    if (closed) return Promise.resolve()
    ownsStream = true
    const stop = openStream(
      deps.createSource,
      (data) => {
        onEvent(data)
        channel.postMessage({ type: 'event', data })
      },
      (value) => {
        setLive(value)
        channel.postMessage({ type: 'state', live: value })
      },
    )
    // Hold the lock, and with it the stream, until this tab disconnects.
    return new Promise<void>((resolve) => {
      release = () => {
        stop()
        resolve()
      }
    })
  })

  return () => {
    closed = true
    release?.()
    channel.close()
  }
}

function openStream(
  createSource: (url: string) => EventSourceLike,
  onEvent: (data: string) => void,
  onState: (live: boolean) => void,
): () => void {
  const source = createSource(EVENTS_URL)
  source.addEventListener('open', () => onState(true))
  source.addEventListener('error', () => onState(false))
  source.addEventListener('vault-event', (e) => {
    if (typeof e.data === 'string') onEvent(e.data)
  })
  return () => source.close()
}

/** The browser's own APIs, or a per-tab stream where they are missing. */
export function browserLiveEventDeps(): LiveEventDeps | null {
  if (typeof EventSource === 'undefined') return null
  const locks = typeof navigator !== 'undefined' ? (navigator as Navigator & { locks?: LocksLike }).locks : undefined
  return {
    createSource: (url) => new EventSource(url) as unknown as EventSourceLike,
    ...(locks && typeof BroadcastChannel !== 'undefined'
      ? { locks, createChannel: (name: string) => new BroadcastChannel(name) as unknown as ChannelLike }
      : {}),
  }
}

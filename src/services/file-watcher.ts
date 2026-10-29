import chokidar, { type FSWatcher } from 'chokidar'

export interface FileWatcherOptions {
  debounceMs?: number
}

export class VaultFileWatcher {
  private watcher: FSWatcher | null = null
  private listeners: Array<(paths: string[]) => void> = []
  private buffer = new Set<string>()
  private timer: NodeJS.Timeout | null = null

  constructor(
    private readonly root: string,
    private readonly opts: FileWatcherOptions = {}
  ) {}

  onChange(listener: (paths: string[]) => void): void {
    this.listeners.push(listener)
  }

  async start(): Promise<void> {
    this.watcher = chokidar.watch(this.root, {
      persistent: true,
      ignoreInitial: true,
      ignored: (p: string) => p.includes('node_modules') || p.includes('.git'),
    })
    this.watcher
      .on('add', (p) => this.queue(p))
      .on('change', (p) => this.queue(p))
      .on('unlink', (p) => this.queue(p))
    await new Promise<void>((resolve) => this.watcher!.once('ready', () => resolve()))
  }

  async stop(): Promise<void> {
    if (this.timer) clearTimeout(this.timer)
    await this.watcher?.close()
    this.watcher = null
    this.timer = null
  }

  private queue(path: string): void {
    if (!path.endsWith('.md')) return
    this.buffer.add(path)
    const debounce = this.opts.debounceMs ?? 100
    if (this.timer) clearTimeout(this.timer)
    this.timer = setTimeout(() => this.flush(), debounce)
  }

  private flush(): void {
    const paths = Array.from(this.buffer)
    this.buffer.clear()
    this.timer = null
    for (const l of this.listeners) l(paths)
  }
}

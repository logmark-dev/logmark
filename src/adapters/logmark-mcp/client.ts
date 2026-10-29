import { Client } from '@modelcontextprotocol/sdk/client'
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio'

export interface McpClientOptions {
  exePath: string
  args?: string[]
}

export class LogMarkMcpClient {
  private client: Client | null = null
  private transport: StdioClientTransport | null = null

  constructor(private readonly opts: McpClientOptions) {}

  async connect(): Promise<void> {
    if (this.client) return
    this.transport = new StdioClientTransport({
      command: this.opts.exePath,
      args: this.opts.args ?? [],
    })
    this.client = new Client(
      { name: 'logmark-knowledge', version: '0.1.0' },
      { capabilities: {} }
    )
    await this.client.connect(this.transport)
  }

  async disconnect(): Promise<void> {
    await this.client?.close()
    this.client = null
    this.transport = null
  }

  isConnected(): boolean {
    return this.client !== null
  }

  async listTools(): Promise<string[]> {
    this.requireConnected()
    const result = await this.client!.listTools()
    return result.tools.map((t) => t.name)
  }

  async callTool<T = unknown>(name: string, args: Record<string, unknown>): Promise<T> {
    this.requireConnected()
    const result = await this.client!.callTool({ name, arguments: args })
    if (result.isError) {
      throw new Error(`MCP tool ${name} returned error: ${JSON.stringify(result.content)}`)
    }
    const content = (result.content ?? []) as Array<{ type: string; text?: string }>
    const text = content.find((c) => c.type === 'text')?.text
    if (!text) return {} as T
    try {
      return JSON.parse(text) as T
    } catch {
      return text as unknown as T
    }
  }

  private requireConnected(): void {
    if (!this.client) throw new Error('MCP client not connected')
  }
}

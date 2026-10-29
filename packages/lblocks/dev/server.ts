import { createServer } from 'http'
import { readFileSync, existsSync } from 'fs'
import { resolve, extname } from 'path'

const PORT = 3712
const DEV_DIR = resolve(import.meta.dirname)
const DIST_DIR = resolve(DEV_DIR, '../dist')
const FIXTURES_DIR = resolve(DEV_DIR, '../../../schemas/viewblocks/fixtures')

const MIME: Record<string, string> = {
  '.html': 'text/html',
  '.js': 'application/javascript',
  '.json': 'application/json',
  '.css': 'text/css',
  '.png': 'image/png',
}

const server = createServer((req, res) => {
  const url = new URL(req.url ?? '/', `http://localhost:${PORT}`)

  if (url.pathname === '/fixtures') {
    const files = ['activity-chart', 'type-breakdown', 'route-distribution', 'narrative-summary']
    const fixtures: Record<string, unknown> = {}
    for (const name of files) {
      const path = resolve(FIXTURES_DIR, `${name}.fixture.json`)
      if (existsSync(path)) {
        fixtures[name] = JSON.parse(readFileSync(path, 'utf-8'))
      }
    }
    res.writeHead(200, { 'Content-Type': 'application/json' })
    res.end(JSON.stringify(fixtures))
    return
  }

  const filePath = url.pathname === '/'
    ? resolve(DEV_DIR, 'index.html')
    : existsSync(resolve(DIST_DIR, url.pathname.slice(1)))
      ? resolve(DIST_DIR, url.pathname.slice(1))
      : resolve(DEV_DIR, url.pathname.slice(1))

  if (!existsSync(filePath)) {
    res.writeHead(404)
    res.end('Not found')
    return
  }

  const mime = MIME[extname(filePath)] ?? 'text/plain'
  res.writeHead(200, { 'Content-Type': mime })
  res.end(readFileSync(filePath))
})

server.listen(PORT, () => {
  console.log(`lblocks dev server: http://localhost:${PORT}`)
})

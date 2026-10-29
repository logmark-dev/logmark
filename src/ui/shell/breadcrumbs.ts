import { deriveTitleFromPath, isPathTitle } from '../lib/notation.js'

export interface Breadcrumb {
  label: string
  href?: string
}

export function breadcrumbsForPath(pathname: string): Breadcrumb[] {
  if (pathname === '/') return [{ label: 'vault' }]
  if (pathname === '/concepts') return [{ label: 'vault', href: '/' }, { label: 'concepts' }]
  if (pathname === '/concepts/new') {
    return [
      { label: 'vault', href: '/' },
      { label: 'concepts', href: '/concepts' },
      { label: 'new' },
    ]
  }
  if (pathname === '/search') return [{ label: 'vault', href: '/' }, { label: 'search' }]
  if (pathname === '/timeline') return [{ label: 'vault', href: '/' }, { label: 'timeline' }]
  if (pathname === '/health') return [{ label: 'vault', href: '/' }, { label: 'health' }]
  if (pathname === '/settings') return [{ label: 'vault', href: '/' }, { label: 'settings' }]
  if (pathname === '/constitution') return [{ label: 'vault', href: '/' }, { label: 'constitution' }]

  const lens = pathname.match(/^\/lens\/([^/]+)$/)
  if (lens) {
    const type = decodeURIComponent(lens[1])
    return [
      { label: 'vault', href: '/' },
      { label: 'lenses' },
      { label: type },
    ]
  }

  const route = pathname.match(/^\/routes\/([^/]+)$/)
  if (route) {
    // The + route-operator is notation, not display copy (pass-2 R4).
    const routePath = decodeURIComponent(route[1]).replace(/^\+/, '')
    return [
      { label: 'vault', href: '/' },
      { label: 'routes' },
      { label: routePath },
    ]
  }

  const conceptEdit = pathname.match(/^\/concepts\/([^/]+)\/edit$/)
  if (conceptEdit) {
    return [
      { label: 'vault', href: '/' },
      { label: 'concepts', href: '/concepts' },
      { label: decodeURIComponent(conceptEdit[1]), href: `/concepts/${conceptEdit[1]}` },
      { label: 'edit' },
    ]
  }

  const concept = pathname.match(/^\/concepts\/([^/]+)$/)
  if (concept) {
    return [
      { label: 'vault', href: '/' },
      { label: 'concepts', href: '/concepts' },
      { label: decodeURIComponent(concept[1]) },
    ]
  }

  const entry = pathname.match(/^\/entries\/([^/]+)$/)
  if (entry) {
    // Path-derived ids get the shared human-title rule (pass-3 P3).
    const id = decodeURIComponent(entry[1])
    return [
      { label: 'vault', href: '/' },
      { label: 'entries' },
      { label: isPathTitle(id) ? deriveTitleFromPath(id) : id },
    ]
  }

  return [{ label: 'vault', href: '/' }, { label: pathname.replace(/^\//, '') || 'unknown' }]
}

export function downloadFileUrl(href: string, filename: string) {
  const anchor = document.createElement('a')
  anchor.hidden = true
  anchor.href = href
  anchor.download = filename
  document.body.appendChild(anchor)
  try { anchor.click() } finally { anchor.remove() }
}

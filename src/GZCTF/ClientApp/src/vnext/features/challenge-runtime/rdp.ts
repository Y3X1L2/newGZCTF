function safeRdpValue(value: string) {
  return value.replace(/[\r\n]/g, '')
}

export function buildRdpFile(host: string, port: number, username: string) {
  const address = `${safeRdpValue(host)}:${port}`
  return [
    `full address:s:${address}`,
    `username:s:${safeRdpValue(username)}`,
    'prompt for credentials:i:1',
    'redirectclipboard:i:1',
    'redirectdrives:i:0',
    'redirectprinters:i:0',
    'authentication level:i:2',
    'enablecredsspsupport:i:1',
    '',
  ].join('\r\n')
}

export function downloadRdpFile(host: string, port: number, username: string) {
  const blob = new Blob([`\uFEFF${buildRdpFile(host, port, username)}`], { type: 'text/plain;charset=utf-8' })
  const href = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = href
  anchor.download = 'yinyu-windows-vm.rdp'
  anchor.click()
  URL.revokeObjectURL(href)
}

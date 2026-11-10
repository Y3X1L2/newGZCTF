export async function copyText(text: string): Promise<void> {
  if (navigator.clipboard) return navigator.clipboard.writeText(text)

  const focused = document.activeElement as HTMLElement | null
  const input = document.createElement('textarea')
  input.value = text
  input.style.cssText = 'position:fixed;opacity:0;pointer-events:none'
  ;(focused?.parentElement ?? document.body).append(input)
  try {
    input.select()
    if (!document.execCommand('copy')) throw new Error('复制失败，请手动复制')
  } finally {
    input.remove()
    focused?.focus()
  }
}

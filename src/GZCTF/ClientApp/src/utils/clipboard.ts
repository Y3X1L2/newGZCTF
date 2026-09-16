/** Copy in secure contexts and HTTP deployments without reporting false success. */
export async function copyText(value: string): Promise<boolean> {
  if (!value || typeof document === 'undefined' || typeof navigator === 'undefined') return false

  if (navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(value)
      return true
    } catch {
      // Permissions or browser policy may still allow the user-initiated DOM copy.
    }
  }

  const active = document.activeElement instanceof HTMLElement ? document.activeElement : null
  const selection = window.getSelection()
  const ranges = selection
    ? Array.from({ length: selection.rangeCount }, (_, index) => selection.getRangeAt(index).cloneRange())
    : []
  const input = active instanceof HTMLInputElement || active instanceof HTMLTextAreaElement ? active : null
  const start = input?.selectionStart
  const end = input?.selectionEnd
  const direction = input?.selectionDirection
  const textarea = document.createElement('textarea')
  textarea.value = value
  textarea.readOnly = true
  textarea.tabIndex = -1
  textarea.style.cssText = 'position:fixed;left:-9999px;top:0;opacity:0'
  // A native modal dialog makes nodes outside it inert.
  const host = active?.closest('dialog[open]') ?? document.body
  try {
    host.appendChild(textarea)
    textarea.focus({ preventScroll: true })
    textarea.select()
    textarea.setSelectionRange(0, value.length)
    return typeof document.execCommand === 'function' && document.execCommand('copy')
  } catch {
    return false
  } finally {
    textarea.remove()
    if (active?.isConnected) active.focus({ preventScroll: true })
    if (input?.isConnected && start != null && end != null) input.setSelectionRange(start, end, direction ?? undefined)
    if (selection && ranges.length) {
      selection.removeAllRanges()
      ranges.forEach((range) => selection.addRange(range))
    }
  }
}

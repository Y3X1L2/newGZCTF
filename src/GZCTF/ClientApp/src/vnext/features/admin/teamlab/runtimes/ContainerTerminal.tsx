import { RefreshCw } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../../shared/Interaction'
import { errorMessage } from '../../../../shared/errors'
import styles from './RuntimePanels.module.css'

type Props = {
  sessionId: string | null
  closing: boolean
  closeError: unknown
  recreating: boolean
  returnFocus: () => void
  onClose: () => Promise<void>
  onRecreate: () => Promise<void>
}

export function ContainerTerminal({ sessionId, closing, closeError, recreating, returnFocus, onClose, onRecreate }: Props) {
  const [host, setHost] = useState<HTMLDivElement | null>(null)
  const [status, setStatus] = useState('正在连接终端')
  const [disconnected, setDisconnected] = useState(false)
  const socket = useRef<WebSocket | null>(null)
  const wasOpen = useRef(false)

  useEffect(() => {
    if (!sessionId || !host) return
    let disposed = false
    const resources: (() => void)[] = []
    const cleanup = () => { for (const release of resources.splice(0).reverse()) release() }
    setStatus('正在连接终端')
    setDisconnected(false)
    void Promise.all([import('@xterm/xterm'), import('@xterm/addon-fit')]).then(([{ Terminal }, { FitAddon }]) => {
      if (disposed) return
      const css = getComputedStyle(host)
      const terminal = new Terminal({
        cursorBlink: !window.matchMedia('(prefers-reduced-motion: reduce)').matches,
        fontSize: 14,
        fontFamily: css.getPropertyValue('--yn-font-mono').trim() || 'monospace',
        scrollback: 5000,
        theme: {
          background: css.getPropertyValue('--yn-color-surface').trim(),
          foreground: css.getPropertyValue('--yn-color-text').trim(),
          cursor: css.getPropertyValue('--yn-color-text').trim(),
        },
      })
      resources.push(() => terminal.dispose())
      const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)')
      const applyTheme = () => {
        const tokens = getComputedStyle(host)
        terminal.options.theme = {
          background: tokens.getPropertyValue('--yn-color-surface').trim(),
          foreground: tokens.getPropertyValue('--yn-color-text').trim(),
          cursor: tokens.getPropertyValue('--yn-color-text').trim(),
        }
        terminal.options.cursorBlink = !reducedMotion.matches
      }
      const themeObserver = new MutationObserver(applyTheme)
      themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['data-yinyu-theme'] })
      reducedMotion.addEventListener('change', applyTheme)
      resources.push(() => { themeObserver.disconnect(); reducedMotion.removeEventListener('change', applyTheme) })
      const fit = new FitAddon()
      terminal.loadAddon(fit)
      terminal.open(host)
      fit.fit()
      const scheme = window.location.protocol === 'https:' ? 'wss' : 'ws'
      const ws = new WebSocket(`${scheme}://${window.location.host}/api/admin/teamlab/remote-sessions/${sessionId}/terminal`)
      resources.push(() => {
        ws.onopen = ws.onmessage = ws.onerror = ws.onclose = null
        ws.close()
        if (socket.current === ws) socket.current = null
      })
      ws.binaryType = 'arraybuffer'
      socket.current = ws
      let transportFailure: string | null = null
      const failTransport = (message: string) => {
        transportFailure = message
        setStatus(message)
        setDisconnected(true)
        ws.close()
      }
      const connectTimeout = window.setTimeout(() => {
        if (!disposed && ws.readyState === WebSocket.CONNECTING) failTransport('终端连接超时，请检查节点或重新创建会话')
      }, 30_000)
      resources.push(() => window.clearTimeout(connectTimeout))
      const send = (data: string) => {
        if (ws.readyState !== WebSocket.OPEN) return
        const bytes = new TextEncoder().encode(data)
        if (ws.bufferedAmount + bytes.length > 1024 * 1024) { failTransport('终端输入积压过多，连接已关闭'); return }
        for (let offset = 0; offset < bytes.length; offset += 16384) ws.send(bytes.subarray(offset, offset + 16384))
      }
      const input = terminal.onData(send)
      resources.push(() => input.dispose())
      const binary = terminal.onBinary((data) => {
        if (ws.readyState !== WebSocket.OPEN) return
        if (ws.bufferedAmount + data.length > 1024 * 1024) { failTransport('终端输入积压过多，连接已关闭'); return }
        ws.send(Uint8Array.from(data, (char) => char.charCodeAt(0)))
      })
      resources.push(() => binary.dispose())
      const resize = () => {
        if (host.clientWidth === 0 || host.clientHeight === 0) return
        fit.fit()
        if (ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify({ type: 'resize', cols: Math.max(2, Math.min(500, terminal.cols)), rows: Math.max(1, Math.min(300, terminal.rows)) }))
      }
      const observer = new ResizeObserver(resize)
      resources.push(() => observer.disconnect())
      observer.observe(host)
      ws.onopen = () => {
        window.clearTimeout(connectTimeout)
        if (disposed) return
        setStatus('已连接')
        resize()
        terminal.focus()
      }
      let queuedOutputBytes = 0
      ws.onmessage = (event: MessageEvent<ArrayBuffer | string>) => {
        if (disposed || transportFailure) return
        const length = typeof event.data === 'string' ? event.data.length * 2 : event.data.byteLength
        queuedOutputBytes += length
        if (queuedOutputBytes > 4 * 1024 * 1024) { failTransport('终端输出过快，连接已关闭'); return }
        terminal.write(typeof event.data === 'string' ? event.data : new Uint8Array(event.data), () => { queuedOutputBytes -= length })
      }
      ws.onerror = () => {
        window.clearTimeout(connectTimeout)
        if (!disposed) { setStatus('终端连接失败'); setDisconnected(true) }
      }
      ws.onclose = () => {
        window.clearTimeout(connectTimeout)
        if (!disposed) { setStatus(transportFailure ?? '终端已断开'); setDisconnected(true) }
      }
    }).catch(() => {
      cleanup()
      if (!disposed) { setStatus('终端加载失败'); setDisconnected(true) }
    })
    return () => { disposed = true; cleanup() }
  }, [host, sessionId])

  useEffect(() => { if (closing) socket.current?.close() }, [closing])
  useEffect(() => {
    if (sessionId) { wasOpen.current = true; return }
    if (wasOpen.current) { wasOpen.current = false; returnFocus() }
  }, [returnFocus, sessionId])

  return <VNextDialog closeDisabled={closing || recreating} eyebrow="容器终端" footer={<>
    <ActionButton disabled={closing || recreating || socket.current?.readyState !== WebSocket.OPEN} type="button"
      onClick={() => { if (socket.current?.readyState === WebSocket.OPEN) socket.current.send(new Uint8Array([3])) }}>中断命令</ActionButton>
    <ActionButton disabled={closing || recreating} onClick={() => void onClose()} type="button">{closing ? '正在关闭终端' : '关闭终端'}</ActionButton>
  </>} onClose={() => void onClose()} open={sessionId !== null} title="容器终端" wide>
    <div className={styles.terminalSurface}>
      <div aria-label="交互终端" className={styles.terminalHost} ref={setHost} />
      <span role="status">{status}</span>
      {closeError ? <InlineFeedback tone="danger">{errorMessage(closeError, '会话清理未完成，请重试。')}</InlineFeedback> : null}
      {disconnected ? <ActionButton disabled={closing || recreating} icon={<RefreshCw size={14} />} onClick={() => void onRecreate()} type="button">{recreating ? '正在重新创建' : '重新创建终端'}</ActionButton> : null}
    </div>
  </VNextDialog>
}

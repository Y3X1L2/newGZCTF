import { ActionIcon, Button, Modal, Textarea } from '@mantine/core'
import RFB from '@novnc/novnc'
import { FitAddon } from '@xterm/addon-fit'
import { Terminal } from '@xterm/xterm'
import { Maximize2, Minimize2, Monitor, Settings2, SquareTerminal, Clipboard } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import '@xterm/xterm/css/xterm.css'
import { useWorkspaceApi } from '../../api/client'
import styles from './ConsoleWorkspace.module.css'
import { RDPDisplay, type RDPControls } from './RDPDisplay'
import type { ConsoleSession } from './consoles'

export default function ConsoleWorkspace({
  environmentId,
  session,
  onSettings,
}: {
  environmentId: string
  session: ConsoleSession
  onSettings?: () => void
}) {
  const workspace = useRef<HTMLElement>(null)
  const [fullscreen, setFullscreen] = useState(false)
  useEffect(() => {
    const changed = () => setFullscreen(document.fullscreenElement === workspace.current)
    document.addEventListener('fullscreenchange', changed)
    return () => document.removeEventListener('fullscreenchange', changed)
  }, [])
  const label = { vnc: '控制台', serial: '串口', ssh: 'SSH', rdp: '远程桌面', terminal: '终端' }[session.kind]
  return (
    <section ref={workspace} className={styles.workspace} aria-label="资产连接">
      <header className={styles.toolbar}>
        <h1 className={styles.title}>
          {session.kind === 'vnc' || session.kind === 'rdp' ? <Monitor size={17} /> : <SquareTerminal size={17} />}
          {session.name}
          <span>{label}</span>
        </h1>
        <div className={styles.actions}>
          {onSettings && (
            <ActionIcon variant="subtle" color="gray" aria-label="连接设置" onClick={onSettings}>
              <Settings2 size={16} />
            </ActionIcon>
          )}
          <ActionIcon
            variant="subtle"
            color="gray"
            aria-label={fullscreen ? '退出全屏连接' : '全屏连接'}
            onClick={() => {
              void (fullscreen ? document.exitFullscreen() : workspace.current?.requestFullscreen())
            }}
          >
            {fullscreen ? <Minimize2 size={16} /> : <Maximize2 size={16} />}
          </ActionIcon>
        </div>
      </header>
      <ConsoleConnection session={session} environmentId={environmentId} />
    </section>
  )
}

function ConsoleConnection({ session, environmentId }: { session: ConsoleSession; environmentId: string }) {
  const api = useWorkspaceApi()
  const host = useRef<HTMLDivElement>(null)
  const [status, setStatus] = useState('连接中')
  const [attempt, setAttempt] = useState(0)
  const vnc = useRef<RFB | null>(null)
  const fit = useRef<FitAddon | null>(null)
  const rdp = useRef<RDPControls>(null)
  const [clipboardOpened, setClipboardOpened] = useState(false)
  const [clipboardText, setClipboardText] = useState('')
  useEffect(() => {
    if (session.kind === 'rdp') return
    const element = host.current!
    const url = new URL(
      api.path(
        `/environments/${encodeURIComponent(environmentId)}/assets/${encodeURIComponent(session.id)}/console?kind=${session.kind}`
      ),
      window.location.href
    )
    url.protocol = location.protocol === 'https:' ? 'wss:' : 'ws:'
    setStatus('连接中')
    if (session.kind === 'vnc') {
      const connection = new RFB(element, url.href, {
        wsProtocols: ['binary'],
      })
      vnc.current = connection
      connection.scaleViewport = true
      connection.resizeSession = true
      const connected = () => setStatus('已连接')
      const disconnected = (event: CustomEvent<{ clean: boolean }>) =>
        setStatus(event.detail.clean ? '连接已结束' : '连接中断')
      const failed = (event: CustomEvent<{ reason?: string }>) => setStatus(event.detail.reason ?? '控制台认证失败')
      connection.addEventListener('connect', connected)
      connection.addEventListener('disconnect', disconnected)
      connection.addEventListener('securityfailure', failed)
      return () => {
        connection.removeEventListener('connect', connected)
        connection.removeEventListener('disconnect', disconnected)
        connection.removeEventListener('securityfailure', failed)
        connection.disconnect()
        vnc.current = null
      }
    }
    const terminalStyle = getComputedStyle(element)
    const terminal = new Terminal({
      cursorBlink: !window.matchMedia('(prefers-reduced-motion: reduce)').matches,
      fontSize: 13,
      fontFamily: terminalStyle.getPropertyValue('--font-mono').trim(),
      theme: {
        background: terminalStyle.getPropertyValue('--terminal-bg').trim(),
        foreground: terminalStyle.getPropertyValue('--terminal-text').trim(),
        cursor: terminalStyle.getPropertyValue('--terminal-cursor').trim(),
        selectionBackground: terminalStyle.getPropertyValue('--terminal-selection').trim(),
      },
    })
    const fitter = new FitAddon()
    fit.current = fitter
    terminal.loadAddon(fitter)
    terminal.open(element)
    fitter.fit()
    const socket = new WebSocket(url)
    socket.binaryType = 'arraybuffer'
    socket.onopen = () => {
      setStatus('已连接')
      fitter.fit()
      if (session.kind === 'terminal' || session.kind === 'ssh')
        socket.send(JSON.stringify({ cols: terminal.cols, rows: terminal.rows }))
      terminal.focus()
    }
    socket.onmessage = (event) => terminal.write(new Uint8Array(event.data as ArrayBuffer))
    socket.onclose = (event) => setStatus(event.reason || (event.code === 1000 ? '连接已结束' : '连接中断'))
    const input = terminal.onData((data) => {
      if (socket.readyState === WebSocket.OPEN) socket.send(new TextEncoder().encode(data))
    })
    const resize = terminal.onResize((size) => {
      if ((session.kind === 'terminal' || session.kind === 'ssh') && socket.readyState === WebSocket.OPEN)
        socket.send(JSON.stringify(size))
    })
    const observer = new ResizeObserver(() => {
      if (element.clientWidth && element.clientHeight) fitter.fit()
    })
    observer.observe(element)
    return () => {
      input.dispose()
      resize.dispose()
      observer.disconnect()
      socket.onopen = null
      socket.onmessage = null
      socket.onclose = null
      socket.close()
      terminal.dispose()
      fit.current = null
    }
  }, [environmentId, session.id, session.kind, session.revision, attempt])
  return (
    <div className={styles.connection} role="region" aria-label="控制台内容">
      <div className={styles.status}>
        <span role="status" className={status === '已连接' ? styles.connected : ''}>
          <i />
          {status}
        </span>
        <div className={styles.actions}>
          {session.kind === 'rdp' && status === '已连接' && (
            <ActionIcon
              variant="subtle"
              color="gray"
              aria-label="远程剪贴板"
              onClick={() => {
                setClipboardText(rdp.current?.clipboard() ?? '')
                setClipboardOpened(true)
              }}
            >
              <Clipboard size={14} />
            </ActionIcon>
          )}
          {(session.kind === 'vnc' || session.kind === 'rdp') && status === '已连接' && (
            <Button
              variant="subtle"
              size="compact-xs"
              onClick={() => (session.kind === 'rdp' ? rdp.current?.attention() : vnc.current?.sendCtrlAltDel())}
            >
              Ctrl · Alt · Del
            </Button>
          )}
          {status !== '已连接' && status !== '连接中' && (
            <Button variant="subtle" size="compact-xs" onClick={() => setAttempt((value) => value + 1)}>
              重新连接
            </Button>
          )}
        </div>
      </div>
      {session.kind === 'rdp' ? (
        <RDPDisplay
          environmentId={environmentId}
          assetId={session.id}
          attempt={attempt + (session.revision ?? 0)}
          active
          onStatus={setStatus}
          controls={rdp}
        />
      ) : (
        <div ref={host} className={`${styles.surface} ${session.kind === 'vnc' ? styles.vnc : styles.terminal}`} />
      )}
      <Modal
        opened={clipboardOpened}
        withinPortal={false}
        title="远程剪贴板"
        onClose={() => setClipboardOpened(false)}
        centered
      >
        <Textarea
          aria-label="剪贴板内容"
          value={clipboardText}
          onChange={(event) => setClipboardText(event.currentTarget.value)}
          minRows={5}
          maxRows={12}
          autosize
          autoFocus
        />
        <div className={styles.clipboardActions}>
          <Button variant="default" onClick={() => setClipboardOpened(false)}>
            关闭
          </Button>
          <Button
            onClick={() => {
              rdp.current?.paste(clipboardText)
              setClipboardOpened(false)
            }}
          >
            发送
          </Button>
        </div>
      </Modal>
    </div>
  )
}

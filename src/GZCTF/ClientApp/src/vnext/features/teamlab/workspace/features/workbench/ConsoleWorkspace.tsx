import { ActionIcon, Button, Modal, Textarea } from '@mantine/core'
import RFB from '@novnc/novnc'
import { FitAddon } from '@xterm/addon-fit'
import { Terminal } from '@xterm/xterm'
import {
  Clipboard,
  Maximize2,
  Minimize2,
  PanelBottomClose,
  PanelBottomOpen,
  Monitor,
  Settings2,
  SquareTerminal,
  X,
} from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import '@xterm/xterm/css/xterm.css'
import { useWorkspaceApi } from '../../api/client'
import styles from './ConsoleWorkspace.module.css'
import { RDPDisplay, type RDPControls } from './RDPDisplay'
import { consoleKey, type ConsoleTab } from './consoles'

export default function ConsoleWorkspace({
  environmentId,
  tabs,
  selected,
  onSelect,
  onClose,
  onSettings,
}: {
  environmentId: string
  tabs: ConsoleTab[]
  selected: string
  onSelect: (key: string) => void
  onClose: (key: string) => void
  onSettings: (tab: ConsoleTab) => void
}) {
  const workspace = useRef<HTMLElement>(null)
  const [collapsed, setCollapsed] = useState(false)
  const [fullscreen, setFullscreen] = useState(false)
  const current = tabs.find((tab) => consoleKey(tab) === selected)
  useEffect(() => setCollapsed(false), [selected])
  useEffect(() => {
    const changed = () => setFullscreen(document.fullscreenElement === workspace.current)
    document.addEventListener('fullscreenchange', changed)
    return () => document.removeEventListener('fullscreenchange', changed)
  }, [])
  return (
    <section ref={workspace} className={styles.workspace} data-collapsed={collapsed} aria-label="资产连接">
      <div className={styles.toolbar}>
        <div className={styles.tabs} role="tablist" aria-label="当前连接">
          {tabs.map((tab) => {
            const key = consoleKey(tab)
            return (
              <div className={`${styles.tab} ${selected === key ? styles.active : ''}`} key={key}>
                <button
                  role="tab"
                  aria-selected={selected === key}
                  aria-controls={`console-${key}`}
                  onClick={() => {
                    onSelect(key)
                    setCollapsed(false)
                  }}
                >
                  {tab.kind === 'vnc' || tab.kind === 'rdp' ? <Monitor size={14} /> : <SquareTerminal size={14} />}
                  {tab.name}
                  {tab.kind === 'serial' && ' · 串口'}
                  {tab.kind === 'ssh' && ' · SSH'}
                  {tab.kind === 'rdp' && ' · RDP'}
                </button>
                <ActionIcon
                  variant="subtle"
                  color="gray"
                  aria-label={`结束 ${tab.name} 连接`}
                  onClick={() => onClose(key)}
                >
                  <X size={14} />
                </ActionIcon>
              </div>
            )
          })}
        </div>
        <div className={styles.actions}>
          {!fullscreen && (
            <ActionIcon
              variant="subtle"
              color="gray"
              aria-label={collapsed ? '展开连接' : '收起连接'}
              onClick={() => setCollapsed(!collapsed)}
            >
              {collapsed ? <PanelBottomOpen size={16} /> : <PanelBottomClose size={16} />}
            </ActionIcon>
          )}
          {(current?.kind === 'ssh' || current?.kind === 'rdp') && (
            <ActionIcon variant="subtle" color="gray" aria-label="连接设置" onClick={() => onSettings(current)}>
              <Settings2 size={16} />
            </ActionIcon>
          )}
          <ActionIcon
            variant="subtle"
            color="gray"
            aria-label={fullscreen ? '退出全屏连接' : '全屏连接'}
            onClick={() => {
              setCollapsed(false)
              void (fullscreen ? document.exitFullscreen() : workspace.current?.requestFullscreen())
            }}
          >
            {fullscreen ? <Minimize2 size={16} /> : <Maximize2 size={16} />}
          </ActionIcon>
        </div>
      </div>
      {tabs.map((tab) => (
        <ConsoleConnection
          key={consoleKey(tab)}
          tab={tab}
          environmentId={environmentId}
          active={!collapsed && selected === consoleKey(tab)}
        />
      ))}
    </section>
  )
}

function ConsoleConnection({
  tab,
  environmentId,
  active,
}: {
  tab: ConsoleTab
  environmentId: string
  active: boolean
}) {
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
    if (active) fit.current?.fit()
  }, [active])
  useEffect(() => {
    if (tab.kind === 'rdp') return
    const element = host.current!
    const url = new URL(
      api.path(
        `/environments/${encodeURIComponent(environmentId)}/assets/${encodeURIComponent(tab.id)}/console?kind=${tab.kind}`
      ),
      window.location.href
    )
    url.protocol = location.protocol === 'https:' ? 'wss:' : 'ws:'
    setStatus('连接中')
    if (tab.kind === 'vnc') {
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
      if (tab.kind === 'terminal' || tab.kind === 'ssh')
        socket.send(JSON.stringify({ cols: terminal.cols, rows: terminal.rows }))
      terminal.focus()
    }
    socket.onmessage = (event) => terminal.write(new Uint8Array(event.data as ArrayBuffer))
    socket.onclose = (event) => setStatus(event.reason || (event.code === 1000 ? '连接已结束' : '连接中断'))
    const input = terminal.onData((data) => {
      if (socket.readyState === WebSocket.OPEN) socket.send(new TextEncoder().encode(data))
    })
    const resize = terminal.onResize((size) => {
      if ((tab.kind === 'terminal' || tab.kind === 'ssh') && socket.readyState === WebSocket.OPEN)
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
  }, [environmentId, tab.id, tab.kind, tab.revision, attempt])
  return (
    <div id={`console-${consoleKey(tab)}`} className={styles.connection} role="tabpanel" hidden={!active}>
      <div className={styles.status}>
        <span role="status" className={status === '已连接' ? styles.connected : ''}>
          <i />
          {status}
        </span>
        <div className={styles.actions}>
          {tab.kind === 'rdp' && status === '已连接' && (
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
          {(tab.kind === 'vnc' || tab.kind === 'rdp') && status === '已连接' && (
            <Button
              variant="subtle"
              size="compact-xs"
              onClick={() => (tab.kind === 'rdp' ? rdp.current?.attention() : vnc.current?.sendCtrlAltDel())}
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
      {tab.kind === 'rdp' ? (
        <RDPDisplay
          environmentId={environmentId}
          assetId={tab.id}
          attempt={attempt + (tab.revision ?? 0)}
          active={active}
          onStatus={setStatus}
          controls={rdp}
        />
      ) : (
        <div ref={host} className={`${styles.surface} ${tab.kind === 'vnc' ? styles.vnc : styles.terminal}`} />
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

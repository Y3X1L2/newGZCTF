import { useEffect, useImperativeHandle, useRef, type Ref } from 'react'
import { useWorkspaceApi } from '../../api/client'
import Guacamole from '../../vendor/guacamole'
import styles from './ConsoleWorkspace.module.css'

export interface RDPControls {
  attention(): void
  clipboard(): string
  paste(text: string): void
}

export function RDPDisplay({
  environmentId,
  assetId,
  attempt,
  active,
  onStatus,
  controls,
}: {
  environmentId: string
  assetId: string
  attempt: number
  active: boolean
  onStatus: (status: string) => void
  controls: Ref<RDPControls>
}) {
  const api = useWorkspaceApi()
  const host = useRef<HTMLDivElement>(null)
  const client = useRef<Guacamole.Client>(null)
  const clipboard = useRef('')
  const activated = useRef(active)
  activated.current = active
  useImperativeHandle(
    controls,
    () => ({
      attention: () => {
        const keys = [0xffe3, 0xffe9, 0xffff]
        keys.forEach((key) => client.current?.sendKeyEvent(1, key))
        keys.reverse().forEach((key) => client.current?.sendKeyEvent(0, key))
        client.current?.getDisplay().getElement().focus()
      },
      clipboard: () => clipboard.current,
      paste: (text: string) => {
        const stream = client.current!.createClipboardStream('text/plain')
        const writer = new Guacamole.StringWriter(stream)
        writer.sendText(text)
        writer.sendEnd()
        client.current?.getDisplay().getElement().focus()
      },
    }),
    []
  )
  useEffect(() => {
    const element = host.current!
    onStatus('连接中')
    let failed = false
    const url = new URL(
      api.path(`/environments/${encodeURIComponent(environmentId)}/assets/${encodeURIComponent(assetId)}/console`),
      window.location.href
    )
    url.protocol = location.protocol === 'https:' ? 'wss:' : 'ws:'
    const connection = new Guacamole.Client(new Guacamole.WebSocketTunnel(url.href))
    client.current = connection
    const display = connection.getDisplay()
    const screen = display.getElement()
    screen.tabIndex = 0
    screen.setAttribute('aria-label', '远程桌面画面')
    element.appendChild(screen)
    const fit = () => {
      if (display.getWidth() && display.getHeight() && element.clientWidth && element.clientHeight)
        display.scale(Math.min(element.clientWidth / display.getWidth(), element.clientHeight / display.getHeight()))
    }
    const resize = () => {
      if (!element.clientWidth || !element.clientHeight) return
      connection.sendSize(
        Math.max(320, Math.round(element.clientWidth)),
        Math.max(200, Math.round(element.clientHeight))
      )
      fit()
    }
    display.onresize = fit
    connection.onstatechange = (state) => {
      if (state === 3) {
        onStatus('已连接')
        if (activated.current) screen.focus()
        resize()
      }
      if (state === 5 && !failed) onStatus('连接已结束')
    }
    connection.onerror = (error) => {
      failed = true
      onStatus(error.message || '远程桌面连接失败')
    }
    connection.onclipboard = (stream, type) => {
      if (type !== 'text/plain') return
      const reader = new Guacamole.StringReader(stream)
      let text = ''
      reader.ontext = (value) => {
        text += value
      }
      reader.onend = () => {
        clipboard.current = text
      }
    }
    for (const pointer of [new Guacamole.Mouse(screen), new Guacamole.Mouse.Touchscreen(screen)])
      pointer.onEach(['mousedown', 'mouseup', 'mousemove'], (event) => {
        event.preventDefault()
        if (activated.current) {
          screen.focus()
          connection.sendMouseState(event.state, true)
        }
      })
    const keyboard = new Guacamole.Keyboard(screen)
    keyboard.onkeydown = (key) => {
      connection.sendKeyEvent(1, key)
      return false
    }
    keyboard.onkeyup = (key) => connection.sendKeyEvent(0, key)
    screen.addEventListener('blur', () => keyboard.reset())
    const observer = new ResizeObserver(resize)
    observer.observe(element)
    connection.connect(
      new URLSearchParams({
        kind: 'rdp',
        width: String(Math.max(640, element.clientWidth)),
        height: String(Math.max(480, element.clientHeight)),
        dpi: '96',
      }).toString()
    )
    return () => {
      observer.disconnect()
      keyboard.reset()
      connection.onstatechange = null
      connection.onerror = null
      connection.onclipboard = null
      display.onresize = null
      connection.disconnect()
      screen.remove()
      client.current = null
      clipboard.current = ''
    }
  }, [environmentId, assetId, attempt, onStatus])
  useEffect(() => {
    if (active) client.current?.getDisplay().getElement().focus()
  }, [active])
  return <div ref={host} className={`${styles.surface} ${styles.desktop}`} />
}

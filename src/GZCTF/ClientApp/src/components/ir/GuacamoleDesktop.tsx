import { Alert, Text, Loader } from '@mantine/core'
import { useEffect, useRef, useState } from 'react'

interface GuacamoleDesktopProps {
  connectionUrl: string
  token: string
}

type ConnectionState = 'connecting' | 'connected' | 'disconnected' | 'error'

export default function GuacamoleDesktop({ connectionUrl, token }: GuacamoleDesktopProps) {
  const containerRef = useRef<HTMLDivElement>(null)
  const [state, setState] = useState<ConnectionState>('connecting')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let display: HTMLDivElement | null = null

    const initGuacamole = async () => {
      try {
        // Guacamole JS client is loaded via CDN or bundled import
        // This component assumes guacamole-common-js is available in the window scope
        // In production, install: pnpm add guacamole-common-js
        const Guacamole = (window as unknown as Record<string, unknown>).Guacamole as any
        const GuacClient = Guacamole?.Client
        const GuacDisplay = Guacamole?.Display

        if (!GuacClient || !GuacDisplay) {
          setState('error')
          setError('Guacamole 客户端库未加载。请联系管理员。')
          return
        }

        const tunnel = new Guacamole.HTTPTunnel(connectionUrl, false, { token })

        const client = new GuacClient(tunnel)
        display = new GuacDisplay(client.getDisplay())

        if (containerRef.current && display) {
          display.classList.add('guac-display')
          containerRef.current.innerHTML = ''
          containerRef.current.appendChild(display)
        }

        client.onstatechange = (guacState: number) => {
          // 0=IDLE, 1=CONNECTING, 2=WAITING, 3=CONNECTED, 4=DISCONNECTING, 5=DISCONNECTED
          if (guacState === 3) setState('connected')
          else if (guacState === 5) setState('disconnected')
        }

        client.onerror = (guacError: { message: string }) => {
          setState('error')
          setError(guacError.message || '连接错误')
        }

        client.connect()
      } catch (e) {
        setState('error')
        setError(e instanceof Error ? e.message : '初始化失败')
      }
    }

    initGuacamole()

    return () => {
      if (display && containerRef.current) {
        containerRef.current.innerHTML = ''
      }
    }
  }, [connectionUrl, token])

  return (
    <div>
      {state === 'connecting' && (
        <Alert color="blue">
          <Loader size="sm" mr="sm" />
          <Text span>正在连接远程桌面...</Text>
        </Alert>
      )}
      {state === 'error' && (
        <Alert color="red">
          <Text>连接失败: {error}</Text>
        </Alert>
      )}
      {state === 'disconnected' && (
        <Alert color="yellow">
          <Text>远程桌面连接已断开</Text>
        </Alert>
      )}
      <div
        ref={containerRef}
        style={{
          width: '100%',
          minHeight: state === 'connected' ? '600px' : '0',
          background: '#1a1a2e',
          borderRadius: '4px',
          overflow: 'hidden',
        }}
      />
    </div>
  )
}

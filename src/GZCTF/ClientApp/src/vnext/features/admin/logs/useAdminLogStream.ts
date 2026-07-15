import * as signalR from '@microsoft/signalr'
import { useCallback, useEffect, useRef, useState } from 'react'
import type { AdminLogEntry } from '../api'
import { appendAdminLogBuffer } from './adminLogBuffer'
import { adminLogKey } from './adminLogPresentation'

export type AdminLogConnectionState = 'connected' | 'connecting' | 'disconnected' | 'reconnecting'

export function useAdminLogStream() {
  const connectionRef = useRef<signalR.HubConnection | null>(null)
  const bufferRef = useRef<AdminLogEntry[]>([])
  const [connectionState, setConnectionState] = useState<AdminLogConnectionState>('connecting')
  const [buffered, setBuffered] = useState<AdminLogEntry[]>([])
  const [dropped, setDropped] = useState(0)

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hub/admin')
      .withHubProtocol(new signalR.JsonHubProtocol())
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
      .configureLogging(signalR.LogLevel.None)
      .build()
    connectionRef.current = connection

    connection.on('ReceivedLog', (message: AdminLogEntry) => {
      const current = bufferRef.current
      const result = appendAdminLogBuffer(current, message)
      if (result.items === current) return
      if (result.dropped) setDropped((value) => value + result.dropped)
      bufferRef.current = result.items
      setBuffered(result.items)
    })
    connection.onreconnecting(() => setConnectionState('reconnecting'))
    connection.onreconnected(() => setConnectionState('connected'))
    connection.onclose(() => setConnectionState('disconnected'))
    connection
      .start()
      .then(() => setConnectionState('connected'))
      .catch(() => setConnectionState('disconnected'))

    return () => {
      connectionRef.current = null
      void connection.stop()
    }
  }, [])

  const retry = useCallback(async () => {
    const connection = connectionRef.current
    if (!connection || connection.state !== signalR.HubConnectionState.Disconnected) return
    setConnectionState('connecting')
    try {
      await connection.start()
      setConnectionState('connected')
    } catch {
      setConnectionState('disconnected')
    }
  }, [])

  const consume = useCallback((keys?: Set<string>) => {
    const next = keys ? bufferRef.current.filter((item) => !keys.has(adminLogKey(item))) : []
    bufferRef.current = next
    setBuffered(next)
  }, [])

  return { buffered, dropped, connectionState, consume, retry }
}

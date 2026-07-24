import * as signalR from '@microsoft/signalr'
import { useEffect, useRef, useState } from 'react'

export type AwdpMonitorState = 'connected' | 'connecting' | 'offline' | 'reconnecting'

export function useAwdpMonitor(gameId: number, enabled: boolean, onSnapshotRequired: () => void) {
  const [state, setState] = useState<AwdpMonitorState>('connecting')
  const callbackRef = useRef(onSnapshotRequired)
  const refreshTimerRef = useRef<number | null>(null)

  useEffect(() => {
    callbackRef.current = onSnapshotRequired
  }, [onSnapshotRequired])

  useEffect(() => {
    if (!enabled) return undefined
    let disposed = false
    const requestSnapshot = () => {
      if (refreshTimerRef.current !== null) return
      refreshTimerRef.current = window.setTimeout(() => {
        refreshTimerRef.current = null
        callbackRef.current()
      }, 180)
    }
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hub/monitor?game=${gameId}`)
      .withHubProtocol(new signalR.JsonHubProtocol())
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.None)
      .build()

    connection.serverTimeoutInMilliseconds = 2 * 60 * 60 * 1000
    connection.on('ReceivedAwdpRoundChange', requestSnapshot)
    connection.on('ReceivedAwdpServiceStatusChange', requestSnapshot)
    connection.on('ReceivedAwdpPatchResult', requestSnapshot)
    connection.onreconnecting(() => setState('reconnecting'))
    connection.onreconnected(() => {
      setState('connected')
      requestSnapshot()
    })
    connection.onclose(() => setState('offline'))

    setState('connecting')
    void connection.start().then(
      () => {
        if (!disposed) setState('connected')
      },
      () => {
        if (!disposed) setState('offline')
      }
    )

    return () => {
      disposed = true
      if (refreshTimerRef.current !== null) window.clearTimeout(refreshTimerRef.current)
      refreshTimerRef.current = null
      void connection.stop()
    }
  }, [enabled, gameId])

  return state
}

import { Monitor, RefreshCw, Terminal, Unplug } from 'lucide-react'
import { useCallback, useEffect, useRef, useState } from 'react'
import useSWR from 'swr'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { errorMessage } from '../../../../shared/errors'
import { teamLabRemoteAccessApi, type TeamLabRuntime } from '../api'
import { ContainerTerminal } from './ContainerTerminal'
import styles from './RuntimePanels.module.css'

export function RuntimeRemoteAccessPanel({ runtime, assetId }: { runtime: TeamLabRuntime; assetId: number }) {
  const asset = runtime.assets.find(item => item.id === assetId)!
  const [acting, setActing] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const [terminalId, setTerminalId] = useState<string | null>(null)
  const ownedTerminal = useRef<string | null>(null)
  const mounted = useRef(false)
  const busy = useRef(false)
  const trigger = useRef<HTMLButtonElement | null>(null)
  const availability = useSWR(['teamlab:remote-access', runtime.id, assetId],
    () => teamLabRemoteAccessApi.getAvailability(runtime.id, assetId))
  const sessions = useSWR(['teamlab:asset-sessions', runtime.id, assetId],
    () => teamLabRemoteAccessApi.list({ runtimeId: runtime.id }))
  const active = sessions.data?.items.filter(item => item.session.assetId === assetId &&
    ['creating', 'ready', 'connected', 'ending'].includes(item.session.status)) ?? []

  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
      if (ownedTerminal.current) void teamLabRemoteAccessApi.end(ownedTerminal.current).catch(setError)
    }
  }, [])

  const open = async (console: boolean, button?: HTMLButtonElement) => {
    if (busy.current) return
    busy.current = true
    trigger.current = button ?? trigger.current
    setActing(true)
    setError(null)
    // Open before awaiting the session so browsers allow the remote window.
    const popup = asset.kind === 'vm' ? window.open('about:blank', '_blank') : null
    if (popup) popup.opener = null
    let sessionId: string | null = null
    let connected = false
    try {
      if (asset.kind === 'vm' && !popup) throw new Error('请允许浏览器打开远程连接窗口。')
      const session = console
        ? await teamLabRemoteAccessApi.createConsoleSession(runtime.id, assetId)
        : await teamLabRemoteAccessApi.createSession(runtime.id, assetId)
      sessionId = session.id
      if (!mounted.current) return
      if (session.protocol === 'containerTerminal') {
        ownedTerminal.current = session.id
        setTerminalId(session.id)
      } else {
        const connection = await teamLabRemoteAccessApi.connect(session.id)
        if (!mounted.current || popup?.closed) return
        popup!.location.href = connection.url
      }
      connected = true
      await sessions.mutate()
    } catch (failure) {
      if (mounted.current) setError(failure)
    } finally {
      if (!connected) {
        popup?.close()
        if (sessionId) {
          try { await teamLabRemoteAccessApi.end(sessionId) }
          catch (failure) { if (mounted.current) setError(failure) }
        }
      }
      busy.current = false
      if (mounted.current) setActing(false)
    }
  }

  const end = useCallback(async (sessionId: string) => {
    setActing(true)
    setError(null)
    try {
      await teamLabRemoteAccessApi.end(sessionId)
      if (ownedTerminal.current === sessionId) {
        ownedTerminal.current = null
        setTerminalId(null)
      }
      await sessions.mutate()
      return true
    } catch (failure) { setError(failure); return false }
    finally { setActing(false) }
  }, [sessions.mutate])

  const usable = !!asset.runtimeResourceId && !['destroying', 'destroyed', 'cleanup-pending'].includes(runtime.status)
  const protocol = availability.data?.protocol === 'rdp' ? 'RDP' : 'SSH'
  return <section className={styles.panel} aria-label="远程连接">
    <header className={styles.panelHeader}><h3>远程连接</h3></header>
    <div className={styles.connectionActions}>
      <ActionButton disabled={!usable || acting || !!terminalId || availability.isLoading || !availability.data?.available}
        icon={asset.kind === 'vm' ? <Monitor size={17} /> : <Terminal size={17} />} tone="primary"
        onClick={event => void open(false, event.currentTarget)} type="button">
        {asset.kind === 'vm' ? protocol : '打开终端'}
      </ActionButton>
      {asset.kind === 'vm' ? <ActionButton disabled={!usable || acting} icon={<Monitor size={17} />}
        onClick={event => void open(true, event.currentTarget)} type="button">VNC</ActionButton> : null}
    </div>
    {availability.error ? <InlineFeedback tone="danger">{errorMessage(availability.error, '连接信息读取失败。')}
      <ActionButton aria-label="重新读取连接信息" icon={<RefreshCw size={15} />} onClick={() => void availability.mutate()} type="button" />
    </InlineFeedback> : availability.data?.unavailableReason ? <p className={styles.muted}>{availability.data.unavailableReason}</p> : null}
    {error ? <InlineFeedback tone="danger">{errorMessage(error, '远程连接失败。')}</InlineFeedback> : null}
    {sessions.error ? <InlineFeedback tone="danger">{errorMessage(sessions.error, '会话读取失败。')}</InlineFeedback> : null}
    {active.length ? <ul className={styles.assetSessions}>{active.map(({ session, requestedByName }) => <li key={session.id}>
      <span><strong>{requestedByName}</strong> · {session.protocol === 'containerTerminal' ? '终端' : session.protocol.toUpperCase()}</span>
      <ActionButton aria-label={`结束 ${requestedByName} 的连接`} disabled={acting} icon={<Unplug size={15} />}
        onClick={() => void end(session.id)} type="button" />
    </li>)}</ul> : null}
    <ContainerTerminal sessionId={terminalId} closing={acting} closeError={error} returnFocus={() => trigger.current?.focus()}
      onClose={async () => { if (ownedTerminal.current) await end(ownedTerminal.current) }}
      onRecreate={async () => { if (ownedTerminal.current && await end(ownedTerminal.current)) await open(false) }} recreating={acting} />
  </section>
}

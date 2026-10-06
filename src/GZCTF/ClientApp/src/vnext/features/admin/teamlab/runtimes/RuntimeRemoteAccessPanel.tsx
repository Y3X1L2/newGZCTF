import { Monitor, RefreshCw, Settings2, Terminal, Unplug } from 'lucide-react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
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
  const accessKind = asset.kind === 'docker' ? 'terminal' : asset.operatingSystem === 'windows' ? 'rdp'
    : asset.operatingSystem === 'linux' ? 'ssh' : null
  const capability = asset.capabilities?.find(item => item.kind === accessKind)
  const consoleCapability = asset.capabilities?.find(item => item.kind === 'console')
  const configured = capability?.status === 'configured-unverified'
  const protocolMatches = accessKind === 'terminal' ? availability.data?.protocol === 'containerTerminal'
    : availability.data?.protocol === accessKind
  const settingsId = capability?.settingsTemplateId ?? asset.sourceTemplateId
  const returnTo = encodeURIComponent(window.location.pathname + window.location.search)
  return <section className={styles.panel} aria-label="远程连接">
    <header className={styles.panelHeader}><h3>机器连接</h3></header>
    <div className={styles.connectionActions}>
      {configured && protocolMatches ? <ActionButton disabled={!usable || acting || !!terminalId || availability.isLoading || !availability.data?.available}
        icon={asset.kind === 'vm' ? <Monitor size={17} /> : <Terminal size={17} />} tone="primary"
        onClick={event => void open(false, event.currentTarget)} type="button">
        {accessKind === 'rdp' ? '远程桌面' : accessKind === 'ssh' ? 'SSH' : '打开终端'}
      </ActionButton> : null}
      {asset.kind === 'vm' && consoleCapability?.status === 'configured-unverified' ? <ActionButton disabled={!usable || acting} icon={<Monitor size={17} />}
        onClick={event => void open(true, event.currentTarget)} type="button">控制台</ActionButton> : null}
    </div>
    {capability?.status === 'unconfigured' ? <p className={styles.muted}>{capability.reason}{settingsId && accessKind !== 'terminal' ? <>。<Link to={`/admin/images?template=${settingsId}&remoteAccess=1&returnTo=${returnTo}`}><Settings2 size={15} />配置{accessKind === 'rdp' ? '远程桌面' : 'SSH'}入口</Link></> : null}</p> : null}
    {capability?.status === 'configured-unverified' ? <p className={styles.muted}>{asset.kind === 'docker'
      ? '容器终端已准备，实际连接需打开后确认。'
      : '运维入口已配置，来宾服务与账号尚未实测。修改镜像模板设置可能影响本环境后续新连接。'}</p> : null}
    {configured && availability.data && !protocolMatches ? <p className={styles.muted}>远程协议与当前能力记录不一致，请刷新运行状态或检查镜像设置。</p> : null}
    {capability?.status === 'currently-unavailable' ? <p className={styles.muted}>{capability.reason}</p> : null}
    {consoleCapability?.status === 'currently-unavailable' ? <p className={styles.muted}>控制台当前不可用：{consoleCapability.reason}</p> : null}
    {!accessKind ? <p className={styles.muted}>系统类型尚未确认，无法判断可用的远程协议。可使用虚拟机控制台。</p> : null}
    {availability.error ? <InlineFeedback tone="danger">{errorMessage(availability.error, '连接信息读取失败。')}
      <ActionButton aria-label="重新读取连接信息" icon={<RefreshCw size={15} />} onClick={() => void availability.mutate()} type="button" />
    </InlineFeedback> : configured && availability.data?.unavailableReason ? <p className={styles.muted}>{availability.data.unavailableReason}</p> : null}
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

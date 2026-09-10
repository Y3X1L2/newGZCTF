import { Monitor, RefreshCw, Terminal, Wrench } from 'lucide-react'
import { memo, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { teamLabRemoteAccessApi, type TeamLabRemoteAccessAvailability, type TeamLabRuntime } from '../api'
import styles from './RuntimePanels.module.css'
import { ContainerTerminal } from './ContainerTerminal'

type RuntimeRemoteAccessPanelProps = { runtime: TeamLabRuntime }

type AvailabilityBatch =
  | { state: 'loading' }
  | { state: 'error'; error: unknown }
  | { state: 'ready'; items: ReadonlyMap<number, TeamLabRemoteAccessAvailability> }

export const RuntimeRemoteAccessPanel = memo(function RuntimeRemoteAccessPanel({ runtime }: RuntimeRemoteAccessPanelProps) {
  const [selectedAssetId, setSelectedAssetId] = useState<number | null>(null)
  const [vncConsole, setVncConsole] = useState(false)
  const [reason, setReason] = useState('')
  const [acting, setActing] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const [terminalSessionId, setTerminalSessionId] = useState<string | null>(null)
  const [terminalClosing, setTerminalClosing] = useState(false)
  const [terminalCloseError, setTerminalCloseError] = useState<unknown>(null)
  const selected = runtime.assets.find((asset) => asset.id === selectedAssetId) ?? null
  const assets = useMemo(() => runtime.assets.filter((asset) => asset.status === 'running' ||
    asset.kind === 'vm' && asset.runtimeResourceId && !['destroying', 'destroyed', 'cleanup-pending'].includes(runtime.status)), [runtime.assets, runtime.status])
  const [batch, setBatch] = useState<AvailabilityBatch>({ state: 'loading' })
  const [retryNonce, setRetryNonce] = useState(0)
  const cancelled = useRef(false)
  const terminalClosingRef = useRef(false)
  const remoteTriggerRef = useRef<HTMLButtonElement | null>(null)
  const ownedSession = useRef<string | null>(null)
  const terminalRequest = useRef<{ assetId: number; reason: string } | null>(null)
  const [recreating, setRecreating] = useState(false)
  const mounted = useRef(false)
  const operationPending = useRef(false)
  const [pendingCleanup, setPendingCleanup] = useState<string | null>(null)

  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
      cancelled.current = true
      if (ownedSession.current) void teamLabRemoteAccessApi.end(ownedSession.current).catch(() => undefined)
    }
  }, [])

  // Re-check availability whenever the running-asset set changes (an asset may reach
  // running while the panel is open) and periodically while the panel is mounted.
  const assetSignature = assets.map((asset) => asset.id).join(',')
  useEffect(() => {
    let disposed = false
    const check = () => {
      setBatch((current) => (current.state === 'loading' ? current : { state: 'loading' }))
      void teamLabRemoteAccessApi
        .getAvailabilityBatch(runtime.id)
        .then((items) => {
          if (!disposed) setBatch({ state: 'ready', items: new Map(items.map((item) => [item.assetId, item])) })
        })
        .catch((batchError) => {
          if (!disposed) setBatch({ state: 'error', error: batchError })
        })
    }
    check()
    const timer = window.setInterval(check, 10_000)
    return () => {
      disposed = true
      window.clearInterval(timer)
    }
  }, [assetSignature, retryNonce, runtime.id])

  const close = useCallback(() => {
    cancelled.current = true
    setSelectedAssetId(null)
    setReason('')
    setError(null)
  }, [])

  const restoreRemoteFocus = useCallback(() => {
    remoteTriggerRef.current?.focus()
  }, [])

  const closeTerminal = useCallback(async () => {
    const sessionId = pendingCleanup ?? terminalSessionId
    if (!sessionId || terminalClosingRef.current || operationPending.current) return
    terminalClosingRef.current = true
    setTerminalClosing(true)
    setTerminalCloseError(null)
    try {
      await teamLabRemoteAccessApi.end(sessionId)
      ownedSession.current = null
      setTerminalSessionId(null)
      setPendingCleanup(null)
      terminalRequest.current = null
    } catch (closeError) {
      setTerminalCloseError(closeError)
    } finally {
      terminalClosingRef.current = false
      setTerminalClosing(false)
    }
  }, [pendingCleanup, terminalSessionId])

  const recreateTerminal = useCallback(async () => {
    if (!terminalRequest.current || operationPending.current || terminalClosingRef.current) return
    operationPending.current = true
    setRecreating(true)
    setTerminalCloseError(null)
    try {
      if (ownedSession.current) await teamLabRemoteAccessApi.end(ownedSession.current)
      ownedSession.current = null
      const request = terminalRequest.current
      const session = await teamLabRemoteAccessApi.createSession(runtime.id, request.assetId, request.reason)
      ownedSession.current = session.id
      if (cancelled.current || !mounted.current) {
        await teamLabRemoteAccessApi.end(session.id)
        ownedSession.current = null
        return
      }
      setTerminalSessionId(session.id)
    } catch (nextError) {
      setTerminalCloseError(nextError)
    } finally {
      operationPending.current = false
      setRecreating(false)
    }
  }, [recreating, runtime.id])

  const open = useCallback(async () => {
    if (!selected || operationPending.current || ownedSession.current) return
    operationPending.current = true
    cancelled.current = false
    setActing(true)
    setError(null)
    // Open the window before the awaited calls so the browser does not treat it as
    // a popup; the URL is filled in once the session is ready.
    const popup = selected.kind === 'vm' ? window.open('about:blank', '_blank') : null
    if (popup) popup.opener = null
    let createdSessionId: string | null = null
    let delivered = false
    try {
      if (selected.kind === 'vm' && !popup) throw new Error('浏览器阻止了运维窗口，请允许弹出窗口后重试。')
      if (!vncConsole) {
        const available = await teamLabRemoteAccessApi.getAvailability(runtime.id, selected.id)
        if (cancelled.current || !mounted.current) return
        if (!available.available) throw new Error(available.unavailableReason ?? '当前资产暂不可进入运维。')
      }
      const session = vncConsole
        ? await teamLabRemoteAccessApi.createConsoleSession(runtime.id, selected.id, reason.trim())
        : await teamLabRemoteAccessApi.createSession(runtime.id, selected.id, reason.trim())
      createdSessionId = session.id
      ownedSession.current = session.id
      if (vncConsole && session.protocol !== 'vnc') throw new Error('当前服务未创建 VNC 控制台，会话将被关闭。')
      if (cancelled.current || !mounted.current) return
      if (session.protocol === 'containerTerminal') {
        terminalRequest.current = { assetId: selected.id, reason: reason.trim() }
        delivered = true
        setTerminalSessionId(session.id)
        // Success path: reset the dialog state directly; close() marks the flow as
        // cancelled, which would close the freshly navigated popup in finally.
        setSelectedAssetId(null)
        setReason('')
        setError(null)
        return
      }
      const connect = await teamLabRemoteAccessApi.connect(session.id)
      if (cancelled.current || !mounted.current) return
      if (popup) {
        if (popup.closed) throw new Error('运维窗口已关闭。')
        popup.location.href = connect.url
      } else {
        window.open(connect.url, '_blank', 'noopener,noreferrer')
      }
      delivered = true
      ownedSession.current = null
      setSelectedAssetId(null)
      setReason('')
      setError(null)
    } catch (nextError) {
      if (!cancelled.current) setError(nextError)
    } finally {
      if (!delivered) {
        popup?.close()
        if (createdSessionId) {
          try {
            await teamLabRemoteAccessApi.end(createdSessionId)
            ownedSession.current = null
          } catch (cleanupError) {
            if (mounted.current) {
              setTerminalCloseError(cleanupError)
              setPendingCleanup(createdSessionId)
            }
          }
        }
      }
      operationPending.current = false
      if (mounted.current) setActing(false)
    }
  }, [acting, close, reason, runtime.id, selected, vncConsole])

  return (
    <section aria-labelledby="runtime-remote-access-title" className={styles.panel}>
      <header className={styles.panelHeader}>
        <div><span>运维访问</span><h3 id="runtime-remote-access-title">资产运维</h3></div>
      </header>
      {assets.length ? (
        <>
          {batch.state === 'error' ? (
            <InlineFeedback tone="danger">
              {errorMessage(batch.error, '可用性批量检查失败。')}
              <ActionButton
                icon={<RefreshCw size={14} />}
                onClick={() => setRetryNonce((value) => value + 1)}
                tone="danger"
                type="button"
              >
                重试
              </ActionButton>
            </InlineFeedback>
          ) : null}
          <div className={styles.remoteAssetList}>
            {assets.map((asset) => {
              const availability = batch.state === 'ready' ? batch.items.get(asset.id) : undefined
              const checking = batch.state === 'loading'
              const failed = batch.state === 'error'
              const unavailable = batch.state === 'ready' && availability === undefined ? true : availability?.available === false
              const disabled = checking || failed || unavailable || acting || terminalSessionId !== null || pendingCleanup !== null
              const reasonText = checking
                ? undefined
                : failed
                  ? '可用性检查失败'
                  : availability?.available === false
                    ? availability.unavailableReason ?? '当前资产暂不可进入运维。'
                    : availability === undefined
                      ? '批量检查未返回该资产的可用性结果'
                      : undefined
              return (
                <article data-available={availability?.available || undefined} key={asset.id}>
                  <div>
                    <strong>{asset.name}</strong>
                    <small>{asset.kind === 'vm' ? '虚拟机' : '容器'} {asset.primaryIp ? `· ${asset.primaryIp}` : ''}</small>
                    {checking ? <small>正在检查可用性...</small> : null}
                    {reasonText ? <small>{asset.kind === 'vm' ? 'SSH/RDP：' : ''}{reasonText}</small> : null}
                  </div>
                  <ActionButton disabled={disabled} icon={asset.kind === 'vm' ? <Monitor size={15} /> : <Terminal size={15} />} onClick={(event) => { remoteTriggerRef.current = event.currentTarget; setVncConsole(false); setSelectedAssetId(asset.id) }} type="button">
                    {asset.kind === 'vm' ? 'SSH/RDP 运维' : '进入运维'}
                  </ActionButton>
                  {asset.kind === 'vm' ? <ActionButton disabled={acting || !!terminalSessionId || !!pendingCleanup}
                    icon={<Monitor size={15} />} onClick={event => { remoteTriggerRef.current = event.currentTarget; setVncConsole(true); setSelectedAssetId(asset.id) }} type="button">VNC 控制台</ActionButton> : null}
                </article>
              )
            })}
          </div>
        </>
      ) : (
        <DataState description="仅显示当前代次中正在运行的容器和虚拟机。" title="暂无可运维资产" />
      )}
      <VNextDialog
        description="建立短期、仅限当前资产的运维连接。连接原因会写入审计记录。"
        eyebrow="运维访问"
        footer={<><ActionButton onClick={close} type="button">取消</ActionButton><ActionButton disabled={acting || reason.trim().length < 4} icon={<Wrench size={16} />} onClick={() => void open()} tone="primary" type="button">{acting ? '正在建立连接' : '建立连接'}</ActionButton></>}
        onClose={close}
        open={selected !== null}
        title={selected ? `${vncConsole ? 'VNC 控制台' : '运维'} ${selected.name}` : '资产运维'}
      >
        <label className={styles.remoteReason}>
          <span>运维原因</span>
          <textarea autoFocus maxLength={500} onChange={(event) => setReason(event.target.value)} placeholder="例如：核查服务启动状态" value={reason} />
          <small>{reason.trim().length}/500，至少 4 个字符</small>
        </label>
        {error ? <InlineFeedback tone="danger">{errorMessage(error, '无法建立运维连接。')}</InlineFeedback> : null}
      </VNextDialog>
      {pendingCleanup ? <InlineFeedback tone="danger">
        {errorMessage(terminalCloseError, '会话清理未完成。')}
        <ActionButton disabled={terminalClosing} icon={<RefreshCw size={14} />} onClick={() => void closeTerminal()} type="button">重试清理</ActionButton>
      </InlineFeedback> : null}
      <ContainerTerminal
        closeError={terminalCloseError}
        closing={terminalClosing}
        sessionId={terminalSessionId}
        returnFocus={restoreRemoteFocus}
        onClose={closeTerminal}
        onRecreate={recreateTerminal}
        recreating={recreating}
      />
    </section>
  )
})

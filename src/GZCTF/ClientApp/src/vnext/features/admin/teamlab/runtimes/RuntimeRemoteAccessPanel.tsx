import { Monitor, RefreshCw, Terminal, Wrench } from 'lucide-react'
import { memo, useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { teamLabRemoteAccessApi, type TeamLabRemoteAccessAvailability, type TeamLabRuntime } from '../api'
import styles from './RuntimePanels.module.css'

type RuntimeRemoteAccessPanelProps = { runtime: TeamLabRuntime }

type AvailabilityBatch =
  | { state: 'loading' }
  | { state: 'error'; error: unknown }
  | { state: 'ready'; items: ReadonlyMap<number, TeamLabRemoteAccessAvailability> }

export const RuntimeRemoteAccessPanel = memo(function RuntimeRemoteAccessPanel({ runtime }: RuntimeRemoteAccessPanelProps) {
  const [selectedAssetId, setSelectedAssetId] = useState<number | null>(null)
  const [reason, setReason] = useState('')
  const [acting, setActing] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const [terminalSessionId, setTerminalSessionId] = useState<string | null>(null)
  const [terminalClosing, setTerminalClosing] = useState(false)
  const [terminalCloseError, setTerminalCloseError] = useState<unknown>(null)
  const selected = runtime.assets.find((asset) => asset.id === selectedAssetId) ?? null
  const assets = useMemo(() => runtime.assets.filter((asset) => asset.status === 'running'), [runtime.assets])
  const [batch, setBatch] = useState<AvailabilityBatch>({ state: 'loading' })
  const [retryNonce, setRetryNonce] = useState(0)
  const cancelled = useRef(false)
  const terminalClosingRef = useRef(false)
  const remoteTriggerRef = useRef<HTMLButtonElement | null>(null)

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
    const sessionId = terminalSessionId
    if (!sessionId || terminalClosingRef.current) return
    terminalClosingRef.current = true
    setTerminalClosing(true)
    setTerminalCloseError(null)
    try {
      await teamLabRemoteAccessApi.end(sessionId)
      setTerminalSessionId(null)
    } catch (closeError) {
      setTerminalCloseError(closeError)
    } finally {
      terminalClosingRef.current = false
      setTerminalClosing(false)
    }
  }, [terminalClosing, terminalSessionId])

  const open = useCallback(async () => {
    if (!selected || acting) return
    cancelled.current = false
    setActing(true)
    setError(null)
    // Open the window before the awaited calls so the browser does not treat it as
    // a popup; the URL is filled in once the session is ready.
    const popup = selected.kind === 'vm' ? window.open('about:blank', '_blank') : null
    if (popup) popup.opener = null
    try {
      const available = await teamLabRemoteAccessApi.getAvailability(runtime.id, selected.id)
      if (cancelled.current) return
      if (!available.available) throw new Error(available.unavailableReason ?? '当前资产暂不可进入运维。')
      const session = await teamLabRemoteAccessApi.createSession(runtime.id, selected.id, reason.trim())
      if (cancelled.current) return
      if (session.protocol === 'containerTerminal') {
        setTerminalSessionId(session.id)
        // Success path: reset the dialog state directly; close() marks the flow as
        // cancelled, which would close the freshly navigated popup in finally.
        setSelectedAssetId(null)
        setReason('')
        setError(null)
        return
      }
      const connect = await teamLabRemoteAccessApi.connect(session.id)
      if (cancelled.current) return
      if (popup) {
        popup.location.href = connect.url
      } else {
        window.open(connect.url, '_blank', 'noopener,noreferrer')
      }
      setSelectedAssetId(null)
      setReason('')
      setError(null)
    } catch (nextError) {
      if (!cancelled.current) setError(nextError)
    } finally {
      if (cancelled.current && popup) popup.close()
      setActing(false)
    }
  }, [acting, close, reason, runtime.id, selected])

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
              const disabled = checking || failed || unavailable
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
                    {reasonText ? <small>{reasonText}</small> : null}
                  </div>
                  <ActionButton disabled={disabled} icon={asset.kind === 'vm' ? <Monitor size={15} /> : <Terminal size={15} />} onClick={(event) => { remoteTriggerRef.current = event.currentTarget; setSelectedAssetId(asset.id) }} type="button">
                    进入运维
                  </ActionButton>
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
        footer={<><ActionButton disabled={acting} onClick={close} type="button">取消</ActionButton><ActionButton disabled={acting || reason.trim().length < 4} icon={<Wrench size={16} />} onClick={() => void open()} tone="primary" type="button">{acting ? '正在建立连接' : '建立连接'}</ActionButton></>}
        onClose={close}
        open={selected !== null}
        title={selected ? `运维 ${selected.name}` : '资产运维'}
      >
        <label className={styles.remoteReason}>
          <span>运维原因</span>
          <textarea autoFocus maxLength={500} onChange={(event) => setReason(event.target.value)} placeholder="例如：核查服务启动状态" value={reason} />
          <small>{reason.trim().length}/500，至少 4 个字符</small>
        </label>
        {error ? <InlineFeedback tone="danger">{errorMessage(error, '无法建立运维连接。')}</InlineFeedback> : null}
      </VNextDialog>
      <ContainerTerminal
        closeError={terminalCloseError}
        closing={terminalClosing}
        sessionId={terminalSessionId}
        returnFocus={restoreRemoteFocus}
        onClose={closeTerminal}
      />
    </section>
  )
})

function ContainerTerminal({
  sessionId,
  closing,
  closeError,
  returnFocus,
  onClose
}: {
  sessionId: string | null
  closing: boolean
  closeError: unknown
  returnFocus: () => void
  onClose: () => Promise<void>
}) {
  const [output, setOutput] = useState('')
  const [input, setInput] = useState('')
  const [connected, setConnected] = useState(false)
  const [connectError, setConnectError] = useState<string | null>(null)
  const [retryNonce, setRetryNonce] = useState(0)
  const socket = useRef<WebSocket | null>(null)
  const outputRef = useRef<HTMLPreElement>(null)
  const wasOpen = useRef(false)

  useEffect(() => {
    if (!sessionId) return undefined
    setOutput('')
    setConnectError(null)
    setConnected(false)
    const scheme = window.location.protocol === 'https:' ? 'wss' : 'ws'
    const ws = new WebSocket(`${scheme}://${window.location.host}/api/admin/teamlab/remote-sessions/${sessionId}/terminal`)
    socket.current = ws
    ws.onopen = () => {
      setConnected(true)
      setConnectError(null)
    }
    ws.onmessage = (event) => setOutput((current) => (current + String(event.data)).slice(-100_000))
    ws.onerror = () => {
      socket.current = null
      setConnected(false)
      setConnectError('终端连接失败，请检查网络后重试。')
    }
    ws.onclose = (event) => {
      socket.current = null
      setConnected(false)
      if (!event.wasClean) setConnectError('终端连接已中断，请重试。')
    }
    return () => ws.close()
  }, [retryNonce, sessionId])

  useEffect(() => {
    if (closing) socket.current?.close()
  }, [closing])

  useEffect(() => {
    if (sessionId) {
      wasOpen.current = true
      return
    }
    if (!wasOpen.current) return
    wasOpen.current = false
    returnFocus()
  }, [returnFocus, sessionId])

  useEffect(() => {
    if (outputRef.current) outputRef.current.scrollTop = outputRef.current.scrollHeight
  }, [output])

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (socket.current?.readyState === WebSocket.OPEN && input) socket.current.send(input + '\n')
    setInput('')
  }

  return <VNextDialog closeDisabled={closing} eyebrow="容器终端" footer={<ActionButton disabled={closing} onClick={() => void onClose()} type="button">{closing ? '正在关闭终端' : '关闭终端'}</ActionButton>} onClose={() => void onClose()} open={sessionId !== null} title="容器终端" wide>
    <div className={styles.terminalSurface}>
      <pre ref={outputRef}>{output || (connectError ? '等待重新连接...' : '正在连接终端...')}</pre>
      {closeError ? <InlineFeedback tone="danger">{errorMessage(closeError, '终端清理未完成，请重试关闭。')}</InlineFeedback> : null}
      {connectError ? (
        <div className={styles.terminalError}>
          <span>{connectError}</span>
          <ActionButton disabled={closing} icon={<RefreshCw size={14} />} onClick={() => setRetryNonce((value) => value + 1)} tone="danger" type="button">重试连接</ActionButton>
        </div>
      ) : null}
      <form onSubmit={submit}><input autoFocus disabled={!connected || closing} onChange={(event) => setInput(event.target.value)} value={input} /><ActionButton disabled={!connected || closing} type="submit">发送</ActionButton></form>
    </div>
  </VNextDialog>
}

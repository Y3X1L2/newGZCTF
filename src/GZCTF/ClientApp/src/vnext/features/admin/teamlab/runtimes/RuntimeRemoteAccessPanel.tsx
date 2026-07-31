import { Monitor, Terminal, Wrench } from 'lucide-react'
import { memo, useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { teamLabRemoteAccessApi, type TeamLabRuntime } from '../api'
import styles from './RuntimePanels.module.css'

type RuntimeRemoteAccessPanelProps = { runtime: TeamLabRuntime }

export const RuntimeRemoteAccessPanel = memo(function RuntimeRemoteAccessPanel({ runtime }: RuntimeRemoteAccessPanelProps) {
  const [selectedAssetId, setSelectedAssetId] = useState<number | null>(null)
  const [reason, setReason] = useState('')
  const [acting, setActing] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const [terminalSessionId, setTerminalSessionId] = useState<string | null>(null)
  const selected = runtime.assets.find((asset) => asset.id === selectedAssetId) ?? null
  const assets = runtime.assets.filter((asset) => asset.status === 'running')

  const close = useCallback(() => {
    setSelectedAssetId(null)
    setReason('')
    setError(null)
  }, [])

  const open = useCallback(async () => {
    if (!selected || acting) return
    setActing(true)
    setError(null)
    try {
      const available = await teamLabRemoteAccessApi.getAvailability(runtime.id, selected.id)
      if (!available.available) throw new Error(available.unavailableReason ?? '当前资产暂不可进入运维。')
      const session = await teamLabRemoteAccessApi.createSession(runtime.id, selected.id, reason.trim())
      if (session.protocol === 'containerTerminal') {
        setTerminalSessionId(session.id)
        close()
        return
      }
      const connect = await teamLabRemoteAccessApi.connect(session.id)
      window.open(connect.url, '_blank', 'noopener,noreferrer')
      close()
    } catch (nextError) {
      setError(nextError)
    } finally {
      setActing(false)
    }
  }, [acting, close, reason, runtime.id, selected])

  return (
    <section aria-labelledby="runtime-remote-access-title" className={styles.panel}>
      <header className={styles.panelHeader}>
        <div><span>OPERATOR ACCESS</span><h3 id="runtime-remote-access-title">资产运维</h3></div>
      </header>
      {assets.length ? (
        <div className={styles.remoteAssetList}>
          {assets.map((asset) => (
            <article key={asset.id}>
              <div>
                <strong>{asset.name}</strong>
                <small>{asset.kind === 'vm' ? '虚拟机' : '容器'} {asset.primaryIp ? `· ${asset.primaryIp}` : ''}</small>
              </div>
              <ActionButton icon={asset.kind === 'vm' ? <Monitor size={15} /> : <Terminal size={15} />} onClick={() => setSelectedAssetId(asset.id)} type="button">
                进入运维
              </ActionButton>
            </article>
          ))}
        </div>
      ) : (
        <DataState description="仅显示当前代次中正在运行的容器和虚拟机。" title="暂无可运维资产" />
      )}
      <VNextDialog
        description="建立短期、仅限当前资产的运维连接。连接原因会写入审计记录。"
        eyebrow="OPERATOR ACCESS"
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
      <ContainerTerminal sessionId={terminalSessionId} onClose={() => setTerminalSessionId(null)} />
    </section>
  )
})

function ContainerTerminal({ sessionId, onClose }: { sessionId: string | null; onClose: () => void }) {
  const [output, setOutput] = useState('')
  const [input, setInput] = useState('')
  const [connected, setConnected] = useState(false)
  const socket = useRef<WebSocket | null>(null)
  const outputRef = useRef<HTMLPreElement>(null)

  useEffect(() => {
    if (!sessionId) return undefined
    const scheme = window.location.protocol === 'https:' ? 'wss' : 'ws'
    const ws = new WebSocket(`${scheme}://${window.location.host}/api/admin/teamlab/remote-sessions/${sessionId}/terminal`)
    socket.current = ws
    ws.onopen = () => setConnected(true)
    ws.onmessage = (event) => setOutput((current) => (current + String(event.data)).slice(-100_000))
    ws.onclose = () => { socket.current = null; setConnected(false) }
    return () => ws.close()
  }, [sessionId])

  useEffect(() => {
    if (outputRef.current) outputRef.current.scrollTop = outputRef.current.scrollHeight
  }, [output])

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (socket.current?.readyState === WebSocket.OPEN && input) socket.current.send(input + '\n')
    setInput('')
  }

  return <VNextDialog eyebrow="CONTAINER TERMINAL" footer={<ActionButton onClick={onClose} type="button">关闭终端</ActionButton>} onClose={onClose} open={sessionId !== null} title="容器终端" wide>
    <div className={styles.terminalSurface}><pre ref={outputRef}>{output || '正在连接终端...'}</pre><form onSubmit={submit}><input autoFocus disabled={!connected} onChange={(event) => setInput(event.target.value)} value={input} /><ActionButton disabled={!connected} type="submit">发送</ActionButton></form></div>
  </VNextDialog>
}

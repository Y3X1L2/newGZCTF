import { ActionIcon, Drawer, SegmentedControl, Select, Switch } from '@mantine/core'
import { RefreshCw } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { useWorkspaceApi, type Asset, type Schema } from '../../api/client'
import { ErrorMessage } from '../../foundation/Feedback'
import styles from './LogDrawer.module.css'

const displayLimit = 1 << 20

export function LogDrawer({
  environmentId,
  asset,
  onClose,
}: {
  environmentId: string
  asset: Asset
  onClose: () => void
}) {
  const api = useWorkspaceApi()
  const [stream, setStream] = useState('all')
  const [tail, setTail] = useState('200')
  const [follow, setFollow] = useState(true)
  const [refresh, setRefresh] = useState(0)
  const [chunks, setChunks] = useState<Schema<'LogChunk'>[]>([])
  const [status, setStatus] = useState('连接中')
  const [error, setError] = useState<Error>()
  const output = useRef<HTMLPreElement>(null)
  const atEnd = useRef(true)
  useEffect(() => {
    const controller = new AbortController()
    setChunks([])
    setError(undefined)
    setStatus('连接中')
    atEnd.current = true
    void api
      .assetLogs(
        environmentId,
        asset.id,
        { stream, tail: Number(tail), follow },
        controller.signal,
        (chunk) => {
          setStatus(follow ? '实时' : '已加载')
          setChunks((previous) => {
            const next = [...previous, chunk]
            let size = next.reduce((total, item) => total + item.data.length, 0)
            while ((size > displayLimit || next.length > 1024) && next.length > 1) size -= next.shift()!.data.length
            return next
          })
        },
        () => setStatus(follow ? '实时' : '已加载')
      )
      .then(() => {
        if (!controller.signal.aborted) setStatus(follow ? '已结束' : '已加载')
      })
      .catch((error: Error) => {
        if (!controller.signal.aborted) {
          setError(error)
          setStatus('连接中断')
        }
      })
    return () => controller.abort()
  }, [environmentId, asset.id, stream, tail, follow, refresh])
  useEffect(() => {
    if (atEnd.current && output.current) output.current.scrollTop = output.current.scrollHeight
  }, [chunks])
  return (
    <Drawer
      opened
      position="right"
      size="xl"
      title={`${asset.name} · 进程日志`}
      onClose={onClose}
      classNames={{ body: styles.body }}
    >
      <div className={styles.toolbar}>
        <SegmentedControl
          size="xs"
          value={stream}
          onChange={setStream}
          data={[
            { value: 'all', label: '全部' },
            { value: 'stdout', label: '输出' },
            { value: 'stderr', label: '错误' },
          ]}
        />
        <Select
          aria-label="日志行数"
          size="xs"
          w={112}
          value={tail}
          onChange={(value) => setTail(value!)}
          allowDeselect={false}
          data={[
            { value: '100', label: '100 行' },
            { value: '200', label: '200 行' },
            { value: '1000', label: '1000 行' },
          ]}
        />
        <Switch size="xs" label="实时" checked={follow} onChange={(event) => setFollow(event.currentTarget.checked)} />
        <ActionIcon variant="subtle" aria-label="刷新日志" onClick={() => setRefresh((value) => value + 1)}>
          <RefreshCw size={15} />
        </ActionIcon>
      </div>
      <ErrorMessage error={error} />
      <pre
        ref={output}
        className={styles.output}
        aria-label="容器日志"
        onScroll={(event) => {
          const element = event.currentTarget
          atEnd.current = element.scrollHeight - element.scrollTop - element.clientHeight < 32
        }}
      >
        {chunks.map((chunk, index) => (
          <span key={index} className={chunk.stream === 'stderr' ? styles.stderr : undefined}>
            {chunk.data}
          </span>
        ))}
      </pre>
      <div className={styles.status}>{status}</div>
    </Drawer>
  )
}

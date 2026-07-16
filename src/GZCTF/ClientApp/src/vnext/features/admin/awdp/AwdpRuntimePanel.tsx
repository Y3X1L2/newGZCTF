import { ExternalLink, RotateCcw, Search, Undo2 } from 'lucide-react'
import { useMemo, useState } from 'react'
import { VNextConfirmDialog } from '../../../shared/Interaction'
import { AwdpInstance, checkerMeta } from '../../awdp/awdpDomain'
import { AdminDataColumn, DataTable, FilterToolbar, StatusBadge, ToolbarGroup } from '../shared/AdminWorkbench'
import styles from './AdminAwdp.module.css'

type Confirmation = { kind: 'recover' | 'reset'; instance: AwdpInstance }

export function AwdpRuntimePanel({
  instances,
  operation,
  onAction,
}: {
  instances: AwdpInstance[]
  operation: string | null
  onAction: (kind: 'recover' | 'reset', instance: AwdpInstance) => Promise<boolean>
}) {
  const [query, setQuery] = useState('')
  const [confirmation, setConfirmation] = useState<Confirmation | null>(null)
  const visible = useMemo(() => {
    const keyword = query.trim().toLocaleLowerCase('zh-CN')
    return instances.filter(
      (item) =>
        !keyword ||
        `${item.serviceName} ${item.teamName} ${item.ipAddress ?? ''}`.toLocaleLowerCase('zh-CN').includes(keyword)
    )
  }, [instances, query])
  const columns = useMemo<AdminDataColumn<AwdpInstance>[]>(
    () => [
      {
        id: 'identity',
        header: '服务 / 战队',
        width: 'wide',
        render: (item) => (
          <div className={styles.identity}>
            <strong>{item.serviceName}</strong>
            <small>{item.teamName}</small>
          </div>
        ),
      },
      {
        id: 'endpoint',
        header: '入口',
        width: 'wide',
        visibility: 'desktop',
        render: (item) =>
          item.endpoint ? (
            <a className={styles.endpoint} href={item.endpoint} rel="noreferrer" target="_blank">
              {item.ipAddress}:{item.port}
              <ExternalLink size={14} />
            </a>
          ) : (
            '未分配'
          ),
      },
      {
        id: 'running',
        header: '运行状态',
        width: 'medium',
        render: (item) => (
          <StatusBadge tone={item.running ? 'success' : 'danger'}>{item.running ? '运行中' : '已停止'}</StatusBadge>
        ),
      },
      {
        id: 'checker',
        header: 'Checker',
        width: 'medium',
        render: (item) => {
          const meta = checkerMeta(item.checkerStatus)
          return <StatusBadge tone={meta.tone}>{meta.label}</StatusBadge>
        },
      },
      {
        id: 'quota',
        header: '剩余次数',
        visibility: 'wide',
        width: 'medium',
        render: (item) => (
          <span className={styles.mono}>
            重置 {item.remainingResetCount} · 恢复 {item.remainingRecoveryCount}
          </span>
        ),
      },
      {
        id: 'actions',
        header: '操作',
        align: 'right',
        width: 'compact',
        render: (item) => (
          <span className={styles.rowActions}>
            <button
              aria-label={`重置 ${item.teamName} ${item.serviceName}`}
              disabled={operation !== null}
              onClick={() => setConfirmation({ kind: 'reset', instance: item })}
              type="button"
            >
              <RotateCcw size={16} />
            </button>
            <button
              aria-label={`恢复 ${item.teamName} ${item.serviceName}`}
              disabled={operation !== null}
              onClick={() => setConfirmation({ kind: 'recover', instance: item })}
              type="button"
            >
              <Undo2 size={16} />
            </button>
          </span>
        ),
      },
    ],
    [operation]
  )
  const execute = async () => {
    if (!confirmation) return false
    const success = await onAction(confirmation.kind, confirmation.instance)
    if (success) setConfirmation(null)
    return success
  }
  return (
    <>
      <FilterToolbar>
        <ToolbarGroup grow>
          <label className={styles.searchBox}>
            <Search size={16} />
            <input
              aria-label="搜索 AWDP 实例"
              onChange={(event) => setQuery(event.currentTarget.value)}
              placeholder="服务、战队或入口"
              type="search"
              value={query}
            />
          </label>
        </ToolbarGroup>
        <span className={styles.helper}>共 {instances.length} 个实例</span>
      </FilterToolbar>
      <DataTable
        caption="AWDP 实例矩阵"
        columns={columns}
        emptyDescription="开始 AWDP 后，队伍服务实例会显示在这里。"
        emptyTitle="暂无 AWDP 实例"
        rowKey={(item) => item.instanceId || `${item.serviceId}:${item.teamId}`}
        rows={visible}
      />
      <VNextConfirmDialog
        confirmLabel={confirmation?.kind === 'reset' ? '确认重置' : '确认恢复'}
        description={confirmation?.kind === 'reset' ? '重置会重新创建当前运行实例。' : '恢复会清除补丁并回到原始镜像。'}
        message={confirmation ? `${confirmation.instance.teamName} / ${confirmation.instance.serviceName}` : ''}
        onClose={() => setConfirmation(null)}
        onConfirm={execute}
        open={Boolean(confirmation)}
        title={confirmation?.kind === 'reset' ? '重置队伍实例？' : '恢复原始实例？'}
      />
    </>
  )
}

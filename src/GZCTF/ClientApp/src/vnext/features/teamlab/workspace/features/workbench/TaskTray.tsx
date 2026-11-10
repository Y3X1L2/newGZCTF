import { Button, Drawer, Select } from '@mantine/core'
import { CircleCheck, RotateCcw } from 'lucide-react'
import { useState } from 'react'
import type { EnvironmentSpec, Operation } from '../../api/client'
import { LoadMore, type CursorPagination } from '../../foundation/LoadMore'
import { Status } from '../../foundation/Status'
import { actionLabels, dateTime } from '../../foundation/format'
import styles from './TaskTray.module.css'

const phaseLabels: Record<string, string> = {
  pending: '等待调度',
  scheduling: '分配资源',
  placing: '分配资源',
  network: '配置网络',
  services: '应用服务入口',
  vpn: '配置 VPN',
  'revoke-services': '撤销服务入口',
  prepare: '准备资产',
  activate: '启动资产',
  complete: '完成',
  completed: '完成',
  failed: '执行失败',
  cleanup: '清理资源',
  rollback: '恢复变更',
  'rolled-back': '已回滚',
  destroy: '销毁资产',
  queued: '等待执行',
  running: '执行中',
  'backup-complete': '备份完成',
  'prepare-backup-templates': '恢复模板',
  'recovery-quiesce': '暂停写入',
  'recovery-capture': '保存恢复点',
  'recovery-resume': '恢复运行',
  'migration-prepare': '准备目标节点',
  'migration-stop': '停止源资产',
  'migration-capture': '保存迁移数据',
  'migration-copy': '传输数据',
  'migration-apply': '应用资产配置',
  'migration-activate': '启动目标资产',
  'migration-release-capture': '清理迁移数据',
  'migration-transfer': '迁移虚拟机',
  'migration-verify': '确认目标运行',
  'migration-commit': '交接节点归属',
  'migration-cleanup': '清理源节点',
  'migration-services': '更新访问入口',
}

export function TaskTray({
  operations,
  spec,
  operation,
  pagination,
  retrying,
  onRetry,
  opened,
  onClose,
  canRetry,
}: {
  operations: Operation[]
  spec: EnvironmentSpec
  operation?: Operation
  pagination: CursorPagination
  retrying: boolean
  onRetry: (id: string) => void
  opened: boolean
  onClose: () => void
  canRetry: boolean
}) {
  const [selected, setSelected] = useState<string>()
  const recent = operation ? [operation, ...operations.filter((item) => item.id !== operation.id)] : operations
  const latest = recent[0]
  const shown = recent.find((item) => item.id === selected) ?? latest
  const assetNames = new Map(spec.assets.map((asset) => [asset.id, asset.name]))
  return (
    <Drawer
      opened={opened}
      onClose={onClose}
      title="运行任务"
      position="right"
      size={420}
      classNames={{ body: styles.body }}
    >
      {recent.length > 0 ? (
        <>
          <Select
            aria-label="选择任务"
            value={shown?.id}
            onChange={(value) => setSelected(value ?? undefined)}
            data={recent.map((item) => ({
              value: item.id,
              label: `${actionLabels[item.kind] ?? item.kind} · ${dateTime(item.createdAt)}`,
            }))}
            searchable
            allowDeselect={false}
          />
          <LoadMore list={pagination} />
          {shown && (
            <div className={styles.detail}>
              <div className={styles.detailHeading}>
                <strong>{actionLabels[shown.kind] ?? shown.kind}</strong>
                <Status value={shown.state} />
                {shown.retryable && canRetry && (
                  <Button
                    variant="default"
                    size="compact-xs"
                    leftSection={<RotateCcw size={13} />}
                    loading={retrying}
                    onClick={() => onRetry(shown.id)}
                  >
                    重试
                  </Button>
                )}
              </div>
              <div className={styles.phase}>
                <span>{phaseLabels[shown.phase] ?? shown.phase}</span>
                {shown.total > 0 && (
                  <span>
                    {shown.completed} / {shown.total}
                  </span>
                )}
              </div>
              {shown.error && (
                <div className={styles.error} role="alert">
                  {shown.error}
                </div>
              )}
              {shown.total > 0 && (
                <div
                  className={styles.completion}
                  data-state={shown.state}
                  role="progressbar"
                  aria-label="任务进度"
                  aria-valuenow={shown.completed}
                  aria-valuemax={shown.total}
                  aria-valuemin={0}
                >
                  <span
                    style={{
                      width: `${(shown.completed / shown.total) * 100}%`,
                    }}
                  />
                </div>
              )}
              <div className={styles.results}>
                {shown.results?.map((result) => (
                  <div key={result.assetId}>
                    <span>{assetNames.get(result.assetId) ?? '已移除资产'}</span>
                    <Status value={result.state} />
                    {result.error && <p>{result.error}</p>}
                  </div>
                ))}
              </div>
            </div>
          )}
        </>
      ) : (
        <div className={styles.empty}>
          <CircleCheck size={24} />
          暂无任务
        </div>
      )}
    </Drawer>
  )
}

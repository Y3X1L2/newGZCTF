import { ExternalLink, XCircle } from 'lucide-react'
import { Link } from 'react-router'
import { ActionButton, InlineFeedback } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import type { DeploymentTask, DeploymentTaskDetail } from '../api'
import { DetailDrawer, StatusBadge } from '../shared/AdminWorkbench'
import {
  deploymentSlotsLabel,
  deploymentStageLabel,
  deploymentStatusMeta,
  formatAdminDate,
} from './deploymentQueuePresentation'
import styles from './AdminQueuePage.module.css'

function Fact({ label, value, wide = false }: { label: string; value: React.ReactNode; wide?: boolean }) {
  return (
    <div className={wide ? styles.detailWide : undefined}>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  )
}

export function DeploymentTaskDrawer({
  task,
  detail,
  detailError,
  detailLoading,
  cancelPending,
  onClose,
  onCancel,
}: {
  task: DeploymentTask | null
  detail?: DeploymentTaskDetail
  detailError?: unknown
  detailLoading: boolean
  cancelPending: boolean
  onClose: () => void
  onCancel: () => void
}) {
  const status = task ? deploymentStatusMeta(task.statusKey) : null
  const correlationId = detail?.correlationId ?? task?.correlationId ?? task?.id

  return (
    <DetailDrawer
      description={task ? `${task.typeLabel} · ${task.actionLabel}` : undefined}
      footer={
        task && status?.active ? (
          <ActionButton
            disabled={cancelPending}
            icon={<XCircle size={16} />}
            onClick={onCancel}
            tone="danger"
            type="button"
          >
            {cancelPending ? '取消确认中' : '取消任务'}
          </ActionButton>
        ) : undefined
      }
      onClose={onClose}
      open={Boolean(task)}
      title={task?.requestLabel ?? '部署任务详情'}
    >
      {detailLoading ? (
        <DataState description="正在读取部署阶段和关联错误。" loading title="任务详情加载中" />
      ) : detailError ? (
        <InlineFeedback tone="danger">任务详情暂时不可用，列表中的基础状态仍然有效。</InlineFeedback>
      ) : task ? (
        <div className={styles.detailBody}>
          <section className={styles.detailIdentity}>
            <div>
              <span>任务 {task.id.slice(0, 8)}</span>
              <strong>{task.requestLabel}</strong>
            </div>
            <StatusBadge pulse={status?.active} tone={status?.tone}>
              {cancelPending ? '取消确认中' : status?.label}
            </StatusBadge>
          </section>

          {cancelPending ? (
            <InlineFeedback>取消请求已经提交，任务会保留到服务端确认终态。</InlineFeedback>
          ) : null}
          {task.errorMessage || detail?.errorMessage ? (
            <InlineFeedback tone="danger">{detail?.errorMessage || task.errorMessage}</InlineFeedback>
          ) : null}

          <dl className={styles.detailGrid}>
            <Fact label="资源类型" value={task.typeLabel} />
            <Fact label="操作" value={task.actionLabel} />
            <Fact label="当前阶段" value={deploymentStageLabel(detail?.stage ?? task.stage, task.stageMessage)} />
            <Fact label="槽位" value={deploymentSlotsLabel(task)} />
            <Fact label="目标节点" value={task.targetNodeLabel || '尚未分配'} wide />
            <Fact
              label="执行结果地址"
              value={
                detail?.resultHost
                  ? `${detail.resultHost}${detail.resultPort ? `:${detail.resultPort}` : ''}`
                  : task.result || '—'
              }
              wide
            />
            <Fact label="所有者" value={task.ownerLabel || detail?.subjectDisplayName || '—'} />
            <Fact label="业务资源" value={task.challengeLabel || detail?.resourceDisplayName || '—'} />
            <Fact label="镜像" value={task.image || '—'} wide />
            <Fact label="创建时间" value={formatAdminDate(task.createdAt)} />
            <Fact label="开始时间" value={formatAdminDate(task.startedAt)} />
            <Fact label="完成时间" value={formatAdminDate(task.completedAt)} />
            <Fact label="队列位置" value={task.queuePosition > 0 ? `第 ${task.queuePosition} 位` : '—'} />
            <Fact label="阶段说明" value={task.stageMessage || '暂无阶段说明。'} wide />
            <Fact label="阻塞原因" value={task.blockedReasonCode || '—'} wide />
            <Fact label="错误分类" value={detail?.errorCategory ?? '—'} />
            <Fact label="错误代码" value={detail?.errorCode || '—'} />
            <Fact label="可重试" value={detail?.retryable === null || detail?.retryable === undefined ? '—' : detail.retryable ? '是' : '否'} />
            <Fact label="任务 ID" value={task.id} wide />
            <Fact label="关联 ID" value={correlationId || '—'} wide />
          </dl>

          {correlationId ? (
            <Link className={styles.correlationLink} to={`/admin/logs?correlationId=${encodeURIComponent(correlationId)}`}>
              查看关联日志
              <ExternalLink size={14} />
            </Link>
          ) : null}
        </div>
      ) : null}
    </DetailDrawer>
  )
}

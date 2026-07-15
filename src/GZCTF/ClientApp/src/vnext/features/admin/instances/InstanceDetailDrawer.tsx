import { ExternalLink, Trash2 } from 'lucide-react'
import { ActionButton, InlineFeedback } from '../../../shared/Interaction'
import type { GlobalInstanceItem } from '../api'
import { formatAdminDate } from '../shared/adminFormat'
import { DetailDrawer, StatusBadge } from '../shared/AdminWorkbench'
import {
  canDestroyInstance,
  instanceContextLabel,
  instanceEntryLabel,
  instanceKindLabel,
  instanceOwnerLabel,
  instanceStatusMeta,
} from './instancePresentation'
import styles from './AdminInstancesPage.module.css'

function Fact({ label, value, wide = false }: { label: string; value: React.ReactNode; wide?: boolean }) {
  return (
    <div className={wide ? styles.detailWide : undefined}>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  )
}

export function InstanceDetailDrawer({
  instance,
  destroyPending,
  onClose,
  onDestroy,
}: {
  instance: GlobalInstanceItem | null
  destroyPending: boolean
  onClose: () => void
  onDestroy: () => void
}) {
  const status = instance ? instanceStatusMeta(instance) : null
  const entry = instance ? instanceEntryLabel(instance) : null

  return (
    <DetailDrawer
      description={instance ? `${instanceKindLabel(instance.kind)} · ${instance.nodeName}` : undefined}
      footer={
        instance && canDestroyInstance(instance) ? (
          <ActionButton
            disabled={destroyPending}
            icon={<Trash2 size={16} />}
            onClick={onDestroy}
            tone="danger"
            type="button"
          >
            {destroyPending ? '销毁确认中' : '销毁实例'}
          </ActionButton>
        ) : undefined
      }
      onClose={onClose}
      open={Boolean(instance)}
      title={instance?.name ?? '运行实例详情'}
    >
      {instance ? (
        <div className={styles.detailBody}>
          <section className={styles.detailIdentity}>
            <div>
              <span>{instanceKindLabel(instance.kind)}</span>
              <strong>{instance.name}</strong>
            </div>
            <StatusBadge pulse={instance.isActive} tone={destroyPending ? 'warning' : status?.tone}>
              {destroyPending ? '销毁确认中' : status?.label}
            </StatusBadge>
          </section>

          {destroyPending ? (
            <InlineFeedback>销毁请求已经提交，实例会保留到节点资源接口确认终态。</InlineFeedback>
          ) : null}

          <dl className={styles.detailGrid}>
            <Fact label="资源类型" value={instanceKindLabel(instance.kind)} />
            <Fact label="运行状态" value={instance.status} />
            <Fact label="所有者" value={instanceOwnerLabel(instance)} />
            <Fact label="所属节点" value={instance.nodeName} />
            <Fact label="业务上下文" value={instanceContextLabel(instance)} wide />
            <Fact label="开始时间" value={formatAdminDate(instance.startedAt)} />
            <Fact label="到期时间" value={formatAdminDate(instance.expectedStopAt)} />
            <Fact label="停止时间" value={formatAdminDate(instance.stoppedAt)} />
            <Fact label="持续时间" value={instance.duration || '—'} />
            <Fact label="公开入口" value={entry} wide />
            <Fact label="内部地址" value={instance.ip || '—'} />
            <Fact label="端口" value={instance.port ?? '—'} />
            <Fact label="镜像" value={instance.image || '—'} wide />
            <Fact label="运行标识" value={instance.runtimeId || instance.id} wide />
            <Fact label="提供方" value={instance.providerName || '—'} />
            <Fact label="操作系统" value={instance.osType || '—'} />
          </dl>

          {instance.entry?.startsWith('http') ? (
            <a className={styles.entryLink} href={instance.entry} rel="noopener noreferrer" target="_blank">
              打开实例入口
              <ExternalLink size={14} />
            </a>
          ) : null}
        </div>
      ) : null}
    </DetailDrawer>
  )
}

import { Pencil, Plus, Trash2 } from 'lucide-react'
import { useMemo, useState } from 'react'
import { ActionButton, VNextConfirmDialog } from '../../../shared/Interaction'
import { AwdpService } from '../../awdp/awdpDomain'
import { AdminDataColumn, DataTable, FilterToolbar, StatusBadge, ToolbarGroup } from '../shared/AdminWorkbench'
import styles from './AdminAwdp.module.css'
import { AwdpServiceDrawer } from './AwdpServiceDrawer'
import { awdpServiceWarnings } from './awdpServiceForm'

type ImageOption = { id: number; name: string; registryUrl: string }

export function AwdpServicePanel({
  services,
  images,
  operation,
  onSave,
  onDelete,
}: {
  services: AwdpService[]
  images: ImageOption[]
  operation: string | null
  onSave: Parameters<typeof AwdpServiceDrawer>[0]['onSave']
  onDelete: (serviceId: number, name: string) => Promise<boolean>
}) {
  const [editor, setEditor] = useState<{ open: boolean; service: AwdpService | null }>({ open: false, service: null })
  const [deleteTarget, setDeleteTarget] = useState<AwdpService | null>(null)
  const columns = useMemo<AdminDataColumn<AwdpService>[]>(
    () => [
      {
        id: 'service',
        header: '服务',
        width: 'wide',
        render: (service) => (
          <div className={styles.identity}>
            <strong>{service.name}</strong>
            <small>{service.imageName || '未配置镜像'}</small>
          </div>
        ),
      },
      {
        id: 'port',
        header: '端口',
        width: 'compact',
        render: (service) => <span className={styles.mono}>{service.exposePort}</span>,
      },
      {
        id: 'phase',
        header: '轮次',
        width: 'medium',
        render: (service) => (
          <div className={styles.identity}>
            <strong>{service.totalRounds} 轮</strong>
            <small>
              {service.attackPhaseMinutes}+{service.patchPhaseMinutes} 分钟
            </small>
          </div>
        ),
      },
      {
        id: 'score',
        header: '计分',
        visibility: 'desktop',
        width: 'medium',
        render: (service) => (
          <div className={styles.identity}>
            <strong>
              攻击 {service.attackPoints} / 修补 {service.patchPoints}
            </strong>
            <small>
              SLA {service.slaPoints} · 扣分 {service.serviceAbnormalPenalty}
            </small>
          </div>
        ),
      },
      {
        id: 'config',
        header: '配置状态',
        width: 'medium',
        render: (service) => {
          const warnings = awdpServiceWarnings(service)
          return (
            <StatusBadge tone={warnings.length ? 'warning' : 'success'}>
              {warnings.length ? `${warnings.length} 项缺失` : '配置完整'}
            </StatusBadge>
          )
        },
      },
      {
        id: 'actions',
        header: '操作',
        align: 'right',
        width: 'compact',
        render: (service) => (
          <span className={styles.rowActions}>
            <button
              aria-label={`编辑 ${service.name}`}
              onClick={() => setEditor({ open: true, service })}
              type="button"
            >
              <Pencil size={16} />
            </button>
            <button
              aria-label={`删除 ${service.name}`}
              data-danger
              onClick={() => setDeleteTarget(service)}
              type="button"
            >
              <Trash2 size={16} />
            </button>
          </span>
        ),
      },
    ],
    []
  )
  const remove = async () => {
    if (!deleteTarget) return false
    const success = await onDelete(deleteTarget.id, deleteTarget.name)
    if (success) setDeleteTarget(null)
    return success
  }
  return (
    <>
      <FilterToolbar>
        <ToolbarGroup>
          <ActionButton
            icon={<Plus size={16} />}
            onClick={() => setEditor({ open: true, service: null })}
            tone="primary"
            type="button"
          >
            创建服务
          </ActionButton>
        </ToolbarGroup>
        <span className={styles.helper}>
          {images.length
            ? `${images.length} 个就绪 Docker 模板可选`
            : '未读取到就绪 Docker 模板，可手动填写 Registry 地址'}
        </span>
      </FilterToolbar>
      <DataTable
        caption="AWDP 服务配置"
        columns={columns}
        emptyDescription="创建至少一个带 Checker 和 Exp 的服务后才能开始完整 AWDP 流程。"
        emptyTitle="尚未配置 AWDP 服务"
        onRowClick={(service) => setEditor({ open: true, service })}
        rowKey={(service) => service.id}
        rows={services}
      />
      <AwdpServiceDrawer
        images={images}
        onClose={() => setEditor({ open: false, service: null })}
        onSave={onSave}
        open={editor.open}
        saving={operation?.startsWith('service:') ?? false}
        service={editor.service}
      />
      <VNextConfirmDialog
        confirmationText={deleteTarget?.name}
        description="删除服务会移除其 AWDP 配置；运行中的比赛不应执行此操作。"
        message={deleteTarget ? `确认删除服务“${deleteTarget.name}”。` : ''}
        onClose={() => setDeleteTarget(null)}
        onConfirm={remove}
        open={Boolean(deleteTarget)}
        title="删除 AWDP 服务？"
      />
    </>
  )
}

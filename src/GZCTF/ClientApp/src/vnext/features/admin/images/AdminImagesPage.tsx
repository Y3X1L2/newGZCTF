import { Box, Eye, FileArchive, FolderInput, Search, Trash2, Upload } from 'lucide-react'
import { ChangeEvent, useEffect, useMemo, useState } from 'react'
import useSWR from 'swr'
import { ImageStatus, ImageType, OSType } from '@Api'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import { errorMessage } from '../../../shared/errors'
import { useVNextPageTitle } from '../../../shared/useVNextPageTitle'
import { imageTemplateAdminApi, type ImageTemplateSummary } from '../api'
import {
  AdminPageHeader,
  DataTable,
  DetailDrawer,
  FilterToolbar,
  MetricItem,
  MetricStrip,
  PaginationBar,
  RefreshIndicator,
  StatusBadge,
  ToolbarGroup,
  type AdminDataColumn,
} from '../shared/AdminWorkbench'
import { numericEnumValue, useAdminQueryState } from '../shared/useAdminQueryState'
import styles from './AdminImagesPage.module.css'
import { ImageActionDialog, type ImageActionMode } from './ImageActionDialog'
import {
  formatAdminTime,
  formatBytes,
  imageOsLabel,
  imageStatusMeta,
  imageTypeLabel,
  useAdminImages,
  useDockerRegistry,
} from './useAdminImages'

const PAGE_SIZE = 20

function searchableText(template: ImageTemplateSummary) {
  return `${template.name} ${template.registryUrl ?? ''} ${template.imageHash ?? ''} ${template.description ?? ''}`.toLocaleLowerCase(
    'zh-CN'
  )
}

export function AdminImagesPage() {
  const queryState = useAdminQueryState(PAGE_SIZE)
  const [query, setQuery] = useState(queryState.params.get('q') ?? '')
  const [actionMode, setActionMode] = useState<ImageActionMode | null>(null)
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<ImageTemplateSummary | null>(null)
  const [actionFailure, setActionFailure] = useState<string | null>(null)
  const { images, error, isLoading, isRefreshing, mutate } = useAdminImages({})
  const { registry } = useDockerRegistry()
  const detail = useSWR(
    selectedId === null ? null : ['vnext:admin:image-detail', selectedId],
    () => imageTemplateAdminApi.detail(selectedId as number),
    { revalidateOnFocus: false }
  )

  useVNextPageTitle('环境模板')

  useEffect(() => setQuery(queryState.params.get('q') ?? ''), [queryState.params])

  useEffect(() => {
    const current = queryState.params.get('q') ?? ''
    if (query.trim() === current) return undefined
    const timer = window.setTimeout(() => queryState.update({ q: query.trim() || null }, { replace: true }), 250)
    return () => window.clearTimeout(timer)
  }, [query, queryState])

  const imageType = numericEnumValue(queryState.params.get('type'), [
    ImageType.Docker,
    ImageType.Qcow2,
    ImageType.Ova,
    ImageType.Vmdk,
  ])
  const osType = numericEnumValue(queryState.params.get('os'), [OSType.Linux, OSType.Windows])
  const status = numericEnumValue(queryState.params.get('status'), [
    ImageStatus.Ready,
    ImageStatus.Importing,
    ImageStatus.Error,
    ImageStatus.Deleting,
  ])

  const filtered = useMemo(() => {
    const keyword = (queryState.params.get('q') ?? '').trim().toLocaleLowerCase('zh-CN')
    return (images ?? []).filter(
      (template) =>
        (!keyword || searchableText(template).includes(keyword)) &&
        (imageType === undefined || template.imageType === imageType) &&
        (osType === undefined || template.osType === osType) &&
        (status === undefined || template.status === status)
    )
  }, [imageType, images, osType, queryState.params, status])

  const pageCount = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE))
  const page = Math.min(queryState.page, pageCount)
  const visible = filtered.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE)

  useEffect(() => {
    if (queryState.page <= pageCount) return
    queryState.update({ page: pageCount <= 1 ? null : pageCount }, { replace: true, resetPage: false })
  }, [pageCount, queryState])

  const metrics = useMemo(() => {
    const source = images ?? []
    return {
      total: source.length,
      importing: source.filter((item) => item.status === ImageStatus.Importing).length,
      errors: source.filter((item) => item.status === ImageStatus.Error).length,
      bytes: source.reduce((total, item) => total + item.fileSize, 0),
    }
  }, [images])

  const columns: AdminDataColumn<ImageTemplateSummary>[] = [
    {
      id: 'name',
      header: '模板',
      width: 'wide',
      render: (template) => (
        <div className={styles.templateName}>
          <span className={styles.templateIcon}>
            {template.imageType === ImageType.Docker ? <Box size={17} /> : <FileArchive size={17} />}
          </span>
          <span>
            <strong>{template.name}</strong>
            <small>#{template.id}</small>
          </span>
        </div>
      ),
    },
    {
      id: 'type',
      header: '类型',
      width: 'compact',
      visibility: 'desktop',
      render: (template) => `${imageTypeLabel(template.imageType)} · ${imageOsLabel(template.osType)}`,
    },
    {
      id: 'source',
      header: 'Registry / Hash',
      width: 'wide',
      visibility: 'desktop',
      render: (template) => (
        <span className={styles.reference}>{template.registryUrl || template.imageHash || '—'}</span>
      ),
    },
    {
      id: 'size',
      header: '大小',
      width: 'compact',
      visibility: 'wide',
      render: (template) => <span className={styles.mono}>{formatBytes(template.fileSize)}</span>,
    },
    {
      id: 'status',
      header: '状态',
      width: 'compact',
      render: (template) => {
        const meta = imageStatusMeta(template.status)
        return (
          <StatusBadge pulse={meta.active} tone={meta.tone}>
            {meta.label}
          </StatusBadge>
        )
      },
    },
    {
      id: 'time',
      header: '登记时间',
      width: 'medium',
      visibility: 'desktop',
      render: (template) => <time className={styles.mono}>{formatAdminTime(template.uploadedAt)}</time>,
    },
    {
      id: 'action',
      header: '操作',
      width: 'compact',
      align: 'right',
      render: (template) => (
        <button
          aria-label={`查看 ${template.name}`}
          className={styles.iconButton}
          onClick={() => setSelectedId(template.id)}
          type="button"
        >
          <Eye size={16} />
        </button>
      ),
    },
  ]

  const completed = async () => {
    setActionFailure(null)
    await mutate()
  }

  const remove = async () => {
    if (!deleteTarget) return false
    setActionFailure(null)
    try {
      await imageTemplateAdminApi.delete(deleteTarget.id)
      setSelectedId(null)
      setDeleteTarget(null)
      await mutate()
      return true
    } catch (removeError) {
      setActionFailure(errorMessage(removeError, '环境模板删除失败。'))
      return false
    }
  }

  const selected = detail.data ?? images?.find((item) => item.id === selectedId) ?? null

  return (
    <div className={styles.page}>
      <AdminPageHeader
        actions={
          <>
            <ActionButton
              icon={<Box size={16} />}
              onClick={() => setActionMode('docker-register')}
              tone="primary"
              type="button"
            >
              注册 Docker
            </ActionButton>
            <ActionButton icon={<Upload size={16} />} onClick={() => setActionMode('docker-upload')} type="button">
              上传 Docker
            </ActionButton>
            <ActionButton icon={<FileArchive size={16} />} onClick={() => setActionMode('vm-upload')} type="button">
              上传 VM
            </ActionButton>
            <ActionButton icon={<FolderInput size={16} />} onClick={() => setActionMode('local-import')} type="button">
              本地导入
            </ActionButton>
          </>
        }
        description="管理 Docker、虚拟机镜像与内部 Registry 中的平台运行资源。"
        eyebrow="RESOURCE CATALOG"
        title="环境模板"
      />

      <MetricStrip>
        <MetricItem
          detail={registry?.enabled ? registry.address : 'Registry 未启用'}
          label="Registry"
          tone={registry?.enabled ? 'success' : 'warning'}
          value={registry?.enabled ? '在线' : '未知'}
        />
        <MetricItem detail="当前可见模板" label="模板总数" value={metrics.total} />
        <MetricItem
          detail="导入或分发中"
          label="处理中"
          tone={metrics.importing ? 'info' : 'neutral'}
          value={metrics.importing}
        />
        <MetricItem
          detail="需要检查"
          label="异常"
          tone={metrics.errors ? 'danger' : 'neutral'}
          value={metrics.errors}
        />
        <MetricItem
          detail={registry ? `单次上限 ${registry.maxUploadSizeGb} GB` : undefined}
          label="登记大小"
          value={formatBytes(metrics.bytes)}
        />
      </MetricStrip>

      <FilterToolbar>
        <ToolbarGroup grow>
          <label className={styles.searchBox}>
            <Search aria-hidden="true" size={16} />
            <input
              aria-label="搜索环境模板"
              onChange={(event: ChangeEvent<HTMLInputElement>) => setQuery(event.currentTarget.value)}
              placeholder="搜索名称、Registry 或 Hash"
              type="search"
              value={query}
            />
          </label>
          <select
            aria-label="镜像类型"
            onChange={(event) => queryState.update({ type: event.currentTarget.value || null })}
            value={queryState.params.get('type') ?? ''}
          >
            <option value="">全部类型</option>
            <option value={ImageType.Docker}>Docker</option>
            <option value={ImageType.Qcow2}>QCOW2</option>
            <option value={ImageType.Ova}>OVA</option>
            <option value={ImageType.Vmdk}>VMDK</option>
          </select>
          <select
            aria-label="操作系统"
            onChange={(event) => queryState.update({ os: event.currentTarget.value || null })}
            value={queryState.params.get('os') ?? ''}
          >
            <option value="">全部系统</option>
            <option value={OSType.Linux}>Linux</option>
            <option value={OSType.Windows}>Windows</option>
          </select>
          <select
            aria-label="模板状态"
            onChange={(event) => queryState.update({ status: event.currentTarget.value || null })}
            value={queryState.params.get('status') ?? ''}
          >
            <option value="">全部状态</option>
            <option value={ImageStatus.Ready}>可用</option>
            <option value={ImageStatus.Importing}>处理中</option>
            <option value={ImageStatus.Error}>异常</option>
            <option value={ImageStatus.Deleting}>删除中</option>
          </select>
        </ToolbarGroup>
        <ToolbarGroup>
          <RefreshIndicator active={isRefreshing} label={metrics.importing ? '自动刷新中' : '状态已同步'} />
        </ToolbarGroup>
      </FilterToolbar>

      {actionFailure ? <InlineFeedback tone="danger">{actionFailure}</InlineFeedback> : null}

      {isLoading ? (
        <DataState description="正在读取 Registry、镜像类型和分发状态。" loading title="环境模板加载中" />
      ) : error ? (
        <DataState description="环境模板接口暂时不可用，请检查服务端连接。" title="环境模板加载失败" />
      ) : (
        <>
          <DataTable
            caption="环境模板列表"
            columns={columns}
            emptyDescription="调整关键词、类型或状态筛选后重试。"
            emptyTitle="没有符合条件的环境模板"
            onRowClick={(template) => setSelectedId(template.id)}
            rowKey={(template) => template.id}
            rows={visible}
          />
          <PaginationBar onPageChange={queryState.setPage} page={page} pageCount={pageCount} total={filtered.length} />
        </>
      )}

      <DetailDrawer
        description={selected ? `${imageTypeLabel(selected.imageType)} · ${imageOsLabel(selected.osType)}` : undefined}
        footer={
          selected ? (
            <ActionButton
              icon={<Trash2 size={16} />}
              onClick={() => setDeleteTarget(selected)}
              tone="danger"
              type="button"
            >
              删除模板
            </ActionButton>
          ) : undefined
        }
        onClose={() => setSelectedId(null)}
        open={selectedId !== null}
        title={selected?.name ?? '环境模板详情'}
      >
        {!selected && !detail.error ? (
          <DataState description="正在读取模板完整状态。" loading title="详情加载中" />
        ) : detail.error ? (
          <DataState description="模板不存在或当前账户没有管理权限。" title="详情加载失败" />
        ) : selected ? (
          <div className={styles.detailBody}>
            <section className={styles.detailIdentity}>
              <span className={styles.detailIcon}>
                {selected.imageType === ImageType.Docker ? <Box size={22} /> : <FileArchive size={22} />}
              </span>
              <div>
                <strong>{selected.name}</strong>
                <span>模板 #{selected.id}</span>
              </div>
              {(() => {
                const meta = imageStatusMeta(selected.status)
                return (
                  <StatusBadge pulse={meta.active} tone={meta.tone}>
                    {meta.label}
                  </StatusBadge>
                )
              })()}
            </section>
            {selected.errorMessage ? <InlineFeedback tone="danger">{selected.errorMessage}</InlineFeedback> : null}
            <dl className={styles.detailGrid}>
              <div>
                <dt>类型</dt>
                <dd>{imageTypeLabel(selected.imageType)}</dd>
              </div>
              <div>
                <dt>操作系统</dt>
                <dd>{imageOsLabel(selected.osType)}</dd>
              </div>
              <div>
                <dt>文件大小</dt>
                <dd>{formatBytes(selected.fileSize)}</dd>
              </div>
              <div>
                <dt>登记时间</dt>
                <dd>{formatAdminTime(selected.uploadedAt)}</dd>
              </div>
              <div className={styles.detailWide}>
                <dt>Registry</dt>
                <dd>{selected.registryUrl || '—'}</dd>
              </div>
              <div className={styles.detailWide}>
                <dt>镜像 Hash</dt>
                <dd>{selected.imageHash || '—'}</dd>
              </div>
              <div className={styles.detailWide}>
                <dt>说明</dt>
                <dd>{selected.description || '暂无说明。'}</dd>
              </div>
            </dl>
          </div>
        ) : null}
      </DetailDrawer>

      {actionMode ? (
        <ImageActionDialog mode={actionMode} onClose={() => setActionMode(null)} onCompleted={completed} open />
      ) : null}

      <VNextConfirmDialog
        confirmLabel="删除模板"
        confirmationText={deleteTarget?.name}
        description="服务端将检查模板引用关系"
        message="模板文件和登记记录将被删除；仍被题目、课程或运行环境引用时，服务端会拒绝该操作。"
        onClose={() => setDeleteTarget(null)}
        onConfirm={remove}
        open={Boolean(deleteTarget)}
        title={`删除 ${deleteTarget?.name ?? '环境模板'}？`}
      />
    </div>
  )
}

import { ActionIcon, Button, Drawer, Menu, Modal, Checkbox, TextInput, SegmentedControl, Select } from '@mantine/core'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Archive, Copy, MoreHorizontal, Plus, RotateCcw, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { useWorkspaceApi, type Schema } from '../../api/client'
import { Empty, ErrorMessage, Loading } from '../../foundation/Feedback'
import { LoadMore } from '../../foundation/LoadMore'
import { Status } from '../../foundation/Status'
import { dateTime } from '../../foundation/format'
import { useCursorList } from '../../foundation/useCursorList'
import { useNavigate } from './../../routing'
import styles from './RecoveryDrawer.module.css'

type RecoveryItem =
  | (Schema<'RecoveryPointSummary'> & { kind: 'point' })
  | (Schema<'BackupSummary'> & { kind: 'backup' })

const consistencyLabel = {
  crash: '崩溃一致',
  filesystem: '文件系统一致',
  application: '应用一致',
}

export function RecoveryDrawer({
  id,
  projectId,
  revision,
  busy,
  canManage,
  canClone,
  canCapture,
  canCaptureMemory,
  onClose,
}: {
  id: string
  projectId: string
  revision: number
  busy: boolean
  canManage: boolean
  canClone: boolean
  canCapture: boolean
  canCaptureMemory: boolean
  onClose: () => void
}) {
  const api = useWorkspaceApi()
  const client = useQueryClient()
  const navigate = useNavigate()
  const [creating, setCreating] = useState(false)
  const [name, setName] = useState('')
  const [includeMemory, setIncludeMemory] = useState(false)
  const [section, setSection] = useState('points')
  const [removing, setRemoving] = useState<RecoveryItem>()
  const [restoring, setRestoring] = useState<RecoveryItem>()
  const [cloning, setCloning] = useState<RecoveryItem>()
  const [backingUp, setBackingUp] = useState<Schema<'RecoveryPointSummary'>>()
  const [repositoryId, setRepositoryId] = useState<string | null>(null)
  const [backupName, setBackupName] = useState('')
  const [cloneName, setCloneName] = useState('')
  const [runClone, setRunClone] = useState(false)
  const points = useCursorList(['recovery-points', id], (page) => api.recoveryPoints(id, page), {
    refetchInterval: (items) => (items.some((point) => ['capturing', 'deleting'].includes(point.state)) ? 2000 : false),
  })
  const backups = useCursorList(['backups', id], (page) => api.backups(id, page), {
    refetchInterval: (items) =>
      items.some((backup) => ['creating', 'deleting'].includes(backup.state)) ? 2000 : false,
  })
  const repositories = useQuery({
    queryKey: ['backup-repositories'],
    queryFn: api.backupRepositories,
    enabled: Boolean(backingUp),
  })
  const list = section === 'points' ? points : backups
  const items: RecoveryItem[] =
    section === 'points'
      ? (points.data ?? []).map((point) => ({ ...point, kind: 'point' }))
      : (backups.data ?? []).map((backup) => ({ ...backup, kind: 'backup' }))
  const refresh = () => {
    for (const key of ['recovery-points', 'backups', 'state', 'operations', 'environment'])
      void client.invalidateQueries({ queryKey: [key, id] })
  }
  const capture = useMutation({
    mutationFn: () =>
      api.captureRecoveryPoint(id, {
        name,
        expectedRevision: revision,
        includeMemory: canCaptureMemory && includeMemory,
      }),
    onSuccess: () => {
      setCreating(false)
      refresh()
    },
  })
  const remove = useMutation({
    mutationFn: (item: RecoveryItem) =>
      item.kind === 'point' ? api.deleteRecoveryPoint(id, item.id) : api.deleteBackup(id, item.id),
    onSuccess: () => {
      setRemoving(undefined)
      refresh()
    },
  })
  const restore = useMutation({
    mutationFn: (item: RecoveryItem) =>
      item.kind === 'point'
        ? api.restoreRecoveryPoint(id, item.id, revision)
        : api.restoreBackup(id, item.id, revision),
    onSuccess: () => {
      setRestoring(undefined)
      refresh()
    },
  })
  const clone = useMutation({
    mutationFn: () =>
      api.createEnvironment({
        name: cloneName,
        projectId,
        sourceEnvironmentId: id,
        ...(cloning!.kind === 'point' ? { recoveryPointId: cloning!.id } : { backupId: cloning!.id }),
        run: runClone,
      }),
    onSuccess: (environment) => {
      void client.invalidateQueries({ queryKey: ['environments'] })
      onClose()
      navigate(`/environments/${environment.id}`)
    },
  })
  const backup = useMutation({
    mutationFn: () =>
      api.createBackup(id, {
        name: backupName,
        recoveryPointId: backingUp!.id,
        repositoryId: repositoryId!,
      }),
    onSuccess: () => {
      setBackingUp(undefined)
      setSection('backups')
      refresh()
    },
  })
  return (
    <>
      <Drawer opened onClose={onClose} title="恢复与备份" position="right" size="lg">
        <div className={styles.body}>
          <SegmentedControl
            fullWidth
            value={section}
            onChange={setSection}
            data={[
              { label: '恢复点', value: 'points' },
              { label: '备份', value: 'backups' },
            ]}
          />
          <ErrorMessage error={list.error} />
          {canManage && section === 'points' && (
            <div className={styles.toolbar}>
              <Button
                leftSection={<Plus size={15} />}
                disabled={busy || !canCapture}
                onClick={() => {
                  capture.reset()
                  setName(dateTime(new Date().toISOString()) + ' 恢复点')
                  setCreating(true)
                }}
              >
                创建恢复点
              </Button>
            </div>
          )}
          {list.isLoading ? (
            <Loading />
          ) : items.length ? (
            items.map((point) => (
              <article key={point.id} className={styles.point}>
                <div className={styles.icon}>
                  <Archive size={20} />
                </div>
                <div className={styles.content}>
                  <div className={styles.heading}>
                    <h3>{point.name}</h3>
                    <Status value={point.state} />
                  </div>
                  <div className={styles.metadata}>
                    <time dateTime={point.createdAt}>{dateTime(point.createdAt)}</time>
                    {point.kind === 'point' && <span>{point.assetCount} 个资产</span>}
                    {point.kind === 'point' && point.state === 'ready' && point.consistency && (
                      <span>{consistencyLabel[point.consistency]}</span>
                    )}
                    {point.kind === 'point' && (point.memoryAssetCount ?? 0) > 0 && (
                      <span>含 {point.memoryAssetCount} 台虚拟机内存</span>
                    )}
                    {point.state === 'ready' && (
                      <span>
                        {new Intl.NumberFormat('zh-CN', {
                          maximumFractionDigits: 1,
                        }).format(point.sizeBytes / 2 ** 20)}{' '}
                        MiB
                      </span>
                    )}
                  </div>
                  {point.error && (
                    <div className={styles.error} role="alert">
                      {point.error}
                    </div>
                  )}
                </div>
                {canManage && (
                  <Menu position="bottom-end">
                    <Menu.Target>
                      <ActionIcon variant="subtle" aria-label={`${point.name}操作`}>
                        <MoreHorizontal size={18} />
                      </ActionIcon>
                    </Menu.Target>
                    <Menu.Dropdown>
                      {point.kind === 'point' && (
                        <Menu.Item
                          leftSection={<Archive size={15} />}
                          disabled={point.state !== 'ready'}
                          onClick={() => {
                            backup.reset()
                            setBackingUp(point)
                            setBackupName(point.name)
                            setRepositoryId(null)
                          }}
                        >
                          保存为备份
                        </Menu.Item>
                      )}
                      {canClone && (
                        <Menu.Item
                          leftSection={<Copy size={15} />}
                          disabled={point.state !== 'ready'}
                          onClick={() => {
                            clone.reset()
                            setCloneName(`${point.name} 副本`)
                            setRunClone(false)
                            setCloning(point)
                          }}
                        >
                          克隆为新环境
                        </Menu.Item>
                      )}
                      <Menu.Item
                        leftSection={<RotateCcw size={15} />}
                        disabled={busy || point.state !== 'ready'}
                        onClick={() => {
                          restore.reset()
                          setRestoring(point)
                        }}
                      >
                        恢复
                      </Menu.Item>
                      <Menu.Item
                        color="red"
                        leftSection={<Trash2 size={15} />}
                        disabled={
                          (point.kind === 'point' && busy) ||
                          point.state === 'capturing' ||
                          point.state === 'creating' ||
                          point.state === 'deleting'
                        }
                        onClick={() => {
                          remove.reset()
                          setRemoving(point)
                        }}
                      >
                        删除
                      </Menu.Item>
                    </Menu.Dropdown>
                  </Menu>
                )}
              </article>
            ))
          ) : (
            <Empty icon={<Archive size={26} />} title={section === 'points' ? '暂无恢复点' : '暂无备份'} />
          )}
          <LoadMore list={list} />
        </div>
      </Drawer>
      <Modal opened={creating} onClose={() => setCreating(false)} title="创建恢复点" centered>
        <form
          onSubmit={(event) => {
            event.preventDefault()
            capture.mutate()
          }}
        >
          <ErrorMessage error={capture.error} />
          <TextInput
            label="名称"
            value={name}
            onChange={(event) => setName(event.currentTarget.value)}
            autoFocus
            required
          />
          <p>捕获期间资产暂停，完成后恢复原状态。</p>
          {canCaptureMemory && (
            <Checkbox
              label="保存虚拟机内存"
              checked={includeMemory}
              onChange={(event) => setIncludeMemory(event.currentTarget.checked)}
            />
          )}
          <div className="dialog-actions">
            <Button variant="default" onClick={() => setCreating(false)}>
              取消
            </Button>
            <Button type="submit" loading={capture.isPending} disabled={busy || !name.trim()}>
              开始捕获
            </Button>
          </div>
        </form>
      </Modal>
      <Modal
        opened={Boolean(removing)}
        onClose={() => setRemoving(undefined)}
        title={removing?.kind === 'backup' ? '删除备份' : '删除恢复点'}
        centered
      >
        <ErrorMessage error={remove.error} />
        <p>删除「{removing?.name}」及其保存的数据？</p>
        <div className="dialog-actions">
          <Button variant="default" onClick={() => setRemoving(undefined)}>
            取消
          </Button>
          <Button color="red" loading={remove.isPending} onClick={() => removing && remove.mutate(removing)}>
            删除
          </Button>
        </div>
      </Modal>
      <Modal opened={Boolean(restoring)} onClose={() => setRestoring(undefined)} title="恢复环境" centered>
        <ErrorMessage error={restore.error} />
        <p>恢复到「{restoring?.name}」？当前资产配置与数据将被替换。</p>
        <div className="dialog-actions">
          <Button variant="default" onClick={() => setRestoring(undefined)}>
            取消
          </Button>
          <Button loading={restore.isPending} disabled={busy} onClick={() => restoring && restore.mutate(restoring)}>
            恢复
          </Button>
        </div>
      </Modal>
      <Modal opened={Boolean(backingUp)} onClose={() => setBackingUp(undefined)} title="保存为备份" centered>
        <form
          className="form-stack"
          onSubmit={(event) => {
            event.preventDefault()
            backup.mutate()
          }}
        >
          <ErrorMessage error={backup.error ?? repositories.error} />
          <TextInput
            label="名称"
            value={backupName}
            onChange={(event) => setBackupName(event.currentTarget.value)}
            autoFocus
            required
          />
          <Select
            label="备份仓库"
            value={repositoryId}
            onChange={setRepositoryId}
            data={(repositories.data ?? [])
              .filter((repo) => repo.state === 'ready')
              .map((repo) => ({ value: repo.id, label: repo.name }))}
            placeholder="选择仓库"
            required
          />
          <div className="dialog-actions">
            <Button variant="default" onClick={() => setBackingUp(undefined)}>
              取消
            </Button>
            <Button type="submit" loading={backup.isPending} disabled={!repositoryId || !backupName.trim()}>
              备份
            </Button>
          </div>
        </form>
      </Modal>
      <Modal opened={Boolean(cloning)} onClose={() => setCloning(undefined)} title="克隆为新环境" centered>
        <form
          onSubmit={(event) => {
            event.preventDefault()
            clone.mutate()
          }}
        >
          <ErrorMessage error={clone.error} />
          <TextInput
            label="环境名称"
            value={cloneName}
            onChange={(event) => setCloneName(event.currentTarget.value)}
            autoFocus
            required
          />
          <Checkbox
            mt="md"
            label="创建后启动"
            checked={runClone}
            onChange={(event) => setRunClone(event.currentTarget.checked)}
          />
          <div className="dialog-actions">
            <Button variant="default" onClick={() => setCloning(undefined)}>
              取消
            </Button>
            <Button type="submit" loading={clone.isPending} disabled={!cloneName.trim()}>
              创建环境
            </Button>
          </div>
        </form>
      </Modal>
    </>
  )
}

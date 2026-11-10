import { Button, Modal, Select } from '@mantine/core'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Monitor } from 'lucide-react'
import { useState } from 'react'
import { useWorkspaceApi, type Asset } from '../../api/client'
import { Empty, ErrorMessage, Loading } from '../../foundation/Feedback'
import { memory } from '../../foundation/format'
import { randomUUID } from '../../foundation/id'
import styles from './MigrationDialog.module.css'

export function MigrationDialog({
  id,
  asset,
  revision,
  onClose,
  onSubmitted,
}: {
  id: string
  asset: Asset
  revision: number
  onClose: () => void
  onSubmitted: () => void
}) {
  const api = useWorkspaceApi()
  const [target, setTarget] = useState<string | null>(null)
  const [storage, setStorage] = useState<string | null>(null)
  const pools = useQuery({
    queryKey: ['storage-pools'],
    queryFn: api.storagePools,
  })
  const template = useQuery({
    queryKey: ['templates', asset.templateId],
    queryFn: () => api.templates({ ids: [asset.templateId] }),
  })
  const nodes = useQuery({
    queryKey: ['migration-destinations', id, asset.id],
    queryFn: () => api.migrationDestinations(id, asset.id),
  })
  const migration = useMutation({
    mutationFn: () =>
      api.migrateAsset(id, asset.id, {
        expectedRevision: revision,
        targetNodeId: target ?? undefined,
        targetStoragePoolId: storage ?? undefined,
        clientRequestId: randomUUID(),
      }),
    onSuccess: () => {
      onSubmitted()
      onClose()
    },
  })
  return (
    <Modal opened onClose={onClose} title={`迁移 ${asset.name}`} size="md" centered>
      <div className={styles.body}>
        {nodes.isPending ? (
          <Loading />
        ) : nodes.isError ? (
          <ErrorMessage error={nodes.error} />
        ) : nodes.data.length ? (
          <fieldset className={styles.targets} disabled={migration.isPending}>
            <legend>目标节点</legend>
            <label className={styles.target}>
              <input
                type="radio"
                name="migration-target"
                checked={target === null}
                onChange={() => {
                  setTarget(null)
                  setStorage(null)
                }}
              />
              <strong>自动选择</strong>
            </label>
            {nodes.data.map((node) => (
              <label key={node.id} className={styles.target}>
                <input
                  type="radio"
                  name="migration-target"
                  checked={target === node.id}
                  onChange={() => {
                    setTarget(node.id)
                    setStorage(null)
                  }}
                />
                <span>
                  <strong>
                    {node.name}
                    {node.current && '（当前节点）'}
                  </strong>
                  <span>
                    {node.available.cpu} 核 · {memory(node.available.memoryMiB)} 可用
                  </span>
                </span>
                <span className={styles.mode}>{node.live ? '在线迁移' : '停机迁移'}</span>
              </label>
            ))}
          </fieldset>
        ) : (
          <Empty icon={<Monitor size={24} />} title="暂无可用迁移节点" />
        )}
        {nodes.data?.length ? (
          <details className={styles.storage}>
            <summary>存储位置</summary>
            <Select
              label="目标存储池"
              placeholder="自动选择"
              clearable
              value={storage}
              onChange={setStorage}
              data={(pools.data ?? [])
                .filter(
                  (pool) =>
                    !pool.error &&
                    (!nodes.data?.find((node) => node.id === target)?.current ||
                      pool.id !== nodes.data.find((node) => node.id === target)?.sourceStoragePoolId) &&
                    (pool.default || pool.state === 'ready') &&
                    (template.data?.[0]?.kind === 'vm' || pool.driver === 'directory') &&
                    (target
                      ? pool.nodeIds.includes(target)
                      : pool.nodeIds.some((id) => nodes.data?.some((node) => node.id === id)))
                )
                .map((pool) => ({ value: pool.id, label: pool.name }))}
            />
          </details>
        ) : null}
        <ErrorMessage error={migration.error ?? pools.error} />
        <div className={styles.actions}>
          <Button variant="default" onClick={onClose}>
            取消
          </Button>
          <Button
            disabled={
              !nodes.data?.length || Boolean(nodes.data?.find((node) => node.id === target)?.current && !storage)
            }
            loading={migration.isPending}
            onClick={() => migration.mutate()}
          >
            {target
              ? nodes.data?.find((node) => node.id === target)?.live &&
                (!storage || storage === nodes.data.find((node) => node.id === target)?.sourceStoragePoolId)
                ? '在线迁移'
                : '停机迁移'
              : '迁移'}
          </Button>
        </div>
      </div>
    </Modal>
  )
}

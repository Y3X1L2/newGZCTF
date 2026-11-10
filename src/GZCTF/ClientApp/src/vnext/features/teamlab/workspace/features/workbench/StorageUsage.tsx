import { ActionIcon, Popover } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { ChevronDown, HardDrive, RefreshCw } from 'lucide-react'
import { useId, useState } from 'react'
import { useWorkspaceApi } from '../../api/client'
import { ErrorMessage, Loading } from '../../foundation/Feedback'
import { byteUnit, reading } from '../../foundation/format'
import styles from './WorkbenchPage.module.css'

export function StorageUsage({ id, revision }: { id: string; revision: number }) {
  const api = useWorkspaceApi()
  const [opened, setOpened] = useState(false)
  const usage = useQuery({
    queryKey: ['environment-storage', id, revision],
    queryFn: ({ signal }) => api.environmentStorage(id, signal),
    staleTime: 60_000,
    refetchInterval: opened ? 60_000 : false,
    retry: false,
  })
  const bytes = usage.data?.bytes
  const value = bytes === undefined ? (usage.error ? '读取失败' : '统计中') : storageSize(bytes)
  return (
    <Popover
      position="bottom-start"
      width={380}
      opened={opened}
      onChange={setOpened}
      shadow="md"
      withArrow
      trapFocus
      returnFocus
    >
      <Popover.Target>
        <button
          className={styles.storageUsage}
          aria-label={`环境占用 ${value}，查看存储详情`}
          aria-expanded={opened}
          onClick={() => setOpened(!opened)}
        >
          <HardDrive size={15} />
          <span>环境占用</span>
          <strong>{value}</strong>
          <ChevronDown size={13} />
        </button>
      </Popover.Target>
      <Popover.Dropdown className={styles.storageDetails} bg="var(--surface)">
        <div className={styles.storageHeading}>
          <h3>环境存储</h3>
          <ActionIcon
            data-autofocus
            variant="subtle"
            aria-label="刷新存储占用"
            loading={usage.isFetching}
            onClick={() => void usage.refetch()}
          >
            <RefreshCw size={16} />
          </ActionIcon>
        </div>
        <ErrorMessage error={usage.error} />
        {usage.isPending && <Loading />}
        {usage.data && (
          <>
            <div className={styles.storageTotal}>
              <span>总占用</span>
              <strong>{value}</strong>
            </div>
            <dl className={styles.storageLegend}>
              <div>
                <dt>
                  <i className={styles.storageBase} />
                  基础镜像
                </dt>
                <dd>{storageSize(usage.data.baseBytes)}</dd>
              </div>
              <div>
                <dt>
                  <i className={styles.storageDelta} />
                  增量数据
                </dt>
                <dd>{storageSize(usage.data.bytes - usage.data.baseBytes)}</dd>
              </div>
            </dl>
            {usage.data.locations.map((location) => (
              <section className={styles.storageLocation} key={location.id}>
                <div className={styles.storageLocationHeading}>
                  <h4>{location.name}</h4>
                </div>
                <StorageWave
                  bytes={location.bytes}
                  baseBytes={location.baseBytes}
                  availableBytes={location.availableBytes}
                />
                <div className={styles.storageBalance}>
                  <span>
                    环境占用 <strong>{storageSize(location.bytes)}</strong>
                  </span>
                  <span>
                    当前可用 <strong>{storageSize(location.availableBytes)}</strong>
                  </span>
                </div>
              </section>
            ))}
          </>
        )}
      </Popover.Dropdown>
    </Popover>
  )
}

function storageSize(bytes: number) {
  const { scale, unit } = byteUnit(bytes)
  return `${reading(bytes, scale)} ${unit}`
}

function StorageWave({
  bytes,
  baseBytes,
  availableBytes,
}: {
  bytes: number
  baseBytes: number
  availableBytes: number
}) {
  const clip = useId()
  const total = bytes + availableBytes
  const base = total > 0 ? (baseBytes / total) * 600 : 0
  const used = total > 0 ? (bytes / total) * 600 : 0
  return (
    <svg
      className={styles.storageWave}
      viewBox="0 0 600 44"
      preserveAspectRatio="none"
      role="img"
      aria-label={`基础镜像 ${storageSize(baseBytes)}，增量数据 ${storageSize(bytes - baseBytes)}，当前可用 ${storageSize(availableBytes)}`}
    >
      <defs>
        <clipPath id={clip}>
          <path
            className={styles.storageWaveMotion}
            d="M0 8 Q75 0 150 8 T300 8 T450 8 T600 8 T750 8 T900 8 T1050 8 T1200 8 V44 H0 Z"
          />
        </clipPath>
      </defs>
      <g clipPath={`url(#${clip})`}>
        <rect width={used} height="44" className={styles.storageDelta} />
        <rect width={base} height="44" className={styles.storageBase} />
      </g>
    </svg>
  )
}

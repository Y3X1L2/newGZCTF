import { GitBranch, Save } from 'lucide-react'
import { useEffect, useId, useMemo, useState } from 'react'
import { ActionButton } from '../../../../shared/Interaction'
import { formatAdminDate } from '../../shared/adminFormat'
import type { TeamLabGameBinding, TeamLabGameReleaseOption, TeamLabGameRollout } from '../../api/teamlabGameAdminApi'
import styles from './TeamLabGame.module.css'

export function TeamLabReleaseSelector({
  binding,
  rollout,
  releases,
  busy,
  onSelect,
}: {
  binding: TeamLabGameBinding | null
  rollout: TeamLabGameRollout | null
  releases: readonly TeamLabGameReleaseOption[]
  busy: boolean
  onSelect: (release: TeamLabGameReleaseOption) => Promise<void>
}) {
  const selectId = useId()
  const [selectedId, setSelectedId] = useState(binding?.activeReleaseId ?? releases[0]?.releaseId ?? '')
  useEffect(() => {
    setSelectedId(binding?.activeReleaseId ?? releases[0]?.releaseId ?? '')
  }, [binding?.activeReleaseId, releases])
  const selected = useMemo(() => releases.find((release) => release.releaseId === selectedId) ?? null, [releases, selectedId])
  const locked = Boolean(rollout && rollout.status !== 'completed' && selectedId !== binding?.activeReleaseId)
  const unchanged = selectedId === binding?.activeReleaseId

  return (
    <section className={styles.releaseSection} aria-labelledby="teamlab-release-title">
      <header className={styles.sectionHeader}>
        <div><h2 id="teamlab-release-title">比赛场景版本</h2></div>
      </header>
      <div className={styles.releaseControl}>
        <label htmlFor={selectId}>
          <span>已发布场景</span>
          <select id={selectId} onChange={(event) => setSelectedId(event.currentTarget.value)} value={selectedId}>
            {releases.map((release) => (
              <option key={release.releaseId} value={release.releaseId}>{release.topologyName} · v{release.version} · {release.networkCount} 网段 / {release.assetCount} 资产</option>
            ))}
          </select>
        </label>
        <ActionButton disabled={!selected || unchanged || locked || busy} icon={<Save size={16} />} onClick={() => selected ? onSelect(selected) : undefined} tone="primary" type="button">
          {busy ? '正在保存' : binding?.topologyId === selected?.topologyId ? '选择版本' : '绑定并选择'}
        </ActionButton>
      </div>
      {selected ? (
        <div className={styles.releaseFacts}>
          <span><GitBranch size={15} /><strong>{selected.topologyName} v{selected.version}</strong></span>
          <span>{selected.networkCount} 个网段</span><span>{selected.assetCount} 个资产</span><time>{formatAdminDate(selected.publishedAt, false)}</time>
        </div>
      ) : null}
      {locked ? <p className={styles.warningText}>清理比赛环境后可更换场景。</p> : null}
    </section>
  )
}

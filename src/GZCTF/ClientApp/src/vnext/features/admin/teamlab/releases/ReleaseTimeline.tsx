import { Check, GitCommitHorizontal } from 'lucide-react'
import { formatAdminDate } from '../../shared/adminFormat'
import type { TeamLabRelease } from '../api'
import styles from './TeamLabReleasesPage.module.css'

export function ReleaseTimeline({
  releases,
  selectedId,
  onSelect,
}: {
  releases: readonly TeamLabRelease[]
  selectedId: string
  onSelect: (releaseId: string) => void
}) {
  return (
    <ol aria-label="发布版本" className={styles.timeline}>
      {releases.map((release) => {
        const selected = release.id === selectedId
        return (
          <li key={release.id}>
            <button aria-pressed={selected} onClick={() => onSelect(release.id)} type="button">
              <span className={styles.timelineMarker}>
                {selected ? <Check aria-hidden="true" size={14} /> : <GitCommitHorizontal aria-hidden="true" size={14} />}
              </span>
              <span className={styles.timelineIdentity}>
                <strong>v{release.version}</strong>
                <small>设计修订 {release.sourceRevision}</small>
                <small>由 {release.publisherName ?? '未知用户'} 发布</small>
              </span>
              <time dateTime={new Date(release.publishedAt).toISOString()}>
                {formatAdminDate(release.publishedAt)}
              </time>
            </button>
          </li>
        )
      })}
    </ol>
  )
}

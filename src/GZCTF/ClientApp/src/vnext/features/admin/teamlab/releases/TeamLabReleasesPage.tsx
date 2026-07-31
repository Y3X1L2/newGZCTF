import { RefreshCw } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import useSWR from 'swr'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { RefreshIndicator, StatusBadge } from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import { teamLabAdminApi, teamLabAdminKeys, teamLabRuntimeApi } from '../api'
import { useTeamLabScene } from '../shared/TeamLabSceneShell'
import { ReleaseReadinessPanel } from './ReleaseReadinessPanel'
import { ReleaseTimeline } from './ReleaseTimeline'
import { TrialRunDialog } from './TrialRunDialog'
import styles from './TeamLabReleasesPage.module.css'

export function TeamLabReleasesPage() {
  const { scene } = useTeamLabScene()
  const navigate = useNavigate()
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [trialOpen, setTrialOpen] = useState(false)
  const [creatingTrial, setCreatingTrial] = useState(false)
  const [operationError, setOperationError] = useState<unknown>(null)
  const releasesRequest = useSWR(
    teamLabAdminKeys.releases(scene.id),
    () => teamLabAdminApi.listReleases(scene.id),
    { keepPreviousData: true, revalidateOnFocus: true }
  )
  const releases = useMemo(
    () => [...(releasesRequest.data ?? [])].sort((left, right) => right.version - left.version),
    [releasesRequest.data]
  )
  const selectedRelease = releases.find((release) => release.id === selectedId) ?? releases[0] ?? null
  const readinessRequest = useSWR(
    selectedRelease ? [...teamLabAdminKeys.plan(scene.id, selectedRelease.id), 'readiness'] : null,
    () => teamLabAdminApi.releaseReadiness(scene.id, selectedRelease!.id),
    { keepPreviousData: false, revalidateOnFocus: true }
  )

  const createTrial = async () => {
    if (!selectedRelease || !readinessRequest.data?.ready || creatingTrial) return false
    setCreatingTrial(true)
    setOperationError(null)
    try {
      const runtime = await teamLabRuntimeApi.createTrial(crypto.randomUUID(), {
        releaseId: selectedRelease.id,
        constraints: null,
        overlays: null,
        externalReference: null,
      })
      setTrialOpen(false)
      navigate(`/admin/teamlab/${scene.id}/runtimes/${runtime.id}`)
      return true
    } catch (error) {
      setOperationError(error)
      return false
    } finally {
      setCreatingTrial(false)
    }
  }

  if (!releasesRequest.data && !releasesRequest.error)
    return <DataState description="正在读取场景的不可变发布记录。" loading title="发布版本加载中" />
  if (releasesRequest.error)
    return <DataState description={errorMessage(releasesRequest.error, '发布版本加载失败。')} title="无法读取发布版本" />
  if (!releases.length)
    return <DataState description="请先在“设计”页保存、校验并发布当前修订。" title="尚无发布版本" />

  return (
    <section className={styles.page}>
      <header className={styles.pageHeader}>
        <div>
          <span>IMMUTABLE RELEASES</span>
          <h2>发布版本</h2>
          <p>核对不可变快照、服务端执行计划、镜像就绪度与试运行状态。</p>
        </div>
        <div className={styles.headerActions}>
          <RefreshIndicator
            active={releasesRequest.isValidating || readinessRequest.isValidating}
            label={releasesRequest.isValidating || readinessRequest.isValidating ? '同步中' : '状态已同步'}
          />
          <ActionButton icon={<RefreshCw size={16} />} onClick={() => void Promise.all([releasesRequest.mutate(), readinessRequest.mutate()])} type="button">
            刷新
          </ActionButton>
        </div>
      </header>

      {operationError ? <InlineFeedback tone="danger">{errorMessage(operationError, '试运行创建失败。')}</InlineFeedback> : null}

      <div className={styles.workspace}>
        <aside className={styles.releaseRail}>
          <div className={styles.railHeading}><span>版本历史</span><strong>{releases.length}</strong></div>
          <ReleaseTimeline releases={releases} selectedId={selectedRelease!.id} onSelect={setSelectedId} />
        </aside>
        <div className={styles.releaseDetail}>
          <header className={styles.releaseIdentity}>
            <div><span>RELEASE</span><h3>v{selectedRelease!.version}</h3></div>
            <StatusBadge tone={selectedRelease!.sourceRevision === scene.revision ? 'success' : 'neutral'}>
              {selectedRelease!.sourceRevision === scene.revision ? '当前设计版本' : `设计修订 ${selectedRelease!.sourceRevision}`}
            </StatusBadge>
          </header>
          <dl className={styles.releaseFacts}>
            <div><dt>发布时间</dt><dd>{formatAdminDate(selectedRelease!.publishedAt)}</dd></div>
            <div><dt>发布人</dt><dd>{selectedRelease!.publishedBy ?? '系统记录不可用'}</dd></div>
            <div><dt>Schema</dt><dd>v{selectedRelease!.schemaVersion}</dd></div>
            <div><dt>内容摘要</dt><dd><code title={selectedRelease!.contentHash}>{selectedRelease!.contentHash.slice(0, 20)}</code></dd></div>
          </dl>

          {!readinessRequest.data && !readinessRequest.error ? (
            <DataState description="正在核对调度、镜像与最近试运行事实。" loading title="计算运行就绪度" />
          ) : readinessRequest.error ? (
            <InlineFeedback tone="danger">{errorMessage(readinessRequest.error, '运行就绪度加载失败。')}</InlineFeedback>
          ) : readinessRequest.data ? (
            <ReleaseReadinessPanel
              creatingTrial={creatingTrial}
              onCreateTrial={() => setTrialOpen(true)}
              readiness={readinessRequest.data}
            />
          ) : null}
        </div>
      </div>
      <TrialRunDialog
        onClose={() => setTrialOpen(false)}
        onConfirm={createTrial}
        open={trialOpen}
        release={selectedRelease}
      />
    </section>
  )
}

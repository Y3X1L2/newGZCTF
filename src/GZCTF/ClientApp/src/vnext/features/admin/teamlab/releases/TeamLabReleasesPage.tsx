import { RefreshCw } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { Link } from 'react-router'
import useSWR from 'swr'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { RefreshIndicator, StatusBadge } from '../../shared/AdminWorkbench'
import { formatAdminDate } from '../../shared/adminFormat'
import { listTeamLabImageOptions, teamLabAdminApi, teamLabAdminKeys, teamLabRuntimeApi, type TeamLabRuntimeOverlay } from '../api'
import { useTeamLabScene } from '../shared/TeamLabSceneShell'
import { TeamLabRuntimesPage } from '../runtimes/TeamLabRuntimesPage'
import { ReleaseReadinessPanel } from './ReleaseReadinessPanel'
import { ReleaseTimeline } from './ReleaseTimeline'
import { createTrialIdempotencyKey, TrialRunDialog } from './TrialRunDialog'
import styles from './TeamLabReleasesPage.module.css'

export function TeamLabReleasesPage() {
  const { scene } = useTeamLabScene()
  const navigate = useNavigate()
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [trialOpen, setTrialOpen] = useState(false)
  const [creatingTrial, setCreatingTrial] = useState(false)
  const [preparingImages, setPreparingImages] = useState(false)
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
  const imagesRequest = useSWR(['vnext:admin:teamlab:image-options'], listTeamLabImageOptions)
  const readinessMatchesSelection = readinessRequest.data?.releaseId === selectedRelease?.id

  const createTrial = async (overlays: readonly TeamLabRuntimeOverlay[] | null) => {
    if (!selectedRelease || !readinessMatchesSelection || !readinessRequest.data?.ready || creatingTrial) return false
    setCreatingTrial(true)
    setOperationError(null)
    try {
      const runtime = await teamLabRuntimeApi.createTrial(createTrialIdempotencyKey(), {
        releaseId: selectedRelease.id,
        constraints: null,
        overlays,
        externalReference: null,
      })
      setTrialOpen(false)
      navigate(`/admin/teamlab/runtimes/${runtime.id}`)
      return true
    } catch (error) {
      setOperationError(error)
      return false
    } finally {
      setCreatingTrial(false)
    }
  }

  const prepareImages = async () => {
    if (!selectedRelease || preparingImages) return
    setPreparingImages(true)
    setOperationError(null)
    try {
      await teamLabAdminApi.prepareReleaseImages(scene.id, selectedRelease.id)
      await readinessRequest.mutate()
    } catch (error) {
      setOperationError(error)
    } finally {
      setPreparingImages(false)
    }
  }

  if (!releasesRequest.data && !releasesRequest.error)
    return <DataState description="正在读取场景的不可变发布记录。" loading title="发布版本加载中" />
  if (releasesRequest.error)
    return <DataState description={errorMessage(releasesRequest.error, '发布版本加载失败。')} title="无法读取发布版本" />
  return (
    <section className={styles.page}>
      <header className={styles.pageHeader}>
        <div>
          <h2>版本与启动</h2>
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
      {releases[0] && releases[0].sourceRevision !== scene.revision ? <p className={styles.draftNotice}>设计草稿有未发布修改。启动只使用所选不可变版本；新设计须先<Link to={`/admin/teamlab/${scene.id}/design`}>检查并发布</Link>。</p> : null}

      {!releases.length ? <DataState description="请先在设计页保存、校验并发布当前修订。" title="尚无发布版本" /> : <>
      <div className={styles.workspace}>
        <div className={styles.releaseDetail}>
          <header className={styles.releaseIdentity}>
            <div><h3>启动版本 v{selectedRelease!.version}</h3></div>
            <StatusBadge tone={selectedRelease!.sourceRevision === scene.revision ? 'success' : 'neutral'}>
              {selectedRelease!.sourceRevision === scene.revision ? '与当前设计一致' : '与当前设计不同'}
            </StatusBadge>
          </header>
          <div className={styles.releaseFacts}>
            <span>{formatAdminDate(selectedRelease!.publishedAt)}</span>
            {selectedRelease!.publisherName ? <span>{selectedRelease!.publisherName}</span> : null}
          </div>

          {!readinessRequest.data && !readinessRequest.error ? (
            <DataState description="" loading title="读取版本状态" />
          ) : readinessRequest.error ? (
            <InlineFeedback tone="danger">{errorMessage(readinessRequest.error, '运行就绪度加载失败。')}</InlineFeedback>
          ) : readinessRequest.data ? (
            <ReleaseReadinessPanel
              creatingTrial={creatingTrial}
              preparingImages={preparingImages}
              onCreateTrial={() => {
                if (readinessMatchesSelection) setTrialOpen(true)
              }}
              onPrepareImages={() => void prepareImages()}
              imageOptions={imagesRequest.data}
              readiness={readinessRequest.data}
            />
          ) : null}
        </div>
      </div>
      {releases.length > 1 ? <details className={styles.versionHistory}>
        <summary>选择历史版本</summary>
        <ReleaseTimeline releases={releases} selectedId={selectedRelease!.id} onSelect={setSelectedId} />
      </details> : null}
      </>}
      <div id="runtimes" className={styles.runtimeHistory}><TeamLabRuntimesPage /></div>
      <TrialRunDialog
        onClose={() => setTrialOpen(false)}
        onConfirm={createTrial}
        open={trialOpen}
        release={selectedRelease}
        submitting={creatingTrial}
      />
    </section>
  )
}

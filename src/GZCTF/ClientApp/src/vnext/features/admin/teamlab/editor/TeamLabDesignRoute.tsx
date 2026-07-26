import { useCallback, useMemo, useRef, useState } from 'react'
import useSWR from 'swr'
import { useSWRConfig } from 'swr'
import { InlineFeedback, VNextConfirmDialog } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import {
  listTeamLabImageOptions,
  teamLabAdminApi,
  teamLabAdminKeys,
  type TeamLabTopologyDetail,
  type TeamLabImageOption,
  type TeamLabRelease,
  type TeamLabValidationIssue,
  type TeamLabValidationResult,
} from '../api'
import { mapDocumentToUpdateRequest, mapTopologyDetailToDocument, type VmDeviceType } from '../model/topologyMapper'
import type { TopologyDocument } from '../model/topologyDocument'
import { useTeamLabScene } from '../shared/TeamLabSceneShell'
import { TeamLabDesignPage, type TeamLabEditorFocusTarget } from './TeamLabDesignPage'
import { useTopologyNavigationGuard } from './state/useTopologyNavigationGuard'
import { useTopologyAutosave } from './state/useTopologyAutosave'
import { SaveConflictDialog } from './validation/SaveConflictDialog'
import { ValidationDrawer } from './validation/ValidationDrawer'
import { locateValidationIssue } from './validation/validationLocator'
import styles from './TeamLabDesignRoute.module.css'

interface TeamLabDesignSessionProps {
  scene: TeamLabTopologyDetail
  imageOptions: readonly TeamLabImageOption[]
  releases: readonly TeamLabRelease[] | undefined
  releasesError: unknown
  refreshReleases: () => Promise<readonly TeamLabRelease[] | undefined>
  onReload: () => Promise<void>
}

function TeamLabDesignSession({
  scene,
  imageOptions,
  releases,
  releasesError,
  refreshReleases,
  onReload,
}: TeamLabDesignSessionProps) {
  const { mutate } = useSWRConfig()
  const initialDocument = useMemo(
    () =>
      mapTopologyDetailToDocument(scene, {
        resolveVmDeviceType: (asset) =>
          (imageOptions.find((option) => option.id === asset.imageTemplateId)?.deviceType as VmDeviceType | undefined) ??
          'linux-vm',
      }),
    [imageOptions, scene]
  )
  const [draft, setDraft] = useState(initialDocument)
  const revisionRef = useRef(scene.revision)
  const [savedRevision, setSavedRevision] = useState(scene.revision)
  const [validation, setValidation] = useState<{ revision: number; result: TeamLabValidationResult } | null>(null)
  const [validationOpen, setValidationOpen] = useState(false)
  const [validating, setValidating] = useState(false)
  const [publishOpen, setPublishOpen] = useState(false)
  const [publishing, setPublishing] = useState(false)
  const [operationError, setOperationError] = useState<unknown>(null)
  const [focusTarget, setFocusTarget] = useState<TeamLabEditorFocusTarget | null>(null)

  const save = useCallback(
    (document: TopologyDocument, revision: number) =>
      teamLabAdminApi.updateTopology(scene.id, mapDocumentToUpdateRequest(document, revision)),
    [scene.id]
  )
  const saved = useCallback(
    (detail: TeamLabTopologyDetail) => {
      revisionRef.current = detail.revision
      setSavedRevision(detail.revision)
      setValidation(null)
      void mutate(teamLabAdminKeys.topology(scene.id), detail, { revalidate: false })
    },
    [mutate, scene.id]
  )
  const autosave = useTopologyAutosave({
    initialRevision: scene.revision,
    initialDocument,
    save,
    onSaved: saved,
  })
  const flushDraft = useCallback(() => autosave.flush(), [autosave.flush])
  useTopologyNavigationGuard(autosave.status !== 'saved', flushDraft)

  const documentChanged = useCallback(
    (document: TopologyDocument) => {
      setDraft(document)
      setValidation(null)
      setOperationError(null)
      autosave.schedule(document)
    },
    [autosave]
  )
  const validate = useCallback(async () => {
    setValidating(true)
    setOperationError(null)
    try {
      if (!(await autosave.flush())) return
      const result = await teamLabAdminApi.validateTopology(scene.id)
      setValidation({ revision: revisionRef.current, result })
      setValidationOpen(true)
    } catch (error) {
      setOperationError(error)
    } finally {
      setValidating(false)
    }
  }, [autosave, draft, scene.id])
  const currentValidation = validation?.revision === savedRevision ? validation.result : null
  const latestRelease = useMemo(
    () => [...(releases ?? [])].sort((left, right) => right.version - left.version)[0] ?? null,
    [releases]
  )
  const hasUnpublishedChanges = autosave.status !== 'saved' || latestRelease?.sourceRevision !== savedRevision
  const publicationState = releasesError
    ? 'loading'
    : !releases
      ? 'loading'
      : !latestRelease
        ? 'unpublished'
        : hasUnpublishedChanges
          ? 'changed'
          : 'current'
  const publicationStatus = releasesError
    ? '发布状态暂不可用'
    : !releases
      ? '正在读取发布状态'
      : !latestRelease
        ? '当前设计尚未发布'
        : hasUnpublishedChanges
          ? `存在未发布更改 · 下一版本 v${latestRelease.version + 1}`
          : `与最新版本 v${latestRelease.version} 一致`

  const publish = useCallback(async () => {
    if (!currentValidation?.valid || publishing) return false
    setPublishing(true)
    setOperationError(null)
    try {
      if (!(await autosave.flush())) return false
      if (validation?.revision !== revisionRef.current) {
        setOperationError(new Error('当前设计在校验后发生了变化，请重新校验。'))
        return false
      }
      await teamLabAdminApi.publishTopology(scene.id, { revision: revisionRef.current })
      await refreshReleases()
      setPublishOpen(false)
      return true
    } catch (error) {
      setOperationError(error)
      return false
    } finally {
      setPublishing(false)
    }
  }, [autosave, currentValidation?.valid, draft, publishing, refreshReleases, scene.id, validation?.revision])
  const locate = useCallback(
    (issue: TeamLabValidationIssue) => {
      const location = locateValidationIssue(draft, issue)
      setFocusTarget((current) => ({
        nodeKey: location.nodeKey,
        connectionKey: location.connectionKey,
        requestId: (current?.requestId ?? 0) + 1,
      }))
      setValidationOpen(false)
    },
    [draft]
  )

  return (
    <div className={styles.session}>
      {autosave.error && autosave.status === 'error' ? (
        <InlineFeedback tone="danger">{errorMessage(autosave.error, '场景保存失败，请检查配置后重试。')}</InlineFeedback>
      ) : null}
      {operationError ? (
        <InlineFeedback tone="danger">{errorMessage(operationError, '当前设计操作失败。')}</InlineFeedback>
      ) : null}
      {validating ? <div aria-live="polite" className={styles.validating}>正在保存当前设计并执行服务端校验...</div> : null}
      <TeamLabDesignPage
        focusTarget={focusTarget}
        imageOptions={imageOptions}
        initialDocument={initialDocument}
        onDocumentChange={documentChanged}
        onSave={async (document) => {
          await autosave.flush(document)
        }}
        onPublish={() => setPublishOpen(true)}
        onValidate={validate}
        publicationState={publicationState}
        publicationStatus={publicationStatus}
        publishDisabled={!currentValidation?.valid || !hasUnpublishedChanges}
        publishing={publishing}
        saveStatus={autosave.status}
        validationIssueCount={currentValidation?.issues.length ?? 0}
      />
      <ValidationDrawer
        onClose={() => setValidationOpen(false)}
        onLocate={locate}
        open={validationOpen}
        result={currentValidation}
      />
      <SaveConflictDialog conflict={autosave.conflict} onReload={() => void onReload()} />
      <VNextConfirmDialog
        confirmLabel={publishing ? '正在发布' : '确认发布'}
        description="发布后该版本不可修改；后续编辑仍在当前设计中继续，并可发布为下一个版本。"
        message={`将场景“${scene.definition.name}”的当前设计发布为 ${latestRelease ? `v${latestRelease.version + 1}` : 'v1'}。`}
        onClose={() => setPublishOpen(false)}
        onConfirm={publish}
        open={publishOpen}
        title="发布当前设计"
        tone="primary"
      />
    </div>
  )
}

export function TeamLabDesignRoute() {
  const { scene } = useTeamLabScene()
  const { mutate } = useSWRConfig()
  const [session, setSession] = useState(0)
  const images = useSWR(
    ['vnext:admin:teamlab:image-options'],
    listTeamLabImageOptions,
    { keepPreviousData: true, revalidateOnFocus: true }
  )
  const releases = useSWR(
    teamLabAdminKeys.releases(scene.id),
    () => teamLabAdminApi.listReleases(scene.id),
    { keepPreviousData: true, revalidateOnFocus: true }
  )

  const reload = useCallback(async () => {
    await mutate(teamLabAdminKeys.topology(scene.id))
    setSession((value) => value + 1)
  }, [mutate, scene.id])

  if (images.error)
    return <DataState description={errorMessage(images.error, '无法读取可用镜像目录。')} title="设计器加载失败" />
  if (!images.data)
    return <DataState description="正在读取 Docker、Linux VM 和 Windows VM 可用镜像。" loading title="准备设计器" />

  return (
    <TeamLabDesignSession
      imageOptions={images.data}
      key={`${scene.id}:${session}`}
      onReload={reload}
      refreshReleases={() => releases.mutate()}
      releases={releases.data}
      releasesError={releases.error}
      scene={scene}
    />
  )
}

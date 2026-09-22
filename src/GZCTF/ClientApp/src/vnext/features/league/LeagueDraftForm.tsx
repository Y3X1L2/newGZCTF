import { useBlocker } from 'react-router'
import type { LeagueDraftModel, LeagueMatchDetail } from '@Api'
import { TextField, SelectField } from '../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../shared/Interaction'
import { CursorPaginationBar } from '../admin/shared/AdminWorkbench'
import styles from './League.module.css'
import { leagueError } from './leaguePresentation'
import { useLeagueSceneCatalog } from './useLeagueController'
import { useLeagueDraft } from './useLeagueDraft'

export function LeagueDraftForm({
  identity,
  detail,
  busy,
  onSave,
}: {
  identity: string
  detail?: LeagueMatchDetail
  busy: boolean
  onSave: (draft: LeagueDraftModel, revision?: number) => Promise<LeagueMatchDetail | undefined>
}) {
  const form = useLeagueDraft(detail)
  const catalog = useLeagueSceneCatalog(identity, form.draft.topologyId)
  const blocker = useBlocker(form.dirty && !busy)
  return (
    <form
      className={styles.panel}
      onSubmit={async (event) => {
        event.preventDefault()
        if (busy || form.stale) return
        const data = form.payload()
        if (!data) return
        const saved = await onSave(data, form.revision)
        if (saved) form.reset(saved)
      }}
    >
      <h2>{detail ? '场次配置' : '创建场次'}</h2>
      <p>创建后即可报名，由管理员审核并确定两队。准备环境后配置将固定。</p>
      {form.stale ? (
        <InlineFeedback tone="danger">
          服务器配置已更新：{detail?.match?.name}，初始金币 {detail?.initialCoins}
          。当前输入保留，请先核对并载入最新配置。
        </InlineFeedback>
      ) : null}
      {form.error ? <InlineFeedback tone="danger">{form.error}</InlineFeedback> : null}
      <fieldset disabled={busy} className={styles.fields}>
        <TextField
          label="场次名称"
          required
          maxLength={160}
          value={form.draft.name}
          onValueChange={(name) => form.setDraft((draft) => ({ ...draft, name }))}
        />
        <TextField
          label="初始金币"
          required
          type="number"
          min={0}
          max={2147483647}
          step={1}
          value={form.draft.initialCoins}
          onValueChange={(initialCoins) => form.setDraft((draft) => ({ ...draft, initialCoins }))}
          hint="每场每队的初始金额，金币不用于首期胜负判定。"
        />
        <SelectField
          label="固定场景"
          value={form.draft.topologyId}
          onValueChange={(topologyId) => form.setDraft((draft) => ({ ...draft, topologyId, releaseId: '' }))}
        >
          <option value="">暂不配置</option>
          {form.draft.topologyId && !catalog.scenes.data?.items.some((scene) => scene.id === form.draft.topologyId) ? (
            <option value={form.draft.topologyId}>已选场景 · {form.draft.topologyId}</option>
          ) : null}
          {catalog.scenes.data?.items.map((scene) => (
            <option key={scene.id} value={scene.id}>
              {scene.name}
            </option>
          ))}
        </SelectField>
        <SelectField
          label="发布版本"
          disabled={!form.draft.topologyId || catalog.releases.isLoading}
          value={form.draft.releaseId}
          onValueChange={(releaseId) => form.setDraft((draft) => ({ ...draft, releaseId }))}
        >
          <option value="">{catalog.releases.isLoading ? '版本加载中' : '请选择发布版本'}</option>
          {form.draft.releaseId && !catalog.releases.data?.some((release) => release.id === form.draft.releaseId) ? (
            <option value={form.draft.releaseId}>已选版本 · {form.draft.releaseId}</option>
          ) : null}
          {catalog.releases.data?.map((release) => (
            <option key={release.id} value={release.id}>
              版本 {release.version} · {new Date(release.publishedAt).toLocaleDateString('zh-CN')}
            </option>
          ))}
        </SelectField>
      </fieldset>
      {catalog.scenes.isLoading ? <p role="status">正在读取场景目录…</p> : null}
      {catalog.scenes.error || catalog.releases.error ? (
        <InlineFeedback tone="danger">
          {leagueError(catalog.scenes.error || catalog.releases.error)}{' '}
          <ActionButton
            type="button"
            onClick={() => {
              void catalog.scenes.mutate()
              void catalog.releases.mutate()
            }}
          >
            重试目录
          </ActionButton>
        </InlineFeedback>
      ) : null}
      {form.draft.topologyId && catalog.releases.data?.length === 0 ? (
        <p>该场景尚无发布版本，请先在 TeamLab 中发布，或选择其他场景。</p>
      ) : null}
      <CursorPaginationBar
        label="场景目录"
        page={catalog.page}
        hasNext={Boolean(catalog.scenes.data?.nextCursor)}
        onPrevious={catalog.previous}
        onNext={catalog.next}
      />
      <div className={styles.actions}>
        <ActionButton tone="primary" disabled={busy || form.stale} type="submit">
          {busy ? '正在保存…' : detail ? '保存配置' : '创建场次'}
        </ActionButton>
        {detail ? (
          <ActionButton disabled={busy} type="button" onClick={() => form.reset()}>
            放弃输入，载入最新配置
          </ActionButton>
        ) : null}
        <span>{form.dirty ? '有未保存的修改' : detail ? '配置已同步' : '场景可稍后配置'}</span>
      </div>
      <VNextConfirmDialog
        open={blocker.state === 'blocked'}
        title="离开未保存的配置？"
        message="当前输入尚未保存。离开后将丢弃这些修改。"
        confirmLabel="放弃修改并离开"
        onClose={() => {
          if (blocker.state === 'blocked') blocker.reset()
        }}
        onConfirm={() => {
          if (blocker.state === 'blocked') blocker.proceed()
          return false
        }}
      />
    </form>
  )
}

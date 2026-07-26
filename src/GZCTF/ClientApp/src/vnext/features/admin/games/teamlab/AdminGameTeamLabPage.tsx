import { Network, RefreshCw } from 'lucide-react'
import { useState } from 'react'
import { useOutletContext } from 'react-router'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { DataState } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { AdminPageHeader, RefreshIndicator } from '../../shared/AdminWorkbench'
import { teamLabGameAdminApi, type TeamLabGameReleaseOption, type TeamLabGameRollout, type TeamLabGameTarget } from '../../api/teamlabGameAdminApi'
import { supportsTeamLabGame, type GameAdminOutletContext } from '../GameAdminShell'
import { GameObjectiveBindingEditor } from './GameObjectiveBindingEditor'
import { TeamLabReleaseSelector } from './TeamLabReleaseSelector'
import { TeamLabRolloutControls } from './TeamLabRolloutControls'
import { TeamLabRolloutSummary } from './TeamLabRolloutSummary'
import { TeamLabTargetDrawer } from './TeamLabTargetDrawer'
import { TeamLabTargetTable } from './TeamLabTargetTable'
import { useGameTeamLab } from './useGameTeamLab'
import styles from './TeamLabGame.module.css'

export function AdminGameTeamLabPage() {
  const { game } = useOutletContext<GameAdminOutletContext>()
  const gameId = game.id ?? 0
  const data = useGameTeamLab(gameId)
  const [busy, setBusy] = useState(false)
  const [objectivesDirty, setObjectivesDirty] = useState(false)
  const [actionError, setActionError] = useState<unknown>(null)
  const [selectedTarget, setSelectedTarget] = useState<TeamLabGameTarget | null>(null)

  if (gameId <= 0) return <DataState description="比赛编号无效。" title="无法打开 TeamLab 编排" />
  if (!supportsTeamLabGame(game.gameType)) {
    return <DataState description="仅渗透演练和混合赛制可以绑定 TeamLab 场景。" title="当前赛制不支持 TeamLab" />
  }
  if (data.isLoading) return <DataState description="正在并行读取比赛绑定、Release 与 rollout 状态。" loading title="TeamLab 编排加载中" />
  if (data.stateError || !data.state) return <DataState description={errorMessage(data.stateError, 'TeamLab 比赛状态加载失败。')} title="无法打开 TeamLab 编排" />

  const updateRollout = async (action: () => Promise<TeamLabGameRollout>) => {
    if (busy) return
    setBusy(true)
    setActionError(null)
    try {
      const rollout = await action()
      await data.mutateState({ binding: data.state!.binding, rollout }, { revalidate: false })
      await data.targets.mutate()
    } catch (error) {
      setActionError(error)
    } finally {
      setBusy(false)
    }
  }

  const selectRelease = async (release: TeamLabGameReleaseOption) => {
    if (busy) return
    setBusy(true)
    setActionError(null)
    try {
      if (data.state!.binding?.topologyId !== release.topologyId) {
        await teamLabGameAdminApi.bind(gameId, release.topologyId)
      }
      await teamLabGameAdminApi.activateRelease(gameId, release.releaseId)
      await data.mutateState()
    } catch (error) {
      setActionError(error)
    } finally {
      setBusy(false)
    }
  }

  const refreshTargets = async () => {
    await Promise.all([data.mutateState(), data.targets.mutate()])
  }

  return (
    <main className={styles.page}>
      <AdminPageHeader
        actions={<div className={styles.headerActions}><RefreshIndicator active={data.isRefreshing} label={data.isRefreshing ? '同步中' : '状态已同步'} /><ActionButton icon={<RefreshCw size={15} />} onClick={() => void refreshTargets()} type="button">刷新</ActionButton></div>}
        description="选择不可变场景版本，提前准备各队环境，并控制比赛入口与结束清理。"
        eyebrow="TEAMLAB ORCHESTRATION"
        title="TeamLab 编排"
      />
      {actionError ? <InlineFeedback tone="danger">{errorMessage(actionError, 'TeamLab 编排操作失败。')}</InlineFeedback> : null}
      {data.releasesError ? <InlineFeedback tone="danger">{errorMessage(data.releasesError, '可用 Release 加载失败。')}</InlineFeedback> : null}
      {data.topologyError ? <InlineFeedback tone="danger">{errorMessage(data.topologyError, '绑定场景加载失败。')}</InlineFeedback> : null}

      {data.releases.length ? (
        <TeamLabReleaseSelector binding={data.state.binding} busy={busy || objectivesDirty} onSelect={selectRelease} releases={data.releases} rollout={data.state.rollout} />
      ) : <DataState description="请先在 TeamLab 场景库中校验并发布一个不可变版本。" title="暂无可用 Release" />}

      <GameObjectiveBindingEditor
        assets={data.topology?.definition.assets ?? []}
        binding={data.state.binding}
        loading={data.topologyLoading}
        onSave={async (request) => {
          const binding = await teamLabGameAdminApi.replaceObjectives(gameId, request)
          await data.mutateState({ binding, rollout: data.state!.rollout }, { revalidate: false })
          return binding
        }}
        onDirtyChange={setObjectivesDirty}
        rollout={data.state.rollout}
      />

      <TeamLabRolloutControls
        binding={data.state.binding}
        busy={busy}
        configurationDirty={objectivesDirty}
        onAccess={(open) => updateRollout(() => teamLabGameAdminApi.setAccess(gameId, open))}
        onDrain={() => updateRollout(() => teamLabGameAdminApi.drain(gameId))}
        onPrepare={() => updateRollout(() => teamLabGameAdminApi.prepare(gameId))}
        rollout={data.state.rollout}
      />

      {data.state.rollout ? (
        <>
          <TeamLabRolloutSummary rollout={data.state.rollout} />
          <TeamLabTargetTable onSelect={setSelectedTarget} targets={data.targets} />
        </>
      ) : (
        <section className={styles.notPrepared}><Network size={20} /><DataState description="选择 Release 后点击“提前准备环境”，系统会按已审核队伍建立可观测部署目标。" title="比赛环境尚未准备" /></section>
      )}

      <TeamLabTargetDrawer
        onCleanup={async (target) => { await teamLabGameAdminApi.cleanupTeam(gameId, target.teamId); setSelectedTarget(null); await refreshTargets() }}
        onClose={() => setSelectedTarget(null)}
        onRebuild={async (target) => { await teamLabGameAdminApi.rebuildTeam(gameId, target.teamId); setSelectedTarget(null); await refreshTargets() }}
        rollout={data.state.rollout}
        target={selectedTarget}
        topologyId={data.state.binding?.topologyId ?? null}
      />
    </main>
  )
}

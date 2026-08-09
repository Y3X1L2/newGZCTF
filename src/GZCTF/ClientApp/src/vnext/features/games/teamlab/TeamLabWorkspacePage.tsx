import { RotateCcw } from 'lucide-react'
import { useState } from 'react'
import { ActionButton, InlineFeedback } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import { errorMessage } from '../../../shared/errors'
import { useGameWorkspace } from '../workspace/GameWorkspaceShell'
import { PlayerAccessPanel } from './PlayerAccessPanel'
import { PlayerObjectiveList } from './PlayerObjectiveList'
import { PlayerResetDialog } from './PlayerResetDialog'
import { PlayerRuntimeStages } from './PlayerRuntimeStages'
import { PlayerRuntimeStatus } from './PlayerRuntimeStatus'
import styles from './TeamLabWorkspacePage.module.css'
import { usePlayerTeamLab } from './usePlayerTeamLab'

export function TeamLabWorkspacePage() {
  const { gameId, revision } = useGameWorkspace()
  const request = usePlayerTeamLab(gameId)
  const [resetOpen, setResetOpen] = useState(false)

  if (request.isLoading)
    return (
      <div className={styles.state}>
        <DataState description="正在读取队伍环境和任务目标。" loading title="渗透工作区加载中" />
      </div>
    )
  if (request.error || !request.workspace)
    return (
      <div className={styles.state}>
        <DataState
          description={errorMessage(request.error, '队伍环境尚未部署或当前不可访问。')}
          title="无法打开渗透工作区"
        />
      </div>
    )

  const workspace = request.workspace
  void revision
  return (
    <div className={styles.page}>
      <header className={styles.pageHeader}>
        <div>
          <span>PENETRATION WORKSPACE</span>
          <h1>渗透演练</h1>
        </div>
        <ActionButton
          disabled={workspace.resetAllowance.remaining <= 0 || workspace.status === 'destroying'}
          icon={<RotateCcw size={16} />}
          onClick={() => setResetOpen(true)}
          type="button"
        >
          重置环境
        </ActionButton>
      </header>
      {request.isRefreshing ? <span className={styles.syncing}>状态同步中</span> : null}
      {workspace.status === 'failed' ? (
        <InlineFeedback tone="danger">环境部署失败，请联系比赛管理员查看运行日志。</InlineFeedback>
      ) : null}
      <PlayerRuntimeStatus workspace={workspace} />
      <PlayerRuntimeStages workspace={workspace} />
      <div className={styles.contentGrid}>
        <PlayerObjectiveList gameId={gameId} onSubmitted={() => void request.mutate()} workspace={workspace} />
        <PlayerAccessPanel gameId={gameId} ready={workspace.status === 'running'} runtimeId={workspace.runtimeId} />
      </div>
      <PlayerResetDialog
        gameId={gameId}
        onClose={() => setResetOpen(false)}
        onReset={() => {
          setResetOpen(false)
          void request.mutate()
        }}
        open={resetOpen}
      />
    </div>
  )
}

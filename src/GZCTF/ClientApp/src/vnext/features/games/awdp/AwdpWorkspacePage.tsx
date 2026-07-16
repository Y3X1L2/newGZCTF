import { useEffect, useMemo, useState } from 'react'
import { useConfig } from '@Hooks/useConfig'
import { InlineFeedback, VNextConfirmDialog } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import { errorMessage } from '../../../shared/errors'
import { useVNextPageTitle } from '../../../shared/useVNextPageTitle'
import { AwdpInstance, awdpPhase, defenseMeta } from '../../awdp/awdpDomain'
import { useGameWorkspace } from '../workspace/GameWorkspaceShell'
import { AwdpActionPanel } from './AwdpActionPanel'
import { AwdpActivityPanels } from './AwdpActivityPanels'
import { AwdpServiceTable } from './AwdpServiceTable'
import { AwdpStageHeader } from './AwdpStageHeader'
import styles from './AwdpWorkspace.module.css'
import { useAwdpWorkspaceController } from './useAwdpWorkspaceController'

type Confirmation = { kind: 'recover' | 'reset'; instance: AwdpInstance }

export function AwdpWorkspacePage() {
  const { gameId, game } = useGameWorkspace()
  const { config } = useConfig()
  const controller = useAwdpWorkspaceController(gameId, config.apiPublicKey, game.teamName)
  const [confirmation, setConfirmation] = useState<Confirmation | null>(null)
  const [now, setNow] = useState(Date.now())
  useVNextPageTitle(`${game.title} · AWDP`)

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [])

  const metrics = useMemo(() => {
    const snapshot = controller.snapshot
    const manageable = snapshot?.instances.filter((item) => item.canManage) ?? []
    return {
      running: snapshot?.instances.filter((item) => item.running).length ?? 0,
      total: snapshot?.instances.length ?? 0,
      defended: snapshot?.patchStatus.filter((item) => defenseMeta(item.defenseStatus).tone === 'success').length ?? 0,
      serviceCount: snapshot?.patchStatus.length ?? 0,
      myScore: snapshot?.scoreboard.find((item) => item.teamId === controller.myTeamId),
      manageable,
    }
  }, [controller.myTeamId, controller.snapshot])

  if (controller.loading && !controller.snapshot)
    return (
      <div className={styles.statePage}>
        <DataState description="正在读取轮次、服务和计分状态。" loading title="AWDP 工作区加载中" />
      </div>
    )
  if (!controller.snapshot)
    return (
      <div className={styles.statePage}>
        <DataState
          description={errorMessage(controller.error, '比赛尚未配置 AWDP，或当前账户没有访问权限。')}
          title="无法打开 AWDP 工作区"
        />
      </div>
    )
  const snapshot = controller.snapshot
  const phase = awdpPhase(snapshot.status.status)
  const executeConfirmation = async () => {
    if (!confirmation) return false
    const success = await controller.runInstanceAction(confirmation.kind, confirmation.instance)
    if (success) setConfirmation(null)
    return success
  }

  return (
    <div className={styles.page}>
      <AwdpStageHeader
        defended={metrics.defended}
        monitorState={controller.monitorState}
        myScore={metrics.myScore}
        now={now}
        onRefresh={controller.refresh}
        refreshing={controller.refreshing}
        running={metrics.running}
        serviceCount={metrics.serviceCount}
        status={snapshot.status}
        total={metrics.total}
      />
      {controller.feedback ? (
        <InlineFeedback tone={controller.feedback.tone}>{controller.feedback.message}</InlineFeedback>
      ) : null}
      {controller.error ? (
        <InlineFeedback tone="danger">
          {errorMessage(controller.error, 'AWDP 状态刷新失败，当前显示最后一次有效快照。')}
        </InlineFeedback>
      ) : null}
      <AwdpActionPanel
        onSubmitFlag={controller.submitFlag}
        onSubmitPatch={controller.submitPatch}
        operation={controller.operation}
        patchStatus={snapshot.patchStatus}
        phase={phase}
      />
      <AwdpServiceTable
        instances={snapshot.instances}
        myTeamId={controller.myTeamId}
        onAction={(kind, instance) => setConfirmation({ kind, instance })}
        operation={controller.operation}
      />
      <AwdpActivityPanels
        attackLogs={snapshot.attackLogs}
        myTeamId={controller.myTeamId}
        patchStatus={snapshot.patchStatus}
        scoreboard={snapshot.scoreboard}
      />
      <VNextConfirmDialog
        confirmLabel={confirmation?.kind === 'reset' ? '确认重置' : '确认恢复'}
        description={
          confirmation?.kind === 'reset'
            ? '重置会重新创建当前服务实例，可能清除运行时修改。'
            : '恢复会回到原始未修补镜像，并清除当前补丁结果。'
        }
        message={
          confirmation
            ? `${confirmation.instance.serviceName} 当前剩余${confirmation.kind === 'reset' ? '重置' : '恢复'}次数为 ${confirmation.kind === 'reset' ? confirmation.instance.remainingResetCount : confirmation.instance.remainingRecoveryCount}。`
            : ''
        }
        onClose={() => setConfirmation(null)}
        onConfirm={executeConfirmation}
        open={Boolean(confirmation)}
        title={confirmation?.kind === 'reset' ? '重置本队实例？' : '恢复原始实例？'}
      />
    </div>
  )
}

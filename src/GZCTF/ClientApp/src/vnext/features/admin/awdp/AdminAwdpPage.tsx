import { Play, RefreshCw, Square } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useOutletContext, useSearchParams } from 'react-router'
import { ActionButton, InlineFeedback, VNextConfirmDialog } from '../../../shared/Interaction'
import { DataState } from '../../../shared/Primitives'
import { errorMessage } from '../../../shared/errors'
import { useVNextPageTitle } from '../../../shared/useVNextPageTitle'
import { awdpPhase, patchMeta, phaseMeta } from '../../awdp/awdpDomain'
import type { GameAdminOutletContext } from '../games/GameAdminShell'
import { AdminPageHeader, MetricItem, MetricStrip, RefreshIndicator, StatusBadge } from '../shared/AdminWorkbench'
import styles from './AdminAwdp.module.css'
import { AwdpAttackLogPanel, AwdpPatchLogPanel, AwdpScoreboardPanel } from './AwdpAuditPanels'
import { AwdpRuntimePanel } from './AwdpRuntimePanel'
import { AwdpServicePanel } from './AwdpServicePanel'
import { awdpServiceWarnings } from './awdpServiceForm'
import { useAdminAwdpController } from './useAdminAwdpController'

type Tab = 'attacks' | 'patches' | 'runtime' | 'scoreboard' | 'services'
const tabs: Array<{ id: Tab; label: string }> = [
  { id: 'services', label: '服务配置' },
  { id: 'runtime', label: '轮次与实例' },
  { id: 'patches', label: '补丁记录' },
  { id: 'attacks', label: '攻击日志' },
  { id: 'scoreboard', label: 'AWDP 榜单' },
]

export function AdminAwdpPage() {
  const { game } = useOutletContext<GameAdminOutletContext>()
  const gameId = game.id as number
  const controller = useAdminAwdpController(gameId)
  const [params, setParams] = useSearchParams()
  const [lifecycleAction, setLifecycleAction] = useState<'start' | 'stop' | null>(null)
  const requestedTab = params.get('tab') as Tab | null
  const tab: Tab = tabs.some((item) => item.id === requestedTab) ? requestedTab! : 'services'
  useVNextPageTitle(`${game.title} · AWDP 管理`)

  const setTab = (nextTab: Tab) => {
    const next = new URLSearchParams(params)
    next.set('tab', nextTab)
    setParams(next, { replace: true })
  }
  const metrics = useMemo(() => {
    const snapshot = controller.snapshot
    return {
      running: snapshot?.instances.filter((item) => item.running).length ?? 0,
      instances: snapshot?.instances.length ?? 0,
      completeServices: snapshot?.services.filter((service) => awdpServiceWarnings(service).length === 0).length ?? 0,
      successfulPatches:
        snapshot?.patches.filter((patch) => patchMeta(patch.finalStatus).tone === 'success').length ?? 0,
    }
  }, [controller.snapshot])

  if (controller.loading && !controller.snapshot)
    return <DataState description="正在读取服务、轮次和运行实例。" loading title="AWDP 管理加载中" />
  if (!controller.snapshot)
    return (
      <DataState
        description={errorMessage(controller.error, '当前比赛没有 AWDP 配置，或接口暂不可用。')}
        title="无法打开 AWDP 管理"
      />
    )
  const snapshot = controller.snapshot
  const phase = phaseMeta(snapshot.status.status)
  const running = awdpPhase(snapshot.status.status) === 'attack' || awdpPhase(snapshot.status.status) === 'patch'
  const readyToStart = snapshot.services.length > 0 && metrics.completeServices === snapshot.services.length
  const executeLifecycle = async () => {
    if (!lifecycleAction) return false
    const success = await controller.setRunning(lifecycleAction === 'start')
    if (success) setLifecycleAction(null)
    return success
  }

  return (
    <div className={styles.page}>
      <AdminPageHeader
        actions={
          <>
            <ActionButton
              disabled={controller.operation !== null || running || !readyToStart}
              icon={<Play size={16} />}
              onClick={() => setLifecycleAction('start')}
              tone="primary"
              type="button"
            >
              开始 AWDP
            </ActionButton>
            <ActionButton
              disabled={controller.operation !== null || !running}
              icon={<Square size={15} />}
              onClick={() => setLifecycleAction('stop')}
              tone="danger"
              type="button"
            >
              停止当前轮次
            </ActionButton>
            <ActionButton
              disabled={controller.refreshing}
              icon={<RefreshCw size={16} />}
              onClick={controller.refresh}
              type="button"
            >
              刷新
            </ActionButton>
          </>
        }
        description="配置服务、控制轮次并检查每支队伍的实例、攻击和修补结果。"
        eyebrow="AWDP CONTROL"
        title="AWDP 管理"
      />
      <MetricStrip>
        <MetricItem
          detail={`第 ${snapshot.status.currentRound} 轮`}
          label="当前阶段"
          tone={phase.tone}
          value={phase.label}
        />
        <MetricItem
          detail={`${metrics.completeServices} 个含 Checker 与 Exp`}
          label="服务配置"
          tone={
            metrics.completeServices === snapshot.services.length && snapshot.services.length ? 'success' : 'warning'
          }
          value={`${metrics.completeServices}/${snapshot.services.length}`}
        />
        <MetricItem
          detail="运行 / 全部"
          label="队伍实例"
          tone={metrics.running === metrics.instances && metrics.instances ? 'success' : 'neutral'}
          value={`${metrics.running}/${metrics.instances}`}
        />
        <MetricItem
          detail="漏洞验证成功"
          label="有效补丁"
          tone={metrics.successfulPatches ? 'success' : 'neutral'}
          value={metrics.successfulPatches}
        />
      </MetricStrip>
      <div className={styles.liveRow}>
        <StatusBadge
          tone={
            controller.monitorState === 'connected'
              ? 'success'
              : controller.monitorState === 'offline'
                ? 'danger'
                : 'warning'
          }
        >
          {controller.monitorState === 'connected'
            ? 'Monitor Hub 已连接'
            : controller.monitorState === 'offline'
              ? 'Monitor Hub 已断开'
              : 'Monitor Hub 连接中'}
        </StatusBadge>
        <RefreshIndicator active={controller.refreshing} label="30 秒快照校准" />
      </div>
      {controller.feedback ? (
        <InlineFeedback tone={controller.feedback.tone}>{controller.feedback.message}</InlineFeedback>
      ) : null}
      {controller.error ? (
        <InlineFeedback tone="danger">
          {errorMessage(controller.error, 'AWDP 状态刷新失败，当前显示最后一次有效快照。')}
        </InlineFeedback>
      ) : null}
      {!running && !readyToStart ? (
        <InlineFeedback>开始 AWDP 前至少需要一个服务，并为每个服务配置 Checker 与 Exp 脚本。</InlineFeedback>
      ) : null}
      <nav aria-label="AWDP 管理视图" className={styles.tabs}>
        {tabs.map((item) => (
          <button
            className={tab === item.id ? styles.tabActive : styles.tab}
            key={item.id}
            onClick={() => setTab(item.id)}
            type="button"
          >
            {item.label}
          </button>
        ))}
      </nav>
      <section className={styles.workspace}>
        {tab === 'services' ? (
          <AwdpServicePanel
            images={controller.images}
            onDelete={controller.deleteService}
            onSave={controller.saveService}
            operation={controller.operation}
            services={snapshot.services}
          />
        ) : null}
        {tab === 'runtime' ? (
          <AwdpRuntimePanel
            instances={snapshot.instances}
            onAction={controller.runInstanceAction}
            operation={controller.operation}
          />
        ) : null}
        {tab === 'patches' ? <AwdpPatchLogPanel patches={snapshot.patches} /> : null}
        {tab === 'attacks' ? <AwdpAttackLogPanel logs={snapshot.attackLogs} /> : null}
        {tab === 'scoreboard' ? <AwdpScoreboardPanel scoreboard={snapshot.scoreboard} /> : null}
      </section>
      <VNextConfirmDialog
        confirmLabel={lifecycleAction === 'start' ? '确认开始' : '确认停止'}
        confirmationText={lifecycleAction === 'start' ? game.title : undefined}
        description={
          lifecycleAction === 'start'
            ? '后端会为已通过报名的队伍创建所有服务实例并开始新轮次。'
            : '停止会结束当前 AWDP 状态；保留或清理实例以服务器实际语义为准。'
        }
        message={
          lifecycleAction === 'start'
            ? `${snapshot.services.length} 个服务将进入部署。请确认 Checker、Exp、镜像和节点容量均已验证。`
            : '停止后当前轮次不会因为容器仍存在而自动恢复。'
        }
        onClose={() => setLifecycleAction(null)}
        onConfirm={executeLifecycle}
        open={Boolean(lifecycleAction)}
        title={lifecycleAction === 'start' ? '开始 AWDP？' : '停止当前 AWDP？'}
      />
    </div>
  )
}

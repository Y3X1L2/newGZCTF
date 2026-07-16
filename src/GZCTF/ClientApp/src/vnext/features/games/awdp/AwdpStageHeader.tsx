import { Radio, RefreshCw, ShieldCheck, Swords, TimerReset } from 'lucide-react'
import { AwdpScore, AwdpStatus, phaseMeta, remainingPhaseLabel } from '../../awdp/awdpDomain'
import { AwdpMonitorState } from '../../awdp/useAwdpMonitor'
import styles from './AwdpWorkspace.module.css'

export function AwdpStageHeader({
  status,
  myScore,
  running,
  total,
  defended,
  serviceCount,
  monitorState,
  refreshing,
  now,
  onRefresh,
}: {
  status: AwdpStatus
  myScore?: AwdpScore
  running: number
  total: number
  defended: number
  serviceCount: number
  monitorState: AwdpMonitorState
  refreshing: boolean
  now: number
  onRefresh: () => void
}) {
  const phase = phaseMeta(status.status)
  const connectionLabel =
    monitorState === 'connected' ? '实时连接' : monitorState === 'offline' ? '连接中断' : '正在连接'
  return (
    <>
      <header className={styles.stageHeader} data-phase={phase.tone}>
        <div>
          <span>ROUND {String(status.currentRound).padStart(2, '0')}</span>
          <h1>{phase.label}</h1>
          <p>攻击与修补状态以服务器轮次快照为准，连接恢复后会自动校准。</p>
        </div>
        <div className={styles.stageLive}>
          <span className={styles.connection} data-state={monitorState}>
            <Radio size={15} />
            {connectionLabel}
          </span>
          <strong>{remainingPhaseLabel(status, now)}</strong>
          <small>当前阶段剩余</small>
          <button aria-label="刷新 AWDP 状态" disabled={refreshing} onClick={onRefresh} type="button">
            <RefreshCw size={17} />
          </button>
        </div>
      </header>
      <section aria-label="AWDP 状态概览" className={styles.metrics}>
        <div>
          <Swords size={17} />
          <span>本队 AWDP</span>
          <strong>{myScore?.awdpScore ?? '—'}</strong>
          <small>{myScore ? `排名 #${myScore.rank}` : '等待参赛信息'}</small>
        </div>
        <div>
          <Radio size={17} />
          <span>运行实例</span>
          <strong>
            {running}/{total}
          </strong>
          <small>所有可攻击服务</small>
        </div>
        <div>
          <ShieldCheck size={17} />
          <span>已防守服务</span>
          <strong>
            {defended}/{serviceCount}
          </strong>
          <small>仅代表漏洞验证</small>
        </div>
        <div>
          <TimerReset size={17} />
          <span>阶段时长</span>
          <strong>
            {status.attackPhaseMinutes}+{status.patchPhaseMinutes}
          </strong>
          <small>攻击 + 修补（分钟）</small>
        </div>
      </section>
    </>
  )
}

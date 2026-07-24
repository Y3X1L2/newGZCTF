import { useMemo } from 'react'
import {
  AwdpAttackLog,
  AwdpPatchSubmission,
  AwdpScore,
  checkerMeta,
  formatAwdpTime,
  patchMeta,
} from '../../awdp/awdpDomain'
import { AdminDataColumn, DataTable, StatusBadge } from '../shared/AdminWorkbench'
import styles from './AdminAwdp.module.css'

export function AwdpPatchLogPanel({ patches }: { patches: AwdpPatchSubmission[] }) {
  const columns = useMemo<AdminDataColumn<AwdpPatchSubmission>[]>(
    () => [
      {
        id: 'identity',
        header: '战队 / 服务',
        width: 'wide',
        render: (item) => (
          <div className={styles.identity}>
            <strong>{item.teamName}</strong>
            <small>
              {item.serviceName} · 第 {item.roundNumber} 轮
            </small>
          </div>
        ),
      },
      {
        id: 'time',
        header: '提交时间',
        width: 'medium',
        visibility: 'desktop',
        render: (item) => <span className={styles.mono}>{formatAwdpTime(item.submittedAt)}</span>,
      },
      {
        id: 'checker',
        header: 'Checker',
        width: 'medium',
        render: (item) => {
          const meta = checkerMeta(item.checkerResult)
          return <StatusBadge tone={meta.tone}>{meta.label}</StatusBadge>
        },
      },
      {
        id: 'exp',
        header: 'Exp',
        width: 'medium',
        render: (item) => {
          const meta = patchMeta(item.expResult)
          return <StatusBadge tone={meta.tone}>{meta.label}</StatusBadge>
        },
      },
      {
        id: 'final',
        header: '最终结果',
        width: 'medium',
        render: (item) => {
          const meta = patchMeta(item.finalStatus)
          return <StatusBadge tone={meta.tone}>{meta.label}</StatusBadge>
        },
      },
      { id: 'message', header: '验证消息', width: 'wide', visibility: 'wide', render: (item) => item.message || '—' },
    ],
    []
  )
  return (
    <DataTable
      caption="AWDP 补丁记录"
      columns={columns}
      emptyDescription="队伍上传补丁后将显示 Checker、Exp 和最终结果。"
      emptyTitle="暂无补丁提交"
      rowKey={(item) => item.id}
      rows={patches}
    />
  )
}

export function AwdpAttackLogPanel({ logs }: { logs: AwdpAttackLog[] }) {
  const columns = useMemo<AdminDataColumn<AwdpAttackLog>[]>(
    () => [
      {
        id: 'time',
        header: '时间',
        width: 'medium',
        visibility: 'desktop',
        render: (item) => <span className={styles.mono}>{formatAwdpTime(item.time)}</span>,
      },
      { id: 'attacker', header: '攻击方', width: 'wide', render: (item) => <strong>{item.attackerTeam}</strong> },
      { id: 'victim', header: '目标方', width: 'wide', render: (item) => item.victimTeam },
      { id: 'service', header: '服务', width: 'wide', render: (item) => item.serviceName },
      {
        id: 'points',
        header: '得分',
        width: 'compact',
        align: 'right',
        render: (item) => <strong className={styles.score}>+{item.points}</strong>,
      },
    ],
    []
  )
  return (
    <DataTable
      caption="AWDP 攻击日志"
      columns={columns}
      emptyDescription="正确攻击 Flag 产生得分后将记录在这里。"
      emptyTitle="暂无攻击记录"
      rowKey={(item) => item.key}
      rows={logs}
    />
  )
}

export function AwdpScoreboardPanel({ scoreboard }: { scoreboard: AwdpScore[] }) {
  const columns = useMemo<AdminDataColumn<AwdpScore>[]>(
    () => [
      {
        id: 'rank',
        header: '排名',
        width: 'compact',
        render: (item) => <strong className={styles.mono}>#{item.rank}</strong>,
      },
      { id: 'team', header: '战队', width: 'wide', render: (item) => <strong>{item.teamName}</strong> },
      { id: 'attack', header: '攻击', width: 'medium', render: (item) => item.attackScore },
      { id: 'sla', header: 'SLA', width: 'medium', render: (item) => item.slaScore },
      { id: 'patch', header: '修补', width: 'medium', render: (item) => item.patchScore },
      {
        id: 'penalty',
        header: '扣分',
        width: 'medium',
        render: (item) => (item.penaltyScore ? `-${item.penaltyScore}` : 0),
      },
      {
        id: 'awdp',
        header: 'AWDP 总分',
        width: 'medium',
        align: 'right',
        render: (item) => <strong className={styles.score}>{item.awdpScore}</strong>,
      },
    ],
    []
  )
  return (
    <DataTable
      caption="AWDP 排行榜"
      columns={columns}
      emptyDescription="AWDP 开始并完成结算后会生成独立排名。"
      emptyTitle="AWDP 榜单尚未生成"
      rowKey={(item) => item.teamId}
      rows={scoreboard}
    />
  )
}

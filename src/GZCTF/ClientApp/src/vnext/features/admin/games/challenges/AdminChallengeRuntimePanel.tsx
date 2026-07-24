import { ExternalLink, Play, RefreshCw, Square } from 'lucide-react'
import { ChallengeEditDetailModel, ContainerStatus, EnvironmentType } from '@Api'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import { externalEntryHref } from '../../../../shared/urls'
import { AdminEditorSection, StatusBadge } from '../../shared/AdminWorkbench'
import { challengeEnvironmentLabel } from '../gamePresentation'
import styles from './AdminChallengeEditorPage.module.css'

export type RuntimeOperation = {
  kind: 'create' | 'destroy'
  startedAt: number
  ticketId?: string
}

function runtimeStatus(status?: ContainerStatus) {
  if (status === ContainerStatus.Running) return { label: '运行中', tone: 'success' as const }
  if (status === ContainerStatus.Pending) return { label: '部署中', tone: 'info' as const }
  return { label: '已销毁', tone: 'neutral' as const }
}

function runtimeTime(value?: number) {
  if (!value) return '—'
  return new Intl.DateTimeFormat('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  }).format(value)
}

interface AdminChallengeRuntimePanelProps {
  challenge: ChallengeEditDetailModel
  dirty: boolean
  operation: RuntimeOperation | null
  refreshing: boolean
  containerChallenge: boolean
  draftEnvironment: EnvironmentType
  onCreate: () => Promise<void>
  onDestroy: () => Promise<void>
  onRefresh: () => Promise<unknown>
}

export function AdminChallengeRuntimePanel({
  challenge,
  dirty,
  operation,
  refreshing,
  containerChallenge,
  draftEnvironment,
  onCreate,
  onDestroy,
  onRefresh,
}: AdminChallengeRuntimePanelProps) {
  const testContainer = challenge.testContainer
  const status = runtimeStatus(testContainer?.status)
  const entryHref = externalEntryHref(testContainer?.entry)

  return (
    <AdminEditorSection
      description="管理员测试实例只支持 Docker；创建和销毁都进入部署队列，并每 3 秒回读题目状态。"
      title="测试实例"
    >
      {draftEnvironment === EnvironmentType.Docker && containerChallenge ? (
        <div className={styles.runtimePanel}>
          <div className={styles.runtimeHeader}>
            <div>
              <StatusBadge pulse={Boolean(operation)} tone={operation ? 'info' : status.tone}>
                {operation
                  ? operation.kind === 'create'
                    ? '创建排队中'
                    : '销毁排队中'
                  : testContainer
                    ? status.label
                    : '未创建'}
              </StatusBadge>
              <span>
                {challengeEnvironmentLabel(challenge.environment)} · {challenge.containerImage}
              </span>
            </div>
            <div className={styles.panelButtons}>
              <ActionButton
                disabled={Boolean(operation) || Boolean(testContainer) || dirty}
                icon={<Play size={16} />}
                onClick={() => void onCreate()}
                tone="primary"
                type="button"
              >
                创建测试实例
              </ActionButton>
              <ActionButton
                disabled={Boolean(operation) || !testContainer}
                icon={<Square size={16} />}
                onClick={() => void onDestroy()}
                tone="danger"
                type="button"
              >
                销毁实例
              </ActionButton>
              <ActionButton
                disabled={refreshing}
                icon={<RefreshCw size={16} />}
                onClick={() => void onRefresh()}
                type="button"
              >
                刷新状态
              </ActionButton>
            </div>
          </div>
          {dirty ? <InlineFeedback tone="danger">请先保存运行环境更改，再创建测试实例。</InlineFeedback> : null}
          {testContainer ? (
            <div className={styles.runtimeFacts}>
              <span>
                <small>启动时间</small>
                <strong>{runtimeTime(testContainer.startedAt)}</strong>
              </span>
              <span>
                <small>预计停止</small>
                <strong>{runtimeTime(testContainer.expectStopAt)}</strong>
              </span>
              <span>
                <small>访问入口</small>
                {entryHref ? (
                  <a href={entryHref} rel="noreferrer noopener" target="_blank">
                    打开实例 <ExternalLink size={14} />
                  </a>
                ) : (
                  <strong>等待分配</strong>
                )}
              </span>
            </div>
          ) : null}
          {operation?.ticketId ? <code className={styles.ticket}>QUEUE {operation.ticketId}</code> : null}
        </div>
      ) : draftEnvironment === EnvironmentType.WindowsVM && containerChallenge ? (
        <div className={styles.passiveEnvironment}>
          后端尚未提供 Windows VM 管理员测试实例接口。请保存配置后，通过专用测试比赛验证选手实例流程。
        </div>
      ) : (
        <div className={styles.passiveEnvironment}>当前题目没有可测试的 Docker 环境。</div>
      )}
    </AdminEditorSection>
  )
}

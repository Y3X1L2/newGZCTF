import {
  Check,
  Clock3,
  Copy,
  Download,
  ExternalLink,
  KeyRound,
  Monitor,
  RefreshCw,
  Server,
  Trash2,
  User,
} from 'lucide-react'
import { useEffect, useId, useMemo, useState } from 'react'
import { ContainerEntryStatus } from '@Api'
import { ActionButton, InlineFeedback } from '../../shared/Interaction'
import { StatusPill } from '../../shared/Primitives'
import { externalEntryHref } from '../../shared/urls'
import styles from './InstanceControl.module.css'
import { downloadRdpFile } from './rdp'
import { RuntimeInstanceController, RuntimeInstancePhase } from './types'

const phaseLabels: Record<RuntimeInstancePhase, string> = {
  idle: '未创建',
  queued: '调度排队',
  provisioning: '正在准备',
  running: '运行中',
  extending: '正在延期',
  stopping: '正在销毁',
  failed: '状态异常',
}

function formatRemaining(target: number | null, now: number) {
  if (!target) return '--:--:--'
  const total = Math.max(0, Math.floor((target - now) / 1000))
  const hours = Math.floor(total / 3600)
  const minutes = Math.floor((total % 3600) / 60)
  const seconds = total % 60
  return [hours, minutes, seconds].map((value) => String(value).padStart(2, '0')).join(':')
}

export function InstanceControl({ controller }: { controller: RuntimeInstanceController }) {
  const titleId = useId()
  const [now, setNow] = useState(Date.now())
  const [copiedField, setCopiedField] = useState<string | null>(null)
  const running = controller.phase === 'running' || controller.phase === 'extending'

  useEffect(() => {
    if (!running || !controller.closeTime) return undefined
    const timer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [controller.closeTime, running])

  const remaining = useMemo(() => formatRemaining(controller.closeTime, now), [controller.closeTime, now])
  const queue = controller.vmStatus?.queue
  const stageMessage = controller.vmStatus?.stageMessage
  const entryHref = controller.kind === 'docker' ? externalEntryHref(controller.entry) : null
  const vmStatus = controller.kind === 'windows' ? controller.vmStatus : null
  const rdpAddress = vmStatus?.rdpHost && vmStatus.rdpPort ? `${vmStatus.rdpHost}:${vmStatus.rdpPort}` : null
  const entryPublicationFailed = controller.entryStatus === ContainerEntryStatus.Error && controller.closeTime !== null

  if (controller.kind === 'none') return null

  const copyValue = async (field: string, value?: string | null) => {
    if (!value) return
    await navigator.clipboard.writeText(value)
    setCopiedField(field)
    window.setTimeout(() => setCopiedField((current) => (current === field ? null : current)), 1600)
  }

  return (
    <section aria-labelledby={titleId} className={styles.instanceSection}>
      <header className={styles.sectionHeader}>
        <div>
          <span>RUNTIME</span>
          <h2 id={titleId}>{controller.kind === 'windows' ? 'Windows 靶机' : 'Docker 实例'}</h2>
        </div>
        <StatusPill tone={running ? 'success' : controller.phase === 'failed' ? 'warning' : 'neutral'}>
          {phaseLabels[controller.phase]}
        </StatusPill>
      </header>

      {controller.error ? <InlineFeedback tone="danger">{controller.error}</InlineFeedback> : null}

      {controller.phase === 'idle' || controller.phase === 'failed' ? (
        <div className={styles.idlePanel}>
          <span className={styles.runtimeIcon}>
            {controller.kind === 'windows' ? <Monitor size={22} /> : <Server size={22} />}
          </span>
          <div>
            <strong>{controller.phase === 'failed' ? '实例未能就绪' : '当前没有运行实例'}</strong>
            <p>
              {controller.kind === 'windows'
                ? '创建后将经历镜像准备、虚拟机创建、启动和网络就绪阶段。'
                : '创建后将显示由服务端返回的访问入口和剩余时间。'}
            </p>
          </div>
          {entryPublicationFailed ? (
            <div className={styles.runtimeActions}>
              <ActionButton icon={<RefreshCw size={16} />} onClick={() => void controller.refresh()} type="button">
                刷新状态
              </ActionButton>
              <ActionButton
                icon={<Trash2 size={16} />}
                onClick={() => void controller.destroy()}
                tone="danger"
                type="button"
              >
                销毁实例
              </ActionButton>
            </div>
          ) : (
            <ActionButton
              icon={<RefreshCw size={16} />}
              onClick={() => void controller.create()}
              tone="primary"
              type="button"
            >
              {controller.phase === 'failed' ? '重新创建' : '创建实例'}
            </ActionButton>
          )}
        </div>
      ) : controller.phase === 'queued' || controller.phase === 'provisioning' ? (
        <div className={styles.progressPanel} role="status">
          <span className={styles.progressMark}>
            <i />
            <i />
            <i />
          </span>
          <div>
            <strong>{stageMessage || (controller.phase === 'queued' ? '等待调度资源' : '正在准备运行环境')}</strong>
            <p>
              {queue?.queuePosition ? `队列位置 ${queue.queuePosition}` : '页面会自动读取最新状态，无需重复点击创建。'}
              {queue?.targetNodeName ? ` · 目标节点 ${queue.targetNodeName}` : ''}
            </p>
          </div>
          <ActionButton icon={<RefreshCw size={16} />} onClick={() => void controller.refresh()} type="button">
            刷新状态
          </ActionButton>
        </div>
      ) : (
        <div className={styles.runningPanel}>
          <div className={styles.runtimeMeta}>
            <div>
              <Clock3 size={17} />
              <span>剩余时间</span>
              <strong>{controller.kind === 'docker' ? remaining : '由平台管理'}</strong>
            </div>
            {controller.vmStatus?.stageMessage ? (
              <div>
                <Monitor size={17} />
                <span>部署阶段</span>
                <strong>{controller.vmStatus.stageMessage}</strong>
              </div>
            ) : null}
          </div>

          {controller.kind === 'windows' && rdpAddress && vmStatus?.rdpUsername && vmStatus.rdpPassword ? (
            <div className={styles.rdpPanel}>
              <div className={styles.rdpCredentials}>
                {[
                  { field: 'address', label: '连接地址', value: rdpAddress, icon: <Monitor size={16} /> },
                  { field: 'username', label: '用户名', value: vmStatus.rdpUsername, icon: <User size={16} /> },
                  { field: 'password', label: '密码', value: vmStatus.rdpPassword, icon: <KeyRound size={16} /> },
                ].map((item) => (
                  <div className={styles.rdpCredential} key={item.field}>
                    {item.icon}
                    <span>{item.label}</span>
                    <code>{item.value}</code>
                    <button
                      aria-label={`复制${item.label}`}
                      onClick={() => void copyValue(item.field, item.value)}
                      title={`复制${item.label}`}
                      type="button"
                    >
                      {copiedField === item.field ? <Check size={17} /> : <Copy size={17} />}
                    </button>
                  </div>
                ))}
              </div>
              <div className={styles.rdpActions}>
                {vmStatus.rdpUrl ? (
                  <a href={vmStatus.rdpUrl} rel="noreferrer noopener" target="_blank">
                    <ExternalLink size={16} />
                    <span>浏览器远程桌面</span>
                  </a>
                ) : null}
                <ActionButton
                  icon={<Download size={16} />}
                  onClick={() => downloadRdpFile(vmStatus.rdpHost!, vmStatus.rdpPort!, vmStatus.rdpUsername!)}
                  type="button"
                >
                  下载 RDP
                </ActionButton>
              </div>
            </div>
          ) : controller.entry ? (
            <div className={styles.entryRow}>
              <code>{controller.entry}</code>
              <button
                aria-label="复制实例入口"
                onClick={() => void copyValue('entry', controller.entry)}
                title="复制入口"
                type="button"
              >
                {copiedField === 'entry' ? <Check size={17} /> : <Copy size={17} />}
              </button>
              {entryHref ? (
                <a
                  aria-label="打开实例入口"
                  href={entryHref}
                  rel="noreferrer noopener"
                  target="_blank"
                  title="在新窗口打开"
                >
                  <ExternalLink size={17} />
                </a>
              ) : null}
            </div>
          ) : (
            <InlineFeedback>实例已运行，正在等待服务端确认访问入口。</InlineFeedback>
          )}

          <div className={styles.runtimeActions}>
            {controller.kind === 'docker' ? (
              <ActionButton
                disabled={controller.phase !== 'running'}
                icon={<Clock3 size={16} />}
                onClick={() => void controller.extend()}
                type="button"
              >
                延长时间
              </ActionButton>
            ) : null}
            <ActionButton
              disabled={controller.phase === 'stopping'}
              icon={<Trash2 size={16} />}
              onClick={() => void controller.destroy()}
              tone="danger"
              type="button"
            >
              销毁实例
            </ActionButton>
          </div>
        </div>
      )}
    </section>
  )
}

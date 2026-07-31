import { Check, Circle, LoaderCircle } from 'lucide-react'
import type { TeamLabPlayerWorkspaceProjection } from './api'
import styles from './TeamLabWorkspacePage.module.css'

const stages = [
  { keys: ['pending', 'queued'], label: '排队' },
  { keys: ['planning', 'reserve', 'scheduled'], label: '资源预留' },
  { keys: ['image', 'image-distribution', 'image-ready'], label: '镜像准备' },
  { keys: ['network', 'networking'], label: '网络创建' },
  { keys: ['deploying', 'asset', 'vm-creating', 'container-creating'], label: '资产创建' },
  { keys: ['probing', 'health'], label: '健康检查' },
  { keys: ['ready', 'running', 'access'], label: '环境就绪' },
] as const

export function PlayerRuntimeStages({ workspace }: { workspace: TeamLabPlayerWorkspaceProjection }) {
  const current = stages.findIndex((stage) => stage.keys.some((key) => workspace.stage.toLowerCase().includes(key)))
  const ready = workspace.status === 'running'
  return (
    <ol aria-label="环境部署阶段" className={styles.stageList}>
      {stages.map((stage, index) => {
        const completed = ready
        const active = !ready && index === current
        return (
          <li data-active={active || undefined} data-completed={completed || undefined} key={stage.label}>
            {completed ? <Check size={15} /> : active ? <LoaderCircle size={15} /> : <Circle size={15} />}
            <span>{stage.label}</span>
          </li>
        )
      })}
    </ol>
  )
}

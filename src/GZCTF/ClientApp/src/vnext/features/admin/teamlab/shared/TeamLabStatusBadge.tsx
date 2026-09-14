import {
  AlertTriangle,
  CheckCircle2,
  Circle,
  CirclePause,
  CircleX,
  Clock3,
  DoorOpen,
  LoaderCircle,
  Lock,
  Power,
  Trash2,
  type LucideIcon,
} from 'lucide-react'
import type { TeamLabAdminSceneSummary, TeamLabRuntimeStatus } from '../api'
import { StatusBadge, type AdminStatusTone } from '../../shared/AdminWorkbench'

export type TeamLabSceneLifecycle = 'draft' | 'changed' | 'published' | 'invalid' | 'trial-running'

interface StatusMeta {
  icon: LucideIcon
  label: string
  tone: AdminStatusTone
  pulse?: boolean
}

const sceneMeta: Record<TeamLabSceneLifecycle, StatusMeta> = {
  draft: { icon: Circle, label: '草稿', tone: 'neutral' },
  changed: { icon: AlertTriangle, label: '待发布', tone: 'warning' },
  published: { icon: CheckCircle2, label: '已发布', tone: 'success' },
  invalid: { icon: CircleX, label: '校验未通过', tone: 'danger' },
  'trial-running': { icon: LoaderCircle, label: '试运行中', tone: 'info' },
}

const runtimeMeta: Record<TeamLabRuntimeStatus, StatusMeta> = {
  pending: { icon: Clock3, label: '等待中', tone: 'neutral', pulse: true },
  planning: { icon: LoaderCircle, label: '规划中', tone: 'info', pulse: true },
  scheduled: { icon: Clock3, label: '已排队', tone: 'info', pulse: true },
  deploying: { icon: LoaderCircle, label: '部署中', tone: 'info', pulse: true },
  probing: { icon: LoaderCircle, label: '探测中', tone: 'info', pulse: true },
  running: { icon: CheckCircle2, label: '环境运行中', tone: 'success' },
  failed: { icon: CircleX, label: '失败', tone: 'danger' },
  'cleanup-pending': { icon: Clock3, label: '待清理', tone: 'warning' },
  paused: { icon: CirclePause, label: '已暂停', tone: 'neutral' },
  destroying: { icon: LoaderCircle, label: '销毁中', tone: 'warning', pulse: true },
  destroyed: { icon: Trash2, label: '已销毁', tone: 'neutral' },
  stopped: { icon: Power, label: '已停止', tone: 'neutral' },
}

export function teamLabSceneLifecycle(scene: TeamLabAdminSceneSummary): TeamLabSceneLifecycle {
  if (scene.latestTrialRuntime && isTrialInProgress(scene.latestTrialRuntime.status)) return 'trial-running'
  if (scene.validation && !scene.validation.valid) return 'invalid'
  if (!scene.latestRelease) return 'draft'
  return scene.latestRelease.sourceRevision === scene.revision ? 'published' : 'changed'
}

export function TeamLabSceneStatusBadge({ scene }: { scene: TeamLabAdminSceneSummary }) {
  const lifecycle = teamLabSceneLifecycle(scene)
  const meta = sceneMeta[lifecycle]
  const Icon = meta.icon
  const pulse = lifecycle === 'trial-running' && Boolean(runtimeMeta[scene.latestTrialRuntime!.status].pulse)
  return <StatusBadge icon={<Icon />} pulse={pulse} tone={meta.tone}>{meta.label}</StatusBadge>
}

export function TeamLabRuntimeStatusBadge({ status }: { status: TeamLabRuntimeStatus }) {
  const meta = runtimeMeta[status]
  const Icon = meta.icon
  return <StatusBadge icon={<Icon />} pulse={meta.pulse} tone={meta.tone}>{meta.label}</StatusBadge>
}

export function TeamLabAccessStatusBadge({ open }: { open: boolean }) {
  return <StatusBadge icon={open ? <DoorOpen /> : <Lock />} tone={open ? 'success' : 'neutral'}>{open ? '已开放' : '未开放'}</StatusBadge>
}

export function TeamLabReadinessStatusBadge({ ready }: { ready: boolean }) {
  return <StatusBadge icon={ready ? <CheckCircle2 /> : <AlertTriangle />} tone={ready ? 'success' : 'warning'}>
    {ready ? '可创建试运行' : '存在阻断项'}
  </StatusBadge>
}

function isTrialInProgress(status: TeamLabRuntimeStatus) {
  return status === 'pending' || status === 'planning' || status === 'scheduled' || status === 'deploying' ||
    status === 'probing'
}

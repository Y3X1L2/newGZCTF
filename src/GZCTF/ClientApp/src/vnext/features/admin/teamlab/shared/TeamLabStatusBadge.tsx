import type { TeamLabAdminSceneSummary, TeamLabRuntimeStatus } from '../api'
import { StatusBadge, type AdminStatusTone } from '../../shared/AdminWorkbench'

export type TeamLabSceneLifecycle = 'draft' | 'changed' | 'published' | 'invalid'

const sceneMeta: Record<TeamLabSceneLifecycle, { label: string; tone: AdminStatusTone }> = {
  draft: { label: '草稿', tone: 'neutral' },
  changed: { label: '待发布', tone: 'warning' },
  published: { label: '已发布', tone: 'success' },
  invalid: { label: '校验未通过', tone: 'danger' },
}

const runtimeMeta: Record<TeamLabRuntimeStatus, { label: string; tone: AdminStatusTone; pulse?: boolean }> = {
  pending: { label: '等待中', tone: 'neutral', pulse: true },
  planning: { label: '规划中', tone: 'info', pulse: true },
  scheduled: { label: '已排队', tone: 'info', pulse: true },
  deploying: { label: '部署中', tone: 'info', pulse: true },
  probing: { label: '探测中', tone: 'info', pulse: true },
  running: { label: '运行中', tone: 'success' },
  failed: { label: '失败', tone: 'danger' },
  'cleanup-pending': { label: '待清理', tone: 'warning', pulse: true },
  paused: { label: '已暂停', tone: 'neutral' },
  destroying: { label: '清理中', tone: 'warning', pulse: true },
  destroyed: { label: '已销毁', tone: 'neutral' },
}

export function teamLabSceneLifecycle(scene: TeamLabAdminSceneSummary): TeamLabSceneLifecycle {
  if (scene.validation && !scene.validation.valid) return 'invalid'
  if (!scene.latestRelease) return 'draft'
  return scene.latestRelease.sourceRevision === scene.revision ? 'published' : 'changed'
}

export function TeamLabSceneStatusBadge({ scene }: { scene: TeamLabAdminSceneSummary }) {
  const meta = sceneMeta[teamLabSceneLifecycle(scene)]
  return <StatusBadge tone={meta.tone}>{meta.label}</StatusBadge>
}

export function TeamLabRuntimeStatusBadge({ status }: { status: TeamLabRuntimeStatus }) {
  const meta = runtimeMeta[status]
  return <StatusBadge pulse={meta.pulse} tone={meta.tone}>{meta.label}</StatusBadge>
}

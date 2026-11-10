import { RefreshCw } from 'lucide-react'
import { lazy, Suspense } from 'react'
import { ActionButton } from '../../shared/Interaction'
import { DataState } from '../../shared/Primitives'
import { errorMessage } from '../../shared/errors'
import styles from './NetlabWorkspace.module.css'
import type { WorkspaceTarget } from './api/netlabApi'
import { useNetlabWorkspace } from './useNetlabWorkspace'

const Workspace = lazy(() => import('./workspace/Workspace').then((module) => ({ default: module.Workspace })))

export function NetlabWorkspace({ target, title = '组网环境' }: { target: WorkspaceTarget; title?: string }) {
  const state = useNetlabWorkspace(target)
  if (state.isLoading) return <DataState loading title="正在连接环境" />
  if (state.error || !state.data?.available)
    return (
      <section className={styles.unavailable}>
        <DataState title="环境暂不可用" description={state.data?.error || errorMessage(state.error, '连接失败')} />
        <ActionButton icon={<RefreshCw size={16} />} onClick={state.reload}>
          重新连接
        </ActionButton>
      </section>
    )
  return (
    <section className={styles.workspace} aria-label={title}>
      <Suspense fallback={<DataState loading title="正在加载环境" />}>
        <Workspace key={target.gameId ?? 'admin'} gameId={target.gameId} environmentId={state.data.environmentId} />
      </Suspense>
    </section>
  )
}

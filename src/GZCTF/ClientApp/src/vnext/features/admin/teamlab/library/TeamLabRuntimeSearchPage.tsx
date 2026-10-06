import { useVNextPageTitle } from '../../../../shared/useVNextPageTitle'
import { AdminPageHeader } from '../../shared/AdminWorkbench'
import { RemoteSessionsPanel } from '../runtimes/RemoteSessionsPanel'
import { TeamLabWorkspaceNav } from '../shared/TeamLabWorkspaceNav'
import { RuntimeSearchPanel } from './RuntimeSearchPanel'
import styles from './TeamLabLibraryPage.module.css'
import { Link, useSearchParams } from 'react-router'

export function TeamLabRuntimeSearchPage() {
  const [params] = useSearchParams()
  const sessions = params.get('view') === 'sessions'
  useVNextPageTitle(sessions ? '远程会话管理' : '运行环境')

  return <div className={styles.page}>
    <AdminPageHeader eyebrow="TEAMLAB" title={sessions ? '远程会话管理' : '运行环境'} />
    <TeamLabWorkspaceNav active="runtimes" />
    {sessions ? <>
      <p className={styles.maintenanceNote}>远程会话属于环境运维记录。返回环境列表可进入具体环境。</p>
      <Link to="/admin/teamlab/runtimes">返回运行环境</Link>
      <RemoteSessionsPanel />
    </> : <RuntimeSearchPanel />}
  </div>
}

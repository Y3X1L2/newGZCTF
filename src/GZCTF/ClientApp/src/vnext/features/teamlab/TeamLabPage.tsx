import { useParams, useSearchParams } from 'react-router'
import { useState } from 'react'
import { Plug, Server } from 'lucide-react'
import useSWR, { useSWRConfig } from 'swr'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { ActionButton } from '../../shared/Interaction'
import { ActionMenu } from '../../shared/ActionMenu'
import { useCurrentAccount } from '../account/useCurrentAccount'
import { NetlabWorkspace } from './NetlabWorkspace'
import { NetlabConnectionDialog } from './NetlabConnectionDialog'
import { netlabApi, type WorkspaceTarget } from './api/netlabApi'
import styles from './NetlabWorkspace.module.css'

export function TeamLabPage() {
  const { isAdmin } = useCurrentAccount()
  const connection = useSWR(isAdmin ? 'netlab:connection' : null, netlabApi.connection, { revalidateOnFocus: false })
  const [connecting, setConnecting] = useState(false)
  const { mutate } = useSWRConfig()
  const route = useParams()['*'] || ''
  const [search] = useSearchParams()
  const section: WorkspaceTarget['section'] = route === 'templates' ? 'templates' : route.startsWith('resources') ? 'resources' : 'environments'
  const environmentId = search.get('environment') || undefined
  const runtimeId = search.get('runtime') || undefined
  useVNextPageTitle('组网环境')
  return <div className={styles.page}>
    <header className={styles.header}><h1>组网环境</h1>{isAdmin && <ActionMenu label="组网设置" items={[{ label: '底座连接', icon: <Plug size={16} />, onSelect: () => setConnecting(true) }]} />}</header>
    {isAdmin && connection.data?.configured === false ? <section className={styles.unavailable}>
      <Server size={36} /><h2>连接组网底座</h2><ActionButton icon={<Plug size={16} />} tone="primary" onClick={() => setConnecting(true)}>连接底座</ActionButton>
    </section> : <NetlabWorkspace target={{ section: environmentId || runtimeId ? 'workbench' : section, environmentId, runtimeId }} />}
    {connecting && <NetlabConnectionDialog connection={connection.data} onClose={() => setConnecting(false)} onSaved={async () => {
      await connection.mutate()
      await mutate(key => Array.isArray(key) && key[0] === 'netlab:workspace')
    }} />}
  </div>
}

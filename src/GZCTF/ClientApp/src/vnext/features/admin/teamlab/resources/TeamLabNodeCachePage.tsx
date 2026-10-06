import { Link } from 'react-router'
import { useVNextPageTitle } from '../../../../shared/useVNextPageTitle'
import { AdminPageHeader } from '../../shared/AdminWorkbench'
import { NodeCacheTab } from './TeamLabResourcesPage'
import styles from './TeamLabResourcesPage.module.css'

export function TeamLabNodeCachePage() {
  useVNextPageTitle('节点制品缓存')
  return <div className={styles.page}>
    <AdminPageHeader eyebrow="RUNTIME FLEET" title="节点制品缓存" />
    <p><Link to="/admin/nodes">返回节点管理</Link> · <Link to="/admin/images">环境模板</Link></p>
    <NodeCacheTab />
  </div>
}

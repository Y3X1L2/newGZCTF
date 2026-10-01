import { Link } from 'react-router'
import styles from '../library/TeamLabLibraryPage.module.css'

export function TeamLabWorkspaceNav({ active }: { active: 'scenes' | 'runtimes' | 'resources' }) {
  return <nav aria-label="组网工作台" className={styles.views}>
    <Link aria-current={active === 'scenes' ? 'page' : undefined} to="/admin/teamlab">场景</Link>
    <Link aria-current={active === 'runtimes' ? 'page' : undefined} to="/admin/teamlab?view=runtimes">运行环境</Link>
    <Link aria-current={active === 'resources' ? 'page' : undefined} to="/admin/teamlab/resources">资源</Link>
  </nav>
}

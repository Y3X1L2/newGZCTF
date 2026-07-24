import styles from './VNextState.module.css'

export function VNextRouteLoading() {
  return (
    <div className={styles.loading} aria-label="正在加载页面" role="status">
      <span />
      <span />
      <span />
    </div>
  )
}

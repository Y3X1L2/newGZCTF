import { AlertTriangle, RotateCcw } from 'lucide-react'
import { FC } from 'react'
import { FallbackProps } from 'react-error-boundary'
import styles from './VNextState.module.css'

export const VNextErrorFallback: FC<FallbackProps> = ({ error, resetErrorBoundary }) => (
  <main className={styles.screen}>
    <section className={styles.state}>
      <span className={styles.icon}>
        <AlertTriangle aria-hidden="true" size={24} />
      </span>
      <p className={styles.eyebrow}>APPLICATION ERROR</p>
      <h1>页面暂时无法显示</h1>
      <p>{error instanceof Error ? error.message : '发生了未识别的前端错误。'}</p>
      <button className={styles.action} onClick={resetErrorBoundary} type="button">
        <RotateCcw aria-hidden="true" size={17} />
        重新加载
      </button>
    </section>
  </main>
)

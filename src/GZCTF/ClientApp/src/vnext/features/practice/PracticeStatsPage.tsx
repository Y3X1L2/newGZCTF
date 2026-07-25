import { useMemo } from 'react'
import { useExercises } from './api/practiceApi'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { DataState, PageHeading } from '../../shared/Primitives'
import styles from './PracticePage.module.css'

export function PracticeStatsPage() {
  useVNextPageTitle('练习统计')
  const { data: exercises, error } = useExercises()

  const stats = useMemo(() => {
    if (!exercises) return null
    const total = exercises.length
    const byCategory = new Map<string, { total: number; solved: number }>()
    const byDifficulty = new Map<string, { total: number; solved: number }>()

    for (const ex of exercises) {
      const cat = ex.category ?? 'Misc'
      const diff = ex.difficulty ?? 'Baby'

      const catStats = byCategory.get(cat) ?? { total: 0, solved: 0 }
      catStats.total++
      byCategory.set(cat, catStats)

      const diffStats = byDifficulty.get(diff) ?? { total: 0, solved: 0 }
      diffStats.total++
      byDifficulty.set(diff, diffStats)
    }

    const solved = exercises.filter(e => (e.acceptedCount ?? 0) > 0).length
    const accuracy = exercises.length > 0
      ? Math.round((exercises.reduce((a, e) => a + (e.acceptedCount ?? 0), 0) / Math.max(1, exercises.reduce((a, e) => a + (e.submissionCount ?? 0), 0))) * 100)
      : 0

    return { total, solved, accuracy, byCategory, byDifficulty }
  }, [exercises])

  return (
    <div className={styles.page}>
      <PageHeading title="练习统计" description="个人解题进度与能力分析" />

      <DataState data={stats} error={error} loading={!exercises && !error}>
        {stats && (
          <div className={styles.statsGrid}>
            <div className={styles.statCard}>
              <span className={styles.statValue}>{stats.total}</span>
              <span className={styles.statLabel}>总题数</span>
            </div>
            <div className={styles.statCard}>
              <span className={styles.statValue}>{stats.solved}</span>
              <span className={styles.statLabel}>已完成</span>
            </div>
            <div className={styles.statCard}>
              <span className={styles.statValue}>{stats.accuracy}%</span>
              <span className={styles.statLabel}>正确率</span>
            </div>

            <div className={styles.statCardWide}>
              <h3>分类完成情况</h3>
              <div className={styles.statBars}>
                {[...stats.byCategory.entries()].map(([cat, { total, solved }]) => (
                  <div key={cat} className={styles.statBar}>
                    <span>{cat}</span>
                    <div className={styles.barTrack}>
                      <div className={styles.barFill} style={{ width: `${(solved / Math.max(1, total)) * 100}%` }} />
                    </div>
                    <span>{solved}/{total}</span>
                  </div>
                ))}
              </div>
            </div>

            <div className={styles.statCardWide}>
              <h3>难度分布</h3>
              <div className={styles.statBars}>
                {[...stats.byDifficulty.entries()].map(([diff, { total, solved }]) => (
                  <div key={diff} className={styles.statBar}>
                    <span>{diff}</span>
                    <div className={styles.barTrack}>
                      <div className={styles.barFill} style={{ width: `${(solved / Math.max(1, total)) * 100}%` }} />
                    </div>
                    <span>{solved}/{total}</span>
                  </div>
                ))}
              </div>
            </div>
          </div>
        )}
      </DataState>
    </div>
  )
}

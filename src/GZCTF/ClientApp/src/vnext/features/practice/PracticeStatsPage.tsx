import { useMemo } from 'react'
import { useExercises } from './api/practiceApi'
import { ExerciseInfoDto } from './api/practiceApi'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { DataState, PageHeading } from '../../shared/Primitives'
import styles from './PracticePage.module.css'

function barWidthClass(solved: number, total: number) {
  return styles[`barWidth${Math.round((solved / Math.max(1, total)) * 10)}`]
}

export function calculatePracticeStats(exercises: ExerciseInfoDto[]) {
    const total = exercises.length
    const byCategory = new Map<string, { total: number; solved: number }>()
    const byDifficulty = new Map<string, { total: number; solved: number }>()

    for (const ex of exercises) {
      const cat = ex.category ?? 'Misc'
      const diff = ex.difficulty ?? 'Baby'

      const catStats = byCategory.get(cat) ?? { total: 0, solved: 0 }
      catStats.total++
      if (ex.solved) catStats.solved++
      byCategory.set(cat, catStats)

      const diffStats = byDifficulty.get(diff) ?? { total: 0, solved: 0 }
      diffStats.total++
      if (ex.solved) diffStats.solved++
      byDifficulty.set(diff, diffStats)
    }

    const solved = exercises.filter(e => e.solved).length
    const submissions = exercises.reduce((total, exercise) => total + exercise.userSubmissionCount, 0)
    const accepted = exercises.reduce((total, exercise) => total + exercise.userAcceptedCount, 0)
    const accuracy = submissions > 0
      ? Math.round((accepted / submissions) * 100)
      : 0

    return { total, solved, accuracy, byCategory, byDifficulty }
}

export function PracticeStatsPage() {
  useVNextPageTitle('练习统计')
  const { data: exercises, error } = useExercises()
  const stats = useMemo(() => exercises ? calculatePracticeStats(exercises) : null, [exercises])

  return (
    <div className={styles.page}>
      <PageHeading eyebrow="EXERCISE PROGRESS" title="练习统计" description="个人解题进度与能力分析" />

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
                      <div className={`${styles.barFill} ${barWidthClass(solved, total)}`} />
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
                      <div className={`${styles.barFill} ${barWidthClass(solved, total)}`} />
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

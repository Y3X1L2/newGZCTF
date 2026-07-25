import { useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { Search } from 'lucide-react'
import { ChallengeCategory, Difficulty } from '@Api'
import { useExercises } from './api/practiceApi'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { DataState, PageHeading } from '../../shared/Primitives'
import styles from './PracticePage.module.css'

const categoryLabels: Record<ChallengeCategory, string> = {
  [ChallengeCategory.Web]: 'Web',
  [ChallengeCategory.Pwn]: 'Pwn',
  [ChallengeCategory.Reverse]: 'Reverse',
  [ChallengeCategory.Crypto]: 'Crypto',
  [ChallengeCategory.Misc]: 'Misc',
  [ChallengeCategory.Forensics]: 'Forensics',
  [ChallengeCategory.Mobile]: 'Mobile',
  [ChallengeCategory.Blockchain]: 'Blockchain',
  [ChallengeCategory.Programming]: 'Programming',
  [ChallengeCategory.OSint]: 'OSINT',
  [ChallengeCategory.Hardware]: 'Hardware',
}

const difficultyColors: Record<Difficulty, string> = {
  [Difficulty.Baby]: '#67e8f9',
  [Difficulty.Easy]: '#22d3ee',
  [Difficulty.Basic]: '#22c55e',
  [Difficulty.Medium]: '#eab308',
  [Difficulty.Hard]: '#f97316',
  [Difficulty.Extreme]: '#ef4444',
  [Difficulty.Insane]: '#dc2626',
  [Difficulty.Varied]: '#6b7280',
}

export function PracticePage() {
  useVNextPageTitle('自主练习')
  const [searchParams, setSearchParams] = useSearchParams()
  const [query, setQuery] = useState(searchParams.get('q') ?? '')
  const filterStr = useMemo(() => {
    const params = new URLSearchParams()
    if (query.trim()) params.set('Search', query.trim())
    return params.toString()
  }, [query])
  const { data: exercises, error } = useExercises(filterStr)

  const categories = useMemo(() => {
    if (!exercises) return []
    const map = new Map<string, { count: number; credit: number }>()
    for (const ex of exercises) {
      const key = ex.category ?? ChallengeCategory.Misc
      const existing = map.get(key) ?? { count: 0, credit: 0 }
      existing.count++
      if (ex.credit) existing.credit++
      map.set(key, existing)
    }
    return [...map.entries()].sort()
  }, [exercises])

  const recentExercises = useMemo(() => {
    if (!exercises) return []
    return [...exercises].slice(0, 6)
  }, [exercises])

  return (
    <div className={styles.page}>
      <PageHeading title="自主练习" description="题库训练、专题练习与能力提升" />

      <div className={styles.searchBar}>
        <Search size={18} />
        <input
          className={styles.searchInput}
          placeholder="搜索题目名称或内容..."
          value={query}
          onChange={(e) => setQuery(e.currentTarget.value)}
        />
      </div>

      <section className={styles.section}>
        <h2 className={styles.sectionTitle}>分类浏览</h2>
        <DataState data={categories} error={error} loading={!exercises && !error}>
          <div className={styles.categoryGrid}>
            {categories.map(([cat, stats]) => (
              <Link key={cat} to={`/practice/browse?category=${cat}`} className={styles.categoryCard}>
                <span className={styles.categoryName}>{categoryLabels[cat as ChallengeCategory] ?? cat}</span>
                <span className={styles.categoryStats}>
                  {stats.count} 题
                </span>
              </Link>
            ))}
            <Link to="/practice/browse" className={styles.categoryCard}>
              <span className={styles.categoryName}>全部题目</span>
              <span className={styles.categoryStats}>{exercises?.length ?? 0} 题</span>
            </Link>
          </div>
        </DataState>
      </section>

      <section className={styles.section}>
        <h2 className={styles.sectionTitle}>最近更新</h2>
        <DataState data={recentExercises} error={error} loading={!exercises && !error}>
          <div className={styles.recentGrid}>
            {recentExercises.map((ex) => (
              <Link key={ex.id} to={`/practice/challenge/${ex.id}`} className={styles.recentCard}>
                <div className={styles.recentHeader}>
                  <span className={styles.recentCategory}>{categoryLabels[ex.category as ChallengeCategory] ?? ex.category}</span>
                  <span className={styles.difficultyBadge} style={{ color: difficultyColors[ex.difficulty as Difficulty] }}>
                    {ex.difficulty}
                  </span>
                </div>
                <span className={styles.recentTitle}>{ex.title}</span>
                <div className={styles.recentMeta}>
                  {ex.tags?.slice(0, 3).map(t => <span key={t} className={styles.tag}>{t}</span>)}
                </div>
              </Link>
            ))}
          </div>
        </DataState>
      </section>
    </div>
  )
}

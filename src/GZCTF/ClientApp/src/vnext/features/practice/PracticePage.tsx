import { useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { ArrowRight, Search } from 'lucide-react'
import { ChallengeCategory, Difficulty } from '@Api'
import { useExercises } from './api/practiceApi'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { DataState, PageHeading, SectionHeading } from '../../shared/Primitives'
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
  [ChallengeCategory.Hardware]: 'Hardware',
  [ChallengeCategory.PPC]: 'PPC',
  [ChallengeCategory.AI]: 'AI',
  [ChallengeCategory.Pentest]: 'Pentest',
  [ChallengeCategory.OSINT]: 'OSINT',
  [ChallengeCategory.IR]: 'IR',
}

const difficultyClasses: Record<Difficulty, string> = {
  [Difficulty.Baby]: styles.difficultyBaby,
  [Difficulty.Trivial]: styles.difficultyTrivial,
  [Difficulty.Easy]: styles.difficultyEasy,
  [Difficulty.Normal]: styles.difficultyNormal,
  [Difficulty.Medium]: styles.difficultyMedium,
  [Difficulty.Hard]: styles.difficultyHard,
  [Difficulty.Expert]: styles.difficultyExpert,
  [Difficulty.Insane]: styles.difficultyInsane,
}

export function PracticePage() {
  useVNextPageTitle('自主练习')
  const [searchParams] = useSearchParams()
  const [query, setQuery] = useState(searchParams.get('q') ?? '')
  const filterStr = useMemo(() => {
    const params = new URLSearchParams()
    if (query.trim()) params.set('Search', query.trim())
    return params.toString()
  }, [query])
  const { data: exercises, error } = useExercises(filterStr)

  const categories = useMemo(() => {
    const map = new Map<string, { count: number; credit: number }>()
    for (const ex of exercises ?? []) {
      const key = ex.category ?? ChallengeCategory.Misc
      const existing = map.get(key) ?? { count: 0, credit: 0 }
      existing.count++
      if (ex.credit) existing.credit++
      map.set(key, existing)
    }
    return Object.entries(categoryLabels).map(([category, label]) => [
      category,
      label,
      map.get(category)?.count ?? 0,
    ] as const)
  }, [exercises])

  const recentExercises = useMemo(() => {
    if (!exercises) return []
    return [...exercises].slice(0, 6)
  }, [exercises])

  return (
    <div className={styles.page}>
      <PageHeading eyebrow="EXERCISE" title="自主练习" description="题库训练、专题练习与能力提升" />

      <label className={styles.searchBar}>
        <Search size={18} />
        <input
          className={styles.searchInput}
          name="practice-search"
          placeholder="搜索题目名称或内容..."
          value={query}
          onChange={(e) => setQuery(e.currentTarget.value)}
          type="search"
        />
      </label>

      <section className={styles.section}>
        <SectionHeading eyebrow="EXPLORE BY CATEGORY" route="/practice/browse" routeLabel="浏览全部" title="分类浏览" />
        {!exercises && !error ? (
          <DataState description="正在统计练习题分类。" loading title="分类加载中" />
        ) : error ? (
          <DataState description="练习题库暂时不可用，请稍后刷新。" title="分类加载失败" />
        ) : (
          <div className={styles.categoryGrid}>
            <Link to="/practice/browse" className={`${styles.categoryCard} ${styles.categoryCardAll}`}>
              <span className={styles.categoryCardTop}>
                <span className={styles.categoryName}>全部题目</span>
                <ArrowRight size={16} />
              </span>
              <strong>{exercises?.length ?? 0}</strong>
              <span className={styles.categoryStats}>道练习题</span>
            </Link>
            {categories.map(([cat, label, count]) => (
              <Link key={cat} to={`/practice/browse?category=${cat}`} className={styles.categoryCard}>
                <span className={styles.categoryCardTop}>
                  <span className={styles.categoryName}>{label}</span>
                  <ArrowRight size={15} />
                </span>
                <strong>{count}</strong>
                <span className={styles.categoryStats}>道练习题</span>
              </Link>
            ))}
          </div>
        )}
      </section>

      <section className={styles.section}>
        <SectionHeading eyebrow="RECENTLY ADDED" title="最近更新" />
        {!exercises && !error ? (
          <DataState description="正在读取练习题库。" loading title="题库加载中" />
        ) : error ? (
          <DataState description="练习题库暂时不可用，请稍后刷新。" title="题库加载失败" />
        ) : recentExercises.length ? (
          <div className={styles.recentGrid}>
            {recentExercises.map((ex) => (
              <Link key={ex.id} to={`/practice/challenge/${ex.id}`} className={styles.recentCard}>
                <div className={styles.recentHeader}>
                  <span className={styles.recentCategory}>{categoryLabels[ex.category as ChallengeCategory] ?? ex.category}</span>
                  <span className={`${styles.difficultyBadge} ${difficultyClasses[ex.difficulty as Difficulty]}`}>
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
        ) : (
          <div className={styles.emptySection}>
            <span>00</span>
            <div><strong>题库尚未收录练习</strong><p>通过管理端或 Exercise API 添加题目后，最新内容会显示在这里。</p></div>
          </div>
        )}
      </section>
    </div>
  )
}

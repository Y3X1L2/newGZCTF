import { useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { Search, ChevronDown, X } from 'lucide-react'
import { ChallengeCategory, Difficulty } from '@Api'
import { useExercises } from './api/practiceApi'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { DataState, PageHeading } from '../../shared/Primitives'
import styles from './PracticePage.module.css'

const categoryLabels: Record<string, string> = {
  Web: 'Web', Pwn: 'Pwn', Reverse: 'Reverse', Crypto: 'Crypto',
  Misc: 'Misc', Forensics: 'Forensics', Mobile: 'Mobile',
  Blockchain: 'Blockchain', Programming: 'Programming',
  OSint: 'OSINT', Hardware: 'Hardware',
}

const difficultyOrder = ['Baby', 'Easy', 'Basic', 'Medium', 'Hard', 'Extreme', 'Insane']
const allDifficulties = [...difficultyOrder]
const allCategories = Object.keys(categoryLabels)

export function PracticeBrowsePage() {
  useVNextPageTitle('题库浏览')
  const [searchParams, setSearchParams] = useSearchParams()

  const query = searchParams.get('q') ?? ''
  const selectedCats = useMemo(() => searchParams.getAll('category'), [searchParams])
  const selectedDiffs = useMemo(() => searchParams.getAll('difficulty'), [searchParams])
  const selectedTags = useMemo(() => searchParams.getAll('tag'), [searchParams])

  const [showFilters, setShowFilters] = useState(false)
  const [localQuery, setLocalQuery] = useState(query)

  const filterStr = useMemo(() => {
    const params = new URLSearchParams()
    if (query) params.set('Search', query)
    selectedCats.forEach(c => params.append('Categories', c))
    selectedDiffs.forEach(d => params.append('Difficulties', d))
    selectedTags.forEach(t => params.append('Tags', t))
    return params.toString()
  }, [query, selectedCats, selectedDiffs, selectedTags])

  const { data: exercises, error } = useExercises(filterStr)

  const allTags = useMemo(() => {
    if (!exercises) return []
    return [...new Set(exercises.flatMap(e => e.tags ?? []))].sort()
  }, [exercises])

  const filtered = useMemo(() => {
    if (!exercises) return []
    return exercises
  }, [exercises])

  const updateParam = (key: string, values: string[]) => {
    const next = new URLSearchParams(searchParams)
    next.delete(key)
    values.forEach(v => next.append(key, v))
    setSearchParams(next, { replace: true })
  }

  const toggleCategory = (cat: string) => {
    const next = selectedCats.includes(cat)
      ? selectedCats.filter(c => c !== cat)
      : [...selectedCats, cat]
    updateParam('category', next)
  }

  const toggleDifficulty = (diff: string) => {
    const next = selectedDiffs.includes(diff)
      ? selectedDiffs.filter(d => d !== diff)
      : [...selectedDiffs, diff]
    updateParam('difficulty', next)
  }

  const toggleTag = (tag: string) => {
    const next = selectedTags.includes(tag)
      ? selectedTags.filter(t => t !== tag)
      : [...selectedTags, tag]
    updateParam('tag', next)
  }

  const search = (value: string) => {
    setLocalQuery(value)
    const next = new URLSearchParams(searchParams)
    if (value.trim()) next.set('q', value.trim())
    else next.delete('q')
    setSearchParams(next, { replace: true })
  }

  return (
    <div className={styles.page}>
      <PageHeading title="题库浏览" description="筛选、搜索与查看所有练习题" />

      <div className={styles.searchBar}>
        <Search size={18} />
        <input
          className={styles.searchInput}
          placeholder="搜索题目名称或内容..."
          value={localQuery}
          onChange={(e) => search(e.currentTarget.value)}
        />
        <button className={styles.filterToggle} onClick={() => setShowFilters(!showFilters)}>
          <ChevronDown size={16} />
          筛选
        </button>
      </div>

      {showFilters && (
        <div className={styles.filterPanel}>
          <div className={styles.filterGroup}>
            <span className={styles.filterLabel}>分类</span>
            <div className={styles.filterChips}>
              {allCategories.map(cat => (
                <button
                  key={cat}
                  className={selectedCats.includes(cat) ? styles.chipActive : styles.chip}
                  onClick={() => toggleCategory(cat)}
                >
                  {categoryLabels[cat] ?? cat}
                </button>
              ))}
            </div>
          </div>

          <div className={styles.filterGroup}>
            <span className={styles.filterLabel}>难度</span>
            <div className={styles.filterChips}>
              {allDifficulties.map(diff => (
                <button
                  key={diff}
                  className={selectedDiffs.includes(diff) ? styles.chipActive : styles.chip}
                  onClick={() => toggleDifficulty(diff)}
                >
                  {diff}
                </button>
              ))}
            </div>
          </div>

          {allTags.length > 0 && (
            <div className={styles.filterGroup}>
              <span className={styles.filterLabel}>标签</span>
              <div className={styles.filterChips}>
                {allTags.map(tag => (
                  <button
                    key={tag}
                    className={selectedTags.includes(tag) ? styles.chipActive : styles.chip}
                    onClick={() => toggleTag(tag)}
                  >
                    {tag}
                  </button>
                ))}
              </div>
            </div>
          )}
        </div>
      )}

      <div className={styles.activeFilters}>
        {selectedCats.map(c => (
          <span key={c} className={styles.filterPill}>
            {categoryLabels[c] ?? c} <X size={12} onClick={() => toggleCategory(c)} />
          </span>
        ))}
        {selectedDiffs.map(d => (
          <span key={d} className={styles.filterPill}>
            {d} <X size={12} onClick={() => toggleDifficulty(d)} />
          </span>
        ))}
        {selectedTags.map(t => (
          <span key={t} className={styles.filterPill}>
            {t} <X size={12} onClick={() => toggleTag(t)} />
          </span>
        ))}
      </div>

      <DataState data={filtered} error={error} loading={!exercises && !error}>
        <div className={styles.challengeList}>
          {filtered.map((ex) => (
            <Link key={ex.id} to={`/practice/challenge/${ex.id}`} className={styles.challengeCard}>
              <div className={styles.challengeHeader}>
                <span className={styles.challengeCategory}>{categoryLabels[ex.category as string] ?? ex.category}</span>
                <span className={styles.challengeDifficulty}>{ex.difficulty}</span>
              </div>
              <h3 className={styles.challengeTitle}>{ex.title}</h3>
              <div className={styles.challengeMeta}>
                {ex.tags?.slice(0, 4).map(t => <span key={t} className={styles.tag}>{t}</span>)}
              </div>
              <div className={styles.challengeStats}>
                <span>{ex.acceptedCount ?? 0} 已完成</span>
                <span>{ex.submissionCount ?? 0} 提交</span>
              </div>
            </Link>
          ))}
        </div>
      </DataState>
    </div>
  )
}

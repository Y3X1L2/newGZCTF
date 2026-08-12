import { useMemo, useState } from 'react'
import { Link, useLocation, useSearchParams } from 'react-router'
import { Check, ChevronDown, Search, Star, Tag } from 'lucide-react'
import { useExercises } from './api/practiceApi'
import { useCurrentAccount } from '../account/useCurrentAccount'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { DataState, PageHeading } from '../../shared/Primitives'
import styles from './PracticePage.module.css'

const categoryLabels: Record<string, string> = {
  Web: 'Web', Pwn: 'Pwn', Reverse: 'Reverse', Crypto: 'Crypto',
  Misc: 'Misc', Forensics: 'Forensics', Mobile: 'Mobile',
  Blockchain: 'Blockchain', Hardware: 'Hardware', PPC: 'PPC', AI: 'AI',
  Pentest: 'Pentest', OSINT: 'OSINT', IR: 'IR',
}

const difficultyStars: Record<string, number> = {
  Baby: 1,
  Trivial: 2,
  Easy: 3,
  Normal: 4,
  Medium: 5,
  Hard: 7,
  Expert: 9,
  Insane: 10,
}
const difficultyByStar = [
  '', 'Baby', 'Trivial', 'Easy', 'Normal', 'Medium', 'Medium', 'Hard', 'Hard', 'Expert', 'Insane',
]
const maxDifficultyStars = 10
const allCategories = Object.keys(categoryLabels)
const sourceLabels = { Exercise: '练习题目', Game: '比赛题目', Training: '培训习题' } as const
const statusLabels = { all: '全部', solved: '已攻克', unsolved: '未攻克' } as const
type ExerciseStatus = keyof typeof statusLabels

export function PracticeBrowsePage() {
  const { pathname } = useLocation()
  const isBrowseRoute = pathname.endsWith('/browse')
  useVNextPageTitle(isBrowseRoute ? '题库浏览' : '自主练习')
  const [searchParams, setSearchParams] = useSearchParams()

  const query = searchParams.get('q') ?? ''
  const selectedCats = useMemo(() => searchParams.getAll('category'), [searchParams])
  const selectedDifficultyStars = Math.min(
    maxDifficultyStars,
    Math.max(0, Number.parseInt(searchParams.get('difficultyStars') ?? '0', 10) || 0)
  )
  const selectedDiffs = useMemo(() => {
    const legacy = searchParams.getAll('difficulty')
    if (selectedDifficultyStars === 0) return legacy
    return [difficultyByStar[selectedDifficultyStars]]
  }, [searchParams, selectedDifficultyStars])
  const selectedTags = useMemo(() => searchParams.getAll('tag'), [searchParams])
  const selectedSources = useMemo(() => searchParams.getAll('source'), [searchParams])
  const selectedStatus = (searchParams.get('status') as ExerciseStatus | null) ?? 'all'
  const { isTeacher } = useCurrentAccount()

  const [showFilters, setShowFilters] = useState(true)
  const [localQuery, setLocalQuery] = useState(query)

  const filterStr = useMemo(() => {
    const params = new URLSearchParams()
    if (query) params.set('Search', query)
    selectedCats.forEach(c => params.append('Categories', c))
    selectedDiffs.forEach(d => params.append('Difficulties', d))
    if (isTeacher) selectedSources.forEach(source => params.append('Sources', source))
    return params.toString()
  }, [query, selectedCats, selectedDiffs, selectedSources, isTeacher])

  const { data: exercises, error } = useExercises(filterStr)

  const allTags = useMemo(() => {
    if (!exercises) return []
    return [...new Set(exercises.flatMap(e => e.tags ?? []))].sort()
  }, [exercises])

  const filtered = useMemo(() => {
    if (!exercises) return []
    return exercises.filter(exercise => {
      const matchesTags = selectedTags.length === 0 || selectedTags.some(tag => exercise.tags?.includes(tag))
      const matchesStatus = selectedStatus === 'all' || (selectedStatus === 'solved' ? exercise.solved : !exercise.solved)
      return matchesTags && matchesStatus
    })
  }, [exercises, selectedStatus, selectedTags])

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

  const toggleDifficulty = (stars: number) => {
    const next = new URLSearchParams(searchParams)
    next.delete('difficulty')
    next.delete('difficultyStars')
    if (stars > 0) next.set('difficultyStars', String(stars))
    setSearchParams(next, { replace: true })
  }

  const toggleTag = (tag: string) => {
    const next = selectedTags.includes(tag)
      ? selectedTags.filter(t => t !== tag)
      : [...selectedTags, tag]
    updateParam('tag', next)
  }

  const toggleSource = (source: string) => {
    const next = selectedSources.includes(source)
      ? selectedSources.filter(item => item !== source)
      : [...selectedSources, source]
    updateParam('source', next)
  }

  const setStatus = (status: ExerciseStatus) => {
    const next = new URLSearchParams(searchParams)
    if (status === 'all') next.delete('status')
    else next.set('status', status)
    setSearchParams(next, { replace: true })
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
      <PageHeading
        eyebrow="EXERCISE CATALOG"
        title={isBrowseRoute ? '题库浏览' : '自主练习'}
        description="筛选、搜索与查看所有练习题"
      />

      <div className={styles.searchBar}>
        <Search size={18} />
        <input
          className={styles.searchInput}
          name="practice-catalog-search"
          placeholder="搜索题目名称或内容..."
          type="search"
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
          <div className={styles.filterRow}>
            <div className={styles.filterGroup}>
              <span className={styles.filterLabel}>标签</span>
              <details className={styles.tagDropdown}>
                <summary className={styles.tagDropdownTrigger}>
                  <Tag size={14} />
                  {selectedTags.length ? `${selectedTags.length} 个标签` : '全部标签'}
                  <ChevronDown size={14} />
                </summary>
                <div className={styles.tagDropdownMenu}>
                  <button
                    type="button"
                    className={selectedTags.length === 0 ? styles.tagOptionActive : styles.tagOption}
                    onClick={() => updateParam('tag', [])}
                  >
                    {selectedTags.length === 0 && <Check size={13} />}
                    <span>全部标签</span>
                  </button>
                  {allTags.map(tag => (
                    <button
                      type="button"
                      key={tag}
                      className={selectedTags.includes(tag) ? styles.tagOptionActive : styles.tagOption}
                      onClick={() => toggleTag(tag)}
                    >
                      {selectedTags.includes(tag) && <Check size={13} />}
                      <span>{tag}</span>
                    </button>
                  ))}
                </div>
              </details>
            </div>

            <div className={styles.filterGroup}>
              <span className={styles.filterLabel}>难度</span>
              <div className={styles.starFilter} role="radiogroup" aria-label="难度星级，0 颗星表示不限">
                {Array.from({ length: maxDifficultyStars }, (_, index) => {
                  const stars = index + 1
                  const active = stars <= selectedDifficultyStars
                  return (
                  <button
                    type="button"
                    key={stars}
                    className={active ? styles.ratingStarActive : styles.ratingStar}
                    onClick={() => toggleDifficulty(selectedDifficultyStars === stars ? 0 : stars)}
                    aria-label={`${stars} 星难度`}
                    aria-pressed={selectedDifficultyStars === stars}
                  >
                    <Star size={18} fill={active ? 'currentColor' : 'none'} />
                  </button>
                  )
                })}
              </div>
            </div>
          </div>

          <div className={styles.filterGroup}>
            <span className={styles.filterLabel}>分类</span>
            <div className={styles.filterChips}>
              {allCategories.map(cat => (
                <button
                  type="button"
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
            <span className={styles.filterLabel}>状态</span>
            <div className={styles.statusOptions} role="radiogroup" aria-label="题目状态">
              {Object.entries(statusLabels).map(([status, label]) => (
                <button
                  type="button"
                  key={status}
                  className={selectedStatus === status ? styles.chipActive : styles.chip}
                  onClick={() => setStatus(status as ExerciseStatus)}
                  aria-pressed={selectedStatus === status}
                >
                  {label}
                </button>
              ))}
            </div>
          </div>

          {isTeacher && (
            <div className={styles.filterGroup}>
              <span className={styles.filterLabel}>题目来源</span>
              <div className={styles.filterChips}>
                {Object.entries(sourceLabels).map(([source, label]) => (
                  <button
                    key={source}
                    className={selectedSources.includes(source) ? styles.chipActive : styles.chip}
                    onClick={() => toggleSource(source)}
                  >
                    {label}
                  </button>
                ))}
              </div>
            </div>
          )}
        </div>
      )}

      {!exercises && !error ? (
        <DataState description="正在读取题目和筛选条件。" loading title="题库加载中" />
      ) : error ? (
        <DataState description="练习题库暂时不可用，请稍后刷新。" title="题库加载失败" />
      ) : filtered.length ? (
        <div className={styles.challengeList}>
          {filtered.map((ex) => (
            <Link key={ex.id} to={`/practice/challenge/${ex.id}`} className={styles.challengeCard}>
              <div className={styles.challengeHeader}>
                <span className={styles.challengeCategory}>{categoryLabels[ex.category as string] ?? ex.category}</span>
                <span
                  className={styles.challengeDifficulty}
                  aria-label={`${difficultyStars[ex.difficulty ?? 'Baby'] ?? 1} 星难度`}
                  title={`${ex.difficulty ?? 'Baby'} · ${difficultyStars[ex.difficulty ?? 'Baby'] ?? 1} 星`}
                >
                  {Array.from({ length: maxDifficultyStars }, (_, index) => (
                    <Star
                      key={index}
                      size={13}
                      fill={index < (difficultyStars[ex.difficulty ?? 'Baby'] ?? 1) ? 'currentColor' : 'none'}
                    />
                  ))}
                </span>
              </div>
              <h3 className={styles.challengeTitle}>{ex.title}</h3>
              <div className={styles.challengeMeta}>
                {ex.tags?.slice(0, 4).map(t => <span key={t} className={styles.tag}>{t}</span>)}
              </div>
              <div className={styles.challengeStats}>
                <span className={ex.solved ? styles.solvedStatus : styles.unsolvedStatus}>
                  {ex.solved ? '已攻克' : '未攻克'}
                </span>
                <span>{ex.acceptedCount ?? 0} 已完成</span>
                <span>{ex.submissionCount ?? 0} 提交</span>
              </div>
            </Link>
          ))}
        </div>
      ) : (
        <div className={styles.emptySection}>
          <span>00</span>
          <div><strong>没有符合条件的题目</strong><p>调整关键词或筛选条件后重试。</p></div>
        </div>
      )}
    </div>
  )
}

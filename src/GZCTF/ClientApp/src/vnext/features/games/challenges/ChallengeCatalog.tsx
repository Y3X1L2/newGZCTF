import { Check, ChevronDown, ChevronRight, Search, X } from 'lucide-react'
import { ChangeEvent, useMemo, useState } from 'react'
import { ChallengeInfo, SubmissionType } from '@Api'
import styles from './ChallengeCatalog.module.css'
import { categoryMeta } from './challengeCategories'

export interface ChallengeGroup {
  category: string
  challenges: ChallengeInfo[]
}

export function ChallengeCatalog({
  groups,
  selectedId,
  solvedTypes,
  query,
  hideSolved,
  onQueryChange,
  onHideSolvedChange,
  onSelect,
  onMobileClose,
}: {
  groups: ChallengeGroup[]
  selectedId: number | null
  solvedTypes: Map<number, SubmissionType>
  query: string
  hideSolved: boolean
  onQueryChange: (value: string) => void
  onHideSolvedChange: (value: boolean) => void
  onSelect: (challenge: ChallengeInfo) => void
  onMobileClose: () => void
}) {
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set())

  const normalizedQuery = query.trim().toLocaleLowerCase('zh-CN')
  const visibleGroups = useMemo(
    () =>
      groups
        .map((group) => ({
          ...group,
          challenges: group.challenges.filter((challenge) => {
            const solvedType = solvedTypes.get(challenge.id)
            const solved = solvedType !== undefined && solvedType !== SubmissionType.Unaccepted
            if (hideSolved && solved) return false
            if (!normalizedQuery) return true
            return challenge.title.toLocaleLowerCase('zh-CN').includes(normalizedQuery)
          }),
        }))
        .filter((group) => group.challenges.length > 0),
    [groups, hideSolved, normalizedQuery, solvedTypes]
  )

  const effectiveExpanded = normalizedQuery ? new Set(visibleGroups.map((group) => group.category)) : expanded

  const toggleCategory = (category: string) => {
    setExpanded((current) => {
      const next = new Set(current)
      if (next.has(category)) next.delete(category)
      else next.add(category)
      return next
    })
  }

  const onSearchChange = (event: ChangeEvent<HTMLInputElement>) => onQueryChange(event.currentTarget.value)

  return (
    <div className={styles.catalog}>
      <header className={styles.catalogHeader}>
        <div>
          <span>CHALLENGE INDEX</span>
          <strong>题目列表</strong>
        </div>
        <button aria-label="关闭题目列表" className={styles.mobileClose} onClick={onMobileClose} type="button">
          <X size={18} />
        </button>
      </header>

      <div className={styles.catalogControls}>
        <label className={styles.searchBox}>
          <Search size={16} />
          <input aria-label="搜索题目" onChange={onSearchChange} placeholder="搜索题目" type="search" value={query} />
        </label>
        <label className={styles.hideSolved}>
          <input
            checked={hideSolved}
            onChange={(event) => onHideSolvedChange(event.currentTarget.checked)}
            type="checkbox"
          />
          <span>隐藏已解题目</span>
        </label>
      </div>

      <div className={styles.groupList}>
        {visibleGroups.map((group) => {
          const meta = categoryMeta(group.category)
          const Icon = meta.icon
          const open = effectiveExpanded.has(group.category)
          return (
            <section className={styles.challengeGroup} key={group.category}>
              <button
                aria-expanded={open}
                className={styles.categoryRow}
                onClick={() => toggleCategory(group.category)}
                type="button"
              >
                {open ? <ChevronDown size={15} /> : <ChevronRight size={15} />}
                <Icon size={16} />
                <strong>{meta.label}</strong>
                <span>{group.challenges.length}</span>
              </button>
              {open ? (
                <div className={styles.challengeGroupBody}>
                  {group.challenges.map((challenge) => {
                    const solvedType = solvedTypes.get(challenge.id)
                    const solved = solvedType !== undefined && solvedType !== SubmissionType.Unaccepted
                    return (
                      <button
                        className={challenge.id === selectedId ? styles.challengeRowActive : styles.challengeRow}
                        key={challenge.id}
                        onClick={() => onSelect(challenge)}
                        type="button"
                      >
                        <span className={styles.challengeName}>{challenge.title}</span>
                        <span className={styles.challengeScore}>{challenge.score}</span>
                        {solved ? <Check aria-label="已解" className={styles.solvedIcon} size={15} /> : null}
                      </button>
                    )
                  })}
                </div>
              ) : null}
            </section>
          )
        })}
        {!visibleGroups.length ? <div className={styles.emptyCatalog}>没有符合条件的题目。</div> : null}
      </div>
    </div>
  )
}

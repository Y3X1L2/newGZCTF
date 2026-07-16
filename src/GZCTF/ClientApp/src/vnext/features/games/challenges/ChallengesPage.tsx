import { Bell, Check, Download, FileArchive, Flag, Lightbulb, ListTree, ShieldCheck, Trophy, Users } from 'lucide-react'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router'
import { useConfig } from '@Hooks/useConfig'
import { AnswerResult, ChallengeDetailModel, ChallengeInfo, SubmissionType } from '@Api'
import { ActionButton } from '../../../shared/Interaction'
import { MarkdownContent } from '../../../shared/MarkdownContent'
import { DataState, StatusPill } from '../../../shared/Primitives'
import { FlagSubmission } from '../../challenge-runtime/FlagSubmission'
import { InstanceControl } from '../../challenge-runtime/InstanceControl'
import { useGameChallenge, useGameChallengeCatalog } from '../gamePlayerApi'
import { formatWorkspaceNotice, useGameWorkspace } from '../workspace/GameWorkspaceShell'
import { ChallengeCatalog, ChallengeGroup } from './ChallengeCatalog'
import styles from './ChallengesPage.module.css'
import { categoryMeta } from './challengeCategories'
import { useFlagSubmission } from './useFlagSubmission'
import { useGameInstance } from './useGameInstance'

function formatFileSize(value?: number | null) {
  if (!value) return '大小未知'
  const units = ['B', 'KB', 'MB', 'GB']
  let size = value
  let index = 0
  while (size >= 1024 && index < units.length - 1) {
    size /= 1024
    index += 1
  }
  return `${size >= 10 || index === 0 ? size.toFixed(0) : size.toFixed(1)} ${units[index]}`
}

function formatTime(value: number) {
  return new Intl.DateTimeFormat('zh-CN', {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  }).format(value)
}

function attachmentName(url: string) {
  try {
    return decodeURIComponent(
      new URL(url, window.location.origin).pathname.split('/').filter(Boolean).at(-1) || '题目附件'
    )
  } catch {
    return '题目附件'
  }
}

function resultLabel(result: AnswerResult | 'Error') {
  if (result === AnswerResult.Accepted) return '正确'
  if (result === AnswerResult.WrongAnswer) return '错误'
  if (result === AnswerResult.FlagSubmitted) return '判题中'
  if (result === AnswerResult.CheatDetected) return '异常'
  if (result === AnswerResult.NotFound) return '未找到'
  return '失败'
}

function disabledReason({
  start,
  end,
  practiceMode,
  deadline,
  limit,
  attempts,
}: {
  start?: number
  end?: number
  practiceMode?: boolean
  deadline?: number | null
  limit?: number
  attempts?: number
}) {
  const now = Date.now()
  if (start && now < start) return '比赛尚未开始，当前不能提交。'
  if (end && now >= end && !practiceMode) return '比赛已经结束，当前未开放赛后练习。'
  if (deadline && now >= deadline && !(end && now >= end && practiceMode)) return '该题目的提交期限已结束。'
  if (limit && (attempts ?? 0) >= limit) return '该题目的提交次数已经用完。'
  return null
}

function ChallengeContextRail({
  teamName,
  rank,
  score,
  solvedCount,
  notices,
  submissions,
}: {
  teamName?: string | null
  rank?: number
  score?: number
  solvedCount?: number
  notices: ReturnType<typeof useGameWorkspace>['notices']
  submissions: ReturnType<typeof useFlagSubmission>['submissions']
}) {
  return (
    <div className={styles.contextStack}>
      <section className={styles.contextPanel}>
        <header>
          <span>TEAM CONTEXT</span>
          <h2>参赛队伍</h2>
        </header>
        <div className={styles.teamIdentity}>
          <span>
            <Users size={18} />
          </span>
          <div>
            <strong>{teamName || '当前队伍'}</strong>
            <small>本队实时竞赛数据</small>
          </div>
        </div>
        <div className={styles.teamMetrics}>
          <div>
            <Trophy size={16} />
            <span>排名</span>
            <strong>{rank ? `#${rank}` : '--'}</strong>
          </div>
          <div>
            <ShieldCheck size={16} />
            <span>分数</span>
            <strong>{score ?? '--'}</strong>
          </div>
          <div>
            <Check size={16} />
            <span>已解</span>
            <strong>{solvedCount ?? 0}</strong>
          </div>
        </div>
      </section>

      <section className={styles.contextPanel}>
        <header>
          <span>GAME NOTICE</span>
          <h2>比赛通知</h2>
        </header>
        <div className={styles.noticeList}>
          {notices.slice(0, 6).map((notice) => (
            <article key={`${notice.id}:${notice.time}:${notice.type}`}>
              <Bell size={15} />
              <div>
                <p>{formatWorkspaceNotice(notice)}</p>
                <time>{formatTime(notice.time)}</time>
              </div>
            </article>
          ))}
          {!notices.length ? <p className={styles.contextEmpty}>当前没有比赛通知。</p> : null}
        </div>
      </section>

      <section className={styles.contextPanel}>
        <header>
          <span>THIS SESSION</span>
          <h2>本次提交</h2>
        </header>
        <div className={styles.submissionList}>
          {submissions.slice(0, 6).map((submission) => (
            <article key={submission.key}>
              <span
                className={
                  submission.result === AnswerResult.Accepted ? styles.submissionAccepted : styles.submissionOther
                }
              >
                <Flag size={14} />
              </span>
              <div>
                <strong>{submission.challengeTitle}</strong>
                <small>{submission.answerPreview}</small>
              </div>
              <em>{resultLabel(submission.result)}</em>
            </article>
          ))}
          {!submissions.length ? <p className={styles.contextEmpty}>本次会话还没有提交记录。</p> : null}
        </div>
      </section>
    </div>
  )
}

export function ChallengesPage() {
  const { gameId, game, notices, revision } = useGameWorkspace()
  const { config } = useConfig()
  const [searchParams, setSearchParams] = useSearchParams()
  const selectedId = Number(searchParams.get('challenge')) || null
  const query = searchParams.get('q') ?? ''
  const hideSolved = searchParams.get('status') === 'unsolved'
  const [catalogOpen, setCatalogOpen] = useState(false)
  const [flagValues, setFlagValues] = useState<Map<number, string>>(() => new Map())
  const [activeFlagId, setActiveFlagId] = useState<number | null>(null)

  const ongoing = Date.now() >= (game.start ?? 0) && Date.now() < (game.end ?? 0)
  const {
    data: teamInfo,
    error: teamError,
    mutate: mutateTeamInfo,
  } = useGameChallengeCatalog(gameId, ongoing ? 10_000 : 0)

  useEffect(() => {
    if (revision > 0) void mutateTeamInfo()
  }, [mutateTeamInfo, revision])

  const groups = useMemo<ChallengeGroup[]>(
    () =>
      Object.entries(teamInfo?.challenges ?? {}).map(([category, challenges]) => ({
        category,
        challenges,
      })),
    [teamInfo?.challenges]
  )
  const allChallenges = useMemo(() => groups.flatMap((group) => group.challenges), [groups])
  const selectedSummary = useMemo(
    () => allChallenges.find((challenge) => challenge.id === selectedId),
    [allChallenges, selectedId]
  )
  const solvedEntries = useMemo(
    () => (teamInfo?.rank?.solvedChallenges ?? []).filter((entry) => entry.type !== SubmissionType.Unaccepted),
    [teamInfo?.rank?.solvedChallenges]
  )
  const solvedTypes = useMemo(() => new Map(solvedEntries.map((entry) => [entry.id, entry.type])), [solvedEntries])
  const selectedSolvedEntries = useMemo(
    () => solvedEntries.filter((entry) => entry.id === selectedId),
    [selectedId, solvedEntries]
  )
  const solvedFlagIds = useMemo(
    () => new Set(selectedSolvedEntries.map((entry) => entry.flagId)),
    [selectedSolvedEntries]
  )

  useEffect(() => {
    if (!allChallenges.length) return
    if (selectedId && allChallenges.some((challenge) => challenge.id === selectedId)) return
    const next = new URLSearchParams(searchParams)
    next.set('challenge', String(allChallenges[0].id))
    setSearchParams(next, { replace: true })
  }, [allChallenges, searchParams, selectedId, setSearchParams])

  const {
    data: challenge,
    error: challengeError,
    mutate: mutateChallenge,
  } = useGameChallenge(gameId, selectedId ?? 0, ongoing ? 60_000 : 0, Boolean(selectedId))

  useEffect(() => {
    if (!challenge || challenge.id !== selectedId) return
    const flags = challenge.flags ?? []
    if (flags.length <= 1) {
      setActiveFlagId(flags[0]?.id ?? null)
      return
    }
    const available = flags.find((flag, index) => !solvedFlagIds.has(flag.id ?? index + 1)) ?? flags[0]
    setActiveFlagId(available.id ?? null)
  }, [challenge?.id, selectedId, solvedFlagIds])

  const updateChallenge = useCallback(
    (next: ChallengeDetailModel) => {
      void mutateChallenge(next, { revalidate: false })
    },
    [mutateChallenge]
  )
  const refreshChallenge = useCallback(async () => mutateChallenge(), [mutateChallenge])
  const instance = useGameInstance({
    gameId,
    challenge: challenge?.id === selectedId ? challenge : undefined,
    updateChallenge,
    refreshChallenge,
  })
  const flagSubmission = useFlagSubmission(gameId, config.apiPublicKey)

  const setParam = (key: string, value: string | null) => {
    const next = new URLSearchParams(searchParams)
    if (value) next.set(key, value)
    else next.delete(key)
    setSearchParams(next, { replace: true })
  }

  const selectChallenge = (nextChallenge: ChallengeInfo) => {
    setParam('challenge', String(nextChallenge.id))
    setCatalogOpen(false)
  }

  const flagValue = selectedId ? (flagValues.get(selectedId) ?? '') : ''
  const setFlagValue = (value: string) => {
    if (!selectedId) return
    setFlagValues((current) => new Map(current).set(selectedId, value))
  }

  const totalFlags = Math.max(selectedSummary?.totalFlags ?? 0, challenge?.flags?.length ?? 0, 1)
  const solved = selectedSolvedEntries.length >= totalFlags
  const submitDisabledReason = challenge
    ? disabledReason({
        start: game.start,
        end: game.end,
        practiceMode: game.practiceMode,
        deadline: challenge.deadline,
        limit: challenge.limit,
        attempts: challenge.attempts,
      })
    : null
  const pending = selectedId ? flagSubmission.isPending(selectedId, activeFlagId, flagValue) : false

  const submitFlag = async () => {
    if (!challenge?.id || !flagValue.trim() || pending) return
    const capturedId = challenge.id
    const capturedValue = flagValue
    const result = await flagSubmission.submit({
      challengeId: capturedId,
      challengeTitle: challenge.title || selectedSummary?.title || `题目 ${capturedId}`,
      flagId: (challenge.flags?.length ?? 0) > 1 ? activeFlagId : null,
      value: capturedValue,
    })
    if (result === AnswerResult.Accepted) {
      setFlagValues((current) => {
        if (current.get(capturedId) !== capturedValue) return current
        const next = new Map(current)
        next.set(capturedId, '')
        return next
      })
      await Promise.all([mutateTeamInfo(), mutateChallenge()])
    }
  }

  if (!teamInfo && !teamError) {
    return (
      <div className={styles.statePage}>
        <DataState description="正在同步题目、队伍与解题状态。" loading title="题目工作区加载中" />
      </div>
    )
  }
  if (teamError || !teamInfo) {
    return (
      <div className={styles.statePage}>
        <DataState description="当前账户尚未通过报名审核，或题目服务暂时不可用。" title="无法进入题目工作区" />
      </div>
    )
  }
  if (!allChallenges.length) {
    return (
      <div className={styles.statePage}>
        <DataState description="比赛尚未发布题目，请等待管理员开放。" title="暂无题目" />
      </div>
    )
  }

  const displayedChallenge = challenge?.id === selectedId ? challenge : undefined
  const meta = categoryMeta(displayedChallenge?.category ?? selectedSummary?.category ?? 'Misc')
  const CategoryIcon = meta.icon
  const attachmentUrl = displayedChallenge?.context?.url

  return (
    <div className={styles.page}>
      <div className={styles.mobileToolbar}>
        <ActionButton icon={<ListTree size={17} />} onClick={() => setCatalogOpen(true)} type="button">
          题目列表
        </ActionButton>
        <span>{selectedSummary ? `${meta.label} · ${selectedSummary.score} pts` : '选择题目'}</span>
      </div>

      {catalogOpen ? (
        <button
          aria-label="关闭题目列表"
          className={styles.catalogScrim}
          onClick={() => setCatalogOpen(false)}
          type="button"
        />
      ) : null}
      <div className={styles.workspaceLayout}>
        <aside className={catalogOpen ? styles.catalogRailOpen : styles.catalogRail}>
          <ChallengeCatalog
            groups={groups}
            hideSolved={hideSolved}
            onHideSolvedChange={(value) => setParam('status', value ? 'unsolved' : null)}
            onMobileClose={() => setCatalogOpen(false)}
            onQueryChange={(value) => setParam('q', value || null)}
            onSelect={selectChallenge}
            query={query}
            selectedId={selectedId}
            solvedTypes={solvedTypes}
          />
        </aside>

        <main className={styles.challengeMain}>
          {!displayedChallenge && !challengeError ? (
            <DataState description="正在读取题面、附件和实例状态。" loading title="题目加载中" />
          ) : challengeError || !displayedChallenge ? (
            <DataState description="题目不存在、尚未开放或当前队伍没有访问权限。" title="题目加载失败" />
          ) : (
            <article className={styles.challengeArticle}>
              <header className={styles.challengeHeader}>
                <div className={styles.challengeMeta}>
                  <span>
                    <CategoryIcon size={16} />
                    {meta.label}
                  </span>
                  <span>#{displayedChallenge.id}</span>
                  {solved ? <StatusPill tone="success">已完成</StatusPill> : <StatusPill>未完成</StatusPill>}
                </div>
                <div className={styles.titleRow}>
                  <h1>{displayedChallenge.title || selectedSummary?.title || `题目 ${displayedChallenge.id}`}</h1>
                  <strong>
                    {displayedChallenge.score ?? selectedSummary?.score ?? 0}
                    <small>PTS</small>
                  </strong>
                </div>
              </header>

              <div className={styles.articleBody}>
                <MarkdownContent source={displayedChallenge.content || '暂无题目说明。'} />

                {displayedChallenge.hints?.length ? (
                  <section aria-labelledby="hint-title" className={styles.hintSection}>
                    <header>
                      <Lightbulb size={17} />
                      <h2 id="hint-title">题目提示</h2>
                    </header>
                    <div>
                      {displayedChallenge.hints.map((hint, index) => (
                        <MarkdownContent key={`${index}:${hint}`} source={hint} />
                      ))}
                    </div>
                  </section>
                ) : null}

                {attachmentUrl ? (
                  <section aria-labelledby="attachment-title" className={styles.attachmentSection}>
                    <header>
                      <span>ATTACHMENT</span>
                      <h2 id="attachment-title">题目附件</h2>
                    </header>
                    <a href={attachmentUrl} rel="noreferrer noopener" target="_blank">
                      <span>
                        <FileArchive size={21} />
                      </span>
                      <div>
                        <strong>{attachmentName(attachmentUrl)}</strong>
                        <small>{formatFileSize(displayedChallenge.context?.fileSize)}</small>
                      </div>
                      <Download size={18} />
                    </a>
                  </section>
                ) : null}

                <InstanceControl controller={instance} />

                <FlagSubmission
                  activeFlagId={activeFlagId}
                  challenge={displayedChallenge}
                  disabledReason={submitDisabledReason}
                  feedback={flagSubmission.feedbackFor(displayedChallenge.id ?? 0)}
                  onFlagChange={setActiveFlagId}
                  onSubmit={() => void submitFlag()}
                  onValueChange={setFlagValue}
                  pending={pending}
                  solved={solved}
                  solvedFlagIds={solvedFlagIds}
                  value={flagValue}
                />
              </div>
            </article>
          )}
        </main>

        <aside className={styles.contextRail}>
          <ChallengeContextRail
            notices={notices}
            rank={teamInfo.rank?.rank}
            score={teamInfo.rank?.score}
            solvedCount={teamInfo.rank?.solvedCount}
            submissions={flagSubmission.submissions}
            teamName={teamInfo.rank?.name || game.teamName}
          />
        </aside>
      </div>
    </div>
  )
}

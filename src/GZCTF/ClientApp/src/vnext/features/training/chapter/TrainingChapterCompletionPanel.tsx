import { BookOpen, Check, CheckCircle2, Circle } from 'lucide-react'
import { ActionButton, InlineFeedback } from '../../../shared/Interaction'
import { StatusPill } from '../../../shared/Primitives'
import styles from './TrainingChapterPage.module.css'
import { trainingChapterProgress } from './trainingChapterDomain'

interface TrainingChapterCompletionPanelProps {
  progress: ReturnType<typeof trainingChapterProgress>
  completing: boolean
  feedback: { tone: 'success' | 'danger'; message: string } | null
  onComplete: () => Promise<void>
}

export function TrainingChapterCompletionPanel({
  progress,
  completing,
  feedback,
  onComplete,
}: TrainingChapterCompletionPanelProps) {
  return (
    <section className={styles.completionSection} id="chapter-completion">
      <header className={styles.sectionHeader}>
        <div>
          <span>CHAPTER COMPLETION</span>
          <h2>章节完成</h2>
          <p>平台会按教师配置的阅读、实验和理论练习条件确认章节状态。</p>
        </div>
        <StatusPill tone={progress.completed ? 'success' : progress.blockingConditions ? 'info' : 'warning'}>
          {progress.completed ? '已完成' : progress.blockingConditions ? '可以确认' : '条件未满足'}
        </StatusPill>
      </header>

      <div className={styles.conditionList}>
        <div
          className={progress.contentSatisfied || progress.completed ? styles.conditionMet : styles.conditionPending}
        >
          {progress.contentSatisfied || progress.completed ? <CheckCircle2 size={18} /> : <Circle size={18} />}
          <span>
            <strong>章节阅读</strong>
            <small>
              {progress.contentSatisfied || progress.completed ? '阅读状态已记录' : '点击完成按钮时确认已经阅读正文'}
            </small>
          </span>
        </div>
        {progress.requiredChallengeCount > 0 ? (
          <div className={progress.challengesSatisfied ? styles.conditionMet : styles.conditionPending}>
            {progress.challengesSatisfied ? <CheckCircle2 size={18} /> : <Circle size={18} />}
            <span>
              <strong>必做实验</strong>
              <small>
                已完成 {progress.solvedChallengeCount} / {progress.requiredChallengeCount}
              </small>
            </span>
          </div>
        ) : null}
        {progress.theoryRequired ? (
          <div className={progress.theorySatisfied ? styles.conditionMet : styles.conditionPending}>
            {progress.theorySatisfied ? <CheckCircle2 size={18} /> : <Circle size={18} />}
            <span>
              <strong>课后练习</strong>
              <small>
                {progress.theorySatisfied
                  ? `已达到 ${progress.theoryRate}% 要求`
                  : progress.completed
                    ? `当前成绩未达到 ${progress.theoryRate}%，章节已历史完成`
                    : `需要达到 ${progress.theoryRate}%`}
              </small>
            </span>
          </div>
        ) : null}
      </div>

      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
      <div className={styles.completionActions}>
        <ActionButton
          disabled={progress.completed || completing || !progress.blockingConditions}
          icon={progress.completed ? <Check size={17} /> : <BookOpen size={17} />}
          onClick={() => void onComplete()}
          tone="primary"
          type="button"
        >
          {progress.completed
            ? '章节已完成'
            : completing
              ? '正在确认'
              : progress.contentSatisfied
                ? '标记章节完成'
                : '确认阅读并完成'}
        </ActionButton>
        {!progress.completed && !progress.blockingConditions ? <small>请先完成上方标记为未满足的条件。</small> : null}
      </div>
    </section>
  )
}

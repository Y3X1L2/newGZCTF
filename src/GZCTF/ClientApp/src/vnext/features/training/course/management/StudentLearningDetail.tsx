import { memo } from 'react'
import { TheoryAnswerSheetStatus, TrainingCourseStudentLearningDetailModel } from '@Api'
import { StatusPill } from '../../../../shared/Primitives'
import styles from './CoursePeoplePanels.module.css'

export const StudentLearningDetail = memo(function StudentLearningDetail({
  detail,
}: {
  detail: TrainingCourseStudentLearningDetailModel
}) {
  return (
    <div className={styles.learningDetail}>
      <div className={styles.summaryGrid}>
        <div>
          <strong>
            {detail.completedChapterCount ?? 0} / {detail.totalChapterCount ?? 0}
          </strong>
          <span>章节完成</span>
        </div>
        <div>
          <strong>
            {detail.challengeSolvedCount ?? 0} / {detail.challengeTotalCount ?? 0}
          </strong>
          <span>实验完成</span>
        </div>
        <div>
          <strong>
            {detail.theoryPassedCount ?? 0} / {detail.theoryTotalCount ?? 0}
          </strong>
          <span>理论通过</span>
        </div>
        <div>
          <strong>
            {detail.theoryScore ?? 0} / {detail.theoryMaxScore ?? 0}
          </strong>
          <span>理论得分</span>
        </div>
      </div>
      <div className={styles.chapterDetails}>
        {(detail.chapters ?? []).map((chapter) => (
          <article key={chapter.chapterId}>
            <header>
              <div>
                <strong>{chapter.title || `章节 ${chapter.chapterId}`}</strong>
                <small>阅读进度 {chapter.readPercent ?? 0}%</small>
              </div>
              <StatusPill tone={chapter.completedAt ? 'success' : 'neutral'}>
                {chapter.completedAt ? '已完成' : '未完成'}
              </StatusPill>
            </header>
            {chapter.theory ? (
              <section>
                <h4>理论作业</h4>
                <p>
                  {chapter.theory.status === TheoryAnswerSheetStatus.Submitted
                    ? `得分 ${chapter.theory.score ?? 0} / ${chapter.theory.totalScore ?? 0}`
                    : '尚未最终提交'}
                </p>
                {(chapter.theory.answers ?? []).length ? (
                  <div className={styles.answerList}>
                    {chapter.theory.answers?.map((answer, index) => (
                      <div key={answer.questionId ?? index}>
                        <span>
                          {index + 1}. {answer.title || answer.content || '未命名题目'}
                        </span>
                        <StatusPill tone={answer.isCorrect ? 'success' : 'warning'}>
                          {answer.isCorrect ? '正确' : '错误'}
                        </StatusPill>
                      </div>
                    ))}
                  </div>
                ) : null}
              </section>
            ) : null}
            {(chapter.challenges ?? []).length ? (
              <section>
                <h4>实例题</h4>
                <div className={styles.challengeList}>
                  {chapter.challenges?.map((challenge) => (
                    <div key={challenge.exerciseChallengeId}>
                      <span>
                        {challenge.displayTitle || challenge.title || `题目 ${challenge.exerciseChallengeId}`}
                      </span>
                      <StatusPill tone={challenge.solved ? 'success' : 'neutral'}>
                        {challenge.solved ? '已完成' : '未完成'}
                      </StatusPill>
                    </div>
                  ))}
                </div>
              </section>
            ) : null}
          </article>
        ))}
      </div>
    </div>
  )
})

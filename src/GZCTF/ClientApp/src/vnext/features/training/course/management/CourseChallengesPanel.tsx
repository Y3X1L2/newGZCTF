import { Box, FileArchive, Pencil, Plus, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router'
import api, { EnvironmentType, TrainingCourseModel } from '@Api'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../../shared/Interaction'
import { DataState, StatusPill } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import styles from './CourseChallengesPanel.module.css'
import { CourseManagementActionLink, CourseManagementPanelHeader } from './CourseManagementPanelHeader'

export function CourseChallengesPanel({
  course,
  onCourseChanged,
}: {
  course: TrainingCourseModel
  onCourseChanged: () => Promise<unknown>
}) {
  const courseId = course.id ?? 0
  const challenges = [...(course.challenges ?? [])].sort((left, right) => (left.order ?? 0) - (right.order ?? 0))
  const [deleteId, setDeleteId] = useState<number | null>(null)
  const [saving, setSaving] = useState(false)
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'danger'; message: string } | null>(null)

  const remove = async () => {
    if (!deleteId || saving) return
    setSaving(true)
    setFeedback(null)
    try {
      await api.trainingCourseAdmin.trainingCourseAdminRemoveChallenge(courseId, deleteId)
      await onCourseChanged()
      setDeleteId(null)
      setFeedback({ tone: 'success', message: '课程题目已删除。' })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '课程题目删除失败。') })
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className={styles.panel}>
      <CourseManagementPanelHeader
        actions={
          <CourseManagementActionLink icon={<Plus size={17} />} to={`/training/courses/${courseId}/challenges/new`}>
            创建题目
          </CourseManagementActionLink>
        }
        description="课程题目与比赛题目相互隔离，可绑定当前课程的环境模板和一个静态附件。"
        eyebrow="COURSE CHALLENGES"
        title="课程题目"
      />
      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
      {challenges.length ? (
        <div className={styles.challengeList}>
          {challenges.map((challenge) => (
            <article key={challenge.exerciseChallengeId}>
              <span className={styles.challengeIcon}>
                {challenge.environment === EnvironmentType.None ? <FileArchive size={18} /> : <Box size={18} />}
              </span>
              <div>
                <strong>{challenge.displayTitle || challenge.title || `题目 ${challenge.exerciseChallengeId}`}</strong>
                <small>
                  #{challenge.exerciseChallengeId} · {challenge.category} · {challenge.type}
                </small>
              </div>
              <StatusPill tone={challenge.isRequired ? 'warning' : 'neutral'}>
                {challenge.isRequired ? '必做' : '选做'}
              </StatusPill>
              <span className={styles.chapterLabel}>
                {challenge.chapterId ? `章节 #${challenge.chapterId}` : '未绑定章节'}
              </span>
              <div className={styles.rowActions}>
                <Link
                  aria-label="编辑课程题目"
                  title="编辑课程题目"
                  to={`/training/courses/${courseId}/challenges/${challenge.exerciseChallengeId}/edit`}
                >
                  <Pencil size={16} />
                </Link>
                <button
                  aria-label="删除课程题目"
                  onClick={() => setDeleteId(challenge.exerciseChallengeId ?? null)}
                  title="删除课程题目"
                  type="button"
                >
                  <Trash2 size={16} />
                </button>
              </div>
            </article>
          ))}
        </div>
      ) : (
        <DataState description="创建题目后可在章节编辑和课程题目管理中使用。" title="暂无课程题目" />
      )}

      <VNextDialog
        description="删除后章节将不再显示此题，已有提交记录可能使后端拒绝删除。"
        eyebrow="DELETE CHALLENGE"
        footer={
          <>
            <ActionButton onClick={() => setDeleteId(null)} type="button">
              取消
            </ActionButton>
            <ActionButton
              disabled={saving}
              icon={<Trash2 size={16} />}
              onClick={() => void remove()}
              tone="danger"
              type="button"
            >
              {saving ? '正在删除' : '确认删除'}
            </ActionButton>
          </>
        }
        onClose={() => setDeleteId(null)}
        open={Boolean(deleteId)}
        title="删除课程题目"
      >
        <InlineFeedback tone="danger">题目内容、Flag 配置和附件绑定将被删除。</InlineFeedback>
      </VNextDialog>
    </section>
  )
}

import { Search, Trash2, UserPlus } from 'lucide-react'
import { FormEvent, useState } from 'react'
import { Link } from 'react-router'
import { TrainingCourseModel, TrainingCourseTeacherCandidateModel, TrainingCourseTeacherRole } from '@Api'
import { SelectField, TextField } from '../../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../../shared/Interaction'
import { StatusPill } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { trainingAdminApi } from '../../admin/trainingAdminApi'
import { formatTrainingDate } from '../../training'
import { CourseManagementPanelHeader } from './CourseManagementPanelHeader'
import styles from './CoursePeoplePanels.module.css'

export function CourseTeachersPanel({
  course,
  onCourseChanged,
}: {
  course: TrainingCourseModel
  onCourseChanged: () => Promise<unknown>
}) {
  const courseId = course.id ?? 0
  const [addOpen, setAddOpen] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [candidates, setCandidates] = useState<TrainingCourseTeacherCandidateModel[]>([])
  const [selectedTeacherId, setSelectedTeacherId] = useState('')
  const [searching, setSearching] = useState(false)
  const [saving, setSaving] = useState(false)
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'danger'; message: string } | null>(null)

  const searchCandidates = async (event?: FormEvent) => {
    event?.preventDefault()
    setSearching(true)
    setFeedback(null)
    try {
      const response = await trainingAdminApi.findTeacherCandidates(courseId, keyword.trim() || null)
      const available = response.filter((candidate) => !candidate.alreadyTeacher)
      setCandidates(available)
      setSelectedTeacherId(available[0]?.userId ?? '')
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '教师搜索失败。') })
    } finally {
      setSearching(false)
    }
  }

  const addTeacher = async () => {
    if (!selectedTeacherId || saving) return
    setSaving(true)
    try {
      await trainingAdminApi.addTeacher(courseId, selectedTeacherId)
      await onCourseChanged()
      setAddOpen(false)
      setFeedback({ tone: 'success', message: '共同教师已添加。' })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '添加教师失败。') })
    } finally {
      setSaving(false)
    }
  }

  const removeTeacher = async (teacherId?: string) => {
    if (!teacherId || saving) return
    setSaving(true)
    try {
      await trainingAdminApi.removeTeacher(courseId, teacherId)
      await onCourseChanged()
      setFeedback({ tone: 'success', message: '共同教师已移除。' })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '移除教师失败。') })
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className={styles.panel}>
      <CourseManagementPanelHeader
        actions={
          course.canManageTeachers ? (
            <ActionButton icon={<UserPlus size={17} />} onClick={() => setAddOpen(true)} type="button">
              添加教师
            </ActionButton>
          ) : null
        }
        description="共同教师可以维护课程内容；只有课程创建者和管理员可以调整教师成员。"
        eyebrow="COURSE FACULTY"
        title="授课教师"
      />
      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
      <div className={styles.peopleList}>
        {(course.teachers ?? []).map((teacher) => (
          <article key={teacher.teacherId}>
            <div>
              {teacher.teacherId ? (
                <Link to={`/users/${teacher.teacherId}`}>{teacher.realName || teacher.userName || '未命名教师'}</Link>
              ) : (
                <strong>{teacher.realName || teacher.userName || '未命名教师'}</strong>
              )}
              <small>{teacher.userName || teacher.teacherId}</small>
            </div>
            <StatusPill tone={teacher.role === TrainingCourseTeacherRole.Owner ? 'info' : 'neutral'}>
              {teacher.role === TrainingCourseTeacherRole.Owner ? '课程负责人' : '共同教师'}
            </StatusPill>
            <span>{formatTrainingDate(teacher.assignedAt)}</span>
            {course.canManageTeachers && teacher.role !== TrainingCourseTeacherRole.Owner ? (
              <button
                aria-label="移除教师"
                disabled={saving}
                onClick={() => void removeTeacher(teacher.teacherId)}
                title="移除教师"
                type="button"
              >
                <Trash2 size={16} />
              </button>
            ) : (
              <span />
            )}
          </article>
        ))}
      </div>

      <VNextDialog
        description="仅教师及以上角色会出现在搜索结果中。"
        eyebrow="ADD FACULTY"
        footer={
          <>
            <ActionButton onClick={() => setAddOpen(false)} type="button">
              取消
            </ActionButton>
            <ActionButton
              disabled={!selectedTeacherId || saving}
              icon={<UserPlus size={16} />}
              onClick={() => void addTeacher()}
              tone="primary"
              type="button"
            >
              {saving ? '正在添加' : '添加教师'}
            </ActionButton>
          </>
        }
        onClose={() => setAddOpen(false)}
        open={addOpen}
        title="添加共同教师"
        wide
      >
        <form className={styles.searchForm} onSubmit={(event) => void searchCandidates(event)}>
          <TextField label="搜索教师" onValueChange={setKeyword} placeholder="输入姓名或用户名" value={keyword} />
          <ActionButton disabled={searching} icon={<Search size={16} />} type="submit">
            {searching ? '搜索中' : '搜索'}
          </ActionButton>
        </form>
        <SelectField label="选择教师" onValueChange={setSelectedTeacherId} value={selectedTeacherId}>
          <option value="">请选择搜索结果</option>
          {candidates.map((candidate) => (
            <option key={candidate.userId} value={candidate.userId}>
              {candidate.realName || candidate.userName} · {candidate.userName}
            </option>
          ))}
        </SelectField>
      </VNextDialog>
    </section>
  )
}

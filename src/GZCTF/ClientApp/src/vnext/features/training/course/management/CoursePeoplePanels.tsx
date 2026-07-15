import { Check, ChevronLeft, ChevronRight, Eye, Search, UserPlus, X } from 'lucide-react'
import { FormEvent, useMemo, useRef, useState } from 'react'
import api, {
  TrainingCourseEnrollmentStatus,
  TrainingCourseModel,
  TrainingCourseStudentCandidateModel,
  TrainingCourseStudentLearningDetailModel,
} from '@Api'
import { SelectField, TextField } from '../../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextDialog, VNextDrawer } from '../../../../shared/Interaction'
import { DataState, StatusPill } from '../../../../shared/Primitives'
import { errorMessage } from '../../../../shared/errors'
import { formatTrainingDate } from '../../training'
import { CourseManagementPanelHeader } from './CourseManagementPanelHeader'
import styles from './CoursePeoplePanels.module.css'
import { StudentLearningDetail } from './StudentLearningDetail'

const requestOptions = { revalidateOnFocus: false, shouldRetryOnError: false }
const pageSize = 8

function enrollmentLabel(status?: TrainingCourseEnrollmentStatus) {
  if (status === TrainingCourseEnrollmentStatus.Approved) return '已通过'
  if (status === TrainingCourseEnrollmentStatus.Rejected) return '已拒绝'
  if (status === TrainingCourseEnrollmentStatus.Cancelled) return '已撤回'
  return '待审核'
}

function enrollmentTone(status?: TrainingCourseEnrollmentStatus): 'success' | 'warning' | 'neutral' {
  if (status === TrainingCourseEnrollmentStatus.Approved) return 'success'
  if (status === TrainingCourseEnrollmentStatus.Pending) return 'warning'
  return 'neutral'
}

export function CourseStudentsPanel({ course }: { course: TrainingCourseModel }) {
  const courseId = course.id ?? 0
  const enrollmentsRequest = api.trainingCourseAdmin.useTrainingCourseAdminEnrollments(
    courseId,
    requestOptions,
    Boolean(course.canManageEnrollments && courseId)
  )
  const summariesRequest = api.trainingCourseAdmin.useTrainingCourseAdminLearningSummaries(
    courseId,
    requestOptions,
    Boolean(course.canManageEnrollments && courseId)
  )
  const [page, setPage] = useState(1)
  const [addOpen, setAddOpen] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [candidates, setCandidates] = useState<TrainingCourseStudentCandidateModel[]>([])
  const [selectedUserId, setSelectedUserId] = useState('')
  const [searching, setSearching] = useState(false)
  const [saving, setSaving] = useState(false)
  const [detail, setDetail] = useState<TrainingCourseStudentLearningDetailModel | null>(null)
  const [detailOpen, setDetailOpen] = useState(false)
  const [detailLoading, setDetailLoading] = useState(false)
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'danger'; message: string } | null>(null)
  const detailCacheRef = useRef(new Map<string, TrainingCourseStudentLearningDetailModel>())
  const detailRequestIdRef = useRef(0)

  const enrollments = enrollmentsRequest.data ?? []
  const summaries = summariesRequest.data ?? []
  const summaryMap = useMemo(() => new Map(summaries.map((item) => [item.userId, item])), [summaries])
  const rows = useMemo(
    () =>
      enrollments.map((enrollment) => ({
        enrollment,
        learning: summaryMap.get(enrollment.userId),
      })),
    [enrollments, summaryMap]
  )
  const pageCount = Math.max(1, Math.ceil(rows.length / pageSize))
  const visibleRows = rows.slice((Math.min(page, pageCount) - 1) * pageSize, Math.min(page, pageCount) * pageSize)

  const refresh = async () => {
    await Promise.all([enrollmentsRequest.mutate(), summariesRequest.mutate()])
  }

  const review = async (userId: string | undefined, status: TrainingCourseEnrollmentStatus) => {
    if (!userId || saving) return
    setSaving(true)
    setFeedback(null)
    try {
      await api.trainingCourseAdmin.trainingCourseAdminReviewEnrollment(courseId, userId, { status })
      await refresh()
      setFeedback({
        tone: 'success',
        message: status === TrainingCourseEnrollmentStatus.Approved ? '报名已通过。' : '报名已拒绝。',
      })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '报名状态更新失败。') })
    } finally {
      setSaving(false)
    }
  }

  const searchCandidates = async (event?: FormEvent) => {
    event?.preventDefault()
    setSearching(true)
    setFeedback(null)
    try {
      const response = await api.trainingCourseAdmin.trainingCourseAdminStudentCandidates(courseId, {
        keyword: keyword.trim() || null,
      })
      const available = (response.data ?? []).filter((candidate) => !candidate.alreadyEnrolled)
      setCandidates(available)
      setSelectedUserId(available[0]?.userId ?? '')
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '学员搜索失败。') })
    } finally {
      setSearching(false)
    }
  }

  const addStudent = async () => {
    if (!selectedUserId || saving) return
    setSaving(true)
    setFeedback(null)
    try {
      await api.trainingCourseAdmin.trainingCourseAdminAddEnrollment(courseId, { userId: selectedUserId })
      await refresh()
      setAddOpen(false)
      setCandidates([])
      setSelectedUserId('')
      setFeedback({ tone: 'success', message: '学员已加入课程。' })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '添加学员失败。') })
    } finally {
      setSaving(false)
    }
  }

  const openDetail = async (userId?: string) => {
    if (!userId) return
    const requestId = detailRequestIdRef.current + 1
    detailRequestIdRef.current = requestId
    const cachedDetail = detailCacheRef.current.get(userId) ?? null
    setDetailOpen(true)
    setDetail(cachedDetail)
    setDetailLoading(!cachedDetail)
    try {
      const response = await api.trainingCourseAdmin.trainingCourseAdminStudentLearningDetail(courseId, userId)
      if (detailRequestIdRef.current !== requestId) return
      const nextDetail = response.data ?? null
      if (nextDetail) detailCacheRef.current.set(userId, nextDetail)
      setDetail(nextDetail)
    } catch (requestError) {
      if (detailRequestIdRef.current !== requestId) return
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '学员学习详情加载失败。') })
      if (!cachedDetail) setDetailOpen(false)
    } finally {
      if (detailRequestIdRef.current === requestId) setDetailLoading(false)
    }
  }

  if (!course.canManageEnrollments) {
    return <DataState description="当前账户没有查看课程学员信息的权限。" title="无法管理学员" />
  }

  return (
    <section className={styles.panel}>
      <CourseManagementPanelHeader
        actions={
          <ActionButton icon={<UserPlus size={17} />} onClick={() => setAddOpen(true)} type="button">
            添加学员
          </ActionButton>
        }
        description="审核报名并查看每名学员的章节、实验和理论作业完成情况。"
        eyebrow="LEARNER MANAGEMENT"
        title="学员与学习状态"
      />
      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
      {!enrollmentsRequest.data && !enrollmentsRequest.error ? (
        <DataState description="正在读取报名和学习记录。" loading title="学员数据加载中" />
      ) : enrollmentsRequest.error ? (
        <DataState description="学员接口暂时不可用。" title="学员数据加载失败" />
      ) : rows.length ? (
        <>
          <div className={styles.tableWrap}>
            <table>
              <thead>
                <tr>
                  <th>学员</th>
                  <th>报名</th>
                  <th>章节</th>
                  <th>实验</th>
                  <th>理论</th>
                  <th>最近活动</th>
                  <th aria-label="操作" />
                </tr>
              </thead>
              <tbody>
                {visibleRows.map(({ enrollment, learning }) => (
                  <tr key={enrollment.userId}>
                    <td>
                      <strong>{enrollment.realName || enrollment.userName || '未命名学员'}</strong>
                      <small>{enrollment.stdNumber || enrollment.userName || '-'}</small>
                    </td>
                    <td>
                      <StatusPill tone={enrollmentTone(enrollment.status)}>
                        {enrollmentLabel(enrollment.status)}
                      </StatusPill>
                    </td>
                    <td>
                      {learning?.completedChapterCount ?? enrollment.completedChapterCount ?? 0} /{' '}
                      {learning?.totalChapterCount ?? enrollment.totalChapterCount ?? 0}
                    </td>
                    <td>
                      {learning?.challengeSolvedCount ?? 0} / {learning?.challengeTotalCount ?? 0}
                    </td>
                    <td>
                      {learning?.theoryPassedCount ?? 0} / {learning?.theoryTotalCount ?? 0}
                    </td>
                    <td>{formatTrainingDate(learning?.lastActivityAt ?? enrollment.progressUpdatedAt)}</td>
                    <td>
                      <div className={styles.rowActions}>
                        <button
                          aria-label="查看学习详情"
                          onClick={() => void openDetail(enrollment.userId)}
                          title="查看学习详情"
                          type="button"
                        >
                          <Eye size={16} />
                        </button>
                        {enrollment.status === TrainingCourseEnrollmentStatus.Pending ? (
                          <>
                            <button
                              aria-label="通过报名"
                              disabled={saving}
                              onClick={() => void review(enrollment.userId, TrainingCourseEnrollmentStatus.Approved)}
                              title="通过报名"
                              type="button"
                            >
                              <Check size={16} />
                            </button>
                            <button
                              aria-label="拒绝报名"
                              disabled={saving}
                              onClick={() => void review(enrollment.userId, TrainingCourseEnrollmentStatus.Rejected)}
                              title="拒绝报名"
                              type="button"
                            >
                              <X size={16} />
                            </button>
                          </>
                        ) : null}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className={styles.pagination}>
            <span>
              第 {Math.min(page, pageCount)} / {pageCount} 页，共 {rows.length} 人
            </span>
            <div>
              <button
                aria-label="上一页"
                disabled={page <= 1}
                onClick={() => setPage((current) => Math.max(1, current - 1))}
                type="button"
              >
                <ChevronLeft size={17} />
              </button>
              <button
                aria-label="下一页"
                disabled={page >= pageCount}
                onClick={() => setPage((current) => Math.min(pageCount, current + 1))}
                type="button"
              >
                <ChevronRight size={17} />
              </button>
            </div>
          </div>
        </>
      ) : (
        <DataState description="当前没有报名或被添加到课程的学员。" title="暂无课程学员" />
      )}

      <VNextDialog
        description="可按用户名、姓名、学号或邮箱搜索。"
        eyebrow="ADD LEARNER"
        footer={
          <>
            <ActionButton onClick={() => setAddOpen(false)} type="button">
              取消
            </ActionButton>
            <ActionButton
              disabled={!selectedUserId || saving}
              icon={<UserPlus size={16} />}
              onClick={() => void addStudent()}
              tone="primary"
              type="button"
            >
              {saving ? '正在添加' : '加入课程'}
            </ActionButton>
          </>
        }
        onClose={() => setAddOpen(false)}
        open={addOpen}
        title="添加课程学员"
        wide
      >
        <form className={styles.searchForm} onSubmit={(event) => void searchCandidates(event)}>
          <TextField label="搜索用户" onValueChange={setKeyword} placeholder="输入姓名、用户名或学号" value={keyword} />
          <ActionButton disabled={searching} icon={<Search size={16} />} type="submit">
            {searching ? '搜索中' : '搜索'}
          </ActionButton>
        </form>
        <SelectField label="选择学员" onValueChange={setSelectedUserId} value={selectedUserId}>
          <option value="">请选择搜索结果</option>
          {candidates.map((candidate) => (
            <option key={candidate.userId} value={candidate.userId}>
              {candidate.realName || candidate.userName} · {candidate.stdNumber || candidate.userName}
            </option>
          ))}
        </SelectField>
      </VNextDialog>

      <VNextDrawer
        description={
          detail
            ? `${detail.stdNumber || detail.userName || ''} · 最近活动 ${formatTrainingDate(detail.lastActivityAt)}`
            : undefined
        }
        eyebrow="LEARNING DETAIL"
        onClose={() => setDetailOpen(false)}
        open={detailOpen}
        title={detail?.realName || detail?.userName || '学员学习详情'}
      >
        {detailLoading ? (
          <DataState description="正在汇总章节、实验和理论记录。" loading title="详情加载中" />
        ) : detail ? (
          <StudentLearningDetail detail={detail} />
        ) : null}
      </VNextDrawer>
    </section>
  )
}

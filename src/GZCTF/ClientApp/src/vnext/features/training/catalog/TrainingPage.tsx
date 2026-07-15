import { ArrowRight, BookOpenCheck, CalendarCheck, ChevronLeft, ChevronRight, Plus, Search } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import api, { TrainingCourseModel, TrainingCourseStatus } from '@Api'
import { ActionButton, InlineFeedback } from '../../../shared/Interaction'
import { DataState, GeometricPoster, PageHeading, SectionHeading, StatusPill } from '../../../shared/Primitives'
import { useVNextPageTitle } from '../../../shared/useVNextPageTitle'
import { useCurrentAccount } from '../../account/useCurrentAccount'
import { courseProgress, courseStatusLabel, matchesScope, TrainingScope, trainingScopes } from '../training'
import { TrainingActivityCalendar } from './TrainingActivityCalendar'
import { TrainingCourseCard } from './TrainingCourseCard'
import styles from './TrainingPage.module.css'

function newestFirst(left: TrainingCourseModel, right: TrainingCourseModel) {
  return (right.lastStudiedAt ?? right.updatedAt ?? 0) - (left.lastStudiedAt ?? left.updatedAt ?? 0)
}

export function TrainingPage() {
  useVNextPageTitle('培训')
  const account = useCurrentAccount()
  const [searchParams, setSearchParams] = useSearchParams()
  const coursesRequest = api.trainingCourse.useTrainingCourseCourses({ revalidateOnFocus: false })
  const overviewRequest = api.trainingCourse.useTrainingCourseOverview({ revalidateOnFocus: false })
  const [featuredIndex, setFeaturedIndex] = useState(0)
  const [featuredPaused, setFeaturedPaused] = useState(false)
  const [checkingIn, setCheckingIn] = useState(false)
  const [feedback, setFeedback] = useState<string | null>(null)

  const courses = coursesRequest.data ?? []
  const query = (searchParams.get('q') ?? '').trim().toLocaleLowerCase('zh-CN')
  const selectedTag = searchParams.get('tag') ?? ''
  const rawScope = searchParams.get('scope') as TrainingScope | null
  const scope = trainingScopes.some((item) => item.id === rawScope) ? (rawScope as TrainingScope) : 'all'

  const tags = useMemo(
    () =>
      [...new Set(courses.flatMap((course) => course.tags ?? []).filter(Boolean))].sort((a, b) =>
        a.localeCompare(b, 'zh-CN')
      ),
    [courses]
  )

  const featuredCourses = useMemo(() => {
    const published = courses.filter((course) => course.status === TrainingCourseStatus.Published)
    return [...(published.length ? published : courses)]
      .sort((left, right) => {
        const leftProgress = courseProgress(left).percent
        const rightProgress = courseProgress(right).percent
        if (leftProgress !== rightProgress) return rightProgress - leftProgress
        return newestFirst(left, right)
      })
      .slice(0, 5)
  }, [courses])

  useEffect(() => {
    if (featuredIndex < featuredCourses.length) return
    setFeaturedIndex(0)
  }, [featuredCourses.length, featuredIndex])

  useEffect(() => {
    if (featuredPaused || featuredCourses.length <= 1) return undefined
    const timer = window.setInterval(() => setFeaturedIndex((current) => (current + 1) % featuredCourses.length), 6500)
    return () => window.clearInterval(timer)
  }, [featuredCourses.length, featuredPaused])

  const roleCourses = useMemo(() => {
    const editable = courses.filter((course) => course.canEdit).sort(newestFirst)
    if (editable.length) return { title: '授课与管理', description: '按最近更新排序', courses: editable.slice(0, 3) }
    const recent = courses.filter((course) => course.canLearn || course.lastStudiedAt).sort(newestFirst)
    return { title: '最近学习', description: '按最近访问排序', courses: recent.slice(0, 3) }
  }, [courses])

  const visibleCourses = useMemo(
    () =>
      courses.filter((course) => {
        const searchable =
          `${course.title ?? ''} ${course.summary ?? ''} ${(course.tags ?? []).join(' ')}`.toLocaleLowerCase('zh-CN')
        return (
          (!query || searchable.includes(query)) &&
          (!selectedTag || course.tags?.includes(selectedTag)) &&
          matchesScope(course, scope)
        )
      }),
    [courses, query, scope, selectedTag]
  )

  const updateParam = (key: string, value: string) => {
    const next = new URLSearchParams(searchParams)
    if (value) next.set(key, value)
    else next.delete(key)
    setSearchParams(next, { replace: true })
  }

  const checkIn = async () => {
    if (checkingIn || overviewRequest.data?.checkedInToday) return
    setCheckingIn(true)
    setFeedback(null)
    try {
      const response = await api.trainingCourse.trainingCourseCheckIn()
      await overviewRequest.mutate(response.data, { revalidate: false })
      setFeedback('今日签到已记录。')
    } catch {
      setFeedback('签到失败，请稍后重试。')
    } finally {
      setCheckingIn(false)
    }
  }

  const featured = featuredCourses[featuredIndex]
  const featuredProgress = featured ? courseProgress(featured) : null

  return (
    <div className={styles.page}>
      <PageHeading
        actions={
          <>
            {account.isTeacher ? (
              <Link className={styles.createLink} to="/training/courses/new">
                <Plus size={17} />
                创建课程
              </Link>
            ) : null}
            {overviewRequest.data ? (
              <ActionButton
                disabled={checkingIn || overviewRequest.data.checkedInToday}
                icon={<CalendarCheck size={17} />}
                onClick={() => void checkIn()}
                tone={overviewRequest.data.checkedInToday ? 'secondary' : 'primary'}
                type="button"
              >
                {overviewRequest.data.checkedInToday ? '今日已签到' : checkingIn ? '签到中' : '今日签到'}
              </ActionButton>
            ) : null}
          </>
        }
        description="围绕课程持续学习，在章节中完成知识、实验和课后测试。"
        eyebrow="LEARNING CENTER"
        title="培训"
      />

      {feedback ? (
        <InlineFeedback tone={feedback.includes('失败') ? 'danger' : 'success'}>{feedback}</InlineFeedback>
      ) : null}

      {!coursesRequest.data && !coursesRequest.error ? (
        <DataState description="正在读取课程目录与学习状态。" loading title="课程加载中" />
      ) : coursesRequest.error ? (
        <DataState description="培训接口暂时不可用，请稍后刷新。" title="课程加载失败" />
      ) : (
        <>
          <div className={styles.overviewGrid}>
            {featured && featuredProgress ? (
              <section
                className={styles.featured}
                onMouseEnter={() => setFeaturedPaused(true)}
                onMouseLeave={() => setFeaturedPaused(false)}
              >
                <div className={styles.featuredPoster} key={`poster-${featured.id}`}>
                  <GeometricPoster alt={`${featured.title ?? '课程'}课程海报`} src={featured.coverUrl} tone="blue" />
                </div>
                <div className={styles.featuredBody} key={`body-${featured.id}`}>
                  <div className={styles.featuredLabel}>
                    <span>FEATURED COURSE</span>
                    <StatusPill tone="success">{courseStatusLabel(featured)}</StatusPill>
                  </div>
                  <div className={styles.featuredTags}>
                    {(featured.tags ?? []).slice(0, 4).map((tag) => (
                      <StatusPill key={tag}>{tag}</StatusPill>
                    ))}
                  </div>
                  <h2>{featured.title || `课程 ${featured.id}`}</h2>
                  <p>{featured.summary || '课程简介尚未填写。'}</p>
                  <div className={styles.featuredProgress}>
                    <div>
                      <span>当前学习进度</span>
                      <strong>{featuredProgress.percent}%</strong>
                    </div>
                    <progress aria-label="重点课程学习进度" max={100} value={featuredProgress.percent} />
                  </div>
                  <div className={styles.featuredActions}>
                    <Link to={`/training/courses/${featured.id}`}>
                      {featured.canLearn ? '继续学习' : '查看课程'}
                      <ArrowRight size={17} />
                    </Link>
                    {featuredCourses.length > 1 ? (
                      <div className={styles.carouselControls}>
                        <button
                          aria-label="上一门重点课程"
                          onClick={() =>
                            setFeaturedIndex((featuredIndex - 1 + featuredCourses.length) % featuredCourses.length)
                          }
                          type="button"
                        >
                          <ChevronLeft size={18} />
                        </button>
                        <span>
                          {featuredIndex + 1} / {featuredCourses.length}
                        </span>
                        <button
                          aria-label="下一门重点课程"
                          onClick={() => setFeaturedIndex((featuredIndex + 1) % featuredCourses.length)}
                          type="button"
                        >
                          <ChevronRight size={18} />
                        </button>
                      </div>
                    ) : null}
                  </div>
                </div>
              </section>
            ) : null}

            {overviewRequest.data ? (
              <section className={styles.activitySection}>
                <div className={styles.activityHeading}>
                  <div>
                    <span>LEARNING ACTIVITY</span>
                    <h2>学习活动</h2>
                    <p>最近四个月的课程阅读、章节完成、实验和签到记录。</p>
                  </div>
                  <dl>
                    <div>
                      <dt>平均进度</dt>
                      <dd>{overviewRequest.data.averageProgress ?? 0}%</dd>
                    </div>
                    <div>
                      <dt>完成章节</dt>
                      <dd>{overviewRequest.data.completedChapterCount ?? 0}</dd>
                    </div>
                    <div>
                      <dt>签到天数</dt>
                      <dd>{overviewRequest.data.checkInDays ?? 0}</dd>
                    </div>
                    <div>
                      <dt>连续签到</dt>
                      <dd>{overviewRequest.data.currentCheckInStreak ?? 0}</dd>
                    </div>
                  </dl>
                </div>
                <TrainingActivityCalendar activity={overviewRequest.data.activity ?? []} days={119} />
              </section>
            ) : null}
          </div>

          {roleCourses.courses.length ? (
            <section>
              <SectionHeading eyebrow="RECENT" title={roleCourses.title} />
              <div className={styles.sectionNote}>{roleCourses.description}</div>
              <div className={styles.courseGrid}>
                {roleCourses.courses.map((course) => (
                  <TrainingCourseCard compact course={course} key={course.id} />
                ))}
              </div>
            </section>
          ) : null}

          <section>
            <SectionHeading eyebrow="CATALOG" title="所有课程" />
            <div className={styles.toolbar}>
              <label className={styles.search}>
                <Search size={17} />
                <input
                  aria-label="搜索课程"
                  onChange={(event) => updateParam('q', event.currentTarget.value)}
                  placeholder="搜索课程名称、摘要或标签"
                  type="search"
                  value={searchParams.get('q') ?? ''}
                />
              </label>
              <div aria-label="课程范围" className={styles.scopeFilters}>
                {trainingScopes.map((item) => (
                  <button
                    className={scope === item.id ? styles.filterActive : styles.filter}
                    key={item.id}
                    onClick={() => updateParam('scope', item.id === 'all' ? '' : item.id)}
                    type="button"
                  >
                    {item.label}
                  </button>
                ))}
              </div>
              {tags.length ? (
                <label className={styles.tagFilter}>
                  <span>课程标签</span>
                  <select onChange={(event) => updateParam('tag', event.currentTarget.value)} value={selectedTag}>
                    <option value="">全部标签</option>
                    {tags.map((tag) => (
                      <option key={tag} value={tag}>
                        {tag}
                      </option>
                    ))}
                  </select>
                </label>
              ) : null}
              <span className={styles.resultCount}>{visibleCourses.length} 门课程</span>
            </div>

            {visibleCourses.length ? (
              <div className={styles.courseGrid}>
                {visibleCourses.map((course) => (
                  <TrainingCourseCard course={course} key={course.id} />
                ))}
              </div>
            ) : (
              <DataState description="调整搜索、范围或标签后重试。" title="没有符合条件的课程" />
            )}
          </section>

          <footer className={styles.pageFooter}>
            <BookOpenCheck size={17} />
            课程内容、实验环境和理论练习均由平台统一记录学习状态。
          </footer>
        </>
      )}
    </div>
  )
}

import {
  ArrowRight,
  BookOpen,
  CalendarClock,
  CheckCircle2,
  ChevronRight,
  CircleUserRound,
  GraduationCap,
  Play,
  Trophy,
} from 'lucide-react'
import { ReactNode, useMemo } from 'react'
import { Link } from 'react-router'
import { getPrimarySlogan } from '@Utils/Brand'
import { useConfig } from '@Hooks/useConfig'
import api, { TrainingActivityPointModel, TrainingCourseModel, TrainingCourseProgressStatus } from '@Api'
import { DataState, GeometricPoster, SectionHeading, StatusPill } from '../../shared/Primitives'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { useCurrentAccount } from '../account/useCurrentAccount'
import {
  formatGameRange,
  gameStatusLabel,
  gameStatusTone,
  participationLabel,
  useGameCatalog,
} from '../games/gameCatalog'
import styles from './HomePage.module.css'

interface ContinueItem {
  id: string
  title: string
  subtitle: string
  route: string
  meta: string
  icon: ReactNode
  tone: 'green' | 'blue' | 'orange'
}

function courseProgress(course: TrainingCourseModel) {
  if (course.progressStatus === TrainingCourseProgressStatus.Completed) return '已完成'
  if (course.progressStatus === TrainingCourseProgressStatus.Learning) {
    return `${course.completedChapterCount ?? 0} / ${course.totalChapterCount ?? 0} 章`
  }
  return '尚未开始'
}

function activityLevel(point?: TrainingActivityPointModel) {
  if (!point) return 0
  const value =
    (point.studyActions ?? 0) +
    (point.completedChapters ?? 0) * 2 +
    (point.acceptedChallenges ?? 0) * 2 +
    (point.checkedIn ? 1 : 0)
  if (value <= 0) return 0
  if (value <= 2) return 1
  if (value <= 5) return 2
  if (value <= 9) return 3
  return 4
}

function ActivityHeatmap({ points }: { points: TrainingActivityPointModel[] }) {
  const cells = useMemo(() => {
    const byDate = new Map(points.map((point) => [point.date, point]))
    const today = new Date()
    const start = new Date(today)
    start.setHours(0, 0, 0, 0)
    start.setDate(start.getDate() - 111)

    return Array.from({ length: 112 }, (_, index) => {
      const date = new Date(start)
      date.setDate(start.getDate() + index)
      const key = date.toISOString().slice(0, 10)
      return { key, level: activityLevel(byDate.get(key)) }
    })
  }, [points])

  return (
    <div className={styles.heatmap} aria-label="最近 16 周学习活跃度">
      {cells.map((cell) => (
        <span data-level={cell.level} key={cell.key} title={cell.key} />
      ))}
    </div>
  )
}

function OrchestrationScene() {
  return (
    <div className={styles.scene} aria-hidden="true">
      <span className={styles.scenePlaneA} />
      <span className={styles.scenePlaneB} />
      <span className={styles.scenePlaneC} />
      <span className={styles.sceneRouteA}>
        <i />
      </span>
      <span className={styles.sceneRouteB}>
        <i />
      </span>
      <span className={styles.sceneNodeA} />
      <span className={styles.sceneNodeB} />
      <span className={styles.sceneNodeC} />
    </div>
  )
}

function ContinueCard({ item }: { item: ContinueItem }) {
  const toneClass = {
    green: styles.continueCardGreen,
    blue: styles.continueCardBlue,
    orange: styles.continueCardOrange,
  }[item.tone]
  return (
    <Link className={`${styles.continueCard} ${toneClass}`} to={item.route}>
      <span className={styles.continueIcon}>{item.icon}</span>
      <span className={styles.continueCopy}>
        <strong>{item.title}</strong>
        <small>{item.subtitle}</small>
      </span>
      <span className={styles.continueMeta}>{item.meta}</span>
      <ChevronRight aria-hidden="true" size={17} />
    </Link>
  )
}

export function HomePage() {
  const { config } = useConfig()
  const account = useCurrentAccount()
  const gameCatalog = useGameCatalog()
  const posts = api.info.useInfoGetLatestPosts({
    refreshInterval: 5 * 60 * 1000,
    revalidateOnFocus: false,
  })
  const courses = api.trainingCourse.useTrainingCourseCourses({
    refreshInterval: 5 * 60 * 1000,
    revalidateOnFocus: false,
    shouldRetryOnError: false,
  })
  const overview = api.trainingCourse.useTrainingCourseOverview(
    {
      refreshInterval: 5 * 60 * 1000,
      revalidateOnFocus: false,
      shouldRetryOnError: false,
    },
    account.isAuthenticated
  )

  useVNextPageTitle()

  const ongoingGames = gameCatalog.games?.filter((game) => game.status === 'ongoing') ?? []
  const upcomingGames = gameCatalog.games?.filter((game) => game.status === 'upcoming') ?? []
  const featuredGames = [...ongoingGames, ...upcomingGames].slice(0, 2)
  const recentCourses = useMemo(
    () =>
      [...(courses.data ?? [])]
        .filter((course) => course.canLearn || course.status === 'Published')
        .sort(
          (left, right) => (right.lastStudiedAt ?? right.updatedAt ?? 0) - (left.lastStudiedAt ?? left.updatedAt ?? 0)
        )
        .slice(0, 3),
    [courses.data]
  )

  const continueItems = useMemo<ContinueItem[]>(() => {
    const items: ContinueItem[] = []
    const activeGame = ongoingGames[0]
    const activeCourse = recentCourses.find((course) => (course.lastStudiedAt ?? 0) > 0)

    if (activeGame) {
      items.push({
        id: `game-${activeGame.id}`,
        title: activeGame.title || `赛事 ${activeGame.id}`,
        subtitle: '正在进行的安全演练',
        route: `/games/${activeGame.id}`,
        meta: formatGameRange(activeGame),
        icon: <Trophy size={18} />,
        tone: 'green',
      })
    }

    if (activeCourse?.id) {
      items.push({
        id: `course-${activeCourse.id}`,
        title: activeCourse.title || `课程 ${activeCourse.id}`,
        subtitle: activeCourse.summary || '继续最近学习的课程',
        route: `/training/courses/${activeCourse.id}`,
        meta: courseProgress(activeCourse),
        icon: <GraduationCap size={18} />,
        tone: 'blue',
      })
    }

    if (items.length < 3 && upcomingGames[0]) {
      const game = upcomingGames[0]
      items.push({
        id: `upcoming-${game.id}`,
        title: game.title || `赛事 ${game.id}`,
        subtitle: '下一场即将开始的赛事',
        route: `/games/${game.id}`,
        meta: formatGameRange(game),
        icon: <CalendarClock size={18} />,
        tone: 'orange',
      })
    }

    return items
  }, [ongoingGames, recentCourses, upcomingGames])

  const primaryRoute = continueItems[0]?.route ?? '/games'
  const displayName = account.user?.realName || account.user?.userName
  const activity = overview.data?.activity ?? []

  return (
    <div className={styles.page}>
      <section className={styles.brandBand}>
        <div className={styles.brandCopy}>
          <span className={styles.eyebrow}>SECURITY ORCHESTRATION / LIVE</span>
          <h1>
            YINYU <span>安全综合演练平台</span>
          </h1>
          <p>{getPrimarySlogan(config.slogan)}</p>
          {displayName ? <small>欢迎回来，{displayName}</small> : <small>赛事、课程与演练环境统一编排</small>}
          <div className={styles.brandActions}>
            <Link className={styles.primaryAction} to={primaryRoute}>
              <Play size={17} />
              {continueItems.length > 0 ? '继续进行' : '浏览赛事'}
            </Link>
            <Link className={styles.secondaryAction} to="/training">
              浏览课程
              <ArrowRight size={17} />
            </Link>
          </div>
        </div>
        <OrchestrationScene />
      </section>

      {(continueItems.length > 0 || account.isAuthenticated) && (
        <section className={styles.continueSection}>
          <SectionHeading eyebrow="CONTINUE" title="继续进行" />
          {continueItems.length > 0 ? (
            <div className={styles.continueGrid}>
              {continueItems.map((item) => (
                <ContinueCard item={item} key={item.id} />
              ))}
            </div>
          ) : (
            <DataState description="参加赛事或开始课程后，这里会显示可直接继续的任务。" title="当前没有待继续项目" />
          )}
        </section>
      )}

      <section className={styles.metricStrip} aria-label="平台概览">
        <div>
          <span className={styles.metricIcon}>
            <Trophy size={18} />
          </span>
          <small>公开赛事</small>
          <strong>{gameCatalog.games?.length ?? '--'}</strong>
          <p>{ongoingGames.length} 场正在进行</p>
        </div>
        <div>
          <span className={styles.metricIcon}>
            <BookOpen size={18} />
          </span>
          <small>可见课程</small>
          <strong>{overview.data?.visibleCourseCount ?? courses.data?.length ?? '--'}</strong>
          <p>{overview.data ? `${overview.data.joinedCourseCount ?? 0} 门已加入` : '公开课程目录'}</p>
        </div>
        {account.isAuthenticated ? (
          <div>
            <span className={styles.metricIcon}>
              <CheckCircle2 size={18} />
            </span>
            <small>完成章节</small>
            <strong>{overview.data?.completedChapterCount ?? '--'}</strong>
            <p>{overview.data ? `共 ${overview.data.totalChapterCount ?? 0} 章` : '正在加载个人进度'}</p>
          </div>
        ) : (
          <div>
            <span className={styles.metricIcon}>
              <CircleUserRound size={18} />
            </span>
            <small>个人进度</small>
            <strong>登录</strong>
            <p>查看学习和参赛状态</p>
          </div>
        )}
      </section>

      <section className={styles.contentGrid}>
        <div className={styles.gamesSection}>
          <SectionHeading eyebrow="LIVE FIELDS" route="/games" routeLabel="全部赛事" title="正在进行" />
          {gameCatalog.isLoading ? (
            <DataState description="正在读取公开赛事与时间状态。" loading title="赛事加载中" />
          ) : gameCatalog.error ? (
            <DataState description="赛事接口暂时不可用，其他首页区域仍可继续访问。" title="赛事加载失败" />
          ) : featuredGames.length > 0 ? (
            <div className={styles.eventList}>
              {featuredGames.map((game) => (
                <Link className={styles.eventCard} key={game.id} to={`/games/${game.id}`}>
                  <div className={styles.eventPoster}>
                    <GeometricPoster alt={`${game.title || '赛事'}海报`} src={game.poster} />
                  </div>
                  <div className={styles.eventBody}>
                    <div className={styles.eventTopline}>
                      <StatusPill tone={gameStatusTone(game.status)}>{gameStatusLabel(game.status)}</StatusPill>
                      <span>{participationLabel(game.limit)}</span>
                    </div>
                    <h3>{game.title || `赛事 ${game.id}`}</h3>
                    <p>{game.summary || '赛事规则与介绍将在详情页中展示。'}</p>
                    <div className={styles.eventFooter}>
                      <span>{formatGameRange(game)}</span>
                      <ChevronRight size={17} />
                    </div>
                  </div>
                </Link>
              ))}
            </div>
          ) : (
            <DataState description="管理员发布赛事后会在这里按时间状态展示。" title="暂无进行中或即将开始的赛事" />
          )}
        </div>

        <aside className={styles.noticeSection}>
          <SectionHeading eyebrow="NOTICE" route="/posts" routeLabel="全部通知" title="平台通知" />
          {!posts.data && !posts.error ? (
            <DataState description="正在读取平台公告。" loading title="通知加载中" />
          ) : posts.error ? (
            <DataState description="通知接口暂时不可用。" title="通知加载失败" />
          ) : posts.data && posts.data.length > 0 ? (
            <div className={styles.noticeList}>
              {posts.data.slice(0, 5).map((post) => (
                <Link key={post.id} to={`/posts/${post.id}`}>
                  <span className={styles.noticeMarker} aria-hidden="true" />
                  <span>
                    <strong>{post.title}</strong>
                    <small>{post.summary}</small>
                  </span>
                  <time>
                    {new Intl.DateTimeFormat('zh-CN', { month: '2-digit', day: '2-digit' }).format(post.time)}
                  </time>
                </Link>
              ))}
            </div>
          ) : (
            <DataState description="平台维护、赛事安排和规则变化会在这里发布。" title="暂无平台通知" />
          )}
        </aside>
      </section>

      {account.isAuthenticated && overview.data ? (
        <section className={styles.activitySection}>
          <div>
            <span className={styles.eyebrow}>LEARNING TRACE</span>
            <h2>学习脉络</h2>
            <p>最近 16 周的课程学习、章节完成、题目通过和签到记录。</p>
          </div>
          <ActivityHeatmap points={activity} />
          <dl>
            <div>
              <dt>连续签到</dt>
              <dd>{overview.data.currentCheckInStreak ?? 0} 天</dd>
            </div>
            <div>
              <dt>平均进度</dt>
              <dd>{overview.data.averageProgress ?? 0}%</dd>
            </div>
          </dl>
        </section>
      ) : null}

      <section className={styles.courseSection}>
        <SectionHeading eyebrow="TRAINING" route="/training" routeLabel="全部课程" title="最近课程" />
        {!courses.data && !courses.error ? (
          <DataState description="正在读取课程目录。" loading title="课程加载中" />
        ) : courses.error ? (
          <DataState description="课程接口暂时不可用。" title="课程加载失败" />
        ) : recentCourses.length > 0 ? (
          <div className={styles.courseGrid}>
            {recentCourses.map((course) => (
              <Link className={styles.courseCard} key={course.id} to={`/training/courses/${course.id}`}>
                <div className={styles.courseCover}>
                  <GeometricPoster alt={`${course.title || '课程'}封面`} src={course.coverUrl} tone="blue" />
                </div>
                <div className={styles.courseBody}>
                  <div className={styles.courseTags}>
                    {(course.tags ?? []).slice(0, 3).map((tag) => (
                      <span key={tag}>{tag}</span>
                    ))}
                  </div>
                  <h3>{course.title || `课程 ${course.id}`}</h3>
                  <p>{course.summary || '课程介绍尚未填写。'}</p>
                  <div className={styles.courseFooter}>
                    <span>{course.chapterCount ?? course.totalChapterCount ?? 0} 章</span>
                    <strong>{course.canLearn ? courseProgress(course) : '查看简介'}</strong>
                  </div>
                </div>
              </Link>
            ))}
          </div>
        ) : (
          <DataState description="课程发布后会在这里展示。" title="暂无公开课程" />
        )}
      </section>
    </div>
  )
}

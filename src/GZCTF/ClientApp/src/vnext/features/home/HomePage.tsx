import {
  ArrowRight,
  BookOpen,
  CalendarClock,
  CheckCircle2,
  ChevronRight,
  GraduationCap,
  Play,
  Trophy,
} from 'lucide-react'
import { ReactNode, useMemo } from 'react'
import { Link } from 'react-router'
import { useConfig } from '@Hooks/useConfig'
import { TrainingActivityPointModel, TrainingCourseModel, TrainingCourseProgressStatus } from '@Api'
import { DataState, GeometricPoster, SectionHeading, StatusPill } from '../../shared/Primitives'
import { localDateKey } from '../../shared/dates'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { useCurrentAccount } from '../account/useCurrentAccount'
import {
  formatGameRange,
  gameStatusLabel,
  gameStatusTone,
  GameCatalogItem,
  participationLabel,
  useGameCatalog,
} from '../games/gameCatalog'
import styles from './HomePage.module.css'
import { useHomeCourses, useHomePosts, useHomeTrainingOverview } from './homeApi'
import { homePreviewEnabled } from './homePreview'

interface ContinueItem {
  id: string
  title: string
  subtitle: string
  route: string
  meta: string
  icon: ReactNode
  tone: 'green' | 'blue' | 'orange'
}

type HomeModel = {
  config: ReturnType<typeof useConfig>['config']
  account: ReturnType<typeof useCurrentAccount>
  gameCatalog: ReturnType<typeof useGameCatalog>
  posts: ReturnType<typeof useHomePosts>
  courses: ReturnType<typeof useHomeCourses>
  overview: ReturnType<typeof useHomeTrainingOverview>
  ongoingGames: GameCatalogItem[]
  upcomingGames: GameCatalogItem[]
  featuredGames: GameCatalogItem[]
  recentCourses: TrainingCourseModel[]
  continueItems: ContinueItem[]
  activity: TrainingActivityPointModel[]
  primaryRoute: string
  displayName?: string
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
    start.setDate(start.getDate() - 20)

    return Array.from({ length: 21 }, (_, index) => {
      const date = new Date(start)
      date.setDate(start.getDate() + index)
      const key = localDateKey(date)
      return { key, level: activityLevel(byDate.get(key)) }
    })
  }, [points])

  return (
    <div className={styles.heatmap} aria-label="最近 3 周学习与签到活跃度，每行 7 天">
      {cells.map((cell) => (
        <span data-level={cell.level} key={cell.key} title={cell.key} />
      ))}
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

function ContinueSection({ model }: { model: HomeModel }) {
  if (!model.continueItems.length && !model.account.isAuthenticated) return null
  return (
    <section className={styles.continueSection}>
      <SectionHeading eyebrow="CONTINUE" title="继续进行" />
      {model.continueItems.length > 0 ? (
        <div
          className={`${styles.continueGrid} ${model.continueItems.length === 1 ? styles.continueGridSingle : ''}`}
        >
          {model.continueItems.map((item) => (
            <ContinueCard item={item} key={item.id} />
          ))}
        </div>
      ) : (
        <DataState description="参加赛事或开始课程后，这里会显示可直接继续的任务。" title="当前没有待继续项目" />
      )}
    </section>
  )
}

function GamesPanel({ model, title = '公开赛事', eyebrow = 'OPEN FIELDS' }: { model: HomeModel; title?: string; eyebrow?: string }) {
  return (
    <section className={styles.gamesSection}>
      <SectionHeading eyebrow={eyebrow} route="/games" routeLabel="全部赛事" title={title} />
      {model.gameCatalog.isLoading ? (
        <DataState description="正在读取公开赛事与时间状态。" loading title="赛事加载中" />
      ) : model.gameCatalog.error ? (
        <DataState description="赛事接口暂时不可用，其他首页区域仍可继续访问。" title="赛事加载失败" />
      ) : model.featuredGames.length > 0 ? (
        <div className={styles.eventList}>
          {model.featuredGames.map((game) => (
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
    </section>
  )
}

function NoticePanel({ model, limit = 5 }: { model: HomeModel; limit?: number }) {
  return (
    <aside className={styles.noticeSection}>
      <SectionHeading eyebrow="NOTICE" route="/posts" routeLabel="全部通知" title="平台通知" />
      {!model.posts.data && !model.posts.error ? (
        <DataState description="正在读取平台公告。" loading title="通知加载中" />
      ) : model.posts.error ? (
        <DataState description="通知接口暂时不可用。" title="通知加载失败" />
      ) : model.posts.data && model.posts.data.length > 0 ? (
        <div className={styles.noticeList}>
          {model.posts.data.slice(0, limit).map((post) => (
            <Link key={post.id} to={`/posts/${post.id}`}>
              <span className={styles.noticeMarker} aria-hidden="true" />
              <span>
                <strong>{post.title}</strong>
                <small>{post.summary}</small>
              </span>
              <time>{new Intl.DateTimeFormat('zh-CN', { month: '2-digit', day: '2-digit' }).format(post.time)}</time>
            </Link>
          ))}
        </div>
      ) : (
        <DataState description="平台维护、赛事安排和规则变化会在这里发布。" title="暂无平台通知" />
      )}
    </aside>
  )
}

function MetricStrip({ model }: { model: HomeModel }) {
  const overview = model.overview.data
  return (
    <section aria-label="平台概览" className={styles.metricStrip}>
      <div>
        <span className={styles.metricIcon}><Trophy aria-hidden="true" size={18} /></span>
        <small>公开赛事</small>
        <strong>{model.gameCatalog.games?.length ?? '待补充'}</strong>
        <p>{model.gameCatalog.games ? `${model.ongoingGames.length} 场正在进行` : '赛事状态待补充'}</p>
      </div>
      <div>
        <span className={styles.metricIcon}><BookOpen aria-hidden="true" size={18} /></span>
        <small>可见课程</small>
        <strong>{overview?.visibleCourseCount ?? model.courses.data?.length ?? '待补充'}</strong>
        <p>{overview?.joinedCourseCount != null ? `${overview.joinedCourseCount} 门已加入` : '已加入课程数待补充'}</p>
      </div>
      <div>
        <span className={styles.metricIcon}><CheckCircle2 aria-hidden="true" size={18} /></span>
        <small>完成章节</small>
        <strong>{overview?.completedChapterCount ?? '待补充'}</strong>
        <p>{overview?.totalChapterCount != null ? `共 ${overview.totalChapterCount} 章` : '章节总数待补充'}</p>
      </div>
    </section>
  )
}

function ActivitySection({ model, compact = false }: { model: HomeModel; compact?: boolean }) {
  if (!model.account.isAuthenticated && !homePreviewEnabled) {
    return (
      <section className={`${styles.activitySection} ${compact ? styles.activitySectionCompact : ''}`}>
        <div className={styles.activityIntro}>
          <h2>学习脉络</h2>
          <span>个人训练记录</span>
        </div>
        <dl>
          <div><dt>最近学习 · 章节与停留位置</dt><dd>待补充</dd></div>
          <div><dt>学习进度 · 连续学习天数</dt><dd>待补充</dd></div>
          <div><dt>下一步 · 建议学习内容</dt><dd>待补充</dd></div>
        </dl>
      </section>
    )
  }
  return (
    <section className={`${styles.activitySection} ${compact ? styles.activitySectionCompact : ''}`}>
      <div className={styles.activityIntro}>
        <h2>学习脉络</h2>
        <span>最近 3 周</span>
      </div>
      {model.overview.data ? (
        <>
          <ActivityHeatmap points={model.activity} />
          <dl>
            <div>
              <dt>连续签到</dt>
              <dd>{model.overview.data.currentCheckInStreak ?? 0} 天</dd>
            </div>
            <div>
              <dt>平均进度</dt>
              <dd>{model.overview.data.averageProgress ?? 0}%</dd>
            </div>
          </dl>
        </>
      ) : (
        <p className={styles.activityStatus} role="status">
          {model.overview.error ? '学习记录暂时无法读取，请稍后刷新。' : '正在加载学习记录…'}
        </p>
      )}
    </section>
  )
}

function CoursePanel({ model, title = '最近课程', eyebrow = 'TRAINING' }: { model: HomeModel; title?: string; eyebrow?: string }) {
  return (
    <section className={styles.courseSection}>
      <SectionHeading eyebrow={eyebrow} route="/training" routeLabel="全部课程" title={title} />
      {!model.account.isAuthenticated && !homePreviewEnabled ? (
        <DataState description="登录后可查看课程和学习进度。" title="需要登录" />
      ) : !model.courses.data && !model.courses.error ? (
        <DataState description="正在读取课程目录。" loading title="课程加载中" />
      ) : model.courses.error ? (
        <DataState description="课程接口暂时不可用。" title="课程加载失败" />
      ) : model.recentCourses.length > 0 ? (
        <div className={styles.courseGrid}>
          {model.recentCourses.map((course) => (
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
                <p>当前章节：待补充</p>
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
  )
}

function PathLayout({ model }: { model: HomeModel }) {
  return (
    <div className={styles.pathLayout}>
      <section className={styles.personalHeader}>
        <div className={styles.brandCopy}>
          <span className={styles.eyebrow}>PERSONAL TRAINING / YINYU</span>
          <div className={styles.brandTitleRow}>
            <h1>
              欢迎回来，{model.displayName || '训练者'}
            </h1>
          </div>
          <p>从上次停留的位置继续，把学习进度转化为真实的安全能力。</p>
          <div className={styles.brandActions}>
            <Link className={styles.primaryAction} to={model.primaryRoute}>
              <Play size={17} />
              {model.continueItems.length > 0 ? '继续训练' : '开始训练'}
            </Link>
            <Link className={styles.secondaryAction} to="/training">
              查看学习路径
              <ArrowRight size={17} />
            </Link>
          </div>
        </div>
      </section>
      <section className={styles.memoryBand}>
        <ActivitySection model={model} />
        <MetricStrip model={model} />
      </section>
      <section className={styles.personalWorkspace}>
        <div className={styles.personalMain}>
          <ContinueSection model={model} />
          <div className={styles.learningAndGames}>
            <CoursePanel model={model} title="我的学习" eyebrow="01 / LEARNING" />
            <GamesPanel model={model} title="赛事与演练" eyebrow="02 / COMPETITIONS" />
          </div>
        </div>
        <div className={styles.personalNotices}>
          <NoticePanel limit={3} model={model} />
        </div>
      </section>
    </div>
  )
}

export function HomePage() {
  const { config } = useConfig()
  const account = useCurrentAccount()
  const gameCatalog = useGameCatalog()
  const posts = useHomePosts()
  const courses = useHomeCourses()
  const overview = useHomeTrainingOverview(account.isAuthenticated || homePreviewEnabled)

  useVNextPageTitle()

  const ongoingGames = gameCatalog.games?.filter((game) => game.status === 'ongoing') ?? []
  const upcomingGames = gameCatalog.games?.filter((game) => game.status === 'upcoming') ?? []
  const featuredGames = [...ongoingGames, ...upcomingGames]
    .slice(0, 3)
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

  const model: HomeModel = {
    config,
    account,
    gameCatalog,
    posts,
    courses,
    overview,
    ongoingGames,
    upcomingGames,
    featuredGames,
    recentCourses,
    continueItems,
    activity: overview.data?.activity ?? [],
    primaryRoute: continueItems[0]?.route ?? '/games',
    displayName: (account.user?.realName || account.user?.userName) ?? undefined,
  }

  return (
    <div className={styles.page}>
      <PathLayout model={model} />
    </div>
  )
}

import { TrainingCourseProgressStatus, TrainingCourseStatus } from '@Api'
import type { PostInfoModel, TrainingActivityPointModel, TrainingCourseModel, TrainingPersonalOverviewModel } from '@Api'
import type { GameCatalogItem } from '../games/gameCatalog'
import { localDateKey } from '../../shared/dates'

export const homePreviewEnabled = import.meta.env.DEV && import.meta.env.VITE_HOME_PREVIEW !== 'false'

const now = Date.now()

export const previewGames: GameCatalogItem[] = [
  {
    id: 9001,
    title: '蓝队初始访问演练',
    summary: '从告警研判开始，完成一次完整的安全事件响应流程。',
    poster: null,
    limit: 5,
    start: now - 2 * 60 * 60 * 1000,
    end: now + 3 * 24 * 60 * 60 * 1000,
    startsAt: now - 2 * 60 * 60 * 1000,
    endsAt: now + 3 * 24 * 60 * 60 * 1000,
    status: 'ongoing',
  },
  {
    id: 9002,
    title: 'Web 攻防基础实验',
    summary: '覆盖认证、注入和访问控制的综合练习场。',
    poster: null,
    limit: 1,
    start: now + 2 * 24 * 60 * 60 * 1000,
    end: now + 5 * 24 * 60 * 60 * 1000,
    startsAt: now + 2 * 24 * 60 * 60 * 1000,
    endsAt: now + 5 * 24 * 60 * 60 * 1000,
    status: 'upcoming',
  },
  {
    id: 9003,
    title: '云上攻防协同挑战',
    summary: '围绕身份、资产和检测响应的团队协作演练。',
    poster: null,
    limit: 5,
    start: now + 6 * 24 * 60 * 60 * 1000,
    end: now + 8 * 24 * 60 * 60 * 1000,
    startsAt: now + 6 * 24 * 60 * 60 * 1000,
    endsAt: now + 8 * 24 * 60 * 60 * 1000,
    status: 'upcoming',
  },
]

export const previewPosts: PostInfoModel[] = [
  {
    id: 'preview-notice-1',
    title: '平台演练环境已更新',
    summary: '新增蓝队响应和 Web 安全实验内容。',
    isPinned: true,
    tags: ['平台公告'],
    time: now - 2 * 60 * 60 * 1000,
  },
  {
    id: 'preview-notice-2',
    title: '本周赛事开放报名',
    summary: '团队可以提前查看规则并完成赛前准备。',
    isPinned: false,
    tags: ['赛事'],
    time: now - 24 * 60 * 60 * 1000,
  },
  {
    id: 'preview-notice-3',
    title: '培训课程目录已整理',
    summary: '按基础、进阶和实战三个阶段提供学习路径。',
    isPinned: false,
    tags: ['培训'],
    time: now - 3 * 24 * 60 * 60 * 1000,
  },
]

export const previewCourses: TrainingCourseModel[] = [
  {
    id: 9001,
    title: '安全运营与告警研判',
    summary: '掌握日志分析、告警分级和应急响应的基本方法。',
    tags: ['蓝队', '运营'],
    status: TrainingCourseStatus.Published,
    canLearn: true,
    chapterCount: 8,
    totalChapterCount: 8,
    completedChapterCount: 3,
    progressStatus: TrainingCourseProgressStatus.Learning,
    lastStudiedAt: now - 2 * 60 * 60 * 1000,
    updatedAt: now,
  },
  {
    id: 9002,
    title: 'Web 安全入门实验',
    summary: '通过真实场景理解常见 Web 漏洞与修复思路。',
    tags: ['Web', '实战'],
    status: TrainingCourseStatus.Published,
    canLearn: true,
    chapterCount: 6,
    totalChapterCount: 6,
    completedChapterCount: 0,
    progressStatus: TrainingCourseProgressStatus.NotStarted,
    updatedAt: now - 24 * 60 * 60 * 1000,
  },
  {
    id: 9003,
    title: '身份安全与访问控制',
    summary: '从权限设计到审计响应，建立身份安全基础。',
    tags: ['身份', '进阶'],
    status: TrainingCourseStatus.Published,
    canLearn: true,
    chapterCount: 7,
    totalChapterCount: 7,
    completedChapterCount: 7,
    progressStatus: TrainingCourseProgressStatus.Completed,
    updatedAt: now - 3 * 24 * 60 * 60 * 1000,
  },
]

// Development-only activity so the three-week landing-page preview shows the intended rhythm.
export const previewActivity: TrainingActivityPointModel[] = Array.from({ length: 21 }, (_, index) => {
  const date = new Date(now)
  date.setHours(0, 0, 0, 0)
  date.setDate(date.getDate() - (20 - index))
  return {
    date: localDateKey(date),
    studyActions: index % 4 === 0 ? 2 : index % 3 === 0 ? 1 : 0,
    completedChapters: index === 5 || index === 13 ? 1 : 0,
    acceptedChallenges: index === 8 || index === 17 ? 1 : 0,
    checkedIn: index % 3 !== 0,
  }
})

export const previewOverview: TrainingPersonalOverviewModel = {
  visibleCourseCount: previewCourses.length,
  joinedCourseCount: 1,
  completedChapterCount: 3,
  totalChapterCount: 21,
  averageProgress: 43,
  currentCheckInStreak: 2,
  activity: previewActivity,
}

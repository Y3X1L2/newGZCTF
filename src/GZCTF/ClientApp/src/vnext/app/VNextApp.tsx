import { lazy, Suspense } from 'react'
import { Navigate, Route, Routes } from 'react-router'
import { VNextRouteLoading } from './VNextRouteLoading'
import { PlatformShell } from './shell/PlatformShell'

const HomePage = lazy(() => import('../features/home/HomePage').then((module) => ({ default: module.HomePage })))
const GamesPage = lazy(() => import('../features/games/GamesPage').then((module) => ({ default: module.GamesPage })))
const GameDetailPage = lazy(() =>
  import('../features/games/GameDetailPage').then((module) => ({ default: module.GameDetailPage }))
)
const GameWorkspaceShell = lazy(() =>
  import('../features/games/workspace/GameWorkspaceShell').then((module) => ({ default: module.GameWorkspaceShell }))
)
const ScoreboardPage = lazy(() =>
  import('../features/games/scoreboard/ScoreboardPage').then((module) => ({ default: module.ScoreboardPage }))
)
const ChallengesPage = lazy(() =>
  import('../features/games/challenges/ChallengesPage').then((module) => ({ default: module.ChallengesPage }))
)
const GameTheoryPage = lazy(() =>
  import('../features/games/theory/GameTheoryPage').then((module) => ({ default: module.GameTheoryPage }))
)
const TheoryScoreboardPage = lazy(() =>
  import('../features/games/theory/TheoryScoreboardPage').then((module) => ({ default: module.TheoryScoreboardPage }))
)
const PostsPage = lazy(() => import('../features/posts/PostsPage').then((module) => ({ default: module.PostsPage })))
const PostDetailPage = lazy(() =>
  import('../features/posts/PostDetailPage').then((module) => ({ default: module.PostDetailPage }))
)
const SettingsPage = lazy(() =>
  import('../features/settings/SettingsPage').then((module) => ({ default: module.SettingsPage }))
)
const TeamsPage = lazy(() => import('../features/teams/TeamsPage').then((module) => ({ default: module.TeamsPage })))
const TrainingPage = lazy(() =>
  import('../features/training/catalog/TrainingPage').then((module) => ({ default: module.TrainingPage }))
)
const TrainingCoursePage = lazy(() =>
  import('../features/training/course/TrainingCoursePage').then((module) => ({ default: module.TrainingCoursePage }))
)
const TrainingChapterPage = lazy(() =>
  import('../features/training/chapter/TrainingChapterPage').then((module) => ({ default: module.TrainingChapterPage }))
)
const TrainingTheoryPage = lazy(() =>
  import('../features/training/theory/TrainingTheoryPage').then((module) => ({ default: module.TrainingTheoryPage }))
)
const TrainingCourseEditorPage = lazy(() =>
  import('../features/training/admin/course/TrainingCourseEditorPage').then((module) => ({
    default: module.TrainingCourseEditorPage,
  }))
)
const TrainingChapterEditorPage = lazy(() =>
  import('../features/training/admin/chapter/TrainingChapterEditorPage').then((module) => ({
    default: module.TrainingChapterEditorPage,
  }))
)
const TrainingChallengeEditorPage = lazy(() =>
  import('../features/training/admin/challenge/TrainingChallengeEditorPage').then((module) => ({
    default: module.TrainingChallengeEditorPage,
  }))
)
const TrainingTheoryPaperEditorPage = lazy(() =>
  import('../features/training/admin/theory/TrainingTheoryPaperEditorPage').then((module) => ({
    default: module.TrainingTheoryPaperEditorPage,
  }))
)
const PendingPage = lazy(() =>
  import('../features/pending/PendingPage').then((module) => ({ default: module.PendingPage }))
)

export function VNextApp() {
  return (
    <Suspense fallback={<VNextRouteLoading />}>
      <Routes>
        <Route element={<PlatformShell />}>
          <Route index element={<HomePage />} />
          <Route path="games" element={<GamesPage />} />
          <Route path="games/:gameId">
            <Route index element={<GameDetailPage />} />
            <Route element={<GameWorkspaceShell />}>
              <Route path="challenges" element={<ChallengesPage />} />
              <Route path="scoreboard" element={<ScoreboardPage />} />
              <Route path="theory" element={<GameTheoryPage />} />
              <Route path="theory-scoreboard" element={<TheoryScoreboardPage />} />
            </Route>
          </Route>
          <Route path="posts" element={<PostsPage />} />
          <Route path="posts/:postId" element={<PostDetailPage />} />
          <Route path="settings/:section?" element={<SettingsPage />} />
          <Route path="teams" element={<TeamsPage />} />
          <Route path="training" element={<TrainingPage />} />
          <Route path="training/courses/new" element={<TrainingCourseEditorPage />} />
          <Route path="training/courses/:courseId" element={<TrainingCoursePage />} />
          <Route path="training/courses/:courseId/edit" element={<TrainingCourseEditorPage />} />
          <Route path="training/courses/:courseId/chapters/new" element={<TrainingChapterEditorPage />} />
          <Route path="training/courses/:courseId/chapters/:chapterId/edit" element={<TrainingChapterEditorPage />} />
          <Route path="training/courses/:courseId/challenges/new" element={<TrainingChallengeEditorPage />} />
          <Route
            path="training/courses/:courseId/challenges/:challengeId/edit"
            element={<TrainingChallengeEditorPage />}
          />
          <Route
            path="training/courses/:courseId/chapters/:chapterId/theory-edit"
            element={<TrainingTheoryPaperEditorPage />}
          />
          <Route path="training/courses/:courseId/chapters/:chapterId" element={<TrainingChapterPage />} />
          <Route path="training/courses/:courseId/chapters/:chapterId/theory" element={<TrainingTheoryPage />} />
          <Route path="account/profile" element={<Navigate replace to="/settings/profile" />} />
          <Route path="account/settings" element={<Navigate replace to="/settings/security" />} />
          <Route path="*" element={<PendingPage />} />
        </Route>
      </Routes>
    </Suspense>
  )
}

import { lazy, Suspense } from 'react'
import { Navigate, Route, Routes } from 'react-router'
import { VNextRouteLoading } from './VNextRouteLoading'
import { PlatformShell } from './shell/PlatformShell'

const AuthShell = lazy(() => import('../features/auth/AuthShell').then((module) => ({ default: module.AuthShell })))
const LoginPage = lazy(() => import('../features/auth/LoginPage').then((module) => ({ default: module.LoginPage })))
const RegisterPage = lazy(() =>
  import('../features/auth/RegisterPage').then((module) => ({ default: module.RegisterPage }))
)
const RecoveryPage = lazy(() =>
  import('../features/auth/RecoveryPage').then((module) => ({ default: module.RecoveryPage }))
)
const ResetPage = lazy(() => import('../features/auth/ResetPage').then((module) => ({ default: module.ResetPage })))
const VerifyPage = lazy(() => import('../features/auth/VerifyPage').then((module) => ({ default: module.VerifyPage })))
const AuthPendingPage = lazy(() =>
  import('../features/auth/PendingPage').then((module) => ({ default: module.PendingPage }))
)

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
const AwdpWorkspacePage = lazy(() =>
  import('../features/games/awdp/AwdpWorkspacePage').then((module) => ({ default: module.AwdpWorkspacePage }))
)
const PostsPage = lazy(() => import('../features/posts/PostsPage').then((module) => ({ default: module.PostsPage })))
const PostDetailPage = lazy(() =>
  import('../features/posts/PostDetailPage').then((module) => ({ default: module.PostDetailPage }))
)
const SettingsPage = lazy(() =>
  import('../features/settings/SettingsPage').then((module) => ({ default: module.SettingsPage }))
)
const TeamsPage = lazy(() => import('../features/teams/TeamsPage').then((module) => ({ default: module.TeamsPage })))
const UserProfilePage = lazy(() =>
  import('../features/profile/UserProfilePage').then((module) => ({ default: module.UserProfilePage }))
)
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
const AdminShell = lazy(() =>
  import('../features/admin/shell/AdminShell').then((module) => ({ default: module.AdminShell }))
)
const AdminPendingPage = lazy(() =>
  import('../features/admin/AdminPendingPage').then((module) => ({ default: module.AdminPendingPage }))
)
const AdminImagesPage = lazy(() =>
  import('../features/admin/images/AdminImagesPage').then((module) => ({ default: module.AdminImagesPage }))
)
const AdminNodesPage = lazy(() =>
  import('../features/admin/nodes/AdminNodesPage').then((module) => ({ default: module.AdminNodesPage }))
)
const AdminNodeDetailPage = lazy(() =>
  import('../features/admin/nodes/AdminNodeDetailPage').then((module) => ({ default: module.AdminNodeDetailPage }))
)
const AdminQueuePage = lazy(() =>
  import('../features/admin/queue/AdminQueuePage').then((module) => ({ default: module.AdminQueuePage }))
)
const AdminInstancesPage = lazy(() =>
  import('../features/admin/instances/AdminInstancesPage').then((module) => ({ default: module.AdminInstancesPage }))
)
const AdminLogsPage = lazy(() =>
  import('../features/admin/logs/AdminLogsPage').then((module) => ({ default: module.AdminLogsPage }))
)
const AdminUsersPage = lazy(() =>
  import('../features/admin/users/AdminUsersPage').then((module) => ({ default: module.AdminUsersPage }))
)
const AdminTeamsPage = lazy(() =>
  import('../features/admin/teams/AdminTeamsPage').then((module) => ({ default: module.AdminTeamsPage }))
)
const AdminStudentGroupsPage = lazy(() =>
  import('../features/admin/student-groups/AdminStudentGroupsPage').then((module) => ({
    default: module.AdminStudentGroupsPage,
  }))
)
const AdminSystemPage = lazy(() =>
  import('../features/admin/system/AdminSystemPage').then((module) => ({ default: module.AdminSystemPage }))
)
const AdminDashboardPage = lazy(() =>
  import('../features/admin/dashboard/AdminDashboardPage').then((module) => ({ default: module.AdminDashboardPage }))
)
const AdminGamesPage = lazy(() =>
  import('../features/admin/games/AdminGamesPage').then((module) => ({ default: module.AdminGamesPage }))
)
const AdminTheoryBankPage = lazy(() =>
  import('../features/admin/theory/AdminTheoryBankPage').then((module) => ({ default: module.AdminTheoryBankPage }))
)
const TeamLabWorkspacePage = lazy(() =>
  import('../features/games/teamlab/TeamLabWorkspacePage').then((module) => ({ default: module.TeamLabWorkspacePage }))
)
const TeamLabLibraryPage = lazy(() =>
  import('../features/admin/teamlab/library/TeamLabLibraryPage').then((module) => ({ default: module.TeamLabLibraryPage }))
)
const TeamLabResourcesPage = lazy(() =>
  import('../features/admin/teamlab/resources/TeamLabResourcesPage').then((module) => ({ default: module.TeamLabResourcesPage }))
)
const TeamLabSceneShell = lazy(() =>
  import('../features/admin/teamlab/shared/TeamLabSceneShell').then((module) => ({ default: module.TeamLabSceneShell }))
)
const TeamLabDesignRoute = lazy(() =>
  import('../features/admin/teamlab/editor/TeamLabDesignRoute').then((module) => ({ default: module.TeamLabDesignRoute }))
)
const TeamLabRuntimesPage = lazy(() =>
  import('../features/admin/teamlab/runtimes/TeamLabRuntimesPage').then((module) => ({ default: module.TeamLabRuntimesPage }))
)
const TeamLabRuntimeDetailPage = lazy(() =>
  import('../features/admin/teamlab/runtimes/TeamLabRuntimeDetailPage').then((module) => ({ default: module.TeamLabRuntimeDetailPage }))
)
const TeamLabReleasesPage = lazy(() =>
  import('../features/admin/teamlab/releases/TeamLabReleasesPage').then((module) => ({ default: module.TeamLabReleasesPage }))
)
const GameAdminShell = lazy(() =>
  import('../features/admin/games/GameAdminShell').then((module) => ({ default: module.GameAdminShell }))
)
const AdminGameInfoPage = lazy(() =>
  import('../features/admin/games/AdminGameInfoPage').then((module) => ({ default: module.AdminGameInfoPage }))
)
const AdminGameChallengesPage = lazy(() =>
  import('../features/admin/games/challenges/AdminGameChallengesPage').then((module) => ({
    default: module.AdminGameChallengesPage,
  }))
)
const AdminChallengeEditorPage = lazy(() =>
  import('../features/admin/games/challenges/AdminChallengeEditorPage').then((module) => ({
    default: module.AdminChallengeEditorPage,
  }))
)
const AdminTheoryPaperPage = lazy(() =>
  import('../features/admin/theory/AdminTheoryPaperPage').then((module) => ({ default: module.AdminTheoryPaperPage }))
)
const AdminTheoryResultsPage = lazy(() =>
  import('../features/admin/theory/AdminTheoryResultsPage').then((module) => ({
    default: module.AdminTheoryResultsPage,
  }))
)
const AdminAwdpPage = lazy(() =>
  import('../features/admin/awdp/AdminAwdpPage').then((module) => ({ default: module.AdminAwdpPage }))
)
const AdminGamePhasesPage = lazy(() =>
  import('../features/admin/games/phases/AdminGamePhasesPage').then((module) => ({
    default: module.AdminGamePhasesPage,
  }))
)
const AdminGameDivisionsPage = lazy(() =>
  import('../features/admin/games/divisions/AdminGameDivisionsPage').then((module) => ({
    default: module.AdminGameDivisionsPage,
  }))
)
const AdminGameReviewPage = lazy(() =>
  import('../features/admin/games/review/AdminGameReviewPage').then((module) => ({
    default: module.AdminGameReviewPage,
  }))
)
const AdminGameNoticesPage = lazy(() =>
  import('../features/admin/games/notices/AdminGameNoticesPage').then((module) => ({
    default: module.AdminGameNoticesPage,
  }))
)
const AdminGameTeamLabPage = lazy(() =>
  import('../features/admin/games/teamlab/AdminGameTeamLabPage').then((module) => ({
    default: module.AdminGameTeamLabPage,
  }))
)

export function VNextApp() {
  return (
    <Suspense fallback={<VNextRouteLoading />}>
      <Routes>
        <Route element={<AuthShell />}>
          <Route path="account/login" element={<LoginPage />} />
          <Route path="account/register" element={<RegisterPage />} />
          <Route path="account/recovery" element={<RecoveryPage />} />
          <Route path="account/reset" element={<ResetPage />} />
          <Route path="account/verify" element={<VerifyPage />} />
          <Route path="account/confirm" element={<VerifyPage mode="email-change" />} />
          <Route path="account/pending" element={<AuthPendingPage />} />
        </Route>
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
              <Route path="awdp" element={<AwdpWorkspacePage />} />
              <Route path="pentest" element={<TeamLabWorkspacePage />} />
            </Route>
          </Route>
          <Route path="posts" element={<PostsPage />} />
          <Route path="posts/:postId" element={<PostDetailPage />} />
          <Route path="settings/:section?" element={<SettingsPage />} />
          <Route path="teams" element={<TeamsPage />} />
          <Route path="users/:userId" element={<UserProfilePage />} />
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
          <Route path="admin" element={<AdminShell />}>
            <Route index element={<Navigate replace to="dashboard" />} />
            <Route path="dashboard" element={<AdminDashboardPage />} />
            <Route path="games" element={<AdminGamesPage />} />
            <Route path="theory-bank" element={<AdminTheoryBankPage />} />
            <Route path="teamlab" element={<TeamLabLibraryPage />} />
        <Route path="teamlab/resources" element={<TeamLabResourcesPage />} />
            <Route path="teamlab/:topologyId" element={<TeamLabSceneShell />}>
              <Route index element={<Navigate replace to="design" />} />
              <Route path="design" element={<TeamLabDesignRoute />} />
              <Route path="releases" element={<TeamLabReleasesPage />} />
              <Route path="runtimes" element={<TeamLabRuntimesPage />} />
              <Route path="runtimes/:runtimeId" element={<TeamLabRuntimeDetailPage />} />
            </Route>
            <Route path="games/:gameId" element={<GameAdminShell />}>
              <Route index element={<Navigate replace to="info" />} />
              <Route path="info" element={<AdminGameInfoPage />} />
              <Route path="phases" element={<AdminGamePhasesPage />} />
              <Route path="divisions" element={<AdminGameDivisionsPage />} />
              <Route path="review" element={<AdminGameReviewPage />} />
              <Route path="notices" element={<AdminGameNoticesPage />} />
              <Route path="challenges" element={<AdminGameChallengesPage />} />
              <Route path="challenges/:challengeId" element={<AdminChallengeEditorPage />} />
              <Route path="theory-paper" element={<AdminTheoryPaperPage />} />
              <Route path="theory-results" element={<AdminTheoryResultsPage />} />
              <Route path="awdp-services" element={<AdminAwdpPage />} />
              <Route path="teamlab" element={<AdminGameTeamLabPage />} />
            </Route>
            <Route path="images" element={<AdminImagesPage />} />
            <Route path="nodes" element={<AdminNodesPage />} />
            <Route path="nodes/:nodeId" element={<AdminNodeDetailPage />} />
            <Route path="instances" element={<AdminInstancesPage />} />
            <Route path="queue" element={<AdminQueuePage />} />
            <Route path="logs" element={<AdminLogsPage />} />
            <Route path="users" element={<AdminUsersPage />} />
            <Route path="teams" element={<AdminTeamsPage />} />
            <Route path="student-groups" element={<AdminStudentGroupsPage />} />
            <Route path="system" element={<AdminSystemPage />} />
            <Route path="*" element={<AdminPendingPage />} />
          </Route>
          <Route path="*" element={<PendingPage />} />
        </Route>
      </Routes>
    </Suspense>
  )
}

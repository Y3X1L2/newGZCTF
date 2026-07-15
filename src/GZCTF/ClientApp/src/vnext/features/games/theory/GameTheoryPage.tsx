import { useCallback, useEffect } from 'react'
import api, { TheoryAnswerSheetEditModel, TheoryPlayerPaperModel } from '@Api'
import { DataState } from '../../../shared/Primitives'
import { TheoryExamWorkbench } from '../../theory/workbench/TheoryExamWorkbench'
import { useGameWorkspace } from '../workspace/GameWorkspaceShell'
import styles from './GameTheoryPage.module.css'

export function GameTheoryPage() {
  const { gameId, game, revision } = useGameWorkspace()
  const {
    data: paper,
    error,
    mutate,
  } = api.theoryPlayer.useTheoryPlayerGetPaper(gameId, { revalidateOnFocus: false, shouldRetryOnError: false }, true)

  useEffect(() => {
    if (revision > 0) void mutate()
  }, [mutate, revision])

  const saveDraft = useCallback(
    async (data: TheoryAnswerSheetEditModel) => {
      const response = await api.theoryPlayer.theoryPlayerSaveDraft(gameId, data)
      return response.data
    },
    [gameId]
  )

  const submit = useCallback(
    async (data: TheoryAnswerSheetEditModel) => {
      const response = await api.theoryPlayer.theoryPlayerSubmit(gameId, data)
      return response.data
    },
    [gameId]
  )

  const onSubmitted = useCallback(
    (submittedPaper: TheoryPlayerPaperModel) => {
      void mutate(submittedPaper, { revalidate: false })
      void api.theoryPlayer.mutateTheoryPlayerScoreboard(gameId)
    },
    [gameId, mutate]
  )

  return (
    <div className={styles.page}>
      {!paper && !error ? (
        <DataState description="正在读取试卷、答题卡和服务端草稿。" loading title="理论考试加载中" />
      ) : error || !paper ? (
        <DataState description="试卷尚未发布、比赛当前不可作答，或当前账户没有访问权限。" title="理论考试暂不可用" />
      ) : (
        <TheoryExamWorkbench
          deadline={game.end}
          initialPaper={paper}
          onSubmitted={onSubmitted}
          saveDraft={saveDraft}
          submit={submit}
        />
      )}
    </div>
  )
}

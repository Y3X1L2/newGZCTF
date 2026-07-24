import { useCallback, useEffect } from 'react'
import { TheoryAnswerSheetEditModel, TheoryPlayerPaperModel } from '@Api'
import { DataState } from '../../../shared/Primitives'
import { TheoryExamWorkbench } from '../../theory/workbench/TheoryExamWorkbench'
import { useGameWorkspace } from '../workspace/GameWorkspaceShell'
import styles from './GameTheoryPage.module.css'
import { theoryPlayerApi, useGameTheoryPaper } from './theoryPlayerApi'

export function GameTheoryPage() {
  const { gameId, game, revision } = useGameWorkspace()
  const { data: paper, error, mutate } = useGameTheoryPaper(gameId)

  useEffect(() => {
    if (revision > 0) void mutate()
  }, [mutate, revision])

  const saveDraft = useCallback(
    async (data: TheoryAnswerSheetEditModel) => {
      return theoryPlayerApi.saveDraft(gameId, data)
    },
    [gameId]
  )

  const submit = useCallback(
    async (data: TheoryAnswerSheetEditModel) => {
      return theoryPlayerApi.submit(gameId, data)
    },
    [gameId]
  )

  const onSubmitted = useCallback(
    (submittedPaper: TheoryPlayerPaperModel) => {
      void mutate(submittedPaper, { revalidate: false })
      void theoryPlayerApi.refreshScoreboard(gameId)
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

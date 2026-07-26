import { useState } from 'react'
import { InlineFeedback, VNextConfirmDialog } from '../../../shared/Interaction'
import { errorMessage } from '../../../shared/errors'
import { teamLabPlayerApi } from './api'

export function PlayerResetDialog({ gameId, open, onClose, onReset }: { gameId: number; open: boolean; onClose: () => void; onReset: () => void }) {
  const [error, setError] = useState<unknown>(null)
  const reset = async () => {
    setError(null)
    try {
      await teamLabPlayerApi.resetWorkspace(gameId)
      onReset()
      return true
    } catch (reason) {
      setError(reason)
      return false
    }
  }
  return (
    <VNextConfirmDialog
        confirmLabel="确认重置"
        description="当前环境将进入清理和重新部署流程。"
        message={
          <>
            重置会清除队伍在当前环境中的临时运行数据，任务得分记录不受影响。
            {error ? <InlineFeedback tone="danger">{errorMessage(error, '环境重置提交失败。')}</InlineFeedback> : null}
          </>
        }
        onClose={onClose}
        onConfirm={reset}
        open={open}
        title="重置队伍环境"
    />
  )
}

import { useParams } from 'react-router'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { NetlabWorkspace } from './NetlabWorkspace'

export function TeamLabConsolePage() {
  const { gameId, environmentId } = useParams()
  useVNextPageTitle('资产控制台')
  return (
    <NetlabWorkspace
      consoleMode
      title="资产控制台"
      target={gameId ? { gameId: Number(gameId) } : { section: 'workbench', environmentId }}
    />
  )
}

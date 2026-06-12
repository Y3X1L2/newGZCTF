import { FC, useEffect } from 'react'
import { useNavigate, useParams } from 'react-router'
import ScreenDisplayPage from '@Components/screen/ScreenDisplayPage'

const ScreenModePage: FC = () => {
  const navigate = useNavigate()
  const { id, mode } = useParams()
  const numId = parseInt(id ?? '-1', 10)

  useEffect(() => {
    if (mode === 'demo') return
    navigate(`/admin/games/${numId}/screen`, { replace: true })
  }, [mode, navigate, numId])

  if (mode === 'demo') {
    return <ScreenDisplayPage gameId={numId} demoMode />
  }

  return null
}

export default ScreenModePage

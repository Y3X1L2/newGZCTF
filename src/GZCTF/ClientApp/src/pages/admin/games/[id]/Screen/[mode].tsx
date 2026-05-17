import { FC, useEffect } from 'react'
import { useNavigate, useParams } from 'react-router'
import ScreenDisplayPage from '@Components/screen/ScreenDisplayPage'

const ScreenModePage: FC = () => {
  const navigate = useNavigate()
  const { id } = useParams()
  const numId = parseInt(id ?? '-1', 10)

  // Redirect to main screen index
  useEffect(() => {
    navigate(`/admin/games/${numId}/screen`, { replace: true })
  }, [navigate, numId])

  return null
}

export default ScreenModePage

import { FC } from 'react'
import { useParams } from 'react-router'
import ScreenDisplayPage from '@Components/screen/ScreenDisplayPage'

const ScreenIndex: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1', 10)

  return <ScreenDisplayPage gameId={numId} />
}

export default ScreenIndex

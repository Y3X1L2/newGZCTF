import { FC } from 'react'

interface WithWiderScreenProps extends React.PropsWithChildren {
  minWidth?: number
}

export const WithWiderScreen: FC<WithWiderScreenProps> = ({ children }) => {
  return <>{children}</>
}

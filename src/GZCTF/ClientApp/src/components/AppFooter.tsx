import { FC } from 'react'
import { BrandMark } from '@Components/yinyu/BrandMark'
import { getPlatformBrand, getSloganText } from '@Utils/Brand'
import { useConfig } from '@Hooks/useConfig'
import classes from '@Styles/AppFooter.module.css'

export const AppFooter: FC = () => {
  const { config } = useConfig()

  return (
    <footer className={`yy-footer ${classes.wrapper}`}>
      <div className={classes.brand}>
        <BrandMark src={config.logoUrl} />
      </div>
      <div className={classes.copy}>
        <strong>{getPlatformBrand(config?.title)}</strong>
        <span>{getSloganText(config?.slogan)}</span>
      </div>
    </footer>
  )
}

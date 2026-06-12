import { FC } from 'react'
import { BrandMark } from '@Components/yinyu/BrandMark'
import { PLATFORM_BRAND, PLATFORM_SLOGAN } from '@Utils/Brand'
import classes from '@Styles/AppFooter.module.css'

export const AppFooter: FC = () => {
  return (
    <footer className={`yy-footer ${classes.wrapper}`}>
      <div className={classes.brand}>
        <BrandMark />
      </div>
      <div className={classes.copy}>
        <strong>{PLATFORM_BRAND}</strong>
        <span>{PLATFORM_SLOGAN}</span>
      </div>
    </footer>
  )
}

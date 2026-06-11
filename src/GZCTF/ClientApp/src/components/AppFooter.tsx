import { FC } from 'react'
import { FooterRender } from '@Components/FooterRender'
import { BrandMark } from '@Components/yinyu/BrandMark'
import { PLATFORM_BRAND, PLATFORM_SLOGAN } from '@Utils/Brand'
import { useConfig } from '@Hooks/useConfig'
import classes from '@Styles/AppFooter.module.css'

export const AppFooter: FC = () => {
  const { config } = useConfig()

  return (
    <footer className={`yy-footer ${classes.wrapper}`}>
      <div className={classes.brand}>
        <BrandMark />
      </div>
      <div className={classes.copy}>
        <strong>{PLATFORM_BRAND}</strong>
        {config.footerInfo ? <FooterRender source={config.footerInfo} /> : <span>{PLATFORM_SLOGAN}</span>}
      </div>
    </footer>
  )
}

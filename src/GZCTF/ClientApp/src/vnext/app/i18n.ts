import { createInstance } from 'i18next'
import enAdmin from '../../locales/en-US/admin.json'
import zhAdmin from '../../locales/zh-CN/admin.json'

// vNext currently serves the Chinese shell. Keep its runtime independent from
// the retired application's language and Mantine providers.
export const vnextI18n = createInstance()
void vnextI18n.init({
  lng: 'zh-CN',
  fallbackLng: 'en-US',
  defaultNS: 'admin',
  ns: ['admin'],
  resources: { 'zh-CN': { admin: zhAdmin }, 'en-US': { admin: enAdmin } },
  initAsync: false,
  interpolation: { escapeValue: false },
  react: { useSuspense: false },
})

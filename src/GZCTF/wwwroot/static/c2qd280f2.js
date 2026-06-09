/* YINYU CTF Platform @unknown
 *
 * Commit    : Unofficial build version
 * Build     : 2026-06-09T06:10:58.565Z
 */
import{A as e,E as t}from"./i794nfmz.js";function n({color:e,theme:n,autoContrast:r}){return(typeof r==`boolean`?r:n.autoContrast)&&t({color:e||n.primaryColor,theme:n}).isLight?`var(--mantine-color-black)`:`var(--mantine-color-white)`}function r(t,r){return n({color:t.colors[t.primaryColor][e(t,r)],theme:t,autoContrast:null})}function i(e,t){return typeof e==`boolean`?e:t.autoContrast}export{n,r,i as t};
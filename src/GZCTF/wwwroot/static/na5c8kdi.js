/* The GZ::CTF Project @unknown
 * 
 * License   : GNU Affero General Public License v3.0 (Core)
 * License   : LicenseRef-GZCTF-Restricted (Restricted components)
 * Commit    : Unofficial build version
 * Build     : 2026-05-20T05:57:19.720Z
 * Copyright (C) 2022-2026 GZTimeWalker. All Rights Reserved.
 */
import{D as e,j as t}from"./ngkkuwm9.js";function n({color:t,theme:n,autoContrast:r}){return(typeof r==`boolean`?r:n.autoContrast)&&e({color:t||n.primaryColor,theme:n}).isLight?`var(--mantine-color-black)`:`var(--mantine-color-white)`}function r(e,r){return n({color:e.colors[e.primaryColor][t(e,r)],theme:e,autoContrast:null})}function i(e,t){return typeof e==`boolean`?e:t.autoContrast}export{n,r,i as t};
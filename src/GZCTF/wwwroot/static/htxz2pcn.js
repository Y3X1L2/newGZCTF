/* The GZ::CTF Project @unknown
 * 
 * License   : GNU Affero General Public License v3.0 (Core)
 * License   : LicenseRef-GZCTF-Restricted (Restricted components)
 * Commit    : Unofficial build version
 * Build     : 2026-05-20T05:57:19.720Z
 * Copyright (C) 2022-2026 GZTimeWalker. All Rights Reserved.
 */
import{a as e,t}from"./izagbnaw.js";var n=e(t(),1);function r(e){return t=>{if(!t)e(t);else if(typeof t==`function`)e(t);else if(typeof t==`object`&&`nativeEvent`in t){let{currentTarget:n}=t;n.type===`checkbox`?e(n.checked):e(n.value)}else e(t)}}function i(e){let[t,i]=(0,n.useState)(e);return[t,r(i)]}export{i as t};
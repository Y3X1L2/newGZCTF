/* The GZ::CTF Project @unknown
 * 
 * License   : GNU Affero General Public License v3.0 (Core)
 * License   : LicenseRef-GZCTF-Restricted (Restricted components)
 * Commit    : Unofficial build version
 * Build     : 2026-05-20T05:57:19.720Z
 * Copyright (C) 2022-2026 GZTimeWalker. All Rights Reserved.
 */
import{a as e,t}from"./izagbnaw.js";import{t as n,x as r}from"./hmbv74xw.js";var i=e(t(),1),a=e=>{let{data:t,error:a,mutate:o}=r.edit.useEditGetGameChallenges(e,n),[s,c]=(0,i.useState)(null);return(0,i.useEffect)(()=>{t&&c(t.toSorted((e,t)=>(e.category??``)>(t.category??``)?-1:1))},[t]),{challenges:s,error:a,mutate:o}};export{a as t};
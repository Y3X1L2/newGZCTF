/* The GZ::CTF Project @unknown
 * 
 * License   : GNU Affero General Public License v3.0 (Core)
 * License   : LicenseRef-GZCTF-Restricted (Restricted components)
 * Commit    : Unofficial build version
 * Build     : 2026-05-20T05:57:19.720Z
 * Copyright (C) 2022-2026 GZTimeWalker. All Rights Reserved.
 */
import{g as e,m as t}from"./ngkkuwm9.js";import{C as n}from"./lo2h6db2.js";import{A as r}from"./hmbv74xw.js";import{t as i}from"./fxwlajkf2.js";import{t as a}from"./ialu60x5.js";var o=e(),s=e=>{let{disabled:s,participation:c,setParticipation:l,size:u,...d}=e,f=n(),p=f.get(c.status),m=t(),{t:h}=r();return(0,o.jsx)(i,{wrap:`nowrap`,justify:`center`,mx:`xs`,miw:`calc(${m.spacing.xl} * 2)`,...d,children:p.transformTo.map(e=>{let t=f.get(e);return(0,o.jsx)(a,{size:u,iconPath:t.iconPath,color:t.color,message:h(`admin.content.games.review.participation.update`,{status:t.title}),disabled:s,onClick:()=>l(c.id,{status:e,divisionId:c.divisionId})},`${c.id}@${e}`)})})},c={root:`ba`,item:`ca`,label:`da`,control:`ea`};export{s as n,c as t};
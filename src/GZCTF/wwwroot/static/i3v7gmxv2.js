/* The GZ::CTF Project @unknown
 * 
 * License   : GNU Affero General Public License v3.0 (Core)
 * License   : LicenseRef-GZCTF-Restricted (Restricted components)
 * Commit    : Unofficial build version
 * Build     : 2026-05-20T05:57:19.720Z
 * Copyright (C) 2022-2026 GZTimeWalker. All Rights Reserved.
 */
import{a as e,t}from"./izagbnaw.js";import{P as n,g as r,t as i}from"./ngkkuwm9.js";import{t as a}from"./gi0mgpwi2.js";var o={container:`Wc`,text:`Xc`,textWrapper:`Yc`,clone:`Zc`,scroll:`_e`},s=e(t(),1),c=r(),l=({text:e,onClick:t,size:r,speedCharPerSec:l=3.2,...u})=>{let d=(0,s.useRef)(null),f=(0,s.useRef)(null),[p,m]=(0,s.useState)(!1),[h,g]=(0,s.useState)(!1),[_,v]=(0,s.useState)(4),y=(0,s.useCallback)(()=>{if(h)return;let e=d.current,t=f.current;if(!e||!t)return;let n=parseFloat(getComputedStyle(t).fontSize||`14`)||14,r=t.scrollWidth;if(r-e.clientWidth>0){let e=r/(l*n);v(Math.max(3,e)),m(!0)}g(!0)},[h,l]);return(0,c.jsx)(i,{ref:d,className:o.container,onClick:t,onMouseEnter:y,"data-scroll":p||void 0,__vars:{"--scroll-time":`${_}s`},...u,children:(0,c.jsxs)(`div`,{className:o.textWrapper,children:[(0,c.jsx)(a,{ref:f,className:o.text,title:e,fz:r,children:e}),p&&(0,c.jsx)(a,{className:n(o.text,o.clone),fz:r,"aria-hidden":!0,children:e})]})})};export{l as t};
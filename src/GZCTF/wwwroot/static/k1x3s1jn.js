/* The GZ::CTF Project @unknown
 * 
 * License   : GNU Affero General Public License v3.0 (Core)
 * License   : LicenseRef-GZCTF-Restricted (Restricted components)
 * Commit    : Unofficial build version
 * Build     : 2026-05-20T05:57:19.720Z
 * Copyright (C) 2022-2026 GZTimeWalker. All Rights Reserved.
 */
import{a as e,t}from"./izagbnaw.js";import{g as n,m as r,t as i}from"./ngkkuwm9.js";import{t as a}from"./pcm18lg0.js";import{w as o}from"./lo2h6db2.js";import{t as s}from"./fxwlajkf2.js";import{t as c}from"./m5s4g9tx2.js";var l={default:`Fc`,inner:`Gc`,label:`Hc`,icon:`Ic`,hidable:`Jc`,panes:`Kc`},u=e(t(),1),d=n(),f=e=>{let{color:t,label:n,active:r,icon:a,tabKey:o,disabled:s,...c}=e;return(0,d.jsx)(i,{...c,component:`button`,type:`button`,role:`tab`,disabled:s,__vars:{"--tab-active-color":t},"data-active":r||void 0,className:l.default,children:(0,d.jsxs)(`div`,{className:l.inner,children:[a&&(0,d.jsx)(`div`,{className:l.icon,children:a}),n&&(0,d.jsx)(`div`,{className:l.label,children:n})]})},o)},p=e=>{let{active:t,onTabChange:n,tabs:i,withIcon:p,aside:m,disabled:h,...g}=e,[_,v]=(0,u.useState)(t??0),y=r(),{colorScheme:b}=o(),x=e=>e?y.colors[y.primaryColor][b===`dark`?4:6]:void 0,S=a(_,0,i.length-1);(0,u.useEffect)(()=>{v(t??0)},[t]);let C=i.map((e,t)=>(0,d.jsx)(f,{...e,disabled:h,color:x(e.color),active:S===t,onClick:()=>{v(t),n&&n(t,e.tabKey)}},e.tabKey));return(0,d.jsxs)(s,{gap:0,justify:`space-between`,w:`100%`,wrap:`nowrap`,children:[m,p&&(0,d.jsx)(c,{className:l.hidable}),(0,d.jsx)(s,{className:l.panes,...g,children:C})]})};export{p as t};
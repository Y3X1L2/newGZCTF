/* The GZ::CTF Project @unknown
 * 
 * License   : GNU Affero General Public License v3.0 (Core)
 * License   : LicenseRef-GZCTF-Restricted (Restricted components)
 * Commit    : Unofficial build version
 * Build     : 2026-05-20T05:57:19.720Z
 * Copyright (C) 2022-2026 GZTimeWalker. All Rights Reserved.
 */
import{a as e,t}from"./izagbnaw.js";import{g as n}from"./ngkkuwm9.js";import{s as r}from"./khr0xm81.js";import{K as i,v as a}from"./hmbv74xw.js";import{m as o,p as s}from"./index.ot524qw3.js";import{i as c}from"./v6b997pm.js";var l=e(t(),1),u=n(),d=new Map([[a.Admin,3],[a.Monitor,1],[a.User,0],[a.Banned,-1]]),f=(e,t)=>d.get(t??a.User)>=d.get(e),p=({requiredRole:e,children:t})=>{let{role:n,error:a}=c(),f=o(),p=s(),m=d.get(e);return(0,l.useEffect)(()=>{a&&a.status===401&&f(`/account/login?from=${p.pathname}`,{replace:!0}),n&&d.get(n)<m&&f(`/404`)},[n,a,m,f]),n&&d.get(n)<m?(0,u.jsx)(i,{h:`calc(100vh - 32px)`,children:(0,u.jsx)(r,{})}):(0,u.jsx)(u.Fragment,{children:t})};export{p as n,f as t};
/* YINYU CTF Platform @unknown
 *
 * Commit    : Unofficial build version
 * Build     : 2026-07-07T12:59:55.597Z
 */
import{$ as e,Lt as t,S as n,et as r,un as i,xn as a}from"./Api.20260707T12595.cj2ps75j.js";import{yr as o}from"./Icon.20260707T12595.np5a7obx.js";import{z as s}from"./index.20260707T12595.zkxqluhk.js";import{i as c}from"./useUser.20260707T12595.n6nrn1xt.js";var l=a(i(),1),u=t(),d=new Map([[n.SuperAdmin,4],[n.Admin,3],[n.Teacher,2],[n.Monitor,2],[n.Student,1],[n.User,1],[n.Banned,-1]]),f=(e,t)=>d.get(t??n.User)>=d.get(e),p=({requiredRole:t,children:n})=>{let{role:i,error:a}=c(),f=r(),p=e(),m=d.get(t);return(0,l.useEffect)(()=>{a&&a.status===401&&f(`/account/login?from=${p.pathname}`,{replace:!0}),i&&d.get(i)<m&&f(`/`,{replace:!0})},[i,a,m,f,p.pathname]),i&&d.get(i)<m?(0,u.jsx)(s,{h:`calc(100vh - 32px)`,children:(0,u.jsx)(o,{})}):(0,u.jsx)(u.Fragment,{children:n})};export{p as n,f as t};
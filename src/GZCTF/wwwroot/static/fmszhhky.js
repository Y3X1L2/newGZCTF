/* YINYU CTF Platform @unknown
 *
 * Commit    : Unofficial build version
 * Build     : 2026-06-09T06:10:58.565Z
 */
import{a as e,t}from"./izagbnaw.js";import{h as n}from"./i794nfmz.js";import{s as r}from"./nryciy9y.js";import{et as i,w as a}from"./d08360rz.js";import{m as o,p as s}from"./index.d3jdxuhz.js";import{i as c}from"./nn66jybg.js";var l=e(t(),1),u=n(),d=new Map([[a.Admin,3],[a.Monitor,1],[a.User,0],[a.Banned,-1]]),f=(e,t)=>d.get(t??a.User)>=d.get(e),p=({requiredRole:e,children:t})=>{let{role:n,error:a}=c(),f=o(),p=s(),m=d.get(e);return(0,l.useEffect)(()=>{a&&a.status===401&&f(`/account/login?from=${p.pathname}`,{replace:!0}),n&&d.get(n)<m&&f(`/404`)},[n,a,m,f]),n&&d.get(n)<m?(0,u.jsx)(i,{h:`calc(100vh - 32px)`,children:(0,u.jsx)(r,{})}):(0,u.jsx)(u.Fragment,{children:t})};export{p as n,f as t};
/* YINYU CTF Platform @unknown
 *
 * Commit    : Unofficial build version
 * Build     : 2026-06-09T06:10:58.565Z
 */
import{h as e,p as t}from"./i794nfmz.js";import{t as n}from"./j5ay0tzz.js";import{C as r}from"./hr5sxgyp.js";import{V as i}from"./d08360rz.js";import{t as a}from"./inaxoua4.js";var o=e(),s=e=>{let{disabled:s,participation:c,setParticipation:l,size:u,...d}=e,f=r(),p=f.get(c.status),m=t(),{t:h}=i();return(0,o.jsx)(n,{wrap:`nowrap`,justify:`center`,mx:`xs`,miw:`calc(${m.spacing.xl} * 2)`,...d,children:p.transformTo.map(e=>{let t=f.get(e);return(0,o.jsx)(a,{size:u,iconPath:t.iconPath,color:t.color,message:h(`admin.content.games.review.participation.update`,{status:t.title}),disabled:s,onClick:()=>l(c.id,{status:e,divisionId:c.divisionId})},`${c.id}@${e}`)})})},c={root:`H`,item:`I`,label:`J`,control:`K`};export{s as n,c as t};
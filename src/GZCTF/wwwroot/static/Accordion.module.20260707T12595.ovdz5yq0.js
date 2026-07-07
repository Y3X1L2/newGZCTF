/* YINYU CTF Platform @unknown
 *
 * Commit    : Unofficial build version
 * Build     : 2026-07-07T12:59:55.597Z
 */
import{Ft as e,Lt as t,St as n,at as r}from"./Api.20260707T12595.cj2ps75j.js";import{w as i}from"./Shared.20260707T12595.bwf9jd03.js";import{t as a}from"./ActionIconWithConfirm.20260707T12595.jgv4pls9.js";var o=t(),s=t=>{let{disabled:s,participation:c,setParticipation:l,size:u,...d}=t,f=i(),p=f.get(c.status),m=e(),{t:h}=r();return(0,o.jsx)(n,{wrap:`nowrap`,justify:`center`,mx:`xs`,miw:`calc(${m.spacing.xl} * 2)`,...d,children:p.transformTo.map(e=>{let t=f.get(e);return(0,o.jsx)(a,{size:u,iconPath:t.iconPath,color:t.color,message:h(`admin.content.games.review.participation.update`,{status:t.title}),disabled:s,onClick:()=>l(c.id,{status:e,divisionId:c.divisionId})},`${c.id}@${e}`)})})},c={root:`y_`,item:`z_`,label:`A_`,control:`B_`};export{s as n,c as t};
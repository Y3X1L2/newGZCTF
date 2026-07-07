/* YINYU CTF Platform @unknown
 *
 * Commit    : Unofficial build version
 * Build     : 2026-07-07T12:59:55.597Z
 */
import{T as e,un as t,xn as n}from"./Api.20260707T12595.cj2ps75j.js";import{g as r}from"./index.20260707T12595.zkxqluhk.js";var i=n(t(),1),a=(t,n)=>{let{data:i,error:a,mutate:o}=e.edit.useEditGetGameChallenge(t,n,r);return{challenge:i,error:a,mutate:o}},o=t=>{let{data:n,error:a,mutate:o}=e.edit.useEditGetGameChallenges(t,r),[s,c]=(0,i.useState)(null);return(0,i.useEffect)(()=>{n&&c(n.toSorted((e,t)=>(e.category??``)>(t.category??``)?-1:1))},[n]),{challenges:s,error:a,mutate:o}};export{o as n,a as t};
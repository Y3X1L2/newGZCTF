/* YINYU CTF Platform @unknown
 *
 * Commit    : Unofficial build version
 * Build     : 2026-06-09T06:10:58.565Z
 */
import{a as e,t}from"./izagbnaw.js";import{D as n,t as r}from"./d08360rz.js";var i=e(t(),1),a=e=>{let{data:t,error:a,mutate:o}=n.edit.useEditGetGameChallenges(e,r),[s,c]=(0,i.useState)(null);return(0,i.useEffect)(()=>{t&&c(t.toSorted((e,t)=>(e.category??``)>(t.category??``)?-1:1))},[t]),{challenges:s,error:a,mutate:o}};export{a as t};
/* The GZ::CTF Project @unknown
 * 
 * License   : GNU Affero General Public License v3.0 (Core)
 * License   : LicenseRef-GZCTF-Restricted (Restricted components)
 * Commit    : Unofficial build version
 * Build     : 2026-05-20T05:57:19.720Z
 * Copyright (C) 2022-2026 GZTimeWalker. All Rights Reserved.
 */
import{n as e}from"./f9u6cnui.js";var t=e=>fetch(e).then(e=>e.json());function n(){let{data:n,error:r,isLoading:i,mutate:a}=e(`/api/v1/nodes`,t,{refreshInterval:5e3});return{nodes:n,error:r,isLoading:i,mutate:a}}function r(){return{deploy:async e=>(await fetch(`/api/v1/docker/deploy`,{method:`POST`,headers:{"Content-Type":`application/json`},body:JSON.stringify({composeFile:e})})).json(),cleanup:async e=>(await fetch(`/api/v1/docker/cleanup`,{method:`POST`,headers:{"Content-Type":`application/json`},body:JSON.stringify({composeFile:e})})).json()}}export{n,r as t};
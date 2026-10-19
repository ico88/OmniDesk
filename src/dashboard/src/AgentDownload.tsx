import {useEffect,useState} from 'react';
import {api} from './api';
import {Icon} from './Icon';
type PackageInfo={available:boolean;url:string|null;release:{version:string;commit:string;sha256:string;size:number;remoteEngineIncluded:boolean}|null};
export function AgentDownload(){
  const [info,setInfo]=useState<PackageInfo|null>(null);const [error,setError]=useState('');
  useEffect(()=>{void api<PackageInfo>('/admin/downloads/windows').then(setInfo).catch(e=>setError(e instanceof Error?e.message:'Download non disponibile'));},[]);
  return <section className="panel download-card"><span className="eyebrow">INSTALLAZIONE</span><h2><Icon name="download"/>Agent Windows</h2>{error&&<p role="alert">{error}</p>}
    {!info&&!error&&<p>Verifica pacchetto disponibile…</p>}
    {info&&!info.available&&<p>Il pacchetto Windows verificato non è ancora pubblicato.</p>}
    {info?.available&&info.release&&info.url&&<><p>Versione {info.release.version} · Windows x64 · {(info.release.size/1048576).toFixed(1)} MB</p>
      <a className="button" href={info.url} download><Icon name="download"/>Scarica Agent Windows</a>
      <p>Il kit contiene MSI unico, Tray, CA pubblica e istruzioni LAN. Genera il token nel riquadro accanto e incollalo nell’installer. Installazione Windows con privilegi amministrativi.</p>
      <p>Pacchetto di collaudo non firmato. {info.release.remoteEngineIncluded?'Motore remoto incluso: richiede approvazione sul dispositivo e collaudo.':'Motore desktop remoto non incluso in questa versione.'}</p>
      <details><summary>Checksum SHA256 del kit</summary><code style={{overflowWrap:'anywhere'}}>{info.release.sha256}</code></details>
    </>}
  </section>;
}
